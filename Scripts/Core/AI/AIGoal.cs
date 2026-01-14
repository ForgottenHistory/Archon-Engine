using System.Collections.Generic;
using Core.Data;
using Unity.Collections;

namespace Core.AI
{
    /// <summary>
    /// Abstract base class for AI goals.
    ///
    /// Goals follow the Goal-Oriented Action Planning pattern:
    /// 1. CheckConstraints() - Verify preconditions (optional, for debugging)
    /// 2. Evaluate() - Score how desirable this goal is for a country (0-1000)
    /// 3. Execute() - Perform actions to achieve this goal
    ///
    /// Design:
    /// - Stateless evaluation (pure function of GameState)
    /// - Pre-allocated buffers (zero gameplay allocations)
    /// - Uses Command pattern (AI uses same commands as player)
    /// - Goals registered in AIGoalRegistry for plug-and-play extensibility
    /// - Optional constraints for declarative preconditions
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
        /// Constraints that must be satisfied for this goal to be considered.
        /// If any constraint fails, Evaluate() is not called.
        /// </summary>
        protected List<IGoalConstraint> constraints;

        protected AIGoal()
        {
            constraints = new List<IGoalConstraint>(4);
        }
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
        /// Initialize goal (allocate persistent buffers here).
        /// Called once during AISystem initialization.
        /// EventBus provided for goals that need event subscriptions.
        /// </summary>
        public virtual void Initialize(Core.EventBus eventBus) { }

        /// <summary>
        /// Cleanup goal (dispose allocated buffers here)
        /// Called during AISystem disposal.
        /// </summary>
        public virtual void Dispose() { }

        #region Constraints

        /// <summary>
        /// Add a constraint to this goal.
        /// Call during Initialize() or construction.
        /// </summary>
        protected void AddConstraint(IGoalConstraint constraint)
        {
            if (constraint != null)
                constraints.Add(constraint);
        }

        /// <summary>
        /// Check if all constraints are satisfied.
        /// Returns true if goal should be evaluated, false to skip.
        /// </summary>
        public bool CheckConstraints(ushort countryID, GameState gameState)
        {
            for (int i = 0; i < constraints.Count; i++)
            {
                if (!constraints[i].IsSatisfied(countryID, gameState))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Get list of failed constraints (for debugging).
        /// </summary>
        public List<string> GetFailedConstraints(ushort countryID, GameState gameState)
        {
            var failed = new List<string>();
            for (int i = 0; i < constraints.Count; i++)
            {
                if (!constraints[i].IsSatisfied(countryID, gameState))
                    failed.Add(constraints[i].Name);
            }
            return failed;
        }

        /// <summary>
        /// Get all constraints (for debugging/UI).
        /// </summary>
        public IReadOnlyList<IGoalConstraint> Constraints => constraints;

        #endregion
    }
}
