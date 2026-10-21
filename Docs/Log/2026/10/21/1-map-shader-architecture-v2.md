# Map Shader Architecture v2
**Date**: 2026-10-21
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Reflect on the map shader system against the HOI4 shader decompile findings, and improve it

**Secondary Objectives:**
- Remove dead/legacy shader code and ENGINE→GAME references
- Make the map look moddable in shipped builds

**Success Criteria:**
- GAME map shaders no longer copy ENGINE pass code
- Engine/shader contract defined once, with errors instead of silent zeros
- No visual change in existing games

---

## Context & Background

**Previous Work:**
- Learnings: [hoi4-shader-system-notes.md](../../../learnings/hoi4-shader-system-notes.md)
- Superseded: [shader-copy-and-customize-architecture.md](../../../decisions/shader-copy-and-customize-architecture.md)

**Current State (before):**
- GAME copied the Default `.shader` (~300 lines); only one include differed from ENGINE
- Contract in three places: `UnityPerMaterial` CBUFFER, every copied Properties block, ~335 `material.SetX` calls. Missing property = silent 0
- Default terrain shader referenced GAME (`FallBack`, `CustomEditor`); legacy MapCore shaders included GAME paths
- Compositor (blend modes per layer) existed in C# + docs but no shader included it
- Styles were ScriptableObjects only (not moddable in builds)

---

## What We Did

### 1. Phase 1 — Cleanup
**Files:** `Shaders/` (deleted `MapCore`, `MapCoreTessellated`, `MapFallback`, root `MapModeCommon.hlsl`, `Includes/Compositing.hlsl`), `Scripts/Map/Rendering/Compositing/` (deleted), `MapRendererRegistry.cs`, `VisualStyleConfiguration.cs`, `IMapModeHandler.cs`, `GradientMapMode.cs`, Default shaders
- Removed GAME references from `DefaultTerrainMapShader` (fallback → `Archon/DefaultFlat`)
- Deleted unwired compositor stack and dead `MAP_MODE_*` keyword helpers
- Renamed passes `ArchonFlat` / `ArchonTerrain`
- Verified unused via GUID search in `.mat/.unity/.prefab/.asset` + `Shader.Find`/registration grep

### 2. Phase 2 — Engine values as shader globals
**Files:** `Scripts/Map/Rendering/MapShaderGlobals.cs` (new), `Shaders/Includes/DefaultCommon.hlsl`, texture sets (`Core/Visual/DynamicTextureSet`, `PaletteTextureManager`, `MapModeDataTextures`), `MapTextureManager.cs`, `MapModeManager.cs`, `VisualStyleManager.cs`, `MapSystemCoordinator.cs`, image loaders, `FogOfWarSystem.cs`, `BaseCameraController.cs`, `BorderComputeDispatcher.cs`
- `BindToMaterial(material)` → `BindGlobals()`; `BindTexturesToMaterial` → `BindGlobalTextures()`
- `DefaultCommon.hlsl` CBUFFER reduced to per-material values + `ARCHON_GAME_MATERIAL_PROPERTIES` hook
- Properties blocks stripped of engine values (~70 → 12-16 entries)
- `SetDefaults()` replaces Properties defaults (fallback textures, style defaults from `VisualStyleConfiguration`)

### 3. Phase 3 — Engine-owned render pass
**Files:** `Shaders/Includes/MapShaderSetup.hlsl`, `MapFragment.hlsl`, `MapPassFlat.hlsl`, `MapPassTerrain.hlsl` (new); Default shaders → thin shells
- Shell = Properties + render state + setup / dispatcher / `#include_with_pragmas` pass
- Entry points and structs prefixed `ArchonMap*`

### 4. Phase 4 — Per-stage override hooks
**Files:** `Shaders/Includes/MapContext.hlsl` (new), `MapFragment.hlsl`
- `ARCHON_OVERRIDE_OCEAN/_LIGHTING/_BORDERS/_HIGHLIGHTS/_FOG/_OVERLAY`, optional `ARCHON_POST_COMPOSITE`; `ArchonMapContext` (uv, positionWS, provinceID, mapMode)

### 5. Phase 5 — Contract registry + validation
**Files:** `Scripts/Map/Rendering/ShaderParameters/` (new: `MapShaderParameter`, `MapShaderParameterRegistry`, `ShaderParameterOverride`), `MapShaderGlobals.cs`, `VisualStyleManager.cs`, renderer interfaces
- One registered list (name, type, Global/Material, default, required, style-settable); `SetDefaults` is a loop
- `ValidateMaterial` on every style apply: shadowed globals, missing required material properties
- `VisualStyleConfiguration.customShaderParameters` applied by a generic loop
- Removed never-called `ApplyToMaterial` from border/fog/highlight/terrain renderers + `*StyleParams` structs

### 6. Phase 6 — Data-driven styles
**Files:** `Scripts/Map/VisualStyles/VisualStyleDataFile.cs` (new), `VisualStyleConfiguration.Sections.cs` (new partial), `VisualStyleManager.cs`, `Core/GameSettings.cs`, `Template-Data/map/visual_style.json5` (new)
- JSON5 via `DataFileResolver` (override-first), keyed by parameter names; colors, numbers, runtime PNG/JPG textures
- Engine-managed parameters refused (`StyleSettable = false`)
- Hot reload by polling the file timestamp; toggles `GameSettings.WatchStyleDataFile` / `WatchStyleDataFileInDevelopmentBuilds`
- "Export Style To Data File" context menu writes a template

---

## Decisions Made

### Decision 1: Globals over material properties for engine values
**Options:** keep material properties + sync tooling; globals
**Decision:** Globals
**Rationale:** removes Properties sync, rebinding after material swap, and the silent-zero failure; any shader can read them
**Trade-offs:** one set per process; a second simultaneous map with different data needs per-material overrides

### Decision 2: Tessellation stays per-material
**Rationale:** selection and border mesh code read `_HeightScale` back from the material (`HasProperty`) to detect displacement; as a global it would look set in flat mode

### Decision 3: Compile-time hooks instead of the runtime compositor
**Rationale:** compositor was never wired; hooks give full stage replacement at zero runtime cost

### Decision 4: Style files keyed by shader parameter names
**Rationale:** types/scope come from the registry (HOI4-style name contract); engine-managed state refused so shader and C# can't desync
**Trade-offs:** structural settings (border rendering mode, tessellation on/off) stay asset-only

### Decision 5: Defer generated-data disk cache
**Rationale:** at current size generation is cheap; at EU5 scale VRAM is the real limit and needs an opt-in streaming/tiled system. See decision doc

Full record: [map-shader-architecture-v2.md](../../../decisions/map-shader-architecture-v2.md)

---

## What Worked ✅

1. **Diffing GAME copy against ENGINE default before redesigning**
   - Showed only ~5 lines differed, which made the case for an engine-owned pass
2. **GUID search for "unused" assets**
   - Searching `.meta` GUIDs in `.mat/.unity/.prefab/.asset` proved MapCore and friends dead before deleting
3. **Classifying CBUFFER fields by "set from C# or not"**
   - Split globals vs material values mechanically; avoided zeroing values that only had Properties defaults
4. **One phase per commit, verified in Unity between phases**

---

## What Didn't Work ❌

1. **Relying on F5 for style reload**
   - The F5 handler is GAME-side and not present in every scene
   - Lesson: engine-level dev tooling shouldn't depend on GAME scene setup → file watcher in `VisualStyleManager`

---

## Problems Encountered & Solutions

### Problem 1: CS1061 on enum members in `MapShaderGlobals`
**Root Cause:** static field `BorderRenderingMode` (property ID) shadowed the `BorderRenderingMode` enum inside the class
**Solution:** renamed field to `BorderRenderingModeId`
**Pattern for Future:** don't name ID fields after types used in the same class

### Problem 2: Newline in a C# string literal
**Root Cause:** `\\n` in a Python heredoc edit became a real newline inside a `[Tooltip]`
**Solution:** single-line tooltip

### Problem 3: Stray material instances
**Root Cause:** `BaseCameraController` and `FogOfWarSystem` used `renderer.material` (creates an instance; went stale after style swap; fog system used `FindFirstObjectByType<MeshRenderer>()`, i.e. any renderer)
**Solution:** both now write globals; material fields removed

### Problem 4: `TRANSFORM_TEX` on global textures
**Root Cause:** globals have no `_ST`; tiling would read zero
**Solution:** sample at map UV (heightmap displacement, overlay)

---

## Architecture Impact

### Documentation Updates
- [x] `Docs/Wiki/Shaders.md` — setup (thin shell), globals contract, stage overrides, validation, style data files
- [x] `Docs/Engine/visual-styles-architecture.md` — Level 2 rewritten; compositor claims removed
- [x] `Docs/CURRENT_FEATURES.md`, `Docs/Engine/architecture-patterns.md`, `Scripts/Map/FILE_REGISTRY.md`
- [x] Decision doc added; copy-and-customize marked superseded; HOI4 notes link the outcome

### New Pattern: Engine globals + thin shader shell
- When: engine-produced data consumed by GAME shaders
- Rule: never declare an engine global in Properties (validation enforces)

### New Anti-Pattern: material property for engine-set values
- A material property shadows a global of the same name; the value never updates

---

## Code Quality Notes

### Performance
- No runtime cost change for unused hooks (compile-time)
- Startup (5632x2048): blend maps ~46 ms, terrain type ~68 ms, province pixel index 542 ms (largest uncached CPU step)

### Testing
- **Manual:** each phase verified in Unity (console clean, map modes, borders, fog, style swap, selection on terrain); style file edit + hot reload verified; export round trip keeps authored colors

### Technical Debt
- **Paid Down:** legacy MapCore shaders, compositor stack, `MAP_MODE_*` keywords, `ApplyToMaterial`/`*StyleParams`, per-copy pass code
- **Remaining:** `_Dev*` development gradient (GAME policy) still in ENGINE material CBUFFER; culture/religion textures bound but unread by ENGINE shaders

---

## Next Session

### Immediate Next Steps
1. Move `_Dev*` gradient out of `DefaultCommon.hlsl` into GAME shaders (`ARCHON_GAME_MATERIAL_PROPERTIES`)
2. Large-map research (EU5 decompile: texture streaming / memory) before any large-map work

---

## Session Statistics

**Engine commits:** 9 (`42c97e4` … `a7580d1`)
**Engine diff:** 89 files, +2409 / −3473
**Bugs fixed:** stray material instances (camera, fog), unused GAME references in ENGINE shader

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Engine globals: `Scripts/Map/Rendering/MapShaderGlobals.cs`; contract: `MapShaderParameterRegistry`
- Pass + hooks: `Shaders/Includes/MapFragment.hlsl`, `MapPassFlat/Terrain.hlsl`
- Style files: `Scripts/Map/VisualStyles/VisualStyleDataFile.cs`

**Gotchas for Next Session:**
- Never add engine values to a map shader's Properties block
- New engine shader values: add an ID + register in `RegisterEngineParameters()` (mark engine-managed with `E(...)`)
- Pass include must use `#include_with_pragmas`

---

## Links & References

- Decision: [map-shader-architecture-v2.md](../../../decisions/map-shader-architecture-v2.md)
- Learnings: [hoi4-shader-system-notes.md](../../../learnings/hoi4-shader-system-notes.md)
- Wiki: [Shaders.md](../../../../Wiki/Shaders.md)
