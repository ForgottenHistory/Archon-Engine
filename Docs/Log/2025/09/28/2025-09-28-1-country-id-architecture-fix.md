# Country ID Architecture Fix
**Date**: 2025-09-28
**Status**: Implementation Phase
**Priority**: Critical (Technical Debt Removal)

## Problem Statement
CountrySystem uses `byte` for country IDs throughout, artificially limiting to 256 countries (0-255). This violates documented architecture and prevents loading all 979 country files. Architecture docs specify `ushort` (65,535 max) for all entity IDs.

## Root Cause Analysis
- Country loader finds 979 files ✅
- JobifiedCountryLoader reports "Countries loaded: 979" ✅
- CountrySystem reports "initialized with 256 countries" ❌
- Issue: `byte nextCountryId = 1` in InitializeFromCountryData() caps at 255

## Architecture Violation
**Current (Wrong)**: `byte` country IDs (256 max)
**Documented Design**: `ushort` entity IDs (65,535 max)

Reference: `Assets/Docs/Engine/data-linking-architecture.md`
- Line 68: `ushort Register(string key, T item);`
- Line 139: `public readonly ushort Value;` for CountryId
- All examples use `ushort` for entity IDs

## Implementation Plan

### Phase 1: CountrySystem.cs Core Data Structures
**Status**: In Progress
- `NativeHashMap<ushort, byte>` → `NativeHashMap<ushort, ushort>` (tagHashToId)
- `NativeHashMap<byte, ushort>` → `NativeHashMap<ushort, ushort>` (idToTagHash)
- `NativeList<byte>` → `NativeList<ushort>` (activeCountryIds)
- `Dictionary<byte, CountryColdData>` → `Dictionary<ushort, CountryColdData>` (coldDataCache)
- `Dictionary<byte, string>` → `Dictionary<ushort, string>` (countryTags)
- `byte nextCountryId` → `ushort nextCountryId` (key fix)

### Phase 2: CountrySystem.cs Method Signatures
- `GetCountryColor(byte)` → `GetCountryColor(ushort)`
- `GetCountryTag(byte)` → `GetCountryTag(ushort)`
- `GetCountryIdFromTag()` returns `ushort`
- `GetCountryHotData(byte)` → `GetCountryHotData(ushort)`
- `GetCountryColdData(byte)` → `GetCountryColdData(ushort)`
- `HasCountryFlag(byte, byte)` → `HasCountryFlag(ushort, byte)`
- `GetAllCountryIds()` returns `NativeArray<ushort>`
- `AddCountry(byte, ...)` → `AddCountry(ushort, ...)`

### Phase 3: Dependent Systems
- **CountryQueries.cs**: All method signatures + cached data structures
- **GameState.cs**: Public country interface methods
- **Events**: CountryColorChangedEvent and related structs

### Phase 4: Testing & Validation
- Compile all systems successfully
- Load full 979 countries (vs current 256)
- Verify country operations work correctly
- Check memory usage remains acceptable

## Memory Impact
- **Before**: 1 byte per country ID
- **After**: 2 bytes per country ID
- **Cost**: ~1KB additional memory for 1000 countries (negligible)
- **Benefit**: No artificial limits, architecture compliance

## Previous Session Context

### Fixed Issues
1. **ParadoxParser Integration**: ✅ Complete
   - Refactored BurstProvinceHistoryLoader to use ParadoxParser
   - Improved from 0/3922 to 306/3923 province parsing success
   - Fixed FileLogger sharing violations

2. **Initial Country Loading**: ✅ Working
   - JobifiedCountryLoader successfully loads 979 country files
   - Issue was in CountrySystem consumption, not loading

### Current Working State
- GameInitializer loads successfully in 3.64 seconds
- Province loading: 3925 provinces with 306 successful history parses
- Country loading: 979 files found but only 256 processed by CountrySystem
- FileLogger: Fixed with unique filename timestamps and proper shutdown handling

## Key Files Modified This Session
1. `Assets/Scripts/Core/Loaders/BurstProvinceHistoryLoader.cs` - ParadoxParser integration
2. `Assets/Scripts/Utils/FileLogger.cs` - Sharing violation fixes
3. `Assets/Scripts/Utils/ArchonLogger.cs` - Shutdown safety checks
4. `Assets/Scripts/Core/Systems/CountrySystem.cs` - Capacity increased (reverted for architecture fix)

## Next Session Resumption
Start with CountrySystem.cs data structure updates. All compilation errors are expected and will be systematically fixed as each phase completes. The plan is approved and implementation-ready.

## Success Criteria
- [ ] All 979 countries load successfully
- [ ] No compilation errors across all systems
- [ ] Memory usage remains under architecture targets
- [ ] All country operations work with ushort IDs
- [ ] Technical debt eliminated, architecture compliant