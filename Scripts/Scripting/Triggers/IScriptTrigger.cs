namespace Scripting.Triggers
{
    /// <summary>
    /// Interface for script trigger definitions.
    /// Triggers determine when scripts should be executed.
    ///
    /// ARCHITECTURE: ENGINE provides the interface and registry.
    /// GAME layer defines specific triggers (OnMonthlyTick, OnWarDeclared, etc.)
    /// </summary>
    public interface IScriptTrigger
    {
        /// <summary>
        /// Unique identifier for this trigger type
        /// Example: "on_monthly_tick", "on_war_declared"
        /// </summary>
        string TriggerId { get; }

        /// <summary>
        /// Human-readable description for documentation
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Check if this trigger should fire in current game state
        /// </summary>
        /// <param name="context">Script context with current scope</param>
        /// <returns>True if trigger condition is met</returns>
        bool ShouldFire(ScriptContext context);

        /// <summary>
        /// Get the context for script execution when triggered
        /// May modify scope based on trigger-specific data
        /// </summary>
        /// <param name="baseContext">Base context from trigger source</param>
        /// <returns>Scoped context for script execution</returns>
        ScriptContext GetExecutionContext(ScriptContext baseContext);
    }

    /// <summary>
    /// A registered script handler for a trigger
    /// </summary>
    public struct ScriptHandler
    {
        /// <summary>
        /// The Lua code or function name to execute
        /// </summary>
        public string Script { get; set; }

        /// <summary>
        /// Optional condition that must be true for script to run
        /// Evaluated as Lua expression returning boolean
        /// </summary>
        public string Condition { get; set; }

        /// <summary>
        /// Priority for execution order (higher = runs first)
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Source identifier for debugging (e.g., "events/peasant_revolt.lua")
        /// </summary>
        public string Source { get; set; }
    }
}
