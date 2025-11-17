# Archon Engine Architecture - Quick Reference
**Start here for an overview of the entire system**

---

## TL;DR - Core Architecture in 30 Seconds

**Three-Layer System:**
- **CORE (Simulation):** Fixed-size structs, deterministic, FixedPoint64 math
- **MAP (Presentation):** GPU textures + compute shaders, single draw call
- **GAME (Policy):** Formulas, colors, rules, visual styles

**Key Principle:** ENGINE provides mechanisms (HOW), GAME defines policy (WHAT)

**Current Status:** Core ✅ | Map ✅ | Diplomacy ✅ | AI ✅ | Units ✅ | Multiplayer ❌ (future)

---

## System Status Overview

### Core Layer (ENGINE)

| System | Status | Doc |
|--------|--------|-----|
| **Core Architecture** | ✅ Implemented | [master-architecture-document](master-architecture-document.md) |
| **Province System** | ✅ Implemented | [core-data-access-guide](core-data-access-guide.md) |
| **Country System** | ✅ Implemented | [core-data-access-guide](core-data-access-guide.md) |
| **Diplomacy System** | ✅ Implemented | [Flat Storage Burst](flat-storage-burst-architecture.md) |
| **AI System** | ✅ Implemented | Core/FILE_REGISTRY.md |
| **Unit System** | ✅ Implemented | Core/FILE_REGISTRY.md |
| **Resource System** | ✅ Implemented | Core/FILE_REGISTRY.md |
| **Modifier System** | ✅ Implemented | [modifier-system](modifier-system.md) |
| **Command Pattern** | ✅ Implemented | [data-flow-architecture](data-flow-architecture.md) |
| **EventBus** | ✅ Implemented | [data-flow-architecture](data-flow-architecture.md) |
| **Time System** | ✅ Implemented | [time-system-architecture](time-system-architecture.md) |
| **Save/Load** | ✅ Implemented | [save-load-architecture](save-load-architecture.md) |

### Map Layer (ENGINE)

| System | Status | Doc |
|--------|--------|-----|
| **Map Rendering** | ✅ Implemented | [map-system-architecture](map-system-architecture.md) |
| **Border System** | ✅ Implemented | [vector-curve-rendering-pattern](vector-curve-rendering-pattern.md) |
| **Map Modes** | ✅ Implemented | [map-system-architecture](map-system-architecture.md) |
| **Province Selection** | ✅ Implemented | Map/FILE_REGISTRY.md |
| **Visual Styles** | ✅ Implemented | [visual-styles-architecture](visual-styles-architecture.md) |

### Infrastructure

| System | Status | Doc |
|--------|--------|-----|
| **Data Loading** | ✅ Implemented | [data-loading-architecture](data-loading-architecture.md) |
| **Data Linking** | ✅ Implemented | [data-linking-architecture](data-linking-architecture.md) |
| **Sparse Collections** | ✅ Implemented | [sparse-data-structures-design](sparse-data-structures-design.md) |
| **Performance** | ✅ Implemented | [performance-architecture-guide](performance-architecture-guide.md) |

### Future Systems (Planning/)

| System | Status | Doc |
|--------|--------|-----|
| **Multiplayer** | ❌ Not Implemented | [../Planning/multiplayer-design](../Planning/multiplayer-design.md) |
| **Modding** | ❌ Not Implemented | [../Planning/modding-design](../Planning/modding-design.md) |
| **Error Recovery** | ❌ Not Implemented | [../Planning/error-recovery-design](../Planning/error-recovery-design.md) |

**Legend:** ✅ Implemented | ❌ Not Implemented

---

## The Compact Province State (Heart of the System)

**ENGINE LAYER (8 bytes):**
- ProvinceState: ownerID (2B), controllerID (2B), terrainType (2B), gameDataSlot (2B)
- Generic primitives only - no game-specific logic

**Why separation matters:**
- **Reusability:** ENGINE can power different games (space strategy, fantasy, modern)
- **Minimal memory:** 8-byte hot state, cold data on-demand
- **Cache-friendly:** Contiguous NativeArray storage
- **Multiplayer-ready:** Fixed-size deterministic state
- **Clear ownership:** ENGINE owns mechanism, GAME owns policy

**See:** [engine-game-separation](engine-game-separation.md) for complete philosophy

---

## Architecture Layers

### Layer 1: CPU Simulation (Deterministic)
**Purpose:** Game logic, rules, state changes

**Data:**
- ProvinceState (8 bytes hot data)
- ProvinceColdData (loaded on-demand)
- Command queue

**Rules:**
- Fixed-point math only
- No rendering code
- Deterministic for multiplayer
- NativeArray storage

**See:** [master-architecture-document.md](master-architecture-document.md)

### Layer 2: GPU Presentation (High-Performance)
**Purpose:** Rendering, visual effects, UI

**Data:**
- Province ID textures (R16G16)
- Province owner textures (R16)
- Border textures (compute shader generated)
- Color palettes

**Rules:**
- Never modify game state
- Point filtering on ID textures
- Compute shaders for effects

**See:** [map-system-architecture.md](map-system-architecture.md)

---

## Key Architectural Patterns

Archon Engine uses **19 architectural patterns** that solve recurring problems in grand strategy development.

**See:** [architecture-patterns.md](architecture-patterns.md) for complete catalog with examples and decision docs

---

## Critical Rules (Must Follow)

### NEVER DO THESE
1. Process millions of pixels on CPU → Use GPU compute shaders
2. Dynamic collections in simulation → Fixed-size structs only
3. GameObjects for provinces → Textures only
4. Allocate during gameplay → Pre-allocate everything
5. Store history in hot path → Use cold data separation
6. Texture filtering on province IDs → Point filtering only
7. Float math in simulation → Fixed-point for determinism
8. Unbounded data growth → Ring buffers with compression
9. Update everything every frame → Dirty flags only

### ALWAYS DO THESE
1. Fixed-size structs for simulation
2. GPU compute shaders for visual processing
3. Single draw call for map
4. Deterministic operations (fixed-point math)
5. Hot/cold data separation
6. Command pattern for state changes
7. NativeArray for contiguous memory
8. Point filtering on province textures
9. Dirty flag systems
10. Profile at target scale

---

## Current Implementation Status

### ✅ Core Systems
- Province/Country systems with 8-byte hot state
- Command pattern with validation + execution
- EventBus with zero-allocation EventQueue<T>
- TimeManager with layered tick frequencies
- Diplomacy with flat storage + Burst compilation
- AI system with goal-based architecture
- Unit system with movement + pathfinding
- Resource/Modifier systems
- Save/Load with hybrid snapshot + command log
- Data loading with phase-based initialization

### Map Systems
- Texture-based rendering
- Border generation with vector curves + distance fields
- Map modes (Political, Terrain, etc.)
- Province selection via texture lookup (<1ms)
- Visual styles system
- Map labels with zoom-based switching

### Not Implemented
- Multiplayer networking
- Modding system

---

## Document Guide

### Start With These (Onboarding)
2. [master-architecture-document.md](master-architecture-document.md) - Complete technical architecture
3. [architecture-patterns.md](architecture-patterns.md) - 19 architectural patterns catalog
4. [engine-game-separation.md](engine-game-separation.md) - Mechanism vs Policy philosophy
5. [core-data-access-guide.md](core-data-access-guide.md) - How to access game state

### Core System Guides
- [data-flow-architecture.md](data-flow-architecture.md) - Command + Event patterns
- [time-system-architecture.md](time-system-architecture.md) - Tick-based updates
- [save-load-architecture.md](save-load-architecture.md) - Hybrid snapshot + command log
- [modifier-system.md](modifier-system.md) - Generic modifier system

### Map System Guides
- [map-system-architecture.md](map-system-architecture.md) - Texture-based rendering
- [vector-curve-rendering-pattern.md](vector-curve-rendering-pattern.md) - Border rendering
- [visual-styles-architecture.md](visual-styles-architecture.md) - Visual style system

### Data & Performance
- [data-loading-architecture.md](data-loading-architecture.md) - Phase-based initialization
- [data-linking-architecture.md](data-linking-architecture.md) - Cross-reference validation
- [sparse-data-structures-design.md](sparse-data-structures-design.md) - Scale-safe storage
- [flat-storage-burst-architecture.md](flat-storage-burst-architecture.md) - Burst-compiled systems
- [performance-architecture-guide.md](performance-architecture-guide.md) - Optimization patterns

### UI System
- [ui-architecture.md](ui-architecture.md) - UI Toolkit + Presenter Pattern

### File Registries (What Exists and Where)
- [../../Scripts/Core/FILE_REGISTRY.md](../../Scripts/Core/FILE_REGISTRY.md) - Core layer catalog
- [../../Scripts/Map/FILE_REGISTRY.md](../../Scripts/Map/FILE_REGISTRY.md) - Map layer catalog
- [../../../Game/FILE_REGISTRY.md](../../../Game/FILE_REGISTRY.md) - Game layer catalog

---

## FAQ

**"Where do I add [feature]?"**
→ Check FILE_REGISTRY.md files first - system may already exist

**"Should I use floats in simulation?"**
→ NO. Use FixedPoint64 for determinism (Pattern 5)

**"How do I change game state?"**
→ Create ICommand, execute via CommandProcessor (Pattern 2)

**"How do I notify other systems of changes?"**
→ EventBus.Publish<EventType>() (Pattern 3)

**"Can I add fields to ProvinceState (ENGINE)?"**
→ ONLY if you keep it exactly 8 bytes. Otherwise use HegemonProvinceData (GAME) or cold data

**"How do I render provinces?"**
→ GPU textures + shaders via MapTextureManager. See [map-system-architecture.md](map-system-architecture.md)

**"How do I make updates efficient?"**
→ Dirty flags + layered tick frequencies. See [time-system-architecture.md](time-system-architecture.md)

**"How do I access province/country data?"**
→ See [core-data-access-guide.md](core-data-access-guide.md)

**"What architectural pattern should I use?"**
→ See [architecture-patterns.md](architecture-patterns.md)

**"Is this ENGINE or GAME logic?"**
→ See [engine-game-separation.md](engine-game-separation.md) - Mechanisms vs Policy

---

## Related Master Documents

**Essential Reading:**
- [master-architecture-document.md](master-architecture-document.md) - Complete technical architecture
- [architecture-patterns.md](architecture-patterns.md) - 19 pattern catalog
- [engine-game-separation.md](engine-game-separation.md) - Philosophy and principles

**File Catalogs:**
- [Core/FILE_REGISTRY.md](../../Scripts/Core/FILE_REGISTRY.md) - What exists in Core layer
- [Map/FILE_REGISTRY.md](../../Scripts/Map/FILE_REGISTRY.md) - What exists in Map layer
- [Game/FILE_REGISTRY.md](../../../Game/FILE_REGISTRY.md) - What exists in Game layer

---

*Last Updated: 2025-11-17*
*Status: Reflects current production implementation*