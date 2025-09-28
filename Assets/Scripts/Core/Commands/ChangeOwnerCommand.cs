using System;
using Unity.Collections;
using Core.Systems;

namespace Core.Commands
{
    /// <summary>
    /// Command to change province ownership
    /// Fixed 12-byte serialization format for network efficiency
    /// </summary>
    public struct ChangeOwnerCommand : IProvinceCommand
    {
        public uint executionTick;
        public ushort playerID;
        public ushort provinceID;
        public ushort newOwnerID;
        public ushort newControllerID;  // Usually same as owner, but can differ during occupation

        public byte CommandType => CommandTypes.ChangeOwner;

        public uint ExecutionTick
        {
            get => executionTick;
            set => executionTick = value;
        }

        public ushort PlayerID
        {
            get => playerID;
            set => playerID = value;
        }

        public ChangeOwnerCommand(uint tick, ushort player, ushort province, ushort newOwner, ushort newController = 0)
        {
            executionTick = tick;
            playerID = player;
            provinceID = province;
            newOwnerID = newOwner;
            newControllerID = newController == 0 ? newOwner : newController;  // Default controller = owner
        }

        public CommandValidationResult Validate(ProvinceSimulation simulation)
        {
            // Check if province exists
            if (!simulation.HasProvince(provinceID))
            {
                return CommandValidationResult.Fail(CommandValidationError.InvalidProvince,
                    $"Province {provinceID} does not exist");
            }

            // Check if new owner ID is valid (0 = no owner is allowed)
            if (newOwnerID > 255)
            {
                return CommandValidationResult.Fail(CommandValidationError.InvalidParameters,
                    $"Owner ID {newOwnerID} exceeds maximum (255)");
            }

            // Check if new controller ID is valid
            if (newControllerID > 255)
            {
                return CommandValidationResult.Fail(CommandValidationError.InvalidParameters,
                    $"Controller ID {newControllerID} exceeds maximum (255)");
            }

            // Additional validation could include:
            // - Player permissions to change this province
            // - Diplomatic restrictions
            // - War state requirements
            // For now, allow all changes for testing

            return CommandValidationResult.Success();
        }

        public CommandExecutionResult Execute(ProvinceSimulation simulation)
        {
            var result = new CommandExecutionResult();
            result.AffectedProvinces = new NativeList<ushort>(1, Allocator.Temp);

            try
            {
                // Get current state
                var currentState = simulation.GetProvinceState(provinceID);

                // Apply changes
                currentState.ownerID = newOwnerID;
                currentState.controllerID = newControllerID;

                // Update simulation
                simulation.SetProvinceState(provinceID, currentState);

                result.AffectedProvinces.Add(provinceID);
                result.Success = true;
                result.NewStateChecksum = simulation.GetStateChecksum();

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                if (result.AffectedProvinces.IsCreated)
                    result.AffectedProvinces.Dispose();

                // In a real implementation, log the exception
                DominionLogger.LogError($"Command execution failed: {ex.Message}");
                return result;
            }
        }

        public int Serialize(NativeArray<byte> buffer, int offset)
        {
            if (buffer.Length < offset + GetSerializedSize())
                throw new ArgumentException("Buffer too small for serialization");

            int currentOffset = offset;

            // Write fields in deterministic order (12 bytes total)
            // Format: [CommandType:1][ExecutionTick:4][PlayerID:2][ProvinceID:2][NewOwnerID:2][NewControllerID:2][Checksum:1]
            buffer[currentOffset++] = CommandType;

            // ExecutionTick (4 bytes, little-endian)
            var tickBytes = BitConverter.GetBytes(executionTick);
            for (int i = 0; i < 4; i++)
                buffer[currentOffset++] = tickBytes[i];

            // PlayerID (2 bytes, little-endian)
            var playerBytes = BitConverter.GetBytes(playerID);
            buffer[currentOffset++] = playerBytes[0];
            buffer[currentOffset++] = playerBytes[1];

            // ProvinceID (2 bytes, little-endian)
            var provinceBytes = BitConverter.GetBytes(provinceID);
            buffer[currentOffset++] = provinceBytes[0];
            buffer[currentOffset++] = provinceBytes[1];

            // NewOwnerID (2 bytes, little-endian)
            var ownerBytes = BitConverter.GetBytes(newOwnerID);
            buffer[currentOffset++] = ownerBytes[0];
            buffer[currentOffset++] = ownerBytes[1];

            // NewControllerID (2 bytes, little-endian)
            var controllerBytes = BitConverter.GetBytes(newControllerID);
            buffer[currentOffset++] = controllerBytes[0];
            buffer[currentOffset++] = controllerBytes[1];

            return GetSerializedSize();
        }

        public int Deserialize(NativeArray<byte> buffer, int offset)
        {
            if (buffer.Length < offset + GetSerializedSize())
                throw new ArgumentException("Buffer too small for deserialization");

            int currentOffset = offset;

            // Read CommandType and validate
            byte commandType = buffer[currentOffset++];
            if (commandType != CommandType)
                throw new ArgumentException($"Invalid command type: expected {CommandType}, got {commandType}");

            // Read ExecutionTick (4 bytes)
            var tickBytes = new byte[4];
            for (int i = 0; i < 4; i++)
                tickBytes[i] = buffer[currentOffset++];
            executionTick = BitConverter.ToUInt32(tickBytes, 0);

            // Read PlayerID (2 bytes)
            playerID = (ushort)(buffer[currentOffset] | (buffer[currentOffset + 1] << 8));
            currentOffset += 2;

            // Read ProvinceID (2 bytes)
            provinceID = (ushort)(buffer[currentOffset] | (buffer[currentOffset + 1] << 8));
            currentOffset += 2;

            // Read NewOwnerID (2 bytes)
            newOwnerID = (ushort)(buffer[currentOffset] | (buffer[currentOffset + 1] << 8));
            currentOffset += 2;

            // Read NewControllerID (2 bytes)
            newControllerID = (ushort)(buffer[currentOffset] | (buffer[currentOffset + 1] << 8));
            currentOffset += 2;

            return GetSerializedSize();
        }

        public int GetSerializedSize()
        {
            // CommandType(1) + ExecutionTick(4) + PlayerID(2) + ProvinceID(2) + NewOwnerID(2) + NewControllerID(2) = 13 bytes
            return 13;
        }

        public uint GetChecksum()
        {
            // Simple checksum for validation - in production, use better algorithm
            uint checksum = 0;
            checksum ^= CommandType;
            checksum ^= executionTick;
            checksum ^= (uint)playerID << 16;
            checksum ^= (uint)provinceID << 8;
            checksum ^= newOwnerID;
            checksum ^= (uint)newControllerID << 24;
            return checksum;
        }

        public void Dispose()
        {
            // Nothing to dispose for this struct
        }

        public override string ToString()
        {
            return $"ChangeOwner[Tick:{executionTick}, Player:{playerID}, Province:{provinceID}, NewOwner:{newOwnerID}, NewController:{newControllerID}]";
        }
    }
}