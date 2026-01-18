# Archon Engine Wiki

**Archon** is a high-performance grand strategy game engine built in Unity. It provides the foundation for building games like Europa Universalis, Crusader Kings, or Victoria—with support for thousands of provinces, deterministic multiplayer, and modding.

## Quick Links

| Getting Started | Reference | Advanced |
|-----------------|-----------|----------|
| [Getting Started](Getting-Started.md) | [Architecture Overview](Architecture-Overview.md) | [API Documentation](../api/index.html) |
| [Your First Game](Your-First-Game.md) | [Cookbook](Cookbook.md) | [Engine Architecture Docs](../Engine/) |
| [Project Setup](Project-Setup.md) | [Troubleshooting](Troubleshooting.md) | [Session Logs](../Log/) |

## What is Archon?

Archon is split into two parts:

1. **ENGINE** (`Assets/Archon-Engine/`) - Reusable grand strategy infrastructure
   - Province/country systems with GPU-accelerated map rendering
   - Command pattern for deterministic state changes
   - Event-driven architecture with zero allocations
   - Save/load, pathfinding, modifiers, time management

2. **GAME** (your code) - Game-specific rules and content
   - Economy formulas, building effects, unit stats
   - UI panels and player interactions
   - AI goals and decision-making
   - Custom map modes and visualizations

The **StarterKit** (`Assets/Archon-Engine/Scripts/StarterKit/`) demonstrates how to build a GAME layer using the ENGINE.

## Core Concepts

### Dual-Layer Architecture
- **CPU Simulation**: Deterministic game state using fixed-point math
- **GPU Presentation**: High-performance rendering using textures, not meshes

### ENGINE vs GAME Separation
- **ENGINE provides mechanisms** (HOW things work)
- **GAME defines policy** (WHAT happens)

Example: ENGINE provides `ModifierSystem` for applying bonuses. GAME defines that farms give +1 local income.

### Command Pattern
All state changes flow through commands:
```csharp
// GAME layer creates command
var cmd = new ConstructBuildingCommand { ProvinceId = 42, BuildingType = "farm" };

// ENGINE validates and executes
commandProcessor.Execute(cmd);
```

This enables: multiplayer sync, replay, undo, and save/load.

### Event-Driven Communication
Systems communicate via EventBus (zero-allocation structs):
```csharp
// Subscribe to events
gameState.EventBus.Subscribe<BuildingConstructedEvent>(OnBuildingBuilt);

// Emit events
gameState.EventBus.Emit(new BuildingConstructedEvent { ProvinceId = 42 });
```

## Project Structure

```
Assets/
├── Archon-Engine/           # ENGINE (don't modify unless contributing)
│   ├── Scripts/
│   │   ├── Core/            # Simulation: provinces, countries, commands
│   │   ├── Map/             # Rendering: textures, borders, selection
│   │   ├── StarterKit/      # Example GAME layer (copy this!)
│   │   └── Utils/           # Logging, circular buffers
│   └── Docs/
│       ├── Wiki/            # You are here
│       ├── Engine/          # Architecture documentation
│       └── Log/             # Development session logs
│
└── Game/                    # YOUR GAME (modify freely)
    ├── Systems/             # Your game systems
    ├── Commands/            # Your commands
    ├── UI/                  # Your UI panels
    └── ...
```

## Requirements

- **Unity 2022.3 LTS** or newer (2023.x recommended)
- **Universal Render Pipeline (URP)**
- **Burst Compiler** package
- **Collections** package (for NativeArray)

## Next Steps

1. **New to Archon?** → Start with [Getting Started](Getting-Started.md)
2. **Want to build a game?** → Follow [Your First Game](Your-First-Game.md)
3. **Looking for examples?** → Check the [Cookbook](Cookbook.md)
4. **Something not working?** → See [Troubleshooting](Troubleshooting.md)

## Getting Help

- Check the [Troubleshooting](Troubleshooting.md) guide
- Search the [API Documentation](../api/index.html)
- Review the [StarterKit source code](../../Scripts/StarterKit/)

## Contributing

Archon Engine is developed alongside Hegemon. If you find bugs or have improvements:
1. Check existing issues in the repository
2. Create a detailed bug report or feature request
3. For code contributions, follow the patterns in [Architecture Overview](Architecture-Overview.md)
