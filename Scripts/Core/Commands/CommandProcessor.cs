using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Core.Systems;
using Core.Network;

namespace Core.Commands
{
    /// <summary>
    /// Processes and executes commands in deterministic order
    /// Ensures multiplayer consistency through command validation and execution
    /// </summary>
    public class CommandProcessor : IDisposable
    {
        private readonly ProvinceSystem provinceSystem;
        private readonly Queue<IProvinceCommand> pendingCommands;
        private readonly List<CommandExecutionRecord> executionHistory;
        private uint currentTick;
        private bool isDisposed;

        // Network bridge for multiplayer (null for single-player)
        private INetworkBridge networkBridge;

        // Statistics for monitoring
        private int commandsExecuted;
        private int commandsRejected;
        private int validationFailures;

        public CommandProcessor(ProvinceSystem provinceSystem)
        {
            this.provinceSystem = provinceSystem ?? throw new ArgumentNullException(nameof(provinceSystem));
            this.pendingCommands = new Queue<IProvinceCommand>();
            this.executionHistory = new List<CommandExecutionRecord>();
            this.currentTick = 0;
        }

        /// <summary>
        /// Current game tick
        /// </summary>
        public uint CurrentTick => currentTick;

        /// <summary>
        /// Number of pending commands
        /// </summary>
        public int PendingCommandCount => pendingCommands.Count;

        /// <summary>
        /// Whether we are in a multiplayer session.
        /// </summary>
        public bool IsMultiplayer => networkBridge?.IsConnected ?? false;

        /// <summary>
        /// Whether we are the authoritative host (or single-player).
        /// </summary>
        public bool IsAuthoritative => networkBridge == null || networkBridge.IsHost;

        /// <summary>
        /// Set the network bridge for multiplayer.
        /// Pass null for single-player mode.
        /// </summary>
        public void SetNetworkBridge(INetworkBridge bridge)
        {
            // Unsubscribe from old bridge
            if (networkBridge != null)
            {
                networkBridge.OnCommandReceived -= HandleRemoteCommand;
            }

            networkBridge = bridge;

            // Subscribe to new bridge
            if (networkBridge != null)
            {
                networkBridge.OnCommandReceived += HandleRemoteCommand;
                ArchonLogger.Log("Network bridge attached to CommandProcessor", "core_commands");
            }
        }

        private void HandleRemoteCommand(int peerId, byte[] commandData, uint tick)
        {
            // TODO: Deserialize and submit command
            // This requires a command serialization system
            ArchonLogger.Log($"Received remote command from peer {peerId} ({commandData.Length} bytes)", "core_commands");
        }

        /// <summary>
        /// Add a command to be processed
        /// Commands are validated immediately but executed later
        /// </summary>
        public CommandSubmissionResult SubmitCommand(IProvinceCommand command)
        {
            if (isDisposed)
                return CommandSubmissionResult.Failure("CommandProcessor is disposed");

            if (command == null)
                return CommandSubmissionResult.Failure("Command cannot be null");

            // Validate command structure
            try
            {
                var checksum = command.GetChecksum();
                var serializedSize = command.GetSerializedSize();

                if (serializedSize <= 0 || serializedSize > 256)
                {
                    return CommandSubmissionResult.Failure($"Invalid serialized size: {serializedSize}");
                }
            }
            catch (Exception ex)
            {
                return CommandSubmissionResult.Failure($"Command structure validation failed: {ex.Message}");
            }

            // Pre-validate against current game state
            var validationResult = command.Validate(provinceSystem);
            if (!validationResult.IsValid)
            {
                validationFailures++;
                return CommandSubmissionResult.Failure($"Validation failed: {validationResult.ErrorMessage}");
            }

            // Set execution tick if not set
            if (command.ExecutionTick <= currentTick)
            {
                command.ExecutionTick = currentTick + 1;
            }

            // Multiplayer: clients send to host instead of executing locally
            if (IsMultiplayer && !IsAuthoritative)
            {
                // Serialize and send to host
                var commandData = SerializeCommand(command);
                networkBridge.SendCommandToHost(commandData, command.ExecutionTick);
                command.Dispose();
                return CommandSubmissionResult.Success();
            }

            pendingCommands.Enqueue(command);
            return CommandSubmissionResult.Success();
        }

        /// <summary>
        /// Serialize a command for network transmission.
        /// </summary>
        private byte[] SerializeCommand(IProvinceCommand command)
        {
            int size = command.GetSerializedSize();
            var buffer = new byte[size + 4]; // +4 for command type header

            // Write command type
            buffer[0] = command.CommandType;
            buffer[1] = (byte)(command.PlayerID & 0xFF);
            buffer[2] = (byte)(command.PlayerID >> 8);
            buffer[3] = 0; // Reserved

            // Write command data
            var commandBuffer = new NativeArray<byte>(size, Allocator.Temp);
            command.Serialize(commandBuffer, 0);
            NativeArray<byte>.Copy(commandBuffer, 0, buffer, 4, size);
            commandBuffer.Dispose();

            return buffer;
        }

        /// <summary>
        /// Process all commands scheduled for the current tick
        /// Should be called once per game tick in deterministic order
        /// </summary>
        public TickProcessingResult ProcessTick()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(CommandProcessor));

            currentTick++;

            var result = new TickProcessingResult
            {
                Tick = currentTick,
                CommandsProcessed = 0,
                CommandsExecuted = 0,
                CommandsRejected = 0,
                AffectedProvinces = new List<ushort>()
            };

            // Process all commands scheduled for this tick
            var commandsToProcess = new List<IProvinceCommand>();
            var remainingCommands = new Queue<IProvinceCommand>();

            // Separate commands by execution tick
            while (pendingCommands.Count > 0)
            {
                var command = pendingCommands.Dequeue();
                if (command.ExecutionTick == currentTick)
                {
                    commandsToProcess.Add(command);
                }
                else if (command.ExecutionTick > currentTick)
                {
                    remainingCommands.Enqueue(command);
                }
                else
                {
                    // Command is too late - reject it
                    ArchonLogger.LogWarning($"Rejecting late command: {command} (current tick: {currentTick})", "core_commands");
                    command.Dispose();
                    result.CommandsRejected++;
                }
            }

            // Restore remaining commands
            while (remainingCommands.Count > 0)
            {
                pendingCommands.Enqueue(remainingCommands.Dequeue());
            }

            result.CommandsProcessed = commandsToProcess.Count;

            // Execute commands in deterministic order (sorted by command type, then by player ID)
            commandsToProcess.Sort((a, b) =>
            {
                int typeComparison = a.CommandType.CompareTo(b.CommandType);
                if (typeComparison != 0) return typeComparison;
                return a.PlayerID.CompareTo(b.PlayerID);
            });

            // Execute each command
            foreach (var command in commandsToProcess)
            {
                try
                {
                    var executionResult = ExecuteCommand(command);
                    if (executionResult.IsSuccess)
                    {
                        result.CommandsExecuted++;

                        // Track affected provinces
                        if (executionResult.AffectedProvinces.IsCreated)
                        {
                            for (int i = 0; i < executionResult.AffectedProvinces.Length; i++)
                            {
                                if (!result.AffectedProvinces.Contains(executionResult.AffectedProvinces[i]))
                                {
                                    result.AffectedProvinces.Add(executionResult.AffectedProvinces[i]);
                                }
                            }
                        }

                        // Record execution for history
                        var record = new CommandExecutionRecord
                        {
                            Tick = currentTick,
                            CommandType = command.CommandType,
                            PlayerID = command.PlayerID,
                            Checksum = command.GetChecksum(),
                            StateChecksumAfter = executionResult.NewStateChecksum
                        };
                        executionHistory.Add(record);

                        // Multiplayer: host broadcasts executed command to all clients
                        if (IsMultiplayer && IsAuthoritative)
                        {
                            var commandData = SerializeCommand(command);
                            networkBridge.BroadcastCommand(commandData, currentTick);
                        }
                    }
                    else
                    {
                        result.CommandsRejected++;
                        commandsRejected++;
                    }

                    executionResult.Dispose();
                }
                catch (Exception ex)
                {
                    ArchonLogger.LogError($"Command execution failed: {ex}", "core_commands");
                    result.CommandsRejected++;
                    commandsRejected++;
                }
                finally
                {
                    command.Dispose();
                }
            }

            result.FinalStateChecksum = provinceSystem.GetStateChecksum();
            return result;
        }

        /// <summary>
        /// Execute a single command with full validation
        /// </summary>
        private CommandExecutionResult ExecuteCommand(IProvinceCommand command)
        {
            // Final validation before execution
            var validationResult = command.Validate(provinceSystem);
            if (!validationResult.IsValid)
            {
                ArchonLogger.LogWarning($"Command validation failed at execution time: {validationResult.ErrorMessage}", "core_commands");
                return new CommandExecutionResult { IsSuccess = false };
            }

            // Execute the command
            var result = command.Execute(provinceSystem);
            if (result.IsSuccess)
            {
                commandsExecuted++;
            }

            return result;
        }

        /// <summary>
        /// Get processing statistics
        /// </summary>
        public CommandProcessorStatistics GetStatistics()
        {
            return new CommandProcessorStatistics
            {
                CurrentTick = currentTick,
                PendingCommands = pendingCommands.Count,
                CommandsExecuted = commandsExecuted,
                CommandsRejected = commandsRejected,
                ValidationFailures = validationFailures,
                ExecutionHistorySize = executionHistory.Count
            };
        }

        /// <summary>
        /// Clear execution history older than specified ticks (for memory management)
        /// </summary>
        public void ClearOldHistory(uint ticksToKeep = 1000)
        {
            uint cutoffTick = currentTick > ticksToKeep ? currentTick - ticksToKeep : 0;
            executionHistory.RemoveAll(record => record.Tick < cutoffTick);
        }

        public void Dispose()
        {
            if (isDisposed) return;

            // Unsubscribe from network bridge
            SetNetworkBridge(null);

            // Dispose all pending commands
            while (pendingCommands.Count > 0)
            {
                pendingCommands.Dequeue()?.Dispose();
            }

            executionHistory.Clear();
            isDisposed = true;
        }
    }

    /// <summary>
    /// Result of command submission
    /// </summary>
    public struct CommandSubmissionResult
    {
        public bool IsSuccess;
        public string ErrorMessage;

        public static CommandSubmissionResult Success() => new CommandSubmissionResult { IsSuccess = true };
        public static CommandSubmissionResult Failure(string message) =>
            new CommandSubmissionResult { IsSuccess = false, ErrorMessage = message };
    }

    /// <summary>
    /// Result of tick processing
    /// </summary>
    public struct TickProcessingResult
    {
        public uint Tick;
        public int CommandsProcessed;
        public int CommandsExecuted;
        public int CommandsRejected;
        public List<ushort> AffectedProvinces;
        public uint FinalStateChecksum;
    }

    /// <summary>
    /// Record of command execution for history/debugging
    /// </summary>
    public struct CommandExecutionRecord
    {
        public uint Tick;
        public byte CommandType;
        public ushort PlayerID;
        public uint Checksum;
        public uint StateChecksumAfter;
    }

    /// <summary>
    /// Command processor statistics
    /// </summary>
    public struct CommandProcessorStatistics
    {
        public uint CurrentTick;
        public int PendingCommands;
        public int CommandsExecuted;
        public int CommandsRejected;
        public int ValidationFailures;
        public int ExecutionHistorySize;

        public override string ToString()
        {
            return $"Tick:{CurrentTick}, Pending:{PendingCommands}, Executed:{CommandsExecuted}, " +
                   $"Rejected:{CommandsRejected}, Failures:{ValidationFailures}, History:{ExecutionHistorySize}";
        }
    }
}