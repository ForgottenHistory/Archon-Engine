# Archon Engine

A Unity-based game engine for grand strategy games, built around a dual-layer architecture that separates deterministic simulation from GPU-accelerated presentation.

![Archon-Engine](Promotion-Assets/Images/hero2.png)

APIs should be stable by 2026 Q1. Until a release pops up, production use is discouraged. Free open-source game demo will be released in 2026 Q2 to showcase practical integration.

**GAME DATA IS NOT PROVIDED IN PROJECT!**

There is no *"How To Get Started"* guide yet, partly because I'm trying to figure how to get it going too. It's not exactly a small project to develop nor work with. I'll try my best to create something eventually however expect this to be a complex project and it should be treated as such.

**Quick Links:**
- [Docs/Engine/ARCHITECTURE_OVERVIEW.md](Docs/Engine/ARCHITECTURE_OVERVIEW.md) - Architecture overview
- [Docs/CURRENT_FEATURES.md](Docs/CURRENT_FEATURES.md) - Complete feature list

Documentation is extensive and I *try* to keep it up to date. However expect some sloppyness.

![Flat-Map](Promotion-Assets/Images/flat_map.png)
2D map loaded using Europa Universalis 4 data.

## Why

Grand strategy is notorious for being extremely complex. Even for experienced studios it's a daunting task to set everything up. It essentially has given monopoly to Paradox, with only exceptionally few coming close to their quality and scale. And for good reason.

Some extreme hurdles for Paradox-like grand strategy:
- Vector like graphics to scale infinitely
- Create beautiful maps from simple bitmaps/pngs
- Fixed-Point arithmetic & deterministic simulation
- Data oriented design (not OOP)
- AI, Diplomacy, Military, Economy as core pillars
- Modifiers, resources, relations, unit movements, sparse storage
- Modding capability

I could go on.

Archon-Engine is designed to be generic infrastructure, providing everything you need from day 1. You can focus on creating content rather than researching the 1% topics barely anyone knows about.

**This not a dunk on Paradox.** I love their games. I just wish they didn't lag so goddamn much.

## Core Architecture

**Dual-Layer Design**
```
┌──────────────────────────────────┐
│   Map Layer (Presentation)       │
│   - Texture-based rendering      │
│   - GPU compute shaders          │
│   - Single draw call             │
└───────────┬──────────────────────┘
            │ Events (one-way)
            ↓
┌──────────────────────────────────┐
│   Core Layer (Simulation)        │
│   - 8-byte province structs      │
│   - Fixed-point deterministic    │
│   - Command pattern              │
└──────────────────────────────────┘
```

**Key Design Decisions**
- **Fixed-size data**: 8-byte `ProvinceState` structs prevent late-game performance degradation
- **Deterministic math**: Fixed-point calculations (no floats) for multiplayer-ready simulation
- **Texture-based map**: Provinces rendered as pixels, not GameObjects (single draw call)
- **GPU compute shaders**: All visual processing (borders, effects) on GPU
- **Command pattern**: All state changes go through commands for determinism and networking
- **Zero allocations**: Hot paths use pre-allocated memory and value types

## What Makes This Different

**Memory Efficiency:**
- 8-byte province structs: 10,000 provinces = 80KB hot data
- Hot/cold data separation for cache-friendly access
- Zero-allocation EventBus (99.99% allocation reduction)

**Rendering Performance:**
- Single draw call for entire map
- 3D MAP TO BE DETERMINED

**Multiplayer-Ready Architecture:**
- Deterministic fixed-point math (no floats in simulation)
- Command pattern for all state changes
- Built for lockstep synchronization from day one

**Tested with ~4,000 provinces:**
- Province updates: 0.24ms (target: <5ms)
- EventBus: 0.85ms, zero allocations
- Fixed-point math: 0.13ms for 10k calculations
- Memory stability: Stable over 400+ simulated years

## Structure

```
Assets/Archon-Engine/
├── Scripts/
│   ├── Core/           # Deterministic simulation layer
│   ├── Map/            # GPU-accelerated presentation
│   └── StarterKit      # Scripts to start from/look at
├── Shaders/            # Compute shaders for rendering
└── Docs/

    ├── Engine/         # Architecture documentation
    ├── Planning/       # Future features
    └── Log/            # Development journal
```

## StarterKit

A minimal working game template demonstrating ENGINE patterns. Use it as a learning reference or starting point for new games. Currently WIP.

**Scene:** `Assets/Archon-Engine/Scenes/StarterKit.unity`

**What's Included (WORK IN PROGRESS):**
- **EconomySystem** - Simple gold economy (1 gold/province/month + building bonuses)
- **UnitSystem** - Military units with movement and combat stats
- **BuildingSystem** - Province buildings with instant construction
- **AISystem** - Basic AI that builds in owned provinces
- **Command Pattern** - All state changes through commands (add_gold, create_unit, build, etc.)
- **UI Components** - Country selection, resource bar, province info, unit management

**Data Files:**
- Unit types: `Template-Data/units/*.json5`
- Building types: `Template-Data/buildings/*.json5`

StarterKit demonstrates the recommended patterns without the complexity of a full game implementation. See [Scripts/StarterKit/README.md](Scripts/StarterKit/README.md) for details.

![StarterKit](Promotion-Assets/Images/StarterKit.png)
*Work in progress. Generated template data as I can't share real game data, for obvious reasons.*

## Development Status

**Core Engine (Complete):**
- Dual-layer architecture with hot/cold data separation
- Province, Country, Diplomacy, Unit, and Pathfinding systems
- Resource & modifiers system
- Save/load system with command pattern
- Zero-allocation EventBus and performance optimizations
- AI system with goal-oriented behavior

**Game Layer (In Progress):**

As in, practical implementation of core systems.

- Core pillars (Economy, Military, Diplomacy, AI)
- Vector like borders, razor thin
- 3D terrain tessallation and smart texturing for realistic terrain

![early-3d-map](Promotion-Assets/Images/simple_terrain.png)
*Simple province terrain, auto terrain assignment from texture*

**Planned Features:**
- Multiplayer (lockstep command synchronization)
- Modding API (C# scripting support)
- Advanced AI

## Documentation

**Architecture:**
- [Docs/Engine/master-architecture-document.md](Docs/Engine/master-architecture-document.md) - Complete architecture
- [Docs/Engine/ARCHITECTURE_OVERVIEW.md](Docs/Engine/ARCHITECTURE_OVERVIEW.md) - Quick overview
- [Scripts/Core/FILE_REGISTRY.md](Scripts/Core/FILE_REGISTRY.md) - Core layer catalog
- [Scripts/Map/FILE_REGISTRY.md](Scripts/Map/FILE_REGISTRY.md) - Map layer catalog

## Screenshots

![early-3d-map](Promotion-Assets/Images/early_3d_map.png)
WIP 3D map with texturing

![Heightmap-example](Promotion-Assets/Images/tessallation_heightmap.png)
3D tessallated terrain WIP.

## Technical Requirements

- Unity 2023.3+ (6000+, preferably latest LTS version)
- Universal Render Pipeline (URP)
- IL2CPP scripting backend
- Burst Compiler enabled
- UI Toolkit

## License

MIT License - See [LICENSE](LICENSE) file for details.
