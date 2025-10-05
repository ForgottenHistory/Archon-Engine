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
    /// - 360-day year (30-day months, no leap years)
    /// - Tick counter for command synchronization
    /// - Exact fraction speed multipliers
    /// - NO Time.time dependencies (non-deterministic)
    ///
    /// See: Assets/Docs/Engine/time-system-architecture.md
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        // Time constants (deterministic 360-day year)
        private const int HOURS_PER_DAY = 24;
        private const int DAYS_PER_MONTH = 30;  // Simplified for determinism
        private const int MONTHS_PER_YEAR = 12;
        private const int DAYS_PER_YEAR = 360;  // 30 × 12 = 360 (NOT 365!)

        [Header("Game Configuration")]
        [SerializeField] private int startYear = 1444;
        [SerializeField] private int startMonth = 11;
        [SerializeField] private int startDay = 11;
        [SerializeField] private bool autoStart = true;

        [Header("Speed Configuration")]
        [SerializeField] private int initialSpeedLevel = 0; // 0=paused, 1-4=speed levels (default paused for manual testing)

        // Current game time (deterministic)
        private int hour = 0;
        private int day = 1;
        private int month = 1;
        private int year = 1444;

        // Speed control (deterministic fixed-point)
        private int gameSpeedLevel = 2;
        private FixedPoint64 accumulator = FixedPoint64.Zero;
        private readonly FixedPoint64 hoursPerSecond = FixedPoint64.FromInt(24); // At speed level 2 (normal)

        // Tick counter for command synchronization (CRITICAL for multiplayer)
        private ulong currentTick = 0;

        // State
        private bool isPaused = false;
        private bool isInitialized = false;

        // Event bus reference
        private EventBus eventBus;

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
        public int GameSpeed => gameSpeedLevel;
        public bool IsPaused => isPaused;
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Initialize the time manager with event bus
        /// </summary>
        public void Initialize(EventBus eventBus)
        {
            this.eventBus = eventBus;

            // Set start date
            year = startYear;
            month = startMonth;
            day = startDay;
            hour = 0;

            gameSpeedLevel = initialSpeedLevel;
            accumulator = FixedPoint64.Zero;
            currentTick = 0;
            isPaused = (gameSpeedLevel == 0);

            isInitialized = true;

            ArchonLogger.Log($"TimeManager initialized - Starting date: {GetCurrentGameTime()}, Speed: {gameSpeedLevel}");

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
        }

        /// <summary>
        /// Process time progression using deterministic fixed-point math
        /// </summary>
        private void ProcessTimeTicks(float realDeltaTime)
        {
            // Convert real-time to game time (deterministic)
            FixedPoint64 speedMultiplier = GetSpeedMultiplier(gameSpeedLevel);
            FixedPoint64 gameTimeDelta = FixedPoint64.FromFloat(realDeltaTime) * speedMultiplier * hoursPerSecond;

            accumulator += gameTimeDelta;

            // Process full hours
            while (accumulator >= FixedPoint64.One)
            {
                accumulator -= FixedPoint64.One;
                AdvanceHour();
            }
        }

        /// <summary>
        /// Get deterministic speed multiplier (exact fractions)
        /// </summary>
        private FixedPoint64 GetSpeedMultiplier(int speedLevel)
        {
            // Exact fractions for determinism
            return speedLevel switch
            {
                0 => FixedPoint64.Zero,                             // Paused
                1 => FixedPoint64.FromFraction(1, 2),              // 0.5x
                2 => FixedPoint64.One,                              // 1.0x (normal)
                3 => FixedPoint64.FromInt(2),                      // 2.0x
                4 => FixedPoint64.FromInt(5),                      // 5.0x (very fast)
                _ => FixedPoint64.One
            };
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

            if (hour >= HOURS_PER_DAY)
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

            if (day > DAYS_PER_MONTH)
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

            if (month > MONTHS_PER_YEAR)
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
                eventBus?.Emit(new TimeStateChangedEvent { IsPaused = false, GameSpeed = gameSpeedLevel });
                ArchonLogger.Log("Time progression started");
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
                eventBus?.Emit(new TimeStateChangedEvent { IsPaused = true, GameSpeed = gameSpeedLevel });
                ArchonLogger.Log("Time progression paused");
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
        /// Set game speed (0=paused, 1-4=speed levels)
        /// </summary>
        public void SetGameSpeed(int newSpeedLevel)
        {
            if (newSpeedLevel < 0 || newSpeedLevel > 4)
            {
                ArchonLogger.LogWarning($"Invalid speed level: {newSpeedLevel} (must be 0-4)");
                return;
            }

            int oldSpeed = gameSpeedLevel;
            gameSpeedLevel = newSpeedLevel;

            // Speed 0 means paused
            if (newSpeedLevel == 0)
            {
                PauseTime();
            }
            else if (oldSpeed == 0)
            {
                StartTime();
            }

            OnSpeedChanged?.Invoke(gameSpeedLevel);
            eventBus?.Emit(new TimeStateChangedEvent { IsPaused = isPaused, GameSpeed = gameSpeedLevel });

            ArchonLogger.Log($"Game speed changed to level {gameSpeedLevel}");
        }

        /// <summary>
        /// Set specific game date (for loading saves, scenarios)
        /// </summary>
        public void SetGameTime(int newYear, int newMonth, int newDay, int newHour = 0)
        {
            year = newYear;
            month = Mathf.Clamp(newMonth, 1, MONTHS_PER_YEAR);
            day = Mathf.Clamp(newDay, 1, DAYS_PER_MONTH);
            hour = Mathf.Clamp(newHour, 0, HOURS_PER_DAY - 1);

            accumulator = FixedPoint64.Zero;

            eventBus?.Emit(new TimeChangedEvent { GameTime = GetCurrentGameTime() });
            ArchonLogger.Log($"Game time set to: {GetCurrentGameTime()}");
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
                ArchonLogger.LogWarning($"Cannot synchronize backwards: current={currentTick}, target={targetTick}");
                return;
            }

            while (currentTick < targetTick)
            {
                AdvanceHour();
            }

            ArchonLogger.Log($"Synchronized to tick {currentTick}");
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
        /// Calculate total ticks since start date
        /// </summary>
        public ulong CalculateTotalTicks(int fromYear, int fromMonth, int fromDay)
        {
            ulong totalHours = 0;

            // Calculate years
            int yearDiff = year - fromYear;
            totalHours += (ulong)(yearDiff * DAYS_PER_YEAR * HOURS_PER_DAY);

            // Calculate months
            int monthDiff = month - fromMonth;
            totalHours += (ulong)(monthDiff * DAYS_PER_MONTH * HOURS_PER_DAY);

            // Calculate days
            int dayDiff = day - fromDay;
            totalHours += (ulong)(dayDiff * HOURS_PER_DAY);

            // Add current hour
            totalHours += (ulong)hour;

            return totalHours;
        }

        #if UNITY_EDITOR
        [Header("Debug Controls")]
        [SerializeField] private bool debugShowControls = true;

        void OnGUI()
        {
            if (!debugShowControls || !isInitialized)
                return;

            GUILayout.BeginArea(new Rect(10, 10, 250, 200));
            GUILayout.Label($"Game Time: {year}.{month:D2}.{day:D2} {hour:D2}:00");
            GUILayout.Label($"Tick: {currentTick}");
            GUILayout.Label($"Speed: {gameSpeedLevel}x ({GetSpeedMultiplier(gameSpeedLevel).ToFloat():F1}x)");
            GUILayout.Label($"Paused: {isPaused}");
            GUILayout.Label($"Accumulator: {accumulator.ToFloat():F2}");

            if (GUILayout.Button(isPaused ? "Resume" : "Pause"))
            {
                TogglePause();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.5x")) SetGameSpeed(1);
            if (GUILayout.Button("1x")) SetGameSpeed(2);
            if (GUILayout.Button("2x")) SetGameSpeed(3);
            if (GUILayout.Button("5x")) SetGameSpeed(4);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Advance 1 Day"))
            {
                for (int i = 0; i < HOURS_PER_DAY; i++)
                    AdvanceHour();
            }

            GUILayout.EndArea();
        }
        #endif
    }

    /// <summary>
    /// Represents a point in game time (deterministic calendar)
    /// </summary>
    [System.Serializable]
    public struct GameTime
    {
        public int Year;
        public int Month;  // 1-12
        public int Day;    // 1-30 (simplified 30-day months)
        public int Hour;   // 0-23

        public override string ToString()
        {
            return $"{Year}.{Month:D2}.{Day:D2} {Hour:D2}:00";
        }

        public static bool operator ==(GameTime a, GameTime b)
        {
            return a.Year == b.Year && a.Month == b.Month && a.Day == b.Day && a.Hour == b.Hour;
        }

        public static bool operator !=(GameTime a, GameTime b)
        {
            return !(a == b);
        }

        public override bool Equals(object obj)
        {
            return obj is GameTime time && this == time;
        }

        public override int GetHashCode()
        {
            return Year * 1000000 + Month * 10000 + Day * 100 + Hour;
        }

        /// <summary>
        /// Convert to total hours since year 0
        /// </summary>
        public ulong ToTotalHours()
        {
            const int HOURS_PER_DAY = 24;
            const int DAYS_PER_MONTH = 30;
            const int DAYS_PER_YEAR = 360;

            ulong totalHours = 0;
            totalHours += (ulong)(Year * DAYS_PER_YEAR * HOURS_PER_DAY);
            totalHours += (ulong)((Month - 1) * DAYS_PER_MONTH * HOURS_PER_DAY);
            totalHours += (ulong)((Day - 1) * HOURS_PER_DAY);
            totalHours += (ulong)Hour;

            return totalHours;
        }
    }
}
