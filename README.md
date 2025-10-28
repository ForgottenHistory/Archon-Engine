# Archon Engine

A Unity-based game engine for grand strategy games, built around a dual-layer architecture that separates deterministic simulation from GPU-accelerated presentation.

Currently in **ACTIVE development**, not recommended for real use yet. Documentation is about 1:1 size as code, feel free to take a look.

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

## Performance Results

Tested with ~4,000 provinces (see `Docs/Log/2025-10-05/2025-10-05-4-core-stress-tests-eventbus-zero-allocation.md`):
- **Province updates**: 0.24ms (target: <5ms)
- **EventBus**: 0.85ms, zero allocations
- **Fixed-point math**: 0.13ms for 10k calculations
- **Memory stability**: Stable over 400+ simulated years

## Structure

```
Assets/Archon-Engine/
├── Scripts/
│   ├── Core/           # Deterministic simulation layer
│   └── Map/            # GPU-accelerated presentation
├── Shaders/            # Compute shaders for rendering
├── Docs/
│   ├── Engine/         # Architecture documentation
│   ├── Planning/       # Future features
│   └── Log/            # Development journal
└── CLAUDE.md           # AI development guide
```

## Documentation

**Start here:**
- [Docs/Engine/ARCHITECTURE_OVERVIEW.md](Docs/Engine/ARCHITECTURE_OVERVIEW.md) - Quick overview
- [Docs/Engine/master-architecture-document.md](Docs/Engine/master-architecture-document.md) - Complete architecture
- [Scripts/Core/FILE_REGISTRY.md](Scripts/Core/FILE_REGISTRY.md) - Core layer catalog
- [Scripts/Map/FILE_REGISTRY.md](Scripts/Map/FILE_REGISTRY.md) - Map layer catalog

## Requirements

- Unity 2023.3+
- Universal Render Pipeline (URP)
- IL2CPP scripting backend
- Burst Compiler enabled

## License

MIT License - See [LICENSE](LICENSE) file for details.
