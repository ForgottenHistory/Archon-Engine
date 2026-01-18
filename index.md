# Archon Engine API Documentation

Archon Engine is a grand strategy game framework built on Unity, featuring a dual-layer architecture with deterministic simulation (CPU) and high-performance presentation (GPU).

## Quick Links

- [API Reference](api/index.md) - Full API documentation
- [Architecture Docs](Docs/Engine/master-architecture-document.md) - Design principles

## Core Namespaces

| Namespace | Description |
|-----------|-------------|
| `Core` | Simulation layer - GameState, Systems, Commands |
| `Core.Systems` | Province, Country, Diplomacy, Adjacency systems |
| `Core.Queries` | Fluent query builders for provinces, countries, units |
| `Core.Commands` | Command pattern infrastructure |
| `Core.Units` | Unit system with movement and sparse storage |
| `Core.Validation` | Fluent validation framework |
| `Map` | Rendering layer - textures, shaders, interaction |
| `StarterKit` | Example game implementation |

## Key Patterns

- **Command Pattern** - All state changes through validated commands
- **Event-Driven** - Zero-allocation EventBus for system communication
- **Query Builders** - Fluent APIs for filtering provinces, countries, units
- **Engine-Game Separation** - ENGINE provides mechanism, GAME defines policy

## Getting Started

See the [StarterKit README](Scripts/StarterKit/README.md) for a working example game built on Archon Engine.
