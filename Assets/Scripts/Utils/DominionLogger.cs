using UnityEngine;

/// <summary>
/// Static helper class for easy logging throughout the Dominion project
/// Provides a unified logging interface with optional file logging support
/// </summary>
public static class DominionLogger
{
    public static void Log(string message)
    {
        Debug.Log(message);
    }

    public static void LogWarning(string message)
    {
        Debug.LogWarning(message);
    }

    public static void LogError(string message)
    {
        Debug.LogError(message);
    }

    public static void LogFormat(string format, params object[] args)
    {
        Debug.LogFormat(format, args);
    }

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