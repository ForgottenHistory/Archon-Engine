# StarterKit Implementation
**Date**: 2026-01-10
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Create a StarterKit assembly for simple demo/debug scenes using the Archon Engine

**Success Criteria:**
- Country selection UI with Start button
- Resource bar showing gold (1 gold per province per month)
- Time UI with configurable speed controls

---

## What We Did

### 1. Created StarterKit Assembly
**Files Created:** `Assets/Archon-Engine/Scripts/StarterKit/`

- `StarterKit.asmdef` - References Core, MapAssembly, Utils
- `PlayerEvents.cs` - `PlayerCountrySelectedEvent` struct
- `PlayerState.cs` - Plain C# class tracking player's country
- `EconomySystem.cs` - Plain C# class with IDisposable, 1 gold/province/month
- `CountrySelectionUI.cs` - MonoBehaviour with UIDocument, country selection + Start
- `ResourceBarUI.cs` - MonoBehaviour with UIDocument, shows gold at top
- `TimeUI.cs` - MonoBehaviour with UIDocument, time controls at top-left
- `StarterKitInitializer.cs` - Coordinates initialization sequence

**Architecture:**
- PlayerState and EconomySystem are plain C# classes (not MonoBehaviours)
- StarterKitInitializer owns and creates them
- UI components require UIDocument (must be MonoBehaviours)
- Subscribes to EventBus for PlayerCountrySelectedEvent

### 2. Simplified TimeManager Speed System
**Files Changed:** `Core/Systems/TimeManager.cs`

**Before:** Array-based speed levels with index mapping
```csharp
int[] speedMultipliers = { 1, 2, 3, 4, 5, 10 };
void SetGameSpeed(int level) // level indexes into array
```

**After:** Direct multiplier value
```csharp
int gameSpeed; // Actual multiplier (0=paused, 1=1x, 2=2x, etc.)
void SetSpeed(int multiplier) // Pass actual value
```

**Rationale:** Simpler API, no indirection, UI defines its own speeds

### 3. Updated Dependent Files
- `Game/Debug/TimeDebugPanel.cs` - Use SetSpeed() with actual values
- `Map/Debug/EngineDebugUI.cs` - Use SetSpeed() with actual values

---

## Decisions Made

### Decision 1: Plain Classes vs MonoBehaviours
**Context:** PlayerState and EconomySystem don't need Unity lifecycle
**Decision:** Plain C# classes owned by StarterKitInitializer
**Rationale:** Simpler, no unnecessary components in scene
**Trade-offs:** Must manually dispose EconomySystem (IDisposable)

### Decision 2: Speed API Simplification
**Context:** TimeManager had array-based speed levels requiring two places to update
**Decision:** Simple `SetSpeed(int multiplier)` accepting actual value
**Rationale:** One source of truth - UI defines speeds, passes value directly
**Trade-offs:** Removed 0.5x speed option (only integers now)

---

## Problems Encountered & Solutions

### Problem 1: Province Count Returning 0
**Symptom:** EconomySystem.GetMonthlyIncome() returned 0 provinces
**Root Cause:** Manual iteration with GetOwner() didn't match engine's bidirectional mapping

**Solution:** Use the proper API
```csharp
// Before (wrong):
for (ushort i = 1; i <= provinceCount; i++)
    if (gameState.ProvinceQueries.GetOwner(i) == countryId) count++;

// After (correct):
return gameState.ProvinceQueries.GetCountryProvinceCount(countryId);
```

**Pattern:** Always check GAME layer for existing API usage before implementing

### Problem 2: Assembly Reference Names
**Symptom:** CS0246 errors for Map namespace
**Root Cause:** Assembly named "MapAssembly" not "Map"
**Solution:** Check actual .asmdef file names before referencing

---

## Architecture Impact

### StarterKit Layer Position
```
Core → Map → StarterKit → Game
         ↘           ↗
          (both can use StarterKit patterns)
```

StarterKit references Core and Map, provides simple implementations that Game can extend or replace.

---

## Code Quality Notes

### Scene Setup Required
| GameObject | Components |
|------------|------------|
| StarterKitManager | `StarterKitInitializer` |
| CountrySelectionUI | `UIDocument` + `CountrySelectionUI` |
| ResourceBarUI | `UIDocument` + `ResourceBarUI` |
| TimeUI | `UIDocument` + `TimeUI` |

---

## Quick Reference for Future Claude

**Key Files:**
- `StarterKit/StarterKitInitializer.cs` - Entry point, owns PlayerState + EconomySystem
- `StarterKit/TimeUI.cs` - Accepts `int[] customSpeeds` in Initialize()
- `Core/Systems/TimeManager.cs:268` - `SetSpeed(int multiplier)` API

**What Changed:**
- TimeManager: `SetGameSpeed(level)` → `SetSpeed(multiplier)`
- Speed configuration: Array in TimeManager → Array in UI component

**Gotchas:**
- Use `ProvinceQueries.GetCountryProvinceCount()` not manual iteration
- Check assembly .asmdef names match references exactly
- EconomySystem needs Dispose() call in OnDestroy

---

## Files Changed Summary

**New Files (StarterKit):** 8 files
- StarterKit.asmdef, PlayerEvents.cs, PlayerState.cs, EconomySystem.cs
- CountrySelectionUI.cs, ResourceBarUI.cs, TimeUI.cs, StarterKitInitializer.cs

**Modified Files:** 3 files
- Core/Systems/TimeManager.cs - Simplified speed API
- Game/Debug/TimeDebugPanel.cs - Updated to SetSpeed()
- Map/Debug/EngineDebugUI.cs - Updated to SetSpeed()

---

*Session focused on creating reusable StarterKit for engine demos*
