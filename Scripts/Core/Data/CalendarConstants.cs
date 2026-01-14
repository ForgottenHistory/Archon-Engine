namespace Core.Data
{
    /// <summary>
    /// ENGINE: Single source of truth for calendar constants.
    /// All time-related code should reference these constants instead of hardcoding values.
    ///
    /// Design Rationale:
    /// - Real Earth calendar: 365 days, real month lengths (31/30/28)
    /// - No leap years: Deterministic for multiplayer (Feb always 28 days)
    /// - Pre-calculated derived constants: Avoid multiplication in hot paths
    ///
    /// MULTIPLAYER CRITICAL: These constants define the time unit conversions
    /// that must be identical across all clients.
    /// </summary>
    public static class CalendarConstants
    {
        // === Base Time Units ===

        /// <summary>Hours per day (universal constant)</summary>
        public const int HOURS_PER_DAY = 24;

        /// <summary>Days per week (for weekly tick events)</summary>
        public const int DAYS_PER_WEEK = 7;

        // === Standard Calendar (Real Earth, no leap years) ===

        /// <summary>Months per year</summary>
        public const int MONTHS_PER_YEAR = 12;

        /// <summary>Days per year (365, no leap years for determinism)</summary>
        public const int DAYS_PER_YEAR = 365;

        /// <summary>
        /// Days in each month (1-indexed, index 0 unused).
        /// Jan=31, Feb=28 (no leap), Mar=31, Apr=30, May=31, Jun=30,
        /// Jul=31, Aug=31, Sep=30, Oct=31, Nov=30, Dec=31
        /// </summary>
        public static readonly int[] DAYS_IN_MONTH = {
            0,   // Index 0 unused (months are 1-indexed)
            31,  // January
            28,  // February (no leap years)
            31,  // March
            30,  // April
            31,  // May
            30,  // June
            31,  // July
            31,  // August
            30,  // September
            31,  // October
            30,  // November
            31   // December
        };

        /// <summary>
        /// Cumulative days before each month (1-indexed, for fast day-of-year calculation).
        /// Index 1 = 0 (Jan starts at day 0), Index 2 = 31 (Feb starts at day 31), etc.
        /// </summary>
        public static readonly int[] DAYS_BEFORE_MONTH = {
            0,    // Index 0 unused
            0,    // Before January
            31,   // Before February
            59,   // Before March (31+28)
            90,   // Before April
            120,  // Before May
            151,  // Before June
            181,  // Before July
            212,  // Before August
            243,  // Before September
            273,  // Before October
            304,  // Before November
            334   // Before December
        };

        // === Pre-Calculated Derived Constants (Hot Path Optimization) ===

        /// <summary>Hours per week (24 × 7 = 168)</summary>
        public const int HOURS_PER_WEEK = HOURS_PER_DAY * DAYS_PER_WEEK;

        /// <summary>Hours per year (24 × 365 = 8760)</summary>
        public const int HOURS_PER_YEAR = HOURS_PER_DAY * DAYS_PER_YEAR;

        // === Simplified Calendar Constants (for games that want 360-day years) ===

        /// <summary>Simplified: days per month (all months equal)</summary>
        public const int SIMPLIFIED_DAYS_PER_MONTH = 30;

        /// <summary>Simplified: days per year (30 × 12 = 360)</summary>
        public const int SIMPLIFIED_DAYS_PER_YEAR = 360;

        /// <summary>Simplified: hours per year (24 × 360 = 8640)</summary>
        public const int SIMPLIFIED_HOURS_PER_YEAR = HOURS_PER_DAY * SIMPLIFIED_DAYS_PER_YEAR;
    }
}
