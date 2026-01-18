using UnityEngine;

/// <summary>
/// Unified logging for both ENGINE and GAME layers
///
/// Usage:
///   ArchonLogger.Log("Message", "subsystem_name");
///   ArchonLogger.LogWarning("Warning", "subsystem_name");
///   ArchonLogger.LogError("Error", "subsystem_name");
///
/// Subsystem constants available in ArchonLogger.Systems for autocomplete
/// </summary>
public static class ArchonLogger
{
    private static bool logToConsole = true;
    private static bool logToFile = true;

    /// <summary>
    /// Log an informational message.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="system">Optional subsystem identifier for routing to separate log files.</param>
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

    /// <summary>
    /// Log a warning message.
    /// </summary>
    /// <param name="message">The warning message to log.</param>
    /// <param name="system">Optional subsystem identifier for routing to separate log files.</param>
    public static void LogWarning(string message, string system = null)
    {
        if (logToConsole) Debug.LogWarning(message);
        if (logToFile && !FileLogger.IsQuitting && Application.isPlaying)
        {
            var logger = FileLogger.Instance;
            if (logger != null) logger.WriteLogDirect(message, system, LogType.Warning);
        }
    }

    /// <summary>
    /// Log an error message.
    /// </summary>
    /// <param name="message">The error message to log.</param>
    /// <param name="system">Optional subsystem identifier for routing to separate log files.</param>
    public static void LogError(string message, string system = null)
    {
        if (logToConsole) Debug.LogError(message);
        if (logToFile && !FileLogger.IsQuitting && Application.isPlaying)
        {
            var logger = FileLogger.Instance;
            if (logger != null) logger.WriteLogDirect(message, system, LogType.Error);
        }
    }

    /// <summary>
    /// Log a formatted message using string.Format syntax.
    /// </summary>
    /// <param name="format">Format string with placeholders.</param>
    /// <param name="args">Arguments to insert into format string.</param>
    public static void LogFormat(string format, params object[] args)
    {
        string formattedMessage = string.Format(format, args);
        Log(formattedMessage);
    }

    // System-specific subsystem constants - ALL LAYERS
    // Use ArchonLogger.Log(message, ArchonLogger.Systems.XXX) throughout codebase
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
        public const string CoreDiplomacy = "core_diplomacy";        // DiplomacySystem, relations, wars, opinion modifiers
        public const string CoreAI = "core_ai";                      // AISystem, goal evaluation, AI decisions

        // === MAP LAYER (Archon-Engine/Scripts/Map/) ===
        // GPU-accelerated presentation - textures, rendering, interaction
        public const string MapRendering = "map_rendering";          // MapRenderer, texture updates, GPU operations
        public const string MapTextures = "map_textures";            // MapTextureManager, texture sets, palette
        public const string MapInitialization = "map_initialization"; // MapInitializer, system setup
        public const string MapInteraction = "map_interaction";      // ProvinceSelector, mouse input, selection
        public const string MapModes = "map_modes";                  // MapModeManager, mode switching

        // === GAME LAYER (Assets/Game/) ===
        // Hegemon-specific gameplay systems
        public const string GameHegemon = "game_hegemon";            // General Hegemon gameplay (economy, buildings, units)
        public const string GameUI = "game_ui";                      // UI interactions, panels, tooltips
        public const string GameSystems = "game_systems";            // EconomySystem, BuildingConstructionSystem, etc.
        public const string GameInitialization = "game_initialization"; // Game startup, scenario loading
        public const string GameAI = "game_ai";                      // AI goals (BuildEconomy, ExpandTerritory)

        // === LEGACY (for backward compatibility, will migrate) ===
        public const string Provinces = "provinces";                 // LEGACY - use CoreSimulation
        public const string Countries = "countries";                 // LEGACY - use CoreSimulation
        public const string MapGeneration = "map_generation";        // LEGACY - use MapRendering
        public const string Performance = "performance";             // Cross-cutting concern
        public const string Network = "network";                     // Cross-cutting concern (future multiplayer)
    }

    #region Configuration

    /// <summary>Enable or disable logging to Unity console.</summary>
    public static void SetConsoleLogging(bool enabled) => logToConsole = enabled;

    /// <summary>Enable or disable logging to file.</summary>
    public static void SetFileLogging(bool enabled) => logToFile = enabled;

    #endregion

    #region Formatting Helpers

    /// <summary>
    /// Log a section header for visual separation in logs.
    /// </summary>
    /// <param name="sectionName">Name of the section.</param>
    public static void LogSection(string sectionName)
    {
        Log($"\n========== {sectionName} ==========\n");
    }

    /// <summary>
    /// Log a separator line for visual grouping in logs.
    /// </summary>
    /// <param name="title">Optional title to include in separator.</param>
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

    #endregion
}