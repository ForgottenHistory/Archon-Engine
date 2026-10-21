# Map Shader Architecture v2
**Date:** 2026-10-21
**Status:** 🔄 Phase 1 done (pending compile check)
**Scope:** ENGINE only (Hegemon migrates after)
**Source:** `Docs/Log/learnings/hoi4-shader-system-notes.md`
**Supersedes (when done):** `Docs/Log/decisions/shader-copy-and-customize-architecture.md`

---

## Goal

Keep what works (name-based engine↔shader contract, GAME owns visual policy) and remove the cost of using it: a GAME shader should be ~30 lines, engine infrastructure updates should flow through automatically, and a missing contract value should be a logged error, not a silent zero.

---

## Current State (Findings)

1. **The copy is almost all engine code.** `HegemonTerrainMap.shader` vs `DefaultTerrainMapShader.shader`: ~300 lines, only name, pass name, one include and fallback differ. GAME owns copies of tessellation, vertex, frag, and effect order. Engine fixes there never reach GAME.
2. **Contract written in three places.** `UnityPerMaterial` CBUFFER in `DefaultCommon.hlsl`, Properties block in every copied `.shader`, ~335 hand-written `material.SetX` calls in C#. Missing property = silent 0. (Example: stale vector-curve properties had to be removed by hand from both Hegemon shaders.)
3. **ENGINE references GAME:**
   - `DefaultTerrainMapShader.shader` — `FallBack "Hegemon/EU3Classic"`, `CustomEditor "Game.Shaders.EU3MapShaderGUI"`
   - `MapCore.shader`, `MapCoreTessellated.shader` — `#include "../../Game/Shaders/..."`
4. **Dead or half-built:**
   - `Compositing.hlsl` is included by no shader → `IShaderCompositor` / `_EnableBorders` / blend modes do nothing. Visual-styles doc "Level 1 compositing" is not real on the shader side.
   - `BaseMapModeHandler.DisableAllMapModeKeywords` (`MAP_MODE_*`) only serves legacy MapCore shaders.
   - Two diverged copies: `Shaders/MapModeCommon.hlsl` and `Shaders/Includes/MapModeCommon.hlsl`.
5. **Not moddable at runtime.** `VisualStyleConfiguration` is a ScriptableObject; shipped builds can't change it. `DataFileResolver` / `ModLoader` exist but don't feed rendering.

---

## HOI4 Lessons Applied

| HOI4 | Archon v2 |
|---|---|
| Engine uploads named constants/textures each frame; shader just declares them | Engine-owned values become shader **globals** (Phase 2) |
| `Effect` = shared VS/PS code + defines | Engine-owned **pass include**; GAME shader is a thin shell (Phase 3) |
| Mods override single `.fxh` files | **Per-stage override hooks** via `#define` (Phase 4) |
| Clear compile errors naming the file | **Contract registry + startup validation** (Phase 5) |
| `constants.fxh` / data-driven tuning | **Data-driven style files** via `DataFileResolver` (Phase 6) |
| Content-hashed shader cache | Optional disk cache for expensive generated textures (Phase 7) |

**Not borrowed:** runtime shader compilation, text-overridable `.shader` files (Unity players can't compile shaders). Per-quality keyword variants: low value — `_MapMode`/border/fog branches are uniform across the draw and cheap; keyword-per-mode is what produced the dead `MAP_MODE_*` code.

---

## Phases

### Phase 1 — Cleanup (prerequisite)

**Investigation (2026-10-21):**
- `Archon/MapCore`: only user is `Assets/Materials/Map.mat` (Hegemon repo), which nothing references (no scene, prefab, asset, code; not in Resources). `Archon/MapCoreTessellated`: no users. No `Shader.Find` for either.
- Root `Shaders/MapModeCommon.hlsl`: included only by the two MapCore shaders. `DefaultMapModes.hlsl` resolves to `Includes/MapModeCommon.hlsl`.
- `Archon/MapFallback`: no material references it, nothing finds it by name.
- Compositor: nothing calls `RegisterShaderCompositor`/`GetShaderCompositor`; `DefaultShaderCompositor` is never instantiated; `VisualStyleManager` never reads `style.compositor`; no shader includes `Compositing.hlsl`. Fully dead → **delete** (Phase 4 hooks replace it).
- Side note (GAME): `HegemonFlatMapMaterial.mat` uses `Archon/DefaultFlat`, not `Hegemon/FlatMap`.

**Tasks:**
- [x] Remove GAME references from `DefaultTerrainMapShader.shader` (fallback → `Archon/DefaultFlat`, comment out CustomEditor like the flat shader)
- [x] Delete `MapCore.shader`, `MapCoreTessellated.shader`, root `Shaders/MapModeCommon.hlsl`
- [x] Delete `MapFallback.shader` (unused; Phase 3 shells can fall back to URP Unlit)
- [x] Delete `Compositing.hlsl`, `Rendering/Compositing/` (interface, base, `DefaultShaderCompositor`), compositor section of `MapRendererRegistry`, `CompositorStyle` in `VisualStyleConfiguration`
- [x] Remove `MAP_MODE_*` keyword helpers from `IMapModeHandler.cs` + no-op copies in `GradientMapMode.cs`; removed call sites in `EnginePoliticalMapMode`, StarterKit `TerrainVisualMapMode`, and Hegemon `PoliticalMapMode` / `ProvinceColorsMapMode` / `TerrainMapMode`
- [x] Rename pass names in Default shaders (`"MapCore"` / `"MapCoreTessellated"` → `"ArchonFlat"` / `"ArchonTerrain"`)
- [x] Update `Map/FILE_REGISTRY.md`, `Docs/CURRENT_FEATURES.md`, `Docs/Engine/architecture-patterns.md`, `Docs/Engine/visual-styles-architecture.md` (compositor mentions), plus Wiki `Cookbook.md` / `Map-Rendering.md` keyword examples. Note: docs listed Minimal/Stylized/Cinematic compositors that never existed in code.
- [ ] GAME: delete `Assets/Materials/Map.mat` (+ .meta) — now points at a deleted shader; left for user

### Phase 2 — Engine values as globals
Move engine-produced state out of `UnityPerMaterial` into globals set via `Shader.SetGlobalTexture/Buffer/Float/Int/Vector`.

**Globals (engine owns):** simulation/generated textures and buffers (province ID, owner, palettes, province palette, border textures, highlight, fog, heightmap, normal map, terrain arrays/blend maps, `_ProvinceTerrainBuffer`), map-mode state (`_MapMode`, `_CustomMapModeIndex`, `_MaxProvinceID`), engine style params (borders, fog, lighting, tessellation).

**Stays per-material:** GAME-specific tweakables only (its own Properties + its own CBUFFER).

- [ ] New engine include declaring globals (outside `UnityPerMaterial`); `DefaultCommon.hlsl` CBUFFER shrinks to near-empty
- [ ] Convert binding sites (`CoreTextureSet`, `DynamicTextureSet`, palette manager, border/fog/highlight renderers, `MapModeManager`, ~20 files) from `material.SetX` to globals, using cached `Shader.PropertyToID`
- [ ] Remove rebind-after-material-swap logic in `VisualStyleManager`
- [ ] Strip engine properties from Default shaders' Properties blocks

**Trade-off:** globals are shared by all materials — fine for a single map; a second simultaneous map (minimap with different data) would need per-material overrides or a `MaterialPropertyBlock`. Document this.
**Benefit:** border mesh, billboards, units can read fog/height/province ID with no extra binding.

### Phase 3 — Engine pass includes
- [ ] Create `Includes/MapPassTerrain.hlsl` (tessellation + vertex + frag) and `Includes/MapPassFlat.hlsl`, pulling `#pragma`s via `#include_with_pragmas`
- [ ] Contract: GAME defines `RenderMapMode(int mapMode, uint provinceID, float2 uv, float3 positionWS)` before including the pass
- [ ] Rewrite Default shaders as thin shells (Properties + Tags + 3 includes) — they double as the template for games
- [ ] Verify include path resolution from a shader outside `Archon-Engine/`

### Phase 4 — Per-stage override hooks
In the pass include, each stage is defaulted unless GAME overrides:
`ARCHON_OVERRIDE_LIGHTING`, `_BORDERS`, `_HIGHLIGHTS`, `_FOG`, `_OVERLAY`, `_OCEAN`, plus optional `ARCHON_POST_COMPOSITE(color, ctx)` hook for extra GAME effects.
- [ ] Define a small context struct passed to stages (uv, positionWS, provinceID) so signatures stay stable as inputs grow
- [ ] Document hook list as the shader contract

### Phase 5 — Contract registry + validation
- [ ] Single C# definition of shader parameters (name, type, default, scope global/material) — replaces hand-written setters in `VisualStyleManager`
- [ ] Generic apply loop
- [ ] Startup validation: for material-scoped params, `material.HasProperty` check; log error naming shader + missing property (`map_rendering`)
- [ ] GAME can register additional parameters without ENGINE changes

### Phase 6 — Data-driven styles (modding)
- [ ] Style file format (JSON5) resolved via `DataFileResolver`, mapping onto Phase 5 parameter definitions
- [ ] Textures via runtime load (PNG; DDS later), point/linear filtering declared per texture in the file
- [ ] ScriptableObject remains as editor-authored default, serializable to the same format
- [ ] Hot reload hook (existing F5 path)

### Phase 7 — Generated-data disk cache (optional)
Only if startup logs show cost. Candidates: border distance field, terrain blend maps, normal map.
- [ ] Key = hash of inputs (source files + generator version); store full input fingerprint alongside to guard collisions (HOI4 `.scache` idea)
- [ ] Cache under `persistentDataPath`, never inside `Assets/`

---

## Docs To Update When Done
- New decision doc replacing `shader-copy-and-customize-architecture.md` (mark old one superseded)
- `Docs/Engine/visual-styles-architecture.md` — replace Level 1/2/3 with: hooks → thin shell → full override
- `Docs/Wiki/Shaders.md` — new setup guide
- `Map/FILE_REGISTRY.md`

## Then: Hegemon Migration
Rewrite `HegemonFlatMap` / `HegemonTerrainMap` as thin shells, keep `MapModes/*.hlsl`, move Hegemon tweakables to its own CBUFFER, clean `Game/FILE_REGISTRY.md` (stale EU3Classic / VisualStyles entries).
