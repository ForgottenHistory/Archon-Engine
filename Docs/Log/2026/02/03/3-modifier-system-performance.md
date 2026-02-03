# ModifierSystem Monthly Tick Performance
**Date**: 2026-02-03
**Session**: 3
**Status**: 🔄 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Eliminate 7000ms `GameState.Update → GC.Alloc` spike on monthly tick in late game (~665 AI countries, ~50k provinces)

**Success Criteria:**
- Monthly tick under 100ms
- Zero GC allocations during monthly tick

---

## Context & Background

**Previous Work:**
- See: [Session 2 — Semaphore Fix](2-semaphore-waitforsignal-fix.md) — GPU sync stall fix
- See: [Session 1 — Runtime Performance](1-runtime-performance-optimization.md) — reverse index, initial modifier fix

**Current State:**
- Deep profiler showed two hotspots in `EventBus.ProcessEvents → MonthlyTickEvent`:
  - `AISystem.OnMonthlyTick`: 3513ms (TryBuildFarm: 3160ms, TryColonize: 350ms)
  - `EconomySystem.OnMonthlyTick`: 3468ms (CalculateCountryIncome: 3463ms)

**Root Cause Chain:**
1. `MarkCountryProvincesDirty` marked ALL 65,536 provinces dirty (O(65k) struct writes per call)
2. Each AI farm build triggered this → 665 countries × 65k = **43M struct writes**
3. `RebuildIfDirty` iterated 512 `MAX_MODIFIER_TYPES` slots to copy parent modifiers
4. `ScopedModifierContainer` is ~8KB struct (contains `ModifierSet` with 512×2 fixed longs) — every NativeArray access copies 8KB
5. `GetProvinceModifier` called 2× per province per country income calc → ~100k calls × 8KB = **800MB struct copying**

---

## What We Did

### 1. Reverse Index for MarkCountryProvincesDirty
**Files Changed:** `Core/Modifiers/ModifierSystem.cs`, `Core/GameState.cs`

- Added `Action<ushort, NativeList<ushort>> getCountryProvincesFunc` callback + `NativeList<ushort> countryProvinceBuffer`
- `SetCountryProvincesLookup()` wired in `GameState.InitializeSystems()` after `Provinces.Initialize()`
- `MarkCountryProvincesDirty` now marks only owned provinces (~75 avg) instead of all 65k
- Cost reduction: 65,536 → ~75 struct writes per call = **~870x improvement**

### 2. Bitmask Active Type Tracking in ModifierSet
**Files Changed:** `Core/Modifiers/ModifierSet.cs`, `Core/Modifiers/ScopedModifierContainer.cs`

- Added `fixed long activeTypeMask[8]` (64 bytes) to `ModifierSet` — tracks which of 512 types have values
- `Add()` and `Set()` set corresponding bit; `Clear()` zeros the mask
- Added `CopyActiveToSet(ref ModifierSet target)` using bit iteration (trailing zero count)
- `ScopedModifierContainer.RebuildIfDirty` now uses `CopyActiveToSet` instead of 512-type loop
- Cost reduction per rebuild: 512 iterations → ~3 active types = **~170x improvement**

### 3. Separate Dirty Flag Arrays (Avoid 8KB Struct Copy)
**Files Changed:** `Core/Modifiers/ModifierSystem.cs`

- Added `NativeArray<bool> provinceDirtyFlags` and `countryDirtyFlags`
- Kept in sync with `ScopedModifierContainer.isDirty` across all mutation methods
- `MarkCountryProvincesDirty` now sets only 1-byte flags, no 8KB struct read-modify-write
- `MarkAllDirty` skips province struct access entirely, only sets flag array

### 4. GetProvinceModifierFast with Unsafe Pointer Access
**Files Changed:** `Core/Modifiers/ModifierSystem.cs`, `Core/Modifiers/ScopedModifierContainer.cs`, `StarterKit/Systems/EconomySystem.cs`

- `EnsureCountryScopeClean(countryId)` — rebuild country scope once before batch queries
- `GetProvinceModifierFast()` — checks `provinceDirtyFlags[id]` (1-byte) before accessing struct:
  - **Clean (common case):** Unsafe pointer into NativeArray → `ApplyModifierFromCache()` — **zero struct copy**
  - **Dirty (rare):** Full struct copy + rebuild + write-back (same as before)
- `ScopedModifierContainer` gained `IsDirty` property and `ApplyModifierFromCache()`
- `EconomySystem.CalculateCountryIncome` calls `EnsureCountryScopeClean` once, then `GetProvinceModifierFast` per province

### 5. Prior Session Fixes (from context summary)
**Files Changed:** `StarterKit/Systems/AISystem.cs`, `StarterKit/Systems/EconomySystem.cs`, `StarterKit/Systems/ProvinceHistorySystem.cs`, `Core/Commands/CommandProcessor.cs`, `Core/Commands/ICommand.cs`, `StarterKit/Commands/ColonizeCommand.cs`

- AISystem: replaced `ProvinceQueryBuilder` allocations with pre-allocated `NativeList<ushort>` buffers
- EconomySystem: added pre-allocated `provinceBuffer` for `GetCountryProvinces`
- ProvinceHistorySystem: cached `TimeManager` reference (was `FindFirstObjectByType` per event)
- CommandProcessor: replaced reflection (`GetType().GetMethod()` + `method.Invoke()`) with `ICommandMessages` interface
- ColonizeCommand: removed `LogExecution` string allocation, direct field access instead of `GetComponent`

---

## Decisions Made

### Decision 1: Callback Pattern for Cross-Layer Lookup
**Context:** ModifierSystem (Core) needs province ownership data (ProvinceSystem) for `MarkCountryProvincesDirty`
**Options:**
1. Direct reference to ProvinceSystem — violates Core layer independence
2. Callback/delegate set after init — mechanism-only, no import dependency
3. Event-based — too complex for a sync query

**Decision:** Callback (Option 2)
**Rationale:** Engine provides mechanism (callback registration), game wires it. ProvinceSystem doesn't exist at ModifierSystem construction time anyway.

### Decision 2: Unsafe Pointer for Hot Path
**Context:** Even reading `NativeArray[i]` copies the entire 8KB struct
**Options:**
1. Restructure data to avoid large structs in NativeArrays — massive refactor
2. Unsafe pointer access for read-only fast path — targeted fix
3. Separate small query cache alongside NativeArray — memory overhead

**Decision:** Unsafe pointer (Option 2)
**Rationale:** `ScopedModifierContainer` is fully unmanaged (NativeArrays + fixed arrays + bool). Unsafe read is safe and zero-copy. Only used on clean provinces (common case).

### Decision 3: Separate Dirty Flag Arrays
**Context:** Checking `isDirty` required copying 8KB struct from NativeArray
**Options:**
1. Move `isDirty` out of struct — breaks encapsulation
2. Parallel `NativeArray<bool>` kept in sync — small overhead, clean separation
3. Bit-pack dirty flags — complex, minimal memory benefit over bool array

**Decision:** Parallel bool array (Option 2)
**Rationale:** 65KB for provinces + 4KB for countries is trivial. Keeping in sync requires discipline but all mutation goes through ModifierSystem methods.

---

## What Worked

1. **Profiler-driven investigation**
   - Deep profile breakdown pinpointed exact methods and call counts
   - Each fix targeted a measured bottleneck, not guesswork

2. **Bitmask for sparse iteration**
   - 512 modifier types but only ~3 active → bitmask skips 99.4% of iterations
   - Trailing zero count gives O(k) where k = active types

3. **Separate dirty tracking to avoid struct copies**
   - 1-byte dirty check vs 8KB struct copy = 8000x less memory traffic on fast path

---

## What Didn't Work

### 1. Frame-Batching AI Processing (Prior Session)
- **What we tried:** Processing 50 AI countries per frame instead of all 665 at once
- **Why it failed:** Total cost identical, just spread over more frames. User correctly identified: "did you actually fix anything or just spread stuff"
- **Lesson:** Batching hides latency, doesn't fix throughput. Fix the algorithmic cost first.

---

## Problems Encountered & Solutions

### Problem 1: 43M Struct Writes per Monthly Tick
**Symptom:** `TryBuildFarm` 3160ms — 9x slower than `TryColonize` despite simpler logic
**Root Cause:** `BuildingSystem.ApplyBuildingModifiers` → `AddCountryModifier` → `MarkCountryProvincesDirty` marked ALL 65k provinces dirty per farm build. ~665 AI countries × 65k = 43M struct writes.
**Solution:** Reverse index lookup via callback — only marks ~75 owned provinces per country.

### Problem 2: 800MB Struct Copying in Income Calculation
**Symptom:** `CalculateCountryIncome` 3463ms despite pre-allocated buffers
**Root Cause:** `GetProvinceModifier` copies 8KB `ScopedModifierContainer` from NativeArray on every call. 2 calls × ~50k provinces = 100k copies × 8KB = 800MB.
**Solution:** Separate dirty flag array + unsafe pointer read for clean provinces (zero copy).

### Problem 3: RebuildIfDirty Iterating 512 Types
**Symptom:** Even after dirty fix, `ApplyModifier` still 376ms
**Root Cause:** Parent scope copy loop iterated all 512 `MAX_MODIFIER_TYPES` slots. With ~50k province rebuilds × 512 = 25M iterations.
**Solution:** Bitmask tracking active types. `CopyActiveToSet` iterates only active types via trailing zero count.

---

## Architecture Impact

### New Anti-Pattern: Large Structs in NativeArray for Frequent Access
- **What not to do:** Store >1KB structs in NativeArray and access them in tight loops
- **Why it's bad:** Every `array[i]` copies the entire struct. 8KB × 100k accesses = 800MB traffic.
- **Rule:** For hot-path access, either use unsafe pointers or keep frequently-checked flags in a separate small array.

### Pattern Reinforced: Separate Dirty Tracking (Pattern 11 Extension)
- When dirty flag is inside a large struct, checking it costs as much as the full access
- Maintain parallel `NativeArray<bool>` for dirty state when struct size > ~64 bytes
- Keep in sync through the system's mutation API (single point of control)

---

## Code Quality Notes

### Performance
- **Before:** ~7000ms monthly tick (3500ms AI + 3500ms Economy)
- **After fix 1 (reverse index + bitmask):** ~600ms `GetProvinceModifier`
- **After fix 2 (dirty flags + unsafe + EnsureCountryScopeClean):** Improved, measuring ongoing
- **Target:** <100ms monthly tick

### Technical Debt
- `MarkAllDirty()` still does 8KB struct copy for 4096 countries (acceptable, rare operation)
- `GetProvinceModifier` (non-fast path) still copies structs — only `GetProvinceModifierFast` is optimized
- `ExpireModifiers` iterates all 65k provinces — should use sparse tracking (no temp modifiers currently)

---

## Next Session

### Immediate Next Steps
1. Profile remaining hotspots after current fixes — measure actual improvement
2. If still slow: consider moving `ModifierSet` out of `ScopedModifierContainer` into separate NativeArray for cache-friendly access
3. Verify province ownership changes still work correctly after all modifier changes

### Questions to Resolve
1. Is `GetProvinceModifierFast` sufficient or does the original `GetProvinceModifier` also need optimization?
2. Are there other callers of `GetProvinceModifier` outside `EconomySystem` that need the fast path?

---

## Quick Reference for Future Claude

**Key implementations:**
- Reverse index callback: `Core/Modifiers/ModifierSystem.cs` — `SetCountryProvincesLookup()`, `MarkCountryProvincesDirty()`
- Bitmask iteration: `Core/Modifiers/ModifierSet.cs` — `activeTypeMask`, `CopyActiveToSet()`
- Fast province query: `Core/Modifiers/ModifierSystem.cs` — `GetProvinceModifierFast()`, `EnsureCountryScopeClean()`
- Dirty flag arrays: `Core/Modifiers/ModifierSystem.cs` — `provinceDirtyFlags`, `countryDirtyFlags`
- Unsafe pointer access: `Core/Modifiers/ModifierSystem.cs:327` — `provinceScopes.GetUnsafeReadOnlyPtr()`
- Wiring: `Core/GameState.cs:209-211` — `Modifiers.SetCountryProvincesLookup()`

**Gotchas:**
- `ScopedModifierContainer` is ~8KB — NEVER access from NativeArray in tight loops without unsafe
- `provinceDirtyFlags` must be kept in sync with struct's `isDirty` across ALL mutation methods
- `GetProvinceModifierFast` slow path must call `MarkDirty()` on struct before `RebuildIfDirty` (flag array doesn't set struct's field)
- `MarkCountryProvincesDirty` with reverse index only sets flag array, NOT struct — `GetProvinceModifierFast` handles the sync on access

**Files changed this session:**
- `Core/Modifiers/ModifierSystem.cs` — reverse index callback, dirty flag arrays, fast query path, unsafe access
- `Core/Modifiers/ModifierSet.cs` — bitmask active type tracking, `CopyActiveToSet()`, `BitCount` helper
- `Core/Modifiers/ScopedModifierContainer.cs` — `IsDirty`, `ApplyModifierFromCache()`, bitmask-based `RebuildIfDirty`
- `Core/GameState.cs` — wiring `SetCountryProvincesLookup` after province init
- `StarterKit/Systems/EconomySystem.cs` — `EnsureCountryScopeClean` + `GetProvinceModifierFast` in income calc
- `StarterKit/Systems/AISystem.cs` — pre-allocated NativeList buffers (prior session, carried forward)
- `StarterKit/Systems/ProvinceHistorySystem.cs` — cached TimeManager (prior session)
- `Core/Commands/CommandProcessor.cs` — ICommandMessages interface (prior session)
- `Core/Commands/ICommand.cs` — ICommandMessages interface (prior session)

---

## Related Sessions
- [Session 2 — Semaphore Fix](2-semaphore-waitforsignal-fix.md) — GPU sync stall
- [Session 1 — Runtime Performance](1-runtime-performance-optimization.md) — reverse index, initial fixes
