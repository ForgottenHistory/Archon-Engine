# Engine-Game Separation Architecture
**Purpose:** Define what belongs in the reusable ArchonEngine vs game-specific logic

---

## Philosophy: Mechanisms vs Policy

> **Engine provides mechanisms (HOW).
> Game defines policy (WHAT).**

**Engine says:** "Here's how to change province state deterministically"
**Game says:** "When you build a farm, development increases by 1"

**Engine says:** "Here's how to render 10k provinces at 200 FPS"
**Game says:** "Color provinces by income (red = poor, green = rich)"

**Engine says:** "Here's how to process commands over network"
**Game says:** "Declaring war costs 10 prestige and requires a valid CB"

---

## Engine Extension Points

**The engine defines INTERFACES. The game IMPLEMENTS them.**

### 1. Game Systems Interface

```csharp
// ArchonEngine/Core/Interfaces/IGameSystem.cs
namespace ArchonEngine.Core
{
    public interface IGameSystem
    {
        void Initialize(GameState state);
        void OnHourlyTick(HourlyTickEvent evt);
        void OnDailyTick(DailyTickEvent evt);
        void OnMonthlyTick(MonthlyTickEvent evt);
        void OnYearlyTick(YearlyTickEvent evt);
        void Shutdown();
    }
}

// Game/Systems/EconomySystem.cs
namespace Game.Systems
{
    public class EconomySystem : IGameSystem
    {
        public void OnMonthlyTick(MonthlyTickEvent evt)
        {
            // Game-specific: collect taxes, pay maintenance
            CollectTaxes();
            PayMaintenanceCosts();
        }
    }
}
```

### 2. Map Mode Interface (Already Exists!)

```csharp
// ArchonEngine/Map/MapModes/IMapModeHandler.cs
namespace ArchonEngine.Map
{
    public interface IMapModeHandler
    {
        void UpdateTexture(RenderTexture target);
        void OnEnter();
        void OnExit();
    }
}

// Game/MapModes/EconomyMapMode.cs
namespace Game.MapModes
{
    public class EconomyMapMode : IMapModeHandler
    {
        public void UpdateTexture(RenderTexture target)
        {
            // Game-specific: color by income
            foreach (var province in provinces)
            {
                Color color = IncomeToColor(province.income);
                target.SetPixel(x, y, color);
            }
        }
    }
}
```

### 3. Command Interface (Already Exists!)

```csharp
// ArchonEngine/Core/Commands/ICommand.cs
namespace ArchonEngine.Core
{
    public interface ICommand
    {
        void Execute(GameState state);
        bool Validate(GameState state);
        uint GetChecksum();
        void Dispose();
    }
}

// Game/Commands/BuildBuildingCommand.cs
namespace Game.Commands
{
    public class BuildBuildingCommand : ICommand
    {
        private ushort provinceId;
        private ushort buildingId;

        public void Execute(GameState state)
        {
            // Game-specific: build building, pay cost
            var province = state.ProvinceSystem.GetProvinceState(provinceId);
            province.development += buildingDefinitions[buildingId].developmentBonus;
            state.ProvinceSystem.SetProvinceState(provinceId, province);
        }
    }
}
```

### 4. Save/Load Interface (Future)

```csharp
// ArchonEngine/Core/Persistence/ISaveSystem.cs
namespace ArchonEngine.Core
{
    public interface ISaveSystem
    {
        void SaveGame(string path, GameState state);
        GameState LoadGame(string path);
        byte[] SerializeGameState(GameState state);
        GameState DeserializeGameState(byte[] data);
    }
}

// Implementation provided by engine, game just calls it
saveSystem.SaveGame("autosave.bin", gameState);
```

### 5. Mod Loader Interface (Future)

```csharp
// ArchonEngine/Core/Modding/IModLoader.cs
namespace ArchonEngine.Core
{
    public interface IModLoader
    {
        void LoadMod(string modPath);
        T GetDefinition<T>(string id) where T : IDefinition;
        void RegisterDefinition<T>(T definition) where T : IDefinition;
    }
}

// Game defines what's moddable
public class BuildingDefinition : IDefinition
{
    public string id;
    public FixedPoint64 cost;
    public FixedPoint64 incomeBonus;
    public int developmentRequirement;
}
```

### 6. Localization System (Add This)

```csharp
// ArchonEngine/Utils/Localization/ILocalizationSystem.cs
namespace ArchonEngine.Utils
{
    public interface ILocalizationSystem
    {
        string GetText(string key);
        string GetText(string key, params object[] args);
        void SetLanguage(string languageCode);
        void LoadLocalization(string path);
    }
}

// Engine provides YAML parser (already have it)
// Game provides localization files
// en.yaml:
//   building.farm: "Farm"
//   building.farm.desc: "Produces food (+{0} income)"
```

---

## What the Engine Abstracts (The Hard Problems)

### ✅ Already Solved by Engine

1. **Multiplayer Determinism**
   - FixedPoint64 for all math (no float/double)
   - Command pattern for state changes
   - Checksum validation
   - **Game just implements ICommand**

2. **Performance at Scale**
   - 10k provinces at 200+ FPS
   - GPU compute shaders for visuals
   - Burst compilation, NativeArray
   - Hot/cold data separation
   - **Game just uses ProvinceSystem API**

3. **Event-Driven Architecture**
   - Zero-allocation EventBus
   - Frame-coherent processing
   - Event pooling
   - **Game just subscribes to events**

4. **Map Interaction**
   - Texture-based province selection (<1ms)
   - GPU rendering pipeline
   - Map mode switching
   - **Game just implements IMapModeHandler**

5. **Data Loading**
   - JSON5 parsing
   - Burst-optimized loaders
   - Registry system
   - **Game just provides data files**

6. **Time Management**
   - Tick-based progression
   - 360-day calendar
   - Speed controls
   - **Game just subscribes to tick events**

### 🟡 Should Add to Engine

7. **Save/Load System**
   - Serialize GameState (80KB for 10k provinces)
   - Command history for rollback
   - RNG state preservation
   - **Auto-serialize all ProvinceState, CountryState**

8. **Mod Support**
   - Definition loading from JSON5
   - Mod override system
   - **Game defines IDefinition types**

9. **Localization**
   - YAML localization (engine has parser)
   - Language switching
   - String formatting
   - **Game provides translation files**

10. **Performance Profiling**
    - Built-in profiler hooks
    - Performance report generation
    - **Game uses for optimization**

---

## Using the Engine in Future Projects

**Scenario: Building a different grand strategy game**

### Step 1: Import Package
```
1. Create new Unity project
2. Import ArchonEngine-v1.0.unitypackage
3. You now have 28k lines of infrastructure
```

### Step 2: Define Game Systems
```csharp
// MyNewGame/Systems/SpaceEconomySystem.cs
using ArchonEngine.Core;

public class SpaceEconomySystem : IGameSystem
{
    public void OnMonthlyTick(MonthlyTickEvent evt)
    {
        // Different game rules, same engine
        CollectStarbaseTaxes();
        ProcessTradeLanes();
    }
}
```

### Step 3: Create Content
```json5
// MyNewGame/Data/starbases.json5
{
  "starbase_mining": {
    "cost": 500,
    "incomeBonus": 10,
    "requirements": { "minerals": 1 }
  }
}
```

### Step 4: Implement Map Modes
```csharp
// MyNewGame/MapModes/InfluenceMapMode.cs
using ArchonEngine.Map;

public class InfluenceMapMode : IMapModeHandler
{
    public void UpdateTexture(RenderTexture target)
    {
        // Color by empire influence (new game mechanic)
    }
}
```

### Step 5: Bootstrap
```csharp
// MyNewGame/GameBootstrap.cs
void Start()
{
    var gameState = new GameState();
    gameState.RegisterSystem(new SpaceEconomySystem());
    gameState.RegisterSystem(new FleetSystem());
    gameState.Initialize();
}
```

**You've built a completely different game using the same engine!**

---

## Benefits of This Separation

### For Current Project (Archon)
- ✅ Clear separation of concerns
- ✅ Engine code is battle-tested foundation
- ✅ Game code is pure gameplay logic
- ✅ Easy to understand what's what

### For Future Projects
- ✅ Import package = infrastructure for free
- ✅ Focus on game design, not engine work
- ✅ Proven architecture
- ✅ Multiplayer-ready out of the box

### For Maintenance
- ✅ Engine bugs fixed once, all projects benefit
- ✅ Engine improvements (e.g., better save/load) benefit all projects
- ✅ Game-specific bugs isolated to Game/ folder
- ✅ Clear ownership (engine team vs game team in studio context)

### For Learning
- ✅ Other devs can use your engine
- ✅ Portfolio piece: "I built a grand strategy engine"
- ✅ Potential asset store product
- ✅ Case study for "how to architect game engines"

---

## Engine Design Principles

### 1. **Mechanisms, Not Policy**
```csharp
// ❌ Engine should NOT have this
public FixedPoint64 CalculateTax(Province p) {
    return p.development * FixedPoint64.FromFraction(5, 10); // Hard-coded formula
}

// ✅ Engine should have this
public ProvinceState GetProvinceState(ushort id);
public void SetProvinceState(ushort id, ProvinceState state);

// ✅ Game implements formula
public FixedPoint64 CalculateTax(ProvinceState state) {
    return state.development * taxRate; // Game's formula
}
```

### 2. **Flexible, But Opinionated**
```
✅ Opinionated: "Use FixedPoint64 for determinism"
✅ Flexible: "But you define the formulas"

✅ Opinionated: "Use GPU compute shaders for visuals"
✅ Flexible: "But you create the map modes"

✅ Opinionated: "Use command pattern for state changes"
✅ Flexible: "But you define the commands"
```

### 3. **Abstract Hard Problems**
```
✅ Multiplayer determinism → FixedPoint64, command pattern
✅ Performance at scale → GPU, Burst, NativeArray
✅ State management → ProvinceSystem, CountrySystem
✅ Event architecture → EventBus
✅ Data persistence → Save/load system
```

### 4. **Clear Extension Points**
```csharp
// Engine defines interfaces
public interface IGameSystem { ... }
public interface IMapModeHandler { ... }
public interface ICommand { ... }
public interface IDefinition { ... }

// Game implements
public class EconomySystem : IGameSystem { ... }
public class EconomyMapMode : IMapModeHandler { ... }
public class BuildBuildingCommand : ICommand { ... }
```

### 5. **Zero Game Logic in Engine**
```csharp
// ❌ This belongs in Game/, not Engine/
public class BuildingSystem {
    public void BuildFarm(ushort provinceId) {
        // Game-specific logic in engine = bad
    }
}

// ✅ Engine provides primitives
public class ProvinceSystem {
    public void SetProvinceState(ushort id, ProvinceState state) {
        // Generic mechanism
    }
}

// ✅ Game uses primitives
public class BuildingSystem : IGameSystem {
    public void BuildFarm(ushort provinceId) {
        var state = provinceSystem.GetProvinceState(provinceId);
        state.development += farmDefinition.developmentBonus;
        provinceSystem.SetProvinceState(provinceId, state);
    }
}
```

---

## Success Criteria

**The engine is well-designed if:**

1. ✅ **A new developer can build a different game in 1 week**
   - Import package
   - Implement IGameSystem
   - Create data files
   - Works!

2. ✅ **The engine has zero mentions of game-specific concepts**
   - No "farms" or "armies" or "trade" in engine code
   - Only generic concepts: provinces, countries, commands, events

3. ✅ **Game logic never calls engine internals**
   - All engine access through public APIs
   - No `provinceSystem.provinceStates[id]` in game code
   - Only `provinceSystem.GetProvinceState(id)`

4. ✅ **You can export as Unity package and it works**
   - No missing dependencies
   - No hard-coded paths
   - Clean namespace separation

5. ✅ **Documentation is clear about what belongs where**
   - "Want to change tax formula? → Game layer"
   - "Want to optimize rendering? → Engine layer"

---

## Related Documents

**Engine Architecture:**
- [master-architecture-document.md](master-architecture-document.md) - Technical implementation details
- [ARCHITECTURE_OVERVIEW.md](ARCHITECTURE_OVERVIEW.md) - System status overview
- [Core/FILE_REGISTRY.md](../../Scripts/Core/FILE_REGISTRY.md) - Core layer files
- [Map/FILE_REGISTRY.md](../../Scripts/Map/FILE_REGISTRY.md) - Map layer files

**Design Guides:**
- [core-data-access-guide.md](core-data-access-guide.md) - How to access game state
- [data-flow-architecture.md](data-flow-architecture.md) - How data flows through systems
- [performance-architecture-guide.md](performance-architecture-guide.md) - Performance patterns

**Learning Docs:**
- [../Log/learnings/unity-compute-shader-coordination.md](../Log/learnings/unity-compute-shader-coordination.md) - GPU patterns
- [../Log/learnings/unity-gpu-debugging-guide.md](../Log/learnings/unity-gpu-debugging-guide.md) - GPU debugging

---

*Last Updated: 2025-10-10*
