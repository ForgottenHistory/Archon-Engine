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

            // Map development to color
            paletteColors[i] = MapDevelopmentToColor(development);
        }

        textureManager.SetPaletteColors(paletteColors);
        textureManager.ApplyPaletteChanges();
    }
}
```

### Use Case 2: UI Display

```csharp
public class ProvinceInfoPanel : MonoBehaviour
{
    public void ShowProvinceInfo(ushort provinceId)
    {
        var gameState = GameState.Instance;
        if (!gameState.IsInitialized) return;

        // Get basic info
        var owner = gameState.ProvinceQueries.GetOwner(provinceId);
        var development = gameState.ProvinceQueries.GetDevelopment(provinceId);
        var terrain = gameState.ProvinceQueries.GetTerrain(provinceId);

        // Get owner info
        var ownerTag = "Unowned";
        var ownerColor = Color.gray;
        if (owner != 0)
        {
            ownerTag = gameState.CountryQueries.GetTag(owner);
            ownerColor = gameState.CountryQueries.GetColor(owner);
        }

        // Update UI
        provinceNameText.text = $"Province {provinceId}";
        ownerText.text = $"Owner: {ownerTag}";
        developmentText.text = $"Development: {development}";
        ownerFlag.color = ownerColor;
    }
}
```

### Use Case 3: AI Decision Making

```csharp
public class AICountryEvaluator
{
    public float EvaluateCountryStrength(ushort countryId, GameState gameState)
    {
        var countryQueries = gameState.CountryQueries;

        // Get country metrics
        int totalDevelopment = countryQueries.GetTotalDevelopment(countryId);
        int provinceCount = countryQueries.GetProvinceCount(countryId);
        int landProvinces = countryQueries.GetLandProvinceCount(countryId);

        // Calculate strength score
        float strengthScore = totalDevelopment * 0.4f + landProvinces * 0.6f;

        return strengthScore;
    }

    public List<ushort> FindExpansionTargets(ushort countryId, GameState gameState)
    {
        var targets = new List<ushort>();

        // Get unowned land provinces
        using var unownedProvinces = gameState.ProvinceQueries.GetUnownedProvinces(Allocator.Temp);

        for (int i = 0; i < unownedProvinces.Length; i++)
        {
            var provinceId = unownedProvinces[i];

            // Skip ocean provinces
            if (gameState.ProvinceQueries.IsOcean(provinceId))
                continue;

            // Check if province has good development
            var development = gameState.ProvinceQueries.GetDevelopment(provinceId);
            if (development >= 10) // Minimum threshold
            {
                targets.Add(provinceId);
            }
        }

        return targets;
    }
}
```

### Use Case 4: Data Analysis/Statistics

```csharp
public class GameStatistics
{
    public void GenerateReport(GameState gameState)
    {
        // Country statistics
        var countryStats = gameState.CountryQueries.GetCountryStatistics();

        // Province statistics
        var provinceStats = gameState.ProvinceQueries.GetProvinceStatistics();

        // Top countries by development
        var topCountries = gameState.CountryQueries.GetCountriesByDevelopment(ascending: false);

        DominionLogger.Log($"Game Statistics:");
        DominionLogger.Log($"- Total Countries: {countryStats.TotalCountries}");
        DominionLogger.Log($"- Total Provinces: {provinceStats.TotalProvinces}");
        DominionLogger.Log($"- Total Development: {provinceStats.TotalDevelopment}");
        DominionLogger.Log($"- Average Development: {provinceStats.AverageDevelopment:F1}");

        if (topCountries.Length > 0)
        {
            var topCountryId = topCountries[0];
            var topCountryTag = gameState.CountryQueries.GetTag(topCountryId);
            var topCountryDev = gameState.CountryQueries.GetTotalDevelopment(topCountryId);
            DominionLogger.Log($"- Most Developed Country: {topCountryTag} ({topCountryDev} development)");
        }
    }
}
```

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

### Core System Files

#### `GameState.cs` - Central Data Hub
**Location:** `Assets/Scripts/Core/GameState.cs`
- **Purpose:** Central coordinator for all Core data access
- **Key Features:** Singleton pattern, system initialization, unified command execution
- **Usage:** `GameState.Instance` - your entry point to all Core data

#### `ProvinceSystem.cs` - Province Data Owner
**Location:** `Assets/Scripts/Core/Systems/ProvinceSystem.cs`
- **Purpose:** Single source of truth for province simulation data (8-byte ProvinceState)
- **Key Features:** Structure of Arrays, hot/cold separation, NativeArray storage
- **Data Managed:** Province ownership, development, terrain, flags
- **Access Via:** `gameState.Provinces` (internal) or `gameState.ProvinceQueries` (recommended)

#### `CountrySystem.cs` - Country Data Owner
**Location:** `Assets/Scripts/Core/Systems/CountrySystem.cs`
- **Purpose:** Single source of truth for country/nation data (8-byte CountryHotData)
- **Key Features:** Hot data (colors, tags) + cold data (detailed info), lazy loading
- **Data Managed:** Country colors, tags, flags, graphical cultures
- **Access Via:** `gameState.Countries` (internal) or `gameState.CountryQueries` (recommended)

### Query Layer Files

#### `ProvinceQueries.cs` - Province Data Access
**Location:** `Assets/Scripts/Core/Queries/ProvinceQueries.cs`
- **Purpose:** Optimized province data access with performance guarantees
- **Query Types:** Basic (<0.001ms), Computed (<5ms), Cross-system, Cached
- **Key Methods:** `GetOwner()`, `GetDevelopment()`, `GetCountryProvinces()`, `GetProvinceOwnerColor()`

#### `CountryQueries.cs` - Country Data Access
**Location:** `Assets/Scripts/Core/Queries/CountryQueries.cs`
- **Purpose:** Optimized country data access with caching for expensive calculations
- **Query Types:** Basic (colors, tags), Complex (total development), Cached (1s TTL)
- **Key Methods:** `GetColor()`, `GetTag()`, `GetTotalDevelopment()`, `GetProvinces()`

### Data Structure Files

#### `ProvinceState.cs` - Core Province Data (8 bytes)
**Location:** `Assets/Scripts/Core/Data/ProvinceState.cs`
- **Purpose:** Exactly 8-byte struct for deterministic simulation
- **Fields:** `ownerID`, `controllerID`, `development`, `terrain`, `fortLevel`, `flags`
- **Features:** Compile-time size validation, serialization, flag operations

#### `ProvinceInitialState.cs` - Province Initialization
**Location:** `Assets/Scripts/Core/Data/ProvinceInitialState.cs`
- **Purpose:** Burst-compatible initial province data from history files
- **Contains:** Development components (BaseTax, BaseProduction, BaseManpower), string data
- **Calculation:** `Development = BaseTax + BaseProduction + BaseManpower` (capped at 255)

#### `Json5ProvinceData.cs` - Raw Province Data
**Location:** `Assets/Scripts/Core/Data/Json5ProvinceData.cs`
- **Purpose:** Intermediate format between JSON parsing and Burst processing
- **Contains:** Raw values from province history files before ID resolution

### Command System Files

#### `ICommand.cs` - Command Interface
**Location:** `Assets/Scripts/Core/Commands/ICommand.cs`
- **Purpose:** Base interface for all game state changes
- **Features:** Validation, execution, undo support, networking capability
- **Pattern:** All Core modifications must go through commands

#### `ProvinceCommands.cs` - Province Modification Commands
**Location:** `Assets/Scripts/Core/Commands/ProvinceCommands.cs`
- **Commands:** `ChangeProvinceOwnerCommand`, `ChangeProvinceDevelopmentCommand`, `TransferProvincesCommand`
- **Usage:** Deterministic province state changes with validation and events

### Event System Files

#### `EventBus.cs` - Inter-System Communication
**Location:** `Assets/Scripts/Core/EventBus.cs`
- **Purpose:** Decoupled communication between Core systems
- **Events:** `ProvinceOwnershipChangedEvent`, `ProvinceDevelopmentChangedEvent`, etc.
- **Pattern:** Systems emit events, others subscribe to react

### Data Loading Files

#### `BurstProvinceHistoryLoader.cs` - High-Performance Loading
**Location:** `Assets/Scripts/Core/Loaders/BurstProvinceHistoryLoader.cs`
- **Purpose:** Burst-compiled parallel loading of province history data
- **Features:** Job system integration, parallel processing, validation

#### `JobifiedCountryLoader.cs` - Country Data Loading
**Location:** `Assets/Scripts/Core/Loaders/JobifiedCountryLoader.cs`
- **Purpose:** Parallel loading and processing of country definitions
- **Features:** JSON5 parsing, color extraction, validation

### Processing Jobs

#### `ProvinceProcessingJob.cs` - Data Transformation
**Location:** `Assets/Scripts/Core/Jobs/ProvinceProcessingJob.cs`
- **Purpose:** Burst-compiled conversion from raw JSON data to ProvinceInitialState
- **Features:** Parallel processing, validation, default value application
- **Key Logic:** Development calculation, flag packing, bounds checking

---

## File Organization Pattern

```
Assets/Scripts/Core/
├── GameState.cs                    # Central hub
├── Systems/
│   ├── ProvinceSystem.cs          # Province data owner
│   ├── CountrySystem.cs           # Country data owner
│   └── TimeManager.cs             # Game time management
├── Queries/
│   ├── ProvinceQueries.cs         # Province data access
│   └── CountryQueries.cs          # Country data access
├── Commands/
│   ├── ICommand.cs                # Command interface
│   └── ProvinceCommands.cs        # Province modification commands
├── Data/
│   ├── ProvinceState.cs           # 8-byte hot data
│   ├── ProvinceInitialState.cs    # Initialization data
│   └── Json5ProvinceData.cs       # Raw parsed data
├── Jobs/
│   └── ProvinceProcessingJob.cs   # Burst data processing
└── Loaders/
    ├── BurstProvinceHistoryLoader.cs  # Province history loading
    └── JobifiedCountryLoader.cs       # Country data loading
```

### Integration Points

- **Map Layer → Core:** Through Query classes only (`ProvinceQueries`, `CountryQueries`)
- **State Changes:** Through Command pattern (`ICommand` implementations)
- **System Communication:** Through EventBus (loose coupling)
- **Data Flow:** Raw Data → Jobs → InitialState → ProvinceState → Queries

---

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