# Time System

The time system provides deterministic time progression for multiplayer-ready simulation. It uses fixed-point math and tick-based progression to ensure identical behavior across all clients.

## Architecture

```
TimeManager (MonoBehaviour)
├── ICalendar              - Date formatting and validation
├── FixedPoint64           - Deterministic accumulator
├── Tick counter           - Command synchronization
└── Layered events         - Hour/Day/Week/Month/Year
```

**Key Principles:**
- Fixed-point accumulator (NO float drift)
- Real Earth calendar (365 days, no leap years)
- Tick counter for multiplayer sync
- Death spiral prevention (max ticks per frame)

## Basic Usage

### Initialization

```csharp
var timeManager = gameObject.AddComponent<TimeManager>();
timeManager.Initialize(eventBus, provinceSystem);
```

### Controlling Time

```csharp
// Start/Pause
timeManager.StartTime();
timeManager.PauseTime();
timeManager.TogglePause();

// Set speed (0=paused, 1=1x, 2=2x, etc.)
timeManager.SetSpeed(3);  // 3x speed

// Check state
bool paused = timeManager.IsPaused;
int speed = timeManager.GameSpeed;
```

### Reading Current Time

```csharp
// Individual components
int year = timeManager.CurrentYear;
int month = timeManager.CurrentMonth;
int day = timeManager.CurrentDay;
int hour = timeManager.CurrentHour;

// As struct
GameTime current = timeManager.GetCurrentGameTime();

// Formatted
string date = timeManager.GetFormattedDate();       // "11 November 1444 AD"
string compact = timeManager.GetFormattedDateCompact(); // "11 Nov 1444"

// Tick counter (for multiplayer)
ulong tick = timeManager.CurrentTick;
```

## Time Events

### EventBus Events (Recommended)

```csharp
// Subscribe via EventBus
gameState.EventBus.Subscribe<HourlyTickEvent>(OnHourlyTick);
gameState.EventBus.Subscribe<DailyTickEvent>(OnDailyTick);
gameState.EventBus.Subscribe<WeeklyTickEvent>(OnWeeklyTick);
gameState.EventBus.Subscribe<MonthlyTickEvent>(OnMonthlyTick);
gameState.EventBus.Subscribe<YearlyTickEvent>(OnYearlyTick);

void OnMonthlyTick(MonthlyTickEvent evt)
{
    Debug.Log($"Month: {evt.GameTime.Month}, Tick: {evt.Tick}");
}
```

### C# Events (Legacy)

```csharp
// Direct subscription
timeManager.OnHourlyTick += OnHourTick;
timeManager.OnMonthlyTick += OnMonthTick;
timeManager.OnSpeedChanged += OnSpeedChanged;
timeManager.OnPauseStateChanged += OnPauseChanged;

void OnMonthTick(int month)
{
    Debug.Log($"New month: {month}");
}
```

### Event Frequency

| Event | Frequency | Use Case |
|-------|-----------|----------|
| HourlyTickEvent | Every game hour | Unit movement, AI processing |
| DailyTickEvent | Every game day | Resource consumption |
| WeeklyTickEvent | Every 7 days | Rebel checks, attrition |
| MonthlyTickEvent | Every month | Income, building completion |
| YearlyTickEvent | Every year | Population growth, decay |

## GameTime Struct

### Creating GameTime

```csharp
// Factory method
var date = GameTime.Create(1444, 11, 11, 0);

// From total hours
var date = GameTime.FromTotalHours(totalHours);
```

### Comparisons

```csharp
GameTime a = GameTime.Create(1444, 11, 11);
GameTime b = GameTime.Create(1444, 12, 1);

bool earlier = a < b;   // true
bool later = a > b;     // false
bool equal = a == b;    // false
```

### Arithmetic

```csharp
var date = GameTime.Create(1444, 11, 11);

// Add time
var tomorrow = date.AddDays(1);
var nextMonth = date.AddMonths(1);
var nextYear = date.AddYears(1);
var later = date.AddHours(24);

// Duration calculations
long hours = date.HoursBetween(otherDate);
int days = date.DaysBetween(otherDate);
```

### Conversion

```csharp
// To total hours (for persistence/comparison)
long totalHours = date.ToTotalHours();

// Back to GameTime
var restored = GameTime.FromTotalHours(totalHours);
```

## Calendar System

### Standard Calendar

The default calendar uses real Earth dates (365 days, no leap years):

```csharp
var calendar = timeManager.Calendar;

string monthName = calendar.GetMonthName(11);  // "November"
int daysInMonth = calendar.GetDaysInMonth(2);  // 28 (February)
```

### Custom Calendar

Implement `ICalendar` for fantasy calendars:

```csharp
public class FantasyCalendar : ICalendar
{
    public int MonthsPerYear => 13;
    public int HoursPerDay => 20;

    public int GetDaysInMonth(int month) => 28;  // All months same length

    public string GetMonthName(int month) => month switch
    {
        1 => "Firstmoon",
        2 => "Snowmelt",
        // ...
    };

    public string FormatDate(GameTime time)
        => $"{time.Day} {GetMonthName(time.Month)}, Year {time.Year}";
}

// Use custom calendar
timeManager.Initialize(eventBus, provinceSystem, new FantasyCalendar());
```

## Multiplayer Synchronization

### Tick-Based Commands

All commands use tick counters for synchronization:

```csharp
public class MyCommand : BaseCommand
{
    public ulong ExecutionTick { get; set; }

    public override void Execute(GameState gameState)
    {
        var time = gameState.GetComponent<TimeManager>();
        // Command executes at specific tick
    }
}
```

### Synchronizing Clients

```csharp
// On receiving server state
timeManager.SynchronizeToTick(serverTick);

// Manual tick adjustment (for testing)
timeManager.SetCurrentTick(targetTick);
```

## Death Spiral Prevention

The system caps ticks per frame to prevent death spiral:

```csharp
// If frame takes too long, time slows down rather than piling on work
// Leftover accumulator carries to next frame
private const int MAX_TICKS_PER_FRAME = 4;
```

This means at very high speeds, game time may slow down rather than causing frame drops.

## Save/Load Support

```csharp
// Save
var state = new TimeSaveData
{
    Tick = timeManager.CurrentTick,
    Year = timeManager.CurrentYear,
    Month = timeManager.CurrentMonth,
    Day = timeManager.CurrentDay,
    Hour = timeManager.CurrentHour,
    Speed = timeManager.GameSpeed,
    Paused = timeManager.IsPaused,
    Accumulator = timeManager.GetAccumulator()
};

// Load
timeManager.LoadState(
    state.Tick,
    state.Year, state.Month, state.Day, state.Hour,
    state.Speed, state.Paused, state.Accumulator
);
```

## Inspector Configuration

```csharp
[Header("Game Configuration")]
[SerializeField] private int startYear = 1444;
[SerializeField] private int startMonth = 11;
[SerializeField] private int startDay = 11;
[SerializeField] private bool autoStart = true;

[Header("Speed Configuration")]
[SerializeField] private int initialSpeed = 0;  // 0=paused
```

## Performance Tips

1. **Use monthly events** for economic calculations (not hourly)
2. **Use hourly events** for movement/AI only when needed
3. **Cache time references** in systems that use time frequently
4. **Use tick comparisons** instead of GameTime for hot paths

## API Reference

- [TimeManager](~/api/Core.Systems.TimeManager.html) - Main time controller
- [GameTime](~/api/Core.Systems.GameTime.html) - Time point struct
- [ICalendar](~/api/Core.Systems.ICalendar.html) - Calendar interface
- [HourlyTickEvent](~/api/Core.Systems.HourlyTickEvent.html) - Hourly event
- [MonthlyTickEvent](~/api/Core.Systems.MonthlyTickEvent.html) - Monthly event
