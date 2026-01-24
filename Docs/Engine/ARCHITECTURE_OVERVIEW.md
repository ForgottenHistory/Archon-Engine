# Archon Engine Architecture Overview

**Status:** Production Standard

---

## Core Principle

Archon is a dual-layer grand strategy engine: deterministic CPU simulation paired with high-performance GPU presentation. The engine provides generic mechanisms; games built on it define policy.

---

## The Problem

Grand strategy games face conflicting requirements:

- **Scale** - Thousands of provinces, hundreds of countries, millions of relationships
- **Determinism** - Multiplayer requires identical results across platforms
- **Performance** - Complex calculations every tick, smooth rendering every frame
- **Flexibility** - Different games need different rules, visuals, mechanics

Traditional approaches fail at scale. GameObjects per province explode draw calls. Float math breaks multiplayer sync. Tightly coupled systems prevent reuse.

---

## The Solution

### Three-Layer Architecture

**CORE (Simulation)**
- Fixed-size structs in contiguous memory
- Fixed-point math for determinism
- Command pattern for all state changes
- Zero allocations during gameplay

**MAP (Presentation)**
- Texture-based province rendering
- GPU compute shaders for visual processing
- Single draw call for entire map
- Point filtering on province ID textures

**GAME (Policy)**
- Game-specific formulas and rules
- Visual styles and colors
- UI and player interaction
- AI goal definitions

### Engine-Game Separation

The engine provides mechanisms (HOW things work). Games define policy (WHAT happens).

| Engine Provides | Game Defines |
|-----------------|--------------|
| Province ownership tracking | Colonization rules |
| Diplomacy relationship storage | Opinion modifiers |
| Resource system infrastructure | Gold, manpower formulas |
| Map rendering pipeline | Visual styles, colors |
| AI scheduling framework | Goal evaluation logic |

This separation enables building different games (space strategy, fantasy, historical) on the same engine foundation.

---

## Key Constraints

### The 8-Byte Province Rule

Province hot state is exactly 8 bytes. This enables:
- 10,000 provinces in 80KB of memory
- Cache-friendly iteration
- Predictable memory layout for serialization
- Fast network sync

Game-specific province data lives in the GAME layer, accessed via a slot index.

### Fixed-Point Determinism

All simulation math uses FixedPoint64. Float operations are non-deterministic across CPUs and platforms. A single float calculation breaks multiplayer sync.

### Command-Driven State Changes

All modifications to game state flow through commands. Commands validate before execution, serialize for networking, and support replay. Direct state mutation is forbidden.

### GPU for Pixels, CPU for Logic

Pixel-level operations (borders, highlighting, blending) happen on GPU via compute shaders. The CPU handles game logic only. Processing millions of pixels on CPU guarantees performance collapse.

---

## Anti-Patterns

| Don't | Do Instead |
|-------|------------|
| GameObject per province | Texture-based rendering |
| Float math in simulation | FixedPoint64 |
| Direct state mutation | Command pattern |
| Dynamic allocations in gameplay | Pre-allocated buffers |
| CPU pixel processing | GPU compute shaders |
| Unbounded data growth | Ring buffers, capacity limits |
| Update everything every frame | Dirty flags, layered ticks |
| Mixed hot/cold data | Separate by access frequency |

---

## Trade-offs

| Aspect | Benefit | Cost |
|--------|---------|------|
| Fixed-size structs | Cache efficiency, determinism | Less flexible data model |
| Command pattern | Networking, replay, validation | Overhead for simple changes |
| GPU rendering | Massive scale, single draw call | Shader complexity |
| Engine-game separation | Reusability across projects | More indirection |
| Pre-allocation | Zero runtime allocations | Higher baseline memory |
| Fixed-point math | Deterministic multiplayer | More verbose arithmetic |

---

## What's Implemented

The engine has working implementations of:

- Province and country systems with hot/cold separation
- Diplomacy with war, peace, treaties, opinion modifiers
- Unit management with movement and pathfinding
- Resource and modifier systems
- AI with tier-based scheduling and goal evaluation
- Time system with layered tick frequencies
- Save/load with hybrid snapshot architecture
- Texture-based map rendering with three border modes
- Map modes with pluggable colorizers
- Visual styles system
- Data loading with phase-based initialization
- Multiplayer with lockstep synchronization, lobby, time sync
- Mod loading infrastructure

What's designed but not implemented:
- Steam transport (using DirectTransport for LAN/IP)
- Host migration

See CURRENT_FEATURES.md for detailed feature inventory.

---

## Summary

1. **Dual-layer architecture** separates deterministic simulation from GPU presentation
2. **8-byte province state** enables scale through cache-friendly memory layout
3. **Fixed-point math** guarantees deterministic results for multiplayer
4. **Command pattern** provides validation, networking, and replay support
5. **Engine-game separation** allows different games on the same foundation
6. **GPU compute shaders** handle all pixel-level visual processing
7. **Pre-allocation policy** eliminates runtime allocations
8. **Dirty flag systems** minimize unnecessary updates

---

## Related Documents

- Engine-Game Separation - Philosophy of mechanism vs policy
- Data Flow Architecture - Command and event patterns
- Map System Architecture - Texture-based rendering
- Multiplayer Architecture - Lockstep synchronization patterns
- Performance Architecture Guide - Optimization patterns
- Architecture Patterns - Catalog of 22 patterns used throughout

---

*Architecture docs are maps, not turn-by-turn directions.*
