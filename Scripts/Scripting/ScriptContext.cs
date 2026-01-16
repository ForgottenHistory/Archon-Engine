using Core;
using Core.Data.Ids;

namespace Scripting
{
    /// <summary>
    /// Per-execution context for Lua scripts.
    /// Provides scope information and access to game systems.
    ///
    /// ARCHITECTURE: Context is read-only for scripts.
    /// Scripts access game state through bindings that query this context.
    /// State modifications go through CommandSubmitter.
    /// </summary>
    public class ScriptContext
    {
        /// <summary>
        /// The province this script is scoped to (if any)
        /// Used in event effects, modifiers, etc.
        /// </summary>
        public ProvinceId? ScopeProvinceId { get; set; }

        /// <summary>
        /// The country this script is scoped to (if any)
        /// Used in AI decisions, events, etc.
        /// </summary>
        public CountryId? ScopeCountryId { get; set; }

        /// <summary>
        /// Read-only access to game state for queries
        /// </summary>
        public GameState GameState { get; set; }

        /// <summary>
        /// Interface for submitting commands from scripts
        /// Scripts cannot modify state directly - they submit commands
        /// </summary>
        public ICommandSubmitter CommandSubmitter { get; set; }

        /// <summary>
        /// Current game tick for time-based calculations
        /// </summary>
        public ulong CurrentTick => GameState?.Time?.CurrentTick ?? 0;

        /// <summary>
        /// Create an empty context (for testing or initialization)
        /// </summary>
        public ScriptContext()
        {
        }

        /// <summary>
        /// Create a context with game state access
        /// </summary>
        public ScriptContext(GameState gameState, ICommandSubmitter commandSubmitter = null)
        {
            GameState = gameState;
            CommandSubmitter = commandSubmitter;
        }

        /// <summary>
        /// Create a scoped context for a specific province
        /// </summary>
        public ScriptContext WithProvince(ProvinceId provinceId)
        {
            return new ScriptContext
            {
                GameState = GameState,
                CommandSubmitter = CommandSubmitter,
                ScopeProvinceId = provinceId,
                ScopeCountryId = ScopeCountryId
            };
        }

        /// <summary>
        /// Create a scoped context for a specific country
        /// </summary>
        public ScriptContext WithCountry(CountryId countryId)
        {
            return new ScriptContext
            {
                GameState = GameState,
                CommandSubmitter = CommandSubmitter,
                ScopeProvinceId = ScopeProvinceId,
                ScopeCountryId = countryId
            };
        }

        /// <summary>
        /// Create a fully scoped context
        /// </summary>
        public ScriptContext WithScope(ProvinceId? provinceId, CountryId? countryId)
        {
            return new ScriptContext
            {
                GameState = GameState,
                CommandSubmitter = CommandSubmitter,
                ScopeProvinceId = provinceId,
                ScopeCountryId = countryId
            };
        }

        /// <summary>
        /// Get the province ID value for Lua (returns 0 if no scope)
        /// </summary>
        public ushort GetScopeProvinceValue()
        {
            return ScopeProvinceId?.Value ?? 0;
        }

        /// <summary>
        /// Get the country ID value for Lua (returns 0 if no scope)
        /// </summary>
        public ushort GetScopeCountryValue()
        {
            return ScopeCountryId?.Value ?? 0;
        }
    }

    /// <summary>
    /// Interface for submitting commands from scripts.
    /// Implemented in GAME layer to provide command factory access.
    /// </summary>
    public interface ICommandSubmitter
    {
        /// <summary>
        /// Submit a command by name with parameters
        /// Returns true if command was successfully queued
        /// </summary>
        bool SubmitCommand(string commandName, params object[] parameters);

        /// <summary>
        /// Submit a command with validation
        /// Returns error message if validation fails, null on success
        /// </summary>
        string TrySubmitCommand(string commandName, params object[] parameters);
    }
}
