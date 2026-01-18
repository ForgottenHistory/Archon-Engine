# Pathfinding System

The pathfinding system provides A* and Dijkstra pathfinding for province-based maps with customizable movement costs and LRU caching.

## Architecture

```
PathfindingSystem
├── IMovementCostCalculator  - Cost calculation interface
├── PathOptions              - Request configuration
├── PathResult               - Result with metadata
└── PathCache                - LRU cache for results

Movement Cost Hierarchy:
ENGINE: TerrainMovementCostCalculator (terrain costs only)
GAME: Custom calculators (ownership, unit type, supply)
```

**Key Principles:**
- ENGINE provides mechanism, GAME provides policy
- FixedPoint64 for deterministic costs
- Customizable cost calculators
- LRU caching for repeated queries

## Basic Usage

### Simple Path Request

```csharp
var pathResult = pathfindingSystem.FindPath(
    startProvinceId,
    goalProvinceId,
    PathOptions.Default
);

if (pathResult.Success)
{
    foreach (ushort provinceId in pathResult.Path)
    {
        // Follow path
    }
}
```

### With Cost Calculator

```csharp
var options = PathOptions.WithCostCalculator(terrainCostCalculator);
var pathResult = pathfindingSystem.FindPath(startId, goalId, options);
```

### For Specific Unit

```csharp
var options = PathOptions.ForUnit(ownerCountryId, unitTypeId);
var pathResult = pathfindingSystem.FindPath(startId, goalId, options);
```

## Movement Cost Calculators

### Interface

```csharp
public interface IMovementCostCalculator
{
    // Cost to move between adjacent provinces
    FixedPoint64 GetMovementCost(ushort from, ushort to, PathContext context);

    // Whether province can be traversed at all
    bool CanTraverse(ushort provinceId, PathContext context);

    // A* heuristic estimate (must not overestimate)
    FixedPoint64 GetHeuristic(ushort from, ushort goal);
}
```

### Default Calculators

```csharp
// All costs = 1 (Dijkstra's algorithm)
UniformCostCalculator.Instance

// Terrain-based costs from registry
var terrainCalc = new TerrainMovementCostCalculator(
    provinceSystem,
    terrainRegistry
);
```

### Custom GAME Layer Calculator

```csharp
public class LandUnitCostCalculator : IMovementCostCalculator
{
    private readonly TerrainMovementCostCalculator terrainCalc;
    private readonly ProvinceSystem provinces;
    private readonly DiplomacySystem diplomacy;

    public FixedPoint64 GetMovementCost(ushort from, ushort to, PathContext context)
    {
        // Base terrain cost
        var cost = terrainCalc.GetMovementCost(from, to, context);

        // Enemy territory penalty
        ushort owner = provinces.GetProvinceOwner(to);
        if (diplomacy.IsAtWar(context.UnitOwnerCountryId, owner))
        {
            cost *= FixedPoint64.FromInt(2);
        }

        return cost;
    }

    public bool CanTraverse(ushort provinceId, PathContext context)
    {
        // Block water for land units
        return !terrainCalc.IsProvinceWater(provinceId);
    }

    public FixedPoint64 GetHeuristic(ushort from, ushort goal)
    {
        // Distance-based estimate if coordinates available
        return FixedPoint64.Zero; // Safe default
    }
}
```

## Path Options

### Configuration

```csharp
public struct PathOptions
{
    // Cost calculation
    public IMovementCostCalculator CostCalculator;
    public PathContext Context;

    // Restrictions
    public HashSet<ushort> ForbiddenProvinces;  // Hard block
    public HashSet<ushort> AvoidProvinces;      // Soft block
    public FixedPoint64 AvoidPenalty;           // Default: 10x

    // Limits
    public int MaxPathLength;  // 0 = unlimited
    public bool UseCache;      // Default: true
}
```

### Factory Methods

```csharp
// Default options
PathOptions.Default

// With cost calculator
PathOptions.WithCostCalculator(calculator)

// With forbidden provinces
PathOptions.WithForbidden(forbiddenSet)

// For specific unit
PathOptions.ForUnit(ownerCountryId, unitTypeId)
```

## Path Context

```csharp
public struct PathContext
{
    public ushort UnitOwnerCountryId;
    public ushort UnitTypeId;
    public PathContextFlags Flags;
}

[Flags]
public enum PathContextFlags
{
    None = 0,
    IgnoreEnemyTerritory = 1 << 0,
    IgnoreImpassable = 1 << 1,    // Debug only
    PreferRoads = 1 << 2,
    AvoidCombat = 1 << 3
}
```

## Path Result

```csharp
public struct PathResult
{
    public List<ushort> Path;        // Province sequence
    public FixedPoint64 TotalCost;   // Sum of movement costs
    public int NodesExplored;        // Search stats
    public bool WasCached;           // From cache?
    public PathStatus Status;        // Found/NotFound/etc.

    public bool Success => Status == PathStatus.Found;
    public int Length => Path?.Count ?? 0;
}

public enum PathStatus
{
    Found,           // Path exists
    NotFound,        // No path between provinces
    InvalidStart,    // Start province invalid
    InvalidGoal,     // Goal province invalid
    TooLong,         // Exceeds MaxPathLength
    NotInitialized,  // System not ready
    Timeout          // Search timeout
}
```

## Path Cache

### Configuration

```csharp
// Create cache with custom size
var cache = new PathCache(maxSize: 256);
```

### Cache Invalidation

```csharp
// Province blocked/unblocked - invalidate paths through it
cache.InvalidateProvince(provinceId);

// Province ownership changed - invalidate endpoint paths
cache.InvalidateEndpoint(provinceId);

// Clear all
cache.Clear();
```

### Statistics

```csharp
int count = cache.Count;
int hits = cache.HitCount;
int misses = cache.MissCount;
float hitRate = cache.HitRate;  // 0-1

string stats = cache.GetStats();
// "PathCache: 150/256 entries, 450 hits, 50 misses (90.0% hit rate)"
```

## Integration Example

```csharp
public class UnitMovementSystem
{
    private PathfindingSystem pathfinding;
    private IMovementCostCalculator landCostCalc;
    private IMovementCostCalculator navalCostCalc;

    public PathResult GetUnitPath(Unit unit, ushort destination)
    {
        // Select calculator based on unit type
        var calculator = IsNavalUnit(unit)
            ? navalCostCalc
            : landCostCalc;

        var options = new PathOptions
        {
            CostCalculator = calculator,
            Context = PathContext.Create(unit.OwnerId, unit.TypeId),
            UseCache = true
        };

        return pathfinding.FindPath(unit.ProvinceId, destination, options);
    }

    public void OnProvinceOwnerChanged(ushort provinceId, ushort newOwner)
    {
        // Ownership affects costs - invalidate cache
        pathfinding.Cache.InvalidateEndpoint(provinceId);
    }
}
```

## Performance

- A* with heuristic: ~O(n log n) typical
- Dijkstra (zero heuristic): O(n log n) guaranteed
- Cache hit: O(1)
- Cache invalidation: O(cached paths)

## Best Practices

1. **Use terrain calculator as base** - wrap with GAME-specific rules
2. **Implement good heuristics** - better estimates = faster search
3. **Invalidate cache on changes** - ownership, terrain modifications
4. **Disable cache for dynamic paths** - frequently changing conditions
5. **Set MaxPathLength** - prevent extremely long searches

## API Reference

- [PathfindingSystem](~/api/Core.Systems.PathfindingSystem.html) - Main system
- [IMovementCostCalculator](~/api/Core.Systems.IMovementCostCalculator.html) - Cost interface
- [PathOptions](~/api/Core.Systems.PathOptions.html) - Request options
- [PathResult](~/api/Core.Systems.PathResult.html) - Result struct
- [PathCache](~/api/Core.Systems.PathCache.html) - LRU cache
