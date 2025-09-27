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

### Phase 1: Initialize Core Systems (5%)
```csharp
public class GameInitializer {
    public async Task InitializeGame() {
        UpdateLoadingScreen("Initializing core systems...", 0);
        
        // Create managers (instant)
        var gameManager = new GameManager();
        var timeManager = new TimeManager();
        var eventBus = new EventBus();
        
        UpdateLoadingScreen("Creating world...", 5);
    }
}
```

### Phase 2: Load Static Data (10-30%)
```csharp
// Load all the Paradox script files
UpdateLoadingScreen("Loading game data...", 10);

// Parallel load all script files
var tasks = new List<Task> {
    Task.Run(() => LoadProvinceDefinitions()),    // 5ms
    Task.Run(() => LoadNationDefinitions()),       // 3ms
    Task.Run(() => LoadBuildings()),               // 2ms
    Task.Run(() => LoadTechnologies()),            // 2ms
    Task.Run(() => LoadReligions()),               // 1ms
    Task.Run(() => LoadCultures()),                // 1ms
    Task.Run(() => LoadTradeGoods())               // 1ms
};

await Task.WhenAll(tasks);
UpdateLoadingScreen("Compiling scripts...", 20);

// Compile scripts to runtime format
CompileEffects();      // Convert text to bytecode
CompileConditions();   // Convert triggers to bytecode
CompileDecisions();    // Link everything together

UpdateLoadingScreen("Building indices...", 30);
```

### Phase 3: Initialize Map Data (30-60%)
```csharp
UpdateLoadingScreen("Loading map...", 30);

// Load the province bitmap
var provinceBitmap = LoadProvinceBitmap("provinces.bmp");  // 10ms

UpdateLoadingScreen("Processing provinces...", 35);

// Extract province data from bitmap
var provinceData = ExtractProvinces(provinceBitmap);  // 50ms

UpdateLoadingScreen("Generating province textures...", 40);

// Create GPU textures
CreateProvinceIDTexture(provinceData);     // 20ms
CreateProvinceColorTexture(provinceData);  // 20ms

UpdateLoadingScreen("Building adjacency...", 50);

// Build adjacency graph for pathfinding
BuildAdjacencyGraph(provinceData);  // 100ms

UpdateLoadingScreen("Calculating regions...", 55);

// Hierarchical regions for pathfinding
BuildRegionHierarchy(provinceData);  // 50ms

UpdateLoadingScreen("Initializing terrain...", 60);
```

### Phase 4: Load Scenario/Save (60-80%)
```csharp
UpdateLoadingScreen("Loading scenario...", 60);

if (isNewGame) {
    // Load starting scenario (1444, 1836, etc)
    LoadScenario("1444_start.txt");
    
    // Assign province ownership
    foreach (var setup in scenarioData.provinces) {
        Provinces.SetOwner(setup.id, setup.owner);
        Economy.SetProvinceTax(setup.id, setup.tax);
    }
    
    // Setup nations
    foreach (var nation in scenarioData.nations) {
        Nations.Create(nation);
        Economy.SetTreasury(nation.id, nation.startingGold);
        Military.CreateStartingArmies(nation.id, nation.armies);
    }
} else {
    // Load save game
    LoadSaveGame(savePath);
    DeserializeGameState();
}

UpdateLoadingScreen("Initializing nations...", 70);
```

### Phase 5: Initialize AI (80-90%)
```csharp
UpdateLoadingScreen("Initializing AI...", 80);

// Create AI for each nation
foreach (var nation in Nations.GetAll()) {
    if (!nation.isPlayer) {
        AI.InitializeNationAI(nation.id);
        AI.CalculateInitialGoals(nation.id);  // 1ms per nation
    }
}

UpdateLoadingScreen("Calculating initial state...", 85);

// Pre-calculate expensive shared data
SharedAIData.CalculateRegionStrengths();   // 5ms
SharedAIData.CalculateTradeNodeValues();   // 3ms
SharedAIData.BuildDiplomaticWeb();         // 2ms
```

### Phase 6: Final Initialization (90-100%)
```csharp
UpdateLoadingScreen("Starting simulation...", 90);

// Warm up caches
WarmUpPathfindingCache();  // Common routes
WarmUpModifierCache();      // Calculate all modifiers once

UpdateLoadingScreen("Preparing UI...", 95);

// Initialize UI systems
UI.Initialize(gameState);
UI.CreateProvinceLabels();
UI.BuildNationList();

UpdateLoadingScreen("Ready!", 100);

// Start the game loop
GameManager.StartGameLoop();
```

## Data Access Patterns

### Reading Data - The Query Pattern
```csharp
public class DataQueries {
    // Simple queries - direct access
    public float GetProvinceTax(ushort province) {
        return Economy.provinceTax[province];  // Direct array access, <0.001ms
    }
    
    // Computed queries - calculate on demand
    public float GetNationTotalIncome(byte nation) {
        float income = 0;
        
        // Sum from all owned provinces
        foreach (var province in GetNationProvinces(nation)) {
            income += GetProvinceTax(province);
            income += GetProvinceProduction(province);
            income += GetProvinceTrade(province);
        }
        
        return income;
    }
    
    // Cached queries - expensive calculations
    private CachedValue<float> cachedArmyStrength;
    
    public float GetNationArmyStrength(byte nation) {
        return cachedArmyStrength.GetOrCalculate(nation, () => {
            // Expensive calculation
            float strength = 0;
            foreach (var army in GetNationArmies(nation)) {
                strength += CalculateArmyStrength(army);
            }
            return strength;
        });
    }
}
```

### Writing Data - The Command Pattern
```csharp
// All changes go through commands for:
// 1. Validation
// 2. Event emission
// 3. Multiplayer sync
// 4. Save/replay

public interface ICommand {
    bool Validate(GameState state);
    void Execute(GameState state);
    void Undo(GameState state);  // For replay
}

public class ChangeProvinceOwnerCommand : ICommand {
    public ushort provinceId;
    public byte newOwner;
    private byte oldOwner;  // For undo
    
    public bool Validate(GameState state) {
        // Can't take province that doesn't exist
        if (provinceId >= state.Provinces.Count) return false;
        
        // Can't give to non-existent nation
        if (newOwner >= state.Nations.Count) return false;
        
        return true;
    }
    
    public void Execute(GameState state) {
        oldOwner = state.Provinces.GetOwner(provinceId);
        state.Provinces.SetOwner(provinceId, newOwner);
        
        // Emit events for other systems
        EventBus.Emit(new ProvinceConqueredEvent {
            province = provinceId,
            oldOwner = oldOwner,
            newOwner = newOwner
        });
    }
    
    public void Undo(GameState state) {
        state.Provinces.SetOwner(provinceId, oldOwner);
    }
}

// Usage
var command = new ChangeProvinceOwnerCommand {
    provinceId = 1234,
    newOwner = France
};

if (command.Validate(gameState)) {
    command.Execute(gameState);
    
    // For multiplayer
    Network.BroadcastCommand(command);
    
    // For replay
    ReplayRecorder.Record(command);
}
```

### Deleting Data - Pooling Pattern
```csharp
// Don't actually delete, recycle!
public class ArmySystem {
    private Army[] armyPool = new Army[MAX_ARMIES];
    private Stack<int> freeArmies = new Stack<int>();
    private List<int>[] nationArmies;  // Indices into pool
    
    public int CreateArmy(byte nation) {
        if (freeArmies.Count == 0) return -1;  // Pool exhausted
        
        int armyId = freeArmies.Pop();
        armyPool[armyId].Reset();
        armyPool[armyId].nation = nation;
        armyPool[armyId].active = true;
        
        nationArmies[nation].Add(armyId);
        return armyId;
    }
    
    public void DeleteArmy(int armyId) {
        var army = armyPool[armyId];
        nationArmies[army.nation].Remove(armyId);
        
        army.active = false;
        freeArmies.Push(armyId);  // Return to pool
    }
    
    // Iterate only active armies
    public IEnumerable<Army> GetNationArmies(byte nation) {
        foreach (var armyId in nationArmies[nation]) {
            if (armyPool[armyId].active) {
                yield return armyPool[armyId];
            }
        }
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

### Main Loop Structure
```csharp
public class GameManager {
    private bool isRunning = true;
    private float accumulator = 0;
    private const float FIXED_TIMESTEP = 1f / 60f;  // 60 updates per second
    
    public void GameLoop() {
        var lastTime = Time.Now;
        
        while (isRunning) {
            var currentTime = Time.Now;
            var deltaTime = currentTime - lastTime;
            lastTime = currentTime;
            
            // Input (immediate)
            Input.ProcessInput();
            
            // Fixed timestep update
            accumulator += deltaTime;
            while (accumulator >= FIXED_TIMESTEP) {
                FixedUpdate(FIXED_TIMESTEP);
                accumulator -= FIXED_TIMESTEP;
            }
            
            // Render (as fast as possible)
            Render(deltaTime);
        }
    }
    
    private void FixedUpdate(float dt) {
        // Time progression
        TimeManager.Tick(dt);
        
        // This triggers appropriate updates based on time
        // Daily tick? -> Update daily systems
        // Monthly tick? -> Update monthly systems
        
        // Process commands from player/AI
        CommandProcessor.ProcessQueue();
        
        // Update dirty systems
        UpdateDirtySystems();
    }
    
    private void UpdateDirtySystems() {
        // Only update what changed
        if (Provinces.IsDirty) Provinces.Update();
        if (Economy.IsDirty) Economy.Update();
        if (Military.IsDirty) Military.Update();
        // etc...
    }
}
```

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
```csharp
// Instead of array of structures
public class BadDesign {
    public struct Province {
        public ushort id;
        public byte owner;
        public float tax;
        public float production;
        public float manpower;
    }
    public Province[] provinces;  // Bad cache usage
}

// Use structure of arrays
public class GoodDesign {
    public ushort[] provinceIds;
    public byte[] provinceOwners;
    public float[] provinceTax;
    public float[] provinceProduction;
    public float[] provinceManpower;
    // Each array is cache-friendly for iteration
}
```

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