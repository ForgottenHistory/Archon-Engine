# PathfindingSystem Improvements
**Date**: 2026-01-14
**Session**: 10
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Enhance PathfindingSystem with cost calculator interface, path options, and LRU caching

**Success Criteria:**
- IMovementCostCalculator interface for GAME layer customization
- PathOptions for forbidden/avoid provinces
- LRU PathCache for repeated requests
- Backward compatible API

---

## What We Did

### 1. IMovementCostCalculator Interface

**File:** `Core/Systems/Pathfinding/IMovementCostCalculator.cs`

Allows GAME layer to provide custom movement costs based on terrain, ownership, unit type.

**Key Components:**
- `GetMovementCost(from, to, context)` - Cost between adjacent provinces
- `CanTraverse(provinceId, context)` - Hard block check
- `GetHeuristic(from, goal)` - A* heuristic (must be admissible)
- `PathContext` struct - Unit owner, type, flags
- `PathContextFlags` - IgnoreEnemyTerritory, PreferRoads, AvoidCombat, etc.
- `UniformCostCalculator` - Default implementation (all costs = 1)

### 2. PathOptions & PathResult

**File:** `Core/Systems/Pathfinding/PathOptions.cs`

**PathOptions:**
- `CostCalculator` - Custom cost calculator
- `Context` - Unit-specific pathfinding context
- `ForbiddenProvinces` - Hard block (path fails if no alternative)
- `AvoidProvinces` - Soft block (10x penalty by default)
- `MaxPathLength` - Limit path length
- `UseCache` - Enable/disable caching

**PathResult:**
- `Path`, `TotalCost`, `NodesExplored`, `WasCached`
- `PathStatus` enum: Found, NotFound, InvalidStart, InvalidGoal, TooLong, NotInitialized, Timeout

### 3. PathCache (LRU)

**File:** `Core/Systems/Pathfinding/PathCache.cs`

LRU cache for pathfinding results:
- `TryGet()` / `Add()` with automatic eviction
- `InvalidateProvince()` - Clear paths through changed province
- `InvalidateEndpoint()` - Clear paths to/from province
- Statistics: HitCount, MissCount, HitRate

### 4. PathfindingSystem Updates

**File:** `Core/Systems/PathfindingSystem.cs`

**New API:**
```csharp
PathResult FindPathWithOptions(start, goal, PathOptions options);
PathResult FindPathAvoiding(start, goal, HashSet<ushort> avoidProvinces);
PathResult FindPathWithForbidden(start, goal, HashSet<ushort> forbiddenProvinces);
bool PathExists(start, goal);
int GetDistance(start, goal);
void SetDefaultCostCalculator(IMovementCostCalculator calculator);
```

**Dual Implementation:**
- Burst path for uniform costs (fast, no managed code)
- Managed path for custom costs/options (flexible)

**Statistics:**
- TotalSearches, CacheHits, CacheHitRate
- `GetStats()` for debugging

---

## Architecture Discussion

**Question Raised:** Is PathfindingSystem too province-specific for Core?

**Analysis:**
- PathfindingSystem is an **optional utility** - no Core system depends on it
- GAME layer chooses to use it or not
- A space game could implement its own hyperlane pathfinder
- Current design: mechanism in Core, policy in GAME

**Decision:** Keep in Core as optional utility. It's a leaf dependency, not a forced requirement.

---

## Files Changed

| File | Changes |
|------|---------|
| `Core/Systems/Pathfinding/IMovementCostCalculator.cs` | NEW - interface + PathContext + UniformCostCalculator |
| `Core/Systems/Pathfinding/PathOptions.cs` | NEW - PathOptions + PathResult + PathStatus |
| `Core/Systems/Pathfinding/PathCache.cs` | NEW - LRU cache |
| `Core/Systems/PathfindingSystem.cs` | Major update - options, caching, statistics |

---

## Quick Reference for Future Claude

**Usage - Custom Costs:**
```csharp
class TerrainCostCalculator : IMovementCostCalculator
{
    public FixedPoint64 GetMovementCost(ushort from, ushort to, PathContext ctx)
    {
        var terrain = GetTerrain(to);
        return terrain == Terrain.Mountain ? FixedPoint64.FromInt(3) : FixedPoint64.One;
    }
}
pathfindingSystem.SetDefaultCostCalculator(new TerrainCostCalculator());
```

**Usage - Avoid Enemies:**
```csharp
var enemyProvinces = new HashSet<ushort> { 101, 102, 103 };
var result = pathfindingSystem.FindPathAvoiding(start, goal, enemyProvinces);
```

**Usage - Forbidden Zones:**
```csharp
var impassable = new HashSet<ushort> { 200 }; // ocean
var result = pathfindingSystem.FindPathWithForbidden(start, goal, impassable);
```

---

## Links & References

### Related Sessions
- [Previous: CORE Namespace Improvements](9-core-namespace-improvements.md)

### Code References
- IMovementCostCalculator: `Core/Systems/Pathfinding/IMovementCostCalculator.cs`
- PathOptions: `Core/Systems/Pathfinding/PathOptions.cs`
- PathCache: `Core/Systems/Pathfinding/PathCache.cs`
- PathfindingSystem: `Core/Systems/PathfindingSystem.cs`

### Planning
- [CORE Improvements Roadmap](../../Planning/core-namespace-improvements.md)

---

*PathfindingSystem enhanced with IMovementCostCalculator for custom costs, PathOptions for forbidden/avoid zones, and LRU caching. Remains optional utility in Core - no forced dependencies.*
