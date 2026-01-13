using System;

namespace Core.AI
{
    /// <summary>
    /// Marks an AIGoal class for auto-discovery and registration.
    ///
    /// Usage:
    /// [Goal]
    /// public class BuildEconomyGoal : AIGoal { ... }
    ///
    /// Or with metadata:
    /// [Goal(Description = "Economic development", Category = "Economy")]
    /// public class BuildEconomyGoal : AIGoal { ... }
    ///
    /// Discovery:
    /// AIGoalDiscovery.DiscoverAndRegister(registry, eventBus, assemblies);
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class GoalAttribute : Attribute
    {
        /// <summary>
        /// Description of what this goal does (for debugging/documentation).
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Category for grouping goals (e.g., "Economy", "Military", "Diplomacy").
        /// </summary>
        public string Category { get; set; }

        public GoalAttribute()
        {
            Description = "";
            Category = "";
        }
    }
}
