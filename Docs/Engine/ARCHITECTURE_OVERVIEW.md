# Archon Engine Architecture - Quick Reference
**Start here for an overview of the entire system**

**🔄 Recent Update (2025-10-09):** ProvinceState refactored for engine-game separation. Game-specific fields (`development`, `fortLevel`, `flags`) moved to `HegemonProvinceData` in the game layer. Engine now contains only generic primitives. See [phase-3-complete-scenario-loader-bug-fixed.md](../Log/2025-10/2025-10-09/phase-3-complete-scenario-loader-bug-fixed.md) for complete refactoring details.

---

## TL;DR - Core Architecture in 30 Seconds

**Dual-Layer System:**
- **CPU Layer (Simulation):** 8-byte structs, deterministic, 80KB for 10k provinces
- **GPU Layer (Presentation):** Textures + shaders, 60MB VRAM, 200+ FPS

**Key Principle:** Simulation never knows about positions or rendering. Presentation never modifies game state.

**Current Status:** Core architecture ✅ | Multiplayer ❌ (future) | AI ❌ (future)

---

## System Status Overview

### Implemented & Partial Systems (Engine/)

| System | Status | Doc | Lines |
|--------|--------|-----|-------|
| **Core Architecture** | ✅ Implemented | [master-architecture-document](master-architecture-document.md) | ~800 |
| **Map System** | ⚠️ Partial | [map-system-architecture](map-system-architecture.md) | ~650 |
| **Data Access** | ✅ Implemented | [core-data-access-guide](core-data-access-guide.md) | ~400 |
| **Data Linking** | ✅ Implemented | [data-linking-architecture](data-linking-architecture.md) | ~350 |
| **Data Flow** | ⚠️ Partial | [data-flow-architecture](data-flow-architecture.md) | ~450 |
| **Data Loading** | ✅ Implemented | [data-loading-architecture](data-loading-architecture.md) | ~780 |
| **Time System** | ✅ Implemented | [time-system-architecture](time-system-architecture.md) | ~600 |
| **Performance** | ⚠️ Partial | [performance-architecture-guide](performance-architecture-guide.md) | ~450 |
| **Unity/Burst** | ⚠️ Partial | [../Log/learnings/unity-burst-jobs-architecture](../Log/learnings/unity-burst-jobs-architecture.md) | ~580 |

### Future Systems (Planning/)

| System | Status | Doc | Lines |
|--------|--------|-----|-------|
| **AI System** | ❌ Not Implemented | [../Planning/ai-design](../Planning/ai-design.md) | ~1000 |
| **Multiplayer** | ❌ Not Implemented | [../Planning/multiplayer-design](../Planning/multiplayer-design.md) | ~500 |
| **Modding** | ❌ Not Implemented | [../Planning/modding-design](../Planning/modding-design.md) | ~600 |
| **Save/Load** | ❌ Not Implemented | [../Planning/save-load-design](../Planning/save-load-design.md) | ~650 |
| **Error Recovery** | ❌ Not Implemented | [../Planning/error-recovery-design](../Planning/error-recovery-design.md) | ~400 |

**Legend:** ✅ Implemented | ⚠️ Partial/Unknown | ❌ Not Implemented

---

## The 8-Byte Province (Heart of the System)

```csharp
// ENGINE LAYER: Generic primitives only (8 bytes)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ProvinceState {  // EXACTLY 8 bytes
    public ushort ownerID;       // 2 bytes - who owns this
    public ushort controllerID;  // 2 bytes - who controls (war)
    public ushort terrainType;   // 2 bytes - terrain type (expanded to ushort)
    public ushort gameDataSlot;  // 2 bytes - index into game-specific data
}

// GAME LAYER: Hegemon-specific mechanics (4 bytes, separate from engine)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HegemonProvinceData {  // EXACTLY 4 bytes
    public byte development;     // 1 byte - EU4-style development mechanic
    public byte fortLevel;       // 1 byte - fortification system
    public byte unrest;          // 1 byte - stability mechanic
    public byte population;      // 1 byte - population abstraction
}
```

**Why dual-layer matters:**
- Engine 10,000 provinces = 80KB (fits in L2 cache)
- Game 10,000 provinces = 40KB (separate hot data)
- Total = 120KB for complete game state
- Enables 200+ FPS with massive province counts
- Network-friendly for multiplayer
- Engine is reusable across different games

**See:** [master-architecture-document.md](master-architecture-document.md) for complete explanation

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

## Key Systems Quick Reference

### Data Access Patterns
**Hot Data:** Accessed every frame → Tight 8-byte struct → NativeArray
**Cold Data:** Rarely accessed → Separate class → Dictionary

**See:** [core-data-access-guide.md](core-data-access-guide.md)

### Command Pattern
Every state change is a command:
```csharp
interface ICommand {
    void Execute(GameState state);
    void Serialize(BinaryWriter writer);
    bool Validate();
}
```

**Benefits:** Save/load for free, multiplayer sync, undo/replay

**See:** [data-flow-architecture.md](data-flow-architecture.md), [save-load-design.md](../Planning/save-load-design.md) *(Planning - not implemented)*

### Time System
Layered update frequencies:
- **Realtime:** Every frame (input, animations)
- **Daily:** Base game tick (economy, diplomacy)
- **Weekly:** Trade, markets
- **Monthly:** Construction, tech
- **Yearly:** Population, culture
- **On-Demand:** Only when triggered (trade goods, supply limits)

**Performance:** 50-100x fewer calculations than "update everything every tick"

**See:** [time-system-architecture.md](time-system-architecture.md)

### Map Rendering
**Texture-based, not GameObject-based:**
- Provinces are pixels in textures
- Single draw call for entire map
- GPU compute shaders for borders
- Texture lookup for selection

**Performance:** 200+ FPS with 10,000 provinces vs 20 FPS with GameObjects

**See:** [map-system-architecture.md](map-system-architecture.md)

---

## Performance Targets (Non-Negotiable)

### Memory
- **Simulation:** 80KB for 10k provinces
- **GPU Textures:** <60MB
- **Total:** <100MB

### Performance
- **Single-Player:** 200+ FPS @ 10k provinces
- **Multiplayer:** 144+ FPS (target)
- **Province Selection:** <1ms
- **Map Updates:** <5ms

### Network (Multiplayer - Future)
- **Bandwidth:** <5KB/s per client
- **State Sync:** 80KB for full state
- **Command Size:** 8-16 bytes typical

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
8. Array of Structures → Structure of Arrays for cache
9. Unbounded data growth → Ring buffers with compression
10. Update everything every frame → Dirty flags only

### ✅ ALWAYS DO THESE
1. 8-byte fixed structs for simulation
2. GPU compute shaders for visual processing
3. Single draw call for map
4. Deterministic operations (fixed-point math)
5. Hot/cold data separation
6. Command pattern for state changes
7. NativeArray for contiguous memory
8. Point filtering on province textures
9. Dirty flag systems
10. Profile at target scale (10k provinces)

---

## Current Implementation Status

### ✅ Implemented Systems
- ProvinceState 8-byte struct
- Hot/cold data separation
- Command pattern basics
- TimeManager with layered updates
- Data linking and validation (CrossReferenceBuilder, ReferenceResolver)
- 3925 provinces loaded from bitmap

### ⚠️ Partially Implemented
- Map rendering (Phase 1 done, Phase 2+ in progress)
- Border generation (compute shader exists)
- Performance patterns (some applied)
- Burst compilation (BurstProvinceHistoryLoader exists)

### ❌ Not Implemented (See Planning/ folder)
- AI system (see Planning/ai-design.md)
- Multiplayer (see Planning/multiplayer-design.md)
- Modding system (see Planning/modding-design.md)
- Save/load (unclear status)
- Performance monitoring (deleted - use Unity Profiler)

---

## Document Guide

### Start With These (Onboarding)
1. **This Document** - Overview (you are here!)
2. [master-architecture-document.md](master-architecture-document.md) - Core concepts
3. [core-data-access-guide.md](core-data-access-guide.md) - How to access data

### Implementation Guides
- [map-system-architecture.md](map-system-architecture.md) - Map rendering system
- [time-system-architecture.md](time-system-architecture.md) - Update frequencies
- [data-flow-architecture.md](data-flow-architecture.md) - System communication

### Advanced Topics
- [performance-architecture-guide.md](performance-architecture-guide.md) - Optimization patterns
- [unity-burst-jobs-architecture.md](unity-burst-jobs-architecture.md) - Burst compiler guide

### Reference
- [data-linking-architecture.md](data-linking-architecture.md) - Data validation system

### Future Planning (Not Implemented)
- [../Planning/ai-design.md](../Planning/ai-design.md) - AI system design
- [../Planning/multiplayer-design.md](../Planning/multiplayer-design.md) - Multiplayer architecture
- [../Planning/modding-design.md](../Planning/modding-design.md) - Modding system
- [../Planning/save-load-design.md](../Planning/save-load-design.md) - Command-based saves *(not implemented)*
- [../Planning/error-recovery-design.md](../Planning/error-recovery-design.md) - Error recovery *(not implemented)*

---

## Quick Decision Tree

**"Should I use floats in simulation?"**
→ ❌ NO. Use fixed-point math for determinism.

**"Should I store province positions in ProvinceState?"**
→ ❌ NO. Positions are presentation, not simulation. Use lookup tables.

**"Can I add fields to ProvinceState?"**
→ ⚠️ ONLY if you keep it exactly 8 bytes. Otherwise use cold data.

**"How do I render provinces?"**
→ GPU textures + shaders. See [map-system-architecture.md](map-system-architecture.md)

**"How do I make provinces update efficiently?"**
→ Dirty flags + layered updates. See [time-system-architecture.md](time-system-architecture.md)

**"How do I access province data?"**
→ See [core-data-access-guide.md](core-data-access-guide.md)

**"Where's the AI/multiplayer/modding?"**
→ Not implemented yet. See Planning/ folder for designs.

---

## Project Stats

**Documentation:**
- Engine docs: 9 (8 architecture + 1 overview)
- Planning docs: 5 (future features)
- Total lines: ~5,500 (down from ~12,000 after consolidation)

**Implementation:**
- Provinces loaded: 3,925 (from bitmap)
- Core systems: ~70% complete
- Advanced features: ~30% complete
- Multiplayer: 0% (future)

---

## Next Steps for New Developers

1. **Read this document** (you're doing it!)
2. **Read** [master-architecture-document.md](master-architecture-document.md)
3. **Read** [core-data-access-guide.md](core-data-access-guide.md)
4. **Browse codebase:** Assets/Scripts/Core/
5. **Check implementation status** in each doc's header
6. **Ask questions** when architecture seems violated

---

## When You Need Help

**"I need to understand the overall architecture"**
→ Read [master-architecture-document.md](master-architecture-document.md)

**"I need to render something on the map"**
→ Read [map-system-architecture.md](map-system-architecture.md)

**"I need to optimize performance"**
→ Read [performance-architecture-guide.md](performance-architecture-guide.md)

**"I need to add a new system"**
→ Read [data-flow-architecture.md](data-flow-architecture.md)

**"Something seems wrong with the architecture"**
→ Check the Critical Rules section above, then ask!

---

## Success Metrics

The architecture is successful if:
- ✅ 200+ FPS with 10,000 provinces
- ✅ <100MB total memory usage
- ✅ <1ms province selection
- ✅ Zero allocations during gameplay
- ⏳ 144+ FPS in multiplayer (when implemented)
- ⏳ <5KB/s network bandwidth (when implemented)

**Current Status:** Core targets met for implemented systems. Multiplayer TBD.

---

*Last Updated: 2025-09-30*
*For questions or updates, see DOCUMENTATION_AUDIT.md*