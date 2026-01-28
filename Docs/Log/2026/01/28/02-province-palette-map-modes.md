# Province Palette Map Mode System
**Date**: 2026-01-28
**Session**: 02 (continuation of 01-scalability-fixes-gpu-adjacency.md)
**Status**: 🔄 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix `Texture2DArray (15000x6500x16) is too large; only support up to 2GB sizes` error
- Replace full-resolution texture array with province-indexed palette system

**Secondary Objectives:**
- Continue GPU adjacency detection from session 01
- Get StarterKit scene running without crashes

**Success Criteria:**
- MapModeManager initializes without texture size errors
- Province palette uses ~6.4MB instead of 6.24GB

---

## Context & Background

**Previous Work:**
- See: [01-scalability-fixes-gpu-adjacency.md](01-scalability-fixes-gpu-adjacency.md)
- TLS allocator errors and texture size failures with 15000x6500 map

**Current State:**
- Map is 15000x6500 pixels (97.5M pixels)
- Old approach: Texture2DArray at full resolution per map mode = 6.24GB
- New approach: Province palette (256 x rows) = ~6.4MB

**Why Now:**
- StarterKit scene crashes immediately on play due to texture allocation failure

---

## What We Did

### 1. Province Palette System Design

**Architecture Decision:**
Instead of storing full-resolution color per map mode, store per-province colors in a small palette texture.

**Memory Comparison:**
- Old: 15000 × 6500 × 16 modes × 4 bytes = **6.24 GB** (exceeds 2GB limit)
- New: 256 × 400 rows × 16 modes × 4 bytes = **~6.4 MB**

**Palette Layout:**
- Width: 256 columns (provinceID % 256)
- Height: (maxProvinces/256) * numModes rows
- ProvinceID → x = ID % 256, y = (ID / 256) + (modeIndex * rowsPerMode)

### 2. Shader Changes

**Files Changed:**
- `Shaders/Includes/DefaultCommon.hlsl`
- `Shaders/Includes/DefaultMapModes.hlsl`
- `Shaders/DefaultFlatMapShader.shader`
- `Shaders/DefaultTerrainMapShader.shader`

**Key Changes:**
- Replaced `TEXTURE2D_ARRAY(_MapModeTextureArray)` with `TEXTURE2D(_ProvincePaletteTexture)`
- Added `_MaxProvinceID` shader property for row calculation
- `RenderCustomMapMode()` now does provinceID → palette lookup instead of UV sampling

```hlsl
// New palette lookup in RenderCustomMapMode()
int rowsPerMode = (_MaxProvinceID + 255) / 256;
int col = provinceID % 256;
int row = (provinceID / 256) + (_CustomMapModeIndex * rowsPerMode);
float2 paletteUV = float2((col + 0.5) / paletteSize.x, (row + 0.5) / paletteSize.y);
float4 color = SAMPLE_TEXTURE2D_LOD(_ProvincePaletteTexture, sampler_ProvincePaletteTexture, paletteUV, 0);
```

### 3. MapModeManager Refactor

**Files Changed:** `Scripts/Map/MapModes/MapModeManager.cs`

**Removed:**
- `Texture2DArray mapModeTextureArray`
- `mapWidth`, `mapHeight` fields
- `UpdateCustomMapModeTexture()` method
- `CopyRenderTextureToArray()` method
- `GetMapDimensions()` method

**Added:**
- `Texture2D provincePaletteTexture`
- `maxProvinceID`, `rowsPerMode` fields
- `UpdateProvinceColors(int paletteIndex, Dictionary<int, Color32> colors)`
- `UpdateProvinceColor(int paletteIndex, int provinceID, Color32 color)`
- `ApplyPaletteChanges()`
- `GetPaletteInfo()` - returns (maxProvinces, maxModes, rowsPerMode)

### 4. GradientMapMode Refactor

**Files Changed:** `Scripts/Map/MapModes/GradientMapMode.cs`

**Removed:**
- GPU compute shader approach (full-res output texture)
- `ComputeShader`, `ComputeBuffer` resources
- `RenderTexture outputTexture`
- `RunGPUColorization()`, `CopyToTextureArray()`

**New Approach:**
- CPU calculates gradient color per province (~100k iterations, trivial)
- Stores colors in `Dictionary<int, Color32>`
- Calls `mapModeManager.UpdateProvinceColors(paletteIndex, provinceColors)`
- GPU does per-pixel palette lookup at render time (100M lookups, but just texture samples)

**Performance Justification:**
- Old: GPU compute shader iterates 100M pixels
- New: CPU iterates 100k provinces (gradient eval), GPU does 100M texture lookups
- Texture lookups are what GPUs do best - this is actually cleaner

---

## Decisions Made

### Decision 1: Province Palette vs Tiled Textures

**Context:** Texture array at 15000x6500x16 exceeds 2GB GPU limit

**Options Considered:**
1. Province palette - 256-wide texture, provinceID → color lookup
2. Tiled/chunked textures - split map into regions like EU5
3. Single overlay texture - one full-res texture, recompute on mode switch

**Decision:** Province palette

**Rationale:**
- Map modes show per-province data (political, economic, etc.)
- No need for per-pixel variation within a province
- 1000x memory reduction (6.4MB vs 6.24GB)
- Instant mode switching (just change row offset)
- EU5 shader analysis confirmed Paradox uses similar approach

**Trade-offs:**
- Cannot do per-pixel gradients within provinces
- If needed later, can add single effect overlay texture

---

## Files Changed Summary

**Shaders:**
- `Shaders/Includes/DefaultCommon.hlsl` - Added palette texture, removed texture array
- `Shaders/Includes/DefaultMapModes.hlsl` - New `RenderCustomMapMode()` with palette lookup
- `Shaders/DefaultFlatMapShader.shader` - Updated properties
- `Shaders/DefaultTerrainMapShader.shader` - Updated properties

**C# Scripts:**
- `Scripts/Map/MapModes/MapModeManager.cs` - Palette system instead of texture array
- `Scripts/Map/MapModes/GradientMapMode.cs` - CPU gradient calculation, palette update

---

## Next Session - IMMEDIATE TASKS

### Compilation Issues to Fix
The code is not yet compiling. Need to check for:

1. **Any remaining references to old methods:**
   - `GetMapDimensions()` - removed
   - `CopyRenderTextureToArray()` - removed
   - `UpdateCustomMapModeTexture()` - removed

2. **Shader compilation:**
   - Test that `_ProvincePaletteTexture` samples correctly
   - Verify `GetDimensions()` works in HLSL

3. **TLS Allocator errors:**
   - Still happening - unrelated to map mode changes
   - Need to investigate NativeCollection leaks in initialization

### GPU Adjacency Detection (from session 01)
Still pending - `NeighborResult.AdjacencyDictionary` field was added but need to verify full integration.

---

## Quick Reference for Future Claude

**What Changed:**
- Map mode rendering: full-res texture array → province palette
- Memory: 6.24GB → 6.4MB
- GradientMapMode: GPU compute → CPU gradient + palette update

**Key Files:**
- Palette creation: `MapModeManager.cs:InitializeMapModeTextureArray()`
- Palette update: `MapModeManager.cs:UpdateProvinceColors()`
- Shader lookup: `DefaultMapModes.hlsl:RenderCustomMapMode()`

**Gotchas:**
- Shader uses `_MaxProvinceID` to calculate `rowsPerMode`
- Palette must use Point filtering (no interpolation)
- ProvinceID 0 = ocean, skip in palette

**Current Status:**
- Code changes done but NOT YET COMPILED
- User will test and report remaining errors
- TLS allocator issue still present (separate from map mode work)

---

*Session 02 of 2026-01-28*
