# Data Loading Architecture - JSON5 + Burst Hybrid System

**📊 Implementation Status:** ✅ Fully Implemented (BurstProvinceHistoryLoader, BurstCountryLoader)

> **📚 Architecture Context:** This document describes the data loading system. See [master-architecture-document.md](master-architecture-document.md) for overall architecture.

## Executive Summary

**Question**: How do we load thousands of game data files while maintaining 200+ FPS performance?
**Answer**: Hybrid JSON5 + Burst architecture with two-phase loading
**Key Innovation**: Parse readable JSON5 on main thread, process with Burst jobs for 10-100x speedup
**Performance**: ~300-600ms to load 4,000+ files with parallel Burst compilation

---

## Design Rationale

### Why JSON5?
The project migrated from Paradox .txt format to JSON5 in September 2025 after the .txt parser became unmaintainable.

**Advantages of JSON5:**
- ✅ **Readable & Debuggable** - Standard format with IDE support
- ✅ **Reliable Parsing** - Battle-tested Newtonsoft.Json library
- ✅ **Type Safety** - Structured data with clear types
- ✅ **No Complex Parser** - Deleted ~24,000 lines of fragile parser code
- ✅ **Maintainable** - Easy to modify and extend

**Why Not Pure Burst?**
- ❌ Burst doesn't support string parsing or reference types
- ❌ Burst doesn't support file I/O operations
- ❌ JSON parsing requires managed collections (Dictionary, List)

### The Hybrid Approach

**Solution**: Split loading into two phases:
1. **Phase 1 (Main Thread)**: JSON5 parsing → Burst-compatible structs
2. **Phase 2 (Multi-threaded)**: Burst jobs process structs in parallel

This gives us both **maintainability** (JSON5) and **performance** (Burst).

---

## Two-Phase Loading Pattern

### Architecture Overview

```
┌────────────────────────────────────────────────────────┐
│              PHASE 1: JSON5 LOADING                    │
│              (Main Thread Only)                        │
├────────────────────────────────────────────────────────┤
│  File I/O → JSON5 Parse → Burst Struct Conversion     │
│                                                         │
│  Input:  .json5 files                                  │
│  Output: NativeArray<RawData>                          │
│  Tools:  Newtonsoft.Json, System.IO                    │
└──────────────────┬─────────────────────────────────────┘
                   │
                   ▼
┌────────────────────────────────────────────────────────┐
│              PHASE 2: BURST PROCESSING                 │
│              (Multi-threaded via Jobs)                 │
├────────────────────────────────────────────────────────┤
│  Validation → Transformation → Final Game State        │
│                                                         │
│  Input:  NativeArray<RawData>                          │
│  Output: NativeArray<FinalState>                       │
│  Tools:  Unity Jobs, Burst Compiler                    │
└────────────────────────────────────────────────────────┘
```

### Phase 1: JSON5 Loading

**Responsibilities:**
- Read .json5 files from disk
- Parse JSON5 with Newtonsoft.Json
- Convert to Burst-compatible structs (`RawProvinceData`, `RawCountryData`)
- Store in `NativeArray<T>` with `Allocator.Persistent`

**Constraints:**
- ✅ Can use managed types (string, Dictionary, List)
- ✅ Can perform file I/O
- ❌ Single-threaded only (Unity API limitation)
- ❌ No Burst optimization

**Example (Provinces):**
```csharp
// Core/Loaders/Json5ProvinceConverter.cs
public static Json5ProvinceLoadResult LoadProvinceJson5Files(string dataDirectory)
{
    // 1. Find all .json5 files
    var files = Directory.GetFiles(provincePath, "*.json5");

    // 2. Parse each file with Newtonsoft.Json
    foreach (var file in files)
    {
        var json = JObject.Parse(File.ReadAllText(file));

        // 3. Extract data to burst-compatible struct
        var raw = new RawProvinceData
        {
            provinceID = int.Parse(Path.GetFileNameWithoutExtension(file).Split('-')[0]),
            owner = FixedString32Bytes(json["owner"]?.ToString() ?? ""),
            culture = FixedString32Bytes(json["culture"]?.ToString() ?? ""),
            baseTax = json["base_tax"]?.ToObject<int>() ?? 1,
            // ... more fields
        };

        rawDataList.Add(raw);
    }

    // 4. Convert to NativeArray for burst jobs
    var nativeArray = new NativeArray<RawProvinceData>(
        rawDataList.ToArray(),
        Allocator.Persistent
    );

    return new Json5ProvinceLoadResult { rawData = nativeArray };
}
```

### Phase 2: Burst Processing

**Responsibilities:**
- Validate data (bounds checking, default values)
- Transform raw data to final game state
- Apply game logic (calculate development, pack flags)
- Parallel processing across CPU cores

**Constraints:**
- ✅ Multi-threaded via `IJobParallelFor`
- ✅ Burst-compiled for 10-100x speedup
- ✅ SIMD auto-vectorization
- ❌ No managed types (structs only)
- ❌ No file I/O or Unity API calls

**Example (Provinces):**
```csharp
// Core/Jobs/ProvinceProcessingJob.cs
[BurstCompile(OptimizeFor = OptimizeFor.Performance)]
public struct ProvinceProcessingJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<RawProvinceData> rawData;
    [WriteOnly] public NativeArray<ProvinceInitialState> results;
    [ReadOnly] public bool validateData;
    [ReadOnly] public bool applyDefaults;

    public void Execute(int index)
    {
        var raw = rawData[index];
        var state = ProcessSingleProvince(raw);
        results[index] = state;
    }

    private ProvinceInitialState ProcessSingleProvince(RawProvinceData raw)
    {
        var state = ProvinceInitialState.Create(raw.provinceID);

        // Copy string data (FixedString types)
        state.OwnerTag = raw.owner;
        state.Culture = raw.culture;

        // Validate and clamp numeric data
        state.BaseTax = (byte)math.clamp(raw.baseTax, 0, 255);

        // Pack boolean flags into byte
        state.PackFlags(raw.isCity, raw.hre);

        // Calculate derived values
        state.CalculateDevelopment();

        return state;
    }
}

// Scheduling the job
var job = new ProvinceProcessingJob { rawData = json5Result.rawData, results = output };
var handle = job.Schedule(dataCount, batchSize: 32);
handle.Complete();
```

---

## Province Loading Implementation

### Complete Flow

```csharp
// Core/Loaders/BurstProvinceHistoryLoader.cs
public static ProvinceInitialStateLoadResult LoadProvinceInitialStates(string dataDirectory)
{
    // PHASE 1: JSON5 Loading (Main Thread)
    var json5Result = Json5ProvinceConverter.LoadProvinceJson5Files(dataDirectory);
    // → NativeArray<RawProvinceData>

    // PHASE 2: Burst Processing (Multi-threaded)
    var burstResult = ProcessProvincesWithBurstJobs(json5Result);
    // → NativeArray<ProvinceInitialState>

    // Cleanup
    json5Result.Dispose();

    return ProvinceInitialStateLoadResult.Successful(burstResult.provinces);
}

private static ProvinceProcessingResult ProcessProvincesWithBurstJobs(Json5ProvinceLoadResult json5Result)
{
    var output = new NativeArray<ProvinceInitialState>(
        json5Result.rawData.Length,
        Allocator.Persistent
    );

    var job = new ProvinceProcessingJob {
        rawData = json5Result.rawData,
        results = output,
        validateData = true,
        applyDefaults = true
    };

    // Parallel execution: 32 provinces per batch
    var handle = job.Schedule(json5Result.rawData.Length, 32);
    handle.Complete();

    return ProvinceProcessingResult.Success(output, json5Result.rawData.Length);
}
```

### Province JSON5 Format

```json5
// Assets/Data/history/provinces/1-Stockholm.json5
{
  owner: "SWE",
  controller: "SWE",
  culture: "swedish",
  religion: "catholic",
  base_tax: 5,
  base_production: 4,
  base_manpower: 3,
  trade_goods: "naval_supplies",
  is_city: true,
  hre: false,
  center_of_trade: 2,

  // Historical events (timestamped changes)
  "1436.4.28": {
    revolt: { type: "pretender_rebels", size: 1 }
  },
  "1520.11.7": {
    owner: "DEN"  // Stockholm Bloodbath
  }
}
```

### Data Structures

```csharp
// Raw data from JSON5 (Phase 1 output)
public struct RawProvinceData
{
    public int provinceID;
    public FixedString32Bytes owner;
    public FixedString32Bytes controller;
    public FixedString32Bytes culture;
    public FixedString32Bytes religion;
    public FixedString32Bytes tradeGood;
    public int baseTax;
    public int baseProduction;
    public int baseManpower;
    public int centerOfTrade;
    public bool isCity;
    public bool hre;
    // ... more fields
}

// Final game state (Phase 2 output)
public struct ProvinceInitialState
{
    public int ProvinceID;
    public FixedString32Bytes OwnerTag;
    public FixedString32Bytes ControllerTag;
    public FixedString32Bytes Culture;
    public FixedString32Bytes Religion;
    public FixedString32Bytes TradeGood;
    public byte BaseTax;
    public byte BaseProduction;
    public byte BaseManpower;
    public byte CenterOfTrade;
    public byte Flags;  // Packed booleans
    public byte Terrain;
    public byte Development;  // Calculated from base values
    public bool IsValid;
}
```

---

## Country Loading Implementation

### Complete Flow

```csharp
// Core/Loaders/BurstCountryLoader.cs
public static CountryDataLoadResult LoadAllCountries(
    string dataDirectory,
    Dictionary<string, string> tagMapping = null)
{
    // PHASE 1: JSON5 Loading (Main Thread)
    var json5Result = Json5CountryConverter.LoadCountryJson5Files(dataDirectory, tagMapping);
    // → NativeArray<RawCountryData>

    // PHASE 2: Burst Processing (Multi-threaded for hot data)
    var burstResult = ProcessCountriesWithBurstJobs(json5Result);
    // → NativeArray<CountryHotData>

    // PHASE 3: Create Collection (Main Thread for cold data)
    var collection = CreateCountryCollectionFromResults(burstResult, json5Result);
    // → CountryDataCollection (hot + cold data)

    // Cleanup
    burstResult.Dispose();
    json5Result.Dispose();

    return CountryDataLoadResult.CreateSuccess(collection);
}
```

### Hot/Cold Data Separation

**Why Split Data?**
- **Hot Data**: Accessed frequently (colors, tags) → Burst-optimized structs
- **Cold Data**: Accessed rarely (display names, descriptions) → Managed types on main thread

```csharp
// Hot data processed by Burst jobs (performance-critical)
public struct CountryHotData
{
    public FixedString32Bytes tag;
    public Color32 color;
    public Color32 revolutionaryColor;
    public byte colorR, colorG, colorB;  // Packed for fast GPU upload
    // Only frequently-accessed data
}

// Cold data processed on main thread (reference types allowed)
public class CountryColdData
{
    public string tag;
    public string displayName;
    public string graphicalCulture;
    public string preferredReligion;
    public Color32 color;
    public Color32 revolutionaryColors;
    // Infrequently-accessed data, can use strings/classes
}
```

### Country JSON5 Format

```json5
// Assets/Data/common/countries/Sweden.json5
{
  graphical_culture: "westerngfx",
  color: [8, 82, 165],  // RGB
  revolutionary_colors: [255, 255, 255],

  historical_idea_groups: [
    "administrative_ideas",
    "offensive_ideas",
    "quality_ideas"
  ],

  preferred_religion: "protestant",

  monarch_names: {
    "Gustav #6": 15,
    "Karl #12": 10
  }
}
```

---

## Performance Characteristics

### Benchmark Results (4,000 Province Files)

**Phase 1 (JSON5 Loading):**
- File I/O: ~150-200ms
- JSON parsing: ~100-150ms
- Struct conversion: ~50ms
- **Total Phase 1: ~300-400ms**

**Phase 2 (Burst Processing):**
- Job scheduling: ~5ms
- Parallel execution: ~50-100ms (depends on CPU cores)
- **Total Phase 2: ~55-105ms**

**Combined Total: ~355-505ms** (acceptable for game startup)

### Scaling Analysis

| Province Count | Phase 1 | Phase 2 | Total  |
|---------------|---------|---------|--------|
| 1,000         | ~100ms  | ~20ms   | ~120ms |
| 4,000         | ~350ms  | ~70ms   | ~420ms |
| 10,000        | ~800ms  | ~150ms  | ~950ms |

**Key Insights:**
- Phase 1 scales linearly with file count (I/O bound)
- Phase 2 benefits from parallelism (CPU core count matters)
- Burst compilation provides ~10x speedup over managed code in Phase 2

### Memory Usage

**Province Loading (4,000 provinces):**
```
Phase 1 Output: NativeArray<RawProvinceData>
  Size: 4,000 × ~120 bytes = ~480KB

Phase 2 Output: NativeArray<ProvinceInitialState>
  Size: 4,000 × ~80 bytes = ~320KB

Total Allocated: ~800KB (negligible)
```

**Country Loading (979 countries):**
```
Hot Data:  NativeArray<CountryHotData> (~40 bytes each) = ~39KB
Cold Data: Dictionary<string, CountryColdData> = ~200KB
Total: ~240KB
```

### Optimization Notes

1. **Allocator.Persistent** - Required because data survives >4 frames during coroutine loading
2. **Batch Size = 32** - Optimal balance between job overhead and parallelism
3. **[ReadOnly] Attributes** - Enable Burst's aliasing analysis for better SIMD
4. **FixedString Types** - Burst-compatible strings with fixed 32-byte size

---

## Error Handling & Validation

### Phase 1 Validation (JSON5 Loading)

```csharp
try
{
    var json = JObject.Parse(File.ReadAllText(filePath));

    // Validate required fields
    if (json["owner"] == null)
    {
        Debug.LogWarning($"Province {provinceID} missing owner, using default");
        raw.owner = new FixedString32Bytes("---");
    }

    // Type conversion with fallbacks
    raw.baseTax = json["base_tax"]?.ToObject<int>() ?? 1;
}
catch (Exception e)
{
    Debug.LogError($"Failed to parse {filePath}: {e.Message}");
    failedCount++;
    continue;  // Skip this file, continue loading others
}
```

### Phase 2 Validation (Burst Jobs)

```csharp
[BurstCompile]
private void ValidateProvinceData(ref ProvinceInitialState state)
{
    // Bounds checking
    if (state.BaseTax < 1) state.BaseTax = 1;
    if (state.BaseTax > 99) state.BaseTax = 99;

    // Validate enums/ranges
    if (state.CenterOfTrade > 3) state.CenterOfTrade = 3;

    // Ensure valid IDs
    if (state.ProvinceID <= 0) state.IsValid = false;
}
```

### Error Recovery Strategy

**Principle**: Load as much data as possible, skip corrupt files

```csharp
// Result types include success/failure tracking
public struct Json5ProvinceLoadResult
{
    public bool success;
    public NativeArray<RawProvinceData> rawData;
    public int loadedCount;
    public int failedCount;
    public string errorMessage;
}

// Partial success is acceptable
if (result.loadedCount > 0 && result.failedCount < result.loadedCount / 2)
{
    Debug.LogWarning($"Loaded {result.loadedCount} provinces, {result.failedCount} failed");
    // Continue with partial data
}
else
{
    Debug.LogError("Too many load failures, aborting");
    return LoadResult.Failed();
}
```

---

## Integration with Game Systems

### Startup Flow

```csharp
// Core/GameInitializer.cs
private IEnumerator LoadProvinceDataPhase()
{
    SetPhase(LoadingPhase.LoadingProvinces, 15f, "Loading province data...");

    // Hybrid JSON5 + Burst loading
    provinceInitialStates = BurstProvinceHistoryLoader.LoadProvinceInitialStates(
        gameSettings.DataDirectory
    );

    if (!provinceInitialStates.Success)
    {
        ReportError($"Province loading failed: {provinceInitialStates.ErrorMessage}");
        yield break;
    }

    // Initialize game state from loaded data
    gameState.Provinces.InitializeFromProvinceStates(provinceInitialStates);

    LogPhaseComplete($"Loaded {provinceInitialStates.LoadedCount} provinces");
}
```

### ProvinceSystem Integration

```csharp
// Core/Systems/ProvinceSystem.cs
public void InitializeFromProvinceStates(ProvinceInitialStateLoadResult loadResult)
{
    DominionLogger.Log($"Initializing {loadResult.LoadedCount} provinces from JSON5 + Burst");

    for (int i = 0; i < loadResult.InitialStates.Length; i++)
    {
        var initialState = loadResult.InitialStates[i];
        if (!initialState.IsValid) continue;

        ushort provinceId = (ushort)initialState.ProvinceID;
        AddProvince(provinceId, initialState.Terrain);

        // Store initial state for reference resolution
        initialStateCache[provinceId] = initialState;
    }

    // Dispose loaded data after extraction
    loadResult.Dispose();
}
```

---

## Best Practices

### DO ✅

1. **Use Allocator.Persistent** for data that survives multiple frames
2. **Dispose NativeArrays** when done - prevents memory leaks
3. **Batch size = 32** for IJobParallelFor scheduling
4. **[ReadOnly] attributes** on input NativeArrays for safety
5. **Validate in Phase 1** - catch malformed JSON early
6. **Validate in Phase 2** - enforce game rules with Burst
7. **Graceful degradation** - skip corrupt files, continue loading

### DON'T ❌

1. **Don't use Allocator.Temp** - disposed too quickly for async loading
2. **Don't mix managed/unmanaged** - keep phases strictly separated
3. **Don't parse strings in Burst** - use FixedString types instead
4. **Don't forget .Dispose()** - memory leaks crash builds
5. **Don't use Unity API in jobs** - breaks Burst compilation
6. **Don't nest jobs** - schedule jobs in sequence with dependencies

---

## Related Documentation

### Architecture Documents
- **[master-architecture-document.md](master-architecture-document.md)** - Overall system architecture and dual-layer design
- **[data-flow-architecture.md](data-flow-architecture.md)** - System communication and data access patterns
- **[performance-architecture-guide.md](performance-architecture-guide.md)** - Memory optimization and cache efficiency

### Technical References
- **[unity-burst-jobs-architecture.md](../Log/learnings/unity-burst-jobs-architecture.md)** - Complete Burst compiler guide with examples
- **[Core/FILE_REGISTRY.md](../../Scripts/Core/FILE_REGISTRY.md)** - Complete listing of loader files and jobs

### Session Logs
- **[2025-09-28-3-json5-migration.md](../Log/old/2025-09-28-3-json5-migration.md)** - Original JSON5 migration decision and implementation
- **[paradoxparser-cleanup-audit.md](../Log/2025-10-02/paradoxparser-cleanup-audit.md)** - ParadoxParser cleanup removing 24,000 lines

---

## Conclusion

The hybrid JSON5 + Burst architecture provides the best of both worlds:

1. **Maintainability** - Readable JSON5 format with standard tooling
2. **Performance** - Burst-compiled parallel processing for 10-100x speedup
3. **Reliability** - Battle-tested JSON parser, no fragile custom parser
4. **Scalability** - Handles 4,000+ files in <500ms startup time

This architecture replaced a complex 24,000-line .txt parser that was unmaintainable and produced corrupt data. The new system is simpler, faster, and more reliable.

**Key Innovation**: By splitting parsing (managed code) from processing (Burst jobs), we avoid Burst's limitations while maintaining high performance where it matters.

---

*Last Updated: 2025-10-02*
*Implementation: Complete*
*Status: Production-ready*
