using System.Collections.Generic;
using UnityEngine;

namespace Core.Systems
{
    /// <summary>
    /// ENGINE LAYER - Base class for all game systems with standardized lifecycle
    ///
    /// Responsibilities:
    /// - Define standard system lifecycle (Initialize, Shutdown, Save, Load)
    /// - Enforce dependency validation before initialization
    /// - Prevent re-initialization and initialization of missing dependencies
    /// - Provide consistent logging for system operations
    ///
    /// Architecture:
    /// - Pure mechanism, no game-specific knowledge
    /// - Systems declare dependencies explicitly via GetDependencies()
    /// - Initialization order determined by dependency graph
    /// - All state changes go through standard lifecycle methods
    ///
    /// Usage (Game Layer):
    /// public class EconomySystem : GameSystem
    /// {
    ///     public override string SystemName => "Economy";
    ///
    ///     protected override IEnumerable<GameSystem> GetDependencies()
    ///     {
    ///         yield return timeManager;
    ///         yield return provinceSystem;
    ///     }
    ///
    ///     protected override void OnInitialize()
    ///     {
    ///         // Dependencies guaranteed to be initialized here
    ///         timeManager.OnMonthlyTick += CollectTaxes;
    ///     }
    /// }
    ///
    /// Benefits:
    /// - No load order bugs (dependencies validated)
    /// - No circular dependency crashes (detected at startup)
    /// - Easy testing (mock dependencies)
    /// - Save/load support (standard serialization hooks)
    /// - Self-documenting (dependencies explicit)
    /// </summary>
    public abstract class GameSystem : MonoBehaviour
    {
        /// <summary>
        /// Unique name for this system (used in logging and debugging)
        /// </summary>
        public abstract string SystemName { get; }

        /// <summary>
        /// Whether this system has been initialized
        /// Systems cannot be used until initialized
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Get all systems this system depends on
        /// Dependencies must be initialized before this system
        /// Override to declare dependencies (return empty if none)
        /// Internal visibility allows SystemRegistry to access for dependency resolution
        /// </summary>
        protected internal virtual IEnumerable<GameSystem> GetDependencies()
        {
            yield break; // Default: no dependencies
        }

        /// <summary>
        /// Initialize this system (call after all dependencies initialized)
        /// Validates dependencies before calling OnInitialize()
        /// </summary>
        public void Initialize()
        {
            if (IsInitialized)
            {
                ArchonLogger.LogWarning($"{SystemName}: Already initialized, skipping", "core_simulation");
                return;
            }

            // Validate dependencies exist and are initialized
            var dependencies = GetDependencies();
            foreach (var dependency in dependencies)
            {
                if (dependency == null)
                {
                    ArchonLogger.LogError($"{SystemName}: Missing dependency (null reference)", "core_simulation");
                    return;
                }

                if (!dependency.IsInitialized)
                {
                    ArchonLogger.LogError($"{SystemName}: Dependency '{dependency.SystemName}' not initialized yet", "core_simulation");
                    return;
                }
            }

            // Call concrete implementation
            OnInitialize();

            IsInitialized = true;
            ArchonLogger.Log($"{SystemName}: System initialized successfully", "core_simulation");
        }

        /// <summary>
        /// Perform system-specific initialization
        /// All dependencies guaranteed to be initialized when this is called
        /// Override to set up system state, subscribe to events, etc.
        /// </summary>
        protected abstract void OnInitialize();

        /// <summary>
        /// Shutdown this system cleanly
        /// Unsubscribe from events, release resources, etc.
        /// Called before scene unload or game exit
        /// </summary>
        public void Shutdown()
        {
            if (!IsInitialized)
            {
                ArchonLogger.LogWarning($"{SystemName}: Cannot shutdown - not initialized", "core_simulation");
                return;
            }

            OnShutdown();
            IsInitialized = false;
            ArchonLogger.Log($"{SystemName}: System shutdown", "core_simulation");
        }

        /// <summary>
        /// Perform system-specific shutdown
        /// Override to clean up resources, unsubscribe events, etc.
        /// </summary>
        protected virtual void OnShutdown()
        {
            // Default: no shutdown logic
        }

        /// <summary>
        /// Unity lifecycle - automatically shutdown on destroy
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (IsInitialized)
            {
                Shutdown();
            }
        }

        /// <summary>
        /// Save system state to save data
        /// Override to serialize system state for save/load
        /// </summary>
        protected virtual void OnSave(Core.SaveLoad.SaveGameData saveData)
        {
            // Default: no save logic
        }

        /// <summary>
        /// Load system state from save data
        /// Override to deserialize system state for save/load
        /// </summary>
        protected virtual void OnLoad(Core.SaveLoad.SaveGameData saveData)
        {
            // Default: no load logic
        }

        /// <summary>
        /// Check if this system is ready to be used
        /// Systems should check this before performing operations
        /// </summary>
        protected void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                ArchonLogger.LogError($"{SystemName}: System not initialized - cannot perform operation", "core_simulation");
            }
        }

        /// <summary>
        /// Log a system-specific message
        /// Prefixes message with system name for easier debugging
        /// </summary>
        protected void LogSystem(string message)
        {
            ArchonLogger.Log($"{SystemName}: {message}", "core_simulation");
        }

        /// <summary>
        /// Log a system-specific warning
        /// </summary>
        protected void LogSystemWarning(string message)
        {
            ArchonLogger.LogWarning($"{SystemName}: {message}", "core_simulation");
        }

        /// <summary>
        /// Log a system-specific error
        /// </summary>
        protected void LogSystemError(string message)
        {
            ArchonLogger.LogError($"{SystemName}: {message}", "core_simulation");
        }
    }
}
