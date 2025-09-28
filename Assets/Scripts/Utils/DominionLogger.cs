using UnityEngine;

/// <summary>
/// Static helper class for easy logging throughout the Dominion project
/// Provides a unified logging interface with direct file logging
/// </summary>
public static class DominionLogger
{
    private static bool logToConsole = true;
    private static bool logToFile = true;

    public static void Log(string message)
    {
        // Direct approach - call both systems explicitly
        if (logToConsole) Debug.Log(message);
        if (logToFile && !FileLogger.IsQuitting && Application.isPlaying)
        {
            var logger = FileLogger.Instance;
            if (logger != null) logger.WriteLogDirect(message, LogType.Log);
        }
    }

    public static void LogWarning(string message)
    {
        if (logToConsole) Debug.LogWarning(message);
        if (logToFile && !FileLogger.IsQuitting && Application.isPlaying)
        {
            var logger = FileLogger.Instance;
            if (logger != null) logger.WriteLogDirect(message, LogType.Warning);
        }
    }

    public static void LogError(string message)
    {
        if (logToConsole) Debug.LogError(message);
        if (logToFile && !FileLogger.IsQuitting && Application.isPlaying)
        {
            var logger = FileLogger.Instance;
            if (logger != null) logger.WriteLogDirect(message, LogType.Error);
        }
    }

    public static void LogFormat(string format, params object[] args)
    {
        string formattedMessage = string.Format(format, args);
        Log(formattedMessage);
    }

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