# Query System

The query system provides fluent query builders for filtering provinces, countries, and units. Lazy evaluation ensures filters are combined into a single pass.

## Architecture

```
Query (Static Entry Point)
├── ProvinceQueryBuilder  - Province filtering
├── CountryQueryBuilder   - Country filtering
└── UnitQueryBuilder      - Unit filtering

Each builder:
- Lazy evaluation (filters applied on terminal operation)
- Single pass through data (O(n) where n = entities)
- NativeList results (caller must dispose)
```

**Key Principles:**
- Fluent chainable API
- Single pass with combined filters
- Burst-compatible (NativeList results)
- Caller disposes results

## Province Queries

### Basic Usage

```csharp
using var results = Query.Provinces(gameState)
    .OwnedBy(countryId)
    .IsLand()
    .Execute(Allocator.Temp);

foreach (ushort provinceId in results)
{
    // Process province
}
```

### Available Filters

```csharp
// Ownership
.OwnedBy(countryId)      // Owned by country
.ControlledBy(countryId) // Controlled by country
.IsOwned()               // Has any owner
.IsUnowned()             // No owner

// Terrain
.WithTerrain(terrainType)  // Specific terrain
.IsLand()                  // Not ocean (terrain != 0)
.IsOcean()                 // Ocean (terrain == 0)

// Spatial (requires AdjacencySystem)
.AdjacentTo(provinceId)    // Direct neighbor
.BorderingCountry(countryId) // Adjacent to country territory
.WithinDistance(sourceId, maxHops) // Within N hops
```

### Terminal Operations

```csharp
// Get all matching provinces
using var results = query.Execute(Allocator.Temp);

// Count without allocating list
int count = query.Count();

// Check if any match
bool hasAny = query.Any();

// Get first match (0 if none)
ushort first = query.FirstOrDefault();
```

## Country Queries

### Basic Usage

```csharp
using var neighbors = Query.Countries(gameState)
    .BorderingCountry(targetCountryId)
    .WithMinProvinces(5)
    .Execute(Allocator.Temp);
```

### Available Filters

```csharp
// Province count
.WithMinProvinces(min)     // At least N provinces
.WithMaxProvinces(max)     // At most N provinces
.WithProvinceCount(min, max) // Range
.HasProvinces()            // At least one province
.HasNoProvinces()          // Zero provinces

// Relationships (requires AdjacencySystem)
.BorderingCountry(countryId) // Shares border

// Attributes
.WithGraphicalCulture(cultureId) // Graphical culture
```

## Unit Queries

### Basic Usage

```csharp
using var units = Query.Units(unitSystem)
    .OwnedBy(countryId)
    .InProvince(provinceId)
    .Execute(Allocator.Temp);
```

### Available Filters

```csharp
// Ownership
.OwnedBy(countryId)      // Owned by country
.NotOwnedBy(countryId)   // Not owned (enemies)

// Location
.InProvince(provinceId)  // In specific province

// Type
.OfType(unitTypeId)      // Specific unit type

// Strength
.WithMinTroops(min)      // At least N troops
.WithMaxTroops(max)      // At most N troops
.WithTroopCount(min, max) // Range
```

### Unit-Specific Operations

```csharp
// Total troop count of matching units
int totalTroops = Query.Units(unitSystem)
    .OwnedBy(countryId)
    .TotalTroops();
```

## Performance Optimizations

### Index-Based Shortcuts

UnitQueryBuilder uses index optimizations:
- `.InProvince()` - Only iterates province's units
- `.OwnedBy()` - Only iterates country's units
- Otherwise - Full scan with early exit

### Dispose Pattern

Always dispose query results:

```csharp
// CORRECT: Using statement
using var results = Query.Provinces(gameState)
    .OwnedBy(countryId)
    .Execute(Allocator.Temp);

// CORRECT: Manual dispose
var results = Query.Provinces(gameState)
    .OwnedBy(countryId)
    .Execute(Allocator.Temp);
try { ... }
finally { results.Dispose(); }

// WRONG: Memory leak
var results = Query.Provinces(gameState)
    .OwnedBy(countryId)
    .Execute(Allocator.Temp);
// No dispose!
```

### Distance Queries

Distance queries use GraphDistanceCalculator internally:

```csharp
// Dispose the builder when using WithinDistance
using var builder = new ProvinceQueryBuilder(provinceSystem, adjacencySystem);
using var results = builder
    .WithinDistance(capitalId, 3)
    .Execute(Allocator.Temp);
```

## Integration Example

```csharp
public class BorderAnalyzer
{
    public NativeList<ushort> GetVulnerableBorderProvinces(
        GameState gameState,
        ushort countryId,
        Allocator allocator)
    {
        // Find provinces bordering enemies
        using var enemies = Query.Countries(gameState)
            .BorderingCountry(countryId)
            .Execute(Allocator.Temp);

        var vulnerableProvinces = new NativeList<ushort>(64, allocator);

        foreach (ushort enemyId in enemies)
        {
            using var borderProvinces = Query.Provinces(gameState)
                .OwnedBy(countryId)
                .BorderingCountry(enemyId)
                .Execute(Allocator.Temp);

            for (int i = 0; i < borderProvinces.Length; i++)
            {
                if (!vulnerableProvinces.Contains(borderProvinces[i]))
                {
                    vulnerableProvinces.Add(borderProvinces[i]);
                }
            }
        }

        return vulnerableProvinces;
    }
}
```

## Performance

- Province queries: O(P) where P = total provinces
- Country queries: O(C) where C = total countries
- Unit queries: O(U) or O(n) with index shortcuts
- All queries: Single pass with combined filters

## API Reference

- [Query](~/api/Core.Queries.Query.html) - Static entry point
- [ProvinceQueryBuilder](~/api/Core.Queries.ProvinceQueryBuilder.html) - Province filters
- [CountryQueryBuilder](~/api/Core.Queries.CountryQueryBuilder.html) - Country filters
- [UnitQueryBuilder](~/api/Core.Queries.UnitQueryBuilder.html) - Unit filters
