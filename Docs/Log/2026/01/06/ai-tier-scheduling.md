# AI Tier-Based Scheduling
**Date**: 2026-01-06
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:** Replace arbitrary AI bucketing (countryID % 30) with distance-based priority tiers.

**Success Criteria:**
- Near AI (neighbors) processed frequently
- Far AI processed rarely
- No performance spikes
- Configurable tier system (ENGINE mechanism, GAME policy)

---

## Context & Background

**Previous Work:**
- See: `ai-system-implementation.md` - Original bucketing implementation (2025-10-25)

**Current State:**
- AI processed daily via bucket assignment (countryID % 30)
- All AI treated equally regardless of relevance to player

**Why Now:**
- Abstract bucketing has no gameplay meaning
- Player-relevant AI should feel responsive
- Distant AI wastes cycles processing frequently

---

## What We Did

### 1. AISchedulingConfig (NEW)
**Files Changed:** `Core/AI/AISchedulingConfig.cs` (new file)

Configurable tier definitions with distance thresholds and processing intervals.

**Default Tiers:**
| Tier | Distance | Interval |
|------|----------|----------|
| 0 | 0-1 | 1 hour |
| 1 | 2-4 | 6 hours |
| 2 | 5-8 | 24 hours |
| 3 | 9+ | 72 hours |

### 2. AIDistanceCalculator (NEW)
**Files Changed:** `Core/AI/AIDistanceCalculator.cs` (new file)

BFS from player provinces through adjacency graph. Calculates minimum province-hop distance per country.

**Key Methods:**
- `CalculateDistances(playerCountryID, provinceSystem, adjacencySystem)`
- `AssignTiers(aiStates, config)`

### 3. AIState Changes
**Files Changed:** `Core/AI/AIState.cs:21-48`

- `bucket` → `tier` (distance-based priority)
- `reserved` → `lastProcessedHour` (hour-of-year tracking, 0-8639)

### 4. AIScheduler Changes
**Files Changed:** `Core/AI/AIScheduler.cs:36-165`

- Changed from `ProcessDailyBucket()` to `ProcessHourlyTick()`
- Interval-based processing with year-wrap handling
- `CalculateHourOfYear(month, day, hour)` helper

### 5. AISystem Integration
**Files Changed:** `Core/AI/AISystem.cs:104-163`

- New signature: `InitializeCountryAI(countryCount, provinceCount, playerCountryID, currentHourOfYear)`
- `RecalculateDistances()` for monthly updates
- Staggered `lastProcessedHour` initialization (prevents spikes)
- Country 0 (null/unowned) always inactive

### 6. AITickHandler (GAME Layer)
**Files Changed:** `Game/AI/AITickHandler.cs:127-212`

- Subscribe to `HourlyTickEvent` (processing)
- Subscribe to `MonthlyTickEvent` (distance recalculation)
- Pass current hour to initialization for staggering

---

## Problems Encountered & Solutions

### Problem 1: 3-Day Stutter
**Symptom:** Hard hitch every 72 hours
**Root Cause:** All Tier 3 AI had `lastProcessedHour = 0`, became eligible simultaneously
**Solution:** Stagger `lastProcessedHour` at init: `currentHourOfYear - (countryID % 72)`

### Problem 2: Country 0 Building Errors
**Symptom:** "Province 1000 has no owner" spam
**Root Cause:** Country 0 (null/unowned) was processing AI, trying to build in uncolonized provinces
**Solution:** `bool isActive = (i != 0)` in AISystem initialization

### Problem 3: Namespace Conflict
**Symptom:** `CS0118: 'ProvinceSystem' is a namespace`
**Solution:** Use fully qualified `Core.Systems.ProvinceSystem`

### Problem 4: Non-MonoBehaviour GetComponent
**Symptom:** `GetComponent requires MonoBehaviour`
**Solution:** Use `gameState.Provinces` and `gameState.Adjacencies` instead

---

## Architecture Impact

### Documentation Updated
- [x] `ai-system-implementation.md` - Added 2026-01-06 update section
- [x] `ai-design.md` - Updated bucketing → tier-based patterns
- [x] `Core/FILE_REGISTRY.md` - Added new AI files

### Pattern Applied
**ENGINE-GAME Separation (Pattern 1):**
- ENGINE: `AISchedulingConfig` provides tier mechanism
- GAME: Default config provides policy (distance thresholds, intervals)
- GAME can override via `aiSystem.SetSchedulingConfig(customConfig)`

---

## Code Quality Notes

### Performance
- Near AI: Responsive (hourly)
- Far AI: Background (72 hours)
- No stutters (staggered init)
- Monthly BFS recalculation (~13k provinces, acceptable)

---

## Quick Reference for Future Claude

**Key Files:**
- `Core/AI/AISchedulingConfig.cs` - Tier definitions
- `Core/AI/AIDistanceCalculator.cs` - BFS distance calculation
- `Core/AI/AISystem.cs:122-140` - Staggered initialization
- `Game/AI/AITickHandler.cs:190-211` - Hourly/Monthly tick handlers

**Critical Details:**
- Country 0 is always inactive (null country)
- Hour-of-year range: 0-8639 (360 days × 24 hours)
- Year wrap handled in `ShouldProcess()`
- Distances recalculated monthly via `MonthlyTickEvent`

**Gotchas:**
- Don't use `GetComponent<AdjacencySystem>()` - use `gameState.Adjacencies`
- Must stagger `lastProcessedHour` to prevent tier-wide spikes

---

## Links & References

### Related Documentation
- [AI System Implementation](../../Planning/ai-system-implementation.md)
- [AI Design](../../Planning/ai-design.md)

### Code References
- Tier config: `AISchedulingConfig.cs:85-93`
- Distance BFS: `AIDistanceCalculator.cs:75-135`
- Staggered init: `AISystem.cs:122-140`
- Hourly processing: `AIScheduler.cs:55-85`
