# Data Loading Integration Plan
**Date**: 2025-09-27
**Status**: Implementation Phase
**Dependencies**: Central data access hub (completed)

## Problem Statement
The GameState hub architecture exists but has no data. Need to connect existing loaders (ProvinceMapProcessor, JobifiedCountryLoader) to populate the systems with actual game data.

## Architecture Overview

### Current State
- ✅ GameState hub with ProvinceSystem/CountrySystem
- ✅ ProvinceMapProcessor (loads provinces.bmp + definitions.csv)
- ✅ JobifiedCountryLoader (loads country files with burst jobs)
- ❌ No connection between loaders and systems

### Target State
```
Files → Loaders → Results → GameState Systems → Ready to Play
```

## Implementation Plan

### 1. GameInitializer - Master Orchestrator
**File**: `Assets/Scripts/Core/GameInitializer.cs`
- MonoBehaviour that coordinates all loading phases
- Progress reporting with loading screen integration
- Error handling and recovery strategies
- Dependency management between systems

**Key Responsibilities**:
- Initialize core infrastructure (EventBus, TimeManager)
- Coordinate data loading sequence
- Apply scenario data to systems
- Warm up caches and prepare game loop

### 2. Loading Sequence
1. **Core Systems** (instant) - EventBus, TimeManager, empty GameState
2. **Static Data** (2-3s) - provinces.bmp, countries/, definitions
3. **Scenario Data** (1s) - 1444 start conditions, province ownership
4. **Derived Systems** (1s) - AI initialization, cache warming
5. **Game Ready** - Start main game loop

### 3. Integration Points

#### ProvinceMapProcessor → ProvinceSystem
- `ProvinceSystem.InitializeFromMapData()` already exists
- Takes `ProvinceMapResult` from processor
- Populates province IDs, terrain, basic data

#### JobifiedCountryLoader → CountrySystem
- `CountrySystem.InitializeFromCountryData()` already exists
- Takes `CountryDataLoadResult` wrapper
- Populates country definitions and metadata

#### New: ScenarioLoader → Multiple Systems
- Load initial game state (1444, 1836, etc.)
- Apply province ownership via commands
- Set starting treasuries, armies, development

### 4. Configuration System
**File**: `Assets/Scripts/Core/GameSettings.cs` (ScriptableObject)
- Data file paths (provinces.bmp, countries/ directory)
- Loading options (parallel processing, validation levels)
- Performance targets (province count, memory limits)

### 5. Error Handling Strategy
- **Validation**: Check file existence and format before processing
- **Graceful degradation**: Use defaults for non-critical missing data
- **Clear messaging**: User-friendly errors with recovery suggestions
- **Rollback**: Return to menu on critical failures

## Key Technical Details

### Existing Integration Ready
- ProvinceSystem already has `InitializeFromMapData(ProvinceMapResult)`
- CountrySystem already has `InitializeFromCountryData(CountryDataLoadResult)`
- Both systems emit events when initialized

### Performance Targets
- **Total loading**: <5 seconds for 10k provinces
- **Province loading**: <2 seconds (parallel BMP + definitions)
- **Country loading**: <1 second (burst compiled)
- **Memory peak**: <100MB during loading

### Error Recovery
- Missing provinces.bmp → Fatal error (required for gameplay)
- Missing countries/ → Continue with minimal default countries
- Missing scenario → Use empty map with default ownership
- Corrupt definitions.csv → Use BMP data only, warn user

## Testing Requirements

### Integration Tests
- Complete loading pipeline (files → game ready)
- Data integrity validation (loaded data matches files)
- Performance benchmarks at scale (1k, 5k, 10k provinces)
- Error scenarios (missing files, corrupt data)

### Memory Management
- Proper disposal of NativeCollections from loaders
- No memory leaks during loading process
- Peak memory usage within targets

## Future Enhancements
- **Mod Support**: Override data files with mod content
- **Hot Reload**: Reload data in editor without restart
- **Incremental Loading**: Stream huge maps in chunks
- **Caching**: Serialize processed data for faster reloads

## Dependencies on Existing Systems

### Already Complete
- GameState hub architecture
- ProvinceSystem with 8-byte ProvinceState
- CountrySystem with hot/cold data separation
- Command pattern for state changes
- EventBus for system coordination

### Required Existing Loaders
- ProvinceMapProcessor (ParadoxParser.Jobs namespace)
- JobifiedCountryLoader (GameData.Loaders namespace)
- BMPLoadResult and ProvinceMapResult structures
- CountryDataLoadResult wrapper

## Implementation Priority
1. GameInitializer (master coordinator)
2. ScenarioLoader (initial game state)
3. GameSettings (configuration)
4. Integration testing
5. Error handling polish

The core architecture is ready - this plan just wires up the data flow to populate it with real game data.