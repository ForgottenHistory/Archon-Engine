# Archon Engine - Current Features

**Last Updated:** 2026-01-24

---

## TL;DR

**Core:** 8-byte province structs, fixed-point math, command pattern, zero-allocation EventBus, double-buffer snapshots

**Systems:** Provinces, countries, diplomacy (wars/treaties/opinions), units with pathfinding, resources, modifiers, AI with tier scheduling

**Map:** Texture-based rendering, three border modes (distance field/mesh/pixelated), terrain blending, map modes, visual styles

**Multiplayer:** Lockstep sync, lobby system, time sync, dual command processors (ENGINE + GAME layer), auto desync recovery

**Infrastructure:** JSON5 loading, phase-based init, save/load, localization, Burst compilation, sparse collections, mod loading

**Not Implemented:** Steam transport, host migration

---

## Detailed Feature List

This document lists all implemented features in the Archon Engine organized by category.

---

## Core Architecture

- **Dual-Layer Architecture** - Separation of deterministic simulation (CPU) and high-performance presentation (GPU)
- **8-Byte Province Struct** - Fixed-size ProvinceState enables 10k provinces in 80KB with cache-friendly access- 
- **Fixed-Point Math (FixedPoint64)** - Deterministic 32.32 fixed-point arithmetic for multiplayer-ready simulation
- **Hot/Cold Data Separation** - Performance optimization separating frequently-accessed from rarely-accessed data
- **Command Pattern** - All state changes through commands for validation, networking, and replay support
- **Zero-Allocation EventBus** - Frame-coherent event system with 99.99% allocation reduction (EventQueue<T> pattern)
- **GameStateSnapshot (Double-Buffer)** - Zero-blocking UI reads via O(1) pointer swap (240KB at 10k provinces)
- **NativeArray Storage** - Contiguous memory layout for optimal cache performance
- **Deterministic Simulation** - Identical results across platforms for multiplayer compatibility
- **Engine-Game Separation** - ProvinceState = ENGINE (8 bytes), game-specific data in GAME layer slot

---

## System Infrastructure

- **GameSystem Base Class** - Abstract base for all game systems with standard lifecycle hooks
- **SystemRegistry** - Manages system registration and initialization with dependency ordering
- **Topological Sort** - Automatic dependency ordering via reflection
- **Property Injection** - Dependencies injected via properties (MonoBehaviour compatible)
- **Lifecycle Hooks** - Initialize/Shutdown/OnSaveGame/OnLoadGame
- **Circular Dependency Detection** - Fail-fast validation at startup

---

## Province System

- **Province Management** - Efficient management of 10,000+ provinces
- **Province Ownership Tracking** - Fast owner lookup with bidirectional mapping (province→owner, owner→provinces)
- **Province Development** - Economic development levels per province
- **Province Terrain System** - Terrain type tracking and modifiers
- **Province History Database** - Bounded history storage with ring buffers
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

## Diplomacy System

- **RelationData (8 bytes)** - Hot data for diplomatic relationships (opinion, treaties, lastContact, flags)
- **DiplomacyColdData** - Flat modifier storage with relationship keys (Burst-compatible)
- **OpinionModifier** - Time-decaying modifiers with linear decay formula
- **DiplomacySystem** - Sparse storage for active relationships (NativeParallelHashMap O(1) lookups)
- **War/Peace Commands** - DeclareWarCommand, MakePeaceCommand with validation
- **Treaty Commands** - FormAlliance, BreakAlliance, GrantMilitaryAccess, GuaranteeIndependence, etc.
- **Opinion System** - Stackable modifiers clamped to [-200, +200] range
- **Monthly Decay** - Burst-compiled parallel decay processing (IJobParallelFor)
- **Treaty Validation** - Mutual existence checks, war state validation
- **Performance** - 3ms for 610,750 modifiers (350 countries, 100% density) with Burst compilation, <0.0001ms GetOpinion queries
- **Memory** - 954 KB hot data + 19 MB flat modifier storage for 61,075 relationships (all possible pairs), scales linearly with modifier count

---

## Units System

- **UnitSystem** - Military unit management with movement, combat, organization
- **UnitState (16 bytes)** - Fixed-size hot state: unitID, ownerID, provinceID, typeID, organization, morale, strength, movementQueue
- **UnitColdData** - Cold unit data: Name, Experience, CombatHistory, Equipment
- **Unit Commands** - CreateUnit, MoveUnit, DisbandUnit with validation
- **Unit Movement Queue** - Fixed-size circular buffer for pathfinding waypoints
- **Unit Events** - UnitCreated, UnitMoved, UnitDisbanded, CombatResolved
- **Sparse Collections** - Province-to-units mapping with NativeParallelMultiHashMap
- **Hot/Cold Separation** - Frequently-accessed data separate from detailed information

---

## Pathfinding System

- **AdjacencySystem** - Province neighbor management with NativeParallelMultiHashMap for Burst compatibility
- **NativeAdjacencyData** - Read-only struct exposing neighbors for Burst jobs (GetNeighbors enumerator)
- **PathfindingSystem** - Burst-compiled A* pathfinding with binary min-heap priority queue
- **BurstPathfindingJob** - IJob implementing A* with O(log n) heap operations, NativeHashSet/NativeHashMap
- **NativeMinHeap<T>** - Generic Burst-compatible priority queue for pathfinding and other algorithms
- **Managed Fallback** - MovementValidator support via managed code path when validation needed
- **GetNeighbors** - O(1) neighbor lookup with sparse storage
- **FindPath** - A* algorithm with ~0.1ms typical performance on 13k provinces
- **IMovementCostCalculator** - Interface for custom movement cost calculation with PathContext
- **PathOptions** - Configurable pathfinding: forbidden provinces, avoid provinces, max path length
- **PathResult** - Structured result with PathStatus enum (Success, NoPath, StartInvalid, etc.)
- **PathCache** - LRU cache with automatic invalidation on ownership changes
- **FindPathWithOptions** - Advanced pathfinding with full configuration
- **PathExists/GetDistance** - Convenience methods for quick queries
- **Path Statistics** - TotalSearches, CacheHits, CacheHitRate tracking

---

## AI System

- **AISystem** - Central AI manager with tier-based scheduling for countries
- **AIState (8 bytes)** - Fixed-size hot state: countryID, tier, flags, activeGoalID, lastProcessedHour
- **AIGoal** - Abstract base class with Evaluate/Execute pattern; Initialize(EventBus) for event-driven caching
- **AIScheduler** - Goal evaluation with tier-based intervals (near AI every hour, far AI every 72 hours)
- **AIGoalRegistry** - Plug-and-play goal registration; passes EventBus for cache invalidation subscriptions
- **AIDistanceCalculator** - Burst-compiled BFS via BurstBFSDistanceJob for player distance calculation
- **BurstBFSDistanceJob** - IJob traversing province graph using NativeAdjacencyData and NativeProvinceData
- **NativeProvinceData** - Read-only struct exposing province owners for Burst jobs
- **Tier-Based Scheduling** - Near countries (tier 0) processed frequently, far countries (tier 3) rarely
- **Event-Driven Caching** - Goals subscribe to ProvinceOwnershipChangedEvent for cache invalidation
- **Zero Allocations** - Pre-allocated NativeArray with Allocator.Persistent
- **Deterministic Scoring** - FixedPoint64 goal scores with ordered evaluation
- **Command Pattern Integration** - AI uses player commands (DeclareWarCommand, BuildBuildingCommand)
- **Performance** - 60+ FPS at 10x speed with tier scheduling and event-driven caching
- **IGoalSelector** - Interface for custom goal selection strategies (weighted random, cooldowns, priority overrides)
- **GoalConstraint System** - Declarative preconditions: MinProvinces, MinResource, AtWar, AtWarWith, DelegateConstraint
- **AIStatistics** - Runtime statistics: TotalProcessed, TotalSkipped, TotalTimeouts, AverageProcessingTimeMs
- **AIDebugInfo** - Per-country debug info: Tier, ActiveGoal, FailedConstraints
- **Execution Timeout** - Configurable timeout for runaway goal execution
- **Query Methods** - GetActiveGoal, GetCountriesByTier, GetCountryCountByTier, GetActiveAICount

---

## Modifier System

- **ModifierValue** - Single modifier with base/additive/multiplicative bonuses
- **ModifierSet** - Fixed-size array of 512 modifier types (4KB, zero allocations)
- **ModifierSource** - Tracks modifier origin for tooltips and removal
- **ActiveModifierList** - Maintains active modifiers with expiration support
- **ScopedModifierContainer** - Province/Country/Global scope hierarchy
- **ModifierSystem** - Central manager with scope inheritance (Province ← Country ← Global)
- **Formula** - (base + additive) × (1 + multiplicative)
- **Performance** - <0.1ms lookup, <20MB for 10k provinces, zero allocations

---

## Resource System

- **ResourceSystem** - Multi-resource treasury management with FixedPoint64 values
- **ResourceDefinition** - Data structure for resource properties
- **Resource Query API** - GetResource/AddResource/RemoveResource for any resource type
- **Event System** - EventBus integration for resource changes (ResourceChangedEvent)
- **Unlimited Types** - Support for any number of resource types (gold, manpower, prestige, etc.)
- **IResourceProvider** - Interface for custom resource calculation (income, expenses)
- **ResourceCost** - Structured cost validation with CanAfford/TrySpend pattern
- **Batch Operations** - AddResourceToAll, SetResourceForAll with single event
- **HasSufficientResources** - Pre-validation for complex costs

---

## Command System

- **ICommand Interface** - Base command interface: Execute, Validate, GetChecksum, Dispose
- **CommandRegistry** - Reflection-based command auto-discovery
- **CommandMetadataAttribute** - Declarative command metadata (name, aliases, description, usage)
- **ICommandFactory** - Interface for argument parsing and command creation
- **CommandProcessor** - Deterministic command validation and execution
- **Auto-Registration** - Commands auto-discover via reflection, zero manual registration
- **Self-Documenting** - Metadata generates help text automatically

---

## Save/Load System

- **SaveManager** - Orchestrates save/load across all systems with layer separation via callbacks
- **SaveGameData** - Binary save file structure (header + metadata + system data dictionary)
- **SerializationHelper** - Binary serialization utilities (FixedPoint64, NativeArray, strings)
- **Hybrid Architecture** - Snapshot for speed + command log for verification
- **Atomic Writes** - Temp file → rename pattern prevents corruption on crash
- **Hot Data Serialization** - All core systems implement SaveState/LoadState
- **Double Buffer Sync** - GameStateSnapshot.SyncBuffersAfterLoad() prevents stale UI reads
- **Post-Load Finalization** - SaveLoadGameCoordinator rebuilds derived data
- **GameLoadedEvent** - Event broadcast after load for UI refresh
- **Quicksave/Quickload** - F6/F7 hotkeys for rapid save/load iteration
- **Systems Supported** - TimeManager, ResourceSystem, ProvinceSystem, ModifierSystem, CountrySystem, DiplomacySystem, UnitSystem

---

## Time System

- **Tick-Based Progression** - Frame-independent game time with fixed timesteps
- **365-Day Calendar** - Consistent calendar system
- **Layered Update Frequencies** - Hourly, daily, weekly, monthly, yearly tick events
- **Game Speed Controls** - Pause, slow, normal, fast, very fast speeds
- **Time Events** - EventBus integration for time-based system updates
- **Dirty Flag Integration** - Only update systems when state changes
- **ICalendar Interface** - Pluggable calendar system for custom calendars
- **StandardCalendar** - Default 365-day implementation with era support
- **CalendarConstants** - Single source of truth for time constants
- **GameTime Struct** - Full comparison operators (<, >, <=, >=), IComparable<GameTime>
- **GameTime Arithmetic** - AddHours, AddDays, AddMonths, AddYears
- **GameTime Factory** - FromTotalHours, Create(year, month, day, hour)
- **Duration Methods** - HoursBetween, DaysBetween for time calculations
- **Era Formatting** - BC/AD year formatting via ICalendar.FormatYear

---

## Map Rendering System

- **Texture-Based Rendering** - Single draw call for entire map via texture-based approach
- **GPU Compute Shaders** - All visual processing on GPU for maximum performance
- **Three Border Rendering Modes:**
  - **Distance Field** - Fragment-shader borders using JFA distance field (smooth, 3D-compatible for tessellation)
  - **Mesh** - Triangle strip geometry with runtime style updates (resolution-independent smooth borders)
  - **Pixelated** - BorderMask pixel-perfect borders (retro aesthetic, 1-pixel precision)
- **Static Geometry + Dynamic Appearance** - Pre-compute geometry once, update colors/styles at runtime
- **Border Classification** - Automatic country vs province border detection based on ownership
- **BorderMask Texture** - R8 sparse mask for pixel-perfect borders and early-out optimization
- **Province Selection** - Sub-millisecond province selection via texture lookup (no raycasting)
- **Map Texture Management** - Coordinated texture system (~60MB VRAM for 5632×2048 map)
- **Border Thickness Control** - Configurable border width and anti-aliasing per mode
- **Heightmap Support** - 8-bit grayscale heightmap rendering
- **Normal Map Support** - RGB24 normal map for terrain lighting
- **Point Filtering** - Pixel-perfect province ID lookup without interpolation
- **Single Draw Call Optimization** - Entire map rendered in one draw call
- **ProvinceHighlighter** - Province highlighting for selection feedback

---

## Map Rendering Infrastructure

- **MapTextureManager** - Facade coordinator for all map textures
- **MapRendererRegistry** - Central registry for pluggable renderer implementations (6 systems)
- **CoreTextureSet** - Core textures: Province ID, Owner, Color, Development
- **VisualTextureSet** - Visual textures: Terrain, Heightmap, Normal Map, Texture2DArray (27 detail textures)
- **DynamicTextureSet** - Dynamic textures: Border, BorderMask (R8 sparse), Highlight RenderTextures
- **PaletteTextureManager** - Color palette texture with HSV distribution
- **BorderComputeDispatcher** - Orchestrates border rendering across three modes (delegates to IBorderRenderer)
- **BorderCurveExtractor** - Extract border pixel chains from province pairs, apply RDP simplification + Chaikin smoothing
- **BorderCurveCache** - Cache smooth polyline segments with runtime styles (static geometry + dynamic appearance)
- **BorderMeshGenerator** - Generate triangle strip geometry from polylines
- **BorderMeshRenderer** - Render mesh borders with dynamic color updates
- **BorderDistanceFieldGenerator** - Generate JFA distance field for fragment-shader borders
- **TextureUpdateBridge** - Bridge simulation state changes to GPU textures via EventBus
- **TerrainBlendMapGenerator** - Imperator Rome-style 4-channel blend map generation (DetailIndexTexture + DetailMaskTexture)
- **DetailTextureArrayLoader** - Loads 512x512 detail textures from Assets/Data/textures/terrain_detail/
- **ShaderCompositorBase** - Abstract base for layer compositing implementations

---

## Terrain Rendering System (Imperator Rome-Style)

- **4-Channel Terrain Blending** - Ultra-smooth watercolor-like transitions between terrain types
- **Dual-Layer Rendering** - Macro: smooth color blending, Micro: sharp texture detail
- **Manual Bilinear Interpolation** - Fragment shader 4-tap filtering matching Imperator Rome technique
- **DetailIndexTexture** - RGBA8 storing 4 terrain indices per pixel (index/255 encoding)
- **DetailMaskTexture** - RGBA8 storing 4 blend weights per pixel (normalized 0-1)
- **GPU Blend Map Generation** - Compute shader pre-processes blend maps at load time (~50-100ms)
- **Configurable Sample Radius** - Adjustable neighborhood sampling (default 5x5, supports up to 11x11+)
- **Blend Sharpness Control** - Power function for transition tuning (1.0=linear, >1.0=sharper, <1.0=softer)
- **Weight Accumulation** - Boundary-aware blending prevents black artifacts at province borders
- **Texture2DArray Detail Textures** - 27 terrain types with 512x512 tileable multiply-blend textures
- **Moddable Texture System** - Drop-in PNG/JPG files in Assets/Data/textures/terrain_detail/
- **Automatic Fallback** - Missing textures use neutral gray (128,128,128) for no visual effect
- **Performance** - 8 texture samples + accumulation loop per pixel (acceptable for terrain quality)
- **Proven Architecture** - Based on analysis of Imperator Rome's actual pixel shader (375_pixel.txt)

---

## Map Modes

- **Political Map Mode** - Country ownership visualization with color palette
- **Terrain Map Mode** - Imperator Rome-style terrain rendering with smooth blending
- **Debug Map Modes** - Heightmap and normal map debug visualization
- **IMapModeHandler Interface** - Extensible interface for custom map mode DATA
- **IMapModeColorizer Interface** - Pluggable colorization (separate from data handling)
- **MapModeManager** - Runtime switching between visualization modes
- **GradientMapMode Base Class** - Reusable gradient engine for numeric province data
- **GradientMapModeColorizer** - Default 3-color gradient colorization via GPU compute
- **ColorGradient** - Configurable color interpolation (red-to-yellow, etc.)
- **Dirty Flag Optimization** - Skip texture updates when data unchanged

---

## Billboard Rendering

**BillboardAtlasGenerator** - Generate texture atlases for billboard rendering
**InstancedBillboardRenderer** - Instanced rendering for billboards (units, buildings)
**FogOfWarSystem** - Fog of war rendering system

---

## Visual Styles System

- **Visual Style Configuration** - ScriptableObject-based style definitions
- **Material Ownership** - Complete material+shader ownership in game layer
- **Runtime Style Switching** - Hot-swap visual styles without restart
- **Border Configuration** - Per-style border colors, thickness, and anti-aliasing
- **Development Gradients** - Configurable 5-tier color progressions
- **Engine-Game Separation** - Engine provides infrastructure, game defines visuals
- **Two-Level Customization** - Fine-grained (pluggable interfaces) or complete override (custom material)

---

## Pluggable Rendering Architecture (Pattern 20)

- **MapRendererRegistry** - Central registry for all pluggable rendering implementations
- **Interface + Base Class + Default** - Consistent pattern across all 6 rendering systems
- **Runtime Switching** - Change renderers without restart via registry
- **String ID References** - VisualStyleConfiguration references renderers by ID
- **Backwards Compatible** - Enum-based selection maps to renderer IDs

### Pluggable Renderer Interfaces

- **IBorderRenderer** - Border generation (DistanceField, PixelPerfect, MeshGeometry implementations)
- **IHighlightRenderer** - Province selection/hover highlighting (Default GPU compute)
- **IFogOfWarRenderer** - Fog of war visualization (Default GPU compute)
- **ITerrainRenderer** - Terrain blend map generation (Default 4-channel)
- **IMapModeColorizer** - Map mode colorization (Gradient 3-color implementation)
- **IShaderCompositor** - Layer compositing with configurable blend modes

### Shader Compositor System

- **Compositing.hlsl** - Shader-side layer compositing utilities
- **6 Blend Modes** - Normal, Multiply, Screen, Overlay, Additive, SoftLight
- **Per-Layer Configuration** - Enable/disable and blend mode per layer
- **4 Preset Compositors:**
  - **DefaultShaderCompositor** - All layers, normal blend
  - **MinimalShaderCompositor** - No fog/overlay (performance mode)
  - **StylizedShaderCompositor** - Multiply borders, additive highlights (EU4-like)
  - **CinematicShaderCompositor** - Overlay blends, high contrast (screenshots)
- **C# + Shader Hybrid** - C# configures material properties, shader reads blend modes

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

- **Load Balancing (LoadBalancedScheduler)** - Victoria 3 pattern: cost-based job scheduling (24.9% improvement)
- **Zero-Blocking UI Reads** - Double-buffer pattern eliminates lock contention
- **Pre-Allocation Policy** - Zero runtime allocations prevents malloc lock contention
- **Zero Allocations** - No runtime allocations during gameplay loop
- **Frame-Coherent Caching** - Per-frame cache invalidation for expensive queries
- **Ring Buffers** - Bounded history storage preventing memory growth
- **Dirty Flag Systems** - Update-only-what-changed architecture
- **Memory Stability** - Stable memory over 400+ simulated years
- **GPU Border Generation** - 2ms for 10k provinces via compute shader
- **Structure of Arrays** - Cache-friendly memory layout for country colors
- **Array of Structures** - Optimal for province queries accessing multiple fields

---

## Sparse Data Infrastructure

- **IDefinition** - Base interface for all definitions (buildings, modifiers, trade goods)
- **ISparseCollection** - Non-generic interface for polymorphic management
- **SparseCollectionManager<TKey, TValue>** - Generic sparse storage with NativeParallelMultiHashMap
- **One-to-Many Relationships** - Province → BuildingIDs with O(1) to O(m) queries
- **Pre-Allocation** - Allocator.Persistent with capacity warnings at 80%/95%
- **Memory Scaling** - 96% memory reduction vs dense approach at mod scale
- **Zero Allocation Iteration** - ProcessValues API for zero-allocation iteration

---

## Utility Systems

- **ArchonLogger** - Categorized logging system with subsystems
- **DeterministicRandom** - Seeded xorshift128+ RNG for deterministic gameplay
  - **NextGaussian** - Normal distribution via Central Limit Theorem (sum of 12 uniforms)
  - **NextWeightedElement/Index** - Weighted random selection with int[] or FixedPoint32[] weights
  - **NextElementExcept** - Random selection with value or index exclusion
  - **ToSeedPhrase/FromSeedPhrase** - Human-readable 8-word state export/import
- **Strongly-Typed IDs** - Type-safe wrappers (ProvinceId, CountryId, BuildingId, etc.)
- **Registry System** - Fast lookup for static game data
- **CircularBuffer** - Bounded buffers for history and event storage
- **Result<T>** - Railway-oriented error handling with Success/Failure pattern
- **FluentValidator** - Chainable validation with Require, Ensure, WhenValid, Match

---

## Interaction Systems

- **ProvinceSelector** - Texture-based selection with <1ms performance
- **MapInitializer** - Automated setup of map subsystems
- **MapSystemCoordinator** - Coordinate map subsystems
- **FastAdjacencyScanner** - Fast province adjacency scanning

---

## Data Structures

- **ProvinceState (8 bytes)** - ownerID(2), controllerID(2), terrainType(2), gameDataSlot(2)
- **ProvinceColdData** - Name, color, bounds, history, modifiers
- **CountryHotData** - Compact country state for cache efficiency
- **CountryColdData** - Extended country metadata
- **FixedPoint64** - 32.32 fixed-point for deterministic math
  - IsZero, IsPositive, IsNegative properties
  - Sqrt, Pow, Lerp, InverseLerp, Remap math functions
  - Full comparison and arithmetic operators
- **FixedPoint32** - 16.16 fixed-point for compact storage (full parity with FixedPoint64)
  - FromFraction, FromFloat, FromDouble factory methods
  - Division, modulo, Abs, Min, Max, Clamp, Floor, Ceiling, Round
  - IEquatable, IComparable, ToBytes/FromBytes serialization
- **FixedPoint2** - 2D vector with fixed-point components

---

## Adjacency System

- **AdjacencySystem** - Province neighbor graph with managed and native storage
- **NativeAdjacencyData** - Read-only struct for Burst job compatibility
- **IsAdjacent** - O(1) HashSet lookup for neighbor check
- **GetNeighbors** - Zero-allocation buffer version for hot paths
- **GetNeighborsWhere** - Predicate-filtered neighbor query
- **GetConnectedRegion** - BFS flood fill for connected province sets
- **GetSharedBorderProvinces** - Find provinces where two countries touch
- **IsBridgeProvince** - Detect strategic choke points (articulation points)
- **FindBridgeProvinces** - Find all bridge provinces in a region
- **AdjacencyStats** - Queryable statistics struct (ProvinceCount, AvgNeighbors, etc.)

---

## Localization System

- **LocalizationSystem** - Central string localization manager
- **ILocalizationProvider** - Interface for custom localization sources
- **LocalizationKey** - Strongly-typed localization keys
- **Placeholder Substitution** - {0}, {1} style parameter replacement
- **Fallback Chain** - Key → Default language → Key itself
- **Runtime Language Switching** - Change language without restart
- **Batch Loading** - Load localization files efficiently

---

## Query System

- **ProvinceQuery** - Fluent builder for province queries
- **CountryQuery** - Fluent builder for country queries
- **Query Operators** - Where, OrderBy, Take, Skip, First, Count, Any, All
- **Logical Operators** - And, Or, Not for complex predicates
- **ExecuteCount** - Count-only execution without allocation
- **Short-Circuit Evaluation** - Any/All stop early when result known
- **Frame-Coherent Results** - Cached results valid for current frame

---

## Caching Framework

- **ICache<TKey, TValue>** - Generic caching interface
- **LRUCache** - Least-recently-used eviction policy
- **FrameCache** - Automatic invalidation on frame change
- **TTLCache** - Time-to-live based expiration
- **Cache Statistics** - Hits, misses, hit rate tracking
- **Automatic Invalidation** - Event-based cache clearing

---

## Loader System

- **ILoader<T>** - Interface for data loaders
- **LoaderFactory** - Factory pattern for loader instantiation
- **Async Loading** - Non-blocking data loading support
- **Progress Reporting** - Load progress callbacks
- **Error Handling** - Result<T> based error propagation
- **Dependency Resolution** - Automatic loader ordering

---

## Testing & Validation

- **Province State Tests** - Validation of 8-byte struct operations
- **Texture Infrastructure Tests** - GPU texture creation and binding
- **Command System Tests** - Command execution validation
- **Integration Tests** - Full pipeline texture→simulation→GPU
- **Determinism Tests** - Cross-platform checksum validation

---

## Shader Infrastructure

- **BorderDetection.compute** - Dual border generation (country + province)
- **TerrainBlendMapGenerator.compute** - 4-channel terrain blend map generation with configurable sampling
- **Compositing.hlsl** - Modular layer compositing with 6 blend modes (Normal, Multiply, Screen, Overlay, Additive, SoftLight)
- **MapModeTerrain.hlsl** - Imperator Rome manual bilinear filtering + detail texture blending
- **MapFallback.shader** - Pink fallback for missing visual styles
- **MapModeCommon.hlsl** - Shared utilities (ID decoding, sampling)
- **DefaultCommon.hlsl** - Shared compositor parameters and layer enable flags

---

## Unity Integration

- **URP Support** - Universal Render Pipeline compatibility
- **Burst Compilation** - Optimized code generation for hot paths
- **Job System** - Parallel processing for data loading
- **IL2CPP Backend** - Ahead-of-time compilation support
- **Linear Color Space** - Modern color workflow
- **Compute Shader Coordination** - CPU-GPU synchronization patterns

---

## Multiplayer System

### Network Architecture
- **Lockstep Synchronization** - All clients run identical simulation, synced via commands
- **Player-Hosted Sessions** - One player hosts (server + client), others connect as clients
- **Transport Abstraction** - INetworkTransport interface for pluggable backends
- **DirectTransport** - Unity Transport Package implementation for LAN/direct IP

### Lobby System
- **Host/Join Flow** - Host game or join by IP address
- **Country Selection** - Players select countries in lobby with visual feedback
- **Ready/Start** - All players ready before host can start
- **Player Slots** - Track connected players with country assignments

### Command Synchronization
- **Unified CommandProcessor** - All commands (ICommand) with network sync
- **Client→Host→Broadcast** - Clients send commands to host, host validates and broadcasts
- **Auto-Registration** - Commands discovered via reflection, sorted alphabetically for determinism
- **Explicit CountryId** - Commands carry country ID (never use local playerState)

### Time Synchronization
- **NetworkTimeSync** - Game time aligned across all clients
- **Host-Controlled Speed** - Host sets game speed, clients follow
- **Pause Synchronization** - Pause state synced across all clients

### Desync Handling
- **DesyncDetector** - Periodic checksum verification infrastructure
- **DesyncRecovery** - Automatic state resync (reuses late-join sync)
- **Graceful Recovery** - Brief pause (1-3 seconds) vs Paradox-style rehost

### AI in Multiplayer
- **Host-Only AI** - AI runs only on host to prevent divergent decisions
- **Human Country Detection** - Skip AI processing for human-controlled countries
- **Command Pattern** - AI uses same commands as players for sync

### Key Files
- **Network/** - INetworkTransport, DirectTransport, NetworkManager, NetworkBridge, NetworkTimeSync
- **Core/Commands/CommandProcessor** - Command networking with multiplayer sync
- **StarterKit/Network/** - NetworkInitializer, LobbyUI

---

## Modding System

- **ModLoader** - Mod discovery from StreamingAssets/Mods directory
- **Mod Manifest** - mod.json files with metadata (name, version, dependencies)
- **Load Order** - Mods loaded after base game data

---

*Updated: 2026-01-24*
