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

### Game Layer (GAME)

| System | Status | Doc |
|--------|--------|-----|
| **Economy System** | ✅ Implemented | Game/FILE_REGISTRY.md |
| **Building System** | ✅ Implemented | Game/FILE_REGISTRY.md |
| **UI System** | ✅ Implemented | [ui-architecture](ui-architecture.md) |
| **Map Labels** | ✅ Implemented | Game/FILE_REGISTRY.md |
| **Camera Controller** | ✅ Implemented | Game/FILE_REGISTRY.md |

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

**GAME LAYER (8 bytes):**
- HegemonProvinceData: baseTax, baseProduction, baseManpower, unrest
- Game-specific mechanics via separate NativeArray indexed by gameDataSlot

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
- Fixed-point math only (no floats!)
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
- Single draw call for map
- Compute shaders for effects

**See:** [map-system-architecture.md](map-system-architecture.md)

---

## Key Architectural Patterns

Archon Engine uses **19 battle-tested architectural patterns** that solve recurring problems in grand strategy development.

**Most Critical Patterns:**

1. **Engine-Game Separation** - Mechanisms vs Policy (reusable engine)
2. **Command Pattern** - All state changes for multiplayer/replay/undo
3. **Event-Driven Architecture** - Zero-allocation EventBus
4. **Hot/Cold Data Separation** - Cache-friendly by access frequency
5. **Fixed-Point Determinism** - FixedPoint64 for cross-platform sync
6. **Facade Pattern** - High-level coordinators delegate to specialists
11. **Dirty Flag System** - Only update what changed
17. **Single Source of Truth** - ONE owner per data relationship
19. **UI Presenter Pattern** - 4-5 component panels for complex UI

**Quick Selection Guide:**
- Need to change state? → Command Pattern (Pattern 2)
- Cross-system notification? → EventBus (Pattern 3)
- Complex UI panel? → UI Presenter (Pattern 19)
- Deterministic math? → FixedPoint64 (Pattern 5)
- Expensive calculation? → Frame-Coherent Cache (Pattern 10)

**See:** [architecture-patterns.md](architecture-patterns.md) for complete catalog with examples and decision docs

---

## Performance Targets

### Memory
- **Simulation:** Minimal footprint for province data
- **GPU Textures:** Bounded VRAM usage
- **Total:** Strict memory budget

### Performance
- **Single-Player:** High frame rate at scale
- **Multiplayer:** Smooth performance target
- **Province Selection:** Sub-millisecond response
- **Map Updates:** Fast refresh rate

### Network (Multiplayer - Future)
- **Bandwidth:** Minimal per client
- **State Sync:** Compact full state
- **Command Size:** Small packets

**See:** [performance-architecture-guide.md](performance-architecture-guide.md)

---

## Critical Rules (Must Follow)

### ❌ NEVER DO THESE
1. Process millions of pixels on CPU → Use GPU compute shaders
2. Dynamic collections in simulation → Fixed-size structs only
3. GameObjects for provinces → Textures only
4. Allocate during gameplay → Pre-allocate everything
5. Store history in hot path → Use cold data separation
6. Texture filtering on province IDs → Point filtering only
7. Float math in simulation → Fixed-point for determinism
8. Unbounded data growth → Ring buffers with compression
9. Update everything every frame → Dirty flags only

### ✅ ALWAYS DO THESE
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

### ✅ Core Systems (Production Ready)
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

### ✅ Map Systems (Production Ready)
- Texture-based rendering (single draw call)
- Border generation with vector curves + distance fields
- Map modes (Political, Terrain, Development, Economy, etc.)
- Province selection via texture lookup (<1ms)
- Visual styles system (EU3 Classic implemented)
- Map labels with zoom-based switching

### ✅ Game Systems (Production Ready)
- Economy with tax collection + income calculation
- Building construction with validation + progress tracking
- UI with Presenter Pattern (4-5 component architecture)
- Camera with Paradox-style controls + zoom-based fog of war
- Debug console with command execution

### ❌ Not Implemented (Future)
- Multiplayer networking
- Modding system
- Additional visual styles (Imperator, Modern)

---

## Document Guide

### Start With These (Onboarding)
1. **This Document** - Quick overview (you are here!)
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

### Future Planning (Not Implemented)
- [../Planning/multiplayer-design.md](../Planning/multiplayer-design.md) - Multiplayer architecture
- [../Planning/modding-design.md](../Planning/modding-design.md) - Modding system
- [../Planning/error-recovery-design.md](../Planning/error-recovery-design.md) - Error recovery

---

## Quick Decision Tree

**"Where do I add [feature]?"**
→ Check FILE_REGISTRY.md files first - system may already exist

**"Should I use floats in simulation?"**
→ ❌ NO. Use FixedPoint64 for determinism (Pattern 5)

**"How do I change game state?"**
→ Create ICommand, execute via CommandProcessor (Pattern 2)

**"How do I notify other systems of changes?"**
→ EventBus.Publish<EventType>() (Pattern 3)

**"Can I add fields to ProvinceState (ENGINE)?"**
→ ⚠️ ONLY if you keep it exactly 8 bytes. Otherwise use HegemonProvinceData (GAME) or cold data

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

## Next Steps for New Developers

**Day 1: Understand the Vision**
1. Read this document (you're doing it!)
2. Read [engine-game-separation.md](engine-game-separation.md) - The "why" behind everything
3. Read [architecture-patterns.md](architecture-patterns.md) - Recognize the patterns

**Day 2: Learn the Technical Architecture**
4. Read [master-architecture-document.md](master-architecture-document.md) - Complete technical overview
5. Read [core-data-access-guide.md](core-data-access-guide.md) - How to access state
6. Browse FILE_REGISTRY.md files - Know what exists and where

**Day 3: Start Coding**
7. Pick a small feature from Game layer
8. Use existing patterns (Command, EventBus, Facade)
9. Check architecture compliance before implementing

**Always:**
- Check FILE_REGISTRY.md before creating new systems
- Use architectural patterns (Pattern 1-19)
- Ask when architecture seems violated

---

## When You Need Help

**"I need the big picture"** → This document + [master-architecture-document.md](master-architecture-document.md)

**"I need to understand a pattern"** → [architecture-patterns.md](architecture-patterns.md)

**"I need to add game logic"** → [engine-game-separation.md](engine-game-separation.md) - ENGINE or GAME?

**"I need to render on the map"** → [map-system-architecture.md](map-system-architecture.md) + [visual-styles-architecture.md](visual-styles-architecture.md)

**"I need to optimize performance"** → [performance-architecture-guide.md](performance-architecture-guide.md) + Pattern 4, 10, 11, 12

**"I need to add a new system"** → [data-flow-architecture.md](data-flow-architecture.md) + Pattern 2, 3, 6

**"I need to build complex UI"** → [ui-architecture.md](ui-architecture.md) + Pattern 19

**"Something seems architecturally wrong"** → Check Critical Rules, consult FILE_REGISTRY.md, ask!

---

## Success Metrics

The architecture is successful if:
- ✅ **High FPS at scale** - Single draw call rendering, GPU compute shaders
- ✅ **Bounded memory usage** - 8-byte hot state, sparse collections
- ✅ **Fast province selection** - Texture lookup <1ms
- ✅ **Zero allocations during gameplay** - Pre-allocation pattern (Pattern 12)
- ✅ **Clear architecture** - ENGINE/GAME separation, 19 patterns
- ✅ **Reusable engine** - Can build different games on same engine
- ⏳ **Smooth multiplayer** - Foundation ready (FixedPoint64, Commands), not yet implemented
- ⏳ **Efficient network** - Command pattern + delta updates ready, not yet implemented

**Current Status:** All single-player targets met. Multiplayer foundation ready but not implemented.

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