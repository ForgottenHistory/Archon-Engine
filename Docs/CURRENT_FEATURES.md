# Archon Engine - Current Features

**Last Updated:** 2025-10-18
**Version:** 1.2 (Architecture Refactor Complete)

This document lists all implemented features in the Archon Engine. Features are organized by category with brief descriptions.

**Recent:** Completed 3-week architecture refactor (modifier system, GameSystem lifecycle, command abstraction, resource system foundation)

---

## Core Architecture

- **Dual-Layer Architecture** - Separation of deterministic simulation (CPU) and high-performance presentation (GPU)
- **8-Byte Province Struct** - Fixed-size ProvinceState enables 10k provinces in 80KB with cache-friendly access
- **Fixed-Point Math (FixedPoint64)** - Deterministic 32.32 fixed-point arithmetic for multiplayer-ready simulation
- **Hot/Cold Data Separation** - Performance optimization separating frequently-accessed from rarely-accessed data
- **Command Pattern** - All state changes through commands for validation, networking, and replay support
- **Zero-Allocation EventBus** - Frame-coherent event system with 99.99% allocation reduction (EventQueue<T> pattern)
- **GameStateSnapshot (Double-Buffer)** - Zero-blocking UI reads via O(1) pointer swap (240KB at 10k provinces, Victoria 3 lesson)
- **NativeArray Storage** - Contiguous memory layout for optimal cache performance
- **Deterministic Simulation** - Identical results across platforms for multiplayer compatibility
- **Engine-Game Separation** - ProvinceState = ENGINE (8 bytes), game-specific data in GAME layer slot

---

## Modifier System (NEW - 2025-10-18)

- **ModifierValue** - Single modifier with base/additive/multiplicative bonuses
- **ModifierSet** - Fixed-size array of 512 modifier types (4KB, zero allocations)
- **ModifierSource** - Tracks modifier origin for tooltips and removal
- **ActiveModifierList** - Maintains active modifiers with expiration support
- **ScopedModifierContainer** - Province/Country/Global scope hierarchy
- **ModifierSystem** - Central manager with scope inheritance (Province ← Country ← Global)
- **Formula** - (base + additive) × (1 + multiplicative)
- **Performance** - <0.1ms lookup, <20MB for 10k provinces, zero allocations

---

## GameSystem Lifecycle (NEW - 2025-10-18)

- **GameSystem Base Class** - Abstract base for all game systems with standard lifecycle
- **SystemRegistry** - Manages registration and initialization order
- **Topological Sort** - Automatic dependency ordering via reflection
- **Property Injection** - Dependencies injected via properties (MonoBehaviour compatible)
- **Lifecycle Hooks** - Initialize/Shutdown/OnSaveGame/OnLoadGame
- **Circular Dependency Detection** - Fail-fast validation at startup

---

## Command System Enhancements (NEW - 2025-10-18)

- **CommandRegistry** - Reflection-based command auto-discovery
- **CommandMetadataAttribute** - Declarative command metadata (name, aliases, description, usage)
- **ICommandFactory** - Interface for argument parsing and command creation
- **Auto-Registration** - Commands auto-discover via reflection, zero manual registration
- **Self-Documenting** - Metadata generates help text automatically

---

## Resource System Foundation (NEW - 2025-10-18)

- **ResourceSystem** - Generic multi-resource storage (Dictionary<ushort, FixedPoint64[]>)
- **ResourceDefinition** - Data structure for resource properties
- **Resource Query API** - GetResource/AddResource/RemoveResource for any resource type
- **Event System** - OnResourceChanged events for UI updates
- **Unlimited Types** - Support for any number of resource types (gold, manpower, prestige, etc.)

---

## Province System

- **Province Management** - Efficient management of 10,000+ provinces
- **Province Ownership Tracking** - Fast owner lookup with bidirectional mapping (province→owner, owner→provinces)
- **Province Development** - Economic development levels per province
- **Province Terrain System** - Terrain type tracking and modifiers
- **Province History Database** - Bounded history storage with ring buffers (prevents unbounded growth)
- **Province State Serialization** - Network-safe serialization for multiplayer
- **Province Cold Data** - Separate storage for names, colors, bounds, and metadata
- **Province Queries** - Read-only query API with frame-coherent caching
- **Province ID System** - Strongly-typed IDs preventing type confusion

---

## Country System

- **Country Management** - Country data with hot/cold separation pattern
- **Country Color System** - Color palette management with HSV golden angle distribution
- **Country Tag System** - String tag to numeric ID mapping (e.g., "ENG" → CountryId)
- **Country Hot Data** - 8-byte country state for cache efficiency
- **Country Cold Data** - Extended country information (name, government, etc.)
- **Country Queries** - High-performance queries with caching for expensive calculations
- **Country Events** - Event system for country state changes

---

## Map Rendering System

- **Texture-Based Rendering** - Single draw call for entire map via texture-based approach
- **GPU Compute Shaders** - All visual processing on GPU for maximum performance
- **Dual Border System** - Country and province borders in single compute pass (RG16 texture)
- **Province Selection** - Sub-millisecond province selection via texture lookup (no raycasting)
- **Map Texture Management** - Coordinated texture system (~60MB VRAM for 5632×2048 map)
- **Border Thickness Control** - Configurable border width and anti-aliasing
- **Heightmap Support** - 8-bit grayscale heightmap rendering
- **Normal Map Support** - RGB24 normal map for terrain lighting
- **Point Filtering** - Pixel-perfect province ID lookup without interpolation
- **Single Draw Call Optimization** - Entire map rendered in one draw call

---

## Map Modes

- **Political Map Mode** - Country ownership visualization with color palette
- **Terrain Map Mode** - Terrain type visualization from terrain.bmp
- **Debug Map Modes** - Heightmap and normal map debug visualization
- **Map Mode Interface** - Extensible IMapModeHandler for custom modes
- **Map Mode Manager** - Runtime switching between visualization modes
- **GradientMapMode Base Class (NEW - 2025-10-18)** - Reusable gradient engine for numeric province data
- **ColorGradient (NEW - 2025-10-18)** - Configurable color interpolation (red-to-yellow, etc.)
- **Dirty Flag Optimization (NEW - 2025-10-18)** - Skip texture updates when data unchanged

---

## Visual Styles System

- **Visual Style Configuration** - ScriptableObject-based style definitions
- **Material Ownership** - Complete material+shader ownership in game layer
- **Runtime Style Switching** - Hot-swap visual styles without restart
- **EU3 Classic Style** - Implemented reference style with clean borders
- **Border Configuration** - Per-style border colors, thickness, and anti-aliasing
- **Development Gradients** - Configurable 5-tier color progressions
- **Engine-Game Separation** - Engine provides infrastructure, game defines visuals

---

## Time System

- **Tick-Based Progression** - Frame-independent game time with fixed timesteps
- **360-Day Calendar** - Consistent calendar system (12 months × 30 days)
- **Layered Update Frequencies** - Hourly, daily, weekly, monthly, yearly tick events
- **Game Speed Controls** - Pause, slow, normal, fast, very fast speeds
- **Time Events** - EventBus integration for time-based system updates
- **Dirty Flag Integration** - Only update systems when state changes

---

## Data Loading System

- **JSON5 Support** - JSON5 file parsing for game data
- **Burst Province Loading** - Parallel province history loading with Burst compilation
- **Burst Country Loading** - Optimized country data loading
- **Bitmap Map Loading** - Load provinces.bmp, terrain.bmp, heightmap.bmp, normal maps
- **Definition.csv Support** - Complete province definitions (handles 4941 provinces)
- **Reference Resolution** - String→ID resolution (e.g., "ENG" → CountryId)
- **Cross-Reference Builder** - Automated data linking and validation
- **Data Validation** - Integrity checks after loading
- **Phase-Based Initialization** - 7-phase startup with progress tracking

---

## Performance Optimizations

- **Load Balancing (LoadBalancedScheduler)** - Victoria 3 pattern: cost-based job scheduling prevents thread starvation (24.9% improvement at 10k provinces)
- **Zero-Blocking UI Reads** - Double-buffer pattern eliminates lock contention (Victoria 3's profiler "waiting bars" lesson)
- **Pre-Allocation Policy (Principle 4)** - Zero runtime allocations prevents malloc lock contention (HOI4's parallelism lesson)
- **Zero Allocations** - No runtime allocations during gameplay loop
- **Frame-Coherent Caching** - Per-frame cache invalidation for expensive queries
- **Ring Buffers** - Bounded history storage preventing memory growth
- **Dirty Flag Systems** - Update-only-what-changed architecture
- **Memory Stability** - Stable memory over 400+ simulated years
- **GPU Border Generation** - 2ms for 10k provinces via compute shader
- **Structure of Arrays** - Cache-friendly memory layout for country colors
- **Array of Structures** - Optimal for province queries accessing multiple fields

---

## Performance Targets

- **Single-Player FPS** - 200+ FPS with 10,000 provinces
- **Province Selection** - <1ms response time
- **Map Updates** - <5ms for full update
- **Memory Footprint** - <100MB total (80KB simulation + <60MB GPU)
- **EventBus Performance** - 0.85ms for 10k events, zero allocations
- **Fixed-Point Math** - 0.13ms for 10k calculations

---

## Engine-Game Separation

- **Mechanism vs Policy** - Engine provides how, game defines what
- **Extension Interfaces** - IGameSystem, IMapModeHandler, ICommand
- **Clean Dependencies** - No circular dependencies between layers
- **Namespace Separation** - ArchonEngine.Core, ArchonEngine.Map, Game
- **Package Exportable** - Engine as reusable Unity package
- **Zero Game Logic** - No game-specific code in engine layer

---

## Development Infrastructure

- **File Registries** - Complete catalogs for Core (69 files) and Map (51 files) layers
- **Phase-Based Initialization** - 7 initialization phases with rollback support
- **Session Logging** - Development journal with TEMPLATE.md for tracking work
- **Architecture Documentation** - 10 engine docs + 5 planning docs
- **Stress Test Framework** - Automated tests for 10k provinces, 400 years
- **Memory Profiling** - Built-in tracking for allocation detection

---

## Utility Systems

- **ArchonLogger** - Categorized logging system
- **DeterministicRandom** - Seeded xorshift128+ RNG for deterministic gameplay
- **Strongly-Typed IDs** - Type-safe wrappers (ProvinceId, CountryId, etc.)
- **Registry System** - Fast lookup for static game data
- **CircularBuffer** - Bounded buffers for history and event storage

---

## Interaction Systems

- **Paradox-Style Camera** - Pan, zoom, edge scrolling controls
- **Province Selector** - Texture-based selection with <1ms performance
- **Map Initialization** - Automated setup of map subsystems
- **Border Compute Dispatcher** - GPU border generation with multiple modes

---

## Data Structures

- **ProvinceState (8 bytes)** - ownerID(2), controllerID(2), terrainType(2), gameDataSlot(2) - ENGINE struct only
- **ProvinceColdData** - Name, color, bounds, history, modifiers
- **CountryHotData** - Compact country state for cache efficiency
- **CountryColdData** - Extended country metadata
- **FixedPoint64** - 32.32 fixed-point for deterministic math
- **FixedPoint32** - 16.16 fixed-point for compact storage
- **FixedPoint2** - 2D vector with fixed-point components

---

## Sparse Data Infrastructure (NEW - 2025-10-15)

**Purpose:** Prevents HOI4's 30→500 equipment disaster (16x slowdown with mods)

**Pattern:** Three-layer architecture (Definitions → Storage → Access)

**Key Components:**
- **IDefinition** - Base interface for all definitions (buildings, modifiers, trade goods)
  - ID: Runtime-assigned ushort (0-65535)
  - StringID: Stable string for save/load compatibility ("farm", "gold_mine")
  - Version: Definition compatibility checks
- **ISparseCollection** - Non-generic interface for polymorphic management
  - Unified memory monitoring and disposal across all sparse collections
  - SparseCollectionStats struct for profiling
- **SparseCollectionManager<TKey, TValue>** - Generic sparse storage (381 lines)
  - NativeParallelMultiHashMap for one-to-many relationships (Collections 2.1+)
  - Pre-allocation with Allocator.Persistent (Principle 4)
  - Capacity warnings at 80%/95% thresholds
  - Query APIs: Has/HasAny/Get/GetCount (O(1) to O(m) where m = items per key)
  - Modification APIs: Add/Remove/RemoveAll
  - Iteration APIs: ProcessValues (zero allocation), GetKeys

**Memory Scaling:**
- Dense approach: 10k entities × 500 types = 5 MB (must iterate all)
- Sparse approach: 10k entities × 5 actual = 200 KB (iterate only actual)
- Savings: 96% memory reduction at mod scale

**Status:** Phase 1 & 2 complete (foundation ready for buildings/modifiers systems)

---

## Testing & Validation

- **Province State Tests** - Validation of 8-byte struct operations
- **Texture Infrastructure Tests** - GPU texture creation and binding
- **Command System Tests** - Command execution validation
- **Integration Tests** - Full pipeline texture→simulation→GPU
- **Stress Tests** - 10k provinces, 400 years, allocation monitoring
- **Determinism Tests** - Cross-platform checksum validation

---

## Shader Infrastructure

- **BorderDetection.compute** - Dual border generation (country + province)
- **MapFallback.shader** - Pink fallback for missing visual styles
- **EU3MapShader.shader** - Complete map visualization shader
- **MapModeCommon.hlsl** - Shared utilities (ID decoding, sampling)
- **MapModePolitical.hlsl** - Political map mode visualization
- **MapModeTerrain.hlsl** - Terrain map mode visualization
- **MapModeDevelopment.hlsl** - Development gradient visualization

---

## Unity Integration

- **URP Support** - Universal Render Pipeline compatibility
- **Burst Compilation** - Optimized code generation for hot paths
- **Job System** - Parallel processing for data loading
- **IL2CPP Backend** - Ahead-of-time compilation support
- **Linear Color Space** - Modern color workflow
- **Compute Shader Coordination** - CPU-GPU synchronization patterns

---

## Multiplayer-Ready Features

- **Deterministic Math** - FixedPoint64 for identical results across platforms
- **Command Checksums** - Validation for state consistency
- **State Serialization** - 80KB for complete 10k province state
- **Command Pattern** - Network-friendly state changes
- **Rollback Support** - Command buffer for client prediction (designed, not implemented)

---

## Features NOT Implemented (See Planning/ Docs)

- AI System
- Multiplayer Networking
- Modding System
- Save/Load System
- Error Recovery System
- Localization System

---

## Quick Stats

- **Engine Code:** ~29,000 lines (Core + Map layers)
- **Documentation:** 12 engine docs + session logs
- **Systems:** 75+ Core scripts, 51 Map scripts
- **Max File Size:** All files under 500 lines
- **Provinces Tested:** 10,000 provinces with load balancing
- **Performance:** 200+ FPS achieved
- **Architecture:** Modifier system, GameSystem lifecycle, command auto-registration, resource foundation complete

---

## Architecture Compliance

**Enforced Rules:**
- ✅ 8-byte ProvinceState (never larger)
- ✅ Fixed-point math only (no floats in simulation)
- ✅ GPU compute shaders for visual processing
- ✅ Single draw call for map rendering
- ✅ Zero allocations during gameplay
- ✅ NativeArray for contiguous memory
- ✅ Point filtering on province textures
- ✅ All files under 500 lines

**Prevented Anti-Patterns:**
- ❌ GameObjects per province
- ❌ CPU pixel processing
- ❌ Float operations in simulation
- ❌ Unbounded data growth
- ❌ Mixed hot/cold data
- ❌ Multiple draw calls
- ❌ Texture filtering on IDs

---

## Documentation Status

**Architecture Docs (Engine/):**
- ✅ ARCHITECTURE_OVERVIEW.md - Quick reference
- ✅ master-architecture-document.md - Complete architecture
- ✅ map-system-architecture.md - Map rendering
- ✅ visual-styles-architecture.md - Visual system
- ✅ core-data-access-guide.md - Data access patterns
- ✅ data-linking-architecture.md - Reference resolution
- ✅ data-flow-architecture.md - System communication
- ✅ data-loading-architecture.md - JSON5 + Burst loading
- ✅ time-system-architecture.md - Tick system
- ✅ performance-architecture-guide.md - Optimization patterns (updated: Principle 4 - Pre-allocation)
- ✅ engine-game-separation.md - Layer separation
- ✅ sparse-data-structures-design.md - Sparse collections architecture (503 lines, prevents HOI4's 16x disaster)

**Planning Docs (Planning/):**
- ❌ ai-design.md - Not implemented
- ❌ multiplayer-design.md - Not implemented
- ❌ modding-design.md - Not implemented
- ❌ save-load-design.md - Not implemented
- ❌ error-recovery-design.md - Not implemented

**Session Logs (Log/2025-10/):**
- ✅ 15/ - Paradox infrastructure (load balancing, double-buffer, sparse data)
- ✅ 18/ - Architecture refactor (modifier system, GameSystem lifecycle, command abstraction, resource system, performance optimization)

**File Registries:**
- ✅ Scripts/Core/FILE_REGISTRY.md - Updated with sparse data files
- ✅ Scripts/Map/FILE_REGISTRY.md - 51 files cataloged

---

*For detailed implementation information, see individual architecture documents in Docs/Engine/*
