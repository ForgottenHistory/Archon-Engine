# Core Layer File Registry
**Namespace:** `Core.*`
**Purpose:** Deterministic simulation - game state, logic, commands
**Rules:** FixedPoint64 only, deterministic ops, no Unity in hot paths, command pattern for state changes
**Status:** ✅ Multiplayer-ready

---

## Systems/

**TimeManager.cs** [MULTIPLAYER_CRITICAL] [STABLE]
- Tick-based time, game speed, 360-day calendar
- Emits: HourlyTickEvent, DailyTickEvent, MonthlyTickEvent, YearlyTickEvent
- Status: ✅ Deterministic (2025-09-30)

**ProvinceSystem.cs** [HOT_PATH] [MULTIPLAYER_CRITICAL] [STABLE]
- Facade pattern - delegates to ProvinceDataManager, ProvinceStateLoader, ProvinceHistoryDatabase
- API: Get/Set owner/development/terrain, country queries, loading, history
- Status: ✅ Refactored (2025-10-05)

**Province/ProvinceDataManager.cs** [HOT_PATH] [MULTIPLAYER_CRITICAL]
- Manages province NativeArray operations
- API: AddProvince, Get/Set properties, GetCountryProvinces
- Status: ✅ Extracted (2025-10-05)

**Province/ProvinceStateLoader.cs** [HOT_PATH]
- Loading and applying province initial states
- Uses: BurstProvinceHistoryLoader, ProvinceDataManager, ProvinceHistoryDatabase
- Status: ✅ Extracted (2025-10-05)

**Province/ProvinceEvents.cs** [STABLE]
- Event definitions: ProvinceSystemInitializedEvent, ProvinceOwnershipChangedEvent, ProvinceDevelopmentChangedEvent
- Status: ✅ Extracted (2025-10-05)

**ProvinceSimulation.cs**
- Province-level simulation logic (development, economy)
- Pattern: Operates on ProvinceState, emits events
- Status: ✅ Simulation core

**CountrySystem.cs** [REFACTORED]
- Country system facade - delegates to CountryDataManager, CountryStateLoader
- API: GetCountryColor/Tag/IdFromTag, InitializeFromCountryData
- Status: ✅ Refactored (2025-10-05)

**Country/CountryEvents.cs** [STABLE]
- Event definitions: CountrySystemInitializedEvent, CountryColorChangedEvent
- Status: ✅ Extracted (2025-10-05)

**Country/CountryDataManager.cs** [STABLE]
- Manages country hot/cold data (NativeArray + dictionary)
- Pattern: Structure of Arrays (SoA) for colors, tag hash mapping
- Status: ✅ Extracted (2025-10-05)

**Country/CountryStateLoader.cs** [STABLE]
- Loading and initializing country data from files
- Handles duplicates, creates default unowned country
- Status: ✅ Extracted (2025-10-05)

**CommandProcessor.cs** [MULTIPLAYER_CRITICAL]
- Deterministic command validation, tick-based execution
- Pattern: Command pattern, sorts by type then player ID
- Status: ✅ Multiplayer-ready

**ProvinceHistoryDatabase.cs**
- Bounded history storage (recent + compressed + statistical)
- Pattern: Recent (100 events) + compressed medium + ancient stats
- Status: ✅ Memory-bounded

---

## Data/

**ProvinceState.cs** [MULTIPLAYER_CRITICAL] [REFACTORED]
- 8-byte ENGINE state: ownerID(2) + controllerID(2) + terrainType(2) + gameDataSlot(2)
- Architecture: Engine MECHANISM, game layer POLICY
- REMOVED (to Game layer): development, fortLevel, flags → HegemonProvinceSystem
- API: CreateDefault/Owned/Ocean, ToBytes/FromBytes, IsOwned/Occupied/Ocean
- Status: ✅ Engine-game separation (2025-10-09)

### Data/SparseData/ - Sparse Collection Infrastructure

**IDefinition.cs** [NEW]
- Base interface for all definition types (buildings, modifiers, trade goods)
- Fields: ID (runtime ushort), StringID (stable string), Version (compatibility)
- Purpose: Enables mod compatibility with stable string identifiers
- Status: ✅ Foundation (2025-10-15)

**ISparseCollection.cs** [NEW]
- Non-generic interface for polymorphic sparse collection management
- Properties: Name, IsInitialized, Capacity, Count, CapacityUsage
- Includes: SparseCollectionStats struct for memory monitoring
- Purpose: Unified memory monitoring and disposal across all sparse collections
- Status: ✅ Foundation (2025-10-15)

**SparseCollectionManager<TKey, TValue>.cs** [NEW] [HOT_PATH]
- Generic sparse storage using NativeParallelMultiHashMap (Collections 2.1+)
- Pattern: One-to-many relationships (Province → BuildingIDs)
- Memory: Scales with ACTUAL items (200KB) not POSSIBLE items (5MB dense)
- Query APIs: Has/HasAny/Get/GetCount (O(1) to O(m) where m = items per key)
- Modification APIs: Add/Remove/RemoveAll
- Iteration APIs: ProcessValues (zero allocation), GetKeys
- Pre-allocation: Allocator.Persistent, capacity warnings at 80%/95%
- Purpose: Prevents HOI4's 30→500 equipment disaster (16x slowdown)
- Status: ✅ Phase 1 & 2 complete (2025-10-15)

**ProvinceColdData.cs** [STABLE]
- Rarely-accessed data: Name, Color, Bounds, RecentHistory (CircularBuffer<100>)
- Modifiers: Dictionary for gameplay bonuses
- Status: ✅ Deterministic

**CountryData.cs**
- Hot (8-byte) + cold data structures
- Types: CountryHotData (struct), CountryColdData (class), CountryDataCollection
- Status: ✅ Hot/cold separation

**FixedPoint64.cs** [MULTIPLAYER_CRITICAL] [STABLE]
- 32.32 fixed-point math (32 integer, 32 fractional, 8 bytes)
- ALL simulation math must use this (NO float/double)
- Status: ✅ Network-safe (2025-09-30)

**DeterministicRandom.cs** [MULTIPLAYER_CRITICAL]
- xorshift128+ RNG for deterministic random
- API: NextUInt/Int/Fixed/Bool, Shuffle
- Status: ✅ Seed-based, serializable

**Math/FixedPointMath.cs** [MULTIPLAYER_CRITICAL]
- FixedPoint32 (16.16 format), FixedPoint2 (2D vector)
- Pattern: Immutable value types with operators
- Status: ✅ Deterministic

**ProvinceInitialState.cs**
- Initial province data from scenarios
- Converts to ProvinceState via ToProvinceState()
- Status: ✅ Data transfer object

**ProvinceStateSerializer.cs**
- Network serialization for ProvinceState (8-byte structs)
- Status: ✅ Network-ready

---

## Data/Ids/ - Strongly-Typed IDs

**ProvinceId, CountryId, ReligionId, CultureId, TradeGoodId, TerrainId, BuildingId**
- Type-safe ID wrappers (ushort internally)
- Prevents ID confusion
- Status: ✅ Type safety

---

## Commands/

**ICommand.cs** [MULTIPLAYER_CRITICAL]
- Base command interface: Execute, Validate, GetChecksum, Dispose
- Status: ✅ Network-ready interface

**IProvinceCommand.cs**
- Province-specific command interface (extends ICommand)
- Status: ✅ Command hierarchy

**ChangeOwnerCommand.cs**
- Example: Change province ownership
- Data: provinceID, newOwnerID, executionTick
- Status: ✅ Implementation example

**CommandBuffer.cs**
- Ring buffer for command storage (rollback support)
- Status: ✅ Fixed-size buffer

**CommandSerializer.cs**
- Serialize commands for network transmission
- Status: ✅ Network-ready

**ProvinceCommands.cs**
- Collection of common province commands
- Status: ✅ Command implementations

---

## Queries/

**ProvinceQueries.cs**
- Read-only province operations: GetProvincesOwnedBy, GetNeighbors, GetRegion
- Status: ✅ Query layer

**CountryQueries.cs** [REFACTORED]
- High-performance country access with frame-coherent caching
- API: GetColor/Tag/HotData/ColdData, GetTotalDevelopment
- Status: ✅ Refactored (2025-10-05)

---

## Registries/

**IRegistry.cs**
- Interface: Register, Get, GetAll
- Status: ✅ Registry pattern

**Registry.cs**
- Generic registry implementation (Dictionary-based)
- Status: ✅ Generic implementation

**ProvinceRegistry.cs, CountryRegistry.cs**
- Province/country definitions
- Status: ✅ Metadata registries

**GameRegistries.cs**
- Container for all game registries
- Status: ✅ Registry aggregator

---

## Loaders/

**ScenarioLoader.cs**
- Load scenario data (provinces, countries, initial state)
- Orchestrates: BurstProvinceHistoryLoader, BurstCountryLoader
- Status: ✅ High-level loader

**BurstProvinceHistoryLoader.cs** [HOT_PATH]
- Parallel province history loading with Burst
- Pattern: NativeArray, IJob, deterministic parsing
- Status: ✅ Burst-optimized

**BurstCountryLoader.cs** [HOT_PATH]
- Parallel country data loading with Burst
- Status: ✅ Burst-optimized

**JobifiedCountryLoader.cs**
- Job-based country loading
- Status: ✅ Job system

**Json5Loader.cs**
- JSON5 parsing utilities
- Status: ✅ Data format support

**Json5ProvinceConverter.cs**
- Convert JSON5 province history to game structures
- Key: Applies dated historical events up to start date
- Status: ✅ Data conversion with historical events

**Json5CountryConverter.cs**
- Convert JSON5 country data to game structures
- Status: ✅ Data conversion

**DefinitionLoader.cs**
- Load ALL provinces from definition.csv (4941 total)
- Use: Registers provinces without JSON5 files
- Status: ✅ Handles uncolonized provinces

**ReligionLoader, CultureLoader, TradeGoodLoader, TerrainLoader, CountryTagLoader**
- Type-specific registry loaders
- Status: ✅ Type-specific loaders

**ManifestLoader.cs**
- Load scenario manifests
- Status: ✅ Manifest handling

**WaterProvinceLoader.cs**
- Load ocean/water province data
- Status: ✅ Water detection

---

## SaveLoad/

**SaveManager.cs** [MULTIPLAYER_CRITICAL] [STABLE]
- Orchestrates save/load across all systems
- Pattern: Hybrid snapshot + command log for verification
- API: QuickSave (F6), QuickLoad (F7), SaveGame, LoadGame
- Layer Separation: Uses callbacks for GAME layer finalization
- Status: ✅ Implemented (2025-10-19)

**SaveGameData.cs** [STABLE]
- Save file data structure (header + system data dictionary)
- Format: Binary with metadata, atomic writes (temp → rename)
- Status: ✅ Implemented (2025-10-19)

**SerializationHelper.cs** [MULTIPLAYER_CRITICAL] [STABLE]
- Binary serialization utilities for primitives, FixedPoint64, NativeArray
- Patterns: Raw memory copy for NativeArray, deterministic FixedPoint64 as long
- API: WriteFixedPoint64/NativeArray/String, ReadFixedPoint64/NativeArray/String
- Status: ✅ Implemented (2025-10-19)

**CommandLogger.cs**
- Ring buffer for command history (last 6000 commands ≈ 100 ticks)
- Purpose: Determinism verification via replay
- Status: ⏸️ Planned (command logging exists, replay verification not implemented)

---

## Linking/

**CrossReferenceBuilder.cs**
- Resolve string references to numeric IDs during loading
- Pattern: String "ENG" → CountryId lookup
- Status: ✅ Data linking

**DataValidator.cs**
- Validate data integrity after loading
- Status: ✅ Validation layer

**ReferenceResolver.cs**
- Resolve cross-references between data
- Status: ✅ Reference resolution

---

## Jobs/

**ProvinceProcessingJob.cs** [HOT_PATH]
- Burst-compiled province processing (IJobParallelFor)
- Status: ✅ Burst-optimized

**CountryProcessingJob.cs** [HOT_PATH]
- Burst-compiled country processing
- Status: ✅ Burst-optimized

**LoadBalancedScheduler.cs** [NEW]
- Victoria 3 pattern: Split expensive/affordable workloads
- API: SplitByThreshold, SplitByPercentile, CalculateMedianThreshold
- Status: ✅ Implemented (2025-10-15), 24.9% improvement at 10k provinces

**GameStateSnapshot.cs** [NEW] [ZERO_ALLOC]
- Double-buffer pattern for zero-blocking UI reads (Victoria 3 learned this the hard way)
- Pattern: Simulation writes buffer A, UI reads buffer B, O(1) pointer swap after tick
- Memory: 2x hot data (240KB at 10k provinces)
- Performance: Zero blocking, no memcpy overhead
- Status: ✅ Integrated (2025-10-15), used by ProvinceSystem

---

## Events/

**TimeEvents.cs** [STABLE]
- Time event structs: HourlyTickEvent, DailyTickEvent, WeeklyTickEvent, MonthlyTickEvent, YearlyTickEvent
- Pattern: All include tick counter for sync
- Status: ✅ Extracted (2025-10-05)

**SaveLoadEvents.cs** [STABLE]
- Save/load event structs: GameLoadedEvent, GameSavedEvent
- Purpose: UI refresh after load, achievement tracking after save
- Status: ✅ Implemented (2025-10-19)

---

## Initialization/

**IInitializationPhase.cs** [STABLE]
- Interface for phase-based initialization
- API: ExecuteAsync, Rollback, PhaseName, ProgressStart/End
- Status: ✅ Phase architecture (2025-10-05)

**InitializationContext.cs** [STABLE]
- Shared state container between phases
- Contains: GameState, Systems, Registries, Loaded Data, Progress callbacks
- Status: ✅ Phase architecture (2025-10-05)

**Phases/CoreSystemsInitializationPhase.cs** [STABLE]
- Phase 1: Initialize core systems (GameState, EventBus, TimeManager)
- Progress: 0-5%
- Status: ✅ Extracted (2025-10-05)

**Phases/StaticDataLoadingPhase.cs** [STABLE]
- Phase 2: Load static data (religions, cultures, trade goods, terrains)
- Progress: 5-15%, Emits: StaticDataReadyEvent
- Status: ✅ Extracted (2025-10-05)

**Phases/ProvinceDataLoadingPhase.cs** [STABLE]
- Phase 3: Load province data (definition.csv + JSON5 + Burst)
- Progress: 15-40%, Emits: ProvinceDataReadyEvent
- Status: ✅ Extracted (2025-10-05)

**Phases/CountryDataLoadingPhase.cs** [STABLE]
- Phase 4: Load country data (JSON5 + Burst)
- Progress: 40-60%, Emits: CountryDataReadyEvent
- Status: ✅ Extracted (2025-10-05)

**Phases/ReferenceLinkingPhase.cs** [STABLE]
- Phase 5: Link string references to numeric IDs
- Progress: 60-65%, Emits: ReferencesLinkedEvent
- Status: ✅ Extracted (2025-10-05)

**Phases/ScenarioLoadingPhase.cs** [STABLE]
- Phase 6: Load scenario data
- Progress: 65-75%
- Status: ✅ Extracted (2025-10-05)

**Phases/SystemsWarmupPhase.cs** [STABLE]
- Phase 7-8: Initialize systems, warm caches, validate
- Progress: 75-100%
- Status: ✅ Extracted (2025-10-05)

---

## Root Level

**GameState.cs** [MULTIPLAYER_CRITICAL]
- Root game state container
- Contains: ProvinceSystem, CountrySystem, TimeManager
- API: SaveState, LoadState, GetChecksum
- Status: ✅ State management

**GameSettings.cs**
- Game configuration and settings
- Status: ✅ Configuration

**EventBus.cs** [STABLE] [ZERO_ALLOC]
- Decoupled system communication
- Pattern: EventQueue<T> wrapper for zero-allocation polymorphism
- Performance: 0.85ms/10k events, 99.99% allocation reduction
- Status: ✅ Production-ready (2025-10-05)

**EngineInitializer.cs** [STABLE]
- ENGINE orchestrator for phase-based initialization
- Orchestrates: All 7 initialization phases
- Status: ✅ Refactored (2025-10-05)

---

## Quick Reference

**Change province state?** → Create command in Commands/ → Execute via CommandProcessor
**Query province data?** → Use ProvinceQueries or ProvinceSystem.GetProvinceState()
**Time-based event?** → Subscribe to TimeManager events in EventBus
**Load scenario?** → Use ScenarioLoader → Calls Burst loaders
**Save/load game?** → Use SaveManager → F6 quicksave, F7 quickload
**Deterministic random?** → Use DeterministicRandom with seed
**Fixed-point math?** → Use FixedPoint64 (32.32 format)
**Optional/rare data?** → Use SparseCollectionManager<TKey, TValue> (scales with usage, not possibility)

---

## Conventions

**Naming:**
- Systems: `<Name>System.cs`
- Commands: `<Action>Command.cs`
- Queries: `<Entity>Queries.cs`
- Loaders: `<Type>Loader.cs`

**Tags:**
- [MULTIPLAYER_CRITICAL]: FixedPoint64 only, deterministic, serializable, checksums
- [HOT_PATH]: Profile before changing, Burst compile, minimize allocations
- [STABLE]: Production-ready, tests passing

---

*Updated: 2025-10-19*
*Status: ✅ Multiplayer-ready with zero-blocking UI and sparse data infrastructure*
*Recent: Save/Load system implemented (hybrid snapshot + command log)*
