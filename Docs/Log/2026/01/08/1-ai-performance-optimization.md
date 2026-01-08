# AI Performance Optimization
**Date**: 2026-01-08
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:** Fix severe FPS drop at 10x game speed (10 FPS vs 120 FPS at 5x).

**Success Criteria:**
- 10x speed runs at stable 60+ FPS
- No death spiral at high speeds
- AI evaluation costs minimal per tick

---

## Context & Background

**Previous Work:**
- See: `ai-tier-scheduling.md` (2026-01-06) - Tier-based AI scheduling

**Current State:**
- 5x speed: 120 FPS (acceptable)
- 10x speed: 10 FPS (unacceptable death spiral)

**Why Now:**
- 10x speed stability shows capacity for future features
- Death spiral pattern indicates architectural issues

---

## What We Did

### 1. Tick Cap in TimeManager
**Files Changed:** `Core/Systems/TimeManager.cs:31, 140-146`

Added `MAX_TICKS_PER_FRAME = 4` cap to prevent death spiral.

**Rationale:** Paradox-style approach - game time slows rather than FPS collapsing. Breaks positive feedback loop where frame lag → more ticks → more lag.

### 2. Removed Hot-Path Logging
**Files Changed:**
- `Core/AI/AIScheduler.cs:83, 146` - Removed per-tick and per-AI logging
- `Game/AI/Goals/BuildEconomyGoal.cs` - Removed per-execution logging
- `Game/AI/Goals/ExpandTerritoryGoal.cs` - Removed per-execution logging

**Impact:** Profiler showed 13ms/frame on `LogStringToConsole` alone.

### 3. EventBus Wiring for AIGoal
**Files Changed:**
- `Core/AI/AIGoal.cs:86` - `Initialize(EventBus eventBus)`
- `Core/AI/AIGoalRegistry.cs:29-38, 64` - Store and pass EventBus
- `Core/AI/AISystem.cs:71-72` - Pass `gameState.EventBus`

**Rationale:** Goals need event subscription for cache invalidation.

### 4. Event-Driven Caching in ExpandTerritoryGoal
**Files Changed:** `Game/AI/Goals/ExpandTerritoryGoal.cs:42-51, 64-77, 254-276, 283-345, 344-399`

Three caches with `ProvinceOwnershipChangedEvent` invalidation:
- `neighborCache` - neighbor countries per country
- `ownedProvincesCache` - owned provinces per country
- `provinceCountCache` - province count per country

**Before:** O(13k) scan per call, called 7+ times per AI evaluation
**After:** O(1) cache hit, recalculate only on border changes

---

## Problems Encountered & Solutions

### Problem 1: Death Spiral at 10x Speed
**Symptom:** FPS drops from 120 to 10 when switching from 5x to 10x
**Root Cause:** Accumulator-based tick processing with no cap. Frame lag → larger deltaTime → more ticks accumulated → more work → worse frame lag → cascade.
**Solution:** Cap ticks per frame at 4. Deferred ticks carry to next frame.

### Problem 2: 13ms Logging Overhead
**Symptom:** `LogStringToConsole` consuming 13ms per frame
**Root Cause:** AI logging every hourly tick and every goal execution
**Solution:** Remove hot-path logs. Keep error/warning logs only.

### Problem 3: O(13k) Province Scans
**Symptom:** `GetCountryProvinces` at 100ms, `GetProvinceCount` at 50ms
**Root Cause:** Linear scan of all provinces for each call. Called redundantly in Evaluate and Execute.
**Solution:** Event-driven caching. Subscribe to `ProvinceOwnershipChangedEvent`, invalidate affected countries only.

### Problem 4: Redundant Calculations
**Symptom:** Same country's data fetched multiple times per evaluation
**Root Cause:** `GetNeighborCountries` refetched owned provinces. `GetProvinceCount` called for each neighbor.
**Solution:** Cache all three data types. Reuse `ownedProvincesBuffer` between methods.

---

## Architecture Impact

### Pattern Applied
**Event-Driven Cache Invalidation (Pattern 3 extension):**
- Cache expensive calculations per-entity
- Subscribe to relevant change events
- Invalidate only affected entities
- Recalculate on next access (lazy)

### Documentation Updated
- [x] Core/FILE_REGISTRY.md should note AIGoal.Initialize signature change

---

## Code Quality Notes

### Performance
**Before:**
- 10x speed: 10 FPS
- `GetCountryProvinces`: 100ms
- `GetProvinceCount`: 50ms
- `GetNeighborCountries`: expensive

**After:**
- 10x speed: 60+ FPS (stable)
- All three operations: ~0ms (cache hit)

---

## Quick Reference for Future Claude

**Key Files:**
- `Core/Systems/TimeManager.cs:31` - MAX_TICKS_PER_FRAME constant
- `Core/AI/AIGoal.cs:86` - Initialize(EventBus) signature
- `Game/AI/Goals/ExpandTerritoryGoal.cs:254-276` - OnProvinceOwnershipChanged handler
- `Game/AI/Goals/ExpandTerritoryGoal.cs:344-399` - Cached province methods

**Critical Details:**
- Tick cap of 4 prevents death spiral but allows game time to slow
- All AI goal caches invalidated by `ProvinceOwnershipChangedEvent`
- Cache invalidation is conservative: invalidates old owner, new owner, AND their neighbors

**Gotchas:**
- `ownedProvincesBuffer` is populated by `GetCachedOwnedProvinces`, assumed valid in `GetNeighborCountries`
- Don't remove from `invalidatedCountries` until all caches updated

---

## Links & References

### Related Sessions
- [AI Tier Scheduling](../06/ai-tier-scheduling.md) - Previous AI work

### Code References
- Tick cap: `TimeManager.cs:31, 140-146`
- Event subscription: `ExpandTerritoryGoal.cs:67-68`
- Cache invalidation: `ExpandTerritoryGoal.cs:254-276`
- Cached methods: `ExpandTerritoryGoal.cs:283-399`
