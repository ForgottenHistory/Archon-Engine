using System;
using System.Collections.Generic;
using Core;

namespace Core.AI
{
    /// <summary>
    /// Registry for AI goals (plug-and-play extensibility).
    ///
    /// Design:
    /// - GAME layer registers concrete goals during initialization
    /// - ENGINE layer queries goals during AI processing
    /// - Goals assigned sequential IDs (1, 2, 3, ...)
    /// - ID 0 = no active goal
    ///
    /// Usage:
    /// - Registration: registry.Register(new BuildEconomyGoal());
    /// - Query: var goal = registry.GetGoal(goalID);
    /// - Iteration: foreach (var goal in registry.GetAllGoals())
    ///
    /// Pattern: Registry Pattern (similar to CommandFactory)
    /// </summary>
    public class AIGoalRegistry
    {
        private List<AIGoal> goals;
        private Dictionary<ushort, AIGoal> goalsByID;
        private Dictionary<string, AIGoal> goalsByName;
        private ushort nextID;

        public AIGoalRegistry()
        {
            goals = new List<AIGoal>(16); // Pre-allocate for typical goal count
            goalsByID = new Dictionary<ushort, AIGoal>(16);
            goalsByName = new Dictionary<string, AIGoal>(16);
            nextID = 1; // 0 reserved for "no goal"
        }

        /// <summary>
        /// Register a new goal (called during initialization).
        /// </summary>
        public void Register(AIGoal goal)
        {
            if (goal == null)
            {
                ArchonLogger.LogWarning("Cannot register null goal", "core_ai");
                return;
            }

            // Assign sequential ID
            goal.GoalID = nextID++;

            // Initialize goal (allocate buffers)
            goal.Initialize();

            // Store in collections
            goals.Add(goal);
            goalsByID[goal.GoalID] = goal;
            goalsByName[goal.GoalName] = goal;

            ArchonLogger.Log($"Registered AI goal: {goal.GoalName} (ID: {goal.GoalID})", "core_ai");
        }

        /// <summary>
        /// Get goal by ID (fast O(1) lookup).
        /// </summary>
        public AIGoal GetGoal(ushort goalID)
        {
            goalsByID.TryGetValue(goalID, out var goal);
            return goal;
        }

        /// <summary>
        /// Get goal by name (for debugging/testing).
        /// </summary>
        public AIGoal GetGoalByName(string name)
        {
            goalsByName.TryGetValue(name, out var goal);
            return goal;
        }

        /// <summary>
        /// Get all registered goals (for iteration during AI processing).
        /// </summary>
        public IReadOnlyList<AIGoal> GetAllGoals()
        {
            return goals;
        }

        /// <summary>
        /// Get count of registered goals.
        /// </summary>
        public int GoalCount => goals.Count;

        /// <summary>
        /// Dispose all goals (cleanup buffers).
        /// </summary>
        public void Dispose()
        {
            foreach (var goal in goals)
            {
                goal.Dispose();
            }

            goals.Clear();
            goalsByID.Clear();
            goalsByName.Clear();

            ArchonLogger.Log("AIGoalRegistry disposed", "core_ai");
        }
    }
}
