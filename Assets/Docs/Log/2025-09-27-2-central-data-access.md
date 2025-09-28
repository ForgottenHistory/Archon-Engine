# Central Data Access System - Implementation Guide

## Overview & Context

This guide documents the implementation of a central GameState system for Dominion, providing unified access to province and country data. The system follows the dual-layer architecture principles and integrates with our existing Burst-optimized loading systems.

## Background & Problem Statement

### What We Have (As of 2025-09-27)
- **✅ High-performance data loading**: JobifiedBMPLoader, ProvinceMapProcessor, JobifiedDefinitionLoader, JobifiedCountryLoader
- **✅ Dual-layer architecture**: Map.Simulation (8-byte simulation state) + Map.Province (visual/presentation data)
- **✅ Existing data structures**: ProvinceState, CountryHotData/ColdData, ProvinceDataManager

### What We Need
- **❌ Central coordination**: No unified way to access province/country data
- **❌ Single source of truth**: Data scattered across multiple systems
- **❌ Clean API**: Each system has different interfaces
- **❌ Performance guarantees**: No caching, pooling, or optimization strategy

### Architecture Goals
Based on `Assets/Docs/Engine/data-flow-architecture.md`:
1. **Hub-and-spoke architecture** - Central GameState coordinates all systems
2. **Single source of truth** - Each piece of data has one authoritative owner
3. **Event-driven communication** - Systems communicate through events, not direct calls
4. **Performance targets** - Zero allocations during gameplay, all queries <0.01ms
5. **Command pattern** - All changes go through validated commands

## Implementation Strategy

### Phase 1: Core Infrastructure

#### 1.1 Central GameState (`Assets/Scripts/Core/GameState.cs`)
**Purpose**: Central hub that provides unified access to all game data without owning it.

**Key Requirements**:
- References to all systems (ProvinceSystem, CountrySystem, etc.)
- Unified query interface for common operations
- Command execution and validation
- NO business logic (pure coordination)

**Integration Points**:
- Existing Map.Simulation.ProvinceSimulation
- Existing Map.Province.ProvinceDataManager
- New systems we'll create

#### 1.2 Event Bus (`Assets/Scripts/Core/EventBus.cs`)
**Purpose**: Decoupled communication between systems.

**Key Requirements**:
- High-performance event emission (pooled events)
- Type-safe event subscription
- Frame-coherent event processing
- Support for multiplayer event replication

#### 1.3 Command System (`Assets/Scripts/Core/Commands/`)
**Purpose**: All game state changes go through validated commands.

**Key Requirements**:
- Validation before execution
- Event emission on successful execution
- Undo support for replay systems
- Network serialization for multiplayer

### Phase 2: Data Owner Systems

#### 2.1 ProvinceSystem (`Assets/Scripts/Core/Systems/ProvinceSystem.cs`)
**Purpose**: Single source of truth for all province data.

**Responsibilities**:
- Owns the array of 8-byte ProvinceState structs (simulation layer)
- Manages province ownership, development, terrain, flags
- Emits events when province data changes
- Provides high-performance queries

**Integration with Existing**:
- Takes over from Map.Simulation.ProvinceSimulation
- Coordinates with Map.Province.ProvinceDataManager for visual data
- Uses data from ProvinceMapProcessor during initialization

**Performance Notes**:
- Structure of arrays design for cache efficiency
- Pre-allocated arrays (no runtime allocation)
- Burst-compiled query methods

#### 2.2 CountrySystem (`Assets/Scripts/Core/Systems/CountrySystem.cs`)
**Purpose**: Single source of truth for all country/nation data.

**Responsibilities**:
- Owns CountryHotData array (8-byte hot simulation data)
- Lazy-loads CountryColdData on demand
- Manages country relationships, tags, colors
- Provides country-to-province mappings

**Integration with Existing**:
- Uses data from JobifiedCountryLoader during initialization
- Maintains existing GameData.Core.CountryData structures

### Phase 3: Query & Access Layer

#### 3.1 Province Queries (`Assets/Scripts/Core/Queries/ProvinceQueries.cs`)
**Purpose**: High-performance province data access.

**Key Queries**:
- Basic: GetOwner(), GetDevelopment(), GetTerrain()
- Computed: GetCountryProvinces(), GetNeighbors(), GetRegionalPower()
- Cached: GetProvincesInRange(), GetBorderProvinces()

#### 3.2 Country Queries (`Assets/Scripts/Core/Queries/CountryQueries.cs`)
**Purpose**: High-performance country data access.

**Key Queries**:
- Basic: GetColor(), GetTag(), GetCulture()
- Computed: GetTotalDevelopment(), GetProvinceCount(), GetBorders()
- Cached: GetTotalStrength(), GetDiplomaticPower()

### Phase 4: Optimization Layer

#### 4.1 Caching Strategy
**Frame-coherent caching**: Expensive calculations cached per frame and invalidated on changes.

**Cache Categories**:
- **UI queries**: Province tooltips, country panels (reset every frame)
- **AI queries**: Strategic calculations (invalidated on relevant events)
- **Rendering queries**: Visual data for GPU (invalidated on visual changes)

#### 4.2 Memory Management
**Pooling strategy**: Pre-allocate all objects at startup, no runtime allocation.

**Structure of Arrays**: Keep related data in separate arrays for better cache performance.

### Phase 5: Loading Pipeline

#### 5.1 GameInitializer (`Assets/Scripts/Core/Initialization/GameInitializer.cs`)
**Purpose**: Orchestrates the complete game startup sequence.

**Loading Phases**:
1. **Core Systems** (5%): Create managers, event bus
2. **Static Data** (10-30%): Load Paradox script files in parallel
3. **Map Data** (30-60%): Use our ProvinceMapProcessor + JobifiedDefinitionLoader
4. **Country Data** (60-80%): Use JobifiedCountryLoader, build relationships
5. **AI Initialization** (80-90%): Initialize AI for all countries
6. **Final Setup** (90-100%): Warm caches, prepare UI

**Integration Points**:
- Uses ProvinceMapProcessor (our new system)
- Uses JobifiedCountryLoader (existing)
- Initializes ProvinceSystem and CountrySystem
- Coordinates with existing Map.Simulation and Map.Province

## File Structure & Organization

```
Assets/Scripts/Core/
├── GameState.cs                 # Central coordinator hub
├── EventBus.cs                  # Event system infrastructure
├── Commands/
│   ├── ICommand.cs              # Command interface
│   ├── ProvinceCommands.cs      # Province state changes
│   ├── CountryCommands.cs       # Country state changes
│   └── DiplomaticCommands.cs    # Future: diplomatic actions
├── Systems/
│   ├── ProvinceSystem.cs        # Province data owner
│   ├── CountrySystem.cs         # Country data owner
│   ├── TimeManager.cs           # Time progression control
│   └── DiplomaticSystem.cs      # Future: diplomacy
├── Queries/
│   ├── ProvinceQueries.cs       # Province data access
│   ├── CountryQueries.cs        # Country data access
│   └── CrossSystemQueries.cs    # Multi-system queries
├── Cache/
│   ├── QueryCache.cs            # Caching infrastructure
│   └── CacheInvalidator.cs      # Cache invalidation logic
└── Initialization/
    ├── GameInitializer.cs       # Startup coordinator
    ├── LoadingProgress.cs       # Loading screen integration
    └── SystemDependencies.cs    # Dependency management
```

## Integration with Existing Systems

### Map.Simulation Integration
**Current**: `ProvinceSimulation` manages ProvinceState array
**Future**: `ProvinceSystem` takes over this responsibility
**Migration**: Move ProvinceState management to ProvinceSystem, keep interface compatibility

### Map.Province Integration
**Current**: `ProvinceDataManager` handles visual province data
**Future**: Coordinate with ProvinceSystem for source data
**Migration**: ProvinceDataManager becomes a presentation layer that queries ProvinceSystem

### Loading System Integration
**Current**: ProvinceMapProcessor outputs ProvinceMapResult
**Future**: GameInitializer uses this to populate ProvinceSystem
**Migration**: Add ProvinceSystem.InitializeFromMapResult() method

## Performance Considerations

### Memory Layout
- **Structure of Arrays**: Separate arrays for provinceOwners[], provinceDevelopment[], etc.
- **Cache-friendly access**: Related data stored contiguously
- **Hot/Cold separation**: Frequently accessed data kept separate from occasional data

### Query Performance
- **Direct array access**: Simple queries should be O(1) array lookups
- **Burst compilation**: Performance-critical paths use Burst jobs
- **Cached results**: Expensive calculations cached and invalidated on change

### Network Optimization
- **Command-based changes**: All state changes go through serializable commands
- **Delta synchronization**: Only changed data sent over network
- **Deterministic execution**: All operations produce identical results across clients

## Testing Strategy

### Unit Tests
- Test each system in isolation
- Mock dependencies for clean testing
- Performance benchmarks for critical paths

### Integration Tests
- Test complete loading pipeline
- Test system interactions through events
- Test performance with 10k+ provinces

### Performance Tests
- Target: 10k provinces loading < 100ms
- Target: All queries < 0.01ms average
- Target: Zero allocations during gameplay

## Future Extensions

### Additional Systems (Post-Implementation)
- **DiplomaticSystem**: Manage relationships between countries
- **EconomicSystem**: Trade, resources, development
- **MilitarySystem**: Armies, battles, manpower
- **AISystem**: AI decision making and goal management

### Modding Support
- **Data-driven design**: All game rules loaded from files
- **Event hooks**: Mods can listen to game events
- **Command extensions**: Mods can add new command types

## Migration Strategy

### Phase 1: Implement Core (Week 1)
1. Create GameState, EventBus, Command system
2. Create basic ProvinceSystem and CountrySystem
3. Wire up with existing loading systems

### Phase 2: Integrate Existing (Week 2)
1. Migrate Map.Simulation to use ProvinceSystem
2. Update Map.Province to coordinate with ProvinceSystem
3. Test complete loading pipeline

### Phase 3: Optimize (Week 3)
1. Implement caching layer
2. Add performance monitoring
3. Optimize for 10k+ province performance

## Key Decision Points

### Compatibility Layers
**Decision**: Keep existing compatibility layers during migration
**Rationale**: Allows gradual migration without breaking existing functionality
**Timeline**: Remove after all systems migrated (Phase 4)

### Event vs Direct Access
**Decision**: Events for state changes, direct access for queries
**Rationale**: Events enable loose coupling, direct access ensures performance
**Implementation**: Commands emit events, queries use direct system access

### Caching Strategy
**Decision**: Frame-coherent caching with event-based invalidation
**Rationale**: Balances performance with correctness
**Implementation**: Cache expensive queries, invalidate on relevant events

## Success Metrics

### Performance Targets
- ✅ 10k provinces load in <100ms
- ✅ Province queries average <0.01ms
- ✅ Zero allocations during normal gameplay
- ✅ UI responsive at 60fps with full 10k province map

### Code Quality Targets
- ✅ Clear separation of concerns
- ✅ Single source of truth for all data
- ✅ Event-driven architecture
- ✅ 100% test coverage for core systems

## References & Context

- **Architecture Document**: `Assets/Docs/Engine/data-flow-architecture.md`
- **Existing Systems**: `Assets/Scripts/Map/Simulation/`, `Assets/Scripts/Map/Province/`
- **Loading Systems**: `Assets/Scripts/ParadoxParser/Jobs/`
- **Project Documentation**: `Assets/CLAUDE.md`

This implementation will provide the foundation for all future game systems while maintaining the performance characteristics required for large-scale grand strategy gameplay.