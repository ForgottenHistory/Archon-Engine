# Hot Reload System for Data Overrides
**Date**: 2026-04-16
**Session**: 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement hot reload: re-run the data loading pipeline at runtime without restarting play mode

**Success Criteria:**
- "Reset to Original" in terrain editor deletes overrides and hot reloads all data live
- Registries, province data, terrain, GPU textures all refresh correctly
- No gray tiles, no stale rendering

---

## Context & Background

**Previous Work:**
- See: [2-remove-game-fields-from-engine-add-passable.md](2-remove-game-fields-from-engine-add-passable.md)
- Terrain editor could save data but resetting required restarting play mode
- Manual property-by-property patching didn't scale

**Why Now:**
- Every new feature would need its own reset logic without hot reload
- The initialization pipeline was already phase-based with independent, re-runnable phases

---

## What We Did

### 1. Registry Clear Methods
**Files:** `Core/Registries/Registry.cs`, `ProvinceRegistry.cs`, `CountryRegistry.cs`, `GameRegistries.cs`

Added `Clear()` to all registries. Resets items list to just the null sentinel at index 0, clears all lookup dictionaries. `GameRegistries.Clear()` clears all sub-registries.

### 2. ProvinceSystem.ClearForReload()
**File:** `Core/Systems/ProvinceSystem.cs`

Public method that calls `dataManager.Clear()` — zeroes province state, clears ID mappings. Existing method, just exposed publicly.

### 3. DataReloadManager
**File:** `Engine/DataReloadManager.cs` (NEW)

Static utility that orchestrates hot reload as a coroutine:
1. Clear registries (`GameRegistries.Clear()`)
2. Clear province system (`ClearForReload()`)
3. Re-run `StaticDataLoadingPhase` (terrains, buildings)
4. Re-run `ProvinceDataLoadingPhase` (definitions + history)
5. Re-run `CountryDataLoadingPhase`
6. Create fresh `ReferenceResolver`, `CrossReferenceBuilder`, `DataValidator`
7. Re-run `ReferenceLinkingPhase`
8. Reload terrain data + rebind `_ProvinceTerrainBuffer` to material
9. Repopulate owner texture
10. Sync double-buffered province state
11. Refresh map mode palettes + rebind textures + force update

Lives in `Engine` namespace (not `Core`) because it coordinates both Core and Map systems.

### 4. ArchonEngine.ReloadData()
**File:** `Engine/ArchonEngine.cs`

Public method returning `Coroutine` — delegates to `DataReloadManager`. Callers can `yield return` to wait for completion.

### 5. MapSystemCoordinator.ReloadTerrainData()
**File:** `Map/Core/MapSystemCoordinator.cs`

Re-runs terrain analysis, rebinds the new `_ProvinceTerrainBuffer` to material, repopulates owner texture.

### 6. Terrain Editor Integration
**File:** `StarterKit/UI/ProvinceTerrainEditorUI.cs`

"Reset to Original" now: deletes override directory → `yield return engine.ReloadData()` → rebuilds terrain entries + palette from fresh registry.

---

## Decisions Made

### Decision 1: DataReloadManager in Engine, not Core
**Context:** Needs to coordinate both Core (registries, loading phases) and Map (terrain, textures) systems
**Decision:** Engine namespace — follows import rules (Core→nothing, Map→Core, Engine→Core+Map)

### Decision 2: Re-run existing phases, don't write custom reload logic
**Context:** Could have written bespoke reload code or reused init phases
**Decision:** Reuse `IInitializationPhase` implementations directly
**Rationale:** Same code path as startup = fewer bugs. Phases are already designed to be independent and idempotent (they call `Clear()` internally).

---

## Problems Encountered & Solutions

### Problem 1: ReferenceResolver null during reload
**Symptom:** NullReferenceException at `ReferenceLinkingPhase.cs:169`
**Root Cause:** `DataReloadManager` didn't create linking systems (`ReferenceResolver`, `CrossReferenceBuilder`, `DataValidator`). These are normally created by `CoreSystemsInitializationPhase` which we skipped.
**Solution:** Create them after country loading (when registries are populated), before reference linking.

### Problem 2: Terrain entries empty after reload
**Symptom:** "Terrain: Unknown" for all provinces in editor UI
**Root Cause:** `RebuildAfterReload()` used `yield return null` × 2 (frame delay) which executed before the reload coroutine finished.
**Solution:** `yield return engine.ReloadData()` — wait for the actual coroutine, not arbitrary frames.

### Problem 3: All provinces render as ocean blue after reload
**Symptom:** Terrain shader shows ocean (terrain 0) for everything despite correct data in logs
**Root Cause:** `AnalyzeProvinceTerrainAfterMapInit` creates a NEW `ComputeBuffer` for `_ProvinceTerrainBuffer`, but the material still references the old (disposed) buffer.
**Solution:** After terrain analysis, rebind: `material.SetBuffer("_ProvinceTerrainBuffer", terrainBuffer)` in `ReloadTerrainData()`.

**Pattern:** When GPU resources (ComputeBuffers, RenderTextures) are recreated, the material binding must be refreshed. The material holds a reference to the old object.

### Problem 4: IsPassable always true
**Symptom:** Passable toggle didn't work — always showed "Yes"
**Root Cause:** `ProvinceQueries` was constructed before `GameRegistries` was populated (during `GameState.Initialize()`). The `provinceRegistry` parameter was null.
**Solution:** Lazy lookup via `GameState.Instance?.Registries?.Provinces` fallback.

---

## Architecture Impact

### New Engine Capabilities
- **Registry.Clear()** — all registries can be reset and repopulated
- **ProvinceSystem.ClearForReload()** — province hot data can be cleared
- **DataReloadManager** — orchestrates full data pipeline re-execution at runtime
- **ArchonEngine.ReloadData()** — public API for hot reload

### Hot Reload Pipeline
```
Clear registries → Clear provinces → Load static data → Load provinces →
Load countries → Create linkers → Link references → Analyze terrain →
Rebind GPU buffers → Sync double buffers → Refresh palettes → Rebind material
```

### Critical Pattern: GPU Resource Rebinding
When ComputeBuffers or RenderTextures are recreated during reload, `material.SetBuffer()` / `material.SetTexture()` MUST be called again. The material caches the old reference.

---

## Next Session

### Immediate Next Steps
1. Hegemon game layer: implement own json5 parsing for game-specific fields
2. Apply auto-classified terrain to Hegemon's impassable provinces
3. Test hot reload with terrain color changes + province painting together

---

## Session Statistics

**Files Created:** 1 (DataReloadManager)
**Files Changed:** 8 (registries, ProvinceSystem, MapSystemCoordinator, ArchonEngine, terrain editor)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- `ArchonEngine.ReloadData()` returns a `Coroutine` — yield on it to wait for completion
- DataReloadManager lives in `Engine` namespace (not Core) due to import rules
- After reload, `_ProvinceTerrainBuffer` must be rebound to material — new ComputeBuffer object
- `ProvinceQueries.IsPassable()` uses lazy registry lookup via `GameState.Instance`
- Linking systems (ReferenceResolver, etc.) must be created fresh after registries are repopulated

**Gotchas:**
- `Registry.Clear()` re-adds null at index 0 — IDs restart from 1 on next register
- Double-buffer sync (`SyncBuffersAfterLoad()`) required after reload for UI to see fresh data
- `Assembly.GetExecutingAssembly()` in phases returns Core assembly even when called from Engine coroutine — this is correct

---

*Session logged: 2026-04-16*
