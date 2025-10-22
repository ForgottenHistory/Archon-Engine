using UnityEngine;

/// <summary>
/// Static helper class for easy logging throughout the Archon project
/// Provides a unified logging interface with direct file logging
/// </summary>
public static class ArchonLogger
{
    private static bool logToConsole = true;
    private static bool logToFile = true;

    public static void Log(string message, string system = null)
    {
        // Direct approach - call both systems explicitly
        if (logToConsole) Debug.Log(message);
        if (logToFile && !FileLogger.IsQuitting && Application.isPlaying)
        {
            var logger = FileLogger.Instance;
            if (logger != null) logger.WriteLogDirect(message, system, LogType.Log);
        }
    }

    public static void LogWarning(string message, string system = null)
    {
        if (logToConsole) Debug.LogWarning(message);
        if (logToFile && !FileLogger.IsQuitting && Application.isPlaying)
        {
            var logger = FileLogger.Instance;
            if (logger != null) logger.WriteLogDirect(message, system, LogType.Warning);
        }
    }

    public static void LogError(string message, string system = null)
    {
        if (logToConsole) Debug.LogError(message);
        if (logToFile && !FileLogger.IsQuitting && Application.isPlaying)
        {
            var logger = FileLogger.Instance;
            if (logger != null) logger.WriteLogDirect(message, system, LogType.Error);
        }
    }

    public static void LogFormat(string format, params object[] args)
    {
        string formattedMessage = string.Format(format, args);
        Log(formattedMessage);
    }

    // System-specific subsystem constants - ENGINE LAYER ONLY
    // NOTE: GAME layer logging constants must be defined in Game/GameLogger.cs
    // to maintain ENGINE-GAME separation
    public static class Systems
    {
        // === CORE LAYER (Archon-Engine/Scripts/Core/) ===
        // Deterministic simulation - game state, logic, commands
        public const string CoreSimulation = "core_simulation";      // ProvinceSystem, CountrySystem, state changes
        public const string CoreCommands = "core_commands";          // CommandProcessor, command execution, validation
        public const string CoreTime = "core_time";                  // TimeManager, tick events, time progression
        public const string CoreDataLoading = "core_data_loading";   // Loaders (Scenario, Province, Country, Burst)
        public const string CoreDataLinking = "core_data_linking";   // CrossReferenceBuilder, ReferenceResolver
        public const string CoreSaveLoad = "core_saveload";          // SaveManager, serialization, load/save
        public const string CoreEvents = "core_events";              // EventBus, event dispatching (optional, can be noisy)

        // === MAP LAYER (Archon-Engine/Scripts/Map/) ===
        // GPU-accelerated presentation - textures, rendering, interaction
        public const string MapRendering = "map_rendering";          // MapRenderer, texture updates, GPU operations
        public const string MapTextures = "map_textures";            // MapTextureManager, texture sets, palette
        public const string MapInitialization = "map_initialization"; // MapInitializer, system setup
        public const string MapInteraction = "map_interaction";      // ProvinceSelector, mouse input, selection
        public const string MapModes = "map_modes";                  // MapModeManager, mode switching

        // === LEGACY (for backward compatibility, will migrate) ===
        public const string Provinces = "provinces";                 // LEGACY - use CoreSimulation
        public const string Countries = "countries";                 // LEGACY - use CoreSimulation
        public const string MapGeneration = "map_generation";        // LEGACY - use MapRendering
        public const string Performance = "performance";             // Cross-cutting concern
        public const string Network = "network";                     // Cross-cutting concern (future multiplayer)
    }

    // === CORE LAYER CONVENIENCE METHODS ===

    // Core Simulation (ProvinceSystem, CountrySystem, state changes)
    public static void LogCoreSimulation(string message) => Log(message, Systems.CoreSimulation);
    public static void LogCoreSimulationWarning(string message) => LogWarning(message, Systems.CoreSimulation);
    public static void LogCoreSimulationError(string message) => LogError(message, Systems.CoreSimulation);

    // Core Commands (CommandProcessor, command execution)
    public static void LogCoreCommands(string message) => Log(message, Systems.CoreCommands);
    public static void LogCoreCommandsWarning(string message) => LogWarning(message, Systems.CoreCommands);
    public static void LogCoreCommandsError(string message) => LogError(message, Systems.CoreCommands);

    // Core Time (TimeManager, tick events)
    public static void LogCoreTime(string message) => Log(message, Systems.CoreTime);
    public static void LogCoreTimeWarning(string message) => LogWarning(message, Systems.CoreTime);
    public static void LogCoreTimeError(string message) => LogError(message, Systems.CoreTime);

    // Core Data Loading (all loaders)
    public static void LogCoreDataLoading(string message) => Log(message, Systems.CoreDataLoading);
    public static void LogCoreDataLoadingWarning(string message) => LogWarning(message, Systems.CoreDataLoading);
    public static void LogCoreDataLoadingError(string message) => LogError(message, Systems.CoreDataLoading);

    // Core Data Linking (cross-references, string→ID resolution)
    public static void LogDataLinking(string message) => Log(message, Systems.CoreDataLinking);
    public static void LogDataLinkingWarning(string message) => LogWarning(message, Systems.CoreDataLinking);
    public static void LogDataLinkingError(string message) => LogError(message, Systems.CoreDataLinking);

    // Core Save/Load (SaveManager, serialization)
    public static void LogCoreSaveLoad(string message) => Log(message, Systems.CoreSaveLoad);
    public static void LogCoreSaveLoadWarning(string message) => LogWarning(message, Systems.CoreSaveLoad);
    public static void LogCoreSaveLoadError(string message) => LogError(message, Systems.CoreSaveLoad);

    // Core Events (EventBus - use sparingly, can be noisy)
    public static void LogCoreEvents(string message) => Log(message, Systems.CoreEvents);
    public static void LogCoreEventsWarning(string message) => LogWarning(message, Systems.CoreEvents);
    public static void LogCoreEventsError(string message) => LogError(message, Systems.CoreEvents);

    // === MAP LAYER CONVENIENCE METHODS ===

    // Map Rendering (MapRenderer, texture updates, GPU)
    public static void LogMapRendering(string message) => Log(message, Systems.MapRendering);
    public static void LogMapRenderingWarning(string message) => LogWarning(message, Systems.MapRendering);
    public static void LogMapRenderingError(string message) => LogError(message, Systems.MapRendering);

    // Map Textures (MapTextureManager, texture sets)
    public static void LogMapTextures(string message) => Log(message, Systems.MapTextures);
    public static void LogMapTexturesWarning(string message) => LogWarning(message, Systems.MapTextures);
    public static void LogMapTexturesError(string message) => LogError(message, Systems.MapTextures);

    // Map Initialization (MapInitializer, system setup)
    public static void LogMapInit(string message) => Log(message, Systems.MapInitialization);
    public static void LogMapInitWarning(string message) => LogWarning(message, Systems.MapInitialization);
    public static void LogMapInitError(string message) => LogError(message, Systems.MapInitialization);

    // Map Interaction (ProvinceSelector, mouse input)
    public static void LogMapInteraction(string message) => Log(message, Systems.MapInteraction);
    public static void LogMapInteractionWarning(string message) => LogWarning(message, Systems.MapInteraction);
    public static void LogMapInteractionError(string message) => LogError(message, Systems.MapInteraction);

    // Map Modes (MapModeManager, mode switching)
    public static void LogMapModes(string message) => Log(message, Systems.MapModes);
    public static void LogMapModesWarning(string message) => LogWarning(message, Systems.MapModes);
    public static void LogMapModesError(string message) => LogError(message, Systems.MapModes);

    // === GAME LAYER LOGGING ===
    // NOTE: GAME layer should use GameLogger.cs (in Assets/Game/)
    // to maintain ENGINE-GAME separation. ArchonLogger is ENGINE ONLY.

    // Configuration methods
    public static void SetConsoleLogging(bool enabled) => logToConsole = enabled;
    public static void SetFileLogging(bool enabled) => logToFile = enabled;

    public static void LogSection(string sectionName)
    {
        Log($"\n========== {sectionName} ==========\n");
    }

    public static void LogSeparator(string title = null)
    {
        if (string.IsNullOrEmpty(title))
        {
            Log("----------------------------------------");
        }
        else
        {
            Log($"---------- {title} ----------");
        }
    }
}