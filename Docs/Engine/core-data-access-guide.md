# Core Data Access Guide
## How to Get Data from Core in Archon's Architecture

**Implementation Status:** ✅ Implemented (ProvinceState, ProvinceColdData, hot/cold separation)

---

## Overview

Archon uses a **dual-layer architecture**:
- **Core Layer (CPU)**: Deterministic simulation with hot data (compact structs)
- **Map Layer (GPU)**: High-performance presentation reading from Core

This guide explains how to properly access Core simulation data for any purpose - rendering, UI, AI, etc.

---

## Architecture Principles

### The Right Way
```
Your Code → GameState → Query Classes → Core Systems → Data
```

### Wrong Way (Don't Do This)
```
Your Code → Direct System Access → Data
```

### Core Owns Data, Map Reads Data
- Core systems (ProvinceSystem, CountrySystem) own authoritative data
- Map layer reads from Core via queries for rendering
- Never store Core data copies in Map layer

---

## Getting Started: The Central Hub

All Core data access goes through `GameState`:

**Pattern**: Get GameState instance, access query interfaces (ProvinceQueries, CountryQueries), verify initialization status before accessing

---

## Data Access Patterns

### 1. Basic Queries (Ultra-Fast)

Direct access to hot data with no computation. Examples include getting province owner, development, terrain, checking existence, and country data like colors and tags.

### 2. Computed Queries (On-Demand)

Calculations performed when requested. Examples include getting all provinces owned by a country or filtering provinces by criteria. Remember to dispose NativeArrays using `using` statement or manual disposal.

### 3. Cross-System Queries

Combines multiple systems. Examples include getting the color of the country that owns a province, getting the tag of the province owner, or checking if two provinces share the same owner.

### 4. Cached Queries (Expensive Calculations)

These are cached automatically for performance. Examples include total development, province count, and average development for countries.

---

## Province Development Calculation

Development is calculated as: **BaseTax + BaseProduction + BaseManpower** (capped at 255)

The calculation happens in Core during data loading. To get the final development value, use province queries. Individual components are stored in cold data (ProvinceInitialState) and accessed through the province history system if needed.

---

## Common Use Cases

### Use Case 1: Map Rendering (GPU Textures)

Pattern for map modes updating GPU textures:
- Get GameState reference
- Access ProvinceQueries
- Get all province IDs
- Update color palette based on Core data
- Apply changes to texture manager

**Other common patterns**: UI Display (query owner/dev/terrain for panels), AI Decision Making (evaluate strength via total development), Data Analysis/Statistics (aggregate queries for reporting). All follow the same GameState → ProvinceQueries/CountryQueries pattern.

---

## Performance Guidelines

### Memory Management

Always use `using` for automatic disposal or manual try-finally blocks to ensure NativeArrays are disposed. Never forget to dispose - memory leaks will occur.

### Allocator Choice

- **Allocator.Temp**: For short-lived data (within same frame)
- **Allocator.TempJob**: For data passed between frames or jobs (remember to dispose later)
- **Allocator.Persistent**: For permanent data (rare, must dispose manually when no longer needed)

### Caching Considerations

- Certain queries are automatically cached (like total development)
- Invalidate cache when data changes
- Clear all cache periodically at end of major game events

---

## Anti-Patterns (DON'T DO THESE)

### Direct System Access
Don't bypass the query layer by accessing systems directly.

### Storing Core Data in Map Layer
Map layer should not store Core data - always read fresh from Core instead.

### Forgetting to Dispose Native Arrays
Always dispose NativeArrays to prevent memory leaks.

### CPU Processing of Millions of Pixels
Use GPU compute shaders instead of CPU loops for pixel processing.

---

## Advanced Topics

### Command Pattern for State Changes

Use commands to modify Core state with Execute, Serialize, and Validate methods. Try executing command through GameState and query the updated state.

### Event-Driven Updates

Subscribe to events through GameState EventBus to react to changes in game state.

### Hot vs Cold Data

- **Hot data**: Available through queries (compact ProvinceState) - frequently accessed
- **Cold data**: Historical events and detailed info - accessed rarely

---

## Key Script Files Reference

| Component | Location | Purpose |
|-----------|----------|---------|
| **GameState.cs** | `Core/` | Central coordinator, entry point for all Core data |
| **ProvinceSystem.cs** | `Core/Systems/` | Province data owner (compact ProvinceState in NativeArray) |
| **CountrySystem.cs** | `Core/Systems/` | Country data owner (hot/cold data separation) |
| **ProvinceQueries.cs** | `Core/Queries/` | Optimized province data access (fast basic, moderate computed queries) |
| **CountryQueries.cs** | `Core/Queries/` | Optimized country data access (cached queries) |
| **ProvinceState.cs** | `Core/Data/` | Compact struct (owner, controller, development, terrain, flags) |
| **ICommand.cs** | `Core/Commands/` | Command interface for state changes |
| **EventBus.cs** | `Core/` | Inter-system communication |

**File Organization Pattern**: `Core/Systems/` (data owners) → `Core/Queries/` (read access) → `Core/Commands/` (write operations) → `Core/Data/` (structures)

**Integration Points**: Map Layer → Queries only | State Changes → Commands | Communication → EventBus | Data Flow: Raw → Jobs → InitialState → ProvinceState → Queries

---

## Related Documents

- **MapMode System Architecture** - Shows practical usage of Core data access in map rendering
- **Master Architecture Document** - Architecture context and dual-layer system overview

## Summary

1. **Always access Core data through `GameState` and Query classes**
2. **Never access systems directly**
3. **Use appropriate Allocator types and dispose native arrays**
4. **Let Core own the data, read it for presentation**
5. **Use Commands for state changes, Events for notifications**
6. **Cache is handled automatically for expensive queries**

This architecture ensures:
- **Performance**: Query layer is optimized for common access patterns
- **Determinism**: Core simulation remains predictable
- **Modularity**: Clear separation between simulation and presentation
- **Scalability**: Handles many provinces efficiently

---

**Remember**: Core is the single source of truth. Map layer renders what Core tells it. Everything else reads from Core through the Query system.

---

*Last Updated: 2025-10-15*
