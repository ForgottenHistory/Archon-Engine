using Core;
using Core.Data;
using Unity.Collections;

namespace Core.AI
{
    /// <summary>
    /// Abstract base class for AI goals.
    ///
    /// Goals follow the Goal-Oriented Action Planning pattern:
    /// 1. Evaluate() - Score how desirable this goal is for a country (0-1000)
    /// 2. Execute() - Perform actions to achieve this goal
    ///
    /// Design:
    /// - Stateless evaluation (pure function of GameState)
    /// - Pre-allocated buffers (zero gameplay allocations)
    /// - Uses Command pattern (AI uses same commands as player)
    /// - Goals registered in AIGoalRegistry for plug-and-play extensibility
    ///
    /// Performance:
    /// - No Burst compilation (goals need GameState access)
    /// - Target: <5ms for 10 AI evaluations
    ///
    /// ENGINE-GAME Separation:
    /// - ENGINE: Abstract base class (mechanism)
    /// - GAME: Concrete goals (policy - formulas, thresholds, priorities)
    /// </summary>
    public abstract class AIGoal
    {
        /// <summary>
        /// Unique goal ID (assigned by AIGoalRegistry)
        /// </summary>
        public ushort GoalID { get; set; }

        /// <summary>
        /// Human-readable goal name for debugging
        /// </summary>
        public abstract string GoalName { get; }

        /// <summary>
        /// Evaluate how desirable this goal is for the given country.
        ///
        /// Returns:
        /// - 0 = Not desirable or impossible
        /// - 1-1000 = Desirability score (higher = more important)
        ///
        /// Design:
        /// - Pure function (no side effects)
        /// - Quick heuristics (avoid expensive calculations)
        /// - Return 0 early if goal impossible (prune decision space)
        ///
        /// Example Scores:
        /// - Critical (800-1000): Bankruptcy emergency, being invaded
        /// - High (500-799): Major opportunities, important strategic goals
        /// - Medium (200-499): Normal strategic priorities
        /// - Low (1-199): Nice-to-have improvements
        /// - Zero (0): Impossible or undesirable
        /// </summary>
        public abstract FixedPoint64 Evaluate(ushort countryID, GameState gameState);

        /// <summary>
        /// Execute actions to achieve this goal.
        ///
        /// Design:
        /// - Use Command pattern (same commands as player)
        /// - Pre-allocated buffers (no allocations)
        /// - Can execute multiple commands per call
        /// - Stateless (goal persistence in AIState.activeGoalID only)
        ///
        /// Performance:
        /// - Clear and reuse buffers (cheap!)
        /// - Avoid LINQ, foreach over interfaces
        /// - Simple heuristics over perfect optimization
        ///
        /// Example Actions:
        /// - BuildEconomyGoal: Queue farm construction command
        /// - ExpandTerritoryGoal: Declare war command
        /// - ImproveRelationsGoal: Improve relations command
        /// </summary>
        public abstract void Execute(ushort countryID, GameState gameState);

        /// <summary>
        /// Initialize goal (allocate persistent buffers here)
        /// Called once during AISystem initialization.
        /// </summary>
        public virtual void Initialize() { }

        /// <summary>
        /// Cleanup goal (dispose allocated buffers here)
        /// Called during AISystem disposal.
        /// </summary>
        public virtual void Dispose() { }
    }
}
