# QueryBuilder Fluent API
**Date**: 2026-01-14
**Session**: 4
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Implement fluent QueryBuilder API for complex filtered queries
- Add missing facade methods for system consistency

**Success Criteria:**
- `Query.Provinces(gameState).OwnedBy(x).IsLand().Execute()` works
- `Query.Countries(gameState).WithMinProvinces(5).Execute()` works
- All DataManager methods properly exposed on System facades

---

## Context & Background

**Previous Work:**
- See: [3-query-layer-enhancement.md](3-query-layer-enhancement.md)
- Query layer gaps identified: no fluent API, manual loops required

**Why Now:**
- Complex queries like "all land provinces owned by X bordering Y" required manual loops
- Inconsistent facade exposure discovered during implementation

---

## What We Did

### 1. ProvinceQueryBuilder

**File:** `Core/Queries/ProvinceQueryBuilder.cs` (NEW)

Fluent province filtering with lazy evaluation:

```csharp
using var results = Query.Provinces(gameState)
    .OwnedBy(countryId)
    .IsLand()
    .WithTerrain(terrainType)
    .BorderingCountry(enemyId)
    .WithinDistance(capital, 5)
    .Execute(Allocator.Temp);
```

**Filters:**
- `OwnedBy(countryId)`, `ControlledBy(countryId)`
- `WithTerrain(terrainType)`, `IsLand()`, `IsOcean()`
- `IsOwned()`, `IsUnowned()`
- `AdjacentTo(provinceId)`
- `BorderingCountry(countryId)` - provinces adjacent to but not owned by
- `WithinDistance(source, maxHops)`

**Terminal Operations:** `Execute()`, `Count()`, `Any()`, `FirstOrDefault()`

### 2. CountryQueryBuilder

**File:** `Core/Queries/CountryQueryBuilder.cs` (NEW)

```csharp
using var results = Query.Countries(gameState)
    .WithMinProvinces(5)
    .BorderingCountry(playerId)
    .HasProvinces()
    .Execute(Allocator.Temp);
```

**Filters:**
- `WithMinProvinces(n)`, `WithMaxProvinces(n)`, `WithProvinceCount(min, max)`
- `BorderingCountry(countryId)`
- `HasProvinces()`, `HasNoProvinces()`
- `WithGraphicalCulture(cultureId)`

### 3. Query Entry Point

**File:** `Core/Queries/Query.cs` (NEW)

```csharp
Query.Provinces(gameState)  // Returns ProvinceQueryBuilder
Query.Countries(gameState)  // Returns CountryQueryBuilder
```

### 4. Facade Consistency Fixes

**ProvinceDataManager → ProvinceSystem:**
- Added `GetProvinceController()` - was missing, had to use `GetProvinceState().controllerID`
- Added `SetProvinceState()` - bulk update all fields

**CountryDataManager → CountrySystem:**
- Added `HasTag()` - check if country tag exists

---

## Problems Encountered & Solutions

### Problem 1: GetProvinceController Missing

**Symptom:** `CS1061: 'ProvinceSystem' does not contain a definition for 'GetProvinceController'`

**Root Cause:** Facade didn't expose the method, only `GetProvinceOwner` existed

**Solution:** Added matching method to both DataManager and System facade

### Problem 2: Facade/Component Inconsistency

**Discovery:** While fixing GetProvinceController, audited both systems for consistency

**Found Missing:**
- `ProvinceSystem.SetProvinceState()`
- `CountrySystem.HasTag()`

**Solution:** Added all missing facade methods

---

## Architecture Impact

### Pattern Reinforced: Facade Consistency
When adding methods to internal components (DataManager), always check if facade (System) needs updating.

**Audit checklist:**
```
DataManager has method? → System facade exposes it?
Exception: Internal-only methods (dirty tracking for rendering)
```

---

## Files Changed

| File | Changes |
|------|---------|
| `Core/Queries/Query.cs` | NEW - Static entry point |
| `Core/Queries/ProvinceQueryBuilder.cs` | NEW - Fluent province filtering |
| `Core/Queries/CountryQueryBuilder.cs` | NEW - Fluent country filtering |
| `Core/Systems/Province/ProvinceDataManager.cs` | +GetProvinceController |
| `Core/Systems/ProvinceSystem.cs` | +GetProvinceController, +SetProvinceState |
| `Core/Systems/CountrySystem.cs` | +HasTag |
| `Core/FILE_REGISTRY.md` | Document QueryBuilder classes |

---

## Quick Reference for Future Claude

**Fluent Query API:**
```csharp
// Province queries
using var provinces = Query.Provinces(gameState)
    .OwnedBy(countryId)
    .IsLand()
    .BorderingCountry(enemyId)
    .Execute(Allocator.Temp);

// Country queries
using var countries = Query.Countries(gameState)
    .WithMinProvinces(5)
    .BorderingCountry(playerId)
    .Execute(Allocator.Temp);

// Terminal ops
int count = Query.Provinces(gameState).OwnedBy(id).Count();
bool any = Query.Provinces(gameState).IsUnowned().Any();
ushort first = Query.Countries(gameState).HasProvinces().FirstOrDefault();
```

**Key Files:**
- Entry point: `Core/Queries/Query.cs`
- Province builder: `Core/Queries/ProvinceQueryBuilder.cs`
- Country builder: `Core/Queries/CountryQueryBuilder.cs`

---

## Session Statistics

**Files Changed:** 7
**New Files:** 3
**Methods Added:** 15+ (filters + terminals)
**Facade Methods Added:** 4

---

## Links & References

### Related Sessions
- [Previous: Query Layer Enhancement](3-query-layer-enhancement.md)

### Code References
- ProvinceQueryBuilder: `Core/Queries/ProvinceQueryBuilder.cs`
- CountryQueryBuilder: `Core/Queries/CountryQueryBuilder.cs`
- Query entry point: `Core/Queries/Query.cs`

---

*Fluent QueryBuilder API enables complex filtered queries without manual loops. Facade consistency audit completed.*
