using System;
using System.IO;
using UnityEngine;

namespace Core.SaveLoad
{
    /// <summary>
    /// ENGINE LAYER - Binary save file format serializer
    ///
    /// Responsibilities:
    /// - Write SaveGameData to binary file format
    /// - Read SaveGameData from binary file format
    /// - Handle file format: magic bytes, headers, metadata
    /// - Atomic writes (temp file → rename) to prevent corruption
    /// - Version validation
    ///
    /// File Format:
    /// - Magic bytes: "HGSV" (4 bytes)
    /// - Metadata: version, save name, date, tick, speed, scenario
    /// - System data: count + (name, bytes) pairs
    /// - Command log: count + bytes[]
    /// - Checksum: uint32
    ///
    /// Usage:
    /// SaveFileSerializer.WriteToDisk(saveData, filePath);
    /// SaveGameData data = SaveFileSerializer.ReadFromDisk(filePath);
    /// </summary>
    public static class SaveFileSerializer
    {
        private const string MAGIC_BYTES = "HGSV"; // Hegemon Save

        /// <summary>
        /// Write SaveGameData to disk with atomic write (temp file → rename)
        /// </summary>
        public static void WriteToDisk(SaveGameData saveData, string filePath)
        {
            // Atomic write: write to temp file, then rename
            string tempPath = filePath + ".tmp";

            using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                WriteHeader(writer, saveData);
                WriteSystemData(writer, saveData);
                WriteCommandLog(writer, saveData);
                WriteChecksum(writer, saveData);
            }

            // Atomic rename (overwrites existing file)
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            File.Move(tempPath, filePath);
        }

        /// <summary>
        /// Read SaveGameData from disk
        /// </summary>
        public static SaveGameData ReadFromDisk(string filePath)
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                // Verify magic bytes
                VerifyMagicBytes(reader);

                SaveGameData data = new SaveGameData();

                ReadHeader(reader, data);
                ReadSystemData(reader, data);
                ReadCommandLog(reader, data);
                ReadChecksum(reader, data);

                return data;
            }
        }

        /// <summary>
        /// Verify version compatibility
        /// </summary>
        public static bool VerifyVersionCompatibility(SaveGameData saveData, string currentVersion)
        {
            if (!saveData.IsCompatibleVersion(currentVersion))
            {
                ArchonLogger.LogCoreSaveLoadWarning($"SaveFileSerializer: Save file version mismatch (save: {saveData.gameVersion}, current: {currentVersion})");
                // For now, allow loading anyway (no migration yet)
                // TODO: Implement version migration
                return true;
            }

            return true;
        }

        #region Write Operations

        private static void WriteHeader(BinaryWriter writer, SaveGameData saveData)
        {
            // Write magic bytes (file type validation)
            writer.Write(MAGIC_BYTES.ToCharArray());

            // Write metadata
            SerializationHelper.WriteString(writer, saveData.gameVersion);
            writer.Write(saveData.saveFormatVersion);
            SerializationHelper.WriteString(writer, saveData.saveName);
            writer.Write(saveData.saveDateTicks);
            writer.Write(saveData.currentTick);
            writer.Write(saveData.gameSpeed);
            SerializationHelper.WriteString(writer, saveData.scenarioName);
        }

        private static void WriteSystemData(BinaryWriter writer, SaveGameData saveData)
        {
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
        }

        private static void WriteCommandLog(BinaryWriter writer, SaveGameData saveData)
        {
            // Write command log (for verification)
            writer.Write(saveData.commandLog.Count);
            foreach (var commandBytes in saveData.commandLog)
            {
                writer.Write(commandBytes.Length);
                writer.Write(commandBytes);
            }
        }

        private static void WriteChecksum(BinaryWriter writer, SaveGameData saveData)
        {
            writer.Write(saveData.expectedChecksum);
        }

        #endregion

        #region Read Operations

        private static void VerifyMagicBytes(BinaryReader reader)
        {
            char[] magic = reader.ReadChars(4);
            string magicStr = new string(magic);
            if (magicStr != MAGIC_BYTES)
            {
                throw new Exception($"Invalid save file format (expected '{MAGIC_BYTES}', got '{magicStr}')");
            }
        }

        private static void ReadHeader(BinaryReader reader, SaveGameData data)
        {
            data.gameVersion = SerializationHelper.ReadString(reader);
            data.saveFormatVersion = reader.ReadInt32();
            data.saveName = SerializationHelper.ReadString(reader);
            data.saveDateTicks = reader.ReadInt64();
            data.currentTick = reader.ReadInt32();
            data.gameSpeed = reader.ReadInt32();
            data.scenarioName = SerializationHelper.ReadString(reader);
        }

        private static void ReadSystemData(BinaryReader reader, SaveGameData data)
        {
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
        }

        private static void ReadCommandLog(BinaryReader reader, SaveGameData data)
        {
            int commandCount = reader.ReadInt32();
            for (int i = 0; i < commandCount; i++)
            {
                int commandLength = reader.ReadInt32();
                byte[] commandBytes = reader.ReadBytes(commandLength);
                data.commandLog.Add(commandBytes);
            }
        }

        private static void ReadChecksum(BinaryReader reader, SaveGameData data)
        {
            data.expectedChecksum = reader.ReadUInt32();
        }

        #endregion
    }
}
