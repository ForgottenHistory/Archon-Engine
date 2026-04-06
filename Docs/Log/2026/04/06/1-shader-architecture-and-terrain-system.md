# Shader Architecture & Terrain System Overhaul
**Date**: 2026-04-06
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Establish proper shader copy-and-customize architecture for ENGINE-GAME separation
- Fix terrain rendering pipeline (buffer binding, palette colors, province terrain overrides)

**Secondary Objectives:**
- Fix locale loading for Hegemon
- Convert Hegemon map bitmaps to PNG
- Clean up stale shader files

**Success Criteria:**
- Hegemon has its own shader files that include ENGINE infrastructure
- Terrain renders correctly using per-province terrain buffer (not raw terrain.png)
- Province-level terrain overrides from history files work end-to-end

---

## What We Did

### 1. Locale File Structure Fix
Hegemon's locale files were flat in `Assets/Data/localisation/` — ENGINE expects language subdirectories. Moved `*_english.yml` files into `localisation/english/`.

### 2. BMP to PNG Conversion
Converted `provinces.bmp`, `heightmap.bmp`, `terrain.bmp` to PNG via Python. Massive size savings (33MB+33MB+11MB → 0.7MB+0.1MB+2.5MB).

### 3. Shader Copy-and-Customize Architecture
**Decision doc:** `Docs/Log/decisions/shader-copy-and-customize-architecture.md`

**Problem:** Three conflicting shader approaches existed:
- `MapCore.shader` — ENGINE including GAME hlsl (violates separation)
- Default shaders — ENGINE-only visuals (no GAME customization)
- GAME duplicating all ENGINE hlsl (stale copies)

**Solution:** GAME copies ENGINE's `.shader` files, replaces `DefaultMapModes.hlsl` include with its own dispatcher that includes ENGINE utilities + GAME visual policy.

**Files created (Hegemon):**
- `Assets/Game/Shaders/HegemonFlatMap.shader` — "Hegemon/FlatMap"
- `Assets/Game/Shaders/HegemonTerrainMap.shader` — "Hegemon/TerrainMap"
- `Assets/Game/Shaders/MapModes/HegemonMapModes.hlsl` — dispatcher
- `Assets/Game/Shaders/MapModes/MapModePolitical.hlsl` — Hegemon political visuals
- `Assets/Game/Shaders/MapModes/MapModeTerrain.hlsl` — Hegemon terrain visuals
- `Assets/Game/Shaders/MapModes/MapModeDevelopment.hlsl` — Hegemon development visuals

**Files removed:**
- `Assets/Game/Shaders/MapModes/MapModeCommon.hlsl` — was stale ENGINE copy
- `Assets/Game/VisualStyles/EU3Classic/` — old broken shaders causing compile errors

**Include structure:**
```
HegemonTerrainMap.shader (GAME)
  +-- DefaultCommon.hlsl (ENGINE) — CBUFFER, textures
  +-- HegemonMapModes.hlsl (GAME) — replaces DefaultMapModes.hlsl
  |     +-- MapModeCommon.hlsl (ENGINE) — borders, fog, highlights
  |     +-- MapModeTerrain.hlsl (GAME) — Hegemon terrain visuals
  |     +-- MapModePolitical.hlsl (GAME) — Hegemon political visuals
  |     +-- MapModeDevelopment.hlsl (GAME) — Hegemon dev visuals
  +-- DefaultLighting.hlsl (ENGINE)
  +-- DefaultEffects.hlsl (ENGINE)
  +-- DefaultDebugModes.hlsl (ENGINE)
```

### 4. Terrain Buffer Binding Fix (ENGINE)
**File:** `Scripts/Map/Rendering/VisualTextureSet.cs`

`SetTerrainBlendMaps()` stored DetailIndex/DetailMask textures but didn't bind them to the material (since `BindToMaterial` had already run). Fixed by storing bound material reference and binding immediately in `SetTerrainBlendMaps`.

### 5. Province Terrain Buffer Binding (ENGINE)
**File:** `Scripts/Map/Core/MapSystemCoordinator.cs`

`_ProvinceTerrainBuffer` (ComputeBuffer) was only bound to compute shaders, never to the material for fragment shader access. `AnalyzeTerrain()` creates a NEW buffer (disposing the old one), so binding must happen after it completes. Added `material.SetBuffer()` call after `AnalyzeTerrain()`.

### 6. Engine Terrain Shader: Buffer + Palette (ENGINE)
**File:** `Shaders/Includes/MapModeTerrainSimple.hlsl`

Changed from sampling raw `_ProvinceTerrainTexture` (terrain.png bitmap) to using `_ProvinceTerrainBuffer[provinceID]` → `_TerrainColorPalette` lookup. This means terrain colors come from the per-province terrain assignment (GPU majority vote + overrides), not the raw bitmap.

### 7. Terrain Color Palette from Registry (ENGINE)
**File:** `Scripts/Map/MapModes/MapModeDataTextures.cs`

`InitializeTerrainPalette()` was hardcoded with 6 colors. Updated to read from `GameState.Registries.Terrains` (populated from `terrain.json5` colors). Added `RefreshTerrainPalette()` method called from `ArchonEngine` after initialization.

### 8. Province-Level Terrain Overrides (ENGINE)
**Priority cascade:** Province File > terrain.json5 > GPU auto-assign

**Files changed:**
- `Core/Data/Json5ProvinceData.cs` — Added `terrain`, `hasTerrain` to `RawProvinceData`
- `Core/Data/ProvinceInitialState.cs` — Added `TerrainOverride` field
- `Core/Loaders/Json5ProvinceConverter.cs` — Parse `terrain` from province JSON5 files
- `Core/Jobs/ProvinceProcessingJob.cs` — Pass terrain override through Burst job
- `Core/Systems/ProvinceSystem.cs` — Store overrides in `GameState.ProvinceTerrainOverrides`
- `Core/Systems/Province/ProvinceStateLoader.cs` — Same storage in alternate code path
- `Core/GameState.cs` — Added `ProvinceTerrainOverrides` dictionary
- `Map/Loading/MapDataLoader.cs` — Apply province overrides after auto-assign + terrain.json5
- `Map/Core/MapSystemCoordinator.cs` — Pass overrides dictionary to terrain analysis

**Province file format:**
```json5
{
  owner: "CYN",
  controller: "CYN",
  terrain: "desert"
}
```

---

## Decisions Made

### Decision 1: Shader Copy-and-Customize
See `Docs/Log/decisions/shader-copy-and-customize-architecture.md` for full analysis.

### Decision 2: Per-Province Buffer vs Raw Bitmap for Terrain
**Context:** ENGINE's terrain shader sampled raw terrain.png — works for StarterKit but shows hexagons for Hegemon's EU4-style terrain bitmap.
**Decision:** Use `_ProvinceTerrainBuffer` + `_TerrainColorPalette` in both ENGINE and GAME shaders.
**Rationale:** Per-province terrain type is the single source of truth. Raw bitmap is data input, not visual output. Supports overrides at all three levels.

---

## Problems Encountered & Solutions

### Problem 1: Blend Maps Not Bound
**Symptom:** Black terrain in both StarterKit and Hegemon
**Root Cause:** `SetTerrainBlendMaps()` stored textures but didn't bind to material (already bound earlier)
**Solution:** Store material reference in `VisualTextureSet`, bind immediately in `SetTerrainBlendMaps`

### Problem 2: Province Terrain Buffer Not Bound
**Symptom:** All unowned provinces show olive green (grasslands = index 0)
**Root Cause:** `_ProvinceTerrainBuffer` only bound to compute shaders, never to material. `AnalyzeTerrain()` creates new buffer after initial bind.
**Solution:** Bind buffer to material after `AnalyzeTerrain()` completes

### Problem 3: Old EU3Classic Shaders Causing Errors
**Symptom:** Shader compile errors referencing deleted `MapModeCommon.hlsl`. Hex terrain still showing.
**Root Cause:** `Assets/Game/VisualStyles/EU3Classic/` had old broken shaders that material was using
**Solution:** Deleted EU3Classic folder, ensured material uses `Hegemon/TerrainMap`

### Problem 4: ArchonLogger on Background Thread
**Symptom:** `get_isPlaying can only be called from the main thread` crash
**Root Cause:** Added `ArchonLogger.Log()` in `Json5ProvinceConverter` which runs on thread pool
**Solution:** Removed the log. Province file loading is multi-threaded — no Unity API calls.

---

## Architecture Impact

### Documentation Created
- `Docs/Wiki/Shaders.md` — Step-by-step shader setup guide
- `Docs/Log/decisions/shader-copy-and-customize-architecture.md` — Decision doc

### Documentation Updated
- `Docs/Engine/visual-styles-architecture.md` — Added Level 2 (copy-and-customize)
- `Docs/Wiki/Getting-Started.md` — Added Shaders/ to file organization
- `Docs/Wiki/toc.yml` — Added Shaders entry

---

## Next Session

### Immediate Next Steps
1. Verify terrain palette colors match terrain.json5 visually in StarterKit
2. Continue Hegemon scene setup (other systems beyond terrain/shaders)
3. Clean up `terrain_rgb.json5` legacy in Hegemon — may be redundant now

### Questions to Resolve
1. Should `GetTerrainColor()` in Hegemon's `MapModeTerrain.hlsl` also use the palette texture instead of hardcoded colors?
2. Hegemon's terrain.json5 has different category structure than terrain_rgb.json5 — which is authoritative?

---

## Quick Reference for Future Claude

**What Changed:**
- ENGINE shaders now use `_ProvinceTerrainBuffer` + `_TerrainColorPalette` (not raw bitmap)
- GAME shaders are copies of ENGINE Default shaders with custom map mode dispatcher
- Province files support `terrain: "category_name"` for per-province terrain override
- Terrain assignment priority: Province File > terrain.json5 terrain_override > GPU auto-assign

**Gotchas:**
- `AnalyzeTerrain()` creates a NEW ComputeBuffer — must bind to material AFTER it runs
- Province file loading is multi-threaded — no Unity API calls (ArchonLogger uses Debug.Log internally)
- `VisualStyleManager` creates material instances — buffer binding must use `meshRenderer.sharedMaterial` after instance creation
- Properties block in GAME `.shader` must stay in sync with ENGINE's `DefaultCommon.hlsl` CBUFFER (silent failure if out of sync)

**Commits (Archon):**
- `d8a9052` — Shader architecture docs (wiki, decision doc, visual-styles update)
- `0748490` — Fix terrain buffer and blend maps not bound to material
- Uncommitted: terrain palette from registry, province terrain overrides, MapModeTerrainSimple buffer lookup
