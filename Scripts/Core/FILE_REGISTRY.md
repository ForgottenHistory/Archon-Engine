# Core Layer File Registry
**Namespace:** `Core.*`
**Purpose:** Deterministic simulation - game state, logic, commands
**Rules:** FixedPoint64 only, deterministic ops, command pattern for state changes

---

## Systems/
- **Core.Systems.GameSystem** - Base class for all game systems with lifecycle hooks
- **Core.Systems.SystemRegistry** - Type-based system registry with dependency ordering
- **Core.Systems.TimeManager** - Tick-based time, game speed, 360-day calendar, emits tick events
- **Core.Systems.AdjacencySystem** - Province neighbor management with sparse collections
- **Core.Systems.PathfindingSystem** - A* pathfinding with optional GAME POLICY validator delegate for movement rules (ownership, military access, ZOC)
- **Core.Systems.ProvinceSystem** - Facade for province data (delegates to DataManager, StateLoader, HistoryDatabase)
- **Core.Systems.ProvinceSimulation** - Province-level simulation logic (development, economy)
- **Core.Systems.CountrySystem** - Facade for country data (delegates to DataManager, StateLoader)

### Systems/Province/
- **Core.Systems.Province.ProvinceDataManager** - NativeArray operations for province data
- **Core.Systems.Province.ProvinceStateLoader** - Load and apply province initial states
- **Core.Systems.Province.ProvinceEvents** - Events: SystemInitialized, OwnershipChanged, DevelopmentChanged

### Systems/Country/
- **Core.Systems.Country.CountryDataManager** - Hot/cold country data with SoA for colors
- **Core.Systems.Country.CountryStateLoader** - Load and initialize country data from files
- **Core.Systems.Country.CountryEvents** - Events: SystemInitialized, ColorChanged

---

## Data/
- **Core.Data.ProvinceState** - 8-byte ENGINE state: ownerID, controllerID, terrainType, gameDataSlot
- **Core.Data.ProvinceColdData** - Rarely-accessed data: Name, Color, Bounds, RecentHistory, Modifiers
- **Core.Data.ProvinceInitialState** - Initial province data from scenarios
- **Core.Data.ProvinceStateSerializer** - Network serialization for ProvinceState
- **Core.Data.ProvinceHistoryDatabase** - Bounded history storage (recent + compressed + stats)
- **Core.Data.CountryData** - Hot (8-byte) + cold data structures
- **Core.Data.CountryDataLoadResult** - Data transfer object for country loading results
- **Core.Data.Json5CountryData** - JSON5 deserialization model for country data files
- **Core.Data.Json5ProvinceData** - JSON5 deserialization model for province history files
- **Core.Data.FixedPoint64** - 32.32 fixed-point math (8 bytes), ALL simulation math uses this
- **Core.Data.DeterministicRandom** - xorshift128+ RNG for deterministic random

### Data/Ids/
- **Core.Data.Ids.ProvinceId** - Type-safe province ID wrapper (ushort)
- **Core.Data.Ids.CountryId** - Type-safe country ID wrapper (ushort)
- **Core.Data.Ids.ReligionId** - Type-safe religion ID wrapper (ushort)
- **Core.Data.Ids.CultureId** - Type-safe culture ID wrapper (ushort)
- **Core.Data.Ids.TradeGoodId** - Type-safe trade good ID wrapper (ushort)
- **Core.Data.Ids.TerrainId** - Type-safe terrain ID wrapper (ushort)
- **Core.Data.Ids.BuildingId** - Type-safe building ID wrapper (ushort)

### Data/Math/
- **Core.Data.Math.FixedPointMath** - FixedPoint32 (16.16 format), FixedPoint2 (2D vector)

### Data/SparseData/
- **Core.Data.SparseData.IDefinition** - Base interface for definitions (buildings, modifiers, trade goods)
- **Core.Data.SparseData.ISparseCollection** - Non-generic interface for sparse collection management
- **Core.Data.SparseData.SparseCollectionManager** - Generic sparse storage with NativeParallelMultiHashMap

---

## Commands/
- **Core.Commands.ICommand** - Base command interface: Execute, Validate, GetChecksum, Dispose
- **Core.Commands.IProvinceCommand** - Province-specific command interface
- **Core.Commands.ChangeOwnerCommand** - Change province ownership
- **Core.Commands.ProvinceCommands** - Collection of common province commands
- **Core.Commands.CommandProcessor** - Deterministic command validation and execution
- **Core.Commands.CommandBuffer** - Ring buffer for command storage (rollback support)
- **Core.Commands.CommandSerializer** - Serialize commands for network transmission
- **Core.Commands.CommandLogger** - Ring buffer for command history (last 6000 commands)

---

## Diplomacy/ [FACADE_PATTERN]
- **Core.Diplomacy.DiplomacySystem** - Facade for diplomatic relations (284 lines, delegates to managers)
- **Core.Diplomacy.DiplomacyRelationManager** - Opinion calculations and modifiers (stateless, 255 lines)
- **Core.Diplomacy.DiplomacyWarManager** - War state management (stateless, 226 lines)
- **Core.Diplomacy.DiplomacyTreatyManager** - Treaty management (stateless, 423 lines)
- **Core.Diplomacy.DiplomacyModifierProcessor** - Burst-compiled modifier decay (stateless, 126 lines)
- **Core.Diplomacy.DiplomacySaveLoadHandler** - Save/load serialization (stateless, 204 lines)
- **Core.Diplomacy.DiplomacyKeyHelper** - Key packing/unpacking utilities (46 lines)
- **Core.Diplomacy.RelationData** - Hot relation state: baseOpinion, atWar, treatyFlags
- **Core.Diplomacy.OpinionModifier** - Time-decaying modifier with FixedPoint64 value
- **Core.Diplomacy.ModifierWithKey** - Flat storage struct (modifier + relationshipKey for Burst)
- **Core.Diplomacy.DecayModifiersJob** - Burst IJobParallelFor for parallel decay processing
- **Core.Diplomacy.DiplomacyCommands** - Commands: DeclareWar, MakePeace
- **Core.Diplomacy.TreatyCommands** - Commands: ProposeTreaty, AcceptTreaty, BreakTreaty
- **Core.Diplomacy.DiplomacyEvents** - Events: WarDeclared, PeaceMade, OpinionChanged

---

## AI/
- **Core.AI.AISystem** - Manages AI for all countries (tier-based scheduling, distance calculation)
- **Core.AI.AIState** - 8-byte struct per country: countryID, tier, flags, activeGoalID, lastProcessedHour
- **Core.AI.AIGoal** - Base class for AI goals (Evaluate/Execute pattern, extensible)
- **Core.AI.AIScheduler** - Hourly goal evaluation (tier-based intervals, year-wrap handling)
- **Core.AI.AIGoalRegistry** - Plug-and-play goal registration system
- **Core.AI.AISchedulingConfig** - Configurable tier definitions (distance thresholds, intervals)
- **Core.AI.AIDistanceCalculator** - BFS distance calculation from player provinces

---

## Modifiers/
- **Core.Modifiers.ModifierSystem** - Generic modifier system for gameplay bonuses/penalties
- **Core.Modifiers.ModifierValue** - Modifier value with FixedPoint64 (Additive/Multiplicative)
- **Core.Modifiers.ModifierSet** - Container for multiple modifiers by type
- **Core.Modifiers.ModifierSource** - Source tracking for modifiers (building, trait, event)
- **Core.Modifiers.ActiveModifierList** - Active modifiers with expiration tracking
- **Core.Modifiers.ScopedModifierContainer** - Scoped modifier management (province/country level)

---

## Resources/
- **Core.Resources.ResourceSystem** - Multi-resource treasury management with FixedPoint64
- **Core.Resources.ResourceDefinition** - Resource definition from JSON5

---

## Units/
- **Core.Units.UnitSystem** - Military unit management with movement, combat, organization
- **Core.Units.UnitState** - 16-byte fixed state: unitID, ownerID, provinceID, typeID, organization, morale, strength, movementQueue
- **Core.Units.UnitColdData** - Cold unit data: Name, Experience, CombatHistory, Equipment
- **Core.Units.UnitCommands** - Commands: CreateUnit, MoveUnit, DisbandUnit
- **Core.Units.UnitMovementQueue** - Movement queue for pathfinding and multi-province movement
- **Core.Units.UnitEvents** - Events: UnitCreated, UnitMoved, UnitDisbanded, CombatResolved

---

## Queries/
- **Core.Queries.ProvinceQueries** - Read-only province operations
- **Core.Queries.CountryQueries** - High-performance country access with frame-coherent caching

---

## Registries/
- **Core.Registries.IRegistry** - Registry interface: Register, Get, GetAll
- **Core.Registries.Registry** - Generic registry implementation
- **Core.Registries.ProvinceRegistry** - Province definitions
- **Core.Registries.CountryRegistry** - Country definitions
- **Core.Registries.GameRegistries** - Container for all game registries

---

## Loaders/
- **Core.Loaders.ScenarioLoader** - Load scenario data (provinces, countries, initial state)
- **Core.Loaders.BurstProvinceHistoryLoader** - Parallel province history loading with Burst
- **Core.Loaders.BurstCountryLoader** - Parallel country data loading with Burst
- **Core.Loaders.Json5Loader** - JSON5 parsing utilities
- **Core.Loaders.Json5ProvinceConverter** - Convert JSON5 province history with dated events
- **Core.Loaders.Json5CountryConverter** - Convert JSON5 country data
- **Core.Loaders.DefinitionLoader** - Load ALL provinces from definition.csv
- **Core.Loaders.ReligionLoader** - Load religion definitions
- **Core.Loaders.CultureLoader** - Load culture definitions
- **Core.Loaders.TradeGoodLoader** - Load trade good definitions
- **Core.Loaders.TerrainLoader** - Load terrain definitions
- **Core.Loaders.CountryTagLoader** - Load country tag definitions
- **Core.Loaders.ManifestLoader** - Load scenario manifests
- **Core.Loaders.WaterProvinceLoader** - Load ocean/water province data

---

## SaveLoad/
- **Core.SaveLoad.SaveManager** - Orchestrates save/load across all systems (F6 quicksave, F7 quickload)
- **Core.SaveLoad.SaveGameData** - Save file data structure with header and system data dictionary
- **Core.SaveLoad.SerializationHelper** - Binary serialization for primitives, FixedPoint64, NativeArray
- **Core.SaveLoad.SystemSerializer** - Generic serialization interface for game systems
- **Core.SaveLoad.SaveFileSerializer** - Main save file serialization coordinator
- **Core.SaveLoad.CustomSystemSerializers** - Custom serializers for specific systems

---

## Linking/
- **Core.Linking.CrossReferenceBuilder** - Resolve string references to numeric IDs (e.g. "ENG" → CountryId)
- **Core.Linking.DataValidator** - Validate data integrity after loading
- **Core.Linking.ReferenceResolver** - Resolve cross-references between data

---

## Jobs/
- **Core.Jobs.ProvinceProcessingJob** - Burst-compiled province processing (IJobParallelFor)
- **Core.Jobs.CountryProcessingJob** - Burst-compiled country processing
- **Core.Jobs.LoadBalancedScheduler** - Victoria 3 pattern: Split expensive/affordable workloads
- **Core.Jobs.GameStateSnapshot** - Double-buffer pattern for zero-blocking UI reads

---

## Events/
- **Core.Events.TimeEvents** - Time event structs: HourlyTick, DailyTick, WeeklyTick, MonthlyTick, YearlyTick
- **Core.Events.SaveLoadEvents** - Events: GameLoaded, GameSaved

---

## Initialization/
- **Core.Initialization.IInitializationPhase** - Interface for phase-based initialization
- **Core.Initialization.InitializationContext** - Shared state container between phases

### Initialization/Phases/
- **Core.Initialization.Phases.CoreSystemsInitializationPhase** - Phase 1: Core systems (0-5%)
- **Core.Initialization.Phases.StaticDataLoadingPhase** - Phase 2: Static data (5-15%)
- **Core.Initialization.Phases.ProvinceDataLoadingPhase** - Phase 3: Province data (15-40%)
- **Core.Initialization.Phases.CountryDataLoadingPhase** - Phase 4: Country data (40-60%)
- **Core.Initialization.Phases.ReferenceLinkingPhase** - Phase 5: Link references (60-65%)
- **Core.Initialization.Phases.ScenarioLoadingPhase** - Phase 6: Scenario data (65-75%)
- **Core.Initialization.Phases.SystemsWarmupPhase** - Phase 7-8: System warmup (75-100%)

---

## Root/
- **Core.GameState** - Root game state container with ProvinceSystem, CountrySystem, TimeManager
- **Core.GameSettings** - Game configuration and settings
- **Core.EventBus** - Decoupled system communication with EventQueue<T> for zero-allocation
- **Core.EngineInitializer** - ENGINE orchestrator for phase-based initialization
- **Core.GameStateSnapshot** - Double-buffer pattern for zero-blocking UI reads

---

## Quick Reference
**Change province state?** → Create command in Commands/ → Execute via CommandProcessor
**Query province data?** → ProvinceQueries or ProvinceSystem.GetProvinceState()
**Time-based event?** → Subscribe to TimeManager events in EventBus
**Load scenario?** → ScenarioLoader → Calls Burst loaders
**Save/load game?** → SaveManager → F6 quicksave, F7 quickload
**Deterministic random?** → DeterministicRandom with seed
**Fixed-point math?** → FixedPoint64 (32.32 format)
**Optional/rare data?** → SparseCollectionManager<TKey, TValue>
**Add AI goal?** → Implement AIGoal in GAME layer → Register in GameSystemInitializer

---

*Updated: 2025-11-17*
*Fixed: All sections now use proper markdown formatting (dashes for line breaks on GitHub)*
