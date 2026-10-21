# Map Shader Architecture v2
**Date:** 2026-10-21
**Status:** ✅ Phases 1-6 done, Phase 7 deferred
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
Move engine-produced state out of `UnityPerMaterial` into globals set via `Shader.SetGlobal*`.

**Globals (engine owns):** all simulation/generated textures and buffers, palettes, province palette, map-mode state, and every style value C# sets (borders, distance field shape, ocean/unowned colors, terrain look, normal-map lighting, overlay, saturation, fog).

**Stays per-material:** tessellation (`_HeightScale` etc. — describes the shader's geometry; `ProvinceSelector`, `BorderMeshRenderer`, `MeshGeometryBorderRenderer` read it back via `HasProperty`/`GetFloat` to know whether the current shader displaces; as a global it would look set in flat mode too), values never set from C# (`_Dev*` gradient + tiers, `_TriPlanarTightenFactor`, `_HighlightStrength`), GAME tweakables.

- [x] `Scripts/Map/Rendering/MapShaderGlobals.cs` — property IDs, `SetDefaults()` (fallback textures + VisualStyleConfiguration defaults; called once from `MapTextureManager.Initialize`), style→global setters
- [x] `DefaultCommon.hlsl` — CBUFFER reduced to per-material values + `ARCHON_GAME_MATERIAL_PROPERTIES` hook; everything else declared as globals
- [x] Texture sets / palette / map mode textures: `BindToMaterial(material)` → `BindGlobals()`; `MapTextureManager.BindTexturesToMaterial(material)` → `BindGlobalTextures()`; callers updated (coordinator, image loaders, data integrator, data reload)
- [x] `MapModeManager`, `SetShaderMode` (base + `GradientMapMode`), `VisualStyleManager`, renderer `ApplyToMaterial` impls, `FogOfWarSystem`, `BaseCameraController`, `BorderComputeDispatcher`, terrain buffer → globals
- [x] Removed rebind-after-material-swap; `MapModeManager.UpdateMaterial` now only re-activates the mode (per-material keywords)
- [x] Removed unused per-material border setters (`DynamicTextureSet.SetBorderStyle/SetDistanceFieldBorderParams/Bind*Textures`, `MapTextureManager.SetBorderStyle`)
- [x] Removed stray `renderer.material` instances in `BaseCameraController` and `FogOfWarSystem` (`FindFirstObjectByType<MeshRenderer>()` could pick any renderer; instance went stale after style swap)
- [x] Removed `TRANSFORM_TEX` on global textures (heightmap, overlay — globals have no `_ST`)
- [x] Properties blocks stripped in Default shaders + Hegemon shaders (GAME commit)
- [x] Wiki `Shaders.md`, `Map/FILE_REGISTRY.md`

**Found for Phase 5:** renderer `ApplyToMaterial` methods (border/fog/terrain/highlight) are never called; `DistanceFieldBorderRenderer` wrote `_BorderEdgeWidth/_BorderGradientWidth/_BorderSmoothness` and `HighlightRendererBase` writes `_HighlightSelectionColor/_HoverColor/_Opacity` — no shader reads them. `MapModeDataTextures` binds culture/religion textures no shader reads.

**Trade-off:** globals are shared by all materials — fine for a single map; a second simultaneous map (minimap with different data) would need per-material overrides or a `MaterialPropertyBlock`.
**Benefit:** border mesh, billboards, units can read fog/height/province ID with no extra binding.
**Watch:** materials keep stale serialized values for removed properties. Unity ignores them (not in Properties), but if a value looks stuck, reset the material (debug inspector).

### Phase 3 — Engine pass includes
- [x] `Includes/MapShaderSetup.hlsl` (URP + DefaultCommon), `Includes/MapFragment.hlsl` (shared composition, `ArchonMapFragment`), `Includes/MapPassFlat.hlsl`, `Includes/MapPassTerrain.hlsl` (entry points + pragmas incl. `target 4.6`, `TERRAIN_DETAIL_MAPPING`)
- [x] Contract: dispatcher defines `RenderMapMode(int mapMode, uint provinceID, float2 uv, float3 positionWS)` before the pass include
- [x] Default shaders rewritten as thin shells (~65-75 lines); Hegemon shaders too (GAME commit)
- [x] Entry points/structs prefixed `ArchonMap*` to avoid clashing with GAME code
- [x] Include resolution: quoted includes resolve relative to the including file, so ENGINE includes work from GAME shaders via the absolute `Assets/Archon-Engine/...` path
- [x] Wiki `Shaders.md`, `visual-styles-architecture.md` Level 2, `CURRENT_FEATURES.md`

### Phase 4 — Per-stage override hooks
- [x] `Includes/MapContext.hlsl` — `ArchonMapContext` (uv, positionWS, provinceID, mapMode), included by `MapShaderSetup.hlsl` so GAME code sees it
- [x] `MapFragment.hlsl` — stages as functions with `#ifndef ARCHON_OVERRIDE_*` defaults: `OCEAN`, `LIGHTING`, `BORDERS`, `HIGHLIGHTS`, `FOG`, `OVERLAY`; optional `ARCHON_POST_COMPOSITE`
- [x] Hook contract documented in `MapFragment.hlsl` header + Wiki `Shaders.md` (Stage Overrides, wrap-the-default example); visual styles doc updated
- Not done: per-stage overrides of debug modes / map mode dispatch (dispatch is already GAME-owned via `RenderMapMode`)

### Phase 5 — Contract registry + validation
- [x] `Scripts/Map/Rendering/ShaderParameters/` — `MapShaderParameter` + `ShaderValue` (typed value), `MapShaderParameterRegistry` (register, global defaults, generic `Apply`, `ValidateMaterial`), `ShaderParameterOverride` (serializable style entry)
- [x] `MapShaderGlobals.RegisterEngineParameters()` registers all ENGINE params; `SetDefaults()` = register + `ApplyGlobalDefaults()` loop (replaces hand-written default calls). Style defaults still come from `VisualStyleConfiguration` defaults
- [x] Validation on every `VisualStyleManager.ApplyStyle`: error per engine global declared in Properties (shadowing) and per missing required material property (`_HighlightStrength`, `_TriPlanarTightenFactor`)
- [x] GAME extensibility: `RegisterGlobal/RegisterMaterial` from GAME code; `VisualStyleConfiguration.customShaderParameters` applied by a generic loop
- [x] Removed dead per-material API: `ApplyToMaterial` from `IBorderRenderer`/`IFogOfWarRenderer`/`IHighlightRenderer`/`ITerrainRenderer` + all implementations, `ApplyCommonStyleParams`, and the now-unused `Border/FogOfWar/Highlight/TerrainStyleParams` structs (never called; wrote names no shader reads)
- Kept: typed `MapShaderGlobals.Apply*` setters for the built-in style sections (typed config → typed globals). A fully name-driven style is Phase 6 (data files)
- Kept: culture/religion texture bindings (unread by ENGINE shaders, but available to GAME shaders)

### Phase 6 — Data-driven styles (modding)
- [x] `VisualStyles/VisualStyleDataFile.cs` — JSON5 file via `DataFileResolver` (override-first), `parameters` keyed by registry names; value forms: number, `[r,g,b(,a)]` (0-1 or 0-255), `"#rrggbb"`, `{ texture, filter, wrap, linear, mipmaps }`, `{ value, type, scope }` for unregistered GAME params
- [x] `MapShaderParameter.StyleSettable` — engine-managed globals (simulation/generated textures, palettes, map mode state, border mode, fog toggle/zoom) refused from styles; only `_OverlayTexture` is a style texture
- [x] Runtime PNG/JPG textures with per-entry filter/wrap/linear/mipmaps; cached per path, old texture destroyed on reload. DDS not supported yet
- [x] `VisualStyleConfiguration.styleDataFile` (default `map/visual_style.json5`); applied last in `ApplyStyle`, so F5 reload re-reads it
- [x] Export: `VisualStyleManager` context menu "Export Style To Data File" writes current values to the override folder (template)
- [x] Example `Template-Data/map/visual_style.json5` (empty `parameters`, documented forms). Hegemon uses `Assets/Data`, so it's unaffected
- [x] `VisualStyleConfiguration` split into partial `VisualStyleConfiguration.Sections.cs` (500-line guideline)
- Structural settings (border rendering mode, pixel-perfect thickness, tessellation on/off) stay asset-only: they drive C# systems, not just shader values
- [x] Hot reload: `VisualStyleManager` polls the override + base paths (built once per style apply; 1s interval) and re-applies the style on change. Toggles: `GameSettings.WatchStyleDataFile` (editor, default on), `WatchStyleDataFileInDevelopmentBuilds` (default off). F5 lives in GAME (`DebugInputHandler`), which isn't in every scene
- Verified: export keeps colors as authored (exported values match the style asset)

### Phase 7 — Generated-data disk cache (deferred)
Deferred: startup logs show it isn't worth it yet (terrain blend maps ~46 ms, terrain type generation ~68 ms; terrain image already has a pixel cache, 12 ms hit). Revisit at larger map sizes.
- [ ] Key = hash of inputs (source files + generator version); store full input fingerprint alongside to guard collisions (HOI4 `.scache` idea)
- [ ] Cache under `persistentDataPath`, never inside `Assets/`

---

## Docs To Update When Done
- [x] Decision doc `Docs/Log/decisions/map-shader-architecture-v2.md`; old copy-and-customize decision marked superseded
- [x] `Docs/Engine/visual-styles-architecture.md` — Level 2 rewritten (thin shell + hooks); Level 1 (pluggable C# renderers) and Level 3 (own pass) still accurate
- [x] `Docs/Wiki/Shaders.md` — setup guide, globals contract, stage overrides, validation, style data files
- [x] `Map/FILE_REGISTRY.md`

## Then: Hegemon Migration
- [x] `HegemonFlatMap` / `HegemonTerrainMap` are thin shells (done with Phase 3); `MapModes/*.hlsl` kept
- [ ] Move `_Dev*` gradient + tiers out of ENGINE `DefaultCommon.hlsl` into Hegemon (`ARCHON_GAME_MATERIAL_PROPERTIES` + Hegemon Properties; register as Hegemon material params)
- [ ] `HegemonFlatMapMaterial.mat` uses `Archon/DefaultFlat`, not `Hegemon/FlatMap` — decide which is intended
- [ ] Clean `Game/FILE_REGISTRY.md` (stale EU3Classic / VisualStyles / shader GUI entries; unused `EU3MapShaderGUI`, `DefaultMapShaderGUI`)
