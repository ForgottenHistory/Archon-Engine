using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Core.Systems;

namespace Core.Commands
{
    /// <summary>
    /// Processes and executes commands in deterministic order
    /// Ensures multiplayer consistency through command validation and execution
    /// </summary>
    public class CommandProcessor : IDisposable
    {
        private readonly ProvinceSimulation simulation;
        private readonly Queue<IProvinceCommand> pendingCommands;
        private readonly List<CommandExecutionRecord> executionHistory;
        private uint currentTick;
        private bool isDisposed;

        // Statistics for monitoring
        private int commandsExecuted;
        private int commandsRejected;
        private int validationFailures;

        public CommandProcessor(ProvinceSimulation simulation)
        {
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
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
        /// Add a command to be processed
        /// Commands are validated immediately but executed later
        /// </summary>
        public CommandSubmissionResult SubmitCommand(IProvinceCommand command)
        {
            if (isDisposed)
                return CommandSubmissionResult.CreateFailure("CommandProcessor is disposed");

            if (command == null)
                return CommandSubmissionResult.CreateFailure("Command cannot be null");

            // Validate command structure
            try
            {
                var checksum = command.GetChecksum();
                var serializedSize = command.GetSerializedSize();

                if (serializedSize <= 0 || serializedSize > 256)
                {
                    return CommandSubmissionResult.CreateFailure($"Invalid serialized size: {serializedSize}");
                }
            }
            catch (Exception ex)
            {
                return CommandSubmissionResult.CreateFailure($"Command structure validation failed: {ex.Message}");
            }

            // Pre-validate against current game state
            var validationResult = command.Validate(simulation);
            if (!validationResult.IsValid)
            {
                validationFailures++;
                return CommandSubmissionResult.CreateFailure($"Validation failed: {validationResult.ErrorMessage}");
            }

            // Set execution tick if not set
            if (command.ExecutionTick <= currentTick)
            {
                command.ExecutionTick = currentTick + 1;
            }

            pendingCommands.Enqueue(command);
            return CommandSubmissionResult.CreateSuccess();
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
                    ArchonLogger.LogWarning($"Rejecting late command: {command} (current tick: {currentTick})");
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
                    if (executionResult.Success)
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
                    ArchonLogger.LogError($"Command execution failed: {ex}");
                    result.CommandsRejected++;
                    commandsRejected++;
                }
                finally
                {
                    command.Dispose();
                }
            }

            result.FinalStateChecksum = simulation.GetStateChecksum();
            return result;
        }

        /// <summary>
        /// Execute a single command with full validation
        /// </summary>
        private CommandExecutionResult ExecuteCommand(IProvinceCommand command)
        {
            // Final validation before execution
            var validationResult = command.Validate(simulation);
            if (!validationResult.IsValid)
            {
                ArchonLogger.LogWarning($"Command validation failed at execution time: {validationResult.ErrorMessage}");
                return new CommandExecutionResult { Success = false };
            }

            // Execute the command
            var result = command.Execute(simulation);
            if (result.Success)
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
        public bool Success;
        public string ErrorMessage;

        public static CommandSubmissionResult CreateSuccess() => new CommandSubmissionResult { Success = true };
        public static CommandSubmissionResult CreateFailure(string message) =>
            new CommandSubmissionResult { Success = false, ErrorMessage = message };
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