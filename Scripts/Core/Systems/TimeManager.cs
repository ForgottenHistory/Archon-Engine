using UnityEngine;
using System;
using Core.Data;

namespace Core.Systems
{
    /// <summary>
    /// Deterministic time manager for multiplayer-ready simulation
    /// Uses fixed-point math and tick-based progression to ensure identical behavior across all clients
    ///
    /// CRITICAL ARCHITECTURE REQUIREMENTS:
    /// - Fixed-point accumulator (NO float drift)
    /// - Real Earth calendar (365 days, no leap years for determinism)
    /// - Tick counter for command synchronization
    /// - Exact fraction speed multipliers
    /// - NO Time.time dependencies (non-deterministic)
    /// - ICalendar abstraction for custom calendars
    ///
    /// See: Assets/Docs/Engine/time-system-architecture.md
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        // Calendar system (default = real Earth calendar)
        private ICalendar calendar;

        // Performance: Max ticks per frame to prevent death spiral
        // If frame takes too long, defer ticks to next frame rather than piling on more work
        // This is the Paradox approach - game time slows down rather than collapsing
        private const int MAX_TICKS_PER_FRAME = 4;

        [Header("Game Configuration")]
        [SerializeField] private int startYear = 1444;
        [SerializeField] private int startMonth = 11;
        [SerializeField] private int startDay = 11;
        [SerializeField] private bool autoStart = true;

        [Header("Speed Configuration")]
        [SerializeField] private int initialSpeed = 0; // 0=paused, 1+=speed multiplier (default paused)

        // Current game time (deterministic)
        private int hour = 0;
        private int day = 1;
        private int month = 1;
        private int year = 1444;

        // Speed control (deterministic fixed-point)
        private int gameSpeed = 0; // Actual multiplier (0=paused, 1=1x, 2=2x, etc.)
        private FixedPoint64 accumulator = FixedPoint64.Zero;
        private readonly FixedPoint64 hoursPerSecond = FixedPoint64.FromInt(24);

        // Tick counter for command synchronization (CRITICAL for multiplayer)
        private ulong currentTick = 0;

        // State
        private bool isPaused = false;
        private bool isInitialized = false;

        // Event bus reference
        private EventBus eventBus;

        // ProvinceSystem reference for buffer swapping (zero-blocking UI pattern)
        private ProvinceSystem provinceSystem;

        // Update delegates (layered update frequencies)
        public event Action<int> OnHourlyTick;
        public event Action<int> OnDailyTick;
        public event Action<int> OnWeeklyTick;
        public event Action<int> OnMonthlyTick;
        public event Action<int> OnYearlyTick;
        public event Action<int> OnSpeedChanged;
        public event Action<bool> OnPauseStateChanged;

        // Properties
        public int CurrentYear => year;
        public int CurrentMonth => month;
        public int CurrentDay => day;
        public int CurrentHour => hour;
        public ulong CurrentTick => currentTick;
        public int GameSpeed => gameSpeed;
        public bool IsPaused => isPaused;
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Active calendar for date formatting and validation.
        /// Default is StandardCalendar (real Earth calendar, 365 days).
        /// GAME layer can provide custom calendars via Initialize().
        /// </summary>
        public ICalendar Calendar => calendar;

        /// <summary>
        /// Initialize the time manager with event bus and optional calendar.
        /// </summary>
        /// <param name="eventBus">Event bus for time events</param>
        /// <param name="provinceSystem">Optional province system for buffer swapping</param>
        /// <param name="customCalendar">Optional custom calendar (default: StandardCalendar)</param>
        public void Initialize(EventBus eventBus, ProvinceSystem provinceSystem = null, ICalendar customCalendar = null)
        {
            this.eventBus = eventBus;
            this.provinceSystem = provinceSystem;

            // Set up calendar (default to StandardCalendar if not provided)
            calendar = customCalendar ?? new StandardCalendar();

            // Set start date (validated against calendar)
            var startDate = calendar.ClampToValidDate(startYear, startMonth, startDay, 0);
            year = startDate.Year;
            month = startDate.Month;
            day = startDate.Day;
            hour = startDate.Hour;

            gameSpeed = initialSpeed;
            accumulator = FixedPoint64.Zero;
            currentTick = 0;
            isPaused = (gameSpeed == 0);

            isInitialized = true;

            ArchonLogger.Log($"TimeManager initialized - Starting date: {GetCurrentGameTime()}, Speed: {gameSpeed}", "core_time");

            if (autoStart && !isPaused)
            {
                StartTime();
            }
        }

        void Update()
        {
            if (!isInitialized || isPaused)
                return;

            ProcessTimeTicks(Time.deltaTime);

            // Swap double-buffers after all simulation updates complete
            // This ensures UI reads from completed tick (zero-blocking pattern)
            provinceSystem?.SwapBuffers();
        }

        /// <summary>
        /// Process time progression using deterministic fixed-point math
        /// Capped to MAX_TICKS_PER_FRAME to prevent death spiral at high speeds
        /// </summary>
        private void ProcessTimeTicks(float realDeltaTime)
        {
            // Convert real-time to game time (deterministic)
            FixedPoint64 speedMultiplier = GetSpeedMultiplier();
            FixedPoint64 gameTimeDelta = FixedPoint64.FromFloat(realDeltaTime) * speedMultiplier * hoursPerSecond;

            accumulator += gameTimeDelta;

            // Process full hours (capped to prevent death spiral)
            // Leftover accumulator carries to next frame - time deferred, not lost
            int ticksProcessed = 0;
            while (accumulator >= FixedPoint64.One && ticksProcessed < MAX_TICKS_PER_FRAME)
            {
                accumulator -= FixedPoint64.One;
                AdvanceHour();
                ticksProcessed++;
            }
        }

        /// <summary>
        /// Get deterministic speed multiplier (exact fractions)
        /// </summary>
        private FixedPoint64 GetSpeedMultiplier()
        {
            if (gameSpeed <= 0)
                return FixedPoint64.Zero;

            return FixedPoint64.FromInt(gameSpeed);
        }

        /// <summary>
        /// Advance game time by one hour
        /// </summary>
        private void AdvanceHour()
        {
            hour++;
            currentTick++;

            // Emit hourly event
            OnHourlyTick?.Invoke(hour);
            eventBus?.Emit(new HourlyTickEvent { GameTime = GetCurrentGameTime(), Tick = currentTick });

            if (hour >= calendar.HoursPerDay)
            {
                hour = 0;
                AdvanceDay();
            }
        }

        /// <summary>
        /// Advance game time by one day
        /// </summary>
        private void AdvanceDay()
        {
            day++;

            // Emit daily event
            OnDailyTick?.Invoke(day);
            eventBus?.Emit(new DailyTickEvent { GameTime = GetCurrentGameTime(), Tick = currentTick });

            // Weekly event (every 7 days)
            if (day % 7 == 0)
            {
                OnWeeklyTick?.Invoke(day / 7);
                eventBus?.Emit(new WeeklyTickEvent { GameTime = GetCurrentGameTime(), Tick = currentTick });
            }

            // Check if we need to advance to next month (uses calendar for variable month lengths)
            if (day > calendar.GetDaysInMonth(month))
            {
                day = 1;
                AdvanceMonth();
            }
        }

        /// <summary>
        /// Advance game time by one month
        /// </summary>
        private void AdvanceMonth()
        {
            month++;

            // Emit monthly event
            OnMonthlyTick?.Invoke(month);
            eventBus?.Emit(new MonthlyTickEvent { GameTime = GetCurrentGameTime(), Tick = currentTick });

            if (month > calendar.MonthsPerYear)
            {
                month = 1;
                year++;

                // Emit yearly event
                OnYearlyTick?.Invoke(year);
                eventBus?.Emit(new YearlyTickEvent { GameTime = GetCurrentGameTime(), Tick = currentTick });
            }
        }

        /// <summary>
        /// Start time progression
        /// </summary>
        public void StartTime()
        {
            if (isPaused)
            {
                isPaused = false;
                OnPauseStateChanged?.Invoke(false);
                eventBus?.Emit(new TimeStateChangedEvent { IsPaused = false, GameSpeed = gameSpeed });
                ArchonLogger.Log("Time progression started", "core_time");
            }
        }

        /// <summary>
        /// Pause time progression
        /// </summary>
        public void PauseTime()
        {
            if (!isPaused)
            {
                isPaused = true;
                OnPauseStateChanged?.Invoke(true);
                eventBus?.Emit(new TimeStateChangedEvent { IsPaused = true, GameSpeed = gameSpeed });
                ArchonLogger.Log("Time progression paused", "core_time");
            }
        }

        /// <summary>
        /// Toggle pause state
        /// </summary>
        public void TogglePause()
        {
            if (isPaused)
                StartTime();
            else
                PauseTime();
        }

        /// <summary>
        /// Set game speed (0=paused, 1+=speed multiplier)
        /// </summary>
        public void SetSpeed(int multiplier)
        {
            if (multiplier < 0)
            {
                ArchonLogger.LogWarning($"Invalid speed: {multiplier} (must be >= 0)", "core_time");
                return;
            }

            int oldSpeed = gameSpeed;
            gameSpeed = multiplier;

            // CRITICAL: Reset accumulator when speed decreases to prevent "momentum"
            // Without this, switching from 100x to 1x would continue at high speed
            // until the old accumulator drains
            if (multiplier < oldSpeed)
            {
                // Clamp accumulator to at most 1 tick worth to ensure immediate response
                if (accumulator > FixedPoint64.One)
                {
                    accumulator = FixedPoint64.FromFloat(0.5f); // Keep partial progress
                }
            }

            // Speed 0 means paused
            if (multiplier == 0)
            {
                PauseTime();
            }
            else if (oldSpeed == 0)
            {
                StartTime();
            }

            OnSpeedChanged?.Invoke(gameSpeed);
            eventBus?.Emit(new TimeStateChangedEvent { IsPaused = isPaused, GameSpeed = gameSpeed });

            ArchonLogger.Log($"Game speed changed to {gameSpeed}x", "core_time");
        }

        /// <summary>
        /// Set specific game date (for loading saves, scenarios)
        /// </summary>
        public void SetGameTime(int newYear, int newMonth, int newDay, int newHour = 0)
        {
            // Use calendar to validate and clamp the date
            var validDate = calendar.ClampToValidDate(newYear, newMonth, newDay, newHour);
            year = validDate.Year;
            month = validDate.Month;
            day = validDate.Day;
            hour = validDate.Hour;

            accumulator = FixedPoint64.Zero;

            eventBus?.Emit(new TimeChangedEvent { GameTime = GetCurrentGameTime() });
            ArchonLogger.Log($"Game time set to: {GetCurrentGameTime()}", "core_time");
        }

        /// <summary>
        /// Set specific tick (for multiplayer synchronization)
        /// </summary>
        public void SetCurrentTick(ulong newTick)
        {
            currentTick = newTick;
        }

        /// <summary>
        /// Synchronize to specific tick (for multiplayer)
        /// Advances time until we reach the target tick
        /// </summary>
        public void SynchronizeToTick(ulong targetTick)
        {
            if (targetTick < currentTick)
            {
                ArchonLogger.LogWarning($"Cannot synchronize backwards: current={currentTick}, target={targetTick}", "core_time");
                return;
            }

            while (currentTick < targetTick)
            {
                AdvanceHour();
            }

            ArchonLogger.Log($"Synchronized to tick {currentTick}", "core_time");
        }

        /// <summary>
        /// Get current game time as struct
        /// </summary>
        public GameTime GetCurrentGameTime()
        {
            return new GameTime
            {
                Year = year,
                Month = month,
                Day = day,
                Hour = hour
            };
        }

        /// <summary>
        /// Get formatted date string using active calendar.
        /// Example: "11 November 1444 AD"
        /// </summary>
        public string GetFormattedDate()
        {
            return calendar.FormatDate(GetCurrentGameTime());
        }

        /// <summary>
        /// Get compact formatted date string using active calendar.
        /// Example: "11 Nov 1444"
        /// </summary>
        public string GetFormattedDateCompact()
        {
            return calendar.FormatDateCompact(GetCurrentGameTime());
        }

        /// <summary>
        /// Get the name of the current month from the calendar.
        /// </summary>
        public string GetCurrentMonthName()
        {
            return calendar.GetMonthName(month);
        }

        /// <summary>
        /// Calculate total hours between start date and current game time.
        /// Uses proper month lengths via GameTime.ToTotalHours().
        /// </summary>
        public long CalculateTotalTicks(int fromYear, int fromMonth, int fromDay)
        {
            var fromTime = GameTime.Create(fromYear, fromMonth, fromDay, 0);
            var currentTime = GetCurrentGameTime();
            return currentTime.ToTotalHours() - fromTime.ToTotalHours();
        }

        // ====================================================================
        // SAVE/LOAD SUPPORT
        // ====================================================================

        /// <summary>
        /// Get current accumulator value for save/load
        /// </summary>
        public FixedPoint64 GetAccumulator()
        {
            return accumulator;
        }

        /// <summary>
        /// Load complete TimeManager state from save file
        /// Restores all internal state without triggering events
        /// </summary>
        public void LoadState(ulong tick, int newYear, int newMonth, int newDay, int newHour, int speedLevel, bool paused, FixedPoint64 newAccumulator)
        {
            currentTick = tick;
            year = newYear;
            month = newMonth;
            day = newDay;
            hour = newHour;
            gameSpeed = speedLevel;
            isPaused = paused;
            accumulator = newAccumulator;

            ArchonLogger.Log($"TimeManager state loaded: {GetCurrentGameTime()}, Tick: {currentTick}, Speed: {gameSpeed}, Paused: {isPaused}", "core_time");
        }

        // Old OnGUI debug UI removed - replaced by Game.DebugTools.TimeDebugPanel (UI Toolkit)
        // See: Assets/Game/Debug/TimeDebugPanel.cs
    }

    /// <summary>
    /// Represents a point in game time (deterministic calendar).
    /// Supports real Earth calendar (365 days, variable month lengths).
    /// All operations are deterministic for multiplayer compatibility.
    /// </summary>
    [System.Serializable]
    public struct GameTime : System.IComparable<GameTime>, System.IEquatable<GameTime>
    {
        public int Year;
        public int Month;  // 1-12
        public int Day;    // 1-31 (depends on month)
        public int Hour;   // 0-23

        // === Factory Methods ===

        /// <summary>
        /// Create a GameTime with specified components.
        /// </summary>
        public static GameTime Create(int year, int month, int day, int hour = 0)
        {
            return new GameTime { Year = year, Month = month, Day = day, Hour = hour };
        }

        /// <summary>
        /// Create GameTime from total hours since year 0.
        /// Inverse of ToTotalHours().
        /// </summary>
        public static GameTime FromTotalHours(long totalHours)
        {
            int hoursPerDay = CalendarConstants.HOURS_PER_DAY;
            int hoursPerYear = CalendarConstants.HOURS_PER_YEAR;

            // Handle negative years
            int year = (int)(totalHours / hoursPerYear);
            long remainingHours = totalHours % hoursPerYear;

            if (remainingHours < 0)
            {
                year--;
                remainingHours += hoursPerYear;
            }

            // Convert remaining hours to day of year
            int dayOfYear = (int)(remainingHours / hoursPerDay);
            int hour = (int)(remainingHours % hoursPerDay);

            // Convert day of year to month and day
            int month = 1;
            int day = dayOfYear + 1; // Days are 1-indexed

            for (int m = 1; m <= 12; m++)
            {
                int daysInMonth = CalendarConstants.DAYS_IN_MONTH[m];
                if (day <= daysInMonth)
                {
                    month = m;
                    break;
                }
                day -= daysInMonth;
            }

            return new GameTime { Year = year, Month = month, Day = day, Hour = hour };
        }

        // === Conversion ===

        /// <summary>
        /// Convert to total hours since year 0.
        /// Uses real month lengths (365-day year).
        /// </summary>
        public long ToTotalHours()
        {
            int hoursPerDay = CalendarConstants.HOURS_PER_DAY;
            int hoursPerYear = CalendarConstants.HOURS_PER_YEAR;

            long totalHours = (long)Year * hoursPerYear;

            // Add hours for complete months
            if (Month >= 1 && Month <= 12)
            {
                totalHours += CalendarConstants.DAYS_BEFORE_MONTH[Month] * hoursPerDay;
            }

            // Add hours for days (days are 1-indexed)
            totalHours += (Day - 1) * hoursPerDay;

            // Add hours
            totalHours += Hour;

            return totalHours;
        }

        // === Comparison Operators ===

        public static bool operator ==(GameTime a, GameTime b)
        {
            return a.Year == b.Year && a.Month == b.Month && a.Day == b.Day && a.Hour == b.Hour;
        }

        public static bool operator !=(GameTime a, GameTime b)
        {
            return !(a == b);
        }

        public static bool operator <(GameTime a, GameTime b)
        {
            return a.CompareTo(b) < 0;
        }

        public static bool operator >(GameTime a, GameTime b)
        {
            return a.CompareTo(b) > 0;
        }

        public static bool operator <=(GameTime a, GameTime b)
        {
            return a.CompareTo(b) <= 0;
        }

        public static bool operator >=(GameTime a, GameTime b)
        {
            return a.CompareTo(b) >= 0;
        }

        public int CompareTo(GameTime other)
        {
            // Compare year first (most significant)
            if (Year != other.Year) return Year.CompareTo(other.Year);
            if (Month != other.Month) return Month.CompareTo(other.Month);
            if (Day != other.Day) return Day.CompareTo(other.Day);
            return Hour.CompareTo(other.Hour);
        }

        // === Arithmetic Operations ===

        /// <summary>
        /// Add hours (negative to subtract). Returns normalized GameTime.
        /// </summary>
        public GameTime AddHours(int hours)
        {
            return FromTotalHours(ToTotalHours() + hours);
        }

        /// <summary>
        /// Add days (negative to subtract). Returns normalized GameTime.
        /// </summary>
        public GameTime AddDays(int days)
        {
            return FromTotalHours(ToTotalHours() + days * CalendarConstants.HOURS_PER_DAY);
        }

        /// <summary>
        /// Add months (negative to subtract). Day is clamped if necessary.
        /// </summary>
        public GameTime AddMonths(int months)
        {
            int newMonth = Month + months;
            int newYear = Year;

            // Handle month overflow/underflow
            while (newMonth > 12)
            {
                newMonth -= 12;
                newYear++;
            }
            while (newMonth < 1)
            {
                newMonth += 12;
                newYear--;
            }

            // Clamp day to valid range for new month
            int maxDay = CalendarConstants.DAYS_IN_MONTH[newMonth];
            int newDay = Day > maxDay ? maxDay : Day;

            return new GameTime { Year = newYear, Month = newMonth, Day = newDay, Hour = Hour };
        }

        /// <summary>
        /// Add years (negative to subtract).
        /// </summary>
        public GameTime AddYears(int years)
        {
            return new GameTime { Year = Year + years, Month = Month, Day = Day, Hour = Hour };
        }

        // === Duration Calculations ===

        /// <summary>
        /// Calculate hours between this and another GameTime (signed).
        /// Positive if other is later, negative if other is earlier.
        /// </summary>
        public long HoursBetween(GameTime other)
        {
            return other.ToTotalHours() - ToTotalHours();
        }

        /// <summary>
        /// Calculate days between this and another GameTime (signed, rounded down).
        /// </summary>
        public int DaysBetween(GameTime other)
        {
            return (int)(HoursBetween(other) / CalendarConstants.HOURS_PER_DAY);
        }

        // === Equality ===

        public override bool Equals(object obj)
        {
            return obj is GameTime time && this == time;
        }

        public bool Equals(GameTime other)
        {
            return this == other;
        }

        public override int GetHashCode()
        {
            return Year * 1000000 + Month * 10000 + Day * 100 + Hour;
        }

        // === Formatting ===

        public override string ToString()
        {
            return $"{Year}.{Month:D2}.{Day:D2} {Hour:D2}:00";
        }

        /// <summary>
        /// Format using specified calendar.
        /// </summary>
        public string ToString(ICalendar calendar)
        {
            return calendar.FormatDate(this);
        }

        /// <summary>
        /// Format compact using specified calendar.
        /// </summary>
        public string ToCompactString(ICalendar calendar)
        {
            return calendar.FormatDateCompact(this);
        }
    }
}
