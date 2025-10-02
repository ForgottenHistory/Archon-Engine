# Archon Engine

A high-performance grand strategy game engine for Unity, built for deterministic simulation and GPU-accelerated rendering.

## Features

### Core Architecture
- **Deterministic Simulation**: Fixed-point arithmetic (FixedPoint64) for multiplayer-safe calculations
- **Command Pattern**: All state changes go through `ICommand` for replay and validation
- **Event-Driven**: Zero-allocation EventBus with object pooling
- **Dual-Layer Design**: CPU simulation + GPU presentation decoupling

### Rendering System
- **GPU-First**: Compute shader-based map rendering
- **Single Draw Call**: Texture-based province rendering for 10k+ provinces
- **Real-time Visual Updates**: Border detection, texture population, map mode switching
- **Texture Selection**: Sub-millisecond province selection via GPU readback

### Data Management
- **Memory-Efficient**: 8-byte ProvinceState struct (80KB for 10k provinces)
- **Hot/Cold Separation**: Frequently vs rarely accessed data
- **Burst Compilation**: Parallel processing for performance-critical code
- **Modern Formats**: JSON5, CSV, YAML, BMP (no custom parsers)

## Tech Stack

- **Unity 2022.3+**
- **C# with Burst compilation**
- **Compute Shaders (HLSL)**
- **Unity Jobs System**
- **NativeCollections**

## Project Structure

```
Archon/
├── Assets/
│   ├── Scripts/
│   │   ├── Core/           # Simulation systems (ProvinceSystem, CountrySystem, TimeManager)
│   │   ├── Map/            # GPU rendering pipeline (compute shaders, texture management)
│   │   ├── ParadoxParser/  # Data loaders (BMP, CSV, YAML)
│   │   └── Utils/          # Shared utilities (FixedPoint64, EventBus, logging)
│   ├── Docs/
│   │   ├── Engine/         # Architecture documentation
│   │   └── Log/            # Development notes and audits
│   └── Data/               # Game data files (JSON5, CSV, YAML)
```

## Current State

**Implemented:**
- Province and country simulation infrastructure
- GPU-based map rendering with compute shaders
- Texture-based province selection system
- Event-driven architecture with zero allocations
- Time management (hourly/monthly ticks)
- Data loading from JSON5/CSV/YAML/BMP
- Fixed-point deterministic math library

**In Development:**
- Gameplay systems (economy, military, diplomacy)
- User interface layer
- Command validation and replay

## Performance Targets

- **10,000 provinces**: Target architecture scale
- **Province updates**: <5ms per frame
- **Event processing**: <5ms with zero allocations
- **Map rendering**: Single draw call, GPU compute
- **Province selection**: <1ms via texture lookup

## Getting Started

1. Open project in Unity 2022.3+
2. Load scene: `Assets/Scenes/MainScene.unity`
3. Press Play to run simulation

## Documentation

- [Architecture Overview](Assets/Docs/Engine/ARCHITECTURE_OVERVIEW.md)
- [Engine/Game Separation](Assets/Docs/Engine/engine-game-separation.md)
- [GPU Debugging Guide](Assets/Docs/Log/learnings/unity-gpu-debugging-guide.md)
- [File Registries](Assets/Scripts/Core/FILE_REGISTRY.md)

## License

Proprietary
