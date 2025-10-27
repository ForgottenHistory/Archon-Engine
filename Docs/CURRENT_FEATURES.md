# Archon Engine - Current Features

**Last Updated:** 2025-10-26
**Version:** 1.7 (Vector Curve Border Rendering Complete)

This document lists all implemented features in the Archon Engine organized by category.

---

## Core Architecture

**Dual-Layer Architecture** - Separation of deterministic simulation (CPU) and high-performance presentation (GPU)
**8-Byte Province Struct** - Fixed-size ProvinceState enables 10k provinces in 80KB with cache-friendly access
**Fixed-Point Math (FixedPoint64)** - Deterministic 32.32 fixed-point arithmetic for multiplayer-ready simulation
**Hot/Cold Data Separation** - Performance optimization separating frequently-accessed from rarely-accessed data
**Command Pattern** - All state changes through commands for validation, networking, and replay support
**Zero-Allocation EventBus** - Frame-coherent event system with 99.99% allocation reduction (EventQueue<T> pattern)
**GameStateSnapshot (Double-Buffer)** - Zero-blocking UI reads via O(1) pointer swap (240KB at 10k provinces)
**NativeArray Storage** - Contiguous memory layout for optimal cache performance
**Deterministic Simulation** - Identical results across platforms for multiplayer compatibility
**Engine-Game Separation** - ProvinceState = ENGINE (8 bytes), game-specific data in GAME layer slot

---

## System Infrastructure

**GameSystem Base Class** - Abstract base for all game systems with standard lifecycle hooks
**SystemRegistry** - Manages system registration and initialization with dependency ordering
**Topological Sort** - Automatic dependency ordering via reflection
**Property Injection** - Dependencies injected via properties (MonoBehaviour compatible)
**Lifecycle Hooks** - Initialize/Shutdown/OnSaveGame/OnLoadGame
**Circular Dependency Detection** - Fail-fast validation at startup

---

## Province System

**Province Management** - Efficient management of 10,000+ provinces
**Province Ownership Tracking** - Fast owner lookup with bidirectional mapping (province→owner, owner→provinces)
**Province Development** - Economic development levels per province
**Province Terrain System** - Terrain type tracking and modifiers
**Province History Database** - Bounded history storage with ring buffers
**Province State Serialization** - Network-safe serialization for multiplayer
**Province Cold Data** - Separate storage for names, colors, bounds, and metadata
**Province Queries** - Read-only query API with frame-coherent caching
**Province ID System** - Strongly-typed IDs preventing type confusion

---

## Country System

**Country Management** - Country data with hot/cold separation pattern
**Country Color System** - Color palette management with HSV golden angle distribution
**Country Tag System** - String tag to numeric ID mapping (e.g., "ENG" → CountryId)
**Country Hot Data** - 8-byte country state for cache efficiency
**Country Cold Data** - Extended country information (name, government, etc.)
**Country Queries** - High-performance queries with caching for expensive calculations
**Country Events** - Event system for country state changes

---

## Diplomacy System

**RelationData (8 bytes)** - Hot data for diplomatic relationships (opinion, treaties, lastContact, flags)
**DiplomacyColdData** - Flat modifier storage with relationship keys (Burst-compatible)
**OpinionModifier** - Time-decaying modifiers with linear decay formula
**DiplomacySystem** - Sparse storage for active relationships (NativeParallelHashMap O(1) lookups)
**War/Peace Commands** - DeclareWarCommand, MakePeaceCommand with validation
**Treaty Commands** - FormAlliance, BreakAlliance, GrantMilitaryAccess, GuaranteeIndependence, etc.
**Opinion System** - Stackable modifiers clamped to [-200, +200] range
**Monthly Decay** - Burst-compiled parallel decay processing (IJobParallelFor)
**Treaty Validation** - Mutual existence checks, war state validation
**Performance** - 3ms for 610,750 modifiers (350 countries, 100% density) with Burst compilation, <0.0001ms GetOpinion queries
**Memory** - 954 KB hot data + 19 MB flat modifier storage for 61,075 relationships (all possible pairs), scales linearly with modifier count

---

## Units System

**UnitSystem** - Military unit management with movement, combat, organization
**UnitState (16 bytes)** - Fixed-size hot state: unitID, ownerID, provinceID, typeID, organization, morale, strength, movementQueue
**UnitColdData** - Cold unit data: Name, Experience, CombatHistory, Equipment
**Unit Commands** - CreateUnit, MoveUnit, DisbandUnit with validation
**Unit Movement Queue** - Fixed-size circular buffer for pathfinding waypoints
**Unit Events** - UnitCreated, UnitMoved, UnitDisbanded, CombatResolved
**Sparse Collections** - Province-to-units mapping with NativeParallelMultiHashMap
**Hot/Cold Separation** - Frequently-accessed data separate from detailed information

---

## Pathfinding System

**AdjacencySystem** - Province neighbor management with sparse collections
**PathfindingSystem** - A* pathfinding for unit movement
**GetNeighbors** - O(1) neighbor lookup with sparse storage
**FindPath** - A* algorithm with province graph traversal
**GetReachableProvinces** - Calculate movement range for units
**Distance Caching** - Cached distance matrix for performance

---

## AI System

**AISystem** - Central AI manager with bucketing scheduler for 979 countries
**AIState (8 bytes)** - Fixed-size hot state: countryID, bucket, flags, activeGoalID
**AIGoal** - Abstract base class for goal-oriented decision making (Evaluate/Execute pattern)
**AIScheduler** - Goal evaluation and execution scheduler (picks best goal, executes it)
**AIGoalRegistry** - Plug-and-play goal registration system (extensible without refactoring)
**Bucketing Strategy** - 979 countries / 30 days = ~33 AI per day (spread load across month)
**Two-Phase Initialization** - Phase 1: System startup (register goals), Phase 2: Player selection (activate AI states)
**Zero Allocations** - Pre-allocated NativeArray with Allocator.Persistent
**Deterministic Scoring** - FixedPoint64 goal scores with ordered evaluation
**Command Pattern Integration** - AI uses player commands (DeclareWarCommand, BuildBuildingCommand)
**Performance** - <5ms per frame target with bucketing (10 AI × 0.5ms each)

---

## Modifier System

**ModifierValue** - Single modifier with base/additive/multiplicative bonuses
**ModifierSet** - Fixed-size array of 512 modifier types (4KB, zero allocations)
**ModifierSource** - Tracks modifier origin for tooltips and removal
**ActiveModifierList** - Maintains active modifiers with expiration support
**ScopedModifierContainer** - Province/Country/Global scope hierarchy
**ModifierSystem** - Central manager with scope inheritance (Province ← Country ← Global)
**Formula** - (base + additive) × (1 + multiplicative)
**Performance** - <0.1ms lookup, <20MB for 10k provinces, zero allocations

---

## Resource System

**ResourceSystem** - Multi-resource treasury management with FixedPoint64 values
**ResourceDefinition** - Data structure for resource properties
**Resource Query API** - GetResource/AddResource/RemoveResource for any resource type
**Event System** - OnResourceChanged events for UI updates
**Unlimited Types** - Support for any number of resource types (gold, manpower, prestige, etc.)

---

## Command System

**ICommand Interface** - Base command interface: Execute, Validate, GetChecksum, Dispose
**CommandRegistry** - Reflection-based command auto-discovery
**CommandMetadataAttribute** - Declarative command metadata (name, aliases, description, usage)
**ICommandFactory** - Interface for argument parsing and command creation
**CommandProcessor** - Deterministic command validation and execution
**Auto-Registration** - Commands auto-discover via reflection, zero manual registration
**Self-Documenting** - Metadata generates help text automatically

---

## Save/Load System

**SaveManager** - Orchestrates save/load across all systems with layer separation via callbacks
**SaveGameData** - Binary save file structure (header + metadata + system data dictionary)
**SerializationHelper** - Binary serialization utilities (FixedPoint64, NativeArray, strings)
**Hybrid Architecture** - Snapshot for speed + command log for verification
**Atomic Writes** - Temp file → rename pattern prevents corruption on crash
**Hot Data Serialization** - All core systems implement SaveState/LoadState
**Double Buffer Sync** - GameStateSnapshot.SyncBuffersAfterLoad() prevents stale UI reads
**Post-Load Finalization** - SaveLoadGameCoordinator rebuilds derived data
**GameLoadedEvent** - Event broadcast after load for UI refresh
**Quicksave/Quickload** - F6/F7 hotkeys for rapid save/load iteration
**Systems Supported** - TimeManager, ResourceSystem, ProvinceSystem, ModifierSystem, CountrySystem, DiplomacySystem, UnitSystem

---

## Time System

**Tick-Based Progression** - Frame-independent game time with fixed timesteps
**360-Day Calendar** - Consistent calendar system (12 months × 30 days)
**Layered Update Frequencies** - Hourly, daily, weekly, monthly, yearly tick events
**Game Speed Controls** - Pause, slow, normal, fast, very fast speeds
**Time Events** - EventBus integration for time-based system updates
**Dirty Flag Integration** - Only update systems when state changes

---

## Map Rendering System

**Texture-Based Rendering** - Single draw call for entire map via texture-based approach
**GPU Compute Shaders** - All visual processing on GPU for maximum performance
**Vector Curve Borders** - Resolution-independent smooth borders using parametric Bézier curves evaluated in fragment shader
**Spatial Acceleration** - Uniform hash grid (88×32 cells, 64px) for O(nearby) curve lookup preventing GPU timeout
**Border Classification** - Automatic country vs province border detection based on ownership
**BorderMask Texture** - R8 sparse mask for early-out optimization (~90% of pixels skip curve testing)
**Province Selection** - Sub-millisecond province selection via texture lookup (no raycasting)
**Map Texture Management** - Coordinated texture system (~60MB VRAM for 5632×2048 map)
**Border Thickness Control** - Configurable border width and anti-aliasing
**Heightmap Support** - 8-bit grayscale heightmap rendering
**Normal Map Support** - RGB24 normal map for terrain lighting
**Point Filtering** - Pixel-perfect province ID lookup without interpolation
**Single Draw Call Optimization** - Entire map rendered in one draw call
**ProvinceHighlighter** - Province highlighting for selection feedback
**Memory Efficiency** - 720KB curve data vs 40MB rasterized (55x compression)

---

## Map Rendering Infrastructure

**MapTextureManager** - Facade coordinator for all map textures
**CoreTextureSet** - Core textures: Province ID, Owner, Color, Development
**VisualTextureSet** - Visual textures: Terrain, Heightmap, Normal Map
**DynamicTextureSet** - Dynamic textures: Border, BorderMask (R8 sparse), Highlight RenderTextures
**PaletteTextureManager** - Color palette texture with HSV distribution
**BorderComputeDispatcher** - Dispatch border detection and vector curve rendering
**BorderCurveExtractor** - Extract border pixel chains from province pairs using AdjacencySystem
**BorderCurveCache** - Cache Bézier curve segments with metadata (type, provinces, colors)
**BorderCurveRenderer** - Upload Bézier curves to GPU and manage curve buffers
**SpatialHashGrid** - Uniform grid spatial acceleration (88×32 cells, 64px) for O(nearby) curve lookup
**TextureUpdateBridge** - Bridge simulation state changes to GPU textures via EventBus

---

## Map Modes

**Political Map Mode** - Country ownership visualization with color palette
**Terrain Map Mode** - Terrain type visualization from terrain.bmp
**Debug Map Modes** - Heightmap and normal map debug visualization
**IMapModeHandler Interface** - Extensible interface for custom modes
**MapModeManager** - Runtime switching between visualization modes
**GradientMapMode Base Class** - Reusable gradient engine for numeric province data
**ColorGradient** - Configurable color interpolation (red-to-yellow, etc.)
**Dirty Flag Optimization** - Skip texture updates when data unchanged

---

## Billboard Rendering

**BillboardAtlasGenerator** - Generate texture atlases for billboard rendering
**InstancedBillboardRenderer** - Instanced rendering for billboards (units, buildings)
**FogOfWarSystem** - Fog of war rendering system

---

## Visual Styles System

**Visual Style Configuration** - ScriptableObject-based style definitions
**Material Ownership** - Complete material+shader ownership in game layer
**Runtime Style Switching** - Hot-swap visual styles without restart
**Border Configuration** - Per-style border colors, thickness, and anti-aliasing
**Development Gradients** - Configurable 5-tier color progressions
**Engine-Game Separation** - Engine provides infrastructure, game defines visuals

---

## Data Loading System

**JSON5 Support** - JSON5 file parsing for game data
**Burst Province Loading** - Parallel province history loading with Burst compilation
**Burst Country Loading** - Optimized country data loading
**Bitmap Map Loading** - Load provinces.bmp, terrain.bmp, heightmap.bmp, normal maps
**Definition.csv Support** - Complete province definitions (handles 4941 provinces)
**Reference Resolution** - String→ID resolution (e.g., "ENG" → CountryId)
**Cross-Reference Builder** - Automated data linking and validation
**Data Validation** - Integrity checks after loading
**Phase-Based Initialization** - 7-phase startup with progress tracking

---

## Performance Optimizations

**Load Balancing (LoadBalancedScheduler)** - Victoria 3 pattern: cost-based job scheduling (24.9% improvement)
**Zero-Blocking UI Reads** - Double-buffer pattern eliminates lock contention
**Pre-Allocation Policy** - Zero runtime allocations prevents malloc lock contention
**Zero Allocations** - No runtime allocations during gameplay loop
**Frame-Coherent Caching** - Per-frame cache invalidation for expensive queries
**Ring Buffers** - Bounded history storage preventing memory growth
**Dirty Flag Systems** - Update-only-what-changed architecture
**Memory Stability** - Stable memory over 400+ simulated years
**GPU Border Generation** - 2ms for 10k provinces via compute shader
**Structure of Arrays** - Cache-friendly memory layout for country colors
**Array of Structures** - Optimal for province queries accessing multiple fields

---

## Sparse Data Infrastructure

**IDefinition** - Base interface for all definitions (buildings, modifiers, trade goods)
**ISparseCollection** - Non-generic interface for polymorphic management
**SparseCollectionManager<TKey, TValue>** - Generic sparse storage with NativeParallelMultiHashMap
**One-to-Many Relationships** - Province → BuildingIDs with O(1) to O(m) queries
**Pre-Allocation** - Allocator.Persistent with capacity warnings at 80%/95%
**Memory Scaling** - 96% memory reduction vs dense approach at mod scale
**Zero Allocation Iteration** - ProcessValues API for zero-allocation iteration

---

## Utility Systems

**ArchonLogger** - Categorized logging system with subsystems
**DeterministicRandom** - Seeded xorshift128+ RNG for deterministic gameplay
**Strongly-Typed IDs** - Type-safe wrappers (ProvinceId, CountryId, BuildingId, etc.)
**Registry System** - Fast lookup for static game data
**CircularBuffer** - Bounded buffers for history and event storage

---

## Interaction Systems

**ProvinceSelector** - Texture-based selection with <1ms performance
**MapInitializer** - Automated setup of map subsystems
**MapSystemCoordinator** - Coordinate map subsystems
**FastAdjacencyScanner** - Fast province adjacency scanning

---

## Data Structures

**ProvinceState (8 bytes)** - ownerID(2), controllerID(2), terrainType(2), gameDataSlot(2)
**ProvinceColdData** - Name, color, bounds, history, modifiers
**CountryHotData** - Compact country state for cache efficiency
**CountryColdData** - Extended country metadata
**FixedPoint64** - 32.32 fixed-point for deterministic math
**FixedPoint32** - 16.16 fixed-point for compact storage
**FixedPoint2** - 2D vector with fixed-point components

---

## Testing & Validation

**Province State Tests** - Validation of 8-byte struct operations
**Texture Infrastructure Tests** - GPU texture creation and binding
**Command System Tests** - Command execution validation
**Integration Tests** - Full pipeline texture→simulation→GPU
**Determinism Tests** - Cross-platform checksum validation

---

## Shader Infrastructure

**BorderDetection.compute** - Dual border generation (country + province)
**MapFallback.shader** - Pink fallback for missing visual styles
**MapModeCommon.hlsl** - Shared utilities (ID decoding, sampling)

---

## Unity Integration

**URP Support** - Universal Render Pipeline compatibility
**Burst Compilation** - Optimized code generation for hot paths
**Job System** - Parallel processing for data loading
**IL2CPP Backend** - Ahead-of-time compilation support
**Linear Color Space** - Modern color workflow
**Compute Shader Coordination** - CPU-GPU synchronization patterns

---

## Multiplayer-Ready Features

**Deterministic Math** - FixedPoint64 for identical results across platforms
**Command Checksums** - Validation for state consistency
**State Serialization** - 80KB for complete 10k province state
**Command Pattern** - Network-friendly state changes
**Rollback Support** - Command buffer for client prediction (designed, not implemented)

---

## Quick Stats

**Engine Code** - 119 Core scripts (5 AI) + 57 Map scripts
**Systems** - TimeManager, ProvinceSystem, CountrySystem, DiplomacySystem, UnitSystem, PathfindingSystem, AdjacencySystem, ResourceSystem, ModifierSystem, AISystem
**Documentation** - 13 architecture docs + session logs + file registries

---

*Updated: 2025-10-25*
