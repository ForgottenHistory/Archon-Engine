using System;
using System.IO;
using UnityEngine;
using Core.Systems;
using Core.Data;

namespace Core.SaveLoad
{
    /// <summary>
    /// ENGINE LAYER - Orchestrates save/load operations across all systems
    ///
    /// Responsibilities:
    /// - Coordinate OnSave/OnLoad calls to all systems (via SystemRegistry)
    /// - Serialize/deserialize SaveGameData to disk (binary format)
    /// - Manage save file paths and naming
    /// - Atomic writes (temp file → rename to prevent corruption)
    /// - Version compatibility checks
    ///
    /// Architecture:
    /// - Pure orchestrator, doesn't own game state
    /// - Systems serialize their own data via OnSave/OnLoad
    /// - Dependency order handled by SystemRegistry
    /// - GAME layer hooks OnPostLoadFinalize for game-specific finalization
    ///
    /// Usage:
    /// saveManager.SaveGame("my_save");
    /// saveManager.LoadGame("my_save");
    /// saveManager.QuickSave();
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Save file extension (without dot)")]
        public string saveFileExtension = "sav";

        [Tooltip("Enable debug logging for save/load operations")]
        public bool logSaveLoadOperations = true;

        // Save file directory (platform-specific)
        private string saveDirectory;

        // GameState reference (accessed via singleton)
        private GameState gameState => GameState.Instance;

        // GAME layer finalization callback (architecture compliance: ENGINE doesn't call GAME code)
        // GAME layer sets this to rebuild caches, etc.
        public System.Action OnPostLoadFinalize;

        // GAME layer PlayerState serialization (ENGINE stores as opaque byte[], GAME interprets)
        public System.Func<byte[]> OnSerializePlayerState;
        public System.Action<byte[]> OnDeserializePlayerState;

        void Awake()
        {
            // Determine platform-specific save directory
            saveDirectory = GetSaveDirectory();

            // Create directory if it doesn't exist
            if (!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
                ArchonLogger.Log($"SaveManager: Created save directory at {saveDirectory}");
            }
            else if (logSaveLoadOperations)
            {
                ArchonLogger.Log($"SaveManager: Using save directory {saveDirectory}");
            }
        }

        void Update()
        {
            // F6 - Quicksave
            if (Input.GetKeyDown(KeyCode.F6))
            {
                ArchonLogger.Log("SaveManager: F6 pressed - Quicksaving...");
                QuickSave();
            }

            // F7 - Quickload
            if (Input.GetKeyDown(KeyCode.F7))
            {
                ArchonLogger.Log("SaveManager: F7 pressed - Quickloading...");
                QuickLoad();
            }
        }

        /// <summary>
        /// Get platform-specific save directory path
        /// </summary>
        private string GetSaveDirectory()
        {
            // Use Application.persistentDataPath (handles all platforms correctly)
            return Path.Combine(Application.persistentDataPath, "Saves");
        }

        /// <summary>
        /// Save game to file
        /// </summary>
        public bool SaveGame(string saveName)
        {
            if (string.IsNullOrEmpty(saveName))
            {
                ArchonLogger.LogError("SaveManager: Save name cannot be null or empty");
                return false;
            }

            if (gameState == null)
            {
                ArchonLogger.LogError("SaveManager: GameState not assigned");
                return false;
            }

            try
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log($"SaveManager: Starting save '{saveName}'...");
                }

                // Create save data container
                SaveGameData saveData = CreateSaveData(saveName);

                // Call OnSave for all systems (dependency order)
                CallOnSaveForAllSystems(saveData);

                // Serialize to disk (atomic write)
                string filePath = GetSaveFilePath(saveName);
                SerializeToDisk(saveData, filePath);

                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log($"SaveManager: ✓ Save completed - {filePath}");
                }

                return true;
            }
            catch (Exception ex)
            {
                ArchonLogger.LogError($"SaveManager: Save failed - {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Post-load finalization - sync ENGINE double buffers, delegate rest to callbacks
        /// GAME/MAP layer finalization handled via OnPostLoadFinalize callback
        /// </summary>
        private void FinalizeAfterLoad()
        {
            if (logSaveLoadOperations)
            {
                ArchonLogger.Log("SaveManager: Finalizing after load...");
            }

            // Step 1: Sync double buffers (ENGINE layer only - safe)
            SyncDoubleBuffers();

            // Step 2: Let GAME layer handle MAP refresh + GAME finalization via callback
            // (GAME can import MAP, so no architecture violation)
            OnPostLoadFinalize?.Invoke();

            if (logSaveLoadOperations)
            {
                ArchonLogger.Log("SaveManager: ✓ Post-load finalization complete");
            }
        }

        /// <summary>
        /// Sync double buffers after load to prevent UI reading stale data
        /// </summary>
        private void SyncDoubleBuffers()
        {
            if (gameState.Provinces != null)
            {
                gameState.Provinces.SyncBuffersAfterLoad();
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log("SaveManager: ✓ Province buffers synced");
                }
            }
        }

        /// <summary>
        /// Load game from file
        /// </summary>
        public bool LoadGame(string saveName)
        {
            if (string.IsNullOrEmpty(saveName))
            {
                ArchonLogger.LogError("SaveManager: Save name cannot be null or empty");
                return false;
            }

            if (gameState == null)
            {
                ArchonLogger.LogError("SaveManager: GameState not assigned");
                return false;
            }

            try
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log($"SaveManager: Starting load '{saveName}'...");
                }

                // Check if file exists
                string filePath = GetSaveFilePath(saveName);
                if (!File.Exists(filePath))
                {
                    ArchonLogger.LogError($"SaveManager: Save file not found - {filePath}");
                    return false;
                }

                // Deserialize from disk
                SaveGameData saveData = DeserializeFromDisk(filePath);

                // Verify version compatibility
                if (!VerifyVersionCompatibility(saveData))
                {
                    return false;
                }

                // Call OnLoad for all systems (dependency order)
                CallOnLoadForAllSystems(saveData);

                // CRITICAL: Finalize after load - rebuild caches and refresh GPU textures
                FinalizeAfterLoad();

                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log($"SaveManager: ✓ Load completed - {filePath}");
                }

                return true;
            }
            catch (Exception ex)
            {
                ArchonLogger.LogError($"SaveManager: Load failed - {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Quick save (F5 hotkey) - saves to "quicksave.sav"
        /// </summary>
        public bool QuickSave()
        {
            return SaveGame("quicksave");
        }

        /// <summary>
        /// Quick load (F9 hotkey) - loads from "quicksave.sav"
        /// </summary>
        public bool QuickLoad()
        {
            return LoadGame("quicksave");
        }

        /// <summary>
        /// Get list of all save files
        /// </summary>
        public string[] GetSaveFileNames()
        {
            if (!Directory.Exists(saveDirectory))
                return new string[0];

            string searchPattern = $"*.{saveFileExtension}";
            string[] files = Directory.GetFiles(saveDirectory, searchPattern);

            // Extract file names without extension
            string[] saveNames = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                saveNames[i] = Path.GetFileNameWithoutExtension(files[i]);
            }

            return saveNames;
        }

        /// <summary>
        /// Delete save file
        /// </summary>
        public bool DeleteSave(string saveName)
        {
            try
            {
                string filePath = GetSaveFilePath(saveName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    if (logSaveLoadOperations)
                    {
                        ArchonLogger.Log($"SaveManager: Deleted save '{saveName}'");
                    }
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                ArchonLogger.LogError($"SaveManager: Failed to delete save - {ex.Message}");
                return false;
            }
        }

        // ====================================================================
        // PRIVATE IMPLEMENTATION
        // ====================================================================

        private SaveGameData CreateSaveData(string saveName)
        {
            SaveGameData data = new SaveGameData
            {
                gameVersion = Application.version,
                saveFormatVersion = 1,
                saveName = saveName,
                scenarioName = "Default" // TODO: Get from scenario system
            };

            data.SetSaveDate(DateTime.Now);

            // TODO: Get current tick from TimeManager when available
            data.currentTick = 0;
            data.gameSpeed = 1;

            return data;
        }

        private void CallOnSaveForAllSystems(SaveGameData saveData)
        {
            // Save core ENGINE systems directly
            if (gameState.Time != null && gameState.Time.IsInitialized)
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log("SaveManager: Saving TimeManager...");
                }
                SaveTimeManager(saveData);
            }

            if (gameState.Resources != null && gameState.Resources.IsInitialized)
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log("SaveManager: Saving ResourceSystem...");
                }
                SaveResourceSystem(saveData);
            }

            if (gameState.Provinces != null)
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log("SaveManager: Saving ProvinceSystem...");
                }
                SaveProvinceSystem(saveData);
            }

            if (gameState.Modifiers != null)
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log("SaveManager: Saving ModifierSystem...");
                }
                SaveModifierSystem(saveData);
            }

            if (gameState.Countries != null)
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log("SaveManager: Saving CountrySystem...");
                }
                SaveCountrySystem(saveData);
            }

            if (gameState.Units != null)
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log("SaveManager: Saving UnitSystem...");
                }
                SaveUnitSystem(saveData);
            }

            // Save GAME layer PlayerState (via callback to maintain layer separation)
            if (OnSerializePlayerState != null)
            {
                byte[] playerStateData = OnSerializePlayerState();
                if (playerStateData != null)
                {
                    saveData.SetSystemData("PlayerState", playerStateData);
                    if (logSaveLoadOperations)
                    {
                        ArchonLogger.Log("SaveManager: Saved PlayerState");
                    }
                }
            }


            // Save GAME layer GameSystems by calling their OnSave methods via reflection
            foreach (var gameSystem in gameState.GetAllRegisteredGameSystems())
            {
                if (!gameSystem.IsInitialized)
                {
                    if (logSaveLoadOperations)
                    {
                        ArchonLogger.LogWarning($"SaveManager: Skipping uninitialized system '{gameSystem.SystemName}'");
                    }
                    continue;
                }

                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log($"SaveManager: Saving {gameSystem.SystemName}...");
                }

                // Call OnSave (uses reflection to call protected method)
                var method = gameSystem.GetType().GetMethod("OnSave",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                if (method != null)
                {
                    method.Invoke(gameSystem, new object[] { saveData });
                }
                else if (logSaveLoadOperations)
                {
                    ArchonLogger.LogWarning($"SaveManager: {gameSystem.SystemName} does not implement OnSave");
                }
            }
        }

        private void CallOnLoadForAllSystems(SaveGameData saveData)
        {
            // Load core ENGINE systems directly
            if (gameState.Time != null)
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log("SaveManager: Loading TimeManager...");
                }
                LoadTimeManager(saveData);
            }

            if (gameState.Resources != null)
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log("SaveManager: Loading ResourceSystem...");
                }
                LoadResourceSystem(saveData);
            }

            if (gameState.Provinces != null)
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log("SaveManager: Loading ProvinceSystem...");
                }
                LoadProvinceSystem(saveData);
            }

            if (gameState.Modifiers != null)
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log("SaveManager: Loading ModifierSystem...");
                }
                LoadModifierSystem(saveData);
            }

            if (gameState.Countries != null)
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log("SaveManager: Loading CountrySystem...");
                }
                LoadCountrySystem(saveData);
            }

            if (gameState.Units != null)
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log("SaveManager: Loading UnitSystem...");
                }
                LoadUnitSystem(saveData);
            }

            // Load GAME layer PlayerState (via callback to maintain layer separation)
            if (OnDeserializePlayerState != null)
            {
                byte[] playerStateData = saveData.GetSystemData<byte[]>("PlayerState");
                if (playerStateData != null)
                {
                    OnDeserializePlayerState(playerStateData);
                    if (logSaveLoadOperations)
                    {
                        ArchonLogger.Log("SaveManager: Loaded PlayerState");
                    }
                }
            }


            // Load GAME layer GameSystems by calling their OnLoad methods via reflection
            foreach (var gameSystem in gameState.GetAllRegisteredGameSystems())
            {
                if (!gameSystem.IsInitialized)
                {
                    if (logSaveLoadOperations)
                    {
                        ArchonLogger.LogWarning($"SaveManager: Skipping uninitialized system '{gameSystem.SystemName}'");
                    }
                    continue;
                }

                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log($"SaveManager: Loading {gameSystem.SystemName}...");
                }

                // Call OnLoad (uses reflection to call protected method)
                var method = gameSystem.GetType().GetMethod("OnLoad",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                if (method != null)
                {
                    method.Invoke(gameSystem, new object[] { saveData });
                }
                else if (logSaveLoadOperations)
                {
                    ArchonLogger.LogWarning($"SaveManager: {gameSystem.SystemName} does not implement OnLoad");
                }
            }
        }

        // ====================================================================
        // CORE SYSTEM SERIALIZATION
        // ====================================================================

        /// <summary>
        /// Save TimeManager data to save file
        /// Saves: tick, date/time, speed, pause state, accumulator
        /// </summary>
        private void SaveTimeManager(SaveGameData saveData)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream))
            {
                var time = gameState.Time;

                // Write tick counter
                writer.Write(time.CurrentTick);

                // Write date/time
                writer.Write(time.CurrentYear);
                writer.Write(time.CurrentMonth);
                writer.Write(time.CurrentDay);
                writer.Write(time.CurrentHour);

                // Write speed and pause state
                writer.Write(time.GameSpeed);
                writer.Write(time.IsPaused);

                // Write accumulator (need to add getter to TimeManager)
                SerializationHelper.WriteFixedPoint64(writer, time.GetAccumulator());

                // Store in save data
                saveData.SetSystemData("TimeManager", stream.ToArray());
            }
        }

        /// <summary>
        /// Load TimeManager data from save file
        /// </summary>
        private void LoadTimeManager(SaveGameData saveData)
        {
            byte[] data = saveData.GetSystemData<byte[]>("TimeManager");
            if (data == null)
            {
                ArchonLogger.LogWarning("SaveManager: No TimeManager data found in save file");
                return;
            }

            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(data))
            using (System.IO.BinaryReader reader = new System.IO.BinaryReader(stream))
            {
                var time = gameState.Time;

                // Read tick counter
                ulong tick = reader.ReadUInt64();

                // Read date/time
                int year = reader.ReadInt32();
                int month = reader.ReadInt32();
                int day = reader.ReadInt32();
                int hour = reader.ReadInt32();

                // Read speed and pause state
                int speed = reader.ReadInt32();
                bool paused = reader.ReadBoolean();

                // Read accumulator
                FixedPoint64 accumulator = SerializationHelper.ReadFixedPoint64(reader);

                // Restore state (need to add setter to TimeManager)
                time.LoadState(tick, year, month, day, hour, speed, paused, accumulator);
            }
        }

        /// <summary>
        /// Save ResourceSystem data to save file
        /// Saves: maxCountries, resource storage arrays
        /// Skips: resourceDefinitions (will be re-registered by GAME layer on load)
        /// </summary>
        private void SaveResourceSystem(SaveGameData saveData)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream))
            {
                var resources = gameState.Resources;

                // Write capacity
                writer.Write(resources.MaxCountries);

                // Write resource count
                var resourceIds = new System.Collections.Generic.List<ushort>(resources.GetAllResourceIds());
                writer.Write(resourceIds.Count);

                // Write each resource's data
                foreach (ushort resourceId in resourceIds)
                {
                    writer.Write(resourceId);

                    // Write resource values for all countries
                    for (int countryId = 0; countryId < resources.MaxCountries; countryId++)
                    {
                        FixedPoint64 value = resources.GetResource((ushort)countryId, resourceId);
                        SerializationHelper.WriteFixedPoint64(writer, value);
                    }
                }

                // Store in save data
                saveData.SetSystemData("ResourceSystem", stream.ToArray());
            }
        }

        /// <summary>
        /// Load ResourceSystem data from save file
        /// Assumes ResourceSystem is already initialized with resource definitions from GAME layer
        /// </summary>
        private void LoadResourceSystem(SaveGameData saveData)
        {
            byte[] data = saveData.GetSystemData<byte[]>("ResourceSystem");
            if (data == null)
            {
                ArchonLogger.LogWarning("SaveManager: No ResourceSystem data found in save file");
                return;
            }

            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(data))
            using (System.IO.BinaryReader reader = new System.IO.BinaryReader(stream))
            {
                var resources = gameState.Resources;

                // Read capacity
                int savedMaxCountries = reader.ReadInt32();

                // Verify capacity matches (resources should already be initialized)
                if (savedMaxCountries != resources.MaxCountries)
                {
                    ArchonLogger.LogWarning($"SaveManager: Resource capacity mismatch (saved: {savedMaxCountries}, current: {resources.MaxCountries})");
                }

                // Read resource count
                int resourceCount = reader.ReadInt32();

                // Read each resource's data
                for (int i = 0; i < resourceCount; i++)
                {
                    ushort resourceId = reader.ReadUInt16();

                    // Read resource values for all countries
                    for (int countryId = 0; countryId < savedMaxCountries; countryId++)
                    {
                        FixedPoint64 value = SerializationHelper.ReadFixedPoint64(reader);

                        // Only set if resource is registered and country ID is valid
                        if (countryId < resources.MaxCountries && resources.IsResourceRegistered(resourceId))
                        {
                            resources.SetResource((ushort)countryId, resourceId, value);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Save ProvinceSystem data to save file
        /// Saves: capacity, province states (8 bytes each), id mappings, active province list
        /// Uses raw memory copy for ProvinceState array (blittable struct)
        /// </summary>
        private void SaveProvinceSystem(SaveGameData saveData)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream))
            {
                var provinces = gameState.Provinces;

                // Delegate to ProvinceSystem.SaveState
                provinces.SaveState(writer);

                // Store in save data
                saveData.SetSystemData("ProvinceSystem", stream.ToArray());
            }
        }

        /// <summary>
        /// Load ProvinceSystem data from save file
        /// Restores province states, mappings, and syncs double buffers
        /// </summary>
        private void LoadProvinceSystem(SaveGameData saveData)
        {
            byte[] data = saveData.GetSystemData<byte[]>("ProvinceSystem");
            if (data == null)
            {
                ArchonLogger.LogWarning("SaveManager: No ProvinceSystem data found in save file");
                return;
            }

            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(data))
            using (System.IO.BinaryReader reader = new System.IO.BinaryReader(stream))
            {
                var provinces = gameState.Provinces;

                // Delegate to ProvinceSystem.LoadState
                provinces.LoadState(reader);
            }
        }

        /// <summary>
        /// Save ModifierSystem data to save file
        /// Saves: capacities, global scope, country scopes, province scopes
        /// Only saves local modifiers - cachedModifierSet will be rebuilt on load
        /// </summary>
        private void SaveModifierSystem(SaveGameData saveData)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream))
            {
                var modifiers = gameState.Modifiers;

                // Delegate to ModifierSystem.SaveState
                modifiers.SaveState(writer);

                // Store in save data
                saveData.SetSystemData("ModifierSystem", stream.ToArray());
            }
        }

        /// <summary>
        /// Load ModifierSystem data from save file
        /// Restores modifiers and marks caches as dirty to force rebuild
        /// </summary>
        private void LoadModifierSystem(SaveGameData saveData)
        {
            byte[] data = saveData.GetSystemData<byte[]>("ModifierSystem");
            if (data == null)
            {
                ArchonLogger.LogWarning("SaveManager: No ModifierSystem data found in save file");
                return;
            }

            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(data))
            using (System.IO.BinaryReader reader = new System.IO.BinaryReader(stream))
            {
                var modifiers = gameState.Modifiers;

                // Delegate to ModifierSystem.LoadState
                modifiers.LoadState(reader);
            }
        }

        /// <summary>
        /// Save CountrySystem data to save file
        /// Saves: capacity, hot data arrays, id mappings, tags, cold data cache
        /// </summary>
        private void SaveCountrySystem(SaveGameData saveData)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream))
            {
                var countries = gameState.Countries;

                // Delegate to CountrySystem.SaveState
                countries.SaveState(writer);

                // Store in save data
                saveData.SetSystemData("CountrySystem", stream.ToArray());
            }
        }

        /// <summary>
        /// Load CountrySystem data from save file
        /// Restores hot/cold data, id mappings, and tags
        /// </summary>
        private void LoadCountrySystem(SaveGameData saveData)
        {
            byte[] data = saveData.GetSystemData<byte[]>("CountrySystem");
            if (data == null)
            {
                ArchonLogger.LogWarning("SaveManager: No CountrySystem data found in save file");
                return;
            }

            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(data))
            using (System.IO.BinaryReader reader = new System.IO.BinaryReader(stream))
            {
                var countries = gameState.Countries;

                // Delegate to CountrySystem.LoadState
                countries.LoadState(reader);
            }
        }

        /// <summary>
        /// Save UnitSystem data to save file
        /// Saves: unit hot data, sparse mappings (province→units, country→units), cold data
        /// </summary>
        private void SaveUnitSystem(SaveGameData saveData)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream))
            {
                var units = gameState.Units;

                // Delegate to UnitSystem.SaveState
                units.SaveState(writer);

                // Store in save data
                saveData.SetSystemData("UnitSystem", stream.ToArray());
            }
        }

        /// <summary>
        /// Load UnitSystem data from save file
        /// Restores all units, sparse mappings, and cold data
        /// </summary>
        private void LoadUnitSystem(SaveGameData saveData)
        {
            byte[] data = saveData.GetSystemData<byte[]>("UnitSystem");
            if (data == null)
            {
                ArchonLogger.LogWarning("SaveManager: No UnitSystem data found in save file");
                return;
            }

            using (System.IO.MemoryStream stream = new System.IO.MemoryStream(data))
            using (System.IO.BinaryReader reader = new System.IO.BinaryReader(stream))
            {
                var units = gameState.Units;

                // Delegate to UnitSystem.LoadState
                units.LoadState(reader);
            }
        }

        private void SerializeToDisk(SaveGameData saveData, string filePath)
        {
            // Atomic write: write to temp file, then rename
            string tempPath = filePath + ".tmp";

            using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                // Write header (magic bytes for file type validation)
                writer.Write("HGSV".ToCharArray()); // Hegemon Save

                // Write metadata
                SerializationHelper.WriteString(writer, saveData.gameVersion);
                writer.Write(saveData.saveFormatVersion);
                SerializationHelper.WriteString(writer, saveData.saveName);
                writer.Write(saveData.saveDateTicks);
                writer.Write(saveData.currentTick);
                writer.Write(saveData.gameSpeed);
                SerializationHelper.WriteString(writer, saveData.scenarioName);

                // Write system data count
                writer.Write(saveData.systemData.Count);

                // Write each system's data
                foreach (var kvp in saveData.systemData)
                {
                    SerializationHelper.WriteString(writer, kvp.Key); // System name

                    // Serialize system data as byte array
                    if (kvp.Value is byte[] bytes)
                    {
                        writer.Write(bytes.Length);
                        writer.Write(bytes);
                    }
                    else
                    {
                        // For now, skip non-byte-array data
                        // TODO: Add support for other serialization formats
                        writer.Write(0);
                    }
                }

                // Write command log (for verification)
                writer.Write(saveData.commandLog.Count);
                foreach (var commandBytes in saveData.commandLog)
                {
                    writer.Write(commandBytes.Length);
                    writer.Write(commandBytes);
                }

                // Write checksum
                writer.Write(saveData.expectedChecksum);
            }

            // Atomic rename (overwrites existing file)
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            File.Move(tempPath, filePath);
        }

        private SaveGameData DeserializeFromDisk(string filePath)
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                // Read and verify header
                char[] magic = reader.ReadChars(4);
                string magicStr = new string(magic);
                if (magicStr != "HGSV")
                {
                    throw new Exception($"Invalid save file format (expected 'HGSV', got '{magicStr}')");
                }

                SaveGameData data = new SaveGameData();

                // Read metadata
                data.gameVersion = SerializationHelper.ReadString(reader);
                data.saveFormatVersion = reader.ReadInt32();
                data.saveName = SerializationHelper.ReadString(reader);
                data.saveDateTicks = reader.ReadInt64();
                data.currentTick = reader.ReadInt32();
                data.gameSpeed = reader.ReadInt32();
                data.scenarioName = SerializationHelper.ReadString(reader);

                // Read system data
                int systemDataCount = reader.ReadInt32();
                for (int i = 0; i < systemDataCount; i++)
                {
                    string systemName = SerializationHelper.ReadString(reader);
                    int dataLength = reader.ReadInt32();

                    if (dataLength > 0)
                    {
                        byte[] systemBytes = reader.ReadBytes(dataLength);
                        data.systemData[systemName] = systemBytes;
                    }
                }

                // Read command log
                int commandCount = reader.ReadInt32();
                for (int i = 0; i < commandCount; i++)
                {
                    int commandLength = reader.ReadInt32();
                    byte[] commandBytes = reader.ReadBytes(commandLength);
                    data.commandLog.Add(commandBytes);
                }

                // Read checksum
                data.expectedChecksum = reader.ReadUInt32();

                return data;
            }
        }

        private bool VerifyVersionCompatibility(SaveGameData saveData)
        {
            if (!saveData.IsCompatibleVersion(Application.version))
            {
                ArchonLogger.LogWarning($"SaveManager: Save file version mismatch (save: {saveData.gameVersion}, current: {Application.version})");
                // For now, allow loading anyway (no migration yet)
                // TODO: Implement version migration
                return true;
            }

            return true;
        }

        private string GetSaveFilePath(string saveName)
        {
            return Path.Combine(saveDirectory, $"{saveName}.{saveFileExtension}");
        }
    }
}
