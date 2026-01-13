using System;
using System.Linq;
using System.Reflection;
using Utils;

namespace Core.AI
{
    /// <summary>
    /// Auto-discovery and registration for AI goals.
    ///
    /// Scans assemblies for classes with [Goal] attribute and registers them.
    /// Follows same pattern as SimpleCommandFactory.DiscoverAndRegister().
    ///
    /// Usage:
    /// // During initialization
    /// AIGoalDiscovery.DiscoverAndRegister(goalRegistry, Assembly.GetExecutingAssembly());
    ///
    /// // Or scan multiple assemblies
    /// AIGoalDiscovery.DiscoverAndRegister(goalRegistry,
    ///     typeof(EngineGoal).Assembly,   // ENGINE goals
    ///     typeof(GameGoal).Assembly);    // GAME goals
    /// </summary>
    public static class AIGoalDiscovery
    {
        /// <summary>
        /// Discover all AIGoal classes with [Goal] attribute and register them.
        /// </summary>
        /// <param name="registry">The AIGoalRegistry to register goals with.</param>
        /// <param name="assemblies">Assemblies to scan. If empty, uses calling assembly.</param>
        public static void DiscoverAndRegister(AIGoalRegistry registry, params Assembly[] assemblies)
        {
            if (registry == null)
            {
                ArchonLogger.LogError("AIGoalDiscovery: Registry is null", "core_ai");
                return;
            }

            if (assemblies == null || assemblies.Length == 0)
            {
                assemblies = new[] { Assembly.GetCallingAssembly() };
            }

            int count = 0;
            foreach (var assembly in assemblies)
            {
                try
                {
                    var goalTypes = assembly.GetTypes()
                        .Where(t => typeof(AIGoal).IsAssignableFrom(t)
                                 && !t.IsAbstract
                                 && t.GetCustomAttribute<GoalAttribute>() != null);

                    foreach (var type in goalTypes)
                    {
                        try
                        {
                            // Create instance via parameterless constructor
                            var goal = (AIGoal)Activator.CreateInstance(type);

                            // Get metadata for logging
                            var attr = type.GetCustomAttribute<GoalAttribute>();
                            string description = !string.IsNullOrEmpty(attr.Description)
                                ? $" - {attr.Description}"
                                : "";

                            // Register with registry (handles ID assignment and Initialize())
                            registry.Register(goal);

                            ArchonLogger.Log($"AIGoalDiscovery: Discovered {goal.GoalName}{description}", "core_ai");
                            count++;
                        }
                        catch (Exception e)
                        {
                            ArchonLogger.LogError($"AIGoalDiscovery: Failed to create {type.Name}: {e.Message}", "core_ai");
                        }
                    }
                }
                catch (ReflectionTypeLoadException e)
                {
                    // Some assemblies may have types that can't be loaded
                    ArchonLogger.LogWarning($"AIGoalDiscovery: Could not load some types from {assembly.GetName().Name}: {e.Message}", "core_ai");
                }
            }

            if (count > 0)
            {
                ArchonLogger.Log($"AIGoalDiscovery: Registered {count} AI goals", "core_ai");
            }
            else
            {
                ArchonLogger.LogWarning("AIGoalDiscovery: No goals found (did you add [Goal] attributes?)", "core_ai");
            }
        }
    }
}
