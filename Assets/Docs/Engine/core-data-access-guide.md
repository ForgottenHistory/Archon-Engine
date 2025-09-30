# Core Data Access Guide
## How to Get Data from Core in Dominion's Architecture

---

## Overview

Dominion uses a **dual-layer architecture**:
- **Core Layer (CPU)**: Deterministic simulation with hot data (8-byte structs)
- **Map Layer (GPU)**: High-performance presentation reading from Core

This guide explains how to properly access Core simulation data for any purpose - rendering, UI, AI, etc.

---

## Architecture Principles

### ✅ **The Right Way**
```
Your Code → GameState → Query Classes → Core Systems → Data
```

### ❌ **Wrong Way (Don't Do This)**
```
Your Code → Direct System Access → Data
```

### ✅ **Core Owns Data, Map Reads Data**
- Core systems (ProvinceSystem, CountrySystem) own authoritative data
- Map layer reads from Core via queries for rendering
- Never store Core data copies in Map layer

---

## Getting Started: The Central Hub

All Core data access goes through `GameState`:

```csharp
// Get the central hub
var gameState = Object.FindFirstObjectByType<GameState>();
if (gameState?.IsInitialized != true)
{
    DominionLogger.LogError("GameState not available");
    return;
}

// Access query interfaces
var provinceQueries = gameState.ProvinceQueries;
var countryQueries = gameState.CountryQueries;
```

---

## Data Access Patterns

### 1. Basic Queries (Ultra-Fast: <0.001ms)

Direct access to hot data with no computation:

```csharp
// Province data
ushort owner = provinceQueries.GetOwner(provinceId);
byte development = provinceQueries.GetDevelopment(provinceId);
byte terrain = provinceQueries.GetTerrain(provinceId);
bool exists = provinceQueries.Exists(provinceId);

// Country data
Color32 color = countryQueries.GetColor(countryId);
string tag = countryQueries.GetTag(countryId);
ushort countryId = countryQueries.GetIdFromTag("ENG");
bool hasFlag = countryQueries.HasFlag(countryId, someFlag);
```

### 2. Computed Queries (On-Demand: <5ms)

Calculations performed when requested:

```csharp
// Get all provinces owned by a country
using var provinces = provinceQueries.GetCountryProvinces(countryId, Allocator.Temp);
for (int i = 0; i < provinces.Length; i++)
{
    var provinceId = provinces[i];
    // Process each province
}
// provinces.Dispose() called automatically by using statement

// Filter provinces by criteria
using var oceanProvinces = provinceQueries.GetOceanProvinces(Allocator.Temp);
using var landProvinces = provinceQueries.GetLandProvinces(Allocator.Temp);
```

### 3. Cross-System Queries (Combines Multiple Systems)

```csharp
// Get the color of the country that owns a province
Color32 ownerColor = provinceQueries.GetProvinceOwnerColor(provinceId);

// Get the tag of the province owner
string ownerTag = provinceQueries.GetProvinceOwnerTag(provinceId);

// Check if two provinces have the same owner
bool sameOwner = provinceQueries.ShareSameOwner(provinceId1, provinceId2);
```

### 4. Cached Queries (Expensive Calculations: <0.01ms if cached)

```csharp
// These are cached automatically for performance
int totalDev = countryQueries.GetTotalDevelopment(countryId);
int provinceCount = countryQueries.GetProvinceCount(countryId);
float avgDev = countryQueries.GetAverageDevelopment(countryId);
```

---

## Province Development Calculation

Development is calculated as: **BaseTax + BaseProduction + BaseManpower** (capped at 255)

```csharp
// This calculation happens in Core during data loading:
// Development = (byte)math.min(255, BaseTax + BaseProduction + BaseManpower);

// To get the final development value:
byte development = provinceQueries.GetDevelopment(provinceId);

// The individual components are stored in cold data (ProvinceInitialState)
// and accessed through the province history system if needed
```

---

## Common Use Cases

### Use Case 1: Map Rendering (GPU Textures)

```csharp
public class SomeMapMode : MapMode
{
    public override void UpdateGPUTextures(MapTextureManager textureManager)
    {
        var gameState = Object.FindFirstObjectByType<GameState>();
        if (gameState?.ProvinceQueries == null) return;

        // Get all provinces
        using var allProvinces = gameState.ProvinceQueries.GetAllProvinceIds(Allocator.Temp);

        // Update color palette based on Core data
        var paletteColors = new Color32[256];
        for (int i = 0; i < allProvinces.Length && i < 256; i++)
        {
            var provinceId = allProvinces[i];
            var development = gameState.ProvinceQueries.GetDevelopment(provinceId);
            paletteColors[i] = MapDevelopmentToColor(development);
        }

        textureManager.SetPaletteColors(paletteColors);
        textureManager.ApplyPaletteChanges();
    }
}
```

**Other common patterns**: UI Display (query owner/dev/terrain for panels), AI Decision Making (evaluate strength via total development), Data Analysis/Statistics (aggregate queries for reporting). All follow the same GameState → ProvinceQueries/CountryQueries pattern.

---

## Performance Guidelines

### Memory Management

```csharp
// ✅ Use 'using' for automatic disposal
using var provinces = provinceQueries.GetCountryProvinces(countryId, Allocator.Temp);
// provinces.Dispose() called automatically

// ✅ Manual disposal if needed
var provinces = provinceQueries.GetCountryProvinces(countryId, Allocator.TempJob);
try
{
    // Use provinces
}
finally
{
    provinces.Dispose();
}

// ❌ Don't forget to dispose!
var provinces = provinceQueries.GetCountryProvinces(countryId, Allocator.TempJob);
// Memory leak!
```

### Allocator Choice

```csharp
// For short-lived data (within same frame)
using var provinces = provinceQueries.GetCountryProvinces(countryId, Allocator.Temp);

// For data passed between frames or jobs
var provinces = provinceQueries.GetCountryProvinces(countryId, Allocator.TempJob);
// Remember to dispose later

// For permanent data (rare)
var provinces = provinceQueries.GetCountryProvinces(countryId, Allocator.Persistent);
// Must dispose manually when no longer needed
```

### Caching Considerations

```csharp
// ✅ These are automatically cached
int totalDev = countryQueries.GetTotalDevelopment(countryId); // Fast on subsequent calls

// ✅ Invalidate cache when data changes
countryQueries.InvalidateCache(countryId); // After ownership changes

// ✅ Clear all cache periodically
countryQueries.ClearCache(); // At end of major game events
```

---

## Anti-Patterns (DON'T DO THESE)

### ❌ **Direct System Access**
```csharp
// WRONG - bypasses the query layer
var provinceSystem = FindObjectOfType<ProvinceSystem>();
var owner = provinceSystem.GetProvinceOwner(provinceId); // Don't do this!
```

### ❌ **Storing Core Data in Map Layer**
```csharp
// WRONG - Map layer should not store Core data
public class SomeMapComponent : MonoBehaviour
{
    private Dictionary<ushort, ushort> provinceOwners; // Don't store this!

    void Update()
    {
        // Always read fresh from Core instead
        var owner = gameState.ProvinceQueries.GetOwner(provinceId);
    }
}
```

### ❌ **Forgetting to Dispose Native Arrays**
```csharp
// WRONG - memory leak
var provinces = provinceQueries.GetCountryProvinces(countryId, Allocator.TempJob);
return provinces.Length; // Forgot to dispose!
```

### ❌ **CPU Processing of Millions of Pixels**
```csharp
// WRONG - use GPU compute shaders instead
for (int y = 0; y < 2048; y++)
{
    for (int x = 0; x < 2048; x++)
    {
        // Don't process millions of pixels on CPU!
    }
}
```

---

## Advanced Topics

### Command Pattern for State Changes

```csharp
// ✅ Use commands to modify Core state
var command = new ChangeOwnerCommand
{
    provinceId = provinceId,
    newOwner = newOwner
};

if (gameState.TryExecuteCommand(command))
{
    // State changed, query layer will reflect the change
    var updatedOwner = gameState.ProvinceQueries.GetOwner(provinceId);
}
```

### Event-Driven Updates

```csharp
public class SomeSystem : MonoBehaviour
{
    void Start()
    {
        var gameState = GameState.Instance;
        gameState.EventBus.Subscribe<ProvinceOwnershipChangedEvent>(OnProvinceOwnershipChanged);
    }

    private void OnProvinceOwnershipChanged(ProvinceOwnershipChangedEvent evt)
    {
        // React to ownership changes
        UpdateDisplay(evt.ProvinceId);
    }
}
```

### Hot vs Cold Data

```csharp
// Hot data: Available through queries (8-byte ProvinceState)
var development = provinceQueries.GetDevelopment(provinceId);
var owner = provinceQueries.GetOwner(provinceId);

// Cold data: Historical events and detailed info
var recentHistory = gameState.Provinces.GetRecentHistory(provinceId);
var historySummary = gameState.Provinces.GetHistorySummary(provinceId);
```

---

## Key Script Files Reference

| Component | Location | Purpose |
|-----------|----------|---------|
| **GameState.cs** | `Assets/Scripts/Core/` | Central coordinator, singleton pattern, entry point for all Core data |
| **ProvinceSystem.cs** | `Assets/Scripts/Core/Systems/` | Province data owner (8-byte ProvinceState, NativeArray storage) |
| **CountrySystem.cs** | `Assets/Scripts/Core/Systems/` | Country data owner (hot/cold data separation) |
| **ProvinceQueries.cs** | `Assets/Scripts/Core/Queries/` | Optimized province data access (<0.001ms basic, <5ms computed) |
| **CountryQueries.cs** | `Assets/Scripts/Core/Queries/` | Optimized country data access (cached with 1s TTL) |
| **ProvinceState.cs** | `Assets/Scripts/Core/Data/` | 8-byte struct (owner, controller, development, terrain, flags) |
| **ICommand.cs** | `Assets/Scripts/Core/Commands/` | Command interface for state changes |
| **EventBus.cs** | `Assets/Scripts/Core/` | Inter-system communication |

**File Organization Pattern**: `Core/Systems/` (data owners) → `Core/Queries/` (read access) → `Core/Commands/` (write operations) → `Core/Data/` (structures)

**Integration Points**: Map Layer → Queries only | State Changes → Commands | Communication → EventBus | Data Flow: Raw → Jobs → InitialState → ProvinceState → Queries

---

## Related Documents

- **[MapMode System Architecture](mapmode-system-architecture.md)** - Shows practical usage of Core data access in map rendering
- **[Master Architecture Document](master-architecture-document.md)** - Architecture context and dual-layer system overview

## Summary

1. **Always access Core data through `GameState` and Query classes**
2. **Never access systems directly**
3. **Use appropriate Allocator types and dispose native arrays**
4. **Let Core own the data, read it for presentation**
5. **Use Commands for state changes, Events for notifications**
6. **Cache is handled automatically for expensive queries**

This architecture ensures:
- **Performance**: Query layer is optimized for common access patterns
- **Determinism**: Core simulation remains predictable
- **Modularity**: Clear separation between simulation and presentation
- **Scalability**: Handles 10,000+ provinces efficiently

---

**Remember**: Core is the single source of truth. Map layer renders what Core tells it. Everything else reads from Core through the Query system.