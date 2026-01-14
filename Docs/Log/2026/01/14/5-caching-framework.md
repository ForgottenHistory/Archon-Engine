# Caching Framework & Border Query
**Date**: 2026-01-14
**Session**: 5
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Add GetProvincesBorderingCountry() to ProvinceQueries
- Implement generic caching framework for expensive calculations

**Success Criteria:**
- Query provinces adjacent to but not owned by a country
- FrameCache for per-frame caching, TimedCache for time-based caching

---

## Context & Background

**Previous Work:**
- See: [4-query-builder-fluent-api.md](4-query-builder-fluent-api.md)
- QueryBuilder fluent API complete, facade consistency fixed

**Why Now:**
- "All provinces bordering country X" was missing from query layer
- Caching pattern repeated across codebase needed standardization

---

## What We Did

### 1. GetProvincesBorderingCountry()

**File:** `Core/Queries/ProvinceQueries.cs`

Two overloads for finding provinces adjacent to a country's borders:

```csharp
// All provinces bordering country X (owned by anyone else)
public NativeList<ushort> GetProvincesBorderingCountry(
    ushort countryId,
    Allocator allocator = Allocator.TempJob)

// Provinces bordering country X that are owned by specific country
public NativeList<ushort> GetProvincesBorderingCountry(
    ushort countryId,
    ushort filterOwnerId,
    Allocator allocator = Allocator.TempJob)
```

**Use Cases:**
- AI expansion: "What provinces can I attack?"
- Diplomacy: "What provinces of mine border enemy?"
- Map visualization: Border highlighting

### 2. FrameCache (Per-Frame Caching)

**File:** `Core/Common/FrameCache.cs` (NEW)

Auto-clears on frame change via `Time.frameCount`:

```csharp
private FrameCache<ushort, int> devCache = new();

public int GetTotalDevelopment(ushort countryId)
{
    return devCache.GetOrCompute(countryId, () => CalculateDevelopment(countryId));
}
```

**Classes:**
- `FrameCache<TKey, TValue>` - Keyed cache, multiple values
- `FrameCacheValue<TValue>` - Single value variant

**When to Use:** Same query called multiple times within ONE frame (UI, tooltips)

### 3. TimedCache (Time-Based Caching)

**File:** `Core/Common/TimedCache.cs` (NEW)

Expires after configurable lifetime via `Time.time`:

```csharp
private TimedCache<ushort, int> borderCache = new(lifetime: 1.0f);

public int GetBorderingCountryCount(ushort countryId)
{
    return borderCache.GetOrCompute(countryId, () => CountBorders(countryId));
}
```

**Classes:**
- `TimedCache<TKey, TValue>` - Keyed cache with expiration
- `TimedCacheValue<TValue>` - Single value variant

**When to Use:** Data changes infrequently, recalculating every frame wasteful

### Cache Comparison

| Cache Type | Clears | Use Case |
|------------|--------|----------|
| FrameCache | Every frame | Multiple queries same frame |
| TimedCache | After N seconds | Infrequently changing data |

---

## Files Changed

| File | Changes |
|------|---------|
| `Core/Queries/ProvinceQueries.cs` | +GetProvincesBorderingCountry (2 overloads) |
| `Core/Common/FrameCache.cs` | NEW - Frame-coherent caching |
| `Core/Common/TimedCache.cs` | NEW - Time-based caching |
| `Core/FILE_REGISTRY.md` | Document cache classes |

---

## Quick Reference for Future Claude

**Border Query:**
```csharp
// All provinces I can attack
using var targets = provinceQueries.GetProvincesBorderingCountry(enemyId);

// My provinces that border enemy
using var myBorder = provinceQueries.GetProvincesBorderingCountry(enemyId, myCountryId);
```

**Caching Patterns:**
```csharp
// Per-frame (UI queries)
private FrameCache<ushort, int> cache = new();
return cache.GetOrCompute(key, () => ExpensiveCalc(key));

// Time-based (infrequent changes)
private TimedCache<ushort, int> cache = new(lifetime: 1.0f);
return cache.GetOrCompute(key, () => ExpensiveCalc(key));

// Single value variants
private FrameCacheValue<Stats> statsCache = new();
private TimedCacheValue<Stats> statsCache = new(lifetime: 5.0f);
```

---

## Session Statistics

**Files Changed:** 4
**New Files:** 2
**Methods Added:** 2 (GetProvincesBorderingCountry overloads)
**Classes Added:** 4 (FrameCache, FrameCacheValue, TimedCache, TimedCacheValue)

---

## Links & References

### Related Sessions
- [Previous: QueryBuilder Fluent API](4-query-builder-fluent-api.md)
- [Query Layer Enhancement](3-query-layer-enhancement.md)

### Code References
- ProvinceQueries: `Core/Queries/ProvinceQueries.cs`
- FrameCache: `Core/Common/FrameCache.cs`
- TimedCache: `Core/Common/TimedCache.cs`

---

*Generic caching framework standardizes expensive calculation patterns. GetProvincesBorderingCountry completes relationship query coverage.*
