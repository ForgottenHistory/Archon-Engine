# Unit Badges: Broken Shader Reference and Province Centers
**Date**: 2026-11-20
**Session**: 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- A game spawned ~1,100 units at scenario start; the StarterKit unit badges showed none

**Success Criteria:**
- Badges render for units that exist before `UnitVisualization` initializes

---

## Problems Encountered & Solutions

### Problem 1: Badge material rendered with the error shader
**Symptom:** log `UnitVisualization: Created material with shader 'Hidden/InternalErrorShader'`, nothing drawn.
**Root Cause:** `InstancedAtlasBadge.shader` moved from `Shaders/Instancing/` to `Resources/Shaders/`
(commit "Added missing resources") and got a new GUID in its new `.meta`.
`InstancedAtlasBadgeMaterial.mat` still referenced a GUID that no file has.
**Solution:**
- Material points at the current shader GUID.
- `UnitVisualization` falls back to `Shader.Find("Engine/InstancedAtlasBadge")` (with a warning)
  when the material's shader is missing, unsupported or the error shader.

**Pattern for Future:** moving an asset by deleting and re-adding it (instead of moving it with its
`.meta`) changes its GUID and silently breaks every reference to it. Move `.meta` files along.

### Problem 2: Units created before the visualizer were never tracked
**Symptom:** `RebuildMatrices called, 0 tracked units`.
**Root Cause:** `UnitVisualization` only learned about units from `UnitCreatedEvent`, and it
initializes after country selection. Units from a scenario start (or a loaded save) were created
earlier.
**Solution:** new `Core.Units.UnitSystem.GetAllUnits()`; `UnitVisualization.Initialize` seeds its
tracked units from it before following events.

### Problem 3: ProvinceCenterLookup returned no centers
**Symptom:** `Failed to get center for province N` for every province.
**Root Cause:** `ProvinceCenterLookup` averaged `ProvinceMapping.GetProvincePixels()`. The map
loading optimization (commit ac2db13, GPU province texture + raw pixel cache) stopped filling those
per-province pixel lists (`AddPixelToProvince` has no callers). Every list is empty, so no lookup
user (unit badges, unit path lines) has worked since.
**Solution:** `ProvinceCenterLookup` takes the `MapTextureManager` and builds all centers once from
`ProvinceIdReadback` (the readback map labels use): one pass for center of mass and pixel count
per province; a second pass, only for provinces whose center of mass lies outside them (concave
shapes), moves the center to the province's nearest pixel. Pixel lists remain a fallback.

**Pattern for Future:** when an optimization removes a data structure, grep its readers.
`ProvinceMapping` is a "compatibility shim" whose pixel API still exists but is always empty.

### Problem 4: Per-unit console logging
`Core.Units.UnitSystem.CreateUnit` / `DisbandUnit` logged every unit with `Debug.Log`; ~1,100 console
lines at scenario start. Moved to `ArchonLogger` (`core_simulation`).

---

## Quick Reference for Future Claude

- Province centers: `ProvinceCenterLookup.TryGetProvinceCenter` / `TryGetPixelCenter`
  (map pixels, row 0 = top). Pass the texture manager to `Initialize`.
- `UnitPathRenderer.Initialize(gameState, provinceMapping, ...)` still builds a lookup without a
  texture manager (pixel-list fallback, so no centers); the StarterKit passes its shared lookup.
- `ProvinceMapping.GetProvincePixels` is always empty with the current loader.
