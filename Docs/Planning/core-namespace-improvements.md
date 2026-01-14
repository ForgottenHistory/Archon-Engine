# CORE Namespace Improvement Plan

**Created**: 2026-01-14
**Status**: Planning
**Purpose**: Identify systems that work but need enhancement for a production-ready public engine

---

## Overview

Comprehensive audit of the CORE namespace (162 C# files) to find systems similar to TimeManager - functional but needing expansion for real-world public use.

---

## HIGH PRIORITY

### 1. ResourceSystem
**Location**: `Core/Resources/ResourceSystem.cs`
**Current State**: Good foundation with complete API

**Missing**:
- No `IResourceProvider` interface for GAME layer customization
- No bulk operations (`AddResourceToAll`, `SetResourceForAll`)
- No `HasSufficientResources()` for cost validation
- No monthly tracking (`GetMonthlyGain`, `GetMonthlyLoss`)
- No batch event suppression during setup

**Recommendation**:
- Add `IResourceProvider` interface
- Add `ResourceQuery` builder for filtering
- Add batch operations with single event emission
- Add resource tracking for economy UI

---

### 2. AISystem & AIScheduler ✅ DONE (Session 11)
**Location**: `Core/AI/AISystem.cs`, `Core/AI/AIScheduler.cs`

**Implemented:**
- `IGoalSelector` interface for custom goal prioritization
- `GoalConstraint` system with built-in constraints (MinProvinces, MinResource, AtWar, etc.)
- `GetActiveGoal(countryId)`, `GetCountriesByTier(tier)`, `GetCountryCountByTier(tier)`
- `SetAIEnabled(countryId, bool)` (alias for existing SetAIActive)
- Execution timeout with statistics tracking
- `AIStatistics` and `AIDebugInfo` for debugging

**Also Fixed:**
- Added `GameState.Diplomacy` property (was missing)
- Added `ProvinceSystem.GetProvinceCountForCountry()`
- Added `DiplomacySystem.IsAtWar(countryID)` single-param overload

---

### 3. PathfindingSystem ✅ DONE (Session 10)
**Location**: `Core/Systems/PathfindingSystem.cs`

**Implemented**:
- `IMovementCostCalculator` interface with PathContext, PathContextFlags
- `PathOptions` struct for forbidden/avoid provinces, max path length
- `PathResult` struct with PathStatus enum
- `PathCache` LRU cache with invalidation support
- `FindPathWithOptions()`, `FindPathAvoiding()`, `FindPathWithForbidden()`
- `PathExists()`, `GetDistance()` convenience methods
- Statistics: TotalSearches, CacheHits, CacheHitRate

**Not Implemented** (optional future):
- Batched pathfinding (100 units at once) - add when needed
- Memory usage reporting - add when needed

---

### 4. FixedPoint64 & FixedPoint32
**Location**: `Core/Data/FixedPoint64.cs`, `Core/Data/Math/FixedPointMath.cs`
**Current State**: FixedPoint64 comprehensive, FixedPoint32 minimal

**Missing from Both**:
- No `IsZero`, `IsPositive`, `IsNegative` properties
- No `Sqrt()` square root
- No `Pow(exponent)` exponentiation
- No `Lerp(a, b, t)` interpolation
- No `InverseLerp(a, b, value)` reverse interpolation
- No `Remap(value, inMin, inMax, outMin, outMax)`

**Missing from FixedPoint32 Only**:
- No `FromFraction()`, `FromFloat()`, `FromDouble()`
- No division operator
- No modulo operator
- No `Abs()`, `Min()`, `Max()`, `Clamp()`
- No `Floor()`, `Ceiling()`, `Round()`
- No `==`, `!=` operators
- No `IEquatable`, `IComparable`
- No serialization (`ToBytes`, `FromBytes`)

**Recommendation**:
- Bring FixedPoint32 to parity with FixedPoint64
- Add convenience properties to both
- Add math functions (Sqrt, Pow, Lerp) to both
- All functions must be Burst-compatible

---

## MEDIUM PRIORITY

### 5. DeterministicRandom ✅ DONE (Session 11)
**Location**: `Core/Data/DeterministicRandom.cs`

**Implemented:**
- `NextGaussian()` - normal distribution (Central Limit Theorem)
- `NextWeightedElement()` / `NextWeightedIndex()` - weighted selection
- `NextElementExcept()` / `NextElementExceptIndices()` - exclusion
- `ToSeedPhrase()` / `FromSeedPhrase()` - human-readable state

---

### 6. AdjacencySystem ✅ DONE (Session 12)
**Location**: `Core/Systems/AdjacencySystem.cs`

**Implemented:**
- `AdjacencyStats` struct for queryable statistics
- `GetNeighborsWhere(predicate)` - filtered neighbor query
- `GetConnectedRegion(start, predicate)` - BFS flood fill
- `GetSharedBorderProvinces(owned, foreign)` - find border provinces
- `IsBridgeProvince(province, predicate)` - critical choke point detection
- `FindBridgeProvinces(region, predicate)` - find all bridge provinces

---

### 7. Query System
**Location**: `Core/Queries/`
**Current State**: Good fluent builders

**Missing**:
- No `And()` / `Or()` / `Not()` operators
- No `ExecuteCount()` without allocation
- No `Any()` short-circuit
- No query profiling/timing

**Recommendation**:
- Add logical operators
- Add count-only execution
- Add query metrics

---

### 8. ProvinceDataManager
**Location**: `Core/Systems/Province/ProvinceDataManager.cs`
**Current State**: Comprehensive with double-buffering

**Missing**:
- No fluent query interface
- No batch operations (change owner for multiple)
- No province flags/tags system
- No bulk event suppression

**Recommendation**:
- Add `ProvinceDataQuery` builder
- Add batch operations with single event
- Add `BeginBatch()` / `EndBatch()` for event suppression

---

### 9. ProvinceHistoryDatabase
**Location**: `Core/Data/ProvinceHistoryDatabase.cs`
**Current State**: Tiered storage with compression

**Missing**:
- Compression threshold hardcoded (50 years)
- Uses `float` for AverageDevelopment (should be FixedPoint64)
- No cross-province trending queries
- No history export for saves
- No pruning for very old data

**Recommendation**:
- Add `HistoryConfig` with configurable thresholds
- Fix float → FixedPoint64
- Add trending and export methods

---

### 10. GameRegistries
**Location**: `Core/Registries/GameRegistries.cs`
**Current State**: Minimal, placeholder classes

**Missing**:
- No registry validation
- No change notifications
- No hot reload support
- No schema documentation

**Recommendation**:
- Add `IRegistry` with `ValidateIntegrity()`
- Add change events
- Document content requirements

---

## LOW PRIORITY

### 11. GameSystem Base Class
**Location**: `Core/Systems/GameSystem.cs`
**Current State**: Solid with dependency validation

**Missing**:
- No lifecycle state tracking
- No async initialization support

**Recommendation**:
- Add `SystemState` enum
- Minor enhancement only

---

### 12. Result Types
**Location**: `Core/Common/Result.cs`
**Current State**: Good result pattern

**Missing**:
- No `Match(onSuccess, onFailure)`
- No cache statistics

**Recommendation**:
- Add Match operator
- Minor enhancement only

---

## Key Architectural Observations

1. ~~**Interface Gaps**: ResourceSystem, PathfindingSystem, AISystem lack interfaces~~ ✅ All done (Sessions 9-11)
2. **Query Inconsistency**: Some systems use builders, others don't
3. **Hardcoded Values**: AIScheduler intervals, history thresholds should be configurable
4. ~~**Event Inconsistency**: ResourceSystem uses C# events, others use EventBus~~ ✅ Fixed - EventBus only
5. ~~**FixedPoint32 Underdeveloped**: Significantly behind FixedPoint64 in features~~ ✅ Fixed

---

## Implementation Order

| Phase | Systems | Focus |
|-------|---------|-------|
| 1 | FixedPoint64, FixedPoint32 | Foundation - every system uses these |
| 2 | ResourceSystem | High value for strategy games |
| 3 | PathfindingSystem | Scale bottleneck prevention |
| 4 | AISystem | Modder/plugin support |
| 5 | Query consistency | Polish and DX |

---

## Completed

- [x] TimeManager / Calendar System (Session 8)
- [x] FixedPoint64 / FixedPoint32 enhancements (Session 9)
- [x] ResourceSystem interfaces (Session 9)
- [x] PathfindingSystem improvements (Session 10)
- [x] AISystem flexibility (Session 11)
- [x] DeterministicRandom enhancements (Session 11)
- [x] AdjacencySystem queries (Session 12)

---

*Last Updated: 2026-01-14*
