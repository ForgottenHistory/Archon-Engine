using UnityEngine;
using System;

namespace Core.Systems
{
    /// <summary>
    /// Manages game time progression and tick-based updates
    /// Controls when different systems update based on game time scale
    /// Supports pausing, speed changes, and deterministic time progression
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        [Header("Time Configuration")]
        [SerializeField] private float timeScale = 1.0f;
        [SerializeField] private bool isPaused = false;
        [SerializeField] private bool autoStart = true;

        [Header("Tick Configuration")]
        [SerializeField] private float dailyTickInterval = 1.0f;    // Real seconds per game day
        [SerializeField] private float monthlyTickInterval = 30.0f;  // Real seconds per game month
        [SerializeField] private float yearlyTickInterval = 365.0f;  // Real seconds per game year

        // Current game time
        private GameTime currentGameTime;
        private float lastTickTime;
        private bool isInitialized;

        // Events
        public event Action<GameTime> OnDailyTick;
        public event Action<GameTime> OnMonthlyTick;
        public event Action<GameTime> OnYearlyTick;
        public event Action<float> OnTimeScaleChanged;
        public event Action<bool> OnPauseStateChanged;

        // Properties
        public GameTime CurrentTime => currentGameTime;
        public float TimeScale => timeScale;
        public bool IsPaused => isPaused;
        public bool IsInitialized => isInitialized;

        // Event bus reference
        private EventBus eventBus;

        /// <summary>
        /// Initialize the time manager with event bus
        /// </summary>
        public void Initialize(EventBus eventBus)
        {
            this.eventBus = eventBus;

            // Initialize with default start date (can be customized)
            currentGameTime = new GameTime
            {
                Year = 1444,
                Month = 11,
                Day = 11
            };

            lastTickTime = Time.time;
            isInitialized = true;

            DominionLogger.Log($"TimeManager initialized - Starting date: {currentGameTime}");

            if (autoStart && !isPaused)
            {
                StartTime();
            }
        }

        void Update()
        {
            if (!isInitialized || isPaused)
                return;

            ProcessTimeTicks();
        }

        /// <summary>
        /// Process time progression and emit tick events
        /// </summary>
        private void ProcessTimeTicks()
        {
            float currentTime = Time.time;
            float deltaTime = (currentTime - lastTickTime) * timeScale;

            // Check for daily tick
            if (deltaTime >= dailyTickInterval)
            {
                AdvanceDay();
                lastTickTime = currentTime;

                // Emit daily tick event
                OnDailyTick?.Invoke(currentGameTime);
                eventBus?.Emit(new DailyTickEvent { GameTime = currentGameTime });
            }
        }

        /// <summary>
        /// Advance game time by one day
        /// </summary>
        private void AdvanceDay()
        {
            currentGameTime.Day++;

            // Handle month rollover
            if (currentGameTime.Day > GetDaysInMonth(currentGameTime.Month, currentGameTime.Year))
            {
                currentGameTime.Day = 1;
                currentGameTime.Month++;

                // Emit monthly tick
                OnMonthlyTick?.Invoke(currentGameTime);
                eventBus?.Emit(new MonthlyTickEvent { GameTime = currentGameTime });

                // Handle year rollover
                if (currentGameTime.Month > 12)
                {
                    currentGameTime.Month = 1;
                    currentGameTime.Year++;

                    // Emit yearly tick
                    OnYearlyTick?.Invoke(currentGameTime);
                    eventBus?.Emit(new YearlyTickEvent { GameTime = currentGameTime });
                }
            }
        }

        /// <summary>
        /// Get number of days in a given month/year
        /// </summary>
        private int GetDaysInMonth(int month, int year)
        {
            return DateTime.DaysInMonth(year, month);
        }

        /// <summary>
        /// Start time progression
        /// </summary>
        public void StartTime()
        {
            if (isPaused)
            {
                isPaused = false;
                lastTickTime = Time.time; // Reset to avoid time jump
                OnPauseStateChanged?.Invoke(false);
                eventBus?.Emit(new TimeStateChangedEvent { IsPaused = false, TimeScale = timeScale });
                DominionLogger.Log("Time progression started");
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
                eventBus?.Emit(new TimeStateChangedEvent { IsPaused = true, TimeScale = timeScale });
                DominionLogger.Log("Time progression paused");
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
        /// Set time scale (game speed)
        /// </summary>
        public void SetTimeScale(float newTimeScale)
        {
            if (newTimeScale < 0)
            {
                DominionLogger.LogWarning("Time scale cannot be negative");
                return;
            }

            float oldTimeScale = timeScale;
            timeScale = newTimeScale;

            OnTimeScaleChanged?.Invoke(timeScale);
            eventBus?.Emit(new TimeStateChangedEvent { IsPaused = isPaused, TimeScale = timeScale });

            DominionLogger.Log($"Time scale changed from {oldTimeScale:F2} to {timeScale:F2}");
        }

        /// <summary>
        /// Set specific game date (for loading saves, scenarios)
        /// </summary>
        public void SetGameTime(GameTime newTime)
        {
            currentGameTime = newTime;
            lastTickTime = Time.time;

            eventBus?.Emit(new TimeChangedEvent { GameTime = currentGameTime });
            DominionLogger.Log($"Game time set to: {currentGameTime}");
        }

        /// <summary>
        /// Get time until next tick in real seconds
        /// </summary>
        public float GetTimeUntilNextTick()
        {
            if (isPaused) return float.MaxValue;

            float timeSinceLastTick = (Time.time - lastTickTime) * timeScale;
            return Mathf.Max(0, dailyTickInterval - timeSinceLastTick);
        }

        #if UNITY_EDITOR
        [Header("Debug Controls")]
        [SerializeField] private bool debugShowControls = true;

        void OnGUI()
        {
            if (!debugShowControls || !isInitialized)
                return;

            GUILayout.BeginArea(new Rect(10, 10, 200, 150));
            GUILayout.Label($"Game Time: {currentGameTime}");
            GUILayout.Label($"Time Scale: {timeScale:F2}");
            GUILayout.Label($"Paused: {isPaused}");
            GUILayout.Label($"Next Tick: {GetTimeUntilNextTick():F1}s");

            if (GUILayout.Button(isPaused ? "Resume" : "Pause"))
            {
                TogglePause();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.5x")) SetTimeScale(0.5f);
            if (GUILayout.Button("1x")) SetTimeScale(1.0f);
            if (GUILayout.Button("2x")) SetTimeScale(2.0f);
            if (GUILayout.Button("5x")) SetTimeScale(5.0f);
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
        #endif
    }

    /// <summary>
    /// Represents a point in game time
    /// </summary>
    [System.Serializable]
    public struct GameTime
    {
        public int Year;
        public int Month; // 1-12
        public int Day;   // 1-31

        public override string ToString()
        {
            return $"{Year}.{Month:D2}.{Day:D2}";
        }

        public static bool operator ==(GameTime a, GameTime b)
        {
            return a.Year == b.Year && a.Month == b.Month && a.Day == b.Day;
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
            return Year * 10000 + Month * 100 + Day;
        }
    }

    // Time-related events
    public struct DailyTickEvent : IGameEvent
    {
        public GameTime GameTime;
        public float TimeStamp { get; set; }
    }

    public struct MonthlyTickEvent : IGameEvent
    {
        public GameTime GameTime;
        public float TimeStamp { get; set; }
    }

    public struct YearlyTickEvent : IGameEvent
    {
        public GameTime GameTime;
        public float TimeStamp { get; set; }
    }

    public struct TimeStateChangedEvent : IGameEvent
    {
        public bool IsPaused;
        public float TimeScale;
        public float TimeStamp { get; set; }
    }

    public struct TimeChangedEvent : IGameEvent
    {
        public GameTime GameTime;
        public float TimeStamp { get; set; }
    }
}