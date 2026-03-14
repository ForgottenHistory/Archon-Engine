using System;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using UnityEngine;

namespace Core.SaveLoad
{
    /// <summary>
    /// ENGINE LAYER - Binary save file format serializer
    ///
    /// File Format v2:
    /// - Magic bytes: "HGSV" (4 bytes)
    /// - Format version: int32 (4 bytes) — 1 = legacy, 2 = metadata+compression
    /// - Metadata header: 256 bytes (fixed-size, readable without full deserialization)
    /// - Compressed payload (GZip):
    ///   - System data: count + (name, length, bytes) pairs
    ///   - Command log: count + bytes[]
    /// - Checksum: uint32 (CRC32 of compressed payload)
    ///
    /// File Format v1 (legacy):
    /// - Magic bytes: "HGSV" (4 bytes)
    /// - Header: variable-length metadata
    /// - System data + Command log (uncompressed)
    /// - Checksum: uint32
    /// </summary>
    public static class SaveFileSerializer
    {
        private const string MAGIC_BYTES = "HGSV";
        // Format version marker — must be > 1000 to distinguish from v1 legacy format
        // (v1 writes gameVersion string length as first int32 after magic, which is always small)
        private const int CURRENT_FORMAT_VERSION = 1001;

        private static uint CalculateChecksum(byte[] data)
        {
            var crc = new Crc32();
            crc.Append(data);
            return BitConverter.ToUInt32(crc.GetCurrentHash(), 0);
        }

        /// <summary>
        /// Read only the metadata header from a save file (fast — reads ~264 bytes).
        /// Returns null if the file is invalid or uses legacy format.
        /// </summary>
        public static SaveMetadata? ReadSaveMetadata(string filePath)
        {
            try
            {
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    // Verify magic bytes
                    char[] magic = reader.ReadChars(4);
                    if (new string(magic) != MAGIC_BYTES)
                        return null;

                    int formatVersion = reader.ReadInt32();
                    if (formatVersion < 1000)
                    {
                        // Legacy format — no quick metadata available
                        return null;
                    }

                    return SaveMetadata.ReadFrom(reader);
                }
            }
            catch (Exception ex)
            {
                ArchonLogger.LogWarning($"SaveFileSerializer: Failed to read metadata from {filePath}: {ex.Message}", "core_saveload");
                return null;
            }
        }

        /// <summary>
        /// Write SaveGameData to disk with atomic write (temp file → rename).
        /// Uses format v2: fixed metadata header + GZip compressed payload.
        /// </summary>
        public static void WriteToDisk(SaveGameData saveData, string filePath)
        {
            string tempPath = filePath + ".tmp";

            // Compress payload to memory
            byte[] compressedPayload;
            using (MemoryStream compressedStream = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(compressedStream, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
                using (BinaryWriter payloadWriter = new BinaryWriter(gzip))
                {
                    WriteSystemData(payloadWriter, saveData);
                    WriteCommandLog(payloadWriter, saveData);
                }
                compressedPayload = compressedStream.ToArray();
            }

            // Calculate checksum of compressed payload
            saveData.expectedChecksum = CalculateChecksum(compressedPayload);

            // Build metadata header
            var metadata = new SaveMetadata
            {
                saveName = saveData.saveName,
                scenarioName = saveData.scenarioName,
                gameVersion = saveData.gameVersion,
                saveDateTicks = saveData.saveDateTicks,
                currentTick = saveData.currentTick,
                gameSpeed = saveData.gameSpeed,
                saveFormatVersion = CURRENT_FORMAT_VERSION,
                compressedPayloadSize = compressedPayload.Length,
                flags = 1 // bit 0 = compressed
            };

            // Get province/country counts if available
            var gameState = GameState.Instance;
            if (gameState?.Provinces != null)
                metadata.provinceCount = gameState.Provinces.ProvinceCount;
            if (gameState?.Countries != null)
                metadata.countryCount = gameState.Countries.CountryCount;

            // Write file
            using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                // Magic bytes
                writer.Write(MAGIC_BYTES.ToCharArray());

                // Format version
                writer.Write(CURRENT_FORMAT_VERSION);

                // Fixed-size metadata header
                metadata.WriteTo(writer);

                // Compressed payload
                writer.Write(compressedPayload);

                // Checksum
                writer.Write(saveData.expectedChecksum);
            }

            // Atomic rename
            if (File.Exists(filePath))
                File.Delete(filePath);
            File.Move(tempPath, filePath);

            // Log compression stats
            long fileSize = new FileInfo(filePath).Length;
            ArchonLogger.Log($"SaveFileSerializer: Saved {filePath} ({fileSize:N0} bytes, payload compressed to {compressedPayload.Length:N0} bytes)", "core_saveload");
        }

        /// <summary>
        /// Read SaveGameData from disk. Supports both v1 (legacy) and v2 (metadata+compression) formats.
        /// </summary>
        public static SaveGameData ReadFromDisk(string filePath)
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);

            using (MemoryStream stream = new MemoryStream(fileBytes))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                VerifyMagicBytes(reader);

                // Peek format version
                int formatVersion = reader.ReadInt32();

                if (formatVersion >= 1000)
                {
                    return ReadV2(reader, fileBytes, formatVersion);
                }
                else
                {
                    // Legacy v1: the int we just read was the gameVersion string length, not a format marker
                    return ReadV1Legacy(reader, fileBytes, formatVersion);
                }
            }
        }

        /// <summary>
        /// Read v2 format: metadata header + compressed payload + checksum
        /// </summary>
        private static SaveGameData ReadV2(BinaryReader reader, byte[] fileBytes, int formatVersion)
        {
            // Read metadata header
            SaveMetadata metadata = SaveMetadata.ReadFrom(reader);

            // Read compressed payload
            int payloadStart = (int)reader.BaseStream.Position;
            byte[] compressedPayload = reader.ReadBytes(metadata.compressedPayloadSize);

            // Read and verify checksum
            uint storedChecksum = reader.ReadUInt32();
            uint calculatedChecksum = CalculateChecksum(compressedPayload);

            if (storedChecksum != calculatedChecksum)
            {
                ArchonLogger.LogWarning($"SaveFileSerializer: Checksum mismatch! File may be corrupted. " +
                    $"(expected: {storedChecksum:X8}, calculated: {calculatedChecksum:X8})", "core_saveload");
            }

            // Decompress payload
            SaveGameData data = new SaveGameData
            {
                gameVersion = metadata.gameVersion,
                saveFormatVersion = metadata.saveFormatVersion,
                saveName = metadata.saveName,
                saveDateTicks = metadata.saveDateTicks,
                currentTick = metadata.currentTick,
                gameSpeed = metadata.gameSpeed,
                scenarioName = metadata.scenarioName,
                expectedChecksum = storedChecksum
            };

            using (MemoryStream compressedStream = new MemoryStream(compressedPayload))
            using (GZipStream gzip = new GZipStream(compressedStream, CompressionMode.Decompress))
            using (BinaryReader payloadReader = new BinaryReader(gzip))
            {
                ReadSystemData(payloadReader, data);
                ReadCommandLog(payloadReader, data);
            }

            ArchonLogger.Log($"SaveFileSerializer: Loaded v2 save '{data.saveName}' (compressed: {compressedPayload.Length:N0} bytes)", "core_saveload");
            return data;
        }

        /// <summary>
        /// Read v1 legacy format for backward compatibility.
        /// In v1, the file is: Magic(4) + Header(variable) + SystemData + CommandLog + Checksum(4)
        /// We've already read Magic(4) and the first int32 (which was saveFormatVersion in v1 header).
        /// </summary>
        private static SaveGameData ReadV1Legacy(BinaryReader reader, byte[] fileBytes, int saveFormatVersion)
        {
            // Validate checksum (last 4 bytes of file)
            int payloadLength = fileBytes.Length - 4;
            byte[] payloadBytes = new byte[payloadLength];
            Array.Copy(fileBytes, 0, payloadBytes, 0, payloadLength);

            uint storedChecksum = BitConverter.ToUInt32(fileBytes, payloadLength);
            uint calculatedChecksum = CalculateChecksum(payloadBytes);

            if (storedChecksum != calculatedChecksum)
            {
                ArchonLogger.LogWarning($"SaveFileSerializer: v1 checksum mismatch! " +
                    $"(expected: {storedChecksum:X8}, calculated: {calculatedChecksum:X8})", "core_saveload");
            }

            // In v1 header, after magic bytes came:
            // gameVersion(string), saveFormatVersion(int32), saveName(string), ...
            // We already read magic + first int32 (saveFormatVersion).
            // But wait — v1 wrote gameVersion FIRST as a string, then saveFormatVersion.
            // The int we read was actually the string length of gameVersion.
            // We need to re-read from after magic bytes.

            // Rewind to after magic bytes (position 4)
            reader.BaseStream.Position = 4;

            SaveGameData data = new SaveGameData();
            ReadHeaderV1(reader, data);
            ReadSystemData(reader, data);
            ReadCommandLog(reader, data);
            data.expectedChecksum = storedChecksum;

            ArchonLogger.Log($"SaveFileSerializer: Loaded v1 legacy save '{data.saveName}'", "core_saveload");
            return data;
        }

        /// <summary>
        /// Verify version compatibility
        /// </summary>
        public static bool VerifyVersionCompatibility(SaveGameData saveData, string currentVersion)
        {
            if (!saveData.IsCompatibleVersion(currentVersion))
            {
                ArchonLogger.LogWarning($"SaveFileSerializer: Save file version mismatch (save: {saveData.gameVersion}, current: {currentVersion})", "core_saveload");
            }
            return true; // Allow loading anyway for now
        }

        #region Write Operations

        private static void WriteSystemData(BinaryWriter writer, SaveGameData saveData)
        {
            writer.Write(saveData.systemData.Count);

            foreach (var kvp in saveData.systemData)
            {
                SerializationHelper.WriteString(writer, kvp.Key);

                if (kvp.Value is byte[] bytes)
                {
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                }
                else
                {
                    writer.Write(0);
                }
            }
        }

        private static void WriteCommandLog(BinaryWriter writer, SaveGameData saveData)
        {
            writer.Write(saveData.commandLog.Count);
            foreach (var commandBytes in saveData.commandLog)
            {
                writer.Write(commandBytes.Length);
                writer.Write(commandBytes);
            }
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

        /// <summary>
        /// Read v1 header format (variable-length strings)
        /// </summary>
        private static void ReadHeaderV1(BinaryReader reader, SaveGameData data)
        {
            data.gameVersion = SerializationHelper.ReadString(reader);
            data.saveFormatVersion = reader.ReadInt32();
            data.saveName = SerializationHelper.ReadString(reader);
            data.saveDateTicks = reader.ReadInt64();
            data.currentTick = reader.ReadUInt64();
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

        #endregion
    }
}
