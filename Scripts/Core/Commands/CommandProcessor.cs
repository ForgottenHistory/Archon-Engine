using System;
using System.Collections.Generic;
using System.IO;
using Core.Network;

namespace Core.Commands
{
    /// <summary>
    /// Processes commands (ICommand) with network synchronization.
    /// All game state changes flow through this processor.
    ///
    /// Commands are validated locally, then:
    /// - Host: executes locally and broadcasts to clients
    /// - Client: sends to host for validation and execution
    /// </summary>
    public class CommandProcessor : IDisposable
    {
        private readonly GameState gameState;
        private readonly Dictionary<ushort, Func<ICommand>> commandFactories;
        private readonly Dictionary<Type, ushort> commandTypeIds;

        private INetworkBridge networkBridge;
        private ushort nextCommandTypeId = 1;
        private bool disposed;

        // Network state
        private bool isMultiplayer;
        private bool isHost;

        /// <summary>
        /// Whether we are in a multiplayer session.
        /// </summary>
        public bool IsMultiplayer => isMultiplayer && networkBridge?.IsConnected == true;

        /// <summary>
        /// Whether we are the authoritative host (or single-player).
        /// </summary>
        public bool IsAuthoritative => !IsMultiplayer || (networkBridge?.IsHost ?? true);

        public CommandProcessor(GameState gameState)
        {
            this.gameState = gameState ?? throw new ArgumentNullException(nameof(gameState));
            this.commandFactories = new Dictionary<ushort, Func<ICommand>>();
            this.commandTypeIds = new Dictionary<Type, ushort>();
        }

        /// <summary>
        /// Register a command type for network serialization.
        /// Must be called for all command types that will be networked.
        /// Call order must be identical on all clients!
        /// </summary>
        public void RegisterCommandType<T>() where T : ICommand, new()
        {
            Type type = typeof(T);
            if (commandTypeIds.ContainsKey(type))
            {
                ArchonLogger.LogWarning($"CommandProcessor: Command type {type.Name} already registered", "core_commands");
                return;
            }

            ushort typeId = nextCommandTypeId++;
            commandTypeIds[type] = typeId;
            commandFactories[typeId] = () => new T();

            ArchonLogger.Log($"CommandProcessor: Registered {type.Name} as type ID {typeId}", "core_commands");
        }

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
            isMultiplayer = bridge != null;
            isHost = bridge?.IsHost ?? true;

            // Subscribe to new bridge
            if (networkBridge != null)
            {
                networkBridge.OnCommandReceived += HandleRemoteCommand;
                ArchonLogger.Log("CommandProcessor: Network bridge attached", "core_commands");
            }
        }

        /// <summary>
        /// Submit a command for execution without allocating a result message string.
        /// Used by AI and other hot-path callers that don't need feedback.
        /// </summary>
        public bool SubmitCommand<T>(T command) where T : ICommand
        {
            if (disposed)
                return false;

            // Get command type ID for networking (use runtime type, not compile-time)
            Type commandType = command.GetType();
            if (!commandTypeIds.TryGetValue(commandType, out ushort typeId))
            {
                if (IsMultiplayer)
                {
                    ArchonLogger.LogWarning($"CommandProcessor: Command type {commandType.Name} not registered for networking - executing locally only!", "core_commands");
                }
                // Unregistered command: validate + execute locally
                if (!command.Validate(gameState))
                    return false;
                return ExecuteLocallyPreValidated(command);
            }

            // Local validation first
            if (!command.Validate(gameState))
                return false;

            // Multiplayer routing
            if (IsMultiplayer && !IsAuthoritative)
            {
                byte[] commandData = SerializeCommand(typeId, command);
                networkBridge.SendCommandToHost(commandData, 0);
                return true;
            }

            // Host or single-player: execute locally (already validated)
            bool success = ExecuteLocallyPreValidated(command);

            // Host: broadcast to clients
            if (success && IsMultiplayer && IsAuthoritative)
            {
                byte[] commandData = SerializeCommand(typeId, command);
                networkBridge.BroadcastCommand(commandData, 0);
            }

            return success;
        }

        /// <summary>
        /// Submit a command for execution.
        /// In multiplayer:
        /// - Clients send to host for validation
        /// - Host validates, executes, and broadcasts
        /// </summary>
        public bool SubmitCommand<T>(T command, out string resultMessage) where T : ICommand
        {
            if (disposed)
            {
                resultMessage = "CommandProcessor is disposed";
                return false;
            }

            // Get command type ID for networking (use runtime type, not compile-time)
            Type commandType = command.GetType();
            if (!commandTypeIds.TryGetValue(commandType, out ushort typeId))
            {
                // Command type not registered - can't network it
                // Fall back to local execution only (with warning in multiplayer)
                if (IsMultiplayer)
                {
                    ArchonLogger.LogWarning($"CommandProcessor: Command type {commandType.Name} not registered for networking - executing locally only!", "core_commands");
                }
                return ExecuteLocally(command, out resultMessage);
            }

            if (GameSettings.Instance?.ShouldLog(LogLevel.Debug) ?? false)
                ArchonLogger.Log($"CommandProcessor: Submitting {commandType.Name} (typeId={typeId}, multiplayer={IsMultiplayer}, authoritative={IsAuthoritative})", "core_commands");

            // Local validation first
            if (!command.Validate(gameState))
            {
                resultMessage = GetCommandValidationError(command);
                return false;
            }

            // Multiplayer routing
            if (IsMultiplayer && !IsAuthoritative)
            {
                // Client: send to host
                byte[] commandData = SerializeCommand(typeId, command);
                networkBridge.SendCommandToHost(commandData, 0); // tick managed by host
                resultMessage = "Command sent to host for validation";
                return true; // Assume success - host will validate
            }

            // Host or single-player: execute locally
            bool success = ExecuteLocally(command, out resultMessage);

            // Host: broadcast to clients
            if (success && IsMultiplayer && IsAuthoritative)
            {
                byte[] commandData = SerializeCommand(typeId, command);
                if (GameSettings.Instance?.ShouldLog(LogLevel.Debug) ?? false)
                    ArchonLogger.Log($"CommandProcessor: Broadcasting {commandType.Name} ({commandData.Length} bytes) to clients", "core_commands");
                networkBridge.BroadcastCommand(commandData, 0);
            }

            return success;
        }

        /// <summary>
        /// Execute a pre-validated command locally without message allocation.
        /// Caller must have already validated the command.
        /// Used by AI and hot-path callers.
        /// </summary>
        private bool ExecuteLocallyPreValidated<T>(T command) where T : ICommand
        {
            try
            {
                command.Execute(gameState);

                gameState.EventBus.Emit(new CommandExecutedEvent
                {
                    CommandType = typeof(T),
                    IsSuccess = true
                });

                return true;
            }
            catch (Exception e)
            {
                ArchonLogger.LogError($"CommandProcessor: Command execution failed - {e.Message}", "core_commands");

                gameState.EventBus.Emit(new CommandExecutedEvent
                {
                    CommandType = typeof(T),
                    IsSuccess = false,
                    Error = e.Message
                });

                return false;
            }
        }

        /// <summary>
        /// Execute a command locally without network routing.
        /// Used by host after validation and by clients after receiving from host.
        /// </summary>
        private bool ExecuteLocally<T>(T command, out string resultMessage) where T : ICommand
        {
            // Validate
            if (!command.Validate(gameState))
            {
                resultMessage = GetCommandValidationError(command);
                return false;
            }

            // Execute
            try
            {
                command.Execute(gameState);
                resultMessage = GetCommandSuccessMessage(command);

                // Emit event
                gameState.EventBus.Emit(new CommandExecutedEvent
                {
                    CommandType = typeof(T),
                    IsSuccess = true
                });

                return true;
            }
            catch (Exception e)
            {
                resultMessage = $"Execution error: {e.Message}";
                ArchonLogger.LogError($"CommandProcessor: Command execution failed - {e.Message}", "core_commands");

                gameState.EventBus.Emit(new CommandExecutedEvent
                {
                    CommandType = typeof(T),
                    IsSuccess = false,
                    Error = e.Message
                });

                return false;
            }
        }

        /// <summary>
        /// Handle command received from network.
        /// </summary>
        private void HandleRemoteCommand(int peerId, byte[] commandData, uint tick)
        {
            if (GameSettings.Instance?.ShouldLog(LogLevel.Debug) ?? false)
                ArchonLogger.Log($"CommandProcessor: HandleRemoteCommand from peer {peerId} ({commandData?.Length ?? 0} bytes)", "core_commands");

            if (commandData == null || commandData.Length < 2)
            {
                ArchonLogger.LogWarning($"CommandProcessor: Invalid command data from peer {peerId}", "core_commands");
                return;
            }

            // Read type ID from first 2 bytes
            ushort typeId = (ushort)(commandData[0] | (commandData[1] << 8));

            // Check if this is one of our registered types
            if (!commandFactories.TryGetValue(typeId, out var factory))
            {
                // Not a GameCommand - let CommandProcessor handle it
                ArchonLogger.Log($"CommandProcessor: TypeId {typeId} not registered, ignoring", "core_commands");
                return;
            }

            try
            {
                // Deserialize command
                ICommand command = factory();
                using (var stream = new MemoryStream(commandData, 2, commandData.Length - 2))
                using (var reader = new BinaryReader(stream))
                {
                    command.Deserialize(reader);
                }

                if (IsAuthoritative)
                {
                    // Host received from client - validate and broadcast
                    if (command.Validate(gameState))
                    {
                        command.Execute(gameState);

                        // Broadcast to all clients (including sender)
                        networkBridge.BroadcastCommand(commandData, tick);

                        if (GameSettings.Instance?.ShouldLog(LogLevel.Debug) ?? false)
                            ArchonLogger.Log($"CommandProcessor: Executed and broadcast command from peer {peerId}", "core_commands");
                    }
                    else
                    {
                        ArchonLogger.LogWarning($"CommandProcessor: Rejected invalid command from peer {peerId}", "core_commands");
                    }
                }
                else
                {
                    // Client received from host - execute directly (already validated)
                    command.Execute(gameState);
                    if (GameSettings.Instance?.ShouldLog(LogLevel.Debug) ?? false)
                        ArchonLogger.Log($"CommandProcessor: Executed command from host", "core_commands");
                }
            }
            catch (Exception e)
            {
                ArchonLogger.LogError($"CommandProcessor: Failed to process remote command - {e.Message}", "core_commands");
            }
        }

        /// <summary>
        /// Serialize a command for network transmission.
        /// Format: [typeId:2][serialized command data]
        /// </summary>
        private byte[] SerializeCommand<T>(ushort typeId, T command) where T : ICommand
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                // Write type ID
                writer.Write(typeId);

                // Write command data
                command.Serialize(writer);

                return stream.ToArray();
            }
        }

        private string GetCommandValidationError<T>(T command) where T : ICommand
        {
            if (command is ICommandMessages messages)
                return messages.GetValidationError(gameState);
            return "Command failed validation";
        }

        private string GetCommandSuccessMessage<T>(T command) where T : ICommand
        {
            if (command is ICommandMessages messages)
                return messages.GetSuccessMessage(gameState);
            return "Command executed successfully";
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            SetNetworkBridge(null);
            commandFactories.Clear();
            commandTypeIds.Clear();
        }
    }
}
