# Grand Strategy Game - Data Linking & Reference Resolution Architecture

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
    
    // Special registries with known IDs
    public readonly ProvinceRegistry Provinces = new();
}
```

## ID Mapping Strategy

### String Tags to Runtime IDs
```csharp
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
CountryId englandId = Registries.Countries.GetId(countryTag);

// At runtime - no strings!
Province province = GetProvince(1234);
Country owner = Registries.Countries.Get(province.OwnerId);
```

### Province ID Handling (Special Case)
Provinces use numeric IDs, but still need registration:

```csharp
public class ProvinceRegistry {
    private Province[] provinces;
    private Dictionary<int, ushort> definitionToRuntime = new();
    
    public void Initialize(int maxProvinceId) {
        // Province IDs from files might be sparse (1, 2, 5, 100, 101...)
        // We want dense runtime IDs for array efficiency
        provinces = new Province[maxProvinceId + 1];
    }
    
    public ushort Register(int definitionId, Province province) {
        // Convert sparse file IDs to dense runtime IDs
        ushort runtimeId = (ushort)definitionToRuntime.Count;
        definitionToRuntime[definitionId] = runtimeId;
        provinces[runtimeId] = province;
        province.RuntimeId = runtimeId;
        province.DefinitionId = definitionId;
        return runtimeId;
    }
    
    public Province GetByDefinition(int definitionId) {
        if (definitionToRuntime.TryGetValue(definitionId, out ushort runtimeId)) {
            return provinces[runtimeId];
        }
        return null;
    }
    
    public Province GetByRuntime(ushort runtimeId) {
        return provinces[runtimeId];
    }
}
```

## Reference Resolution System

### Raw Data with String References
```csharp
// What we load from files
public class RawProvinceData {
    public int Id;
    public string Owner;
    public string Controller;
    public string Religion;
    public string Culture;
    public string TradeGood;
    public List<string> Buildings;
    public Dictionary<string, float> Modifiers;
}

// What we want at runtime
public class Province {
    public ushort RuntimeId;
    public int DefinitionId;
    public CountryId Owner;
    public CountryId Controller;
    public ushort Religion;
    public ushort Culture;
    public ushort TradeGood;
    public ushort[] Buildings;
    public ModifierSet Modifiers;
}
```

### Reference Resolver
```csharp
public class ReferenceResolver {
    private GameRegistries registries;
    private List<Action> deferredResolutions = new();
    private List<string> errors = new();
    
    public void ResolveProvince(RawProvinceData raw, Province province) {
        // Resolve country references
        province.Owner = ResolveCountryRef(raw.Owner, $"Province {raw.Id} owner");
        province.Controller = ResolveCountryRef(raw.Controller ?? raw.Owner, $"Province {raw.Id} controller");
        
        // Resolve other references
        province.Religion = ResolveRef(registries.Religions, raw.Religion, $"Province {raw.Id} religion");
        province.Culture = ResolveRef(registries.Cultures, raw.Culture, $"Province {raw.Id} culture");
        province.TradeGood = ResolveRef(registries.TradeGoods, raw.TradeGood, $"Province {raw.Id} trade good");
        
        // Resolve building list
        if (raw.Buildings != null) {
            province.Buildings = raw.Buildings
                .Select(b => ResolveRef(registries.Buildings, b, $"Province {raw.Id} building"))
                .Where(id => id != 0)
                .ToArray();
        }
    }
    
    private CountryId ResolveCountryRef(string tag, string context) {
        if (string.IsNullOrEmpty(tag)) return CountryId.None;
        
        var id = registries.Countries.GetId(tag);
        if (id == 0) {
            errors.Add($"{context}: Unknown country '{tag}'");
            return CountryId.None;
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
    
    public void DeferResolution(Action resolution) {
        // Some references might need to be resolved after everything is loaded
        deferredResolutions.Add(resolution);
    }
    
    public void ResolveDeferredReferences() {
        foreach (var resolution in deferredResolutions) {
            resolution();
        }
    }
}
```

## Cross-Reference System

### Bidirectional References
```csharp
public class CrossReferenceBuilder {
    // Build reverse lookups after loading
    public void BuildCountryProvinces(GameData data) {
        // Clear any existing data
        foreach (var country in data.Countries.GetAll()) {
            country.OwnedProvinces.Clear();
            country.ControlledProvinces.Clear();
        }
        
        // Build province lists for countries
        foreach (var province in data.Provinces.GetAll()) {
            if (province.Owner.IsValid) {
                var owner = data.Countries.Get(province.Owner);
                owner.OwnedProvinces.Add(province.RuntimeId);
            }
            
            if (province.Controller.IsValid) {
                var controller = data.Countries.Get(province.Controller);
                controller.ControlledProvinces.Add(province.RuntimeId);
            }
        }
    }
    
    public void BuildCultureGroups(GameData data) {
        // Group cultures by culture group
        foreach (var culture in data.Cultures.GetAll()) {
            var group = data.CultureGroups.Get(culture.GroupId);
            if (group != null) {
                group.Cultures.Add(culture.Id);
            }
        }
    }
    
    public void BuildTradeNodes(GameData data) {
        // Link provinces to trade nodes
        foreach (var province in data.Provinces.GetAll()) {
            if (province.TradeNodeId != 0) {
                var node = data.TradeNodes.Get(province.TradeNodeId);
                node.Provinces.Add(province.RuntimeId);
            }
        }
    }
}
```

## Validation System

### Data Integrity Checker
```csharp
public class DataValidator {
    private List<ValidationError> errors = new();
    
    public bool ValidateGameData(GameData data) {
        ValidateCountries(data);
        ValidateProvinces(data);
        ValidateTechnologies(data);
        ValidateTradeNetwork(data);
        
        if (errors.Count > 0) {
            LogErrors();
            return false;
        }
        
        return true;
    }
    
    private void ValidateProvinces(GameData data) {
        foreach (var province in data.Provinces.GetAll()) {
            // Every owned province must have valid owner
            if (province.Owner != 0) {
                var owner = data.Countries.Get(province.Owner);
                if (owner == null) {
                    AddError($"Province {province.DefinitionId} has invalid owner {province.Owner}");
                }
            }
            
            // Controller must be valid if different from owner
            if (province.Controller != province.Owner) {
                var controller = data.Countries.Get(province.Controller);
                if (controller == null) {
                    AddError($"Province {province.DefinitionId} has invalid controller");
                }
                
                // Controller different from owner implies war
                if (!IsAtWar(province.Owner, province.Controller)) {
                    AddWarning($"Province {province.DefinitionId} controlled by {province.Controller} but no war with {province.Owner}");
                }
            }
            
            // Validate religion exists
            if (province.Religion != 0 && data.Religions.Get(province.Religion) == null) {
                AddError($"Province {province.DefinitionId} has invalid religion");
            }
        }
    }
    
    private void ValidateCountries(GameData data) {
        foreach (var country in data.Countries.GetAll()) {
            // Capital must be owned
            if (country.Capital != 0) {
                var capital = data.Provinces.GetByRuntime(country.Capital);
                if (capital == null || capital.Owner != country.Id) {
                    AddError($"Country {country.Tag} capital not owned");
                }
            }
            
            // Technology group must exist
            if (!data.TechGroups.Exists(country.TechGroup)) {
                AddError($"Country {country.Tag} has invalid tech group");
            }
        }
    }
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
            LoadEntityData(dataPath);
            
            // Phase 4: Resolve all references
            ResolveReferences();
            
            // Phase 5: Build cross-references
            BuildCrossReferences(gameData);
            
            // Phase 6: Validate
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
        foreach (var file in Directory.GetFiles($"{dataPath}/history/provinces")) {
            var provinceId = ExtractProvinceId(file);
            registries.Provinces.Reserve(provinceId);
        }
        
        foreach (var file in Directory.GetFiles($"{dataPath}/history/countries")) {
            var tag = ExtractCountryTag(file);
            registries.Countries.Register(tag, null);
        }
    }
    
    private void LoadEntityData(string dataPath) {
        // Second pass - load actual data
        var provinceLoader = new ProvinceLoader(registries);
        foreach (var file in Directory.GetFiles($"{dataPath}/history/provinces")) {
            var raw = ParseProvinceFile(file);
            var province = new Province();
            provinceLoader.Load(raw, province);
            registries.Provinces.Set(raw.Id, province);
        }
        
        var countryLoader = new CountryLoader(registries);
        foreach (var file in Directory.GetFiles($"{dataPath}/history/countries")) {
            var raw = ParseCountryFile(file);
            var country = new Country();
            countryLoader.Load(raw, country);
            registries.Countries.Set(raw.Tag, country);
        }
    }
    
    private void ResolveReferences() {
        resolver = new ReferenceResolver(registries);
        
        // Resolve all province references
        foreach (var province in registries.Provinces.GetAll()) {
            resolver.ResolveProvince(province);
        }
        
        // Resolve all country references
        foreach (var country in registries.Countries.GetAll()) {
            resolver.ResolveCountry(country);
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
    
    // Use during loading to reduce memory
    public void InternAllStrings(RawData data) {
        data.Name = Intern(data.Name);
        data.Description = Intern(data.Description);
        // ... intern all string fields
    }
}
```

### Compile-Time ID Generation
```csharp
// Generate at build time from data files
public static class GeneratedIds {
    // Countries
    public const ushort ENG = 1;
    public const ushort FRA = 2;
    public const ushort SPA = 3;
    
    // Religions  
    public const ushort Catholic = 1;
    public const ushort Protestant = 2;
    public const ushort Orthodox = 3;
    
    // Trade Goods
    public const ushort Grain = 1;
    public const ushort Wine = 2;
    public const ushort Wool = 3;
}

// Usage in code
province.Owner = GeneratedIds.ENG;  // Type-safe!
province.Religion = GeneratedIds.Catholic;
```

### Lazy Loading
```csharp
public class LazyLoader<T> where T : class {
    private readonly string path;
    private T data;
    private bool loaded;
    
    public LazyLoader(string path) {
        this.path = path;
    }
    
    public T Get() {
        if (!loaded) {
            data = LoadFromDisk(path);
            loaded = true;
        }
        return data;
    }
}

// Use for rarely accessed data
public class Country {
    public ushort Id;
    public string Tag;
    
    // Frequently accessed
    public ushort Capital;
    public List<ushort> OwnedProvinces;
    
    // Rarely accessed - lazy load
    private LazyLoader<CountryHistory> history;
    
    public CountryHistory History => history.Get();
}
```

## Error Handling

### Missing Reference Strategies
```csharp
public enum MissingReferenceStrategy {
    ThrowException,     // Fail fast
    LogWarning,         // Continue with warning
    UseDefault,         // Silent fallback
    CreatePlaceholder   // Generate missing entity
}

public class ReferenceConfig {
    public MissingReferenceStrategy CountryStrategy = MissingReferenceStrategy.ThrowException;
    public MissingReferenceStrategy ReligionStrategy = MissingReferenceStrategy.UseDefault;
    public MissingReferenceStrategy BuildingStrategy = MissingReferenceStrategy.LogWarning;
    
    public ushort HandleMissing<T>(string key, Registry<T> registry) where T : class {
        switch (GetStrategy<T>()) {
            case MissingReferenceStrategy.ThrowException:
                throw new Exception($"Missing {typeof(T).Name}: {key}");
                
            case MissingReferenceStrategy.LogWarning:
                Debug.LogWarning($"Missing {typeof(T).Name}: {key}");
                return 0;
                
            case MissingReferenceStrategy.UseDefault:
                return registry.GetDefault();
                
            case MissingReferenceStrategy.CreatePlaceholder:
                var placeholder = CreatePlaceholder<T>(key);
                return registry.Register(key, placeholder);
        }
    }
}
```

## Performance Considerations

### Memory Layout
```csharp
// Pack related data together for cache efficiency
public struct ProvinceData {
    // Hot data - accessed frequently
    public ushort Owner;
    public ushort Controller;
    public ushort Development;
    
    // Cold data - accessed rarely
    public ushort Religion;
    public ushort Culture;
    public ushort TradeGood;
}

// Separate arrays for better cache usage
public class ProvinceSystem {
    // Hot path - iterated frequently
    private ushort[] owners;
    private ushort[] controllers;
    
    // Cold path - accessed occasionally
    private ushort[] religions;
    private ushort[] cultures;
}
```

### Lookup Performance
```csharp
public class PerformantLookups {
    // O(1) array lookup for runtime IDs
    public Province GetProvince(ushort id) {
        return provinces[id];  // Direct array access
    }
    
    // O(1) dictionary lookup for string tags (only during loading)
    public ushort GetCountryId(string tag) {
        return countryTags[tag];  // Hash table lookup
    }
    
    // Avoid this at runtime!
    public Province GetProvinceByName(string name) {
        // O(n) search - SLOW!
        return provinces.FirstOrDefault(p => p.Name == name);
    }
}
```

## Usage Examples

### Loading Process
```csharp
public class GameInitializer {
    public void InitializeGame() {
        // Load all game data
        var loader = new GameDataLoader();
        var gameData = loader.LoadGame("Assets/GameData");
        
        // Now everything is linked with IDs
        var england = gameData.Countries.Get("ENG");
        var london = gameData.Provinces.GetByDefinition(236);
        
        Debug.Assert(london.Owner == england.Id);
        Debug.Assert(england.Capital == london.RuntimeId);
        
        // Runtime uses IDs only
        ProcessProvince(london.RuntimeId);
        UpdateCountry(england.Id);
    }
    
    private void ProcessProvince(ushort provinceId) {
        // Fast array lookups
        var owner = owners[provinceId];
        var development = developments[provinceId];
        
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
            if (!registries.Religions.Exists(religion.Key)) {
                registries.Religions.Register(religion.Key, religion.Value);
            } else {
                // Mod overwrites base game
                registries.Religions.Replace(religion.Key, religion.Value);
            }
        }
    }
}
```

## Best Practices

1. **Never use strings at runtime** - Convert everything to IDs during loading
2. **Validate early and often** - Catch bad references during loading, not gameplay
3. **Use typed IDs** - CountryId instead of ushort for type safety
4. **Dense arrays over dictionaries** - Array indexing is much faster
5. **Separate hot/cold data** - Don't load what you don't need
6. **Build reverse lookups once** - Don't search arrays repeatedly
7. **Reserve 0 for "none"** - Makes checking for unset values easy
8. **Log all resolution failures** - Help modders debug their data
9. **Support partial loading** - Allow game to run with some missing data
10. **Generate compile-time constants** - For commonly used IDs

## Summary

This linking architecture ensures:
- **Type safety** through typed ID structs
- **Performance** through array indexing instead of string lookups
- **Validation** catches all bad references at load time
- **Flexibility** for mods to extend base game data
- **Memory efficiency** through ID usage instead of string references
- **Clear error messages** for debugging data issues

The key is the three-phase approach: discover, load, then link. This allows you to handle forward references, validate everything, and convert to efficient runtime representations.