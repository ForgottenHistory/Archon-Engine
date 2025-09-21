using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace ParadoxParser.Utilities
{
    /// <summary>
    /// High-performance date parsing for Paradox formats
    /// Handles YYYY.MM.DD format common in Paradox files
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class FastDateParser
    {
        /// <summary>
        /// Paradox date structure (packed for performance)
        /// </summary>
        public struct ParadoxDate : IEquatable<ParadoxDate>, IComparable<ParadoxDate>
        {
            public readonly short Year;
            public readonly byte Month;
            public readonly byte Day;

            public ParadoxDate(short year, byte month, byte day)
            {
                Year = year;
                Month = month;
                Day = day;
            }

            /// <summary>
            /// Convert to packed integer for fast comparisons
            /// </summary>
            public int ToPackedInt()
            {
                return (Year << 16) | (Month << 8) | Day;
            }

            /// <summary>
            /// Create from packed integer
            /// </summary>
            public static ParadoxDate FromPackedInt(int packed)
            {
                return new ParadoxDate(
                    (short)(packed >> 16),
                    (byte)((packed >> 8) & 0xFF),
                    (byte)(packed & 0xFF)
                );
            }

            /// <summary>
            /// Check if date is valid
            /// </summary>
            public bool IsValid()
            {
                if (Year < 1 || Year > 9999) return false;
                if (Month < 1 || Month > 12) return false;
                if (Day < 1 || Day > GetDaysInMonth(Year, Month)) return false;
                return true;
            }

            /// <summary>
            /// Get days in month (handles leap years)
            /// </summary>
            private static byte GetDaysInMonth(int year, int month)
            {
                return month switch
                {
                    1 or 3 or 5 or 7 or 8 or 10 or 12 => 31,
                    4 or 6 or 9 or 11 => 30,
                    2 => IsLeapYear(year) ? (byte)29 : (byte)28,
                    _ => 0
                };
            }

            /// <summary>
            /// Check if year is leap year
            /// </summary>
            private static bool IsLeapYear(int year)
            {
                return (year % 4 == 0 && year % 100 != 0) || (year % 400 == 0);
            }

            public bool Equals(ParadoxDate other)
            {
                return Year == other.Year && Month == other.Month && Day == other.Day;
            }

            public int CompareTo(ParadoxDate other)
            {
                return ToPackedInt().CompareTo(other.ToPackedInt());
            }

            public override string ToString()
            {
                return $"{Year}.{Month:D2}.{Day:D2}";
            }
        }

        /// <summary>
        /// Parse result for date operations
        /// </summary>
        public struct DateParseResult
        {
            public ParadoxDate Date;
            public bool Success;
            public int BytesConsumed;

            public static DateParseResult Failed => new DateParseResult { Success = false, BytesConsumed = 0 };

            public static DateParseResult Successful(ParadoxDate date, int bytesConsumed)
            {
                return new DateParseResult { Date = date, Success = true, BytesConsumed = bytesConsumed };
            }
        }

        /// <summary>
        /// Parse date in YYYY.MM.DD format
        /// Optimized for common Paradox date patterns
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe DateParseResult ParseDate(NativeSlice<byte> data)
        {
            // Minimum length for YYYY.MM.DD is 10 characters
            if (data.Length < 10)
                return DateParseResult.Failed;

            // Fast validation: check that dots are in the right positions
            if (data[4] != (byte)'.' || data[7] != (byte)'.')
                return DateParseResult.Failed;

            int dataLength = data.Length;

            // Parse year (YYYY)
            var yearResult = ParseFixedDigits(data, 0, 4);
            if (!yearResult.Success)
                return DateParseResult.Failed;

            // Parse month (MM)
            var monthResult = ParseFixedDigits(data, 5, 2);
            if (!monthResult.Success || monthResult.Value < 1 || monthResult.Value > 12)
                return DateParseResult.Failed;

            // Parse day (DD)
            var dayResult = ParseFixedDigits(data, 8, 2);
            if (!dayResult.Success || dayResult.Value < 1 || dayResult.Value > 31)
                return DateParseResult.Failed;

            var date = new ParadoxDate((short)yearResult.Value, (byte)monthResult.Value, (byte)dayResult.Value);

            // Validate the date is actually valid (proper days in month, etc.)
            if (!date.IsValid())
                return DateParseResult.Failed;

            return DateParseResult.Successful(date, 10);
        }

        /// <summary>
        /// Parse date with optional time (YYYY.MM.DD or YYYY.MM.DD.HH.MM.SS)
        /// Returns just the date part
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateParseResult ParseDateOptionalTime(NativeSlice<byte> data)
        {
            var dateResult = ParseDate(data);
            if (!dateResult.Success)
                return dateResult;

            int consumed = 10;

            // Check if there's time information following (optional)
            if (data.Length > 10 && data[10] == (byte)'.')
            {
                // Try to parse time part: .HH.MM.SS
                if (data.Length >= 19 &&
                    data[13] == (byte)'.' &&
                    data[16] == (byte)'.')
                {
                    // Validate time components exist and are digits
                    bool validTime = true;
                    for (int i = 11; i < 19; i++)
                    {
                        if (i == 13 || i == 16) continue; // Skip dots
                        if (!IsDigit(data[i]))
                        {
                            validTime = false;
                            break;
                        }
                    }

                    if (validTime)
                    {
                        consumed = 19; // Include time in consumed bytes
                    }
                }
            }

            return DateParseResult.Successful(dateResult.Date, consumed);
        }

        /// <summary>
        /// Parse date range (start_date.end_date format)
        /// Returns the start date
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateParseResult ParseDateRange(NativeSlice<byte> data, out ParadoxDate endDate)
        {
            endDate = default;

            var startResult = ParseDate(data);
            if (!startResult.Success)
                return startResult;

            int offset = startResult.BytesConsumed;

            // Check for range separator (could be space, dash, or other)
            while (offset < data.Length && IsWhitespace(data[offset]))
                offset++;

            if (offset >= data.Length)
                return startResult; // Just a single date

            // Try to parse end date
            var remainingData = data.Slice(offset);
            var endResult = ParseDate(remainingData);
            if (endResult.Success)
            {
                endDate = endResult.Date;
                return DateParseResult.Successful(startResult.Date, offset + endResult.BytesConsumed);
            }

            return startResult; // Just return start date if end parsing fails
        }

        /// <summary>
        /// Quick check if data looks like a date (YYYY.MM.DD pattern)
        /// Fast validation without full parsing
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool LooksLikeDate(NativeSlice<byte> data)
        {
            if (data.Length < 10)
                return false;

            // Check pattern: DDDD.DD.DD
            return IsDigit(data[0]) && IsDigit(data[1]) && IsDigit(data[2]) && IsDigit(data[3]) &&
                   data[4] == (byte)'.' &&
                   IsDigit(data[5]) && IsDigit(data[6]) &&
                   data[7] == (byte)'.' &&
                   IsDigit(data[8]) && IsDigit(data[9]);
        }

        /// <summary>
        /// Parse relative date offset (+days or -days from a base date)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateParseResult ParseRelativeDate(NativeSlice<byte> data, ParadoxDate baseDate)
        {
            if (data.Length == 0)
                return DateParseResult.Failed;

            int sign = 1;
            int offset = 0;

            // Check for +/- prefix
            if (data[0] == (byte)'+')
            {
                offset = 1;
            }
            else if (data[0] == (byte)'-')
            {
                sign = -1;
                offset = 1;
            }

            // Parse the number of days
            var daysSlice = data.Slice(offset);
            var daysResult = FastNumberParser.ParseInt32(daysSlice);
            if (!daysResult.Success)
                return DateParseResult.Failed;

            int totalDays = sign * daysResult.Value;

            // Add days to base date (simplified - doesn't handle month/year overflow properly)
            // In a real implementation, you'd want proper date arithmetic
            int newDay = baseDate.Day + totalDays;

            // Simple bounds checking (real implementation would handle month/year changes)
            if (newDay < 1 || newDay > 31)
                return DateParseResult.Failed;

            var resultDate = new ParadoxDate(baseDate.Year, baseDate.Month, (byte)newDay);
            return DateParseResult.Successful(resultDate, offset + daysResult.BytesConsumed);
        }

        /// <summary>
        /// Parse fixed number of digits at specified offset
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static FastNumberParser.ParseResult<int> ParseFixedDigits(NativeSlice<byte> data, int offset, int digitCount)
        {
            if (offset + digitCount > data.Length)
                return FastNumberParser.ParseResult<int>.Failed;

            int result = 0;
            for (int i = 0; i < digitCount; i++)
            {
                byte b = data[offset + i];
                if (!IsDigit(b))
                    return FastNumberParser.ParseResult<int>.Failed;

                result = result * 10 + (b - (byte)'0');
            }

            return FastNumberParser.ParseResult<int>.Successful(result, digitCount);
        }

        /// <summary>
        /// Check if byte is a decimal digit
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDigit(byte b)
        {
            return b >= (byte)'0' && b <= (byte)'9';
        }

        /// <summary>
        /// Check if byte is whitespace
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWhitespace(byte b)
        {
            return b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';
        }

        /// <summary>
        /// Convert ParadoxDate to days since epoch (for sorting/comparison)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToDaysSinceEpoch(ParadoxDate date)
        {
            // Simple approximation: (year - 1) * 365 + month * 30 + day
            // Real implementation would handle leap years and actual month lengths
            return (date.Year - 1) * 365 + (date.Month - 1) * 30 + date.Day;
        }

        /// <summary>
        /// Common Paradox epoch dates for reference
        /// </summary>
        public static class CommonDates
        {
            public static readonly ParadoxDate EU4Start = new ParadoxDate(1444, 11, 11);
            public static readonly ParadoxDate HOI4Start = new ParadoxDate(1936, 1, 1);
            public static readonly ParadoxDate CK3Start = new ParadoxDate(867, 1, 1);
            public static readonly ParadoxDate VIC3Start = new ParadoxDate(1836, 1, 1);
        }
    }
}