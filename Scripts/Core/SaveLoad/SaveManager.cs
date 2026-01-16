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

        [Header("Hotkeys")]
        [Tooltip("Key for quick save")]
        public KeyCode quickSaveKey = KeyCode.F6;

        [Tooltip("Key for quick load")]
        public KeyCode quickLoadKey = KeyCode.F7;

        [Tooltip("Enable hotkey handling (disable if using custom input system)")]
        public bool enableHotkeys = true;

        // Save file directory (platform-specific)
        private string saveDirectory;

        // GameState reference (accessed via singleton)
        private GameState gameState => GameState.Instance;

        // System serialization helper (reduces boilerplate)
        private SystemSerializer systemSerializer;

        // GAME layer finalization callback (architecture compliance: ENGINE doesn't call GAME code)
        // GAME layer sets this to rebuild caches, etc.
        public System.Action OnPostLoadFinalize;

        // GAME layer PlayerState serialization (ENGINE stores as opaque byte[], GAME interprets)
        public System.Func<byte[]> OnSerializePlayerState;
        public System.Action<byte[]> OnDeserializePlayerState;

        void Awake()
        {
            // Initialize system serializer
            systemSerializer = new SystemSerializer(logSaveLoadOperations);

            // Determine platform-specific save directory
            saveDirectory = GetSaveDirectory();

            // Create directory if it doesn't exist
            if (!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
                ArchonLogger.Log($"SaveManager: Created save directory at {saveDirectory}", "core_saveload");
            }
            else if (logSaveLoadOperations)
            {
                ArchonLogger.Log($"SaveManager: Using save directory {saveDirectory}", "core_saveload");
            }
        }

        void Update()
        {
            if (!enableHotkeys) return;

            // Quick save
            if (Input.GetKeyDown(quickSaveKey))
            {
                if (logSaveLoadOperations)
                    ArchonLogger.Log($"SaveManager: {quickSaveKey} pressed - Quicksaving...", "core_saveload");
                QuickSave();
            }

            // Quick load
            if (Input.GetKeyDown(quickLoadKey))
            {
                if (logSaveLoadOperations)
                    ArchonLogger.Log($"SaveManager: {quickLoadKey} pressed - Quickloading...", "core_saveload");
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
                ArchonLogger.LogError("SaveManager: Save name cannot be null or empty", "core_saveload");
                return false;
            }

            if (gameState == null)
            {
                ArchonLogger.LogError("SaveManager: GameState not assigned", "core_saveload");
                return false;
            }

            try
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log($"SaveManager: Starting save '{saveName}'...", "core_saveload");
                }

                // Create save data container
                SaveGameData saveData = CreateSaveData(saveName);

                // Call OnSave for all systems (dependency order)
                CallOnSaveForAllSystems(saveData);

                // Serialize to disk (atomic write)
                string filePath = GetSaveFilePath(saveName);
                SaveFileSerializer.WriteToDisk(saveData, filePath);

                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log($"SaveManager: ✓ Save completed - {filePath}", "core_saveload");
                }

                return true;
            }
            catch (Exception ex)
            {
                ArchonLogger.LogError($"SaveManager: Save failed - {ex.Message}\n{ex.StackTrace}", "core_saveload");
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
                ArchonLogger.Log("SaveManager: Finalizing after load...", "core_saveload");
            }

            // Step 1: Sync double buffers (ENGINE layer only - safe)
            SyncDoubleBuffers();

            // Step 2: Let GAME layer handle MAP refresh + GAME finalization via callback
            // (GAME can import MAP, so no architecture violation)
            OnPostLoadFinalize?.Invoke();

            if (logSaveLoadOperations)
            {
                ArchonLogger.Log("SaveManager: ✓ Post-load finalization complete", "core_saveload");
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
                    ArchonLogger.Log("SaveManager: ✓ Province buffers synced", "core_saveload");
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
                ArchonLogger.LogError("SaveManager: Save name cannot be null or empty", "core_saveload");
                return false;
            }

            if (gameState == null)
            {
                ArchonLogger.LogError("SaveManager: GameState not assigned", "core_saveload");
                return false;
            }

            try
            {
                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log($"SaveManager: Starting load '{saveName}'...", "core_saveload");
                }

                // Check if file exists
                string filePath = GetSaveFilePath(saveName);
                if (!File.Exists(filePath))
                {
                    ArchonLogger.LogError($"SaveManager: Save file not found - {filePath}", "core_saveload");
                    return false;
                }

                // Deserialize from disk
                SaveGameData saveData = SaveFileSerializer.ReadFromDisk(filePath);

                // Verify version compatibility
                if (!SaveFileSerializer.VerifyVersionCompatibility(saveData, Application.version))
                {
                    return false;
                }

                // Call OnLoad for all systems (dependency order)
                CallOnLoadForAllSystems(saveData);

                // CRITICAL: Finalize after load - rebuild caches and refresh GPU textures
                FinalizeAfterLoad();

                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log($"SaveManager: ✓ Load completed - {filePath}", "core_saveload");
                }

                return true;
            }
            catch (Exception ex)
            {
                ArchonLogger.LogError($"SaveManager: Load failed - {ex.Message}\n{ex.StackTrace}", "core_saveload");
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
                        ArchonLogger.Log($"SaveManager: Deleted save '{saveName}'", "core_saveload");
                    }
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                ArchonLogger.LogError($"SaveManager: Failed to delete save - {ex.Message}", "core_saveload");
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
            // Save core ENGINE systems using SystemSerializer
            if (gameState.Time != null && gameState.Time.IsInitialized)
            {
                systemSerializer.SaveSystem(saveData, "TimeManager", gameState.Time,
                    writer => CustomSystemSerializers.SaveTimeManager(writer, gameState.Time));
            }

            if (gameState.Resources != null && gameState.Resources.IsInitialized)
            {
                systemSerializer.SaveSystem(saveData, "ResourceSystem", gameState.Resources,
                    writer => CustomSystemSerializers.SaveResourceSystem(writer, gameState.Resources));
            }

            if (gameState.Provinces != null)
            {
                systemSerializer.SaveSystem(saveData, "ProvinceSystem", gameState.Provinces, writer => gameState.Provinces.SaveState(writer));
            }

            if (gameState.Modifiers != null)
            {
                systemSerializer.SaveSystem(saveData, "ModifierSystem", gameState.Modifiers, writer => gameState.Modifiers.SaveState(writer));
            }

            if (gameState.Countries != null)
            {
                systemSerializer.SaveSystem(saveData, "CountrySystem", gameState.Countries, writer => gameState.Countries.SaveState(writer));
            }

            if (gameState.Units != null)
            {
                systemSerializer.SaveSystem(saveData, "UnitSystem", gameState.Units, writer => gameState.Units.SaveState(writer));
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
                        ArchonLogger.Log("SaveManager: Saved PlayerState", "core_saveload");
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
                        ArchonLogger.LogWarning($"SaveManager: Skipping uninitialized system '{gameSystem.SystemName}'", "core_saveload");
                    }
                    continue;
                }

                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log($"SaveManager: Saving {gameSystem.SystemName}...", "core_saveload");
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
                    ArchonLogger.LogWarning($"SaveManager: {gameSystem.SystemName} does not implement OnSave", "core_saveload");
                }
            }
        }

        private void CallOnLoadForAllSystems(SaveGameData saveData)
        {
            // Load core ENGINE systems using SystemSerializer
            if (gameState.Time != null)
            {
                systemSerializer.LoadSystem(saveData, "TimeManager", gameState.Time,
                    reader => CustomSystemSerializers.LoadTimeManager(reader, gameState.Time));
            }

            if (gameState.Resources != null)
            {
                systemSerializer.LoadSystem(saveData, "ResourceSystem", gameState.Resources,
                    reader => CustomSystemSerializers.LoadResourceSystem(reader, gameState.Resources));
            }

            if (gameState.Provinces != null)
            {
                systemSerializer.LoadSystem(saveData, "ProvinceSystem", gameState.Provinces, reader => gameState.Provinces.LoadState(reader));
            }

            if (gameState.Modifiers != null)
            {
                systemSerializer.LoadSystem(saveData, "ModifierSystem", gameState.Modifiers, reader => gameState.Modifiers.LoadState(reader));
            }

            if (gameState.Countries != null)
            {
                systemSerializer.LoadSystem(saveData, "CountrySystem", gameState.Countries, reader => gameState.Countries.LoadState(reader));
            }

            if (gameState.Units != null)
            {
                systemSerializer.LoadSystem(saveData, "UnitSystem", gameState.Units, reader => gameState.Units.LoadState(reader));
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
                        ArchonLogger.Log("SaveManager: Loaded PlayerState", "core_saveload");
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
                        ArchonLogger.LogWarning($"SaveManager: Skipping uninitialized system '{gameSystem.SystemName}'", "core_saveload");
                    }
                    continue;
                }

                if (logSaveLoadOperations)
                {
                    ArchonLogger.Log($"SaveManager: Loading {gameSystem.SystemName}...", "core_saveload");
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
                    ArchonLogger.LogWarning($"SaveManager: {gameSystem.SystemName} does not implement OnLoad", "core_saveload");
                }
            }
        }

        // ====================================================================
        // FILE PATH HELPER
        // ====================================================================

        private string GetSaveFilePath(string saveName)
        {
            return Path.Combine(saveDirectory, $"{saveName}.{saveFileExtension}");
        }
    }
}
