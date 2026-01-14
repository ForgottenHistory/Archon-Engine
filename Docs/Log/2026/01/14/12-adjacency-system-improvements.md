# AdjacencySystem Improvements
**Date**: 2026-01-14
**Session**: 12
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Enhance AdjacencySystem with filtered queries, flood fill, border detection, and bridge detection

**Success Criteria:**
- Queryable statistics struct
- Predicate-based filtered queries
- Connected region flood fill
- Shared border detection between countries
- Bridge/choke point detection

---

## What We Did

### 1. AdjacencyStats Struct

Queryable statistics replacing string-based `GetStatistics()`.

**Fields:**
- `ProvinceCount`, `TotalAdjacencyPairs`
- `MinNeighbors`, `MaxNeighbors`, `TotalNeighborEntries`
- `AverageNeighbors` (computed property)
- `GetSummary()` for formatted output

### 2. Filtered Neighbor Queries

**Method:** `GetNeighborsWhere(provinceId, predicate)`

```csharp
// Example: Get enemy neighbors
adjacencySystem.GetNeighborsWhere(provinceId,
    id => provinceSystem.GetOwner(id) == enemyCountry);
```

Both allocating and buffer versions provided.

### 3. Connected Region (Flood Fill)

**Method:** `GetConnectedRegion(startProvince, predicate)`

BFS flood fill to find all connected provinces matching a predicate.

```csharp
// Example: Get all connected provinces owned by country
var region = adjacencySystem.GetConnectedRegion(capital,
    id => provinceSystem.GetOwner(id) == countryId);
```

### 4. Shared Border Detection

**Method:** `GetSharedBorderProvinces(ownedProvinces, foreignProvinces)`

Find provinces where two countries share a border.

```csharp
// Example: Find French provinces bordering Germany
var borderProvinces = adjacencySystem.GetSharedBorderProvinces(
    frenchProvinces, germanProvincesSet);
```

### 5. Bridge Detection

**Methods:**
- `IsBridgeProvince(province, regionPredicate)` - check single province
- `FindBridgeProvinces(regionProvinces, predicate)` - find all bridges

Bridge provinces are strategic choke points - removing them would disconnect the region.

```csharp
// Example: Find critical provinces in country's territory
var chokePoints = new List<ushort>();
adjacencySystem.FindBridgeProvinces(countryProvinces,
    id => provinceSystem.GetOwner(id) == countryId, chokePoints);
```

---

## Files Changed

| File | Changes |
|------|---------|
| `Core/Systems/AdjacencySystem.cs` | Added AdjacencyStats, filtered queries, flood fill, border/bridge detection |

---

## Quick Reference for Future Claude

**Filtered Neighbors:**
```csharp
var enemies = adjacencySystem.GetNeighborsWhere(provinceId,
    id => gameState.Provinces.GetOwner(id) != myCountry);
```

**Connected Region:**
```csharp
var myTerritory = adjacencySystem.GetConnectedRegion(capital,
    id => gameState.Provinces.GetOwner(id) == myCountry);
```

**Border Provinces:**
```csharp
var borderWithEnemy = adjacencySystem.GetSharedBorderProvinces(
    myProvinces, enemyProvincesSet);
```

**Strategic Choke Points:**
```csharp
bool isCritical = adjacencySystem.IsBridgeProvince(province,
    id => gameState.Provinces.GetOwner(id) == myCountry);
```

**Statistics:**
```csharp
var stats = adjacencySystem.GetStats();
Debug.Log($"Avg neighbors: {stats.AverageNeighbors}");
```

---

## Links & References

### Related Sessions
- [Previous: AISystem Improvements](11-ai-system-improvements.md)
- [PathfindingSystem Improvements](10-pathfinding-system-improvements.md)

### Code References
- AdjacencyStats: `Core/Systems/AdjacencySystem.cs:10-30`
- GetNeighborsWhere: `Core/Systems/AdjacencySystem.cs:246-269`
- GetConnectedRegion: `Core/Systems/AdjacencySystem.cs:283-327`
- GetSharedBorderProvinces: `Core/Systems/AdjacencySystem.cs:339-370`
- IsBridgeProvince: `Core/Systems/AdjacencySystem.cs:386-419`

### Planning
- [CORE Improvements Roadmap](../../Planning/core-namespace-improvements.md)

---

*AdjacencySystem enhanced with AdjacencyStats struct, predicate-based filtering (GetNeighborsWhere), BFS flood fill (GetConnectedRegion), shared border detection (GetSharedBorderProvinces), and bridge/choke point detection (IsBridgeProvince, FindBridgeProvinces).*
