# Runtime Performance Optimization — Monthly Tick & Map Updates
**Date**: 2026-02-03
**Session**: 1
**Status**: 🔄 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Eliminate monthly tick performance spikes (400ms+ hitches) caused by O(n) province scans and GPU sync stalls

**Success Criteria:**
- Monthly tick completes without noticeable frame hitch
- Province ownership queries use reverse index instead of linear scan
- No synchronous GPU readbacks on runtime update paths

---

## Context & Background

**Previous Work:**
- See: [Session 5 — Loading Performance](../02/5-loading-performance-optimization.md) — GPU compute for loading pipeline
- Monthly tick profiling revealed 400ms+ spikes from economy, AI, and map texture updates

**Current State:**
- ~50k provinces, ~10 countries, ~49k unowned provinces
- Every `GetCountryProvinces()` call was O(50k) linear scan
- `ModifierSystem` rebuilt scope caches on every query (struct copy bug)
- `OwnerTextureDispatcher` did synchronous GPU readback + per-province hash lookups
- `ProvinceSelector` allocated/destroyed a Texture2D on every mouse pick

---

## What We Did

### 1. Owner→Provinces Reverse Index in ProvinceDataManager
**Files Changed:** `Scripts/Core/Systems/Province/ProvinceDataManager.cs`

Added `NativeParallelMultiHashMap<ushort, ushort> provincesByOwner` maintained incrementally:
- `AddProvince()`: adds to reverse index (skips owner 0)
- `SetProvinceOwner()`: removes from old, adds to new (skips owner 0)
- `SetProvinceState()`: same owner-change tracking
- `GetCountryProvinces()`: reads from multimap — O(k) not O(50k)
- `GetProvinceCountForCountry()`: uses `CountValuesForKey()`
- `FillOwnerBuffer(uint[])`: bulk linear scan for GPU texture upload
- Owner 0 (unowned) excluded from reverse index to avoid 49k-entry bucket

**Architecture:** Pattern 16 (Bidirectional Mapping) — single owner for both forward and reverse lookups.

### 2. ProvinceQueries.GetCountryProvinceCount Fix
**Files Changed:** `Scripts/Core/Queries/ProvinceQueries.cs:125`

Was allocating a full `NativeArray` via `GetCountryProvinces()` just to read `.Length`. Now delegates to `GetProvinceCountForCountry()` — zero allocation, O(k).

### 3. ModifierSystem Struct Copy Bug Fix
**Files Changed:** `Scripts/Core/Modifiers/ModifierSystem.cs:222-254`

**Root cause:** `ScopedModifierContainer` is a struct. Reading from `NativeArray` creates a copy. `RebuildIfDirty()` sets `isDirty = false` on the copy — original stays dirty. Every call to `GetProvinceModifier` rebuilt the country and province scope caches (iterating 512 modifier types each).

**Fix:** Write back structs to `NativeArray` after `RebuildIfDirty()` in `GetProvinceModifier()` and `GetCountryModifier()`.

### 4. StarterKit EconomySystem Sparse Iteration
**Files Changed:** `Scripts/StarterKit/Systems/EconomySystem.cs:136-146`

- `CollectIncomeForAllCountries()`: replaced `GetAllCountryIds()` (allocates NativeArray of all countries) with `Countries.ActiveCountryIds` (existing NativeList, no allocation)
- Removed redundant `CountProvinces()` pre-check and duplicate call in logging
- Removed unused `CountProvinces()` method

### 5. EconomyManpowerManager Sparse Iteration
**Files Changed:** `Game/Systems/Economy/EconomyManpowerManager.cs:22-32`

Changed `for (ushort countryId = 1; countryId < maxCountries; countryId++)` to iterate `ActiveCountryIds`. Avoids iterating 4096 capacity slots.

### 6. OwnerTextureDispatcher — Bulk Fill + No GPU Readback
**Files Changed:**
- `Scripts/Map/Rendering/OwnerTextureDispatcher.cs` — bulk `FillOwnerBuffer()`, reusable `ownerData` field, removed verbose per-call logging
- `Scripts/Map/Rendering/MapTexturePopulator.cs:350-368` — removed `AsyncGPUReadback.WaitForCompletion()` (CPU sync stall), removed duplicate `PopulateOwnerTexture` call

**Before:** `GetAllProvinceIds()` (alloc) + per-province `GetOwner()` (hash lookup each) + `AsyncGPUReadback.WaitForCompletion()` (GPU sync stall)
**After:** Single `FillOwnerBuffer()` linear pass, reused array, no GPU readback

### 7. ProvinceQueryBuilder Smart Iteration
**Files Changed:** `Scripts/Core/Queries/ProvinceQueryBuilder.cs:192-214`

`Execute()` was always scanning all 50k provinces. Now uses narrowest available set:
1. `borderingSet` available → iterate bordering set directly (tens of provinces)
2. `filterOwnerId` set → use reverse index via `GetCountryProvinces()`
3. Fallback → scan all provinces

Fixes AI `TryColonize` (`BorderingCountry().IsUnowned()`) and `TryBuildFarm` (`OwnedBy()`).

### 8. ProvinceSelector Cached Readback Texture
**Files Changed:**
- `Scripts/Map/MapTextureManager.cs:101-122` — cached `Texture2D(1,1)` field instead of alloc/destroy per call
- `Scripts/Map/Interaction/ProvinceSelector.cs:194-199` — eliminated double `GetProvinceID` call when debug logging enabled

---

## Problems Encountered & Solutions

### Problem 1: Reverse Index Remove from Owner 0 — 125ms
**Symptom:** `RemoveFromReverseIndex` taking 125ms during colonization
**Root Cause:** Owner 0 (unowned) had 49k entries in the multimap. Colonizing removes from owner 0's bucket — linear scan through 49k entries.
**Solution:** Exclude owner 0 from reverse index entirely. Nobody queries "all unowned provinces" through this API.

### Problem 2: ModifierSystem Rebuilding Every Call — 67ms
**Symptom:** `GetProvinceModifier` → `RebuildIfDirty` taking 67ms per country
**Root Cause:** `ScopedModifierContainer` is a value type (struct). Reading from `NativeArray` copies it. `isDirty = false` written inside `RebuildIfDirty` is lost — the array element stays dirty. Every subsequent call rebuilds the 512-type cache.
**Solution:** Write back the struct to the NativeArray after `RebuildIfDirty()`.

### Problem 3: GPU Sync Stall on Map Update — 138ms
**Symptom:** `AsyncGPUReadback.WaitForCompletion()` blocking CPU for 138ms
**Root Cause:** Unnecessary synchronous GPU readback after compute shader dispatch. Downstream GPU operations read `ProvinceOwnerTexture` directly — no CPU readback needed.
**Solution:** Removed the readback entirely. GPU-to-GPU data flow doesn't need CPU involvement.

---

## Architecture Impact

### New Patterns
**Reverse Index with Sentinel Exclusion:**
- Skip storing sentinel values (owner 0 = unowned) in reverse index when they dominate the dataset
- Prevents O(n) bucket scans on the most common ownership transition (unowned → owned)

**Struct-in-NativeArray Write-Back:**
- When mutating structs read from `NativeArray`, always write back to persist changes
- Easy to miss — the code compiles and "works" but silently discards mutations

---

## Next Session

### Immediate Next Steps
1. **Investigate `ReadPixels` GPU sync in ProvinceSelector** — `Semaphore.WaitForSignal` shows CPU waiting for GPU pipeline flush. Options: async readback with 1-frame delay, or CPU-side province ID cache
2. **Profile monthly tick after all fixes** — verify cumulative improvement

### Open Questions
1. Can `ProvinceSelector.GetProvinceID` use async GPU readback with 1-frame latency? Mouse picking tolerates 1 frame delay.
2. Should we maintain a CPU-side copy of the province ID texture for instant lookups? Trade memory for zero GPU sync.

---

## Quick Reference for Future Claude

**Key implementations:**
- Reverse index: `Scripts/Core/Systems/Province/ProvinceDataManager.cs` — `provincesByOwner` field, `FillOwnerBuffer()`, `RemoveFromReverseIndex()`
- ModifierSystem fix: `Scripts/Core/Modifiers/ModifierSystem.cs:222-254` — write-back after `RebuildIfDirty()`
- Query optimization: `Scripts/Core/Queries/ProvinceQueryBuilder.cs:192-214` — smart iteration source selection
- Texture readback cache: `Scripts/Map/MapTextureManager.cs:101-122` — `cachedReadbackTexture` field

**Gotchas:**
- Owner 0 is NOT in the reverse index — `GetCountryProvinces(0)` returns empty
- `ScopedModifierContainer` is a struct — always write back to NativeArray after mutation
- `AsyncGPUReadback.WaitForCompletion()` on runtime paths causes frame hitches — avoid unless CPU needs the data

**Files changed this session:**
- `Scripts/Core/Systems/Province/ProvinceDataManager.cs` — reverse index, bulk fill
- `Scripts/Core/Systems/ProvinceSystem.cs` — exposed `FillOwnerBuffer`
- `Scripts/Core/Queries/ProvinceQueries.cs` — `FillOwnerBuffer`, `GetCountryProvinceCount` fix
- `Scripts/Core/Queries/ProvinceQueryBuilder.cs` — smart iteration
- `Scripts/Core/Modifiers/ModifierSystem.cs` — struct write-back fix
- `Scripts/Map/Rendering/OwnerTextureDispatcher.cs` — bulk fill, no alloc, removed verbose logs
- `Scripts/Map/Rendering/MapTexturePopulator.cs` — removed GPU sync stall + duplicate call
- `Scripts/Map/MapTextureManager.cs` — cached readback texture
- `Scripts/Map/Interaction/ProvinceSelector.cs` — eliminated double GetProvinceID
- `Scripts/StarterKit/Systems/EconomySystem.cs` — sparse country iteration
- `Game/Systems/Economy/EconomyManpowerManager.cs` — ActiveCountryIds
- `Game/Systems/EconomySystem.cs` — pass ActiveCountryIds

---

## Related Sessions
- [Session 5 — Loading Performance](../02/5-loading-performance-optimization.md) — GPU compute for loading
