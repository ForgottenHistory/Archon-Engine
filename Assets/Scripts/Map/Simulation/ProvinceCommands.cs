using Unity.Collections;
using UnityEngine;
using System;

namespace Map.Simulation
{
    /// <summary>
    /// Command pattern interface for deterministic province state changes
    /// All simulation modifications MUST go through commands for multiplayer compatibility
    /// </summary>
    public interface IProvinceCommand
    {
        /// <summary>
        /// Execute the command on the simulation
        /// MUST be deterministic - same input always produces same output
        /// </summary>
        bool Execute(ProvinceSimulation simulation);

        /// <summary>
        /// Validate command before execution
        /// </summary>
        bool Validate(ProvinceSimulation simulation, out string errorMessage);

        /// <summary>
        /// Serialize command for networking (must be compact)
        /// </summary>
        byte[] Serialize();

        /// <summary>
        /// Get command type for deserialization
        /// </summary>
        ProvinceCommandType GetCommandType();

        /// <summary>
        /// Get tick when command should be executed
        /// </summary>
        uint GetExecutionTick();

        /// <summary>
        /// Get provinces affected by this command (for dirty tracking)
        /// </summary>
        ushort[] GetAffectedProvinces();
    }

    /// <summary>
    /// Command types for serialization
    /// </summary>
    public enum ProvinceCommandType : byte
    {
        ChangeOwner = 1,
        ChangeController = 2,
        ChangeDevelopment = 3,
        SetFlag = 4,
        BatchUpdate = 5         // Multiple changes in one command
    }

    /// <summary>
    /// Command to change province ownership
    /// Network size: 9 bytes (type + tick + provinceID + ownerID)
    /// </summary>
    public struct ChangeOwnerCommand : IProvinceCommand
    {
        public uint executionTick;
        public ushort provinceID;
        public ushort newOwnerID;

        public ChangeOwnerCommand(uint tick, ushort provinceID, ushort newOwnerID)
        {
            this.executionTick = tick;
            this.provinceID = provinceID;
            this.newOwnerID = newOwnerID;
        }

        public bool Execute(ProvinceSimulation simulation)
        {
            return simulation.SetProvinceOwner(provinceID, newOwnerID);
        }

        public bool Validate(ProvinceSimulation simulation, out string errorMessage)
        {
            errorMessage = null;

            if (provinceID == 0)
            {
                errorMessage = "Cannot change ownership of ocean province";
                return false;
            }

            var currentState = simulation.GetProvinceState(provinceID);
            if (currentState.ownerID == 0 && currentState.terrain == (byte)TerrainType.Ocean)
            {
                errorMessage = "Province does not exist or is ocean";
                return false;
            }

            return true;
        }

        public byte[] Serialize()
        {
            byte[] buffer = new byte[9];

            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    *ptr = (byte)ProvinceCommandType.ChangeOwner;
                    *(uint*)(ptr + 1) = executionTick;
                    *(ushort*)(ptr + 5) = provinceID;
                    *(ushort*)(ptr + 7) = newOwnerID;
                }
            }

            return buffer;
        }

        public ProvinceCommandType GetCommandType() => ProvinceCommandType.ChangeOwner;
        public uint GetExecutionTick() => executionTick;
        public ushort[] GetAffectedProvinces() => new[] { provinceID };
    }

    /// <summary>
    /// Command to change province controller (for occupation)
    /// Network size: 9 bytes
    /// </summary>
    public struct ChangeControllerCommand : IProvinceCommand
    {
        public uint executionTick;
        public ushort provinceID;
        public ushort newControllerID;

        public ChangeControllerCommand(uint tick, ushort provinceID, ushort newControllerID)
        {
            this.executionTick = tick;
            this.provinceID = provinceID;
            this.newControllerID = newControllerID;
        }

        public bool Execute(ProvinceSimulation simulation)
        {
            return simulation.SetProvinceController(provinceID, newControllerID);
        }

        public bool Validate(ProvinceSimulation simulation, out string errorMessage)
        {
            errorMessage = null;

            if (provinceID == 0)
            {
                errorMessage = "Cannot change controller of ocean province";
                return false;
            }

            var currentState = simulation.GetProvinceState(provinceID);
            if (!currentState.IsOwned)
            {
                errorMessage = "Cannot control unowned province";
                return false;
            }

            return true;
        }

        public byte[] Serialize()
        {
            byte[] buffer = new byte[9];

            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    *ptr = (byte)ProvinceCommandType.ChangeController;
                    *(uint*)(ptr + 1) = executionTick;
                    *(ushort*)(ptr + 5) = provinceID;
                    *(ushort*)(ptr + 7) = newControllerID;
                }
            }

            return buffer;
        }

        public ProvinceCommandType GetCommandType() => ProvinceCommandType.ChangeController;
        public uint GetExecutionTick() => executionTick;
        public ushort[] GetAffectedProvinces() => new[] { provinceID };
    }

    /// <summary>
    /// Command to change province development
    /// Network size: 8 bytes
    /// </summary>
    public struct ChangeDevelopmentCommand : IProvinceCommand
    {
        public uint executionTick;
        public ushort provinceID;
        public byte newDevelopment;

        public ChangeDevelopmentCommand(uint tick, ushort provinceID, byte newDevelopment)
        {
            this.executionTick = tick;
            this.provinceID = provinceID;
            this.newDevelopment = newDevelopment;
        }

        public bool Execute(ProvinceSimulation simulation)
        {
            return simulation.SetProvinceDevelopment(provinceID, newDevelopment);
        }

        public bool Validate(ProvinceSimulation simulation, out string errorMessage)
        {
            errorMessage = null;

            if (provinceID == 0)
            {
                errorMessage = "Cannot develop ocean province";
                return false;
            }

            var currentState = simulation.GetProvinceState(provinceID);
            if (currentState.terrain == (byte)TerrainType.Ocean)
            {
                errorMessage = "Cannot develop ocean terrain";
                return false;
            }

            if (currentState.terrain == (byte)TerrainType.Mountain && newDevelopment > 100)
            {
                errorMessage = "Mountains cannot be highly developed";
                return false;
            }

            return true;
        }

        public byte[] Serialize()
        {
            byte[] buffer = new byte[8];

            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    *ptr = (byte)ProvinceCommandType.ChangeDevelopment;
                    *(uint*)(ptr + 1) = executionTick;
                    *(ushort*)(ptr + 5) = provinceID;
                    *(ptr + 7) = newDevelopment;
                }
            }

            return buffer;
        }

        public ProvinceCommandType GetCommandType() => ProvinceCommandType.ChangeDevelopment;
        public uint GetExecutionTick() => executionTick;
        public ushort[] GetAffectedProvinces() => new[] { provinceID };
    }

    /// <summary>
    /// Command to set or clear province flags
    /// Network size: 9 bytes
    /// </summary>
    public struct SetFlagCommand : IProvinceCommand
    {
        public uint executionTick;
        public ushort provinceID;
        public ProvinceFlags flag;
        public bool value;

        public SetFlagCommand(uint tick, ushort provinceID, ProvinceFlags flag, bool value)
        {
            this.executionTick = tick;
            this.provinceID = provinceID;
            this.flag = flag;
            this.value = value;
        }

        public bool Execute(ProvinceSimulation simulation)
        {
            return simulation.SetProvinceFlag(provinceID, flag, value);
        }

        public bool Validate(ProvinceSimulation simulation, out string errorMessage)
        {
            errorMessage = null;

            if (provinceID == 0)
            {
                errorMessage = "Cannot set flags on ocean province";
                return false;
            }

            // Validate flag-specific rules
            if (flag == ProvinceFlags.IsCapital && value)
            {
                var state = simulation.GetProvinceState(provinceID);
                if (!state.IsOwned)
                {
                    errorMessage = "Capital must be owned";
                    return false;
                }
            }

            return true;
        }

        public byte[] Serialize()
        {
            byte[] buffer = new byte[9];

            unsafe
            {
                fixed (byte* ptr = buffer)
                {
                    *ptr = (byte)ProvinceCommandType.SetFlag;
                    *(uint*)(ptr + 1) = executionTick;
                    *(ushort*)(ptr + 5) = provinceID;
                    *(ptr + 7) = (byte)flag;
                    *(ptr + 8) = (byte)(value ? 1 : 0);
                }
            }

            return buffer;
        }

        public ProvinceCommandType GetCommandType() => ProvinceCommandType.SetFlag;
        public uint GetExecutionTick() => executionTick;
        public ushort[] GetAffectedProvinces() => new[] { provinceID };
    }

    /// <summary>
    /// Command deserializer for network packets
    /// </summary>
    public static class ProvinceCommandSerializer
    {
        /// <summary>
        /// Deserialize command from network data
        /// </summary>
        public static IProvinceCommand Deserialize(byte[] data)
        {
            if (data == null || data.Length < 1)
                throw new ArgumentException("Invalid command data");

            var commandType = (ProvinceCommandType)data[0];

            return commandType switch
            {
                ProvinceCommandType.ChangeOwner => DeserializeChangeOwner(data),
                ProvinceCommandType.ChangeController => DeserializeChangeController(data),
                ProvinceCommandType.ChangeDevelopment => DeserializeChangeDevelopment(data),
                ProvinceCommandType.SetFlag => DeserializeSetFlag(data),
                _ => throw new ArgumentException($"Unknown command type: {commandType}")
            };
        }

        private static ChangeOwnerCommand DeserializeChangeOwner(byte[] data)
        {
            if (data.Length != 9)
                throw new ArgumentException("Invalid ChangeOwner command data");

            unsafe
            {
                fixed (byte* ptr = data)
                {
                    return new ChangeOwnerCommand(
                        *(uint*)(ptr + 1),      // executionTick
                        *(ushort*)(ptr + 5),    // provinceID
                        *(ushort*)(ptr + 7)     // newOwnerID
                    );
                }
            }
        }

        private static ChangeControllerCommand DeserializeChangeController(byte[] data)
        {
            if (data.Length != 9)
                throw new ArgumentException("Invalid ChangeController command data");

            unsafe
            {
                fixed (byte* ptr = data)
                {
                    return new ChangeControllerCommand(
                        *(uint*)(ptr + 1),      // executionTick
                        *(ushort*)(ptr + 5),    // provinceID
                        *(ushort*)(ptr + 7)     // newControllerID
                    );
                }
            }
        }

        private static ChangeDevelopmentCommand DeserializeChangeDevelopment(byte[] data)
        {
            if (data.Length != 8)
                throw new ArgumentException("Invalid ChangeDevelopment command data");

            unsafe
            {
                fixed (byte* ptr = data)
                {
                    return new ChangeDevelopmentCommand(
                        *(uint*)(ptr + 1),      // executionTick
                        *(ushort*)(ptr + 5),    // provinceID
                        *(ptr + 7)              // newDevelopment
                    );
                }
            }
        }

        private static SetFlagCommand DeserializeSetFlag(byte[] data)
        {
            if (data.Length != 9)
                throw new ArgumentException("Invalid SetFlag command data");

            unsafe
            {
                fixed (byte* ptr = data)
                {
                    return new SetFlagCommand(
                        *(uint*)(ptr + 1),          // executionTick
                        *(ushort*)(ptr + 5),        // provinceID
                        (ProvinceFlags)(*(ptr + 7)), // flag
                        *(ptr + 8) != 0             // value
                    );
                }
            }
        }

        /// <summary>
        /// Get command size for bandwidth calculations
        /// </summary>
        public static int GetCommandSize(ProvinceCommandType commandType)
        {
            return commandType switch
            {
                ProvinceCommandType.ChangeOwner => 9,
                ProvinceCommandType.ChangeController => 9,
                ProvinceCommandType.ChangeDevelopment => 8,
                ProvinceCommandType.SetFlag => 9,
                _ => 0
            };
        }
    }
}