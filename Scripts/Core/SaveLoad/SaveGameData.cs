using System;
using System.Collections.Generic;

namespace Core.SaveLoad
{
    /// <summary>
    /// ENGINE LAYER - Container for all save game data
    ///
    /// Architecture:
    /// - Generic container, systems populate their own sections
    /// - Metadata for version compatibility and UI display
    /// - State snapshot for fast loading
    /// - Command log for determinism verification
    ///
    /// Usage:
    /// SaveManager creates this, passes to systems via OnSave()
    /// Systems populate their data sections
    /// SaveManager serializes to disk
    /// </summary>
    [Serializable]
    public class SaveGameData
    {
        // ====================================================================
        // METADATA - Version compatibility and display info
        // ====================================================================

        /// <summary>
        /// Game version that created this save (for compatibility checks)
        /// Format: "1.0.0"
        /// </summary>
        public string gameVersion;

        /// <summary>
        /// Save file format version (incremented when format changes)
        /// Allows migration of old saves to new format
        /// </summary>
        public int saveFormatVersion = 1;

        /// <summary>
        /// User-facing save name (displayed in UI)
        /// </summary>
        public string saveName;

        /// <summary>
        /// Date/time when save was created (for sorting/display)
        /// Stored as ticks for serialization
        /// </summary>
        public long saveDateTicks;

        /// <summary>
        /// Current game tick when saved
        /// </summary>
        public int currentTick;

        /// <summary>
        /// Current game speed when saved
        /// </summary>
        public int gameSpeed;

        /// <summary>
        /// Scenario name (e.g., "1444 Start")
        /// </summary>
        public string scenarioName;

        // ====================================================================
        // SYSTEM DATA - Each system populates its section
        // ====================================================================

        /// <summary>
        /// Generic storage for system-specific data
        /// Key: System name, Value: Serialized system data
        /// Systems can store byte[] or custom serializable objects
        /// </summary>
        public Dictionary<string, object> systemData = new Dictionary<string, object>();

        // ====================================================================
        // COMMAND LOG - For determinism verification
        // ====================================================================

        /// <summary>
        /// Command history (last N commands for verification)
        /// Serialized command bytes
        /// </summary>
        public List<byte[]> commandLog = new List<byte[]>();

        /// <summary>
        /// Expected checksum after replaying command log
        /// Used to verify determinism
        /// </summary>
        public uint expectedChecksum;

        // ====================================================================
        // HELPER METHODS
        // ====================================================================

        /// <summary>
        /// Get save date as DateTime
        /// </summary>
        public DateTime GetSaveDate()
        {
            return new DateTime(saveDateTicks);
        }

        /// <summary>
        /// Set save date from DateTime
        /// </summary>
        public void SetSaveDate(DateTime date)
        {
            saveDateTicks = date.Ticks;
        }

        /// <summary>
        /// Check if this save is compatible with current game version
        /// </summary>
        public bool IsCompatibleVersion(string currentVersion)
        {
            // For now, exact version match required
            // TODO: Implement version migration logic
            return gameVersion == currentVersion;
        }

        /// <summary>
        /// Get system data for a specific system
        /// </summary>
        public T GetSystemData<T>(string systemName) where T : class
        {
            if (systemData.TryGetValue(systemName, out var data))
            {
                return data as T;
            }
            return null;
        }

        /// <summary>
        /// Set system data for a specific system
        /// </summary>
        public void SetSystemData(string systemName, object data)
        {
            systemData[systemName] = data;
        }
    }
}
