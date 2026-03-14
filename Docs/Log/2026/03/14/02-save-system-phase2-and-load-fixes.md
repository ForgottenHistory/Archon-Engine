# Save System Phase 2 + Post-Load Bug Fixes
**Date**: 2026-03-14
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement save system Phase 2: metadata header + compression

**Secondary Objectives:**
- Fix all post-load bugs discovered during testing

**Success Criteria:**
- Fast save browsing without full deserialization
- Compressed save files
- Backward compatible with v1 saves
- Game fully functional after load (AI, economy, map modes)

---

## Context & Background

**Previous Work:**
- See: [01-border-terrain-height-matching.md](01-border-terrain-height-matching.md)
- Save system Phase 1 complete (checksum, tick fix)
- Planning doc: `Docs/Planning/save-system-improvements.md`

**Current State:**
- Save/load functional but no quick preview, no compression
- Multiple post-load bugs: broken economy, dead AI, stale map modes

---

## What We Did

### 1. Save Metadata Header (256 bytes fixed-size)
**Files Changed:** `SaveGameData.cs`, `SaveFileSerializer.cs`, `SaveManager.cs`

- `SaveMetadata` struct: saveName (32 chars), scenarioName (32 chars), gameVersion (16 chars), dateTicks, currentTick, gameSpeed, saveFormatVersion, provinceCount, countryCount, compressedPayloadSize, flags
- Fixed-size UTF-16 strings with null-padding for deterministic layout
- `SaveFileSerializer.ReadSaveMetadata(path)` reads only ~264 bytes
- `SaveManager.GetSaveFileMetadata()` returns metadata for all save files

### 2. GZip Compression
**Files Changed:** `SaveFileSerializer.cs`

- Payload (system data + command log) compressed with `GZipStream` at `CompressionLevel.Fastest`
- Metadata header remains uncompressed for fast reading
- Result: 1.18MB → 29KB (~97% reduction)

### 3. Format Version Discrimination
**Files Changed:** `SaveFileSerializer.cs`

- Format version marker 1001+ distinguishes v2 from v1 legacy
- v1 writes gameVersion string length as first int32 after magic (always small number)
- v1001+ = new format, <1000 = legacy format
- Full backward compatibility: v1 saves load correctly

### 4. provincesByOwner Reverse Index Rebuild (CRITICAL BUG)
**Files Changed:** `ProvinceDataManager.cs`, `ProvinceSystem.cs`

**Root Cause:** `LoadState()` calls `dataManager.Clear()` which clears `provincesByOwner`, then writes province states directly to NativeArray via `ReadNativeArray`. The reverse lookup is never rebuilt — `GetCountryProvinces()` returns empty for all countries.

**Impact:** Economy income +0, AI does nothing, any query for "which provinces does country X own" returns empty.

**Fix:** Added `ProvinceDataManager.RebuildOwnerIndex()` — iterates all active provinces from the read buffer and rebuilds `provincesByOwner`. Called at end of `ProvinceSystem.LoadState()`.

### 5. Economy Income Cache Invalidation
**Files Changed:** `StarterKit/Systems/EconomySystem.cs`

**Root Cause:** `EconomySystem.Deserialize()` restores gold balances but income cache still has pre-load values. Next monthly tick uses stale cached income.

**Fix:** Call `InvalidateAllIncome()` inside `Deserialize()`.

### 6. Province History Clear on Load
**Files Changed:** `StarterKit/Systems/ProvinceHistorySystem.cs`, `StarterKit/Initializer.cs`

**Root Cause:** Cold data dictionary retains ownership history from previous session.

**Fix:** Added `ClearHistory()` method, called in post-load finalization.

### 7. Map Mode Refresh on Load
**Files Changed:** `MapSystemCoordinator.cs`, `MapModeManager.cs`

**Root Cause:** Map mode textures (country color palette, gradient modes) not refreshed after load. Political mode shows wrong colors, gradient modes show stale data.

**Fix:** Added `MapModeManager.InvalidateAllMapModes()` — marks all gradient modes dirty. Called in `RefreshAllVisuals()` before re-activating current mode via `SetMapMode(currentMode, forceUpdate: true)`.

### 8. Stale Gradient Map Mode Colors
**Files Changed:** `GradientMapMode.cs`

**Root Cause:** `UpdateTextures` only writes provinces with valid values (>= 0). Provinces that became unowned after load still have their old color in the palette texture.

**Fix:** Explicitly write `UnownedColor` for provinces returning value < 0. Moved `provinceColors.Clear()` before the first pass so both owned and unowned entries are written.

### 9. Auto-Pause on Load
**Files Changed:** `StarterKit/Initializer.cs`

**Fix:** Call `timeManager.PauseTime()` in post-load finalization.

---

## Decisions Made

### Decision 1: Format Version Marker 1001+
**Context:** Need to distinguish v2 from v1 without breaking legacy saves
**Decision:** Use 1001 as format version (>= 1000 = new format)
**Rationale:** v1 writes gameVersion string length as first int32 after magic — always a small number. 1001+ can never collide.

### Decision 2: RebuildOwnerIndex vs Incremental Updates
**Context:** provincesByOwner empty after load
**Decision:** Full rebuild from read buffer after SyncBuffersAfterLoad
**Rationale:** Load replaces all province data — incremental updates not possible. Full rebuild is O(n) provinces, runs once.

### Decision 3: Explicit UnownedColor in Gradient Modes
**Context:** Stale province colors in palette texture after load
**Decision:** Write UnownedColor for provinces returning value < 0 instead of skipping
**Rationale:** SetPixel only overwrites what's provided — must explicitly clear stale entries.

---

## What Worked

1. **GZip compression** — 97% size reduction with minimal CPU overhead
2. **Fixed-size metadata header** — enables fast save browsing without full deserialization
3. **Format version marker** — clean v1/v2 discrimination without ambiguity

---

## What Didn't Work

1. **Assuming save/load "just works"** — Province system loads data into NativeArray but doesn't rebuild derived data structures (reverse index). Multiple systems had stale caches after load.
   - Lesson: Every system with derived/cached data needs explicit post-load invalidation

---

## Problems Encountered & Solutions

### Problem 1: +0 income after load
**Root Cause:** `provincesByOwner` reverse index empty → `GetCountryProvinces()` returns nothing → income = 0
**Solution:** `RebuildOwnerIndex()` after `LoadState()`

### Problem 2: AI does nothing after load
**Root Cause:** Same as #1 — AI queries owned provinces, gets empty list
**Solution:** Same fix (RebuildOwnerIndex)

### Problem 3: Wrong country colors after load
**Root Cause:** Country color palette texture not refreshed
**Solution:** `SetMapMode(currentMode, forceUpdate: true)` in RefreshAllVisuals

### Problem 4: Farm map mode shows stale ownership
**Root Cause:** Gradient mode skips unowned provinces (value < 0), leaving old colors in texture
**Solution:** Explicitly write UnownedColor for skipped provinces

### Problem 5: `CompressionLevel` ambiguous reference
**Root Cause:** Both `UnityEngine.CompressionLevel` and `System.IO.Compression.CompressionLevel` exist
**Solution:** Fully qualify `System.IO.Compression.CompressionLevel.Fastest`

---

## Quick Reference for Future Claude

**Save File Format v2:**
```
[Magic: "HGSV" 4 bytes]
[Format version: 1001 int32]
[Metadata: 256 bytes fixed — SaveMetadata struct]
[GZip compressed payload — system data + command log]
[Checksum: uint32 CRC32 of compressed payload]
```

**Post-Load Checklist (things that need refreshing):**
- ProvinceSystem: `RebuildOwnerIndex()` ← CRITICAL, done automatically in LoadState
- EconomySystem: `InvalidateAllIncome()` ← done in Deserialize
- ProvinceHistorySystem: `ClearHistory()` ← done in post-load finalization
- MapModeManager: `InvalidateAllMapModes()` + `SetMapMode(current, forceUpdate)` ← done in RefreshAllVisuals
- TimeManager: `PauseTime()` ← done in post-load finalization
- Any system with cached/derived data needs explicit invalidation after load

**Key APIs:**
- `SaveFileSerializer.ReadSaveMetadata(path)` — reads ~264 bytes, returns `SaveMetadata?`
- `SaveManager.GetSaveFileMetadata()` — metadata for all saves
- `MapModeManager.InvalidateAllMapModes()` — marks all gradient modes dirty
- `ProvinceDataManager.RebuildOwnerIndex()` — rebuilds provincesByOwner from loaded state

### Code References
- Metadata struct: `Core/SaveLoad/SaveGameData.cs:SaveMetadata`
- Serializer: `Core/SaveLoad/SaveFileSerializer.cs`
- Reverse index rebuild: `Core/Systems/Province/ProvinceDataManager.cs:RebuildOwnerIndex()`
- Map mode invalidation: `Map/MapModes/MapModeManager.cs:InvalidateAllMapModes()`
- Gradient stale fix: `Map/MapModes/GradientMapMode.cs:CalculateProvinceColors()`
- StarterKit post-load: `StarterKit/Initializer.cs:SetupSaveManager()`

---

## Next Session

### Immediate Next Steps
1. Phase 3: Async save/load (background thread, progress reporting)
2. Phase 4: Version migration (`ISaveMigrator` chain)
3. Autosave system (configurable interval, rolling saves)

### Related Sessions
- [01-border-terrain-height-matching.md](01-border-terrain-height-matching.md)
- Planning: `Docs/Planning/save-system-improvements.md`
