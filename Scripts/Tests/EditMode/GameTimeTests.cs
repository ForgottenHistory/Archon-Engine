using NUnit.Framework;
using Core.Data;
using Core.Systems;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests for GameTime and StandardCalendar - the 365-day, no-leap-year calendar.
    ///
    /// WHY THIS MATTERS: GameTime converts to and from a single total-hours integer, and
    /// that integer is what gets compared, stored and synced. If the conversion is not a
    /// perfect round-trip, dates drift - and a drift of one day in a treaty expiry or
    /// truce timer is a desync, not a cosmetic bug.
    /// </summary>
    public class GameTimeTests
    {
        // ===== Round-trip =====

        /// <summary>
        /// The core invariant: FromTotalHours(t.ToTotalHours()) == t, for every hour of a
        /// full year. Covers all twelve month lengths and both boundaries.
        /// </summary>
        [Test]
        public void ToTotalHours_RoundTrips_ForEveryDayOfYear()
        {
            for (int month = 1; month <= 12; month++)
            {
                int daysInMonth = CalendarConstants.DAYS_IN_MONTH[month];

                for (int day = 1; day <= daysInMonth; day++)
                {
                    for (int hour = 0; hour < 24; hour += 7)
                    {
                        var original = GameTime.Create(1444, month, day, hour);

                        var roundTripped = GameTime.FromTotalHours(original.ToTotalHours());

                        Assert.AreEqual(original, roundTripped,
                            $"Round-trip failed for {original}");
                    }
                }
            }
        }

        /// <summary>
        /// FromTotalHours has explicit negative-remainder handling while ToTotalHours has
        /// none. That asymmetry is correct - Year * HOURS_PER_YEAR is already signed and
        /// the month/day offsets are always positive - but it is exactly the kind of thing
        /// that looks like a bug and gets "fixed" into one.
        /// </summary>
        [Test]
        public void ToTotalHours_RoundTrips_ForNegativeYears()
        {
            foreach (var year in new[] { 0, -1, -2, -100, -1000 })
            {
                for (int month = 1; month <= 12; month++)
                {
                    for (int day = 1; day <= CalendarConstants.DAYS_IN_MONTH[month]; day += 7)
                    {
                        var original = GameTime.Create(year, month, day);

                        var roundTripped = GameTime.FromTotalHours(original.ToTotalHours());

                        Assert.AreEqual(original, roundTripped,
                            $"Round-trip failed for negative-year date {original}");
                    }
                }
            }
        }

        [Test]
        public void ToTotalHours_AtYearZeroStart_IsZero()
        {
            Assert.AreEqual(0L, GameTime.Create(0, 1, 1, 0).ToTotalHours(),
                "Year 0 January 1st is the origin of the hour axis");
        }

        [Test]
        public void ToTotalHours_OneHourBeforeOrigin_IsNegativeOne()
        {
            Assert.AreEqual(-1L, GameTime.Create(-1, 12, 31, 23).ToTotalHours(),
                "The hour before the origin must be -1, not a wrapped positive value");
        }

        [Test]
        public void ToTotalHours_IsMonotonic()
        {
            var previous = long.MinValue;

            for (int year = -3; year <= 3; year++)
            {
                for (int month = 1; month <= 12; month++)
                {
                    for (int day = 1; day <= CalendarConstants.DAYS_IN_MONTH[month]; day += 5)
                    {
                        long current = GameTime.Create(year, month, day).ToTotalHours();

                        Assert.Greater(current, previous,
                            $"Total hours must increase with date; {year}-{month}-{day} broke ordering");

                        previous = current;
                    }
                }
            }
        }

        [Test]
        public void HoursInOneYear_MatchesConstant()
        {
            var start = GameTime.Create(1444, 1, 1);
            var end = GameTime.Create(1445, 1, 1);

            Assert.AreEqual(CalendarConstants.HOURS_PER_YEAR, start.HoursBetween(end),
                "A full year must span exactly HOURS_PER_YEAR");
        }

        // ===== Calendar constants =====

        /// <summary>
        /// DAYS_BEFORE_MONTH is a pre-computed prefix sum of DAYS_IN_MONTH. If the two
        /// ever disagree, every date conversion silently shifts.
        /// </summary>
        [Test]
        public void DaysBeforeMonth_IsPrefixSumOfDaysInMonth()
        {
            int cumulative = 0;

            for (int month = 1; month <= 12; month++)
            {
                Assert.AreEqual(cumulative, CalendarConstants.DAYS_BEFORE_MONTH[month],
                    $"DAYS_BEFORE_MONTH[{month}] disagrees with the running total of DAYS_IN_MONTH");

                cumulative += CalendarConstants.DAYS_IN_MONTH[month];
            }

            Assert.AreEqual(CalendarConstants.DAYS_PER_YEAR, cumulative,
                "Month lengths must sum to DAYS_PER_YEAR");
        }

        [Test]
        public void February_HasNoLeapDay()
        {
            Assert.AreEqual(28, CalendarConstants.DAYS_IN_MONTH[2],
                "Leap years are deliberately absent - they would make date arithmetic " +
                "year-dependent and threaten determinism");
        }

        [Test]
        public void DerivedConstants_MatchTheirFactors()
        {
            Assert.AreEqual(24 * 7, CalendarConstants.HOURS_PER_WEEK);
            Assert.AreEqual(24 * 365, CalendarConstants.HOURS_PER_YEAR);
            Assert.AreEqual(30 * 12, CalendarConstants.SIMPLIFIED_DAYS_PER_YEAR);
            Assert.AreEqual(24 * 360, CalendarConstants.SIMPLIFIED_HOURS_PER_YEAR);
        }

        // ===== Arithmetic =====

        [Test]
        public void AddHours_RollsOverYearBoundary()
        {
            var newYear = GameTime.Create(1444, 12, 31, 23).AddHours(1);

            Assert.AreEqual(GameTime.Create(1445, 1, 1, 0), newYear);
        }

        [Test]
        public void AddHours_RollsBackwardsOverYearBoundary()
        {
            var previous = GameTime.Create(1444, 1, 1, 0).AddHours(-1);

            Assert.AreEqual(GameTime.Create(1443, 12, 31, 23), previous);
        }

        [Test]
        public void AddDays_CrossesFebruaryWithoutLeapDay()
        {
            var march = GameTime.Create(1444, 2, 28).AddDays(1);

            Assert.AreEqual(GameTime.Create(1444, 3, 1), march,
                "February 28th is always followed by March 1st");
        }

        [Test]
        public void AddDays_AndSubtractDays_AreInverse()
        {
            var original = GameTime.Create(1444, 6, 15, 12);

            foreach (var days in new[] { 1, 30, 365, 1000 })
            {
                Assert.AreEqual(original, original.AddDays(days).AddDays(-days),
                    $"Adding then subtracting {days} days must return the original date");
            }
        }

        [Test]
        public void AddMonths_ClampsDayToShorterMonth()
        {
            // January 31st + 1 month has no February 31st to land on.
            var february = GameTime.Create(1444, 1, 31).AddMonths(1);

            Assert.AreEqual(2, february.Month);
            Assert.AreEqual(28, february.Day, "Day must clamp to the length of the target month");
        }

        [Test]
        public void AddMonths_RollsOverYearBoundary()
        {
            Assert.AreEqual(GameTime.Create(1445, 1, 15), GameTime.Create(1444, 12, 15).AddMonths(1));
            Assert.AreEqual(GameTime.Create(1443, 12, 15), GameTime.Create(1444, 1, 15).AddMonths(-1));
        }

        [Test]
        public void AddMonths_HandlesMultiYearSpans()
        {
            Assert.AreEqual(GameTime.Create(1446, 6, 10), GameTime.Create(1444, 6, 10).AddMonths(24));
            Assert.AreEqual(GameTime.Create(1442, 6, 10), GameTime.Create(1444, 6, 10).AddMonths(-24));
        }

        [Test]
        public void AddYears_PreservesMonthDayAndHour()
        {
            var later = GameTime.Create(1444, 7, 4, 9).AddYears(10);

            Assert.AreEqual(GameTime.Create(1454, 7, 4, 9), later);
        }

        // ===== Duration =====

        [Test]
        public void HoursBetween_IsSigned()
        {
            var early = GameTime.Create(1444, 1, 1);
            var late = GameTime.Create(1444, 1, 2);

            Assert.AreEqual(24L, early.HoursBetween(late), "Forward span is positive");
            Assert.AreEqual(-24L, late.HoursBetween(early), "Backward span is negative");
        }

        [Test]
        public void HoursBetween_IdenticalTimes_IsZero()
        {
            var time = GameTime.Create(1444, 5, 5, 5);

            Assert.AreEqual(0L, time.HoursBetween(time));
        }

        // ===== Comparison =====

        [Test]
        public void Comparison_OrdersByYearThenMonthThenDayThenHour()
        {
            // GameTime implements only the generic IComparable<GameTime>, so NUnit's
            // Assert.Less and Is.LessThan do not bind to it. Exercise the operators
            // directly instead.
            Assert.IsTrue(GameTime.Create(1444, 1, 1) < GameTime.Create(1445, 1, 1),
                "year dominates");
            Assert.IsTrue(GameTime.Create(1444, 1, 1) < GameTime.Create(1444, 2, 1),
                "then month");
            Assert.IsTrue(GameTime.Create(1444, 1, 1) < GameTime.Create(1444, 1, 2),
                "then day");
            Assert.IsTrue(GameTime.Create(1444, 1, 1, 0) < GameTime.Create(1444, 1, 1, 1),
                "then hour");
        }

        [Test]
        public void Comparison_AgreesWithTotalHours()
        {
            var times = new[]
            {
                GameTime.Create(-1, 12, 31, 23),
                GameTime.Create(0, 1, 1, 0),
                GameTime.Create(1444, 1, 1, 0),
                GameTime.Create(1444, 6, 15, 12),
                GameTime.Create(1445, 1, 1, 0),
            };

            for (int i = 0; i < times.Length; i++)
            {
                for (int j = 0; j < times.Length; j++)
                {
                    bool byComparison = times[i] < times[j];
                    bool byHours = times[i].ToTotalHours() < times[j].ToTotalHours();

                    Assert.AreEqual(byHours, byComparison,
                        $"Operator ordering disagrees with hour ordering for {times[i]} vs {times[j]}");
                }
            }
        }

        [Test]
        public void Equality_MatchesComponentwiseComparison()
        {
            var a = GameTime.Create(1444, 11, 11, 6);
            var b = GameTime.Create(1444, 11, 11, 6);
            var c = GameTime.Create(1444, 11, 11, 7);

            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.IsTrue(a != c);
            Assert.IsTrue(a <= b);
            Assert.IsTrue(a >= b);
        }

        // ===== StandardCalendar =====

        [Test]
        public void Calendar_ReturnsCorrectMonthLengths()
        {
            var calendar = new StandardCalendar();

            Assert.AreEqual(31, calendar.GetDaysInMonth(1), "January");
            Assert.AreEqual(28, calendar.GetDaysInMonth(2), "February");
            Assert.AreEqual(30, calendar.GetDaysInMonth(4), "April");
            Assert.AreEqual(31, calendar.GetDaysInMonth(12), "December");
        }

        [Test]
        public void Calendar_WithInvalidMonth_FallsBackWithoutThrowing()
        {
            var calendar = new StandardCalendar();

            Assert.AreEqual(30, calendar.GetDaysInMonth(0), "Invalid month must not index out of range");
            Assert.AreEqual(30, calendar.GetDaysInMonth(13));
            Assert.DoesNotThrow(() => calendar.GetMonthName(0));
            Assert.DoesNotThrow(() => calendar.GetMonthAbbreviation(99));
        }

        [Test]
        public void Calendar_ValidatesDatesAgainstMonthLength()
        {
            var calendar = new StandardCalendar();

            Assert.IsTrue(calendar.IsValidDate(1444, 1, 31), "January has 31 days");
            Assert.IsFalse(calendar.IsValidDate(1444, 2, 29), "February never has 29 days");
            Assert.IsFalse(calendar.IsValidDate(1444, 4, 31), "April has 30 days");
            Assert.IsFalse(calendar.IsValidDate(1444, 13, 1), "Month 13 does not exist");
            Assert.IsFalse(calendar.IsValidDate(1444, 1, 0), "Day 0 does not exist");
        }

        [Test]
        public void Calendar_ClampsOutOfRangeComponents()
        {
            var calendar = new StandardCalendar();

            var clamped = calendar.ClampToValidDate(1444, 2, 31, 25);

            Assert.AreEqual(2, clamped.Month);
            Assert.AreEqual(28, clamped.Day, "Day clamps to February's length");
            Assert.AreEqual(23, clamped.Hour, "Hour clamps to 23");
        }

        [Test]
        public void Calendar_ClampsMonthBelowAndAboveRange()
        {
            var calendar = new StandardCalendar();

            Assert.AreEqual(1, calendar.ClampToValidDate(1444, 0, 15, 0).Month);
            Assert.AreEqual(12, calendar.ClampToValidDate(1444, 99, 15, 0).Month);
        }

        /// <summary>
        /// Year 0 is a real year on the arithmetic axis but displays as "1 BC", following
        /// the astronomical convention. The two models are offset by one on purpose -
        /// there is no year zero in the BC/AD display scheme.
        /// </summary>
        [Test]
        public void Calendar_FormatsYearsAcrossTheEraBoundary()
        {
            var calendar = new StandardCalendar();

            Assert.AreEqual("1444 AD", calendar.FormatYear(1444));
            Assert.AreEqual("1 AD", calendar.FormatYear(1));
            Assert.AreEqual("1 BC", calendar.FormatYear(0), "Year 0 displays as 1 BC");
            Assert.AreEqual("2 BC", calendar.FormatYear(-1));
            Assert.AreEqual("3 BC", calendar.FormatYear(-2));
        }

        [Test]
        public void Calendar_FormatsFullDate()
        {
            var calendar = new StandardCalendar();

            Assert.AreEqual("11 November 1444 AD",
                calendar.FormatDate(GameTime.Create(1444, 11, 11)));
        }

        [Test]
        public void Calendar_FormatsCompactDate()
        {
            var calendar = new StandardCalendar();

            Assert.AreEqual("11 Nov 1444",
                calendar.FormatDateCompact(GameTime.Create(1444, 11, 11)));
            Assert.AreEqual("1 Jan 1 BC",
                calendar.FormatDateCompact(GameTime.Create(0, 1, 1)),
                "Compact form keeps the BC suffix even though it drops AD");
        }

        [Test]
        public void Calendar_ExposesConstantsConsistently()
        {
            var calendar = new StandardCalendar();

            Assert.AreEqual(CalendarConstants.HOURS_PER_DAY, calendar.HoursPerDay);
            Assert.AreEqual(CalendarConstants.MONTHS_PER_YEAR, calendar.MonthsPerYear);
            Assert.AreEqual(CalendarConstants.DAYS_PER_YEAR, calendar.DaysPerYear);
        }
    }
}
