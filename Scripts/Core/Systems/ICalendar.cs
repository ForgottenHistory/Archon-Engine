namespace Core.Systems
{
    /// <summary>
    /// ENGINE: Calendar abstraction for custom date systems.
    ///
    /// PATTERN: Engine-Game Separation (Pattern 1)
    /// - ENGINE provides: ICalendar interface + StandardCalendar default
    /// - GAME provides: Custom calendars (Roman AUC, Fantasy 13-month, etc.)
    ///
    /// Use Cases:
    /// - Historical games: AD/BC eras, real month names
    /// - Roman games: AUC (Ab Urbe Condita) dating
    /// - Fantasy games: Custom month names, different year lengths
    ///
    /// All implementations must be deterministic for multiplayer compatibility.
    /// </summary>
    public interface ICalendar
    {
        // === Core Calendar Properties ===

        /// <summary>Hours per day (typically 24)</summary>
        int HoursPerDay { get; }

        /// <summary>Number of months in a year</summary>
        int MonthsPerYear { get; }

        /// <summary>Total days in a year (for year-based calculations)</summary>
        int DaysPerYear { get; }

        // === Month Operations ===

        /// <summary>
        /// Get days in specified month (1-indexed).
        /// Standard calendar returns 30 for all months.
        /// Custom calendars can vary (28-31 for realistic, any for fantasy).
        /// </summary>
        int GetDaysInMonth(int month);

        /// <summary>
        /// Get localized month name.
        /// Standard calendar returns "Month 1", "Month 2", etc.
        /// GAME layer provides real names ("January", "Ianuarius", etc.)
        /// </summary>
        string GetMonthName(int month);

        /// <summary>
        /// Get abbreviated month name for compact UI.
        /// Standard calendar returns "M1", "M2", etc.
        /// GAME layer provides real abbreviations ("Jan", "Feb", etc.)
        /// </summary>
        string GetMonthAbbreviation(int month);

        // === Era/Year Formatting ===

        /// <summary>
        /// Format year with era designation.
        /// Examples: "1444 AD", "753 BC", "AUC 507"
        /// Year 0 = 1 BC in standard AD/BC system.
        /// </summary>
        string FormatYear(int year);

        /// <summary>
        /// Format full date for display.
        /// Example: "11 November 1444 AD"
        /// </summary>
        string FormatDate(GameTime time);

        /// <summary>
        /// Format compact date for limited UI space.
        /// Example: "1444.11.11" or "11 Nov 1444"
        /// </summary>
        string FormatDateCompact(GameTime time);

        // === Validation ===

        /// <summary>
        /// Validate that date components are within calendar bounds.
        /// Returns true if month is 1-MonthsPerYear and day is 1-GetDaysInMonth(month).
        /// </summary>
        bool IsValidDate(int year, int month, int day);

        /// <summary>
        /// Clamp date to valid range for this calendar.
        /// Useful when loading saves or setting dates programmatically.
        /// </summary>
        GameTime ClampToValidDate(int year, int month, int day, int hour);
    }
}
