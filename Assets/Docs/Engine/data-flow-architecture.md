# Grand Strategy Game - Data Flow & System Architecture

## Executive Summary
**Question**: How do all these systems actually connect and communicate?  
**Answer**: Hub-and-spoke architecture with specialized systems and a central game state  
**Key Principle**: Each system owns its data, GameState provides unified access  
**Performance**: Zero allocations during gameplay, all queries <0.01ms

## The Big Picture - System Architecture

```
┌─────────────────────────────────────────────────────────┐
│                     GAME MANAGER                         │
│  (Orchestrates everything, owns the game loop)           │
└────────┬──────────────────────────────────┬──────────────┘
         │                                  │
         ▼                                  ▼
┌──────────────────┐              ┌──────────────────────┐
│   GAME STATE     │◄────────────►│   TIME MANAGER       │
│ (Central truth)  │              │ (Controls ticks)     │
└────────┬─────────┘              └──────────────────────┘
         │
    ┌────┴────┬──────────┬─────────┬──────────┬──────────┐
    ▼         ▼          ▼         ▼          ▼          ▼
┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐
│Province│ │ Nation │ │Military│ │Economic│ │  AI    │ │Diplo   │
│ System │ │ System │ │ System │ │ System │ │ System │ │System  │
└────────┘ └────────┘ └────────┘ └────────┘ └────────┘ └────────┘
```

## Core Design Principles

### 1. Single Source of Truth
There is ONE authoritative place for each piece of data. No duplicates, no confusion.

```csharp
// GOOD: Province ownership lives in one place
public class ProvinceSystem {
    private byte[] provinceOwners;  // THE authority on who owns what
    
    public byte GetOwner(ushort provinceId) => provinceOwners[provinceId];
    public void SetOwner(ushort provinceId, byte nation) {
        provinceOwners[provinceId] = nation;
        EventBus.Emit(new ProvinceOwnershipChanged(provinceId, nation));
    }
}

// BAD: Multiple systems tracking ownership
public class BadDesign {
    // Nation system has its own list
    List<ushort>[] provincesByNation;
    // Province system has its own data
    byte[] provinceOwners;
    // Now they can get out of sync!
}
```

### 2. Systems Own Their Domain
Each system is responsible for its own data and logic. Other systems ask, don't touch.

```csharp
// Economic system owns all economic data
public class EconomicSystem {
    private float[] provinceTax;
    private float[] nationTreasury;
    
    // Only Economic system can modify treasury
    public void AddGold(byte nation, float amount) {
        nationTreasury[nation] += amount;
        // Economic system handles all side effects
        CheckBankruptcy(nation);
        UpdateInflation(nation);
    }
    
    // Others can only read
    public float GetTreasury(byte nation) => nationTreasury[nation];
}
```

### 3. Events for Cross-System Communication
Systems don't directly call each other. They emit events that others listen to.

```csharp
// When province changes ownership
ProvinceSystem.SetOwner(1234, France);
// This emits: ProvinceOwnershipChanged event

// Other systems listening:
EconomicSystem.OnProvinceOwnershipChanged() // Update tax collection
MilitarySystem.OnProvinceOwnershipChanged()  // Update manpower
AISystem.OnProvinceOwnershipChanged()        // Re-evaluate goals
```

## The Central GameState

### What It Is
GameState is the central hub that provides unified access to all game data. It doesn't OWN the data, it COORDINATES access to it.

```csharp
public class GameState {
    // References to all systems
    public readonly ProvinceSystem Provinces;
    public readonly NationSystem Nations;
    public readonly MilitarySystem Military;
    public readonly EconomicSystem Economy;
    public readonly DiplomaticSystem Diplomacy;
    public readonly AISystem AI;
    public readonly TimeManager Time;
    
    // Convenient unified access
    public byte GetProvinceOwner(ushort province) {
        return Provinces.GetOwner(province);
    }
    
    public float GetNationTreasury(byte nation) {
        return Economy.GetTreasury(nation);
    }
    
    // Complex queries that span systems
    public float GetNationTotalTax(byte nation) {
        float total = 0;
        foreach (var province in Provinces.GetNationProvinces(nation)) {
            total += Economy.GetProvinceTax(province);
        }
        return total;
    }
    
    // Commands that affect multiple systems
    public void ConquerProvince(ushort province, byte newOwner) {
        var oldOwner = Provinces.GetOwner(province);
        
        // Province system handles ownership
        Provinces.SetOwner(province, newOwner);
        
        // This triggers events that other systems handle
        // Economic: Transfer tax income
        // Military: Transfer manpower
        // AI: Update both nations' goals
        // All handled automatically through events
    }
}
```

### What It Doesn't Do
GameState is NOT a god object. It doesn't contain business logic.

```csharp
// BAD: GameState doing too much
public class BadGameState {
    public void ConquerProvince(ushort province, byte newOwner) {
        // DON'T put logic here!
        provinces[province].owner = newOwner;
        UpdateTaxes();
        RecalculateManpower();
        CheckForFormableNations();
        UpdateTradeRoutes();
        // This becomes unmaintainable!
    }
}

// GOOD: GameState just coordinates
public class GoodGameState {
    public void ConquerProvince(ushort province, byte newOwner) {
        Provinces.SetOwner(province, newOwner);
        // Systems handle their own logic via events
    }
}
```

## Startup Flow - The Loading Screen

Game initialization follows a sequential pipeline optimized for minimal load time:

```csharp
public class GameInitializer {
    public async Task InitializeGame() {
        // 1. Core Systems (5%) - Instantiate managers, event bus
        InitializeManagers();

        // 2. Static Data (10-30%) - Parallel load all script files
        await LoadGameDefinitions();  // Provinces, nations, buildings, etc.
        CompileScripts();              // Convert text to runtime bytecode

        // 3. Map Data (30-60%) - GPU texture generation
        LoadProvinceBitmap();          // Load provinces.bmp
        CreateProvinceTextures();      // Generate GPU textures
        BuildAdjacencyGraph();         // For pathfinding

        // 4. Scenario/Save (60-80%) - Initialize game state
        LoadScenarioOrSave();          // Set province ownership, nation data

        // 5. AI Initialization (80-90%) - Per-nation AI setup
        InitializeAIForAllNations();

        // 6. Final Prep (90-100%) - Cache warmup, UI setup
        WarmUpCaches();
        StartGameLoop();
    }
}
```

**Key Performance**: Total load time ~500ms for 10k provinces with parallel loading and pre-compiled scripts.

## Data Access Patterns

### Reading Data - The Query Pattern
Three query types based on performance needs:
- **Simple queries**: Direct array access (<0.001ms)
- **Computed queries**: Calculate on-demand from multiple sources
- **Cached queries**: Frame-coherent caching for expensive calculations

```csharp
// Example: Cached query for expensive calculation
private CachedValue<float> cachedArmyStrength;
public float GetNationArmyStrength(byte nation) {
    return cachedArmyStrength.GetOrCalculate(nation, () => {
        float strength = 0;
        foreach (var army in GetNationArmies(nation)) {
            strength += CalculateArmyStrength(army);
        }
        return strength;
    });
}
```

### Writing Data - The Command Pattern
All state changes use commands for validation, event emission, multiplayer sync, and replay support.

```csharp
public interface ICommand {
    bool Validate(GameState state);
    void Execute(GameState state);
    void Undo(GameState state);
}

// Example: Province ownership change
public class ChangeProvinceOwnerCommand : ICommand {
    public ushort provinceId;
    public byte newOwner;

    public void Execute(GameState state) {
        state.Provinces.SetOwner(provinceId, newOwner);
        EventBus.Emit(new ProvinceConqueredEvent(provinceId, newOwner));
    }
}
```
See `save-load-architecture.md` for serialization details and `multiplayer-architecture-guide.md` for network synchronization.

### Deleting Data - Pooling Pattern
Pre-allocated object pools prevent runtime allocations. Objects are recycled, not destroyed.

```csharp
public class ArmySystem {
    private Army[] armyPool = new Army[MAX_ARMIES];
    private Stack<int> freeArmies = new Stack<int>();

    public int CreateArmy(byte nation) {
        int armyId = freeArmies.Pop();
        armyPool[armyId].Reset();
        return armyId;
    }
}
```

## System Communication Patterns

### Event-Driven Updates
```csharp
public class EventDrivenCommunication {
    // Example: Building completed
    // 1. Building system completes construction
    BuildingSystem.CompleteBuilding(province, BuildingType.Workshop);
    
    // 2. This emits event
    EventBus.Emit(new BuildingCompletedEvent {
        province = province,
        building = BuildingType.Workshop
    });
    
    // 3. Other systems respond
    EconomicSystem.OnBuildingCompleted(event) {
        // Recalculate province production
        UpdateProvinceProduction(event.province);
    }
    
    AISystem.OnBuildingCompleted(event) {
        // AI notes improved economy
        UpdateProvinceValue(event.province);
    }
    
    UISystem.OnBuildingCompleted(event) {
        // Show notification
        ShowNotification($"Workshop completed in {GetProvinceName(event.province)}");
    }
}
```

### Direct System Queries
```csharp
// Sometimes you need immediate data
public class DirectQueries {
    // AI needs to evaluate military situation
    public float EvaluateWarSuccess(byte us, byte them) {
        // Direct queries to multiple systems
        float ourStrength = Military.GetNationStrength(us);
        float theirStrength = Military.GetNationStrength(them);
        
        float ourGold = Economy.GetTreasury(us);
        float theirGold = Economy.GetTreasury(them);
        
        var ourAllies = Diplomacy.GetAllies(us);
        var theirAllies = Diplomacy.GetAllies(them);
        
        // Combine data for decision
        return CalculateWarScore(ourStrength, theirStrength, ...);
    }
}
```

## The Game Loop

```csharp
public class GameManager {
    private const float FIXED_TIMESTEP = 1f / 60f;

    public void GameLoop() {
        while (isRunning) {
            Input.ProcessInput();              // Immediate
            FixedUpdate(FIXED_TIMESTEP);       // Deterministic simulation
            Render(deltaTime);                 // As fast as possible
        }
    }

    private void FixedUpdate(float dt) {
        TimeManager.Tick(dt);                  // Triggers daily/monthly updates
        CommandProcessor.ProcessQueue();       // Execute player/AI commands
        UpdateDirtySystems();                  // Only update what changed
    }
}
```
See `time-system-architecture.md` for details on tick-based update scheduling and time progression.

## Specialized vs Generic Loaders

### Hybrid Approach - Best of Both
```csharp
// Generic base loader for common patterns
public abstract class DataLoader<T> {
    public virtual T[] LoadFromFile(string path) {
        var text = File.ReadAllText(path);
        var parsed = ParadoxParser.Parse(text);
        return ParseToObjects(parsed);
    }
    
    protected abstract T[] ParseToObjects(ParsedNode root);
}

// Specialized loaders for complex data
public class ProvinceLoader : DataLoader<ProvinceDefinition> {
    protected override ProvinceDefinition[] ParseToObjects(ParsedNode root) {
        // Province-specific parsing logic
        // Handle special cases like straits, impassable, etc.
    }
    
    // Province-specific methods
    public void LoadProvinceBitmap(string bmpPath) {
        // Special bitmap loading for provinces
    }
    
    public void BuildAdjacency(ProvinceDefinition[] provinces) {
        // Special adjacency calculation
    }
}

// Simple loaders can use generic
public class ReligionLoader : DataLoader<Religion> {
    protected override Religion[] ParseToObjects(ParsedNode root) {
        // Simple parsing for religions
        return root.children.Select(node => new Religion {
            id = node.GetString("id"),
            name = node.GetString("name"),
            color = node.GetColor("color")
        }).ToArray();
    }
}
```

## Memory Management Strategy

### Pool Everything
```csharp
public class PoolingStrategy {
    // Pre-allocate all possible objects
    public class GlobalPools {
        public static Army[] ArmyPool = new Army[10000];
        public static Event[] EventPool = new Event[1000];
        public static Effect[] EffectPool = new Effect[10000];
        
        static GlobalPools() {
            // Initialize all objects at startup
            for (int i = 0; i < ArmyPool.Length; i++) {
                ArmyPool[i] = new Army();
            }
        }
    }
    
    // Never allocate during gameplay
    public Army GetArmy() {
        return GlobalPools.ArmyPool[GetFreeIndex()];
    }
}
```

### Structure of Arrays
Use separate arrays for each field instead of array-of-structs to maximize cache efficiency during iteration. See `performance-architecture-guide.md` for detailed memory layout optimization strategies.

## Error Handling & Validation

### Validation at System Boundaries
```csharp
public class ValidationLayer {
    // Validate at entry points
    public bool TryConquerProvince(ushort province, byte nation) {
        // Validate inputs
        if (province >= ProvinceCount) {
            LogError($"Invalid province {province}");
            return false;
        }
        
        if (nation >= NationCount) {
            LogError($"Invalid nation {nation}");
            return false;
        }
        
        // Check game rules
        if (!IsAtWar(GetProvinceOwner(province), nation)) {
            LogError("Can't conquer province when not at war");
            return false;
        }
        
        // If valid, execute
        ConquerProvince(province, nation);
        return true;
    }
}
```

## Performance Monitoring

### Built-in Profiling
```csharp
public class PerformanceMonitor {
    private Dictionary<string, SystemMetrics> metrics = new();
    
    public void RecordSystemUpdate(string system, long ticks) {
        metrics[system].totalTicks += ticks;
        metrics[system].updateCount++;
        metrics[system].averageMs = metrics[system].totalTicks / 
                                   (float)metrics[system].updateCount / 10000f;
    }
    
    public void LogFrameMetrics() {
        if (Time.frameCount % 60 == 0) {  // Every second
            foreach (var kvp in metrics) {
                if (kvp.Value.averageMs > 1.0f) {
                    LogWarning($"{kvp.Key} taking {kvp.Value.averageMs}ms!");
                }
            }
        }
    }
}
```

## Summary - The Complete Flow

1. **Startup**: Load data → Compile scripts → Initialize systems → Build caches
2. **Game Loop**: Input → Time tick → Process commands → Update dirty → Render
3. **Data Access**: Always through systems, never direct
4. **Changes**: Through commands for validation and sync
5. **Communication**: Events for loose coupling
6. **Performance**: Pool objects, cache queries, update only what changes

The key is that each system is independent but coordinated through GameState and EventBus. This gives you:
- **Clean separation** - Easy to understand and modify
- **Performance** - Each system optimized independently
- **Multiplayer-ready** - Commands can be networked
- **Moddable** - Data-driven through scripts
- **Maintainable** - Clear ownership and responsibilities

## Related Documentation
- **time-system-architecture.md** - Tick-based update scheduling and game speed control
- **save-load-architecture.md** - Serialization, persistence, and replay system
- **multiplayer-architecture-guide.md** - Network synchronization and command distribution
- **performance-architecture-guide.md** - Memory layout optimization and cache efficiency