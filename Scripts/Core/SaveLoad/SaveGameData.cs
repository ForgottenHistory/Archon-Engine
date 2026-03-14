using System;
using System.Collections.Generic;
using System.Text;

namespace Core.SaveLoad
{
    /// <summary>
    /// Fixed-size metadata header for fast save browser preview.
    /// Written at a fixed position in the save file so it can be read
    /// without deserializing the entire file.
    /// Total size: 256 bytes.
    /// </summary>
    public struct SaveMetadata
    {
        /// <summary>Fixed size of the metadata block in bytes</summary>
        public const int SIZE = 256;

        // Field sizes (must sum to 256)
        private const int SAVE_NAME_SIZE = 64;       // 64 bytes = 32 chars UTF-16
        private const int SCENARIO_NAME_SIZE = 64;    // 64 bytes = 32 chars UTF-16
        private const int GAME_VERSION_SIZE = 32;     // 32 bytes = 16 chars UTF-16
        // Remaining: 8 (dateTicks) + 8 (currentTick) + 4 (gameSpeed) + 4 (saveFormatVersion)
        //          + 4 (provinceCount) + 4 (countryCount) + 4 (payloadSize) + 4 (flags)
        //          + 56 (reserved) = 96 bytes
        // Total: 64 + 64 + 32 + 96 = 256

        public string saveName;
        public string scenarioName;
        public string gameVersion;
        public long saveDateTicks;
        public ulong currentTick;
        public int gameSpeed;
        public int saveFormatVersion;
        public int provinceCount;
        public int countryCount;
        public int compressedPayloadSize;
        public int flags; // bit 0: compressed

        public DateTime SaveDate => new DateTime(saveDateTicks);
        public bool IsCompressed => (flags & 1) != 0;

        /// <summary>
        /// Write metadata as exactly SIZE bytes.
        /// Strings are truncated/padded to their fixed allocation.
        /// </summary>
        public void WriteTo(System.IO.BinaryWriter writer)
        {
            long startPos = writer.BaseStream.Position;

            WriteFixedString(writer, saveName, SAVE_NAME_SIZE);
            WriteFixedString(writer, scenarioName, SCENARIO_NAME_SIZE);
            WriteFixedString(writer, gameVersion, GAME_VERSION_SIZE);
            writer.Write(saveDateTicks);
            writer.Write(currentTick);
            writer.Write(gameSpeed);
            writer.Write(saveFormatVersion);
            writer.Write(provinceCount);
            writer.Write(countryCount);
            writer.Write(compressedPayloadSize);
            writer.Write(flags);

            // Pad remaining bytes to reach exactly SIZE
            long written = writer.BaseStream.Position - startPos;
            int padding = SIZE - (int)written;
            if (padding > 0)
                writer.Write(new byte[padding]);
        }

        /// <summary>
        /// Read metadata from exactly SIZE bytes.
        /// </summary>
        public static SaveMetadata ReadFrom(System.IO.BinaryReader reader)
        {
            long startPos = reader.BaseStream.Position;

            var meta = new SaveMetadata
            {
                saveName = ReadFixedString(reader, SAVE_NAME_SIZE),
                scenarioName = ReadFixedString(reader, SCENARIO_NAME_SIZE),
                gameVersion = ReadFixedString(reader, GAME_VERSION_SIZE),
                saveDateTicks = reader.ReadInt64(),
                currentTick = reader.ReadUInt64(),
                gameSpeed = reader.ReadInt32(),
                saveFormatVersion = reader.ReadInt32(),
                provinceCount = reader.ReadInt32(),
                countryCount = reader.ReadInt32(),
                compressedPayloadSize = reader.ReadInt32(),
                flags = reader.ReadInt32()
            };

            // Skip remaining padding
            long read = reader.BaseStream.Position - startPos;
            int remaining = SIZE - (int)read;
            if (remaining > 0)
                reader.ReadBytes(remaining);

            return meta;
        }

        private static void WriteFixedString(System.IO.BinaryWriter writer, string value, int byteSize)
        {
            byte[] buffer = new byte[byteSize];
            if (!string.IsNullOrEmpty(value))
            {
                int maxChars = byteSize / 2; // UTF-16 = 2 bytes per char
                string truncated = value.Length > maxChars ? value.Substring(0, maxChars) : value;
                byte[] strBytes = Encoding.Unicode.GetBytes(truncated);
                Array.Copy(strBytes, 0, buffer, 0, Math.Min(strBytes.Length, byteSize));
            }
            writer.Write(buffer);
        }

        private static string ReadFixedString(System.IO.BinaryReader reader, int byteSize)
        {
            byte[] buffer = reader.ReadBytes(byteSize);
            string result = Encoding.Unicode.GetString(buffer);
            // Trim null characters (padding)
            int nullIdx = result.IndexOf('\0');
            return nullIdx >= 0 ? result.Substring(0, nullIdx) : result;
        }
    }

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
        public ulong currentTick;

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
