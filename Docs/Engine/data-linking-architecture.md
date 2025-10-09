# Grand Strategy Game - Data Linking & Reference Resolution Architecture

**📊 Implementation Status:** ✅ Implemented (CrossReferenceBuilder, ReferenceResolver, DataValidator exist)

**🔄 Recent Update (2025-10-09):** ProvinceState refactored for engine-game separation. Game-specific fields (`development`, `fortLevel`, `flags`) moved to `HegemonProvinceData`. See [phase-3-complete-scenario-loader-bug-fixed.md](../Log/2025-10/2025-10-09/phase-3-complete-scenario-loader-bug-fixed.md).

## Executive Summary
**Challenge**: Loaded data has string references ("ENG", "catholic", "grain") that need linking to actual game objects
**Solution**: Multi-phase loading with efficient ID mapping and reference resolution
**Key Principle**: Convert strings to IDs once at load time, never use strings at runtime
**Result**: Fast lookups (array indexing), type-safe references, and clear data relationships

## The Core Problem

When you load Paradox-style data files, you get string references everywhere:
```
# Province file
1234 = {
    owner = "ENG"
    controller = "FRA"
    religion = "catholic"
    culture = "english"
    trade_good = "grain"
    buildings = { "temple" "marketplace" }
}

# Country file
ENG = {
    capital = 236
    religion = "protestant"
    technology_group = "western"
    government = "monarchy"
}
```

These strings need to be resolved to actual runtime IDs and validated for consistency.

## Three-Phase Loading Architecture

### Phase 1: Discovery & Registration
First pass - discover all entities and assign IDs

### Phase 2: Loading & Parsing
Load actual data with string references intact

### Phase 3: Linking & Resolution
Convert all string references to runtime IDs

```csharp
public class DataLoadingPipeline {
    public void LoadGameData() {
        // Phase 1: Discovery
        DiscoverAllEntities();

        // Phase 2: Load
        LoadRawData();

        // Phase 3: Link
        ResolveAllReferences();

        // Phase 4: Validate
        ValidateDataIntegrity();
    }
}
```

## The Registry Pattern

### Central Registry System
```csharp
public interface IRegistry<T> where T : class {
    ushort Register(string key, T item);
    T Get(ushort id);
    T Get(string key);
    ushort GetId(string key);
    bool TryGet(string key, out T item);
    IEnumerable<T> GetAll();
}

public class Registry<T> : IRegistry<T> where T : class {
    private readonly Dictionary<string, ushort> stringToId = new();
    private readonly List<T> items = new();
    private readonly string typeName;

    public Registry(string typeName) {
        this.typeName = typeName;
        items.Add(null); // Reserve index 0 for "none/invalid"
    }

    public ushort Register(string key, T item) {
        if (stringToId.ContainsKey(key)) {
            throw new Exception($"Duplicate {typeName} key: {key}");
        }

        ushort id = (ushort)items.Count;
        items.Add(item);
        stringToId[key] = id;

        return id;
    }

    public T Get(ushort id) {
        if (id >= items.Count) return null;
        return items[id];
    }

    public T Get(string key) {
        if (stringToId.TryGetValue(key, out ushort id)) {
            return items[id];
        }
        return null;
    }

    public ushort GetId(string key) {
        return stringToId.TryGetValue(key, out ushort id) ? id : (ushort)0;
    }
}
```

### Game-Specific Registries
```csharp
public class GameRegistries {
    public readonly Registry<Country> Countries = new("Country");
    public readonly Registry<Religion> Religions = new("Religion");
    public readonly Registry<Culture> Cultures = new("Culture");
    public readonly Registry<TradeGood> TradeGoods = new("TradeGood");
    public readonly Registry<Building> Buildings = new("Building");
    public readonly Registry<Technology> Technologies = new("Technology");
    public readonly Registry<Government> Governments = new("Government");
    public readonly Registry<TerrainType> Terrains = new("Terrain");
    public readonly Registry<Unit> Units = new("Unit");

    // Provinces use special handling (see below)
}
```

## ID Mapping Strategy

### String Tags to Runtime IDs
```csharp
// Optional: Type-safe ID wrappers
public struct CountryId {
    public readonly ushort Value;

    public CountryId(ushort value) => Value = value;

    public static readonly CountryId None = new(0);

    public bool IsValid => Value != 0;

    // Implicit conversion for ease of use
    public static implicit operator ushort(CountryId id) => id.Value;
    public static implicit operator CountryId(ushort value) => new(value);
}

// During loading
string countryTag = "ENG";
ushort englandId = Registries.Countries.GetId(countryTag);

// At runtime - no strings!
Province province = GetProvince(1234);
Country owner = Registries.Countries.Get(province.ownerID);
```

**Type-Safe ID Trade-offs:**
- **Pros**: Can't accidentally mix province/country/religion IDs, self-documenting
- **Cons**: Extra type complexity, implicit conversions can confuse debugging, struct wrapping overhead
- **Recommendation**: Use plain `ushort` unless you have >200 entity types and complex cross-referencing

### Province ID Handling (Value Types vs Reference Types)

**Critical difference**: Provinces are value types in `NativeArray`, not reference types in `List`.

```csharp
// ✅ For reference types (Countries, Religions, Buildings, etc.)
public class Registry<T> where T : class {
    private List<T> items = new();  // Managed heap
    private Dictionary<string, ushort> stringToId = new();

    public ushort Register(string key, T item) {
        ushort id = (ushort)items.Count;
        items.Add(item);
        stringToId[key] = id;
        return id;
    }

    public T Get(ushort id) => items[id];
}

// ✅ For provinces (value types in NativeArray)
public class ProvinceSystem {
    private NativeArray<ProvinceState> provinces;  // Unmanaged memory, Burst-compatible
    private Dictionary<int, ushort> definitionToRuntime = new();  // File ID → Runtime ID

    public void Initialize(ProvinceDefinition[] definitions) {
        int count = definitions.Length;
        provinces = new NativeArray<ProvinceState>(count, Allocator.Persistent);

        for (int i = 0; i < count; i++) {
            var def = definitions[i];
            definitionToRuntime[def.id] = (ushort)i;

            // Initialize ProvinceState struct
            provinces[i] = new ProvinceState {
                ownerID = ResolveCountryTag(def.owner),
                terrain = ResolveTerrainType(def.terrain),
                development = def.baseDevelopment,
                // ... other fields
            };
        }
    }

    public ref ProvinceState GetByDefinition(int definitionId) {
        if (definitionToRuntime.TryGetValue(definitionId, out ushort runtimeId)) {
            return ref provinces[runtimeId];
        }
        throw new Exception($"Province {definitionId} not found");
    }

    public ref ProvinceState GetByRuntime(ushort runtimeId) {
        return ref provinces[runtimeId];
    }
}
```

**Why the difference matters:**
- Reference types go in managed collections (`List<Country>`)
- Value types go in unmanaged memory (`NativeArray<ProvinceState>`) for Burst compilation
- Provinces are performance-critical hot data → NativeArray required
- Countries/religions are cold data → managed collections fine

### Sparse vs Dense Province IDs

**Question:** Do you need sparse→dense mapping?

```csharp
// Sparse IDs: 1, 2, 5, 100, 200, 5000, 9999
// Need mapping: Dictionary<int, ushort> definitionToRuntime

// Dense IDs: 1, 2, 3, 4, 5, 6, 7, 8, 9, ...
// Direct indexing: provinces[definitionId]
```

**When sparse→dense mapping is worth it:**
- Province IDs have large gaps (e.g., 1-100, then 5000-6000)
- Wasted memory from gaps exceeds ~10% of total
- Example: Max ID 10000, but only 3000 actual provinces → 70% waste

**When direct indexing is simpler:**
- Province IDs are mostly contiguous (1-3925 with few gaps)
- Wasted memory negligible (~50 gaps × 8 bytes = 400 bytes)
- Simpler code: `provinces[id]` instead of `provinces[mapping[id]]`

**Archon reality:** Most Paradox-style games have nearly contiguous IDs. Unless profiling shows memory issues, direct indexing is simpler.

```csharp
// Simple approach (recommended unless proven bottleneck)
public class ProvinceSystem {
    private NativeArray<ProvinceState> provinces;  // Index = province definition ID

    public void Initialize(ProvinceDefinition[] definitions) {
        int maxId = definitions.Max(p => p.id);
        provinces = new NativeArray<ProvinceState>(maxId + 1, Allocator.Persistent);

        foreach (var def in definitions) {
            provinces[def.id] = CreateProvinceState(def);
        }
    }

    public ref ProvinceState Get(int provinceId) {
        return ref provinces[provinceId];
    }
}
```

## Reference Resolution System

### Raw Data with String References
```csharp
// What we load from files
public class RawProvinceData {
    public int id;
    public string owner;
    public string controller;
    public string religion;
    public string culture;
    public string tradeGood;
    public List<string> buildings;
    public Dictionary<string, string> modifiers;
}

// What we want at runtime (8-byte ENGINE struct - generic primitives only)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProvinceState {
    public ushort ownerID;       // Resolved from "ENG" → 5
    public ushort controllerID;  // Resolved from "FRA" → 12
    public ushort terrainType;   // Resolved from "plains" → 2 (expanded to ushort for 65k terrains)
    public ushort gameDataSlot;  // Index into game-specific data array
}

// Engine cold data stored separately (generic metadata)
public class ProvinceColdData {
    public string name;          // Display name
    public Vector2Int position;  // Map coordinates
    public ushort[] neighbors;   // Adjacent province IDs
}

// GAME LAYER: Hegemon-specific hot data (4 bytes, separate from engine)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HegemonProvinceData {
    public byte development;     // EU4-style development mechanic (GAME-SPECIFIC)
    public byte fortLevel;       // Fortification system (GAME-SPECIFIC)
    public byte unrest;          // Stability mechanic (GAME-SPECIFIC)
    public byte population;      // Population abstraction (GAME-SPECIFIC)
}

// GAME LAYER: Hegemon-specific cold data
public class HegemonProvinceColdData {
    public ushort religion;      // Resolved from "catholic" → 3
    public ushort culture;       // Resolved from "english" → 7
    public ushort tradeGood;     // Resolved from "grain" → 4
    public ushort[] buildings;   // Resolved from ["temple", "marketplace"] → [2, 5]
}
```

### Reference Resolver
```csharp
public class ReferenceResolver {
    private GameRegistries registries;
    private List<Action> deferredResolutions = new();
    private List<string> errors = new();

    public ProvinceState ResolveProvinceHotData(RawProvinceData raw) {
        return new ProvinceState {
            ownerID = ResolveCountryRef(raw.owner, $"Province {raw.id} owner"),
            controllerID = ResolveCountryRef(raw.controller ?? raw.owner, $"Province {raw.id} controller"),
            development = raw.baseDevelopment,
            terrain = ResolveTerrainRef(raw.terrain, $"Province {raw.id} terrain"),
            fortLevel = raw.fortLevel,
            flags = ComputeFlags(raw)
        };
    }

    public ProvinceColdData ResolveProvinceColdData(RawProvinceData raw) {
        return new ProvinceColdData {
            religion = ResolveRef(registries.Religions, raw.religion, $"Province {raw.id} religion"),
            culture = ResolveRef(registries.Cultures, raw.culture, $"Province {raw.id} culture"),
            tradeGood = ResolveRef(registries.TradeGoods, raw.tradeGood, $"Province {raw.id} trade good"),
            buildings = ResolveBuildingList(raw.buildings, raw.id)
        };
    }

    private ushort ResolveCountryRef(string tag, string context) {
        if (string.IsNullOrEmpty(tag)) return 0;

        var id = registries.Countries.GetId(tag);
        if (id == 0) {
            errors.Add($"{context}: Unknown country '{tag}'");
            return 0;
        }

        return id;
    }

    private ushort ResolveRef<T>(Registry<T> registry, string key, string context) where T : class {
        if (string.IsNullOrEmpty(key)) return 0;

        var id = registry.GetId(key);
        if (id == 0) {
            errors.Add($"{context}: Unknown {registry.TypeName} '{key}'");
        }

        return id;
    }

    private ushort[] ResolveBuildingList(List<string> buildingNames, int provinceId) {
        if (buildingNames == null) return Array.Empty<ushort>();

        return buildingNames
            .Select(b => ResolveRef(registries.Buildings, b, $"Province {provinceId} building"))
            .Where(id => id != 0)
            .ToArray();
    }

    public void DeferResolution(Action resolution) {
        // Some references might need to be resolved after everything is loaded
        deferredResolutions.Add(resolution);
    }

    public void ResolveDeferredReferences() {
        foreach (var resolution in deferredResolutions) {
            resolution();
        }
    }

    public bool HasErrors() => errors.Count > 0;
    public IReadOnlyList<string> GetErrors() => errors;
}
```

## Cross-Reference System

### Bidirectional References
After loading, build reverse lookups for performance (see [data-flow-architecture.md](data-flow-architecture.md) for why this is important).

```csharp
public class CrossReferenceBuilder {
    // Build reverse lookups after loading
    public void BuildCountryProvinces(GameData data) {
        // Create reverse index: Country → Provinces
        var provincesByOwner = new List<ushort>[data.Countries.Count];
        for (int i = 0; i < provincesByOwner.Length; i++) {
            provincesByOwner[i] = new List<ushort>();
        }

        // Populate from provinces
        for (ushort i = 0; i < data.Provinces.Count; i++) {
            var province = data.Provinces.Get(i);
            if (province.ownerID != 0) {
                provincesByOwner[province.ownerID].Add(i);
            }
        }

        // Store in ProvinceSystem
        data.Provinces.SetProvinceLists(provincesByOwner);
    }

    public void BuildCultureGroups(GameData data) {
        // Group cultures by culture group
        foreach (var culture in data.Cultures.GetAll()) {
            var group = data.CultureGroups.Get(culture.groupId);
            if (group != null) {
                group.cultures.Add(culture.id);
            }
        }
    }

    public void BuildTradeNodes(GameData data) {
        // Link provinces to trade nodes
        var provincesByTradeNode = new Dictionary<ushort, List<ushort>>();

        for (ushort i = 0; i < data.Provinces.Count; i++) {
            var coldData = data.Provinces.GetColdData(i);
            if (coldData?.tradeNodeId != 0) {
                if (!provincesByTradeNode.ContainsKey(coldData.tradeNodeId)) {
                    provincesByTradeNode[coldData.tradeNodeId] = new List<ushort>();
                }
                provincesByTradeNode[coldData.tradeNodeId].Add(i);
            }
        }

        // Store in TradeSystem
        data.TradeSystem.SetProvincesByNode(provincesByTradeNode);
    }
}
```

## Validation System

### Data Integrity Checker
```csharp
public class DataValidator {
    private List<ValidationError> errors = new();
    private List<ValidationWarning> warnings = new();

    public bool ValidateGameData(GameData data) {
        ValidateCountries(data);
        ValidateProvinces(data);
        ValidateTechnologies(data);
        ValidateTradeNetwork(data);

        if (errors.Count > 0) {
            LogErrors();
            return false;
        }

        if (warnings.Count > 0) {
            LogWarnings();
        }

        return true;
    }

    private void ValidateProvinces(GameData data) {
        for (ushort i = 0; i < data.Provinces.Count; i++) {
            var province = data.Provinces.Get(i);

            // Every owned province must have valid owner
            if (province.ownerID != 0) {
                var owner = data.Countries.Get(province.ownerID);
                if (owner == null) {
                    AddError($"Province {i} has invalid owner {province.ownerID}");
                }
            }

            // Controller must be valid if different from owner
            if (province.controllerID != province.ownerID) {
                var controller = data.Countries.Get(province.controllerID);
                if (controller == null) {
                    AddError($"Province {i} has invalid controller {province.controllerID}");
                }

                // Controller different from owner implies war
                if (!data.Diplomacy.IsAtWar(province.ownerID, province.controllerID)) {
                    AddWarning($"Province {i} controlled by {province.controllerID} but no war with {province.ownerID}");
                }
            }

            // Validate terrain type
            if (province.terrain == 0 || data.Terrains.Get(province.terrain) == null) {
                AddError($"Province {i} has invalid terrain {province.terrain}");
            }
        }
    }

    private void ValidateCountries(GameData data) {
        foreach (var country in data.Countries.GetAll()) {
            if (country == null) continue;

            // Capital must be owned
            if (country.capital != 0) {
                var capital = data.Provinces.Get(country.capital);
                if (capital.ownerID != country.id) {
                    AddError($"Country {country.tag} capital not owned");
                }
            }

            // Technology group must exist
            if (country.techGroup == 0 || data.TechGroups.Get(country.techGroup) == null) {
                AddError($"Country {country.tag} has invalid tech group {country.techGroup}");
            }

            // Must own at least one province
            var ownedProvinces = data.Provinces.GetNationProvinces(country.id);
            if (ownedProvinces.Count == 0) {
                AddWarning($"Country {country.tag} owns no provinces");
            }
        }
    }

    private void AddError(string message) => errors.Add(new ValidationError(message));
    private void AddWarning(string message) => warnings.Add(new ValidationWarning(message));
}
```

## Loading Pipeline Implementation

### Complete Loading Process
```csharp
public class GameDataLoader {
    private GameRegistries registries = new();
    private ReferenceResolver resolver;
    private CrossReferenceBuilder crossRef = new();
    private DataValidator validator = new();

    public GameData LoadGame(string dataPath) {
        var gameData = new GameData();

        try {
            // Phase 1: Load static data (no dependencies)
            LoadStaticData(dataPath);

            // Phase 2: Register all entities
            RegisterEntities(dataPath);

            // Phase 3: Load entity data with string refs
            var rawData = LoadRawData(dataPath);

            // Phase 4: Resolve all references to IDs
            ResolveReferences(rawData, gameData);

            // Phase 5: Build cross-references (bidirectional mappings)
            BuildCrossReferences(gameData);

            // Phase 6: Validate data integrity
            if (!validator.ValidateGameData(gameData)) {
                throw new Exception("Data validation failed");
            }

            // Phase 7: Optimize for runtime
            OptimizeForRuntime(gameData);

            return gameData;
        }
        catch (Exception e) {
            Debug.LogError($"Failed to load game data: {e.Message}");
            throw;
        }
    }

    private void LoadStaticData(string dataPath) {
        // Load data that doesn't reference other data
        LoadReligions($"{dataPath}/common/religions");
        LoadCultures($"{dataPath}/common/cultures");
        LoadTradeGoods($"{dataPath}/common/trade_goods");
        LoadBuildings($"{dataPath}/common/buildings");
        LoadTerrains($"{dataPath}/map/terrain.txt");
    }

    private void RegisterEntities(string dataPath) {
        // First pass - just register IDs
        var provinceFiles = Directory.GetFiles($"{dataPath}/history/provinces", "*.txt");
        var countryFiles = Directory.GetFiles($"{dataPath}/history/countries", "*.txt");

        // Register provinces
        foreach (var file in provinceFiles) {
            var id = ExtractProvinceId(file);  // "123 - London.txt" → 123
            // Provinces will be allocated directly by ID
        }

        // Register countries
        foreach (var file in countryFiles) {
            var tag = ExtractCountryTag(file);  // "ENG - England.txt" → "ENG"
            registries.Countries.Register(tag, new Country { tag = tag });
        }
    }

    private RawGameData LoadRawData(string dataPath) {
        var raw = new RawGameData();

        // Load province files with string references intact
        foreach (var file in Directory.GetFiles($"{dataPath}/history/provinces", "*.txt")) {
            var provinceData = ParseProvinceFile(file);
            raw.provinces.Add(provinceData);
        }

        // Load country files
        foreach (var file in Directory.GetFiles($"{dataPath}/history/countries", "*.txt")) {
            var countryData = ParseCountryFile(file);
            raw.countries.Add(countryData);
        }

        return raw;
    }

    private void ResolveReferences(RawGameData raw, GameData gameData) {
        resolver = new ReferenceResolver(registries);

        // Resolve provinces
        int maxProvinceId = raw.provinces.Max(p => p.id);
        gameData.Provinces.Initialize(maxProvinceId + 1);

        foreach (var rawProvince in raw.provinces) {
            // Resolve to 8-byte struct
            var state = resolver.ResolveProvinceHotData(rawProvince);
            gameData.Provinces.Set(rawProvince.id, state);

            // Resolve cold data separately
            var coldData = resolver.ResolveProvinceColdData(rawProvince);
            gameData.Provinces.SetColdData(rawProvince.id, coldData);
        }

        // Resolve countries
        foreach (var rawCountry in raw.countries) {
            var country = resolver.ResolveCountry(rawCountry);
            registries.Countries.Set(rawCountry.tag, country);
        }

        // Resolve deferred references
        resolver.ResolveDeferredReferences();

        // Check for errors
        if (resolver.HasErrors()) {
            foreach (var error in resolver.GetErrors()) {
                Debug.LogError(error);
            }
            throw new Exception("Reference resolution failed");
        }
    }

    private void BuildCrossReferences(GameData gameData) {
        crossRef.BuildCountryProvinces(gameData);    // Country → Provinces
        crossRef.BuildCultureGroups(gameData);       // CultureGroup → Cultures
        crossRef.BuildTradeNodes(gameData);          // TradeNode → Provinces
    }

    private void OptimizeForRuntime(GameData gameData) {
        // Trim excess capacity from dynamic lists
        gameData.Provinces.TrimExcess();

        // Pre-calculate frequently-used values
        gameData.Provinces.PreCalculateNeighborCounts();

        // Warm up caches
        gameData.Economy.WarmUpCache();
    }
}
```

## Optimization Strategies

### String Interning
```csharp
public class StringInterner {
    private Dictionary<string, string> interned = new();

    public string Intern(string str) {
        if (string.IsNullOrEmpty(str)) return str;

        if (!interned.TryGetValue(str, out string result)) {
            interned[str] = str;
            result = str;
        }

        return result;
    }

    // Use during loading to reduce memory for repeated strings
    public void InternAllStrings(RawCountryData data) {
        data.name = Intern(data.name);
        data.adjective = Intern(data.adjective);
        data.governmentType = Intern(data.governmentType);
        // Many countries share government types, religions, etc.
    }
}
```

### Compile-Time ID Generation (Optional)
For frequently-used constants, generate at build time from data files.

```csharp
// Auto-generated from countries.txt at build time
public static class CountryIds {
    public const ushort ENG = 1;
    public const ushort FRA = 2;
    public const ushort SPA = 3;
    public const ushort HRE = 4;
    // ...
}

public static class ReligionIds {
    public const ushort Catholic = 1;
    public const ushort Protestant = 2;
    public const ushort Orthodox = 3;
    // ...
}

// Usage in code (compile-time constants)
if (province.ownerID == CountryIds.ENG) {
    // Type-safe, no string comparison, no runtime lookup
}
```

**Trade-off:** Requires build-time code generation, but eliminates runtime lookups for common cases.

### Lazy Loading for Cold Data
```csharp
public class LazyLoader<T> where T : class {
    private readonly Func<T> loader;
    private T data;
    private bool loaded;

    public LazyLoader(Func<T> loader) {
        this.loader = loader;
    }

    public T Get() {
        if (!loaded) {
            data = loader();
            loaded = true;
        }
        return data;
    }
}

// Use for rarely accessed data
public class Country {
    public ushort id;
    public string tag;

    // Frequently accessed
    public ushort capital;
    public List<ushort> ownedProvinces;

    // Rarely accessed - lazy load
    private LazyLoader<CountryHistory> historyLoader;

    public CountryHistory History => historyLoader.Get();
}
```

## Error Handling

### Missing Reference Strategies
```csharp
public enum MissingReferenceStrategy {
    ThrowException,     // Fail fast (use for critical refs like countries)
    LogWarning,         // Continue with warning (use for optional refs)
    UseDefault,         // Silent fallback (use for cosmetic refs)
    CreatePlaceholder   // Generate missing entity (use for modding)
}

public class ReferenceConfig {
    public MissingReferenceStrategy CountryStrategy = MissingReferenceStrategy.ThrowException;
    public MissingReferenceStrategy ReligionStrategy = MissingReferenceStrategy.UseDefault;
    public MissingReferenceStrategy BuildingStrategy = MissingReferenceStrategy.LogWarning;

    public ushort HandleMissing<T>(string key, Registry<T> registry) where T : class {
        var strategy = GetStrategy<T>();

        switch (strategy) {
            case MissingReferenceStrategy.ThrowException:
                throw new Exception($"Missing {typeof(T).Name}: {key}");

            case MissingReferenceStrategy.LogWarning:
                Debug.LogWarning($"Missing {typeof(T).Name}: {key}, using default");
                return registry.GetDefault();

            case MissingReferenceStrategy.UseDefault:
                return registry.GetDefault();

            case MissingReferenceStrategy.CreatePlaceholder:
                var placeholder = CreatePlaceholder<T>(key);
                return registry.Register(key, placeholder);

            default:
                return 0;
        }
    }
}
```

## Performance Considerations

### Memory Layout

```csharp
// ENGINE: 8-byte hot struct (generic primitives only)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProvinceState {
    public ushort ownerID;       // 2 bytes
    public ushort controllerID;  // 2 bytes
    public ushort terrainType;   // 2 bytes (expanded for 65k terrains)
    public ushort gameDataSlot;  // 2 bytes (index into game data)
}
NativeArray<ProvinceState> provinces;  // 8 bytes × 10k = 80KB

// GAME: 4-byte hot struct (Hegemon-specific)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HegemonProvinceData {
    public byte development;     // 1 byte (EU4-style mechanic)
    public byte fortLevel;       // 1 byte (fortification system)
    public byte unrest;          // 1 byte (stability)
    public byte population;      // 1 byte (abstract population)
}
NativeArray<HegemonProvinceData> hegemonData;  // 4 bytes × 10k = 40KB

// Engine cold data: Separate storage (accessed rarely)
Dictionary<ushort, ProvinceColdData> coldData;

// Game cold data: Separate storage
Dictionary<ushort, HegemonProvinceColdData> hegemonColdData;
```

See [performance-architecture-guide.md](performance-architecture-guide.md) for why we keep the 8-byte struct together.

### Lookup Performance
```csharp
public class PerformantLookups {
    // O(1) array lookup for runtime IDs
    public ref ProvinceState GetProvince(ushort id) {
        return ref provinces[id];  // Direct array access
    }

    // O(1) dictionary lookup for string tags (only during loading)
    public ushort GetCountryId(string tag) {
        return countryTags[tag];  // Hash table lookup
    }

    // ❌ Avoid this at runtime!
    public ushort GetProvinceByName(string name) {
        // O(n) search - SLOW! Only use during loading/debugging
        for (ushort i = 0; i < provinces.Length; i++) {
            var coldData = GetColdData(i);
            if (coldData.name == name) return i;
        }
        return 0;
    }
}
```

## Burst Compatibility

### Ensuring Burst Can Compile Hot Path

```csharp
// ✅ Burst-compatible: Value type struct in NativeArray
[BurstCompile]
struct ProcessProvincesJob : IJobParallelFor {
    [ReadOnly] public NativeArray<ProvinceState> provinces;
    public NativeArray<FixedPoint64> incomes;

    public void Execute(int index) {
        var province = provinces[index];
        incomes[index] = CalculateIncome(province.development, province.terrain);
    }
}

// ❌ NOT Burst-compatible: Managed references
struct BadJob : IJobParallelFor {
    public List<Province> provinces;  // Can't use managed collections in Burst
    public Dictionary<int, float> data;  // Can't use managed collections in Burst
}
```

**Key principle:** Hot data (ProvinceState) must be in NativeArray for Burst. Cold data (ProvinceColdData) can use managed collections since it's accessed rarely.

## Usage Examples

### Loading Process
```csharp
public class GameInitializer {
    public void InitializeGame() {
        // Load all game data
        var loader = new GameDataLoader();
        var gameData = loader.LoadGame("Assets/GameData");

        // Now everything is linked with IDs
        var englandId = gameData.Countries.GetId("ENG");
        var london = gameData.Provinces.Get(236);

        Debug.Assert(london.ownerID == englandId);

        // Runtime uses IDs only
        ProcessProvince(236);
        UpdateCountry(englandId);
    }

    private void ProcessProvince(ushort provinceId) {
        // Fast array lookup (8-byte struct)
        ref var province = ref provinces[provinceId];
        var owner = province.ownerID;
        var development = province.development;

        // No string comparisons at runtime!
    }
}
```

### Modding Support
```csharp
public class ModLoader {
    public void LoadMod(string modPath, GameRegistries registries) {
        // Mods can add new entities
        var modReligions = LoadReligions($"{modPath}/common/religions");
        foreach (var religion in modReligions) {
            if (!registries.Religions.Exists(religion.key)) {
                // New religion
                registries.Religions.Register(religion.key, religion.value);
            } else {
                // Mod overwrites base game
                registries.Religions.Replace(religion.key, religion.value);
            }
        }

        // Re-run reference resolution for mod provinces
        var modProvinces = LoadProvinces($"{modPath}/history/provinces");
        foreach (var province in modProvinces) {
            ResolveAndUpdateProvince(province);
        }

        // Re-validate after mod loading
        validator.ValidateGameData(gameData);
    }
}
```

## Best Practices

1. **Never use strings at runtime** - Convert everything to IDs during loading
2. **Validate early and often** - Catch bad references during loading, not gameplay
3. **Use ushort for IDs** - Simpler than typed wrappers unless you have complex cross-referencing
4. **Dense arrays over dictionaries** - Array indexing is much faster (unless IDs are very sparse)
5. **Separate hot/cold data** - 8-byte hot struct in NativeArray, cold data in Dictionary
6. **Build reverse lookups once** - Don't search arrays repeatedly (O(n) → O(1))
7. **Reserve 0 for "none"** - Makes checking for unset values easy
8. **Log all resolution failures** - Help modders debug their data
9. **Support partial loading** - Allow game to run with some missing optional data
10. **Keep hot data Burst-compatible** - NativeArray with value types only

## Summary

This linking architecture ensures:
- **Type safety** through ID-based references
- **Performance** through array indexing instead of string lookups
- **Validation** catches all bad references at load time
- **Flexibility** for mods to extend base game data
- **Memory efficiency** through 8-byte structs and separate cold data
- **Burst compatibility** through NativeArray and value types
- **Clear error messages** for debugging data issues

The key is the three-phase approach: discover entities, load raw data with strings, then resolve strings to IDs. This allows you to handle forward references, validate everything, and convert to efficient runtime representations.

## Related Documents

- [data-flow-architecture.md](data-flow-architecture.md) - System communication and bidirectional mappings
- [performance-architecture-guide.md](performance-architecture-guide.md) - Memory layout and cache optimization
- [../Planning/modding-design.md](../Planning/modding-design.md) - Mod system uses same reference resolution *(not implemented)*

---

*Last Updated: 2025-09-30*
*For questions or updates, see master-architecture-document.md*
