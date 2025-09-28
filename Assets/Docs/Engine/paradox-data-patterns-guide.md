# Grand Strategy Game - Paradox Data Patterns & Generic Loading Guide

## Executive Summary
**Insight**: Paradox data files follow ~7 distinct patterns regardless of data type  
**Solution**: Create generic loaders per pattern, specialize only where needed  
**Result**: 80% code reuse, consistent loading behavior, easier maintenance  
**Key Principle**: Parse once (syntax), interpret by pattern (structure), specialize by type (semantics)

## The Seven Paradox Data Patterns

### Pattern 1: Hierarchical Nested
**Structure**: Parent contains children in single file
```
parent_group = {
    parent_property = value
    
    child_entity = {
        child_property = value
    }
    
    another_child = {
        child_property = value
    }
}
```

**Examples**:
- Culture groups → Cultures
- Religion groups → Religions  
- Technology groups → Technologies
- Region → Areas → Provinces
- Trade company → Trade provinces

**Key Characteristics**:
- Parent and children defined together
- Children inherit some parent properties
- Bidirectional relationships needed
- Single file contains entire hierarchy

---

### Pattern 2: Manifest/Reference
**Structure**: Index file points to actual data files
```
KEY = "path/to/actual/data.txt"
TAG = "relative/path/to/file.txt"
```

**Examples**:
- country_tags → country files
- bookmarks → bookmark definitions
- decisions_index → decision files
- disasters_index → disaster files

**Key Characteristics**:
- Two-phase loading (manifest then data)
- Enables modular organization
- Supports easy modding/overrides
- Separates existence from definition

---

### Pattern 3: Flat Entity List
**Structure**: Simple list of independent entities
```
entity_1 = {
    property_a = value
    property_b = value
}

entity_2 = {
    property_a = value
    property_b = value
}
```

**Examples**:
- Trade goods
- Buildings
- Unit types
- Terrain types
- Climate types
- Advisors

**Key Characteristics**:
- No hierarchy
- Each entity is independent
- All entities in one file typically
- Simple registration process

---

### Pattern 4: Temporal/Historical
**Structure**: Date-keyed changes to entity state
```
entity = {
    # Base state
    property = value
    
    1444.11.11 = {
        property = new_value
        owner = TAG
    }
    
    1500.1.1 = {
        property = another_value
        add_core = TAG
    }
}
```

**Examples**:
- Province history
- Country history
- Monarch/heir history
- War history

**Key Characteristics**:
- Timeline of changes
- Applied based on start date
- Cumulative changes
- Can reference other entities

---

### Pattern 5: Conditional/Rule-Based
**Structure**: Triggers, conditions, and effects
```
rule_entity = {
    trigger = {
        condition_1 = value
        AND = {
            condition_2 = value
            condition_3 = value
        }
    }
    
    effect = {
        action_1 = value
        action_2 = value
    }
    
    weight = {
        base = 100
        modifier = {
            factor = 2
            condition = value
        }
    }
}
```

**Examples**:
- Events
- Decisions
- Missions
- Disasters
- Policies
- Peace treaties

**Key Characteristics**:
- Complex condition trees
- Effect lists
- Weight/probability modifiers
- Often has localization references

---

### Pattern 6: Weighted/Probability Lists
**Structure**: Items with weights or probabilities
```
list_name = {
    10 = "common_item"
    10 = "another_common"
    5 = "uncommon_item"
    1 = "rare_item"
    0.1 = "legendary_item"
}
```

**Examples**:
- Random leader names
- Dynasty names
- Random events selection
- AI personalities
- Trade good distribution

**Key Characteristics**:
- Items with numeric weights
- Used for random selection
- Weights can be absolute or relative
- Often culture/region specific

---

### Pattern 7: Modifier Collections
**Structure**: Named sets of modifiers/effects
```
modifier_set = {
    modifier_1 = value
    modifier_2 = value
    
    trigger = {  # Optional
        condition = value
    }
    
    duration = days  # Optional
}
```

**Examples**:
- Idea groups
- Government reforms
- Static modifiers
- Triggered modifiers
- Estate privileges
- Age abilities

**Key Characteristics**:
- Collection of effects
- May have triggers
- Can be temporary or permanent
- Often referenced by other systems

---

## Generic Loader Architecture

### Base Pattern Loaders

```csharp
// Abstract base for all pattern loaders
public abstract class PatternLoader<T> where T : class, new() {
    protected readonly ParadoxParser parser;
    protected readonly IRegistry<T> registry;
    
    public abstract void Load(string filePath);
    public abstract void ResolveReferences();
    public abstract void Validate();
    
    protected virtual void OnLoadComplete() { }
}
```

### Pattern 1: Generic Hierarchical Loader

```csharp
public class HierarchicalLoader<TParent, TChild> : PatternLoader<TParent>
    where TParent : class, IHierarchicalParent, new()
    where TChild : class, IHierarchicalChild, new() {
    
    protected IRegistry<TChild> childRegistry;
    
    public override void Load(string filePath) {
        var root = parser.ParseFile(filePath);
        
        foreach (var groupNode in root.Children) {
            // Load parent
            var parent = LoadParent(groupNode);
            registry.Register(groupNode.Key, parent);
            
            // Load children
            foreach (var childNode in GetChildNodes(groupNode)) {
                var child = LoadChild(childNode, parent);
                childRegistry.Register(childNode.Key, child);
                
                // Link bidirectionally
                parent.AddChild(child.Id);
                child.SetParent(parent.Id);
            }
        }
    }
    
    // Override these for specialization
    protected virtual TParent LoadParent(ParsedNode node) {
        var parent = new TParent();
        MapCommonProperties(node, parent);
        return parent;
    }
    
    protected virtual TChild LoadChild(ParsedNode node, TParent parent) {
        var child = new TChild();
        MapCommonProperties(node, child);
        return child;
    }
    
    protected virtual IEnumerable<ParsedNode> GetChildNodes(ParsedNode parent) {
        // Skip known property keys
        return parent.Children.Where(n => !IsPropertyKey(n.Key));
    }
}
```

### Pattern 2: Generic Manifest Loader

```csharp
public class ManifestLoader<T> : PatternLoader<T> where T : class, new() {
    protected Dictionary<string, string> manifest = new();
    protected IDataLoader<T> dataLoader;
    
    public override void Load(string filePath) {
        // Phase 1: Load manifest
        var root = parser.ParseFile(filePath);
        
        foreach (var entry in root.Children) {
            if (!IsComment(entry.Key) && !IsDirective(entry.Key)) {
                manifest[entry.Key] = entry.Value.ToString();
                registry.Reserve(entry.Key);
            }
        }
        
        // Phase 2: Load actual data files
        foreach (var kvp in manifest) {
            try {
                var data = dataLoader.LoadFile(kvp.Value);
                registry.Register(kvp.Key, data);
            } catch (Exception e) {
                HandleMissingFile(kvp.Key, kvp.Value, e);
            }
        }
    }
    
    protected virtual void HandleMissingFile(string key, string path, Exception e) {
        // Override for different strategies
        throw new FileNotFoundException($"Cannot load {key} from {path}", e);
    }
}
```

### Pattern 3: Generic Flat List Loader

```csharp
public class FlatListLoader<T> : PatternLoader<T> where T : class, INamedEntity, new() {
    
    public override void Load(string filePath) {
        var root = parser.ParseFile(filePath);
        
        foreach (var node in root.Children) {
            if (IsValidEntity(node)) {
                var entity = CreateEntity(node);
                registry.Register(node.Key, entity);
            }
        }
    }
    
    protected virtual T CreateEntity(ParsedNode node) {
        var entity = new T();
        entity.Name = node.Key;
        MapProperties(node, entity);
        return entity;
    }
    
    protected virtual bool IsValidEntity(ParsedNode node) {
        return !node.Key.StartsWith("#") && node.HasChildren;
    }
}
```

### Pattern 4: Generic Temporal Loader

```csharp
public class TemporalLoader<T> : PatternLoader<T> 
    where T : class, ITemporalEntity, new() {
    
    public override void Load(string filePath) {
        var root = parser.ParseFile(filePath);
        
        foreach (var entityNode in root.Children) {
            var entity = new T();
            entity.Id = entityNode.Key;
            
            // Load base state
            LoadBaseState(entityNode, entity);
            
            // Load temporal changes
            var timeline = new Timeline<T>();
            foreach (var child in entityNode.Children) {
                if (IsDate(child.Key)) {
                    var date = ParseDate(child.Key);
                    var changes = ParseChanges(child);
                    timeline.AddChange(date, changes);
                }
            }
            
            entity.Timeline = timeline;
            registry.Register(entityNode.Key, entity);
        }
    }
    
    protected virtual bool IsDate(string key) {
        return Regex.IsMatch(key, @"^\d{1,4}\.\d{1,2}\.\d{1,2}$");
    }
}
```

### Pattern 5: Generic Conditional Loader

```csharp
public class ConditionalLoader<T> : PatternLoader<T>
    where T : class, IConditionalEntity, new() {
    
    protected IConditionCompiler conditionCompiler;
    protected IEffectCompiler effectCompiler;
    
    public override void Load(string filePath) {
        var root = parser.ParseFile(filePath);
        
        foreach (var node in root.Children) {
            var entity = new T();
            entity.Id = node.Key;
            
            // Compile conditions
            if (node.HasChild("trigger")) {
                entity.Trigger = conditionCompiler.Compile(node.GetChild("trigger"));
            }
            
            // Compile effects
            if (node.HasChild("effect")) {
                entity.Effects = effectCompiler.Compile(node.GetChild("effect"));
            }
            
            // Load weight modifiers
            if (node.HasChild("weight")) {
                entity.Weight = CompileWeight(node.GetChild("weight"));
            }
            
            registry.Register(node.Key, entity);
        }
    }
}
```

## When to Use Generic vs Specialized

### Use Generic Loaders When:

✅ **Data follows standard pattern exactly**
```csharp
// Buildings are just flat entities with properties
public class BuildingLoader : FlatListLoader<Building> {
    // No override needed if Building has standard properties
}
```

✅ **Differences are minor property mappings**
```csharp
// Trade goods just need custom property names
public class TradeGoodLoader : FlatListLoader<TradeGood> {
    protected override void MapProperties(ParsedNode node, TradeGood good) {
        base.MapProperties(node, good);
        good.BasePrice = node.GetFloat("base_price", 1.0f);
        good.Color = ParseColor(node.GetChild("color"));
    }
}
```

✅ **Reference resolution is standard**
```csharp
// Most manifest loaders work the same
public class CountryTagLoader : ManifestLoader<Country> {
    // Just specify the data loader
    public CountryTagLoader() {
        dataLoader = new CountryFileLoader();
    }
}
```

### Create Specialized Loaders When:

❌ **Complex nested structures**
```csharp
// Cultures have names, dynasties, and complex nesting
public class CultureLoader : HierarchicalLoader<CultureGroup, Culture> {
    protected override Culture LoadChild(ParsedNode node, CultureGroup parent) {
        var culture = base.LoadChild(node, parent);
        
        // Special handling for name lists
        culture.MaleNames = ParseNameList(node.GetChild("male_names"));
        culture.FemaleNames = ParseNameList(node.GetChild("female_names"));
        culture.DynastyNames = ParseWeightedNames(node.GetChild("dynasty_names"));
        
        // Special primary country reference
        culture.PrimaryNation = ResolveCountryTag(node.GetString("primary"));
        
        return culture;
    }
    
    private NameList ParseNameList(ParsedNode node) {
        // Complex parsing logic for names
    }
}
```

❌ **Special validation rules**
```csharp
// Provinces have complex validation
public class ProvinceLoader : TemporalLoader<Province> {
    public override void Validate() {
        foreach (var province in registry.GetAll()) {
            // Must have owner or be wasteland
            if (!province.Owner && !province.IsWasteland) {
                errors.Add($"Province {province.Id} has no owner");
            }
            
            // Capital must be owned by same country
            if (province.IsCapital && province.Owner != province.Country) {
                errors.Add($"Capital province {province.Id} ownership mismatch");
            }
        }
    }
}
```

❌ **Multiple patterns in one file**
```csharp
// Events have both conditional and temporal patterns
public class EventLoader : SpecializedLoader<Event> {
    // Can't use generic because events combine patterns
    public override void Load(string filePath) {
        // Custom loading for complex event structure
    }
}
```

❌ **Performance-critical loading**
```csharp
// Province history is huge and needs optimization
public class ProvinceHistoryLoader : SpecializedLoader<ProvinceHistory> {
    // Custom optimized loading with streaming, caching, etc.
    public override void Load(string filePath) {
        using (var stream = new StreamReader(filePath)) {
            // Optimized streaming parse
        }
    }
}
```

## Loader Composition Strategy

### Orchestrating Multiple Loaders

```csharp
public class DataLoadingOrchestrator {
    private readonly Dictionary<DataPattern, List<IPatternLoader>> loaders = new() {
        [DataPattern.Hierarchical] = new List<IPatternLoader> {
            new CultureLoader(),
            new ReligionLoader(),
            new TechnologyLoader()
        },
        [DataPattern.Manifest] = new List<IPatternLoader> {
            new CountryTagLoader(),
            new BookmarkLoader()
        },
        [DataPattern.FlatList] = new List<IPatternLoader> {
            new TradeGoodLoader(),
            new BuildingLoader(),
            new TerrainLoader()
        },
        [DataPattern.Temporal] = new List<IPatternLoader> {
            new ProvinceHistoryLoader(),
            new CountryHistoryLoader()
        },
        [DataPattern.Conditional] = new List<IPatternLoader> {
            new EventLoader(),
            new DecisionLoader(),
            new MissionLoader()
        }
    };
    
    public void LoadAll() {
        // Load in dependency order
        LoadPattern(DataPattern.FlatList);      // No dependencies
        LoadPattern(DataPattern.Hierarchical);  // May reference flat entities
        LoadPattern(DataPattern.Manifest);      // May reference hierarchical
        LoadPattern(DataPattern.Temporal);      // References everything
        LoadPattern(DataPattern.Conditional);   // References everything
        
        // Resolve all references
        ResolveAllReferences();
        
        // Validate
        ValidateAll();
    }
    
    private void LoadPattern(DataPattern pattern) {
        Parallel.ForEach(loaders[pattern], loader => {
            loader.Load();
        });
    }
}
```

## Code Reuse Metrics

### Typical Reuse with Generic Loaders

| Pattern | Generic Code | Specialized Code | Reuse % |
|---------|--------------|------------------|---------|
| Hierarchical | 200 lines | 50 lines per type | 80% |
| Manifest | 150 lines | 20 lines per type | 88% |
| Flat List | 100 lines | 10 lines per type | 91% |
| Temporal | 250 lines | 80 lines per type | 76% |
| Conditional | 300 lines | 100 lines per type | 75% |
| Weighted | 80 lines | 5 lines per type | 94% |
| Modifier | 120 lines | 30 lines per type | 80% |

**Total average: ~83% code reuse**

## Best Practices

### 1. Start Generic, Specialize As Needed
```csharp
// Start with:
public class TradeGoodLoader : FlatListLoader<TradeGood> { }

// Only specialize when you hit limitations
```

### 2. Keep Pattern Logic Separate from Data Logic
```csharp
// Good: Pattern logic in base class
public abstract class HierarchicalLoader<TParent, TChild> {
    // Handles the hierarchical structure
}

// Good: Data logic in specialized class
public class CultureLoader : HierarchicalLoader<CultureGroup, Culture> {
    // Handles culture-specific properties
}
```

### 3. Use Composition for Mixed Patterns
```csharp
public class ComplexLoader {
    private readonly TemporalLoader temporalLoader;
    private readonly ConditionalLoader conditionalLoader;
    
    public void Load(ParsedNode node) {
        // Use both loaders for different parts
        temporalLoader.LoadHistory(node.GetChild("history"));
        conditionalLoader.LoadTriggers(node.GetChild("triggers"));
    }
}
```

### 4. Document Pattern Variations
```csharp
/// <summary>
/// Loads religions using hierarchical pattern.
/// VARIATION: Religious schools are nested one level deeper
/// SPECIAL: Papacy mechanics only for Catholic
/// </summary>
public class ReligionLoader : HierarchicalLoader<ReligionGroup, Religion> {
```

### 5. Test Generic Loaders Thoroughly
```csharp
[TestFixture]
public class GenericLoaderTests {
    [Test]
    public void TestFlatListLoader() {
        var loader = new FlatListLoader<TestEntity>();
        // Test with minimal entity
        // Test with complex entity
        // Test with malformed data
    }
}
```

## Performance Considerations

### Generic Loader Performance

**Pros:**
- Code is reused = better CPU cache utilization
- Well-tested paths = optimized over time
- Consistent memory patterns

**Cons:**
- Virtual method calls (minimal impact)
- Some unnecessary checks for specific types
- Generic constraints can limit optimizations

**Optimization Strategy:**
```csharp
public class OptimizedGenericLoader<T> : PatternLoader<T> {
    // Use object pooling
    private readonly Stack<T> pool = new();
    
    // Batch operations
    private readonly int BATCH_SIZE = 100;
    
    // Cache reflection
    private readonly PropertyMapper<T> mapper = new();
}
```

## Summary

Using pattern-based generic loaders gives you:

1. **~83% code reuse** across different data types
2. **Consistent behavior** for similar patterns
3. **Easier maintenance** - fix bug once, fixes everywhere
4. **Clear architecture** - 7 patterns vs 50+ individual loaders
5. **Moddability** - modders understand patterns
6. **Testability** - test pattern once, works for all

The key is recognizing that Paradox data files follow patterns regardless of content. A culture file and a religion file are structurally identical (hierarchical pattern), even though their properties differ. Build for the pattern, specialize for the properties.