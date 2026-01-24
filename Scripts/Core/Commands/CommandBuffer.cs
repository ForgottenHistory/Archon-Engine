using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Core.Systems;

namespace Core.Commands
{
    /// <summary>
    /// Manages command buffering with rollback support for multiplayer synchronization
    /// Handles client prediction, lag compensation, and state reconciliation
    /// </summary>
    public class CommandBuffer : IDisposable
    {
        private readonly ProvinceSystem provinceSystem;
        private readonly CommandProcessor processor;
        private readonly CircularBuffer<StateSnapshot> stateHistory;
        private readonly Dictionary<uint, List<IProvinceCommand>> pendingCommands;
        private readonly Queue<IProvinceCommand> unconfirmedCommands;

        private uint confirmedTick;
        private uint predictedTick;
        private const int MAX_ROLLBACK_FRAMES = 30; // 0.5 seconds at 60fps
        private bool isDisposed;

        public CommandBuffer(ProvinceSystem provinceSystem, CommandProcessor processor)
        {
            this.provinceSystem = provinceSystem ?? throw new ArgumentNullException(nameof(provinceSystem));
            this.processor = processor ?? throw new ArgumentNullException(nameof(processor));
            this.stateHistory = new CircularBuffer<StateSnapshot>(MAX_ROLLBACK_FRAMES + 1);
            this.pendingCommands = new Dictionary<uint, List<IProvinceCommand>>();
            this.unconfirmedCommands = new Queue<IProvinceCommand>();

            this.confirmedTick = 0;
            this.predictedTick = 0;
        }

        /// <summary>
        /// Current confirmed tick (acknowledged by server)
        /// </summary>
        public uint ConfirmedTick => confirmedTick;

        /// <summary>
        /// Current predicted tick (including client predictions)
        /// </summary>
        public uint PredictedTick => predictedTick;

        /// <summary>
        /// Number of frames available for rollback
        /// </summary>
        public int RollbackFramesAvailable => stateHistory.Count;

        /// <summary>
        /// Add a command for immediate local prediction
        /// Command will be executed locally and sent to server for confirmation
        /// </summary>
        public CommandBufferResult AddPredictedCommand(IProvinceCommand command)
        {
            if (isDisposed)
                return CommandBufferResult.Failure("CommandBuffer is disposed");

            if (command == null)
                return CommandBufferResult.Failure("Command cannot be null");

            try
            {
                // Set execution tick for next frame
                command.ExecutionTick = predictedTick + 1;

                // Execute locally for immediate feedback
                var submissionResult = processor.SubmitCommand(command);
                if (!submissionResult.IsSuccess)
                {
                    return CommandBufferResult.Failure($"Command submission failed: {submissionResult.ErrorMessage}");
                }

                // Store for potential rollback
                unconfirmedCommands.Enqueue(command);

                return CommandBufferResult.Success();
            }
            catch (Exception ex)
            {
                return CommandBufferResult.Failure($"Failed to add predicted command: {ex.Message}");
            }
        }

        /// <summary>
        /// Add a confirmed command from the server
        /// This will trigger rollback if it conflicts with local predictions
        /// </summary>
        public CommandBufferResult AddConfirmedCommand(IProvinceCommand command, uint serverTick)
        {
            if (isDisposed)
                return CommandBufferResult.Failure("CommandBuffer is disposed");

            try
            {
                // Check if we need to rollback
                if (serverTick <= confirmedTick)
                {
                    ArchonLogger.LogWarning($"Received command for tick {serverTick} but already confirmed up to {confirmedTick}", "core_commands");
                    return CommandBufferResult.Success(); // Ignore old commands
                }

                // Store command for the specific tick
                if (!pendingCommands.TryGetValue(serverTick, out var commandsForTick))
                {
                    commandsForTick = new List<IProvinceCommand>();
                    pendingCommands[serverTick] = commandsForTick;
                }
                commandsForTick.Add(command);

                // If this is the next expected tick, we can confirm it immediately
                if (serverTick == confirmedTick + 1)
                {
                    return ConfirmTick(serverTick);
                }

                return CommandBufferResult.Success();
            }
            catch (Exception ex)
            {
                return CommandBufferResult.Failure($"Failed to add confirmed command: {ex.Message}");
            }
        }

        /// <summary>
        /// Process a game tick with rollback support
        /// Should be called once per frame
        /// </summary>
        public TickProcessResult ProcessTick()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(CommandBuffer));

            try
            {
                // Save current state before processing
                SaveStateSnapshot();

                // Process the tick
                var tickResult = processor.ProcessTick();
                predictedTick = tickResult.Tick;

                // Check if we received confirmation for any pending ticks
                ProcessPendingConfirmations();

                return new TickProcessResult
                {
                    IsSuccess = true,
                    ProcessedTick = predictedTick,
                    ConfirmedTick = confirmedTick,
                    CommandsProcessed = tickResult.CommandsProcessed,
                    CommandsExecuted = tickResult.CommandsExecuted,
                    RollbacksPerformed = 0 // Will be updated if rollback occurs
                };
            }
            catch (Exception ex)
            {
                return new TickProcessResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Tick processing failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Confirm a specific tick, potentially triggering rollback
        /// </summary>
        private CommandBufferResult ConfirmTick(uint tickToConfirm)
        {
            try
            {
                // Check if we need to rollback
                bool needsRollback = tickToConfirm <= predictedTick && HasConflicts(tickToConfirm);

                if (needsRollback)
                {
                    var rollbackResult = PerformRollback(tickToConfirm);
                    if (!rollbackResult.IsSuccess)
                    {
                        return rollbackResult;
                    }
                }

                // Update confirmed tick
                confirmedTick = Math.Max(confirmedTick, tickToConfirm);

                // Remove confirmed commands from unconfirmed queue
                CleanupUnconfirmedCommands(tickToConfirm);

                return CommandBufferResult.Success();
            }
            catch (Exception ex)
            {
                return CommandBufferResult.Failure($"Failed to confirm tick {tickToConfirm}: {ex.Message}");
            }
        }

        /// <summary>
        /// Perform rollback to a specific tick
        /// </summary>
        private CommandBufferResult PerformRollback(uint rollbackToTick)
        {
            try
            {
                // Find the state snapshot for the rollback tick
                var snapshot = FindStateSnapshot(rollbackToTick);
                if (snapshot == null)
                {
                    return CommandBufferResult.Failure($"No state snapshot available for rollback to tick {rollbackToTick}");
                }

                // Restore the state
                RestoreStateSnapshot(snapshot.Value);

                // Re-execute confirmed commands from rollback point
                for (uint tick = rollbackToTick + 1; tick <= predictedTick; tick++)
                {
                    if (pendingCommands.TryGetValue(tick, out var commandsForTick))
                    {
                        foreach (var command in commandsForTick)
                        {
                            processor.SubmitCommand(command);
                        }
                    }
                }

                // Re-process ticks up to current predicted tick
                uint currentProcessorTick = processor.CurrentTick;
                while (currentProcessorTick < predictedTick)
                {
                    processor.ProcessTick();
                    currentProcessorTick++;
                }

                ArchonLogger.Log($"Performed rollback to tick {rollbackToTick}, re-simulated {predictedTick - rollbackToTick} frames", "core_commands");
                return CommandBufferResult.Success();
            }
            catch (Exception ex)
            {
                return CommandBufferResult.Failure($"Rollback failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if there are conflicts between predicted and confirmed commands
        /// </summary>
        private bool HasConflicts(uint tick)
        {
            // Simple conflict detection - in a real implementation, this would be more sophisticated
            // Check if we have unconfirmed commands for this tick that differ from confirmed ones
            return unconfirmedCommands.Count > 0;
        }

        /// <summary>
        /// Save a snapshot of the current game state
        /// </summary>
        private void SaveStateSnapshot()
        {
            try
            {
                var snapshot = new StateSnapshot
                {
                    Tick = predictedTick,
                    StateChecksum = provinceSystem.GetStateChecksum(),
                    Timestamp = DateTime.UtcNow
                };

                // For a full implementation, we would serialize the entire province state here
                // For now, we just store the checksum as a lightweight approach
                stateHistory.Add(snapshot);
            }
            catch (Exception ex)
            {
                ArchonLogger.LogError($"Failed to save state snapshot: {ex}", "core_commands");
            }
        }

        /// <summary>
        /// Find state snapshot for a specific tick
        /// </summary>
        private StateSnapshot? FindStateSnapshot(uint tick)
        {
            for (int i = stateHistory.Count - 1; i >= 0; i--)
            {
                var snapshot = stateHistory[i];
                if (snapshot.Tick == tick)
                {
                    return snapshot;
                }
            }
            return null;
        }

        /// <summary>
        /// Restore state from a snapshot
        /// </summary>
        private void RestoreStateSnapshot(StateSnapshot snapshot)
        {
            // In a full implementation, this would restore the complete provinceSystem state
            // For now, this is a placeholder that would need to be implemented based on
            // the specific needs of the provinceSystem system

            ArchonLogger.LogWarning("RestoreStateSnapshot is not fully implemented - would restore complete provinceSystem state", "core_commands");

            // Reset processor tick to match snapshot
            // Note: This would require additional CommandProcessor API to support state restoration
        }

        /// <summary>
        /// Process any pending confirmations
        /// </summary>
        private void ProcessPendingConfirmations()
        {
            // Check for consecutive confirmed ticks
            uint nextExpectedTick = confirmedTick + 1;
            while (pendingCommands.ContainsKey(nextExpectedTick))
            {
                ConfirmTick(nextExpectedTick);
                nextExpectedTick++;
            }
        }

        /// <summary>
        /// Clean up unconfirmed commands that have been confirmed
        /// </summary>
        private void CleanupUnconfirmedCommands(uint confirmedUpToTick)
        {
            var remaining = new Queue<IProvinceCommand>();
            while (unconfirmedCommands.Count > 0)
            {
                var command = unconfirmedCommands.Dequeue();
                if (command.ExecutionTick > confirmedUpToTick)
                {
                    remaining.Enqueue(command);
                }
                else
                {
                    // Command was confirmed, can dispose it
                    command.Dispose();
                }
            }

            // Restore remaining unconfirmed commands
            while (remaining.Count > 0)
            {
                unconfirmedCommands.Enqueue(remaining.Dequeue());
            }
        }

        /// <summary>
        /// Get buffer statistics
        /// </summary>
        public CommandBufferStatistics GetStatistics()
        {
            return new CommandBufferStatistics
            {
                ConfirmedTick = confirmedTick,
                PredictedTick = predictedTick,
                UnconfirmedCommands = unconfirmedCommands.Count,
                StateSnapshotsStored = stateHistory.Count,
                PendingTicksCount = pendingCommands.Count
            };
        }

        public void Dispose()
        {
            if (isDisposed) return;

            // Dispose all unconfirmed commands
            while (unconfirmedCommands.Count > 0)
            {
                unconfirmedCommands.Dequeue()?.Dispose();
            }

            // Dispose all pending commands
            foreach (var commandList in pendingCommands.Values)
            {
                foreach (var command in commandList)
                {
                    command?.Dispose();
                }
                commandList.Clear();
            }
            pendingCommands.Clear();

            stateHistory.Clear();
            isDisposed = true;
        }
    }

    /// <summary>
    /// Lightweight state snapshot for rollback
    /// </summary>
    public struct StateSnapshot
    {
        public uint Tick;
        public uint StateChecksum;
        public DateTime Timestamp;
        // In a full implementation, this would contain serialized province state
    }

    /// <summary>
    /// Circular buffer for efficient storage of state snapshots
    /// </summary>
    public class CircularBuffer<T>
    {
        private readonly T[] buffer;
        private readonly int capacity;
        private int head;
        private int count;

        public CircularBuffer(int capacity)
        {
            this.capacity = capacity;
            this.buffer = new T[capacity];
            this.head = 0;
            this.count = 0;
        }

        public int Count => count;
        public int Capacity => capacity;

        public void Add(T item)
        {
            buffer[head] = item;
            head = (head + 1) % capacity;

            if (count < capacity)
                count++;
        }

        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                int actualIndex = (head - count + index + capacity) % capacity;
                return buffer[actualIndex];
            }
        }

        public void Clear()
        {
            head = 0;
            count = 0;
        }
    }

    // Result types for command buffer operations
    public struct CommandBufferResult
    {
        public bool IsSuccess;
        public string ErrorMessage;

        public static CommandBufferResult Success() => new CommandBufferResult { IsSuccess = true };
        public static CommandBufferResult Failure(string error) => new CommandBufferResult { IsSuccess = false, ErrorMessage = error };
    }

    public struct TickProcessResult
    {
        public bool IsSuccess;
        public string ErrorMessage;
        public uint ProcessedTick;
        public uint ConfirmedTick;
        public int CommandsProcessed;
        public int CommandsExecuted;
        public int RollbacksPerformed;
    }

    public struct CommandBufferStatistics
    {
        public uint ConfirmedTick;
        public uint PredictedTick;
        public int UnconfirmedCommands;
        public int StateSnapshotsStored;
        public int PendingTicksCount;

        public override string ToString()
        {
            return $"Confirmed:{ConfirmedTick}, Predicted:{PredictedTick}, Unconfirmed:{UnconfirmedCommands}, " +
                   $"Snapshots:{StateSnapshotsStored}, Pending:{PendingTicksCount}";
        }
    }
}