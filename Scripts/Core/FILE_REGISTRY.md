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
- **Lines:** 510

### **ProvinceSystem.cs** [HOT_PATH] [MULTIPLAYER_CRITICAL]
- **Purpose:** Single source of truth for province hot data (8-byte AoS)
- **Key Data:** `NativeArray<ProvinceState>` (10k × 8 bytes = 80KB)
- **Pattern:** Array of Structures (NOT Structure of Arrays)
- **API:** Get/Set owner, development, terrain; country province queries
- **Uses:** EventBus, ProvinceHistoryDatabase, idToIndex lookup
- **Status:** ✅ Architecture compliant (fixed 2025-09-30)
- **Lines:** 664

### **ProvinceSimulation.cs**
- **Purpose:** Province-level simulation logic (development, economy)
- **Pattern:** Operates on ProvinceState, emits events for changes
- **Uses:** ProvinceSystem, CommandProcessor
- **Status:** ✅ Simulation core

### **CountrySystem.cs**
- **Purpose:** Country hot data management (8-byte structs)
- **Key Data:** `NativeArray<CountryHotData>`, hot/cold separation
- **Pattern:** Structure of Arrays for cache efficiency
- **Uses:** CountryRegistry, EventBus
- **Status:** ✅ Architecture compliant

### **CommandProcessor.cs** [MULTIPLAYER_CRITICAL]
- **Purpose:** Deterministic command validation, tick-based execution
- **Pattern:** Command pattern, sorts by type then player ID
- **API:** SubmitCommand(), ProcessTick()
- **Uses:** ProvinceSimulation, checksums for validation
- **Status:** ✅ Ready for multiplayer
- **Lines:** 327

---

## Core/Data/ - Data Structures

### **ProvinceState.cs** [MULTIPLAYER_CRITICAL] [STABLE]
- **Purpose:** 8-byte province state struct (dual-layer architecture foundation)
- **Layout:** ownerID(2) + controllerID(2) + development(1) + terrain(1) + fortLevel(1) + flags(1)
- **Validation:** Compile-time size check enforces 8 bytes
- **API:** CreateDefault(), CreateOwned(), ToBytes(), FromBytes()
- **Status:** ✅ Architecture compliant, DO NOT change size
- **Lines:** 211

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
- **Types:** Also includes FixedPoint32 (16.16 format)
- **API:** NextUInt(), NextInt(), NextFixed(), NextBool(), Shuffle()
- **Pattern:** Seed-based, state serializable
- **Status:** ✅ Deterministic
- **Lines:** 400

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

### **CountryQueries.cs**
- **Purpose:** Query operations on country data
- **API:** GetCountryProvinces(), GetCapital(), GetRevenue()
- **Status:** ✅ Query layer

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

## Core/ - Root Level

### **GameState.cs** [MULTIPLAYER_CRITICAL]
- **Purpose:** Root game state container
- **Contains:** ProvinceSystem, CountrySystem, TimeManager references
- **API:** SaveState(), LoadState(), GetChecksum()
- **Status:** ✅ State management

### **GameSettings.cs**
- **Purpose:** Game configuration and settings
- **Status:** ✅ Configuration

### **EventBus.cs** [STABLE]
- **Purpose:** Decoupled system communication via events
- **Pattern:** Type-safe publish/subscribe, frame-coherent processing
- **Performance:** Event pooling for zero allocations
- **API:** Subscribe(), Emit(), ProcessEvents()
- **Status:** ✅ Zero-allocation event system
- **Lines:** 304

### **GameInitializer.cs**
- **Purpose:** Initialize all game systems in correct order
- **Orchestrates:** TimeManager, ProvinceSystem, EventBus initialization
- **Status:** ✅ Initialization flow

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

*Last Updated: 2025-10-05*
*Total Files: 57 scripts*
*Status: Multiplayer-ready, deterministic simulation layer*
