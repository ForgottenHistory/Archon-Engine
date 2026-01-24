using System;
using Unity.Collections;
using Core.Systems;

namespace Core.Commands
{
    /// <summary>
    /// Interface for all province state change commands in the deterministic simulation.
    /// Commands are the ONLY way to modify province state in multiplayer games.
    /// All commands must be deterministic and serializable for network synchronization.
    /// </summary>
    public interface IProvinceCommand : IDisposable
    {
        /// <summary>
        /// Unique command type identifier for serialization
        /// Must be consistent across all clients and versions
        /// </summary>
        byte CommandType { get; }

        /// <summary>
        /// Game tick when this command should be executed
        /// Used for command scheduling and rollback
        /// </summary>
        uint ExecutionTick { get; set; }

        /// <summary>
        /// Player who issued this command (for validation)
        /// </summary>
        ushort PlayerID { get; set; }

        /// <summary>
        /// Validate if this command can be executed in the current game state
        /// Must be deterministic - same result across all clients
        /// </summary>
        /// <param name="simulation">Current province system</param>
        /// <returns>True if command can be executed</returns>
        CommandValidationResult Validate(ProvinceSystem provinceSystem);

        /// <summary>
        /// Execute this command, modifying the simulation state
        /// Must be deterministic - same result across all clients
        /// </summary>
        /// <param name="simulation">Simulation to modify</param>
        /// <returns>Execution result with affected provinces</returns>
        CommandExecutionResult Execute(ProvinceSystem provinceSystem);

        /// <summary>
        /// Serialize command to binary format for network transmission
        /// Must be deterministic and compact (target: 8-16 bytes)
        /// </summary>
        /// <param name="buffer">Buffer to write to</param>
        /// <param name="offset">Starting offset in buffer</param>
        /// <returns>Number of bytes written</returns>
        int Serialize(NativeArray<byte> buffer, int offset);

        /// <summary>
        /// Deserialize command from binary format
        /// Must exactly reverse the Serialize operation
        /// </summary>
        /// <param name="buffer">Buffer to read from</param>
        /// <param name="offset">Starting offset in buffer</param>
        /// <returns>Number of bytes read</returns>
        int Deserialize(NativeArray<byte> buffer, int offset);

        /// <summary>
        /// Get the serialized size of this command in bytes
        /// Used for buffer allocation and validation
        /// </summary>
        int GetSerializedSize();

        /// <summary>
        /// Calculate checksum of command data for validation
        /// Used to detect network corruption and ensure determinism
        /// </summary>
        uint GetChecksum();
    }

    /// <summary>
    /// Result of command validation
    /// </summary>
    public struct CommandValidationResult
    {
        public bool IsValid;
        public CommandValidationError Error;
        public string ErrorMessage;

        public static CommandValidationResult Success() =>
            new CommandValidationResult { IsValid = true, Error = CommandValidationError.None };

        public static CommandValidationResult Fail(CommandValidationError error, string message = null) =>
            new CommandValidationResult { IsValid = false, Error = error, ErrorMessage = message };
    }

    /// <summary>
    /// Types of validation errors
    /// </summary>
    public enum CommandValidationError : byte
    {
        None = 0,
        InvalidProvince = 1,
        InsufficientPermissions = 2,
        InvalidGameState = 3,
        InvalidParameters = 4,
        ResourceConstraints = 5,
        TimingViolation = 6,
        ChecksumMismatch = 7
    }

    /// <summary>
    /// Result of command execution
    /// </summary>
    public struct CommandExecutionResult
    {
        public bool IsSuccess;
        public NativeList<ushort> AffectedProvinces;
        public uint NewStateChecksum;

        public void Dispose()
        {
            if (AffectedProvinces.IsCreated)
                AffectedProvinces.Dispose();
        }
    }

    /// <summary>
    /// Command type identifiers - must remain stable across versions
    /// Used for serialization and network protocol
    /// </summary>
    public static class CommandTypes
    {
        public const byte ChangeOwner = 1;
        public const byte ChangeController = 2;
        public const byte ChangeDevelopment = 3;
        public const byte ChangeFortification = 4;
        public const byte SetFlags = 5;
        public const byte BatchUpdate = 6;

        // Reserve 1-50 for basic province commands
        // Reserve 51-100 for country commands
        // Reserve 101-150 for diplomacy commands
        // Reserve 151-200 for war commands
        // Reserve 201-255 for future expansion
    }
}