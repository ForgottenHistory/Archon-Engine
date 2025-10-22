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
                ArchonLogger.LogCoreSimulationError("SystemRegistry: Cannot register null system");
                return;
            }

            if (isInitialized)
            {
                ArchonLogger.LogCoreSimulationError($"SystemRegistry: Cannot register '{system.SystemName}' after initialization");
                return;
            }

            if (systems.Contains(system))
            {
                ArchonLogger.LogCoreSimulationWarning($"SystemRegistry: System '{system.SystemName}' already registered");
                return;
            }

            systems.Add(system);
            ArchonLogger.LogCoreSimulation($"SystemRegistry: Registered system '{system.SystemName}'");
        }

        /// <summary>
        /// Initialize all registered systems in dependency order
        /// Uses topological sort to determine correct initialization order
        /// </summary>
        public void InitializeAll()
        {
            if (isInitialized)
            {
                ArchonLogger.LogCoreSimulationWarning("SystemRegistry: Already initialized");
                return;
            }

            if (systems.Count == 0)
            {
                ArchonLogger.LogCoreSimulationWarning("SystemRegistry: No systems registered");
                return;
            }

            ArchonLogger.LogCoreSimulation($"SystemRegistry: Initializing {systems.Count} systems...");

            // Compute initialization order via topological sort
            var initializationOrder = TopologicalSort(systems);

            if (initializationOrder == null)
            {
                ArchonLogger.LogCoreSimulationError("SystemRegistry: Circular dependency detected - cannot initialize systems");
                return;
            }

            // Initialize systems in dependency order
            foreach (var system in initializationOrder)
            {
                system.Initialize();
            }

            isInitialized = true;
            ArchonLogger.LogCoreSimulation($"SystemRegistry: All {systems.Count} systems initialized successfully");
        }

        /// <summary>
        /// Shutdown all systems in reverse initialization order
        /// </summary>
        public void ShutdownAll()
        {
            if (!isInitialized)
            {
                ArchonLogger.LogCoreSimulationWarning("SystemRegistry: Cannot shutdown - not initialized");
                return;
            }

            ArchonLogger.LogCoreSimulation($"SystemRegistry: Shutting down {systems.Count} systems...");

            // Shutdown in reverse order
            for (int i = systems.Count - 1; i >= 0; i--)
            {
                systems[i].Shutdown();
            }

            isInitialized = false;
            ArchonLogger.LogCoreSimulation("SystemRegistry: All systems shutdown");
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
                ArchonLogger.LogCoreSimulationError($"SystemRegistry: Circular dependency detected involving '{system.SystemName}'");
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
                        ArchonLogger.LogCoreSimulationError($"SystemRegistry: System '{system.SystemName}' has null dependency");
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
