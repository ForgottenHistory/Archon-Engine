# Calendar System Refactor
**Date**: 2026-01-14
**Session**: 8
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Centralize time constants (single source of truth)
- Add ICalendar interface for custom calendars
- Enhance GameTime with proper operations
- Switch from 360-day simplified to real 365-day Earth calendar

**Success Criteria:**
- CalendarConstants as single source of truth
- ICalendar + StandardCalendar in ENGINE
- GameTime with FromTotalHours, comparison ops, arithmetic
- TimeManager using ICalendar for variable month lengths

---

## What We Did

### 1. Created CalendarConstants (Single Source of Truth)

**File:** `Core/Data/CalendarConstants.cs`

- Real Earth calendar: 365 days, no leap years
- `DAYS_IN_MONTH[]` array for variable month lengths (31/30/28)
- `DAYS_BEFORE_MONTH[]` for fast day-of-year calculation
- Pre-calculated `HOURS_PER_YEAR = 8760`
- Simplified calendar constants still available for games that want 360-day

### 2. Created ICalendar Interface

**File:** `Core/Systems/ICalendar.cs`

```csharp
public interface ICalendar
{
    int HoursPerDay { get; }
    int MonthsPerYear { get; }
    int DaysPerYear { get; }
    int GetDaysInMonth(int month);
    string GetMonthName(int month);
    string FormatYear(int year);  // Era support (BC/AD)
    string FormatDate(GameTime time);
    bool IsValidDate(int year, int month, int day);
    GameTime ClampToValidDate(int year, int month, int day, int hour);
}
```

### 3. Created StandardCalendar

**File:** `Core/Systems/StandardCalendar.cs`

- Real Earth calendar with proper month lengths
- English month names (January-December)
- AD/BC era formatting
- Validation and clamping

### 4. Enhanced GameTime Struct

**File:** `Core/Systems/TimeManager.cs` (GameTime struct)

**Added:**
- `FromTotalHours(long)` - Inverse of ToTotalHours
- `Create(year, month, day, hour)` - Factory method
- Comparison operators: `<`, `>`, `<=`, `>=`
- `CompareTo(GameTime)` - IComparable
- `AddHours(int)`, `AddDays(int)`, `AddMonths(int)`, `AddYears(int)`
- `HoursBetween(GameTime)`, `DaysBetween(GameTime)`
- `ToString(ICalendar)` - Calendar-aware formatting

**Refactored:**
- `ToTotalHours()` now uses CalendarConstants with real month lengths

### 5. Updated TimeManager

- Added `ICalendar calendar` field
- `Initialize()` accepts optional `ICalendar customCalendar`
- `Calendar` property for GAME layer access
- `AdvanceDay/Month` use `calendar.GetDaysInMonth(month)`
- Added `GetFormattedDate()`, `GetFormattedDateCompact()`, `GetCurrentMonthName()`

### 6. Migrated AIScheduler

- Removed hardcoded `HOURS_PER_YEAR = 8640`
- `CalculateHourOfYear()` uses `CalendarConstants.DAYS_BEFORE_MONTH[]`
- `ShouldProcess()` uses `CalendarConstants.HOURS_PER_YEAR`

---

## Architecture Impact

### Calendar System Pattern

```
ENGINE Layer:
├── CalendarConstants (single source of truth)
├── ICalendar (interface)
├── StandardCalendar (default 365-day)
└── TimeManager (uses ICalendar)

GAME Layer (future):
└── HegemonCalendar : ICalendar (custom formatting)
```

### Key Design Decisions

1. **365-day default**: Real Earth calendar, no leap years for determinism
2. **No leap years**: Multiplayer determinism (Feb always 28 days)
3. **ICalendar injection**: TimeManager.Initialize() accepts custom calendar
4. **Backward compatible**: Calendar parameter is optional

---

## Files Changed

| File | Changes |
|------|---------|
| `Core/Data/CalendarConstants.cs` | NEW - Time constants |
| `Core/Systems/ICalendar.cs` | NEW - Calendar interface |
| `Core/Systems/StandardCalendar.cs` | NEW - Default implementation |
| `Core/Systems/TimeManager.cs` | ICalendar support, GameTime enhancements |
| `Core/AI/AIScheduler.cs` | Use CalendarConstants |

---

## Quick Reference for Future Claude

**Custom calendar in GAME layer:**
```csharp
public class HegemonCalendar : ICalendar
{
    private readonly StandardCalendar base = new StandardCalendar();
    // Override formatting, delegate mechanics to base
}

// In initializer:
timeManager.Initialize(eventBus, provinceSystem, new HegemonCalendar());
```

**Date arithmetic:**
```csharp
var future = time.AddMonths(6);
var past = time.AddDays(-30);
int daysDiff = time.DaysBetween(otherTime);
```

**Date comparison:**
```csharp
if (deadline < currentTime) { /* expired */ }
```

---

## Session Statistics

**Files Created:** 3
**Files Modified:** 2
**Calendar Type:** Real Earth (365-day, variable months)
**Breaking Change:** ToTotalHours() return type changed ulong→long

---

## Links & References

### Related Sessions
- [Previous: Modifier Query API](7-modifier-query-api.md)

### Code References
- CalendarConstants: `Core/Data/CalendarConstants.cs`
- ICalendar: `Core/Systems/ICalendar.cs`
- StandardCalendar: `Core/Systems/StandardCalendar.cs`
- GameTime: `Core/Systems/TimeManager.cs:417-656`

---

*Calendar system refactored with real 365-day Earth calendar, ICalendar abstraction for custom calendars, and enhanced GameTime with full arithmetic and comparison support.*
