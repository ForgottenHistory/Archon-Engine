namespace Core.Commands
{
    /// <summary>
    /// Base interface for all game commands
    /// Commands provide validation, execution, and undo support
    /// All game state changes must go through commands for:
    /// - Validation before execution
    /// - Event emission for system coordination
    /// - Multiplayer synchronization
    /// - Replay/undo support
    /// </summary>
    public interface ICommand
    {
        /// <summary>
        /// Validate that this command can be executed in the current game state
        /// Should be fast (< 0.001ms) and have no side effects
        /// </summary>
        /// <param name="gameState">Current game state for validation</param>
        /// <returns>True if command can be executed</returns>
        bool Validate(GameState gameState);

        /// <summary>
        /// Execute the command and modify game state
        /// Should emit appropriate events for other systems to react
        /// Must be deterministic for multiplayer compatibility
        /// </summary>
        /// <param name="gameState">Game state to modify</param>
        void Execute(GameState gameState);

        /// <summary>
        /// Undo the command - restore previous state
        /// Required for replay systems and error recovery
        /// </summary>
        /// <param name="gameState">Game state to restore</param>
        void Undo(GameState gameState);

        /// <summary>
        /// Serialize command data to binary writer
        /// Used for save/load and command logging
        /// Must be deterministic - same command = same bytes
        /// </summary>
        /// <param name="writer">Binary writer to serialize to</param>
        void Serialize(System.IO.BinaryWriter writer);

        /// <summary>
        /// Deserialize command data from binary reader
        /// Used for save/load and command replay
        /// Must reconstruct identical command state
        /// </summary>
        /// <param name="reader">Binary reader to deserialize from</param>
        void Deserialize(System.IO.BinaryReader reader);

        /// <summary>
        /// Get command priority for execution ordering
        /// Higher priority commands execute first
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Get unique command ID for networking and logging
        /// </summary>
        string CommandId { get; }
    }

    /// <summary>
    /// Base class for commands with common functionality
    /// Provides default implementations and utilities
    /// </summary>
    public abstract class BaseCommand : ICommand
    {
        public virtual int Priority => 0;
        public virtual string CommandId => GetType().Name;

        public abstract bool Validate(GameState gameState);
        public abstract void Execute(GameState gameState);
        public abstract void Undo(GameState gameState);
        public abstract void Serialize(System.IO.BinaryWriter writer);
        public abstract void Deserialize(System.IO.BinaryReader reader);

        /// <summary>
        /// Utility for common validation checks
        /// </summary>
        protected bool ValidateProvinceId(GameState gameState, ushort provinceId)
        {
            return provinceId < gameState.Provinces.ProvinceCount;
        }

        /// <summary>
        /// Utility for common validation checks
        /// </summary>
        protected bool ValidateCountryId(GameState gameState, ushort countryId)
        {
            return countryId < gameState.Countries.CountryCount;
        }

        /// <summary>
        /// Log command execution for debugging
        /// </summary>
        protected void LogExecution(string action)
        {
            #if UNITY_EDITOR
            ArchonLogger.Log($"Command {CommandId}: {action}");
            #endif
        }
    }

    /// <summary>
    /// Interface for commands that can be networked
    /// Provides serialization support for multiplayer
    /// </summary>
    public interface INetworkCommand : ICommand
    {
        /// <summary>
        /// Serialize command data for network transmission
        /// Must be deterministic and compact
        /// </summary>
        byte[] Serialize();

        /// <summary>
        /// Deserialize command data from network
        /// Must reconstruct identical command state
        /// </summary>
        void Deserialize(byte[] data);

        /// <summary>
        /// Get estimated network size in bytes
        /// Used for bandwidth management
        /// </summary>
        int EstimatedNetworkSize { get; }
    }

    /// <summary>
    /// Command execution result
    /// Provides detailed feedback about command execution
    /// </summary>
    public struct CommandResult
    {
        public bool Success;
        public string ErrorMessage;
        public float ExecutionTimeMs;
        public int EventsEmitted;

        public static CommandResult Successful(float executionTime = 0f, int eventsEmitted = 0)
        {
            return new CommandResult
            {
                Success = true,
                ExecutionTimeMs = executionTime,
                EventsEmitted = eventsEmitted
            };
        }

        public static CommandResult Failed(string error)
        {
            return new CommandResult
            {
                Success = false,
                ErrorMessage = error
            };
        }
    }
}