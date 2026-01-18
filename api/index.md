# API Reference

Welcome to the Archon Engine API reference documentation.

This section contains detailed API documentation for all public types, methods, and properties in the Archon Engine.

## Assemblies

- **Core** - Deterministic simulation layer (commands, events, systems, data structures)
- **Map** - GPU-based map rendering and interaction
- **StarterKit** - Ready-to-use implementations for common grand strategy features
- **Utils** - Shared utilities and helpers
- **Engine** - Engine coordination and initialization

## Quick Links

### Core Systems
- @Core.GameState - Central game state container
- @Core.EventBus - Zero-allocation event system
- @Core.Systems.ProvinceSystem - Province management
- @Core.Systems.CountrySystem - Country management
- @Core.Systems.TimeManager - Game time and calendar

### Commands
- @Core.Commands.ICommand - Command interface for state changes
- @Core.Commands.CommandProcessor - Command execution
- @Core.Commands.SimpleCommand - Simplified command base class

### Data Types
- @Core.Data.FixedPoint64 - Deterministic fixed-point math
- @Core.Data.ProvinceState - Province simulation state (8-byte struct)
- @Core.Data.Ids.ProvinceId - Type-safe province identifier
- @Core.Data.Ids.CountryId - Type-safe country identifier

### Map Rendering
- @Map.Core.MapInitializer - Map initialization
- @Map.MapModes.GradientMapMode - Data visualization map modes
- @Map.Province.ProvinceSelector - Mouse-based province selection
