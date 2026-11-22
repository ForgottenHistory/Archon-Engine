namespace Core
{
    /// <summary>
    /// Logging verbosity level for all engine and game systems.
    /// Lives in Utils (namespace Core) so ArchonLogger can filter on it; set from GameSettings.LogLevel.
    /// </summary>
    public enum LogLevel
    {
        None = 0,      // No logging
        Errors = 1,    // Only errors
        Warnings = 2,  // Errors and warnings
        Info = 3,      // Normal operational info: startup, loading, rare events (war declared)
        Debug = 4      // Verbose: per-event and per-step detail (unit created, province changed, render passes)
    }
}
