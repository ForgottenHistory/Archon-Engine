# ModifierSystem Monthly Tick Performance (Continued)
**Date**: 2026-02-03
**Session**: 4
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Eliminate remaining ~377ms in `GetProvinceModifierFast` and ~3400ms in `MarkCountryProvincesDirty`

**Success Criteria:**
- Monthly tick runs without late-game stall (user ran past previous stuck point)

---

## Context & Background

**Previous Work:**
- See: [Session 3 — ModifierSystem Performance](3-modifier-system-performance.md) — bitmask, dirty flags, unsafe pointers, reverse index
- Session 3 reduced 7000ms → ~377ms `GetProvinceModifierFast` + ~3400ms `MarkCountryProvincesDirty`

**Current State (start of session):**
- `GetProvinceModifierFast`: 377ms total, 299ms in `ApplyModifier`, 277ms in `RebuildIfDirty`
- `MarkCountryProvincesDirty`: 3400ms, 2917ms in `NativeArray<bool>.set_Item`

**Root Cause Chain:**
1. `RebuildIfDirty` slow path: `cachedModifierSet.Clear()` zeroes 8KB per province rebuild (~50k provinces × 8KB = 400MB zeroing)
2. `RebuildIfDirty` slow path: country scope passed as `ScopedModifierContainer?` nullable = 8KB copy per province
3. `MarkCountryProvincesDirty`: still O(k) per country change (665 × ~75 = ~50k `NativeArray<bool>.set_Item` calls with bounds checking)
4. `ApplyModifier`: goes through `Get()` → `ModifierValue` → `Apply()` intermediary structs unnecessarily

---

## What We Did

### 1. Generation-Based Lazy Province Invalidation
**Files Changed:** `Core/Modifiers/ModifierSystem.cs`

- Added `NativeArray<uint> countryGeneration` — increments when any country modifier changes (O(1))
- Added `NativeArray<uint> provinceLastCountryGeneration` — stores the country gen each province was last built against
- Added `uint globalGeneration` — increments on global modifier changes
- `GetProvinceModifierFast` compares `provinceLastCountryGeneration[id] != countryGeneration[countryId]` to detect stale provinces
- **Eliminated `MarkCountryProvincesDirty` entirely** — no per-province writes when country modifiers change
- `AddCountryModifier` now just increments `countryGeneration[countryId]` — single `NativeArray<uint>` write
- `SetCountryProvincesLookup` kept as no-op for API compatibility
- Cost reduction: O(k) per country change → **O(1)**

### 2. Full Unsafe Pointer Slow Path
**Files Changed:** `Core/Modifiers/ModifierSystem.cs`, `Core/Modifiers/ScopedModifierContainer.cs`

- `GetProvinceModifierFast` slow path now uses `provinceScopes.GetUnsafePtr()` — writes directly to NativeArray via pointer, no 8KB copy + write-back
- Country scope accessed via `countryScopes.GetUnsafeReadOnlyPtr()` — no 8KB nullable copy
- Added `RebuildIfDirtyFromParentPtr(ScopedModifierContainer*)` — takes unsafe pointer to parent, avoids nullable boxing
- Eliminates: ~50k × 8KB province copy + ~50k × 8KB country nullable copy = **~800MB** memory traffic

### 3. ClearActive() for Sparse Cache Reset
**Files Changed:** `Core/Modifiers/ModifierSet.cs`

- Added `ClearActive()` — zeros only modifier types tracked in bitmask, then clears mask
- Typically 2-5 types × 2 longs = 10-20 bytes vs 8KB full clear
- `RebuildIfDirtyFromParentPtr` uses `ClearActive()` instead of `Clear()`
- Cost reduction per rebuild: 8KB zeroing → ~20 bytes = **~400x improvement**

### 4. Optimized Clear() and ApplyModifier
**Files Changed:** `Core/Modifiers/ModifierSet.cs`

- `Clear()` now uses `UnsafeUtility.MemClear` for bulk zeroing (single call vs 1024-iteration loop)
- `ApplyModifier` inlined: reads raw longs directly, early-outs when both additive and multiplicative are zero, skips multiplication when no multiplicative modifier

---

## Decisions Made

### Decision 1: Generation Counters over Per-Province Dirty Marking
**Context:** `MarkCountryProvincesDirty` wrote `NativeArray<bool>` per owned province per country modifier change. Even with reverse index (Session 3), 665 countries × ~75 provinces = ~50k writes with bounds checking = 3400ms.
**Options:**
1. Optimize NativeArray write (unsafe bulk set) — still O(k) writes per change
2. Generation counter per country + last-seen-gen per province — O(1) per change, lazy check on read
3. Batch dirty marking to once per tick — still O(k), just less frequent

**Decision:** Generation counter (Option 2)
**Rationale:** Moves cost from write-time (every modifier change) to read-time (only when province is actually queried). Most provinces are never queried between changes. O(1) per modifier change regardless of province count.

### Decision 2: Separate Rebuild Method for Unsafe Parent
**Context:** `RebuildIfDirty(ScopedModifierContainer?)` copies 8KB via nullable on every call
**Options:**
1. Change existing method signature — breaks all callers
2. Add `RebuildIfDirtyFromParentPtr` overload — hot path only, existing API unchanged
3. Template/generic approach — C# doesn't support this well for structs

**Decision:** New overload (Option 2)
**Rationale:** Hot path (`GetProvinceModifierFast`) uses the unsafe overload. Non-hot-path callers (`GetProvinceModifier`, tooltips) keep the safe API.

---

## What Worked

1. **Generation-based lazy invalidation**
   - Eliminates O(k) write amplification entirely
   - Cost shifts to read-time where it's amortized across queries
   - Pattern: "don't push dirty state, let readers pull freshness"

2. **Full unsafe pointer chain**
   - Province read + write + country read all via pointers = zero struct copies in hot path
   - Rebuilds happen in-place in the NativeArray

3. **Sparse clear (ClearActive)**
   - 8KB → ~20 bytes per province clear when only 2-5 modifier types active
   - Bitmask from Session 3 enables this naturally

---

## What Didn't Work

### 1. Reverse Index for MarkCountryProvincesDirty (Session 3)
- **What we tried:** Using province ownership reverse lookup to mark only owned provinces dirty
- **Why it wasn't enough:** Even marking ~75 provinces per country × 665 countries = ~50k NativeArray writes. `NativeArray<bool>.set_Item` bounds checking overhead made this 3400ms.
- **Lesson:** O(k) is still too expensive when k × frequency is large. O(1) write + lazy read is the correct pattern for high-frequency modifier changes.

---

## Problems Encountered & Solutions

### Problem 1: 3400ms in MarkCountryProvincesDirty
**Symptom:** `TryBuildFarm → AddCountryModifier → MarkCountryProvincesDirty`: 3400ms, 2917ms in `NativeArray.set_Item`
**Root Cause:** Even with reverse index, ~50k `NativeArray<bool>` writes per monthly tick. Bounds-checked indexer overhead dominates.
**Solution:** Generation counters eliminate the method entirely. Country modifier change = increment one uint. Province detects staleness on read.

### Problem 2: 277ms in RebuildIfDirty
**Symptom:** `GetProvinceModifierFast` → `ApplyModifier` → `RebuildIfDirty`: 277ms
**Root Cause:** Three costs per dirty province rebuild:
  1. `cachedModifierSet.Clear()` zeroes 8KB (512×2 longs + mask)
  2. Country scope passed as nullable = 8KB copy
  3. Province scope copied from NativeArray = 8KB copy + write-back
**Solution:** `ClearActive()` (sparse clear), unsafe parent pointer (zero copy), unsafe province pointer (in-place rebuild)

---

## Architecture Impact

### New Pattern: Generation-Based Lazy Invalidation (Pattern 11 Extension)
- **When to use:** Parent-child relationships where parent changes should invalidate children, but children are queried much less frequently than parents change
- **Mechanism:** Parent has generation counter (uint, incremented on change). Child stores last-seen parent generation. On child query, compare generations — mismatch means rebuild needed.
- **Benefits:** O(1) per parent change regardless of child count. Cost deferred to query time.
- **Trade-off:** Slight overhead on every child query (uint comparison). Worth it when parent changes >> child queries.

### Pattern Reinforced: Unsafe Pointers for Large Struct NativeArrays
- Session 3 used unsafe for read-only fast path
- Session 4 extends to read-write (slow path rebuild) via `GetUnsafePtr()`
- Rule: If struct > ~64 bytes and accessed in loops, always use unsafe pointers

---

## Code Quality Notes

### Performance
- **Before (Session 3 end):** ~3400ms `MarkCountryProvincesDirty` + ~377ms `GetProvinceModifierFast`
- **After Session 4:** User reports game runs past previous stuck point (monthly tick no longer stalls)
- **Target:** <100ms monthly tick — appears met based on user testing

### Technical Debt
- `SetCountryProvincesLookup` is now a no-op — could be removed along with `GameState` wiring, but kept for API stability
- `GetProvinceModifier` (non-fast path) still copies structs — acceptable for non-hot-path callers (tooltips, etc.)
- `MarkAllDirty()` still does 8KB struct copy for 4096 countries — acceptable, only called on global modifier change (rare)
- `RebuildIfDirty` (non-ptr overload) still uses `Clear()` not `ClearActive()` — only used by non-hot-path

---

## Next Session

### Immediate Next Steps
1. Profile monthly tick with deep profiler to confirm <100ms target
2. Look for other hotspots now that ModifierSystem is resolved
3. Consider removing dead code (`SetCountryProvincesLookup` no-op, reverse index callback fields)

### Questions to Resolve
1. Are there other systems calling `GetProvinceModifier` (non-fast) that should migrate to `GetProvinceModifierFast`?
2. Should `MarkAllDirty` also use generation bumps instead of per-province flag writes?

---

## Quick Reference for Future Claude

**Key implementations:**
- Generation counters: `Core/Modifiers/ModifierSystem.cs` — `countryGeneration`, `provinceLastCountryGeneration`, `globalGeneration`
- Unsafe rebuild: `Core/Modifiers/ScopedModifierContainer.cs` — `RebuildIfDirtyFromParentPtr(ScopedModifierContainer*)`
- Sparse clear: `Core/Modifiers/ModifierSet.cs` — `ClearActive()` (uses bitmask to zero only active types)
- Inlined apply: `Core/Modifiers/ModifierSet.cs` — `ApplyModifier()` reads raw longs, early-out on zero
- Bulk clear: `Core/Modifiers/ModifierSet.cs` — `Clear()` uses `UnsafeUtility.MemClear`
- Full unsafe fast path: `Core/Modifiers/ModifierSystem.cs` — `GetProvinceModifierFast()` uses `GetUnsafePtr()` for both read and write

**Gotchas:**
- `SetCountryProvincesLookup` is now a no-op — generation counters replaced `MarkCountryProvincesDirty`
- `provinceLastCountryGeneration` must be updated after province rebuild in `GetProvinceModifierFast`
- `countryGeneration` must be incremented in ALL country mutation methods: `AddCountryModifier`, `RemoveCountryModifiersBySource`, `ClearCountryModifiers`, `ExpireModifiers`, `LoadState`, `MarkAllDirty`
- `RebuildIfDirtyFromParentPtr` assumes parent is already clean — always call `EnsureCountryScopeClean` first
- `ClearActive()` relies on bitmask accuracy — if bitmask gets out of sync, stale values remain

**Files changed this session:**
- `Core/Modifiers/ModifierSystem.cs` — generation counters, eliminated `MarkCountryProvincesDirty`, full unsafe slow path
- `Core/Modifiers/ModifierSet.cs` — `ClearActive()`, `UnsafeUtility.MemClear` in `Clear()`, inlined `ApplyModifier`
- `Core/Modifiers/ScopedModifierContainer.cs` — `RebuildIfDirtyFromParentPtr(ScopedModifierContainer*)`

---

## Related Sessions
- [Session 3 — ModifierSystem Performance](3-modifier-system-performance.md) — bitmask, dirty flags, unsafe read path, reverse index
- [Session 2 — Semaphore Fix](2-semaphore-waitforsignal-fix.md) — GPU sync stall
- [Session 1 — Runtime Performance](1-runtime-performance-optimization.md) — initial fixes
