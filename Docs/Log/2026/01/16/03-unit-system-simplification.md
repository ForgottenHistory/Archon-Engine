# Unit System Simplification: RISK-Style Combat
**Date**: 2026-01-16
**Session**: 03
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Simplify unit system for RISK-style combat (remove strength/morale, use simple troop count)

**Secondary Objectives:**
- Update StarterKit UI to stack units like buildings
- Keep ENGINE generic, let GAME define combat policy

**Success Criteria:**
- UnitState remains 8 bytes
- No complex combat stats in ENGINE
- UI shows stacked units (e.g., "Infantry x5")

---

## Context & Background

**Previous Work:**
- See: [02-terrain-shader-simplification.md](02-terrain-shader-simplification.md)

**Current State:**
- UnitState had `strength` (byte 0-100%) and `morale` (byte 0-100%)
- These were leftover from planned EU4-style combat that was never implemented
- Made no sense for a generic engine or starter kit

**Why Now:**
- Moving toward simple RISK-style combat for starter kit
- ENGINE should be generic, not dictate combat mechanics

---

## What We Did

### 1. Simplified UnitState Struct
**Files Changed:** `Core/Units/UnitState.cs`

**Before (8 bytes):**
```csharp
public ushort provinceID;   // 2 bytes
public ushort countryID;    // 2 bytes
public ushort unitTypeID;   // 2 bytes
public byte strength;       // 1 byte (0-100%)
public byte morale;         // 1 byte (0-100%)
```

**After (8 bytes):**
```csharp
public ushort provinceID;   // 2 bytes
public ushort countryID;    // 2 bytes
public ushort unitTypeID;   // 2 bytes
public ushort unitCount;    // 2 bytes (troop count)
```

**Rationale:**
- Simple troop count is all a generic engine needs
- GAME layer can add complexity via UnitDefinition or UnitColdData if needed
- ushort gives 0-65535 troops per unit (plenty for RISK-style)

### 2. Updated UnitEvents
**Files Changed:** `Core/Units/UnitEvents.cs`

- Removed: `UnitStrengthChangedEvent`, `UnitMoraleChangedEvent`
- Added: `UnitCountChangedEvent`
- Added `UnitCount` field to `UnitCreatedEvent`

### 3. Updated UnitSystem
**Files Changed:** `Core/Units/UnitSystem.cs`

- `CreateUnit()` now takes optional `troopCount` parameter (default 1)
- `CreateUnitWithStats()` takes `unitCount` instead of strength/morale
- Removed: `SetUnitStrength()`, `SetUnitMorale()`
- Added: `SetUnitCount()`, `AddTroops()`, `RemoveTroops()`
- `HasUnit()` now checks `unitCount > 0` instead of `strength > 0`

### 4. Updated UnitCommands
**Files Changed:** `Core/Units/UnitCommands.cs`

- `DisbandUnitCommand.Undo()` uses new `CreateUnitWithStats()` signature

### 5. Updated Template Data
**Files Changed:** `Template-Data/units/infantry.json5`

**Before:**
```json5
stats: {
  max_strength: 1000,
  attack: 2,
  defense: 2,
  morale: 3,
  speed: 2
}
```

**After:**
```json5
stats: {
  speed: 2   // Days per province movement
}
```

### 6. Updated StarterKit Commands
**Files Changed:**
- `StarterKit/Commands/DisbandUnitCommand.cs`
- `StarterKit/Commands/MoveUnitCommand.cs`

- Changed `unit.strength > 0` checks to `unit.unitCount > 0`

### 7. Updated StarterKit UI to Stack Units
**Files Changed:** `StarterKit/UnitInfoUI.cs`

**Before:** Listed each unit separately with strength/morale
**After:** Groups units by type, shows total troops (like buildings)

```csharp
// Group units by type and sum troop counts
var unitsByType = new Dictionary<ushort, int>(); // unitTypeID -> total troops
foreach (var unitId in unitIds)
{
    var unitState = unitSystem.GetUnit(unitId);
    if (!unitsByType.ContainsKey(unitState.unitTypeID))
        unitsByType[unitState.unitTypeID] = 0;
    unitsByType[unitState.unitTypeID] += unitState.unitCount;
}
```

Display: `Infantry x5` instead of listing 5 separate units

---

## Decisions Made

### Decision 1: ushort for unitCount vs keeping bytes
**Context:** Had 2 bytes available (old strength + morale)
**Options Considered:**
1. Keep as bytes - could have `count` and something else
2. Use ushort - single field, larger range

**Decision:** ushort for unitCount
**Rationale:**
- Simple is better for generic engine
- 65535 troops per unit is plenty
- If GAME needs more fields, use cold data
**Trade-offs:** Can't add another byte-sized field without breaking 8-byte struct

### Decision 2: Keep speed in JSON5, not hardcoded
**Context:** User pointed out speed should be data, not code
**Decision:** Keep `speed` in UnitStats, loaded from JSON5
**Rationale:** Movement speed is game data, not engine logic

---

## What Worked ✅

1. **User insight: "made no sense for generic engine"**
   - What: User questioned why ENGINE had combat-specific stats
   - Why it worked: Proper ENGINE/GAME separation
   - Reusable pattern: ENGINE = generic mechanism, GAME = specific policy

2. **Following BuildingInfoUI pattern for stacking**
   - What: Used same approach as buildings for grouping units
   - Why it worked: Consistent UX, proven pattern

---

## What Didn't Work ❌

1. **Initial assumption GAME layer wouldn't be affected**
   - What we tried: Only updating ENGINE files
   - Why it failed: GAME directly references `UnitState` struct fields
   - Lesson learned: Struct field changes break all consumers, regardless of layer

---

## Architecture Impact

### Pattern Reinforced
**Pattern:** ENGINE/GAME Separation
- ENGINE provides generic data structures (UnitState with basic fields)
- GAME defines policy (what combat stats mean, how they're used)
- If GAME needs complex stats: use UnitDefinition (static) or UnitColdData (per-unit runtime)

### Code Quality Notes

**Technical Debt:**
- **Paid Down:** Removed unused EU4-style combat stats that were never implemented

---

## Session Statistics

**Files Changed:** 9
- Core/Units/UnitState.cs
- Core/Units/UnitEvents.cs
- Core/Units/UnitSystem.cs
- Core/Units/UnitCommands.cs
- Template-Data/units/infantry.json5
- StarterKit/Commands/DisbandUnitCommand.cs
- StarterKit/Commands/MoveUnitCommand.cs
- StarterKit/UnitInfoUI.cs
- Game/Commands/Factories/Units/ListUnitsCommandFactory.cs (minimal fix)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- UnitState is now: provinceID, countryID, unitTypeID, unitCount (all ushort, 8 bytes)
- No strength/morale in ENGINE - those were removed
- StarterKit UI stacks units by type like buildings

**Gotchas for Next Session:**
- If adding combat, implement in GAME layer, not ENGINE
- UnitColdData exists for per-unit runtime stats if needed
- UnitDefinition (GAME) still has complex stats for GAME-specific use

---

## Links & References

### Code References
- UnitState struct: `Core/Units/UnitState.cs`
- UnitSystem changes: `Core/Units/UnitSystem.cs:99-154`
- UI stacking: `StarterKit/UnitInfoUI.cs:291-308`

### Related Sessions
- Previous: [02-terrain-shader-simplification.md](02-terrain-shader-simplification.md)

---

## Notes & Observations

- The old strength/morale were completely unused - good cleanup
- RISK-style combat just needs to compare unit counts
- ENGINE stays generic, GAME can add EU4-style complexity later if needed

---

*Session Duration: ~30 minutes*
