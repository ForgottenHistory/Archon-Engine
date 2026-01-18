# Getting Started with Archon Engine

This guide will help you understand Archon Engine and get a development environment running.

## Prerequisites

Before starting, ensure you have:

- **Unity 6000+** (2023.3+, latest LTS recommended)
- **Git** for version control
- Intermediate C# knowledge
- Familiarity with Unity's component system

## Installation

### Option 1: Git Submodule (Recommended)

If your project uses Git, add Archon as a submodule:

```bash
cd YourUnityProject/Assets
git submodule add https://github.com/YourUsername/Archon-Engine.git Archon-Engine
```

This keeps Archon versioned separately and makes updates easy with `git submodule update`.

### Option 2: Manual Download

1. Download or clone the Archon-Engine repository
2. Copy the entire `Archon-Engine` folder into your Unity project's `Assets/` folder

### After Installation

1. **Open Unity** - Let it import all assets and compile scripts (may take a few minutes)

2. **Install required packages** via Package Manager (**Window → Package Manager**):
   - Click **+ → Add package by name**
   - Enter: `com.unity.nuget.newtonsoft-json`
   - Click Add

3. **Verify URP is configured**:
   - Check **Edit → Project Settings → Graphics** has a URP asset assigned
   - If not, create one: **Assets → Create → Rendering → URP Asset**

4. **Test the StarterKit**:
   - Open scene: `Assets/Archon-Engine/Scenes/StarterKit.unity`
   - Press Play
   - You should see a map with provinces, be able to click on them, and see UI panels

### Troubleshooting Installation

| Issue | Solution |
|-------|----------|
| Pink/magenta materials | URP not configured - assign URP asset in Graphics settings |
| Compiler errors about Json | Install Newtonsoft Json.NET package |
| Nothing renders | Check MapGenerator has mesh renderer assigned in Inspector |
| Console spam on Play | Check Logs/ folder for detailed error info |

## Understanding the Architecture

### The Two-Layer Model

Archon uses a **dual-layer architecture**:

```
┌─────────────────────────────────────────────────────────┐
│                    YOUR GAME                            │
│  (Economy, Buildings, Units, UI, AI - game-specific)   │
├─────────────────────────────────────────────────────────┤
│                  ARCHON ENGINE                          │
│  ┌─────────────────┐  ┌─────────────────────────────┐  │
│  │   CORE LAYER    │  │        MAP LAYER            │  │
│  │  (Simulation)   │  │     (Presentation)          │  │
│  │                 │  │                             │  │
│  │ • ProvinceState │  │ • GPU Textures              │  │
│  │ • CountryState  │  │ • Border Rendering          │  │
│  │ • Commands      │  │ • Province Selection        │  │
│  │ • EventBus      │  │ • Map Modes                 │  │
│  │ • FixedPoint64  │  │ • Visual Styles             │  │
│  └─────────────────┘  └─────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

**Key principle**: Core (simulation) is deterministic. Map (presentation) is visual-only.

### ENGINE vs GAME

| Aspect | ENGINE (Archon) | GAME (Your Code) |
|--------|-----------------|------------------|
| **Provides** | Mechanisms (HOW) | Policy (WHAT) |
| **Examples** | Command processor, EventBus, ProvinceSystem | Building costs, income formulas, AI goals |
| **Location** | `Assets/Archon-Engine/` | `Assets/Game/` |
| **Modify?** | No (unless contributing) | Yes (your game logic) |

### The StarterKit

The **StarterKit** (`Assets/Archon-Engine/Scripts/StarterKit/`) is a complete example GAME layer. It includes:

- `Initializer.cs` - Entry point, system setup
- `EconomySystem.cs` - Gold income from provinces
- `BuildingSystem.cs` - Construct buildings in provinces
- `UnitSystem.cs` - Create and move military units
- `PlayerState.cs` - Track which country the player controls
- Commands, UI, map modes, and more

**Recommended**: Study the StarterKit before building your own game.

## Project Setup Options

### Option A: Start from StarterKit (Recommended)

1. **Copy the StarterKit** to your Game folder:
   ```
   Copy: Assets/Archon-Engine/Scripts/StarterKit/*
   To:   Assets/Game/
   ```

2. **Rename the namespace** from `StarterKit` to your game name (e.g., `MyStrategy`)

3. **Modify the copied code** - it's now yours to customize

### Option B: Start from Scratch

1. **Create your Game folder**: `Assets/Game/`

2. **Create an Initializer** that waits for ENGINE, then sets up your systems:
   ```csharp
   using Engine;

   public class MyGameInitializer : MonoBehaviour
   {
       IEnumerator Start()
       {
           // Wait for ENGINE to initialize
           while (ArchonEngine.Instance == null || !ArchonEngine.Instance.IsInitialized)
               yield return null;

           // Get GameState (ENGINE provides this)
           var gameState = ArchonEngine.Instance.GameState;

           // Create your systems
           myEconomySystem = new MyEconomySystem(gameState);

           // Subscribe to events
           gameState.EventBus.Subscribe<MonthlyTickEvent>(OnMonthlyTick);
       }
   }
   ```

3. **Create your systems** as plain C# classes (not MonoBehaviours)

## Key Concepts to Understand

### 1. GameState is the Hub

`GameState` is the central access point for all ENGINE systems:

```csharp
var gameState = GameState.Instance;

// Access systems
gameState.Provinces      // Province data
gameState.Countries      // Country data
gameState.EventBus       // Event system
gameState.TryExecuteCommand()  // Execute commands

// Access queries (read-only helpers)
gameState.ProvinceQueries  // Query province data
gameState.CountryQueries   // Query country data
```

### 2. Commands for State Changes

**Never modify state directly.** Use commands:

```csharp
// ❌ WRONG - direct modification
province.ownerID = newOwner;

// ✅ CORRECT - use a command
var cmd = new TransferProvinceCommand {
    ProvinceId = provinceId,
    NewOwner = newOwner
};
gameState.TryExecuteCommand(cmd);
```

Commands enable: multiplayer sync, undo, replay, save/load.

### 3. Events for Communication

Systems communicate via EventBus:

```csharp
// Subscribe (usually in constructor or Initialize)
gameState.EventBus.Subscribe<ProvinceOwnershipChangedEvent>(OnOwnerChanged);

// Handler
void OnOwnerChanged(ProvinceOwnershipChangedEvent evt)
{
    if (evt.NewOwner == playerCountryId)
        RefreshUI();
}

// Emit (when something happens)
gameState.EventBus.Emit(new MyCustomEvent { Data = value });
```

### 4. FixedPoint64 for Determinism

**Avoid use of `float` or `double` in simulation code.** Use `FixedPoint64`:

```csharp
// ❌ WRONG - floats cause desyncs in multiplayer
float income = provinces * 1.5f;

// ✅ CORRECT - FixedPoint64 is deterministic
FixedPoint64 income = provinceCount * FixedPoint64.FromFraction(3, 2); // 1.5
```

If you're doing strictly singleplayer it's more accepted to use normal numeric datatypes, however the engine still expects FixedPoint mostly. Important for determinism.

### 5. Hot vs Cold Data

- **Hot data**: Accessed every frame (ProvinceState, CountryState) - keep small
- **Cold data**: Accessed rarely (history, detailed info) - can be larger

```csharp
// Hot: 8-byte struct, accessed constantly
ProvinceState state = gameState.Provinces.GetProvinceState(provinceId);

// Cold: loaded on-demand when player clicks
ProvinceHistoryData history = historySystem.GetHistory(provinceId);
```

## Scene Setup

A typical Archon game scene needs:

1. **ArchonEngine prefab** - Drag from `Assets/Archon-Engine/Prefabs/ArchonEngine.prefab`
   - Contains MapGenerator with all required components
   - Assign your map mesh renderer in the Inspector
   - Optionally assign your camera
2. **Your Game Initializer** - Initializes your GAME systems
3. **Map Quad** - GameObject with MeshRenderer and map material
4. **Camera** - To view the map
5. **UI Canvas** - For your UI panels

The StarterKit scene (`Assets/Archon-Engine/Scenes/StarterKit.unity`) shows a working setup.

## File Organization

Recommended structure for your game:

```
Assets/Game/
├── Initializer.cs           # Entry point
├── Systems/
│   ├── EconomySystem.cs     # Your economy logic
│   ├── BuildingSystem.cs    # Your building logic
│   └── ...
├── Commands/
│   ├── BuildCommand.cs      # Your commands
│   └── ...
├── Data/
│   ├── BuildingType.cs      # Your data definitions
│   └── ...
├── UI/
│   ├── ProvinceInfoUI.cs    # Your UI panels
│   └── ...
├── MapModes/
│   └── EconomyMapMode.cs    # Your custom map modes
└── AI/
    └── BuildEconomyGoal.cs  # Your AI goals
```

## Next Steps

1. **[Your First Game](Your-First-Game.md)** - Build a minimal game step-by-step
2. **[Cookbook](Cookbook.md)** - Common recipes and patterns
3. **[Architecture Overview](Architecture-Overview.md)** - Deeper understanding of the architecture
4. **Study StarterKit** - Read the source code in `Assets/Archon-Engine/Scripts/StarterKit/`

## Common Mistakes to Avoid

| Mistake | Why It's Bad | What To Do Instead |
|---------|--------------|---------------------|
| Modify state directly | Breaks multiplayer, undo, save/load | Use Commands |
| Use `float` in simulation | Causes desyncs across platforms | Use `FixedPoint64` |
| Create GameObjects for provinces | Doesn't scale | Use texture-based rendering |
| Allocate in hot paths | Causes GC stutters | Pre-allocate, reuse buffers |
| Put game logic in MonoBehaviours | Hard to test, lifecycle issues | Use plain C# classes |
| Ignore EventBus | Creates tight coupling | Subscribe to events |
