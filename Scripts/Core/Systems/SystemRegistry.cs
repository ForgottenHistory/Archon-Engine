using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Core.Systems
{
    /// <summary>
    /// ENGINE LAYER - Manages game system registration and initialization order
    ///
    /// Responsibilities:
    /// - Register all game systems
    /// - Determine initialization order via topological sort
    /// - Initialize systems in dependency order
    /// - Detect circular dependencies
    /// - Provide access to registered systems
    ///
    /// Architecture:
    /// - Pure mechanism, no game-specific knowledge
    /// - Systems register themselves (or are registered by initializer)
    /// - Dependency graph built from GameSystem.GetDependencies()
    /// - Initialization order computed automatically
    ///
    /// Usage:
    /// var registry = new SystemRegistry();
    /// registry.Register(timeManager);
    /// registry.Register(economySystem);
    /// registry.InitializeAll(); // Initializes in dependency order
    ///
    /// Benefits:
    /// - No manual initialization order management
    /// - Circular dependency detection at startup
    /// - Missing dependency errors before runtime crashes
    /// - Easy to add new systems (just register)
    /// </summary>
    public class SystemRegistry
    {
        private readonly List<GameSystem> systems = new List<GameSystem>();
        private bool isInitialized = false;

        /// <summary>
        /// Register a system with the registry
        /// Must be called before InitializeAll()
        /// </summary>
        public void Register(GameSystem system)
        {
            if (system == null)
            {
                ArchonLogger.LogError("SystemRegistry: Cannot register null system", "core_simulation");
                return;
            }

            if (isInitialized)
            {
                ArchonLogger.LogError($"SystemRegistry: Cannot register '{system.SystemName}' after initialization", "core_simulation");
                return;
            }

            if (systems.Contains(system))
            {
                ArchonLogger.LogWarning($"SystemRegistry: System '{system.SystemName}' already registered", "core_simulation");
                return;
            }

            systems.Add(system);
            ArchonLogger.Log($"SystemRegistry: Registered system '{system.SystemName}'", "core_simulation");
        }

        /// <summary>
        /// Initialize all registered systems in dependency order
        /// Uses topological sort to determine correct initialization order
        /// </summary>
        public void InitializeAll()
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("SystemRegistry: Already initialized", "core_simulation");
                return;
            }

            if (systems.Count == 0)
            {
                ArchonLogger.LogWarning("SystemRegistry: No systems registered", "core_simulation");
                return;
            }

            ArchonLogger.Log($"SystemRegistry: Initializing {systems.Count} systems...", "core_simulation");

            // Compute initialization order via topological sort
            var initializationOrder = TopologicalSort(systems);

            if (initializationOrder == null)
            {
                ArchonLogger.LogError("SystemRegistry: Circular dependency detected - cannot initialize systems", "core_simulation");
                return;
            }

            // Initialize systems in dependency order
            foreach (var system in initializationOrder)
            {
                system.Initialize();
            }

            isInitialized = true;
            ArchonLogger.Log($"SystemRegistry: All {systems.Count} systems initialized successfully", "core_simulation");
        }

        /// <summary>
        /// Shutdown all systems in reverse initialization order
        /// </summary>
        public void ShutdownAll()
        {
            if (!isInitialized)
            {
                ArchonLogger.LogWarning("SystemRegistry: Cannot shutdown - not initialized", "core_simulation");
                return;
            }

            ArchonLogger.Log($"SystemRegistry: Shutting down {systems.Count} systems...", "core_simulation");

            // Shutdown in reverse order
            for (int i = systems.Count - 1; i >= 0; i--)
            {
                systems[i].Shutdown();
            }

            isInitialized = false;
            ArchonLogger.Log("SystemRegistry: All systems shutdown", "core_simulation");
        }

        /// <summary>
        /// Get a registered system by type
        /// Returns null if system not found
        /// </summary>
        public T GetSystem<T>() where T : GameSystem
        {
            foreach (var system in systems)
            {
                if (system is T typedSystem)
                    return typedSystem;
            }
            return null;
        }

        /// <summary>
        /// Check if a system is registered
        /// </summary>
        public bool IsRegistered<T>() where T : GameSystem
        {
            return GetSystem<T>() != null;
        }

        /// <summary>
        /// Topological sort - determine initialization order from dependency graph
        /// Returns null if circular dependency detected
        /// </summary>
        private List<GameSystem> TopologicalSort(List<GameSystem> systemsToSort)
        {
            var sorted = new List<GameSystem>();
            var visited = new HashSet<GameSystem>();
            var visiting = new HashSet<GameSystem>();

            foreach (var system in systemsToSort)
            {
                if (!Visit(system, visited, visiting, sorted))
                {
                    // Circular dependency detected
                    return null;
                }
            }

            return sorted;
        }

        /// <summary>
        /// Depth-first search for topological sort
        /// Returns false if circular dependency detected
        /// </summary>
        private bool Visit(GameSystem system, HashSet<GameSystem> visited, HashSet<GameSystem> visiting, List<GameSystem> sorted)
        {
            if (visited.Contains(system))
                return true; // Already processed

            if (visiting.Contains(system))
            {
                // Circular dependency detected!
                ArchonLogger.LogError($"SystemRegistry: Circular dependency detected involving '{system.SystemName}'", "core_simulation");
                return false;
            }

            visiting.Add(system);

            // Visit all dependencies first
            var dependencies = system.GetDependencies();
            if (dependencies != null)
            {
                foreach (var dependency in dependencies)
                {
                    if (dependency == null)
                    {
                        ArchonLogger.LogError($"SystemRegistry: System '{system.SystemName}' has null dependency", "core_simulation");
                        return false;
                    }

                    if (!Visit(dependency, visited, visiting, sorted))
                        return false; // Circular dependency in child
                }
            }

            visiting.Remove(system);
            visited.Add(system);
            sorted.Add(system); // Add after all dependencies

            return true;
        }

        /// <summary>
        /// Get all registered systems (for debugging)
        /// </summary>
        public IReadOnlyList<GameSystem> GetAllSystems()
        {
            return systems.AsReadOnly();
        }

        /// <summary>
        /// Get initialization status
        /// </summary>
        public bool IsInitialized => isInitialized;
    }
}
