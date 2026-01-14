using Core.Data;

namespace Core.Systems
{
    /// <summary>
    /// ENGINE: Default Earth calendar with real month lengths.
    ///
    /// Features:
    /// - Real month lengths (31/30/28 days)
    /// - 365 days per year (no leap years for determinism)
    /// - Standard AD/BC era formatting
    /// - English month names (GAME layer can override with localized names)
    ///
    /// For simplified 360-day calendar, use SimplifiedCalendar instead.
    /// </summary>
    public class StandardCalendar : ICalendar
    {
        // === Month Names ===

        private static readonly string[] MonthNames =
        {
            "", // Index 0 unused
            "January", "February", "March", "April",
            "May", "June", "July", "August",
            "September", "October", "November", "December"
        };

        private static readonly string[] MonthAbbreviations =
        {
            "", // Index 0 unused
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        };

        // === ICalendar Properties ===

        public int HoursPerDay => CalendarConstants.HOURS_PER_DAY;
        public int MonthsPerYear => CalendarConstants.MONTHS_PER_YEAR;
        public int DaysPerYear => CalendarConstants.DAYS_PER_YEAR;

        // === Month Operations ===

        public int GetDaysInMonth(int month)
        {
            if (month < 1 || month > 12)
                return 30; // Fallback for invalid month

            return CalendarConstants.DAYS_IN_MONTH[month];
        }

        public string GetMonthName(int month)
        {
            if (month < 1 || month > 12)
                return $"Month {month}";

            return MonthNames[month];
        }

        public string GetMonthAbbreviation(int month)
        {
            if (month < 1 || month > 12)
                return $"M{month}";

            return MonthAbbreviations[month];
        }

        // === Era/Year Formatting ===

        public string FormatYear(int year)
        {
            if (year <= 0)
            {
                // Year 0 = 1 BC, Year -1 = 2 BC, etc.
                return $"{1 - year} BC";
            }
            return $"{year} AD";
        }

        public string FormatDate(GameTime time)
        {
            // Format: "11 November 1444 AD"
            return $"{time.Day} {GetMonthName(time.Month)} {FormatYear(time.Year)}";
        }

        public string FormatDateCompact(GameTime time)
        {
            // Format: "11 Nov 1444" (no era suffix for compactness)
            int displayYear = time.Year <= 0 ? 1 - time.Year : time.Year;
            string suffix = time.Year <= 0 ? " BC" : "";
            return $"{time.Day} {GetMonthAbbreviation(time.Month)} {displayYear}{suffix}";
        }

        // === Validation ===

        public bool IsValidDate(int year, int month, int day)
        {
            if (month < 1 || month > MonthsPerYear)
                return false;

            int maxDay = GetDaysInMonth(month);
            return day >= 1 && day <= maxDay;
        }

        public GameTime ClampToValidDate(int year, int month, int day, int hour)
        {
            // Clamp month
            if (month < 1) month = 1;
            if (month > MonthsPerYear) month = MonthsPerYear;

            // Clamp day to valid range for the month
            int maxDay = GetDaysInMonth(month);
            if (day < 1) day = 1;
            if (day > maxDay) day = maxDay;

            // Clamp hour
            if (hour < 0) hour = 0;
            if (hour >= HoursPerDay) hour = HoursPerDay - 1;

            return new GameTime
            {
                Year = year,
                Month = month,
                Day = day,
                Hour = hour
            };
        }
    }
}
