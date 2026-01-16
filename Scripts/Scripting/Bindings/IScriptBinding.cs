using MoonSharp.Interpreter;

namespace Scripting.Bindings
{
    /// <summary>
    /// Interface for registering Lua functions with the script engine.
    ///
    /// ARCHITECTURE: ENGINE provides the interface, GAME implements bindings.
    /// Engine bindings provide basic province/country queries.
    /// Game bindings add economy, diplomacy, and game-specific functions.
    ///
    /// All bindings should:
    /// - Be read-only (queries only) OR
    /// - Submit commands through ICommandSubmitter for state changes
    /// - Never modify game state directly
    /// </summary>
    public interface IScriptBinding
    {
        /// <summary>
        /// Name of this binding group (for logging/debugging)
        /// Example: "Core.Province", "Game.Economy"
        /// </summary>
        string BindingName { get; }

        /// <summary>
        /// Register Lua functions with the script instance.
        /// Called once during script engine initialization.
        /// </summary>
        /// <param name="luaScript">The MoonSharp script to register with</param>
        /// <param name="context">Script context for accessing game state</param>
        void Register(Script luaScript, ScriptContext context);
    }
}
