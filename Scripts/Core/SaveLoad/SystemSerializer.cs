using System;
using System.IO;
using UnityEngine;

namespace Core.SaveLoad
{
    /// <summary>
    /// ENGINE LAYER - Generic system serialization helper
    ///
    /// Responsibilities:
    /// - Provide generic Save/Load methods for systems with SaveState/LoadState
    /// - Handle MemoryStream/BinaryWriter/BinaryReader boilerplate
    /// - Store/retrieve serialized data in SaveGameData
    /// - Reduce SaveManager code duplication
    ///
    /// Pattern:
    /// Instead of writing 6+ pairs of SaveXXX/LoadXXX methods with identical patterns,
    /// use generic methods that work with any system implementing the save/load pattern.
    ///
    /// Usage:
    /// systemSerializer.SaveSystem(saveData, "TimeManager", gameState.Time);
    /// systemSerializer.LoadSystem(saveData, "TimeManager", gameState.Time);
    /// </summary>
    public class SystemSerializer
    {
        private bool logOperations;

        public SystemSerializer(bool logOperations = false)
        {
            this.logOperations = logOperations;
        }

        /// <summary>
        /// Save a system's state using its SaveState(BinaryWriter) method
        /// Generic method that works with any system following the save pattern
        /// </summary>
        public void SaveSystem<T>(SaveGameData saveData, string systemName, T system, Action<BinaryWriter> saveAction)
        {
            if (system == null)
            {
                if (logOperations)
                {
                    ArchonLogger.LogWarning($"SystemSerializer: Skipping save for null system '{systemName}'", "core_saveload");
                }
                return;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream())
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    // Call the system's save logic
                    saveAction(writer);

                    // Store in save data
                    saveData.SetSystemData(systemName, stream.ToArray());

                    if (logOperations)
                    {
                        ArchonLogger.Log($"SystemSerializer: Saved {systemName} ({stream.Length} bytes)", "core_saveload");
                    }
                }
            }
            catch (Exception ex)
            {
                ArchonLogger.LogError($"SystemSerializer: Failed to save {systemName} - {ex.Message}\n{ex.StackTrace}", "core_saveload");
                throw;
            }
        }

        /// <summary>
        /// Load a system's state using its LoadState(BinaryReader) method
        /// Generic method that works with any system following the load pattern
        /// </summary>
        public void LoadSystem<T>(SaveGameData saveData, string systemName, T system, Action<BinaryReader> loadAction)
        {
            if (system == null)
            {
                if (logOperations)
                {
                    ArchonLogger.LogWarning($"SystemSerializer: Skipping load for null system '{systemName}'", "core_saveload");
                }
                return;
            }

            byte[] data = saveData.GetSystemData<byte[]>(systemName);
            if (data == null)
            {
                ArchonLogger.LogWarning($"SystemSerializer: No {systemName} data found in save file", "core_saveload");
                return;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream(data))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    // Call the system's load logic
                    loadAction(reader);

                    if (logOperations)
                    {
                        ArchonLogger.Log($"SystemSerializer: Loaded {systemName} ({data.Length} bytes)", "core_saveload");
                    }
                }
            }
            catch (Exception ex)
            {
                ArchonLogger.LogError($"SystemSerializer: Failed to load {systemName} - {ex.Message}\n{ex.StackTrace}", "core_saveload");
                throw;
            }
        }

        /// <summary>
        /// Enable/disable operation logging
        /// </summary>
        public void SetLogOperations(bool enabled)
        {
            logOperations = enabled;
        }
    }
}
