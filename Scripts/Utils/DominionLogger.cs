using UnityEngine;

/// <summary>
/// Static helper class for easy logging throughout the Dominion project
/// Provides a unified logging interface with direct file logging
/// </summary>
public static class DominionLogger
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

    // System-specific convenience methods
    public static class Systems
    {
        public const string DataLinking = "data_linking";
        public const string MapInitialization = "map_initialization";
        public const string Provinces = "provinces";
        public const string Countries = "countries";
        public const string MapGeneration = "map_generation";
        public const string GameInitialization = "game_initialization";
        public const string Performance = "performance";
        public const string Network = "network";
        public const string AI = "ai";
        public const string UI = "ui";
        public const string Game = "game"; // GAME layer logs (policy, not engine mechanism)
    }

    // Convenience methods for data linking system
    public static void LogDataLinking(string message) => Log(message, Systems.DataLinking);
    public static void LogDataLinkingWarning(string message) => LogWarning(message, Systems.DataLinking);
    public static void LogDataLinkingError(string message) => LogError(message, Systems.DataLinking);

    // Convenience methods for map initialization system
    public static void LogMapInit(string message) => Log(message, Systems.MapInitialization);
    public static void LogMapInitWarning(string message) => LogWarning(message, Systems.MapInitialization);
    public static void LogMapInitError(string message) => LogError(message, Systems.MapInitialization);

    // Convenience methods for game layer system
    public static void LogGame(string message) => Log(message, Systems.Game);
    public static void LogGameWarning(string message) => LogWarning(message, Systems.Game);
    public static void LogGameError(string message) => LogError(message, Systems.Game);

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