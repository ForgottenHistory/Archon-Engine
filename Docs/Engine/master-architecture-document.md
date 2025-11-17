# Grand Strategy Game Master Architecture Document

**📊 Implementation Status:** ✅ Core Implemented (ProvinceState, command system, dual-layer) | ❌ Multiplayer sections are future planning

---

## Executive Summary

This document outlines the complete architecture for a scalable grand strategy game capable of:
- **Large-scale provinces** with high performance
- **Minimal memory footprint** for map system
- **Efficient network synchronization** for multiplayer
- **Deterministic** simulation enabling replays, saves, and multiplayer
- **Sustainable** late-game performance

The core innovation: **Dual-layer architecture** separating deterministic simulation (CPU) from high-performance presentation (GPU), with province data stored as textures in VRAM.

---

## Layer Separation

### Core Layer (Simulation)
**Purpose:** Deterministic simulation - game state, logic, commands

**Principles:**
- Use fixed-point math for determinism (NO float/double)
- Deterministic operations only (multiplayer-safe)
- No Unity API dependencies in hot paths
- All state changes through command pattern

### Map Layer (Presentation)
**Purpose:** GPU-accelerated presentation - textures, rendering, interaction

**Principles:**
- GPU compute shaders for visual processing (NO CPU pixel ops)
- Single draw call for base map
- Presentation only (does NOT affect simulation)
- Read-only from Core layer, updates textures

### Interaction Rules
- Map layer CANNOT modify Core state
- All state changes go through command pattern
- One-way event flow: Core → Map
- Strict separation prevents desync

---

## Part 1: Core Architecture

### 1.1 Fundamental Design Principle

**Traditional Approach (Fails at Scale):**
GameObject per Province → Mesh Renderer → Multiple Draw Calls → Poor Performance

**Our Approach (Scales Effectively):**
Texture Data → Single Quad → GPU Shader → High Performance

### 1.2 Dual-Layer Architecture

**Simulation Layer (CPU):**
- Fixed-size province state structs
- Deterministic operations
- Generic primitives for engine
- Game-specific data slots

**Presentation Layer (GPU):**
- Province ID textures
- Owner/controller textures
- Visual color palettes
- Generated borders

### 1.3 Data Flow Architecture

Input → Command → Simulation → GPU Textures → Screen
- Commands enable networking and replays
- State changes are deterministic
- GPU updates are presentation-only

### 1.4 Memory Architecture

**CPU Memory (RAM):**
- Compact simulation state (fixed-size structs)
- Command buffer (ring buffer for rollback)
- Game logic data

**GPU Memory (VRAM):**
- Province ID map (configurable resolution)
- Owner/controller textures
- Color palettes
- Border cache

**Network:**
- Delta updates (minimal bandwidth)
- Full sync fallback (compact state)

---

## Part 2: GPU-Based Rendering System

### 2.1 Why Textures Instead of Meshes

**Mesh Approach Issues:**
- Multiple GameObjects = multiple transform updates
- Many draw calls (even with batching)
- Vertex processing overhead
- Poor performance at scale

**Texture Approach Benefits:**
- Single GameObject (quad)
- Single draw call
- Minimal vertices
- Excellent performance at scale

### 2.2 Province Data as Textures

Provinces are stored as GPU textures for parallel processing. Fragment shaders read province IDs, look up ownership data, and render colors in a single pass.

See [Map System Architecture](map-system-architecture.md) for implementation details.

### 2.3 Border Generation via Compute Shader

Border generation uses GPU compute shaders to detect province boundaries in parallel.

**CRITICAL:** When multiple compute shaders access the same RenderTexture sequentially, **explicit GPU synchronization is required** to avoid race conditions. See [Unity Compute Shader Coordination](../Log/learnings/unity-compute-shader-coordination.md) for patterns and pitfalls.

### 2.4 Province Selection Without Raycasting

Texture-based province selection eliminates physics raycasting overhead:
- Convert mouse position to texture UV coordinates
- Read province ID from texture
- Decode ID from color channels
- Much faster than physics-based selection

---

## Part 3: Multiplayer-Ready Architecture

### 3.1 Command Pattern for Determinism

All state changes flow through commands for:
- Serialization and network transmission
- Validation before execution
- Replay and debugging support
- Deterministic execution order

Commands are compact, serializable, and carry minimal data.

### 3.2 Fixed-Point Deterministic Math

**All simulation math uses fixed-point arithmetic for cross-platform determinism.**

See **[Fixed-Point Determinism Decision Record](../Log/decisions/fixed-point-determinism.md)** for complete rationale and implementation guide.

**Key Principles:**
- Never use float/double in simulation
- Use fixed-point types for fractional calculations
- Ensure identical results across platforms
- Enable perfect replay and multiplayer sync

### 3.3 Network Synchronization

**Delta Compression:**
- Only transmit changed data
- Minimal bandwidth usage
- Efficient for large game states

**Synchronization Strategy:**
- Delta updates for typical changes
- Full sync as fallback
- Checksum validation

### 3.4 Client Prediction & Rollback

**Prediction:**
- Execute commands immediately for responsive feel
- Maintain command history for rollback
- Update visuals optimistically

**Rollback:**
- Detect server/client divergence
- Revert to authoritative state
- Replay unconfirmed commands
- Update visuals once after reconciliation

---

## Part 4: Performance Optimization Strategies

### 4.1 Hot/Cold Data Separation

**Hot Data (Frequent Access):**
- Fixed-size structs in contiguous arrays
- Cache-line aligned
- Frequently accessed fields
- Minimal memory footprint

**Cold Data (Rare Access):**
- Complex objects with detailed information
- Loaded on-demand
- Can page to disk
- Not performance-critical

### 4.2 Frame-Coherent Caching

Cache expensive calculations per frame:
- Clear cache when frame changes
- Compute once, reuse within frame
- Avoid redundant calculations
- Particularly useful for UI and tooltips

### 4.3 Dirty Flag System

Only update what changed:
- Track modified data
- Batch GPU updates
- Single upload per frame
- Minimize redundant work

### 4.4 Spatial Partitioning for Culling

Optimize by reducing scope:
- Partition world into grid
- Query only visible regions
- Skip processing for off-screen data
- Scale with visible area, not total data

---

## Part 5: Avoiding Late-Game Performance Collapse

### 5.1 Fixed-Size Data Structures

Prevent unbounded growth:
- Use ring buffers for history
- Fixed-size arrays where possible
- Compress or discard old data
- Avoid dynamic collections in hot paths

### 5.2 Progressive History Compression

Multi-tier history storage:
- Recent: Full detail
- Medium: Compressed representation
- Ancient: Statistical summaries only
- Automatic aging and compression

### 5.3 LOD System for Province Updates

Update frequency based on importance:
- Critical: Player provinces, active combat
- High: Visible provinces
- Medium: Known but not visible
- Low: Unexplored or distant regions

---

## Part 6: Implementation Overview

See [Map System Architecture](map-system-architecture.md) for detailed implementation phases and step-by-step instructions.

---

## Part 7: Performance Targets & Validation

### 7.1 Performance Budget

Allocate frame time carefully:
- Map rendering
- Province selection
- Simulation update
- Command processing
- UI updates
- Network synchronization (if multiplayer)
- Reserve for spikes

### 7.2 Validation Tests

Critical validation requirements:
- Large-scale province performance over extended gameplay
- Fast province selection response
- Bounded memory usage
- Deterministic simulation across platforms

See [Performance Architecture](performance-architecture-guide.md) for testing strategies.

---

## Part 8: Critical Success Factors

### 8.1 Must-Have Requirements
- ✅ Single draw call for base map
- ✅ Province data in GPU textures
- ✅ Deterministic simulation
- ✅ Fixed-size data structures
- ✅ Hot/cold data separation
- ✅ Command pattern for all changes

### 8.2 Must-Avoid Anti-Patterns
- ❌ GameObject per province
- ❌ Mesh-based borders
- ❌ Floating-point in simulation
- ❌ Unbounded data growth
- ❌ **Unsynchronized GPU compute shader dispatches** (causes race conditions - see [Compute Shader Coordination](../Log/learnings/unity-compute-shader-coordination.md))
- ❌ **Mixed Texture2D/RWTexture2D bindings** for same RenderTexture (causes UAV/SRV state transition failures)
- ❌ **Y-flipping in compute shaders** (breaks coordinate systems - Y-flip only in fragment shader UVs)
- ❌ Update-everything-every-frame

### 8.3 Key Innovations
1. **Texture-based province storage** - GPU processes all provinces in parallel
2. **Dual-layer architecture** - Simulation separate from presentation
3. **Compute shader borders** - Generate borders in 2ms on GPU
4. **Fixed-point determinism** - Perfect synchronization across clients
5. **Ring buffer history** - Bounded memory growth over time

---

## Appendix A: Technical References

**Texture Formats:**
- Province IDs: High-precision formats for large ID ranges
- Ownership: Moderate precision for country IDs
- Development/Stats: Low precision sufficient
- Colors: Full RGBA for visual fidelity
- Borders: Minimal precision for binary data

**Network Packet Structure:**
- Command packets: Type + ID + payload
- Delta updates: Change count + deltas
- Full sync: Tick + checksum + full state

**Shader Patterns:**
- Province ID decoding from texture channels
- Owner color lookup via indirection
- Border detection via neighbor comparison

---

## Conclusion

This architecture delivers large-scale grand strategy gameplay through:
1. **GPU-driven rendering** using textures instead of meshes
2. **Deterministic simulation** enabling multiplayer and replays
3. **Aggressive optimization** preventing late-game slowdown
4. **Clean separation** of simulation and presentation

The system scales to large province counts while maintaining excellent performance, minimal memory usage, and efficient network bandwidth.

Follow the implementation roadmap, avoid the anti-patterns, and you'll have a performant grand strategy engine.

---

## Related Documents

### Essential Architecture
- **[ARCHITECTURE_OVERVIEW.md](ARCHITECTURE_OVERVIEW.md)** - Start here for quick overview
- **[architecture-patterns.md](architecture-patterns.md)** - 19 architectural patterns catalog
- **[engine-game-separation.md](engine-game-separation.md)** - Mechanism vs Policy philosophy

### Architecture Documents
- **[Map System Architecture](map-system-architecture.md)** - Map rendering system details
- **[Visual Styles Architecture](visual-styles-architecture.md)** - Visual style system
- **[Vector Curve Rendering](vector-curve-rendering-pattern.md)** - Border rendering
- **[Performance Architecture Guide](performance-architecture-guide.md)** - Optimization and memory strategies
- **[Time System Architecture](time-system-architecture.md)** - Update scheduling and dirty flag systems
- **[Data Flow Architecture](data-flow-architecture.md)** - Command + Event patterns
- **[Data Linking Architecture](data-linking-architecture.md)** - Reference resolution and cross-linking
- **[Data Loading Architecture](data-loading-architecture.md)** - Phase-based initialization
- **[Save/Load Architecture](save-load-architecture.md)** - Hybrid snapshot + command log
- **[UI Architecture](ui-architecture.md)** - UI Toolkit + Presenter Pattern
- **[Sparse Data Structures](sparse-data-structures-design.md)** - Scale-safe storage
- **[Flat Storage Burst](flat-storage-burst-architecture.md)** - Burst-compiled systems
- **[Modifier System](modifier-system.md)** - Generic modifier system

### Critical Knowledge
- **[Unity Compute Shader Coordination](../Log/learnings/unity-compute-shader-coordination.md)** - GPU synchronization patterns (MUST READ before writing compute shaders)

### Architecture Decisions
- **[Fixed-Point Determinism](../Log/decisions/fixed-point-determinism.md)** - Deterministic math requirements (MUST READ before implementing simulation logic)
- **[UI Presenter Pattern](../Log/decisions/ui-presenter-pattern-for-panels.md)** - UI pattern rationale