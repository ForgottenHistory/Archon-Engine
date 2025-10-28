# Archon Engine

A Unity-based game engine for grand strategy games, built around a dual-layer architecture that separates deterministic simulation from GPU-accelerated presentation.

**Solo-developed using AI-assisted methodology.** This project explores whether one architect directing AI specialists can build systems that traditionally require teams.

Currently in **ACTIVE development**, not recommended for production use. Documentation is approximately 1:1 with code size - architecture docs, session logs, and decision records are maintained as the foundation for AI collaboration.

**Quick Links:**
- [Docs/Engine/ARCHITECTURE_OVERVIEW.md](Docs/Engine/ARCHITECTURE_OVERVIEW.md) - Architecture overview
- [Docs/CURRENT_FEATURES.md](Docs/CURRENT_FEATURES.md) - Complete feature list

Free open-source game demo will be released later.

## Core Architecture

**Dual-Layer Design**
```
┌─────────────────────────────────┐
│   Map Layer (Presentation)      │
│   - Texture-based rendering      │
│   - GPU compute shaders          │
│   - Single draw call             │
└───────────┬─────────────────────┘
            │ Events (one-way)
            ↓
┌─────────────────────────────────┐
│   Core Layer (Simulation)       │
│   - 8-byte province structs      │
│   - Fixed-point deterministic    │
│   - Command pattern              │
└─────────────────────────────────┘
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
- Vector curve borders: 720KB curve data vs 40MB rasterized (55× compression)
- Resolution-independent borders (sharp at any zoom level)

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
│   └── Map/            # GPU-accelerated presentation
├── Shaders/            # Compute shaders for rendering
└── Docs/
    ├── Engine/         # Architecture documentation
    ├── Planning/       # Future features
    └── Log/            # Development journal
```

## Development Status

**Core Engine (Complete):**
- Dual-layer architecture with hot/cold data separation
- Province, Country, Diplomacy, Unit, and Pathfinding systems
- Vector curve border rendering with spatial acceleration
- Save/load system with command pattern
- Zero-allocation EventBus and performance optimizations
- AI system with goal-oriented behavior and bucketing scheduler

**Game Layer (In Progress):**
- Economic system and resource management
- Building construction and development
- Map modes and visualization

**Planned Features:**
- Multiplayer (lockstep command synchronization)
- Modding API (C# scripting support)
- Advanced AI (heat-map tactical positioning)

## Development Methodology

This engine is built using an "AI CTO" model: one human architect defines the vision, constraints, and architecture, while AI handles implementation. This approach requires rigorous documentation discipline - every architectural decision is recorded not just for humans, but as the instruction manual for AI.

**Key to scalability:**
- Architecture documents define immutable constraints (8-byte structs, deterministic simulation)
- Session logs capture decisions and rationale
- FILE_REGISTRY documents catalog all systems
- Documentation serves as the instruction manual for AI collaboration

## Documentation

**Architecture:**
- [Docs/Engine/master-architecture-document.md](Docs/Engine/master-architecture-document.md) - Complete architecture
- [Docs/Engine/ARCHITECTURE_OVERVIEW.md](Docs/Engine/ARCHITECTURE_OVERVIEW.md) - Quick overview
- [Scripts/Core/FILE_REGISTRY.md](Scripts/Core/FILE_REGISTRY.md) - Core layer catalog
- [Scripts/Map/FILE_REGISTRY.md](Scripts/Map/FILE_REGISTRY.md) - Map layer catalog

## Technical Requirements

- Unity 2023.3+
- Universal Render Pipeline (URP)
- IL2CPP scripting backend
- Burst Compiler enabled

## License

MIT License - See [LICENSE](LICENSE) file for details.
