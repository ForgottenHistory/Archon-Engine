using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Core.Commands
{
    /// <summary>
    /// Handles serialization and deserialization of commands for network transmission
    /// Supports command batching and compression for network efficiency
    /// </summary>
    public static class CommandSerializer
    {
        private static readonly Dictionary<byte, Func<IProvinceCommand>> commandFactories;

        static CommandSerializer()
        {
            commandFactories = new Dictionary<byte, Func<IProvinceCommand>>
            {
                { CommandTypes.ChangeOwner, () => new ChangeOwnerCommand() },
                // Add other command types here as they are implemented
            };
        }

        /// <summary>
        /// Serialize a single command to binary format
        /// </summary>
        public static SerializationResult SerializeCommand(IProvinceCommand command, NativeArray<byte> buffer, int offset)
        {
            if (command == null)
                return SerializationResult.CreateFailure("Command is null");

            if (!buffer.IsCreated)
                return SerializationResult.CreateFailure("Buffer is not created");

            try
            {
                int bytesWritten = command.Serialize(buffer, offset);
                return SerializationResult.CreateSuccess(bytesWritten);
            }
            catch (Exception ex)
            {
                return SerializationResult.CreateFailure($"Serialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Deserialize a single command from binary format
        /// </summary>
        public static DeserializationResult DeserializeCommand(NativeArray<byte> buffer, int offset)
        {
            if (!buffer.IsCreated)
                return DeserializationResult.CreateFailure("Buffer is not created");

            if (offset >= buffer.Length)
                return DeserializationResult.CreateFailure("Offset beyond buffer length");

            try
            {
                // Read command type
                byte commandType = buffer[offset];

                if (!commandFactories.TryGetValue(commandType, out var factory))
                {
                    return DeserializationResult.CreateFailure($"Unknown command type: {commandType}");
                }

                // Create command instance
                var command = factory();

                // Deserialize the command
                int bytesRead = command.Deserialize(buffer, offset);

                return DeserializationResult.CreateSuccess(command, bytesRead);
            }
            catch (Exception ex)
            {
                return DeserializationResult.CreateFailure($"Deserialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Serialize multiple commands into a batch with header
        /// Format: [BatchHeader] [Command1] [Command2] ... [CommandN]
        /// </summary>
        public static BatchSerializationResult SerializeCommandBatch(
            IList<IProvinceCommand> commands,
            uint tick,
            ushort playerID,
            Allocator allocator = Allocator.Temp)
        {
            if (commands == null || commands.Count == 0)
                return BatchSerializationResult.CreateFailure("No commands to serialize");

            try
            {
                // Calculate total size needed
                int totalSize = BatchHeader.SizeInBytes; // Header size
                for (int i = 0; i < commands.Count; i++)
                {
                    totalSize += commands[i].GetSerializedSize();
                }

                // Allocate buffer
                var buffer = new NativeArray<byte>(totalSize, allocator);
                int offset = 0;

                // Write batch header
                var header = new BatchHeader
                {
                    CommandCount = (ushort)commands.Count,
                    Tick = tick,
                    PlayerID = playerID,
                    TotalSize = (ushort)totalSize,
                    Checksum = 0 // Will calculate after serializing commands
                };

                uint batchChecksum = 0;

                // Serialize commands
                int commandDataStart = BatchHeader.SizeInBytes;
                offset = commandDataStart;

                for (int i = 0; i < commands.Count; i++)
                {
                    int bytesWritten = commands[i].Serialize(buffer, offset);
                    offset += bytesWritten;

                    // Add to batch checksum
                    batchChecksum ^= commands[i].GetChecksum();
                }

                // Update header with final checksum
                header.Checksum = batchChecksum;
                header.Serialize(buffer, 0);

                return BatchSerializationResult.CreateSuccess(buffer, totalSize);
            }
            catch (Exception ex)
            {
                return BatchSerializationResult.CreateFailure($"Batch serialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Deserialize a command batch
        /// </summary>
        public static BatchDeserializationResult DeserializeCommandBatch(NativeArray<byte> buffer)
        {
            if (!buffer.IsCreated)
                return BatchDeserializationResult.CreateFailure("Buffer is not created");

            try
            {
                // Deserialize header
                var header = new BatchHeader();
                header.Deserialize(buffer, 0);

                // Validate buffer size
                if (buffer.Length < header.TotalSize)
                {
                    return BatchDeserializationResult.CreateFailure($"Buffer too small: expected {header.TotalSize}, got {buffer.Length}");
                }

                // Deserialize commands
                var commands = new List<IProvinceCommand>(header.CommandCount);
                int offset = BatchHeader.SizeInBytes;
                uint calculatedChecksum = 0;

                for (int i = 0; i < header.CommandCount; i++)
                {
                    var result = DeserializeCommand(buffer, offset);
                    if (!result.Success)
                    {
                        // Cleanup partially deserialized commands
                        foreach (var cmd in commands)
                            cmd?.Dispose();
                        return BatchDeserializationResult.CreateFailure($"Failed to deserialize command {i}: {result.ErrorMessage}");
                    }

                    commands.Add(result.Command);
                    offset += result.BytesRead;
                    calculatedChecksum ^= result.Command.GetChecksum();
                }

                // Validate checksum
                if (calculatedChecksum != header.Checksum)
                {
                    foreach (var cmd in commands)
                        cmd?.Dispose();
                    return BatchDeserializationResult.CreateFailure($"Checksum mismatch: expected {header.Checksum:X8}, got {calculatedChecksum:X8}");
                }

                return BatchDeserializationResult.CreateSuccess(commands, header);
            }
            catch (Exception ex)
            {
                return BatchDeserializationResult.CreateFailure($"Batch deserialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Calculate the size needed to serialize a batch of commands
        /// </summary>
        public static int CalculateBatchSize(IList<IProvinceCommand> commands)
        {
            int size = BatchHeader.SizeInBytes;
            for (int i = 0; i < commands.Count; i++)
            {
                size += commands[i].GetSerializedSize();
            }
            return size;
        }

        /// <summary>
        /// Register a new command type factory
        /// </summary>
        public static void RegisterCommandType(byte commandType, Func<IProvinceCommand> factory)
        {
            if (commandFactories.ContainsKey(commandType))
            {
                Debug.LogWarning($"Command type {commandType} is already registered, overwriting");
            }
            commandFactories[commandType] = factory;
        }
    }

    /// <summary>
    /// Header for command batches - fixed 12 bytes
    /// </summary>
    public struct BatchHeader
    {
        public ushort CommandCount;    // Number of commands in batch
        public uint Tick;             // Tick when commands should execute
        public ushort PlayerID;       // Player who sent this batch
        public ushort TotalSize;      // Total size of batch including header
        public uint Checksum;         // Checksum of all commands in batch

        public const int SizeInBytes = 14; // 2+4+2+2+4 = 14 bytes

        public void Serialize(NativeArray<byte> buffer, int offset)
        {
            if (buffer.Length < offset + SizeInBytes)
                throw new ArgumentException("Buffer too small for BatchHeader");

            int pos = offset;

            // CommandCount (2 bytes)
            var countBytes = BitConverter.GetBytes(CommandCount);
            buffer[pos++] = countBytes[0];
            buffer[pos++] = countBytes[1];

            // Tick (4 bytes)
            var tickBytes = BitConverter.GetBytes(Tick);
            buffer[pos++] = tickBytes[0];
            buffer[pos++] = tickBytes[1];
            buffer[pos++] = tickBytes[2];
            buffer[pos++] = tickBytes[3];

            // PlayerID (2 bytes)
            var playerBytes = BitConverter.GetBytes(PlayerID);
            buffer[pos++] = playerBytes[0];
            buffer[pos++] = playerBytes[1];

            // TotalSize (2 bytes)
            var sizeBytes = BitConverter.GetBytes(TotalSize);
            buffer[pos++] = sizeBytes[0];
            buffer[pos++] = sizeBytes[1];

            // Checksum (4 bytes)
            var checksumBytes = BitConverter.GetBytes(Checksum);
            buffer[pos++] = checksumBytes[0];
            buffer[pos++] = checksumBytes[1];
            buffer[pos++] = checksumBytes[2];
            buffer[pos++] = checksumBytes[3];
        }

        public void Deserialize(NativeArray<byte> buffer, int offset)
        {
            if (buffer.Length < offset + SizeInBytes)
                throw new ArgumentException("Buffer too small for BatchHeader");

            int pos = offset;

            // CommandCount (2 bytes)
            CommandCount = (ushort)(buffer[pos] | (buffer[pos + 1] << 8));
            pos += 2;

            // Tick (4 bytes)
            Tick = (uint)(buffer[pos] | (buffer[pos + 1] << 8) | (buffer[pos + 2] << 16) | (buffer[pos + 3] << 24));
            pos += 4;

            // PlayerID (2 bytes)
            PlayerID = (ushort)(buffer[pos] | (buffer[pos + 1] << 8));
            pos += 2;

            // TotalSize (2 bytes)
            TotalSize = (ushort)(buffer[pos] | (buffer[pos + 1] << 8));
            pos += 2;

            // Checksum (4 bytes)
            Checksum = (uint)(buffer[pos] | (buffer[pos + 1] << 8) | (buffer[pos + 2] << 16) | (buffer[pos + 3] << 24));
        }
    }

    // Result types for serialization operations
    public struct SerializationResult
    {
        public bool Success;
        public string ErrorMessage;
        public int BytesWritten;

        public static SerializationResult CreateSuccess(int bytes) => new SerializationResult { Success = true, BytesWritten = bytes };
        public static SerializationResult CreateFailure(string error) => new SerializationResult { Success = false, ErrorMessage = error };
    }

    public struct DeserializationResult
    {
        public bool Success;
        public string ErrorMessage;
        public IProvinceCommand Command;
        public int BytesRead;

        public static DeserializationResult CreateSuccess(IProvinceCommand command, int bytes) =>
            new DeserializationResult { Success = true, Command = command, BytesRead = bytes };
        public static DeserializationResult CreateFailure(string error) =>
            new DeserializationResult { Success = false, ErrorMessage = error };
    }

    public struct BatchSerializationResult
    {
        public bool Success;
        public string ErrorMessage;
        public NativeArray<byte> Buffer;
        public int TotalSize;

        public static BatchSerializationResult CreateSuccess(NativeArray<byte> buffer, int size) =>
            new BatchSerializationResult { Success = true, Buffer = buffer, TotalSize = size };
        public static BatchSerializationResult CreateFailure(string error) =>
            new BatchSerializationResult { Success = false, ErrorMessage = error };
    }

    public struct BatchDeserializationResult
    {
        public bool Success;
        public string ErrorMessage;
        public List<IProvinceCommand> Commands;
        public BatchHeader Header;

        public static BatchDeserializationResult CreateSuccess(List<IProvinceCommand> commands, BatchHeader header) =>
            new BatchDeserializationResult { Success = true, Commands = commands, Header = header };
        public static BatchDeserializationResult CreateFailure(string error) =>
            new BatchDeserializationResult { Success = false, ErrorMessage = error };
    }
}