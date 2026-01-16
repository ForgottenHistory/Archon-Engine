# Unit Movement System: Timed Movement with Path Visualization
**Date**: 2026-01-16
**Session**: 04
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Add ability to move units around provinces with UI buttons

**Secondary Objectives:**
- Movement should happen over time (not instant teleport)
- Show path lines during movement (EU4-style)
- Use existing PathfindingSystem

**Success Criteria:**
- Units can be ordered to move via UI
- Movement takes time based on unit speed stat
- Path lines show movement route

---

## Context & Background

**Previous Work:**
- See: [03-unit-system-simplification.md](03-unit-system-simplification.md)
- UnitState simplified to 8 bytes with unitCount field

**Current State:**
- Core already had `UnitMovementQueue` for timed movement
- Core already had `PathfindingSystem` with A* pathfinding
- GAME layer had `UnitVisualizationSystem` with path lines
- StarterKit UI had no movement controls

**Why Now:**
- Units exist but can't be moved
- Core systems were ready, just needed UI integration

---

## What We Did

### 1. Added Movement UI to UnitInfoUI
**Files Changed:** `StarterKit/UnitInfoUI.cs`

- Added "Move Units" button (shows when player units exist in province)
- Added "Cancel" button for exiting move mode
- Move mode workflow:
  1. Click "Move Units" → enters move mode
  2. Click destination province → validates path, issues movement orders
  3. If no path exists → stays in move mode to try another target

**Key Code:**
```csharp
// Issue movement orders via Core's MovementQueue
var movementQueue = gameState.Units?.MovementQueue;
ushort firstDestination = path[1];
movementQueue.StartMovement(unitId, firstDestination, movementDays, path);
```

### 2. Created UnitPathRenderer in Map Layer
**Files Changed:** `Map/Rendering/UnitPathRenderer.cs` (new)

Moved path visualization from GAME to ENGINE (Map layer):
- Subscribes to `UnitMovementStartedEvent`, `UnitMovementCompletedEvent`, `UnitMovementCancelledEvent`
- Creates LineRenderer for each moving unit
- Uses `ProvinceCenterLookup` to convert province IDs to world positions
- Updates/destroys path lines as units move

**Rationale:**
- Generic enough for ENGINE (just LineRenderers + Core events)
- GAME layer can override/replace if needed
- Map layer appropriate since it's visual representation

### 3. Connected UnitSystem to Daily Tick
**Files Changed:** `Core/Units/UnitSystem.cs`

- Added subscription to `DailyTickEvent`
- `OnDailyTick()` calls `MovementQueue.ProcessDailyTick()`
- Proper cleanup in `Dispose()`

**Key Code:**
```csharp
private void OnDailyTick(DailyTickEvent evt)
{
    movementQueue?.ProcessDailyTick();
}
```

### 4. Added Speed to StarterKit UnitType
**Files Changed:** `StarterKit/UnitSystem.cs`

- Added `Speed` property to `UnitType` (days per province, default 2)
- Loads from JSON5 `stats.speed` field

### 5. Integrated Path Renderer into UnitVisualization
**Files Changed:** `StarterKit/UnitVisualization.cs`

- Added `enablePathLines` toggle
- Creates `UnitPathRenderer` and shares `ProvinceCenterLookup`

### 6. Cleaned Up Redundant Code
**Files Deleted:** `StarterKit/UnitMovementSystem.cs`

Initially created a redundant movement system, then discovered Core already had `UnitMovementQueue`. Deleted redundant code.

**Files Changed:** `StarterKit/Initializer.cs`
- Removed `UnitMovementSystem` references

---

## Decisions Made

### Decision 1: Use Core's UnitMovementQueue vs StarterKit System
**Context:** Initially created `StarterKit/UnitMovementSystem` for timed movement

**Options Considered:**
1. Keep StarterKit system (duplicate functionality)
2. Use Core's existing `UnitMovementQueue`

**Decision:** Use Core's `UnitMovementQueue`
**Rationale:**
- Core already had full implementation with events
- Avoids code duplication
- Events already integrated with GAME visualization
**Trade-offs:** None - Core system is complete

### Decision 2: Path Visualization in Map Layer vs StarterKit
**Context:** GAME had `UnitVisualizationSystem` with path lines

**Options Considered:**
1. Keep in GAME layer
2. Move to StarterKit
3. Move to Map layer (ENGINE)

**Decision:** Created `UnitPathRenderer` in Map layer
**Rationale:**
- Generic enough (LineRenderer + Core events)
- Map layer handles visual representation
- Can be disabled/replaced by GAME if needed
- Follows ENGINE/GAME separation

---

## What Worked ✅

1. **Leveraging Existing Core Systems**
   - What: Used `UnitMovementQueue` and `PathfindingSystem` instead of reimplementing
   - Why it worked: Core systems were complete and well-designed
   - Reusable pattern: Always check Core before implementing in StarterKit

2. **Event-Driven Path Visualization**
   - What: `UnitPathRenderer` subscribes to movement events
   - Why it worked: Decoupled from movement logic, auto-updates

---

## What Didn't Work ❌

1. **Creating Redundant UnitMovementSystem**
   - What we tried: New movement system in StarterKit
   - Why it failed: Core already had `UnitMovementQueue`
   - Lesson learned: Check Core/FILE_REGISTRY.md before implementing
   - Don't try this again because: Wastes time, creates maintenance burden

---

## Architecture Impact

### Pattern Reinforced
**Pattern:** ENGINE/GAME Separation
- Core provides movement queue (mechanism)
- Map provides path visualization (generic visual)
- StarterKit/GAME provides UI (policy)

### Code Quality Notes

**Technical Debt:**
- **Paid Down:** Deleted redundant `UnitMovementSystem`

---

## Session Statistics

**Files Changed:** 6
- Map/Rendering/UnitPathRenderer.cs (new)
- Core/Units/UnitSystem.cs (tick subscription)
- StarterKit/UnitInfoUI.cs (move mode UI)
- StarterKit/UnitVisualization.cs (path renderer integration)
- StarterKit/UnitSystem.cs (Speed property)
- StarterKit/Initializer.cs (cleanup)

**Files Deleted:** 1
- StarterKit/UnitMovementSystem.cs

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Movement uses Core's `UnitMovementQueue` via `gameState.Units.MovementQueue`
- Path lines rendered by `Map/Rendering/UnitPathRenderer`
- Movement processed on `DailyTickEvent` in `UnitSystem.OnDailyTick()`
- Unit speed from `StarterKit.UnitType.Speed` (days per province)

**Gotchas for Next Session:**
- `PathfindingSystem` must be initialized after adjacencies (done in StarterKit Initializer)
- Movement events: `UnitMovementStartedEvent`, `UnitMovementCompletedEvent`, `UnitMovementCancelledEvent`
- Path includes start position at index 0

---

## Links & References

### Code References
- Movement queue: `Core/Units/UnitMovementQueue.cs`
- Path renderer: `Map/Rendering/UnitPathRenderer.cs`
- Move UI: `StarterKit/UnitInfoUI.cs:450-580`
- Daily tick: `Core/Units/UnitSystem.cs:551-556`

### Related Sessions
- Previous: [03-unit-system-simplification.md](03-unit-system-simplification.md)

---

## Notes & Observations

- Core's `UnitMovementQueue` already had multi-hop path support
- Movement events fire for each hop, allowing path line updates
- Infantry speed is 2 days per province (from Template-Data/units/infantry.json5)

---

*Session Duration: ~45 minutes*
