# Fluent Validation & Query Builders in StarterKit
**Date**: 2026-01-17
**Session**: 07
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Update all StarterKit commands to use fluent validation pattern
- Showcase ProvinceQueryBuilder in AISystem

**Secondary Objectives:**
- Demonstrate ENGINE vs GAME layer separation for both validation and queries

**Success Criteria:**
- All 5 commands use fluent validation
- AISystem uses ProvinceQueryBuilder with GAME-layer post-filtering

---

## Context & Background

**Previous Work:**
- See: [06-image-loading-validation.md](06-image-loading-validation.md)
- Created `StarterKitValidationExtensions.cs` with GAME-layer validators
- Updated `CreateUnitCommand` and `MoveUnitCommand` with fluent validation

**Current State:**
- 3 commands still using manual validation
- AISystem doing manual province filtering with nested loops

**Why Now:**
- Complete the validation pattern across all commands
- Query builders existed in ENGINE but weren't showcased

---

## What We Did

### 1. Updated Remaining Commands with Fluent Validation
**Files Changed:**
- `StarterKit/Commands/AddGoldCommand.cs`
- `StarterKit/Commands/ConstructBuildingCommand.cs`
- `StarterKit/Commands/DisbandUnitCommand.cs`

**AddGoldCommand:**
```csharp
public override bool Validate(GameState gameState)
{
    // If removing gold, validate sufficient funds
    if (Amount < 0)
    {
        return Core.Validation.Validate.For(gameState)
            .HasGold(-Amount)
            .Result(out validationError);
    }
    // Adding gold always valid
    return Initializer.Instance?.EconomySystem != null;
}
```

**ConstructBuildingCommand:**
```csharp
public override bool Validate(GameState gameState)
{
    return Core.Validation.Validate.For(gameState)
        .Province(ProvinceId)
        .BuildingTypeExists(BuildingTypeId)
        .CanConstructBuilding(ProvinceId, BuildingTypeId)
        .Result(out validationError);
}
```

**DisbandUnitCommand:**
```csharp
public override bool Validate(GameState gameState)
{
    return Core.Validation.Validate.For(gameState)
        .UnitExists(UnitId)
        .Result(out validationError);
}
```

### 2. Refactored AISystem to Use ProvinceQueryBuilder
**Files Changed:** `StarterKit/Systems/AISystem.cs`

**TryColonize - Before:** ~40 lines of nested loops
**TryColonize - After:**
```csharp
// ENGINE query: provinces adjacent to us that are unowned
using var query = new ProvinceQueryBuilder(provinceSystem, gameState.Adjacencies);
using var candidates = query
    .BorderingCountry(countryId)  // Adjacent to our provinces
    .IsUnowned()                   // Not owned by anyone
    .Execute(Allocator.Temp);

// GAME-layer filter: only ownable terrain
var colonizeCandidates = new List<ushort>();
for (int i = 0; i < candidates.Length; i++)
{
    ushort provinceId = candidates[i];
    ushort terrainType = provinceSystem.GetProvinceTerrain(provinceId);
    if (terrainLookup == null || terrainLookup.IsTerrainOwnable(terrainType))
        colonizeCandidates.Add(provinceId);
}
```

**TryBuildFarm:**
```csharp
using var query = new ProvinceQueryBuilder(provinceSystem, gameState.Adjacencies);
using var ownedProvinces = query
    .OwnedBy(countryId)
    .Execute(Allocator.Temp);

// GAME-layer filter: provinces with farm capacity
```

### 3. Added Unity.Collections Reference
**Files Changed:** `StarterKit/StarterKit.asmdef`

Added `Unity.Collections` to assembly references for `NativeList<>` support.

---

## Decisions Made

### Decision 1: No Custom Query Extensions
**Context:** Should we add `.IsOwnable()` to ProvinceQueryBuilder like we did with validation?

**Options Considered:**
1. Add generic `.Where(Func<ushort, bool>)` predicate - allocates delegate
2. Add `.IsOwnable()` directly - breaks layer separation (Core can't import Map)
3. Post-filter results in GAME layer - zero allocations, clean separation

**Decision:** Option 3 - Post-filter in GAME layer

**Rationale:**
- Architecture says "zero allocations in hot paths"
- While AI monthly tick isn't hot, the pattern should be safe everywhere
- Clean separation: ENGINE filters what ENGINE knows, GAME filters GAME concepts
- Still significant code reduction (40 lines → 15 lines)

**Trade-offs:** Slightly less fluent API, but architecturally correct.

---

## What Worked ✅

1. **Extension Method Pattern for Validation**
   - Clean ENGINE vs GAME separation
   - Easy to add new validators
   - Chainable, short-circuits on first failure

2. **Query Builder + Post-Filter Pattern**
   - ENGINE handles ownership, adjacency, terrain IDs
   - GAME handles domain concepts (ownable, building capacity)
   - Significant code reduction while maintaining separation

---

## Problems Encountered & Solutions

### Problem 1: Missing Assembly Reference
**Symptom:** `CS0012: The type 'NativeList<>' is defined in an assembly that is not referenced`

**Root Cause:** StarterKit.asmdef didn't reference Unity.Collections

**Solution:** Added `"Unity.Collections"` to references array in StarterKit.asmdef

---

## Architecture Impact

### Pattern Demonstrated
**ENGINE + GAME Query Pattern:**
1. Use `ProvinceQueryBuilder` for ENGINE-known filters (ownership, adjacency)
2. Post-filter results for GAME-specific concepts (ownable terrain, building limits)
3. Zero allocations in the query itself

### Files Summary

| File | Changes |
|------|---------|
| `StarterKit/Commands/AddGoldCommand.cs` | Fluent validation for gold removal |
| `StarterKit/Commands/ConstructBuildingCommand.cs` | Fluent validation chain |
| `StarterKit/Commands/DisbandUnitCommand.cs` | Fluent validation |
| `StarterKit/Systems/AISystem.cs` | ProvinceQueryBuilder in TryColonize/TryBuildFarm |
| `StarterKit/StarterKit.asmdef` | Added Unity.Collections reference |

---

## Next Session

### StarterKit Feature Gaps (from previous analysis)
1. **War indicators** on province tooltips
2. **Notification toasts** for diplomatic events
3. **Advanced AI goals** (Expansion, Defense)
4. **Visual style selector** UI

---

## Quick Reference for Future Claude

**Fluent Validation Pattern:**
```csharp
return Core.Validation.Validate.For(gameState)
    .Province(provinceId)           // ENGINE
    .UnitExists(unitId)             // GAME extension
    .Result(out validationError);
```

**Query Builder + Post-Filter Pattern:**
```csharp
// ENGINE query
using var candidates = new ProvinceQueryBuilder(provinceSystem, adjacencySystem)
    .BorderingCountry(countryId)
    .IsUnowned()
    .Execute(Allocator.Temp);

// GAME-layer filter
for (int i = 0; i < candidates.Length; i++)
    if (IsOwnableTerrain(candidates[i]))
        results.Add(candidates[i]);
```

**Gotchas:**
- Use `Core.Validation.Validate.For()` not `Validate.For()` in commands (name collision)
- StarterKit needs `Unity.Collections` in asmdef for NativeList
- ProvinceQueryBuilder is a struct - must dispose after use

---

*Session Duration: ~30 minutes*
