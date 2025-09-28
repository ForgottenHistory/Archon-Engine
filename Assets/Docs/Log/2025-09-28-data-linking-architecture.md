# Data Linking Architecture Implementation
**Date**: 2025-09-28
**Status**: Implementation Planning
**Priority**: High (Core Feature Implementation)

## Problem Statement
Loaded game data contains string references ("SWE", "catholic", "grain") that need linking to actual runtime IDs. Currently provinces have unresolved string tags instead of efficient numeric IDs, preventing proper data relationships and fast runtime lookups.

## Current State Analysis
✅ **Working Systems:**
- Province bitmap loaded (3925 provinces with visual data)
- Province history files loaded (306 successfully parsed)
- Country files loaded (979 countries with tags)
- Event-driven architecture (MapGenerator receives Core data)
- MapGenerator displays visual map successfully

❌ **Missing Systems:**
- String reference resolution (provinces still have "SWE" strings)
- Registry system for entity lookup
- Cross-references (countries don't know owned provinces)
- Type-safe ID system

## Architecture Reference
Following `Assets/Docs/Engine/data-linking-architecture.md`:
- **Three-phase loading**: Discovery → Load → Link
- **Registry pattern**: String-to-ID mapping with O(1) array lookups
- **Typed IDs**: CountryId, ReligionId instead of raw ushort
- **Performance**: No strings at runtime, only array indexing

## Data Currently Loaded

### Province Data (from 1-Uppland.txt example):
```
owner = SWE          # Needs -> CountryId
culture = swedish    # Needs -> CultureId
religion = catholic  # Needs -> ReligionId
trade_goods = grain  # Needs -> TradeGoodId
```

### Country Data (from SWE - Sweden.txt example):
```
primary_culture = swedish  # Needs -> CultureId
religion = catholic        # Needs -> ReligionId
capital = 1               # Already numeric ✅
```

## Implementation Plan

### Phase 1: Registry Infrastructure
**Location**: `Core/Registries/`
- `IRegistry.cs` - Base interface
- `Registry.cs` - Generic implementation
- `GameRegistries.cs` - Central container
- `CountryRegistry.cs` - Special country handling
- `ProvinceRegistry.cs` - Dense province ID mapping

### Phase 2: Typed ID System
**Location**: `Core/Data/Ids/`
- `CountryId.cs` - Type-safe country IDs
- `ReligionId.cs` - Type-safe religion IDs
- `CultureId.cs` - Type-safe culture IDs
- `TradeGoodId.cs` - Type-safe trade good IDs

### Phase 3: Static Data Loaders
**Location**: `Core/Loaders/`
- `ReligionLoader.cs` - Load common/religions
- `CultureLoader.cs` - Load common/cultures
- `TradeGoodLoader.cs` - Load trade goods
- `TerrainLoader.cs` - Load terrain types

### Phase 4: Reference Resolution
**Location**: `Core/Linking/`
- `ReferenceResolver.cs` - Convert strings to IDs
- `CrossReferenceBuilder.cs` - Build bidirectional links
- `DataValidator.cs` - Validate all references exist

### Phase 5: GameInitializer Integration
**Modify**: `Core/GameInitializer.cs`
New loading phases:
1. Load & register static data (religions, cultures)
2. Register countries and provinces (discovery)
3. Load data with string references
4. **Resolve references to IDs**
5. **Build cross-references**
6. Validate data integrity

### Phase 6: System Updates
**Modify existing systems**:
- `ProvinceInitialState.cs` - Add resolved ID fields
- `ProvinceSystem.cs` - Use resolved IDs
- `CountrySystem.cs` - Add province ownership lists
- `MapGenerator.cs` - Use registry for colors

## Expected Benefits
- **Performance**: O(1) array lookups vs string comparisons
- **Type Safety**: Compile-time ID validation
- **Data Integrity**: All bad references caught at load time
- **Memory**: Efficient ushort IDs vs string storage
- **Maintainability**: Clear raw vs resolved data separation

## Implementation Context

### Current Working Session State
- Event-driven architecture working (SimulationDataReadyEvent delivered)
- MapGenerator successfully generates visual map from Core data
- All data loading phases complete and validated
- FileLogger capturing all debug information

### Key Files Recently Modified
1. `GameInitializer.cs` - Added event emission debug logging
2. `MapGenerator.cs` - Added event reception debug logging
3. EventBus integration working correctly

### Success from Previous Session
```log
[10:14:13.779] MapGenerator: Received SimulationDataReadyEvent - 3925 provinces, 979 countries ready
[10:14:19.207] MapGenerator: Event-driven map generation complete. Rendering 3925 provinces.
```

## Implementation Order
1. **Create registry system** (foundation)
2. **Load static data** (religions, cultures, trade goods)
3. **Implement reference resolution** (string → ID conversion)
4. **Integrate with GameInitializer** (new loading phases)
5. **Update dependent systems** (use resolved IDs)
6. **Test & validate** (all references work)

## Session Continuation Guide
Start with Phase 1 (Registry Infrastructure). Each phase builds on the previous, following the three-phase loading pattern from the architecture document. All changes maintain the dual-layer architecture and event-driven design.

## Success Criteria
- [ ] All string references resolved to numeric IDs
- [ ] Type-safe ID system implemented
- [ ] Cross-references built (countries know owned provinces)
- [ ] O(1) entity lookups working
- [ ] No strings used at runtime
- [ ] All data validation passes
- [ ] Map renders with resolved data