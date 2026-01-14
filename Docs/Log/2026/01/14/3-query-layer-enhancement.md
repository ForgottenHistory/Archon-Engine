# Query Layer Enhancement
**Date**: 2026-01-14
**Session**: 3
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Implement missing relationship and distance queries in Core layer
- Fill gaps identified in previous session: `SharesBorder()` stubbed, no `GetBorderingCountries()`, distance queries AI-only

**Success Criteria:**
- `CountryQueries.SharesBorder()` implemented (was returning false)
- `GetBorderingCountries()` methods added
- Distance queries available in `ProvinceQueries`
- Connected region queries for landmass detection

---

## Context & Background

**Previous Work:**
- See: [2-result-pattern-standardization.md](2-result-pattern-standardization.md)
- Query layer gap identified during Core namespace analysis

**Current State Before:**
- `CountryQueries.SharesBorder()` was STUBBED (always returned false)
- `GetBorderingCountries()` didn't exist - AI goals manually looped
- Distance queries only available in `AIDistanceCalculator` (AI-specific)
- Connected region detection duplicated in `MapLabelManager`

**Why Now:**
- AI goals like `ExpandTerritoryGoal` needed proper border detection
- Distance queries useful for influence calculations, pathfinding prep
- Landmass detection needed for multi-territory country handling

---

## What We Did

### 1. Country Relationship Queries

**File:** `Core/Queries/CountryQueries.cs`

Added AdjacencySystem dependency and implemented:

```csharp
public bool SharesBorder(ushort countryId1, ushort countryId2)
// Walks provinces of country1, checks if any neighbor owned by country2
// Early exit on first match, O(P × N) where P=provinces, N=~6 neighbors

public NativeList<ushort> GetBorderingCountries(ushort countryId, Allocator allocator)
// Returns all unique neighboring countries

public void GetBorderingCountries(ushort countryId, NativeList<ushort> resultBuffer)
// Zero-allocation variant for hot paths

public int GetBorderingCountryCount(ushort countryId)
// Convenience method
```

**Key Implementation Detail:**
- Uses reusable `neighborBuffer` (NativeList) to avoid per-query allocation
- Uses `NativeHashSet` for deduplication of neighboring countries

### 2. GraphDistanceCalculator

**File:** `Core/Graph/GraphDistanceCalculator.cs` (NEW)

Extracted general-purpose BFS from `AIDistanceCalculator`:

```csharp
public class GraphDistanceCalculator : IDisposable
{
    public void Initialize(int maxProvinceId, int maxCountryId = 0);
    public void CalculateDistancesFromProvince(ushort source, NativeAdjacencyData adjacency);
    public void CalculateDistancesFromCountry(ushort countryId, ProvinceSystem, AdjacencySystem);
    public byte GetProvinceDistance(ushort provinceId);
    public byte GetCountryDistance(ushort countryId);
    public byte GetDistanceBetween(ushort province1, ushort province2, AdjacencySystem);
    public NativeList<ushort> GetProvincesWithinDistance(byte maxDist, Allocator);
}
```

- Burst-compiled via `BFSDistanceJob`
- Optionally tracks country distances (for AI tiering)
- Pre-allocated buffers for zero gameplay allocations

### 3. Province Distance Queries

**File:** `Core/Queries/ProvinceQueries.cs`

Added AdjacencySystem dependency and implemented:

```csharp
public byte GetDistanceBetween(ushort province1, ushort province2)
// BFS hop count between provinces

public NativeList<ushort> GetProvincesWithinDistance(ushort source, byte maxDist, Allocator)
// All provinces within N hops

public void CalculateDistancesFromCountry(ushort countryId)
// Pre-calculate distances for batch queries

public byte GetCachedProvinceDistance(ushort provinceId)
// Read from last calculation
```

### 4. Connected Region Queries

**File:** `Core/Queries/ProvinceQueries.cs`

Flood-fill based queries:

```csharp
public NativeList<ushort> GetConnectedProvincesOfSameOwner(ushort source, Allocator)
// Flood-fill to find contiguous territory

public NativeList<ushort> GetConnectedProvincesWithOwner(ushort source, ushort owner, Allocator)
// Flood-fill with specific owner filter

public NativeList<NativeList<ushort>> GetConnectedLandmasses(ushort countryId, Allocator)
// Returns separate landmass groups (e.g., Britain + Caribbean for England)

public int GetLandmassCount(ushort countryId)
// Count disconnected territories
```

### 5. GameState Wiring

**File:** `Core/GameState.cs:191-192`

```csharp
ProvinceQueries = new ProvinceQueries(Provinces, Countries, Adjacencies);
CountryQueries = new CountryQueries(Countries, Provinces, Adjacencies);
```

---

## Problems Encountered & Solutions

### Problem 1: ProvinceSystem.MaxProvinceId Doesn't Exist

**Symptom:** `CS1061: 'ProvinceSystem' does not contain a definition for 'MaxProvinceId'`

**Root Cause:** Assumed property existed; ProvinceSystem has `Capacity` instead

**Solution:** Changed to `provinceSystem.Capacity`

```csharp
int maxProvinceId = provinceSystem.Capacity > 0 ? provinceSystem.Capacity : 20000;
```

---

## Architecture Impact

### Documentation Updates
- [x] Updated `Core/FILE_REGISTRY.md` - Added Graph/ namespace, updated Queries/ descriptions

### New Patterns
**Zero-Allocation Query Pattern:**
- Pre-allocate reusable buffers (e.g., `neighborBuffer`)
- Provide both allocating and buffer-filling variants
- Use `Allocator.Temp` for internal temporaries

---

## Files Changed

| File | Changes |
|------|---------|
| `Core/Queries/CountryQueries.cs` | +AdjacencySystem, +SharesBorder, +GetBorderingCountries |
| `Core/Queries/ProvinceQueries.cs` | +AdjacencySystem, +distance queries, +connected region queries, +IDisposable |
| `Core/Graph/GraphDistanceCalculator.cs` | NEW - Burst BFS calculator |
| `Core/GameState.cs` | Pass Adjacencies to query constructors |
| `Core/FILE_REGISTRY.md` | Document Graph/ namespace |

---

## Quick Reference for Future Claude

**New Query Capabilities:**

```csharp
// Country relationships
gameState.CountryQueries.SharesBorder(country1, country2)
gameState.CountryQueries.GetBorderingCountries(countryId, Allocator.Temp)

// Province distances
gameState.ProvinceQueries.GetDistanceBetween(prov1, prov2)
gameState.ProvinceQueries.GetProvincesWithinDistance(source, maxDist, Allocator.Temp)

// Connected regions
gameState.ProvinceQueries.GetConnectedLandmasses(countryId, Allocator.Temp)
gameState.ProvinceQueries.GetLandmassCount(countryId)
```

**Key Files:**
- Relationship queries: `Core/Queries/CountryQueries.cs:193-324`
- Distance queries: `Core/Queries/ProvinceQueries.cs:367-470`
- Connected regions: `Core/Queries/ProvinceQueries.cs:473-629`
- BFS calculator: `Core/Graph/GraphDistanceCalculator.cs`

---

## Session Statistics

**Files Changed:** 5
**New Files:** 1 (`GraphDistanceCalculator.cs`)
**Methods Added:** 12
**Tests Added:** 0 (deferred)

---

## Links & References

### Related Sessions
- [Previous: Result Pattern Standardization](2-result-pattern-standardization.md)

### Code References
- SharesBorder: `Core/Queries/CountryQueries.cs:200-234`
- GetBorderingCountries: `Core/Queries/CountryQueries.cs:241-297`
- GraphDistanceCalculator: `Core/Graph/GraphDistanceCalculator.cs`
- BFSDistanceJob: `Core/Graph/GraphDistanceCalculator.cs:244-313`

---

*Query layer now provides relationship, distance, and connected region queries with zero-allocation variants for hot paths.*
