# Core Layer File Registry
**Namespace:** `Core.*`
**Purpose:** Deterministic simulation layer - game state, logic, and commands
**Architecture Rules:**
- ✅ Use FixedPoint64 for all math (NO float/double)
- ✅ Deterministic operations only (network-safe)
- ✅ No Unity API dependencies in hot paths
- ✅ All state changes through command pattern

**Status:** ✅ Multiplayer-ready (deterministic, fixed-point math)

---

## Core/Systems/ - Active Game Systems

### **TimeManager.cs** [MULTIPLAYER_CRITICAL] [STABLE]
- **Purpose:** Tick-based time progression, game speed control, calendar system
- **Key Data:** `FixedPoint64 accumulator`, `ulong currentTick`, 360-day year
- **Emits:** HourlyTickEvent, DailyTickEvent, MonthlyTickEvent, YearlyTickEvent
- **Uses:** EventBus, FixedPoint64
- **Status:** ✅ Deterministic (rewritten 2025-09-30)
- **Lines:** 460 (refactored 2025-10-05, ✅ under 500 line limit)

### **ProvinceSystem.cs** [HOT_PATH] [MULTIPLAYER_CRITICAL] [STABLE]
- **Purpose:** Facade for province system - orchestrates data, loading, and history
- **Pattern:** Facade pattern - delegates to ProvinceDataManager, ProvinceStateLoader, ProvinceHistoryDatabase
- **API:** Get/Set owner, development, terrain; country province queries, loading, history
- **Components:** Owns NativeArrays, passes references to managers
- **Status:** ✅ Refactored (2025-10-05) - extracted to 3 components
- **Lines:** 214 (reduced from 576, -362 lines, ✅ under 500 line limit)

### **Province/ProvinceDataManager.cs** [HOT_PATH] [MULTIPLAYER_CRITICAL]
- **Purpose:** Manages province hot data NativeArray operations
- **Key Data:** References to `NativeArray<ProvinceState>`, idToIndex, activeProvinceIds
- **API:** AddProvince, Get/Set owner/development/terrain, GetCountryProvinces
- **Status:** ✅ Extracted from ProvinceSystem (2025-10-05)
- **Lines:** 235

### **Province/ProvinceStateLoader.cs** [HOT_PATH]
- **Purpose:** Handles loading and applying province initial states
- **Uses:** BurstProvinceHistoryLoader, ProvinceDataManager, ProvinceHistoryDatabase
- **API:** LoadProvinceInitialStates, LoadProvinceInitialStatesForLinking, ApplyResolvedInitialStates
- **Status:** ✅ Extracted from ProvinceSystem (2025-10-05)
- **Lines:** 146

### **Province/ProvinceEvents.cs** [STABLE]
- **Purpose:** Province-related event definitions for EventBus
- **Events:** ProvinceSystemInitializedEvent, ProvinceOwnershipChangedEvent, ProvinceDevelopmentChangedEvent, ProvinceInitialStatesLoadedEvent
- **Status:** ✅ Extracted from ProvinceSystem (2025-10-05)
- **Lines:** 37

### **ProvinceSimulation.cs**
- **Purpose:** Province-level simulation logic (development, economy)
- **Pattern:** Operates on ProvinceState, emits events for changes
- **Uses:** ProvinceSystem, CommandProcessor
- **Status:** ✅ Simulation core

### **CountrySystem.cs** [REFACTORED]
- **Purpose:** Country system orchestrator (facade pattern)
- **Pattern:** Delegates to CountryDataManager and CountryStateLoader
- **API:** GetCountryColor(), GetCountryTag(), GetCountryIdFromTag(), InitializeFromCountryData()
- **Uses:** EventBus, NativeArray for hot data
- **Status:** ✅ Refactored (2025-10-05): 564 → 187 lines (-377 lines, -66.8%)
- **Lines:** 187

#### Country/ - Country System Components

##### **Country/CountryEvents.cs** [STABLE]
- **Purpose:** Country-related event definitions
- **Events:** CountrySystemInitializedEvent, CountryColorChangedEvent
- **Status:** ✅ Extracted from CountrySystem (2025-10-05)
- **Lines:** 23

##### **Country/CountryDataManager.cs** [STABLE]
- **Purpose:** Manages country hot/cold data operations (NativeArray + dictionary)
- **Key Data:** NativeArray<CountryHotData>, NativeArray<Color32>, NativeHashMap for ID mapping
- **Pattern:** Structure of Arrays (SoA) for country colors, tag hash mapping
- **API:** AddCountry(), GetCountryColor(), SetCountryColor(), GetCountryColdData()
- **Status:** ✅ Extracted from CountrySystem (2025-10-05)
- **Lines:** 308

##### **Country/CountryStateLoader.cs** [STABLE]
- **Purpose:** Handles loading and initializing country data from files
- **Pattern:** Loads from CountryDataLoadResult, handles duplicates, creates default unowned country
- **API:** InitializeFromCountryData()
- **Status:** ✅ Extracted from CountrySystem (2025-10-05)
- **Lines:** 117

### **CommandProcessor.cs** [MULTIPLAYER_CRITICAL]
- **Purpose:** Deterministic command validation, tick-based execution
- **Pattern:** Command pattern, sorts by type then player ID
- **API:** SubmitCommand(), ProcessTick()
- **Uses:** ProvinceSimulation, checksums for validation
- **Status:** ✅ Ready for multiplayer
- **Lines:** 327

---

## Core/Data/ - Data Structures

### **ProvinceState.cs** [MULTIPLAYER_CRITICAL] [REFACTORED]
- **Purpose:** 8-byte ENGINE province state struct (generic primitives only)
- **Layout:** ownerID(2) + controllerID(2) + terrainType(2) + gameDataSlot(2)
- **Architecture:** Engine provides MECHANISM, game layer provides POLICY
- **Fields:**
  - `ownerID` (ushort) - Who owns this province (generic)
  - `controllerID` (ushort) - Who controls militarily (generic)
  - `terrainType` (ushort) - Terrain type ID (generic, expanded from byte)
  - `gameDataSlot` (ushort) - Index into game-specific data array
- **REMOVED** (migrated to Game layer - HegemonProvinceData):
  - ❌ `development` → HegemonProvinceSystem.GetDevelopment()
  - ❌ `fortLevel` → HegemonProvinceSystem.GetFortLevel()
  - ❌ `flags` → Moved to separate system if needed
- **Validation:** Compile-time size check enforces 8 bytes
- **API:** CreateDefault(), CreateOwned(), CreateOcean(), ToBytes(), FromBytes(), IsOwned, IsOccupied, IsOcean
- **Status:** ✅ Refactored for engine-game separation (2025-10-09)
- **Lines:** 209 (reduced from 211, removed game-specific fields)

### **ProvinceColdData.cs** [STABLE]
- **Purpose:** Rarely-accessed province data (presentation, history, metadata)
- **Key Data:** Name, Color, Bounds, RecentHistory (CircularBuffer<100>)
- **Modifiers:** `Dictionary<string, FixedPoint64>` for gameplay bonuses
- **Cache:** Frame-coherent caching for expensive calculations
- **Uses:** FixedPoint64 (fixed 2025-09-30)
- **Status:** ✅ Deterministic
- **Lines:** 253

### **ProvinceHistoryDatabase.cs**
- **Purpose:** Bounded history storage for provinces (prevents memory growth)
- **Pattern:** Recent (100 events) + compressed medium + statistical ancient
- **API:** AddEvent(), GetRecentEvents(), GetHistorySummary()
- **Status:** ✅ Memory-bounded

### **CountryData.cs**
- **Purpose:** Country hot (8-byte) + cold data structures
- **Types:** CountryHotData (struct), CountryColdData (class), CountryDataCollection
- **Pattern:** Hot/cold separation, NativeArray for hot data
- **Status:** ✅ Architecture compliant
- **Lines:** 399

### **FixedPoint64.cs** [MULTIPLAYER_CRITICAL] [STABLE]
- **Purpose:** 32.32 fixed-point math for deterministic calculations
- **Format:** 32 integer bits, 32 fractional bits, 8 bytes total
- **API:** Operators (+,-,*,/,%), FromInt(), FromFraction(), ToBytes()
- **Usage:** ALL simulation math must use this (NO float/double!)
- **Status:** ✅ Network-safe, created 2025-09-30
- **Lines:** 273

### **DeterministicRandom.cs** [MULTIPLAYER_CRITICAL]
- **Purpose:** xorshift128+ RNG for deterministic random generation
- **API:** NextUInt(), NextInt(), NextFixed(), NextBool(), Shuffle()
- **Pattern:** Seed-based, state serializable
- **Status:** ✅ Deterministic
- **Lines:** 329 (refactored 2025-10-05)

### **Math/FixedPointMath.cs** [MULTIPLAYER_CRITICAL]
- **Purpose:** Fixed-point math types for deterministic calculations
- **Types:** FixedPoint32 (16.16 format), FixedPoint2 (2D vector)
- **API:** Operators (+,-,*,<,>,<=,>=), FromInt(), FromRaw(), ToFloat()
- **Pattern:** Immutable value types with operator overloads
- **Status:** ✅ Deterministic
- **Lines:** 75 (extracted from DeterministicRandom 2025-10-05)

### **ProvinceInitialState.cs**
- **Purpose:** Initial province data from scenario files
- **API:** ToProvinceState() converts to hot ProvinceState
- **Uses:** Loading system, BurstProvinceHistoryLoader
- **Status:** ✅ Data transfer object

### **ProvinceStateSerializer.cs**
- **Purpose:** Network serialization for ProvinceState
- **API:** Serialize(), Deserialize() for 8-byte structs
- **Status:** ✅ Network-ready

---

## Core/Data/Ids/ - Strongly-Typed IDs

### **ProvinceId.cs, CountryId.cs, ReligionId.cs, CultureId.cs, TradeGoodId.cs, TerrainId.cs, BuildingId.cs**
- **Purpose:** Type-safe ID wrappers (ushort internally)
- **Pattern:** Prevents ID confusion (can't pass CountryId where ProvinceId expected)
- **Status:** ✅ Type safety

---

## Core/Commands/ - Command Pattern

### **ICommand.cs** [MULTIPLAYER_CRITICAL]
- **Purpose:** Base command interface for all state changes
- **API:** Execute(), Validate(), GetChecksum(), Dispose()
- **Pattern:** Used by CommandProcessor for deterministic execution
- **Status:** ✅ Network-ready interface

### **IProvinceCommand.cs**
- **Purpose:** Province-specific command interface
- **Extends:** ICommand with province-specific fields
- **Status:** ✅ Command hierarchy

### **ChangeOwnerCommand.cs**
- **Purpose:** Change province ownership command
- **Data:** provinceID, newOwnerID, executionTick
- **Status:** ✅ Example implementation

### **CommandBuffer.cs**
- **Purpose:** Ring buffer for command storage
- **Use Case:** Rollback support for client prediction
- **Status:** ✅ Fixed-size buffer

### **CommandSerializer.cs**
- **Purpose:** Serialize commands for network transmission
- **API:** Serialize(), Deserialize() for ICommand types
- **Status:** ✅ Network-ready

### **ProvinceCommands.cs**
- **Purpose:** Collection of common province commands
- **Status:** ✅ Command implementations

---

## Core/Queries/ - Read-Only Data Access

### **ProvinceQueries.cs**
- **Purpose:** Query operations on province data (no state changes)
- **API:** GetProvincesOwnedBy(), GetNeighbors(), GetRegion()
- **Pattern:** Separates reads from writes
- **Status:** ✅ Query layer

### **CountryQueries.cs** [REFACTORED]
- **Purpose:** High-performance country data access layer with caching
- **Pattern:** Thin query wrapper with frame-coherent caching for expensive calculations
- **API:** GetColor(), GetTag(), GetHotData(), GetColdData(), GetTotalDevelopment()
- **Cache:** Dictionary-based cache with 1-second lifetime for computed queries
- **Status:** ✅ Refactored (2025-10-05): 507 → 461 lines (-46 lines, removed redundant flag methods)
- **Lines:** 461

---

## Core/Registries/ - Static Data Lookups

### **IRegistry.cs**
- **Purpose:** Interface for static data registries
- **API:** Register(), Get(), GetAll()
- **Status:** ✅ Registry pattern

### **Registry.cs**
- **Purpose:** Generic registry implementation
- **Pattern:** Dictionary-based fast lookup
- **Status:** ✅ Generic implementation

### **ProvinceRegistry.cs**
- **Purpose:** Registry for province definitions
- **Status:** ✅ Province metadata

### **CountryRegistry.cs**
- **Purpose:** Registry for country tags and definitions
- **Status:** ✅ Country metadata

### **GameRegistries.cs**
- **Purpose:** Container for all game registries
- **API:** Single access point for all static data
- **Status:** ✅ Registry aggregator

---

## Core/Loaders/ - Data Loading Systems

### **ScenarioLoader.cs**
- **Purpose:** Load scenario data (provinces, countries, initial state)
- **Orchestrates:** BurstProvinceHistoryLoader, BurstCountryLoader
- **Status:** ✅ High-level loader

### **BurstProvinceHistoryLoader.cs** [HOT_PATH]
- **Purpose:** Parallel province history loading with Burst
- **Pattern:** NativeArray, IJob, deterministic parsing
- **Performance:** Burst-compiled for fast loading
- **Status:** ✅ Burst-optimized

### **BurstCountryLoader.cs** [HOT_PATH]
- **Purpose:** Parallel country data loading with Burst
- **Status:** ✅ Burst-optimized

### **JobifiedCountryLoader.cs**
- **Purpose:** Job-based country loading
- **Status:** ✅ Job system

### **Json5Loader.cs**
- **Purpose:** JSON5 parsing utilities
- **Status:** ✅ Data format support

### **Json5ProvinceConverter.cs**
- **Purpose:** Convert JSON5 province history to game data structures
- **Key Feature:** Applies dated historical events up to start date (1444.11.11)
- **Example:** Province with `owner: "TIM"` + `"1442.1.1": {owner: "QOM"}` → QOM at 1444
- **Status:** ✅ Data conversion with historical event processing

### **Json5CountryConverter.cs**
- **Purpose:** Convert JSON5 country data to game data structures
- **Status:** ✅ Data conversion

### **DefinitionLoader.cs**
- **Purpose:** Load ALL provinces from definition.csv (4941 total)
- **API:** LoadDefinitions(), RegisterDefinitions()
- **Use Case:** Registers provinces without JSON5 files (uncolonized, water)
- **Status:** ✅ Handles EU4 uncolonized provinces

### **ReligionLoader.cs, CultureLoader.cs, TradeGoodLoader.cs, TerrainLoader.cs, CountryTagLoader.cs**
- **Purpose:** Load specific registry data types
- **Status:** ✅ Type-specific loaders

### **ManifestLoader.cs**
- **Purpose:** Load scenario manifests
- **Status:** ✅ Manifest handling

### **WaterProvinceLoader.cs**
- **Purpose:** Load ocean/water province data
- **Status:** ✅ Water detection

---

## Core/Linking/ - Reference Resolution

### **CrossReferenceBuilder.cs**
- **Purpose:** Resolve string references to numeric IDs during loading
- **Pattern:** String "ENG" → CountryId lookup
- **API:** BuildLinks(), ResolveReferences()
- **Status:** ✅ Data linking architecture

### **DataValidator.cs**
- **Purpose:** Validate data integrity after loading
- **API:** Validate(), CheckConsistency()
- **Status:** ✅ Validation layer

### **ReferenceResolver.cs**
- **Purpose:** Resolve cross-references between data
- **Status:** ✅ Reference resolution

---

## Core/Jobs/ - Unity Job System

### **ProvinceProcessingJob.cs** [HOT_PATH]
- **Purpose:** Burst-compiled province processing job
- **Pattern:** IJobParallelFor for province updates
- **Status:** ✅ Burst-optimized

### **CountryProcessingJob.cs** [HOT_PATH]
- **Purpose:** Burst-compiled country processing job
- **Status:** ✅ Burst-optimized

---

## Core/Events/ - Event Definitions

### **TimeEvents.cs** [STABLE]
- **Purpose:** Time-related event structs for EventBus
- **Events:** HourlyTickEvent, DailyTickEvent, WeeklyTickEvent, MonthlyTickEvent, YearlyTickEvent, TimeStateChangedEvent, TimeChangedEvent
- **Pattern:** All include tick counter for command synchronization
- **Status:** ✅ Extracted from TimeManager (2025-10-05)
- **Lines:** 53

---

## Core/Initialization/ - Phase-Based Engine Initialization

### **IInitializationPhase.cs** [STABLE]
- **Purpose:** Interface for phase-based initialization pattern
- **Pattern:** Each phase handles one aspect of engine startup
- **API:** ExecuteAsync(), Rollback(), PhaseName, ProgressStart/End
- **Status:** ✅ New phase-based architecture (2025-10-05)
- **Lines:** 38

### **InitializationContext.cs** [STABLE]
- **Purpose:** Shared state container passed between initialization phases
- **Contains:** GameState, Systems, Registries, Loaded Data, Progress callbacks
- **Pattern:** Replaces scattered private fields in EngineInitializer
- **Status:** ✅ New phase-based architecture (2025-10-05)
- **Lines:** 58

### **Phases/CoreSystemsInitializationPhase.cs** [STABLE]
- **Purpose:** Phase 1 - Initialize core engine systems (GameState, EventBus, TimeManager)
- **Replaces:** EngineInitializer.InitializeCoreSystemsPhase() method
- **Progress:** 0-5%
- **Status:** ✅ Extracted from EngineInitializer (2025-10-05)
- **Lines:** 75

### **Phases/StaticDataLoadingPhase.cs** [STABLE]
- **Purpose:** Phase 2 - Load static game data (religions, cultures, trade goods, terrains)
- **Replaces:** EngineInitializer.LoadStaticDataPhase() method
- **Progress:** 5-15%
- **Emits:** StaticDataReadyEvent
- **Status:** ✅ Extracted from EngineInitializer (2025-10-05)
- **Lines:** 98

### **Phases/ProvinceDataLoadingPhase.cs** [STABLE]
- **Purpose:** Phase 3 - Load province data using definition.csv + JSON5 + Burst
- **Replaces:** EngineInitializer.LoadProvinceDataPhase() method
- **Progress:** 15-40%
- **Emits:** ProvinceDataReadyEvent
- **Status:** ✅ Extracted from EngineInitializer (2025-10-05)
- **Lines:** 97

### **Phases/CountryDataLoadingPhase.cs** [STABLE]
- **Purpose:** Phase 4 - Load country data using JSON5 + Burst
- **Replaces:** EngineInitializer.LoadCountryDataPhase() method
- **Progress:** 40-60%
- **Emits:** CountryDataReadyEvent
- **Status:** ✅ Extracted from EngineInitializer (2025-10-05)
- **Lines:** 91

### **Phases/ReferenceLinkingPhase.cs** [STABLE]
- **Purpose:** Phase 5 - Link all string references to numeric IDs
- **Replaces:** EngineInitializer.LinkingReferencesPhase() method
- **Progress:** 60-65%
- **Emits:** ReferencesLinkedEvent
- **Status:** ✅ Extracted from EngineInitializer (2025-10-05)
- **Lines:** 214

### **Phases/ScenarioLoadingPhase.cs** [STABLE]
- **Purpose:** Phase 6 - Load scenario data
- **Replaces:** EngineInitializer.LoadScenarioDataPhase() method
- **Progress:** 65-75%
- **Status:** ✅ Extracted from EngineInitializer (2025-10-05)
- **Lines:** 92

### **Phases/SystemsWarmupPhase.cs** [STABLE]
- **Purpose:** Phase 7-8 - Initialize systems, warm caches, validate
- **Replaces:** EngineInitializer.InitializeSystemsPhase() + WarmCachesPhase() methods
- **Progress:** 75-100%
- **Status:** ✅ Extracted from EngineInitializer (2025-10-05)
- **Lines:** 95

---

## Core/ - Root Level

### **GameState.cs** [MULTIPLAYER_CRITICAL]
- **Purpose:** Root game state container
- **Contains:** ProvinceSystem, CountrySystem, TimeManager references
- **API:** SaveState(), LoadState(), GetChecksum()
- **Status:** ✅ State management

### **GameSettings.cs**
- **Purpose:** Game configuration and settings
- **Status:** ✅ Configuration

### **EventBus.cs** [STABLE] [ZERO_ALLOC]
- **Purpose:** Decoupled system communication via events
- **Pattern:** EventQueue<T> wrapper pattern for zero-allocation polymorphism
- **Performance:** 0.85ms for 10k events, 99.99% allocation reduction (stress tested 2025-10-05)
- **API:** Subscribe(), Emit(), ProcessEvents()
- **Architecture:** Virtual methods avoid boxing, frame-coherent batching
- **Status:** ✅ Production-ready zero-allocation event system
- **Lines:** 304

### **EngineInitializer.cs** [STABLE]
- **Purpose:** ENGINE LAYER - Orchestrate phase-based initialization sequence
- **Pattern:** Phase-based architecture (all 7 phases extracted)
- **Orchestrates:** CoreSystemsInitializationPhase, StaticDataLoadingPhase, ProvinceDataLoadingPhase, CountryDataLoadingPhase, ReferenceLinkingPhase, ScenarioLoadingPhase, SystemsWarmupPhase
- **Status:** ✅ Refactoring complete (2025-10-05)
- **Lines:** 340 (reduced from 904, -62.4%, ✅ under 500 line limit)

---

## Key Patterns & Conventions

### Naming Conventions
- **Systems:** `<Name>System.cs` (e.g., ProvinceSystem, CountrySystem)
- **Commands:** `<Action>Command.cs` (e.g., ChangeOwnerCommand)
- **Queries:** `<Entity>Queries.cs` (e.g., ProvinceQueries)
- **Loaders:** `<Type>Loader.cs` (e.g., ScenarioLoader)

### Data Flow
```
Loader → Registry → System → Command → State Change → Event → GPU Update
```

### Multiplayer Critical Files
Files marked `[MULTIPLAYER_CRITICAL]` MUST:
- Use FixedPoint64 for all math (NO float/double)
- Be deterministic across all platforms
- Support serialization for network sync
- Use checksums for validation

### Hot Path Files
Files marked `[HOT_PATH]` are performance-critical:
- Profile before changing
- Use Burst compilation where possible
- Minimize allocations
- Optimize cache usage

---

## Quick Reference by Use Case

**Need to change province state?**
→ Create command in `Commands/` → Execute via `CommandProcessor`

**Need to query province data?**
→ Use `ProvinceQueries` or `ProvinceSystem.GetProvinceState()`

**Need to add time-based event?**
→ Subscribe to `TimeManager` events in `EventBus`

**Need to load scenario data?**
→ Use `ScenarioLoader` → Calls Burst loaders

**Need deterministic random?**
→ Use `DeterministicRandom` with seed

**Need fixed-point math?**
→ Use `FixedPoint64` (32.32 format)

---

*Last Updated: 2025-10-09*
*Total Files: 69 scripts* (+7 phases, +3 Province components, +3 Country components from refactoring)
*Status: Multiplayer-ready, deterministic simulation layer, all files under 500 lines* ✅
*Recent Changes:*
- **2025-10-09:** ProvinceState.cs refactored for engine-game separation
  - Removed game-specific fields (development, fortLevel, flags)
  - Added gameDataSlot for game layer data indexing
  - Expanded terrainType from byte to ushort (65k terrain types)
  - True engine-game separation achieved
  - See: Assets/Game/Compatibility/ProvinceStateExtensions.cs for migration bridge
  - See: Assets/Archon-Engine/Docs/Log/2025-10/2025-10-09/ for refactoring plan
