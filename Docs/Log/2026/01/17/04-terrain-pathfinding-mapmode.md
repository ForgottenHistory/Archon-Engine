# Terrain-Based Pathfinding & Map Mode
**Date**: 2026-01-17
**Session**: 04
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Add terrain-based movement costs to pathfinding system (ENGINE-GAME separation)
- Create terrain cost map mode to visualize movement costs

**Secondary Objectives:**
- Fix TimeManager speed change bug (accumulator momentum)
- Clean up game-specific fields from ENGINE terrain data

**Success Criteria:**
- Units pathfind using terrain costs from terrain.json5
- Water provinces impassable for land units
- Map mode shows movement cost gradient (green=fast, red=slow)
- Speed changes respond immediately (no momentum delay)

---

## Context & Background

**Previous Work:**
- See: [03-engine-game-data-separation.md](03-engine-game-data-separation.md)
- Refactored CountryData, ProvinceColdData, Json5 data to be generic

**Current State:**
- PathfindingSystem existed with IMovementCostCalculator interface
- UniformCostCalculator was default (all costs = 1)
- Terrain had game-specific fields (defence, supply_limit) in ENGINE

**Why Now:**
- Demonstrate proper ENGINE-GAME separation for pathfinding
- Visualize terrain system for players

---

## What We Did

### 1. Cleaned Up TerrainData (ENGINE)
**Files Changed:**
- `Core/Registries/GameRegistries.cs:67-99`
- `Core/Loaders/TerrainLoader.cs:75-82, 130-165`
- `Template-Data/map/terrain.json5`

**Removed game-specific fields:**
- `DefenceBonus` - GAME policy
- `SupplyLimit` - GAME policy

**Kept ENGINE mechanism:**
- `MovementCost` - pathfinding cost multiplier
- `IsWater` - terrain classification (factual, not policy)
- `Color` - map rendering
- Added `CustomData` dictionary for GAME extensions

### 2. Created TerrainMovementCostCalculator (ENGINE)
**Files Changed:** `Core/Systems/Pathfinding/TerrainMovementCostCalculator.cs`

```csharp
// ENGINE: Pure mechanism - looks up costs, doesn't decide policy
public class TerrainMovementCostCalculator : IMovementCostCalculator
{
    public FixedPoint64 GetMovementCost(...) => terrainCosts[terrainId];
    public bool CanTraverse(...) => true; // ENGINE doesn't restrict
    public bool IsProvinceWater(ushort provinceId); // Helper for GAME
}
```

**Key design:** ENGINE provides terrain cost lookups but doesn't decide what's passable. GAME layer wraps this with policy.

### 3. Created LandUnitCostCalculator (STARTERKIT/GAME)
**Files Changed:** `StarterKit/Systems/LandUnitCostCalculator.cs`

```csharp
// GAME: Wraps ENGINE calculator, adds policy
public class LandUnitCostCalculator : IMovementCostCalculator
{
    public bool CanTraverse(ushort provinceId, PathContext context)
    {
        return !terrainCalculator.IsProvinceWater(provinceId);
    }
}
```

**Architecture:** GAME implements policy (land units can't cross water) using ENGINE mechanism.

### 4. Wired Up in Initializer
**Files Changed:** `StarterKit/Initializer.cs:619-633`

After PathfindingSystem.Initialize():
```csharp
var terrainCalculator = new TerrainMovementCostCalculator(gameState.Provinces, gameState.Registries.Terrains);
var landUnitCalculator = new LandUnitCostCalculator(terrainCalculator);
gameState.Pathfinding.SetDefaultCostCalculator(landUnitCalculator);
```

### 5. Created TerrainCostMapMode
**Files Changed:** `StarterKit/MapModes/TerrainCostMapMode.cs`

Visualizes terrain movement costs:
- Green (1.0x) - grasslands, plains
- Yellow (1.1-1.3x) - desert, marsh, highlands
- Orange (1.4-1.5x) - hills, mountains, jungle
- Red (1.6x+) - snow
- Blue - water (impassable)

Registered under `MapMode.Terrain` with ShaderModeID 3.

### 6. Updated ToolbarUI for Map Mode Cycling
**Files Changed:** `StarterKit/UI/ToolbarUI.cs:30-154`

Changed from toggle to cycle:
```csharp
private static readonly MapMode[] mapModeCycle = { MapMode.Political, MapMode.Economic, MapMode.Terrain };
```

### 7. Fixed TimeManager Speed Change Bug
**Files Changed:** `Core/Systems/TimeManager.cs:292-302`

**Bug:** Changing speed from 100x to 1x continued at high speed until accumulator drained.

**Fix:** Reset accumulator when speed decreases:
```csharp
if (multiplier < oldSpeed && accumulator > FixedPoint64.One)
{
    accumulator = FixedPoint64.FromFloat(0.5f);
}
```

---

## Decisions Made

### Decision 1: IsWater in ENGINE vs GAME
**Context:** Should `IsWater` be ENGINE or GAME layer?

**Options:**
1. GAME only - ENGINE doesn't know about water
2. ENGINE as mechanism - factual terrain classification

**Decision:** ENGINE as mechanism (IsWater is factual classification, not policy)

**Rationale:**
- "Is this water?" is a fact, not a rule
- "Can land units cross water?" is policy (GAME decides)
- ENGINE exposes `IsProvinceWater()` helper, GAME uses it for policy

### Decision 2: Accumulator Reset on Speed Change
**Context:** How to handle accumulator when speed decreases?

**Options:**
1. Keep accumulator - causes momentum bug
2. Reset to zero - loses partial tick progress
3. Clamp to 0.5 - immediate response, keeps some progress

**Decision:** Clamp to 0.5 when speed decreases

**Rationale:** User expects immediate speed response. Small partial progress is acceptable.

---

## What Worked ✅

1. **Wrapper Pattern for Cost Calculator**
   - ENGINE provides mechanism (terrain costs)
   - GAME wraps with policy (water blocking)
   - Clean separation, easy to extend (naval units, amphibious)

2. **Pre-cached Terrain Lookup**
   - O(1) array lookup by terrain ID
   - No dictionary overhead in hot path

---

## Problems Encountered & Solutions

### Problem 1: CS0104 Ambiguous TerrainData
**Symptom:** `'TerrainData' is an ambiguous reference between 'Core.Registries.TerrainData' and 'UnityEngine.TerrainData'`

**Solution:** Using alias
```csharp
using TerrainData = Core.Registries.TerrainData;
```

### Problem 2: Speed Change Momentum
**Symptom:** Setting speed from 100x to 1x still ran fast for several seconds

**Root Cause:** Accumulator had built-up value from high speed, drained at 4 ticks/frame

**Solution:** Clamp accumulator when speed decreases

---

## Architecture Impact

### Pattern Reinforced: ENGINE-GAME Separation
- ENGINE: `TerrainMovementCostCalculator` - mechanism only
- GAME: `LandUnitCostCalculator` - policy wrapper
- Future: `NavalUnitCostCalculator`, `AmphibiousCostCalculator`

### New Files
- `Core/Systems/Pathfinding/TerrainMovementCostCalculator.cs`
- `StarterKit/Systems/LandUnitCostCalculator.cs`
- `StarterKit/MapModes/TerrainCostMapMode.cs`

---

## Code Quality Notes

### Localization
Added: `UI_MAP_TERRAIN:0 "Terrain Cost"` to `ui_l_english.yml`

---

## Next Session

### Potential Next Steps
1. Naval unit pathfinding (water-only)
2. Amphibious units
3. Road/infrastructure speed bonuses
4. Distance-based heuristics for A* (requires province coordinates)

---

## Quick Reference for Future Claude

**Key Implementations:**
- Terrain cost calculator: `Core/Systems/Pathfinding/TerrainMovementCostCalculator.cs`
- Land unit wrapper: `StarterKit/Systems/LandUnitCostCalculator.cs`
- Map mode: `StarterKit/MapModes/TerrainCostMapMode.cs`
- Wiring: `StarterKit/Initializer.cs:619-633`

**Pattern:**
```
ENGINE (mechanism)     GAME (policy)
TerrainMovementCostCalculator → LandUnitCostCalculator
    ↓                              ↓
GetMovementCost()              CanTraverse() blocks water
IsProvinceWater()              Uses for policy decision
```

**Gotchas:**
- Use `TerrainData = Core.Registries.TerrainData` alias to avoid Unity conflict
- TerrainCostMapMode uses ShaderModeID 3 (after FarmDensity which is 2)

---

## Files Changed Summary

| File | Changes |
|------|---------|
| `Core/Registries/GameRegistries.cs` | Removed DefenceBonus, SupplyLimit; added CustomData |
| `Core/Loaders/TerrainLoader.cs` | Simplified to ENGINE fields only |
| `Core/Systems/Pathfinding/TerrainMovementCostCalculator.cs` | **NEW** - terrain cost lookups |
| `Core/Systems/TimeManager.cs` | Fixed speed change accumulator bug |
| `StarterKit/Systems/LandUnitCostCalculator.cs` | **NEW** - blocks water for land units |
| `StarterKit/MapModes/TerrainCostMapMode.cs` | **NEW** - movement cost visualization |
| `StarterKit/UI/ToolbarUI.cs` | Map mode cycling (3 modes) |
| `StarterKit/Initializer.cs` | Wire up terrain calculators, register map mode |
| `Template-Data/map/terrain.json5` | Removed game-specific fields |
| `Template-Data/localisation/english/ui_l_english.yml` | Added UI_MAP_TERRAIN |

---

*Session Duration: ~45 minutes*
