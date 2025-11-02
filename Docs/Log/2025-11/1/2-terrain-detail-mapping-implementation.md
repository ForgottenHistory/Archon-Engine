# Terrain Detail Mapping Implementation
**Date**: 2025-11-02
**Session**: 2
**Status**: ❌ Blocked
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement AAA scale-independent terrain detail mapping for tessellated shader

**Secondary Objectives:**
- Support modding via texture file drop-in
- Support 256 terrain types via Texture2DArray

**Success Criteria:**
- Tiled detail textures visible when zoomed in
- Sharp detail at all zoom levels (world-space UVs)
- No performance degradation

---

## Context & Background

**Previous Work:**
- Tessellation system implemented (3D terrain height displacement)
- TerrainColorMapper defines 20+ terrain types from terrain.bmp

**Current State:**
- Basic terrain rendering works (macro texture only)
- Looks blurry when zoomed in (single 5632x2048 texture stretched)

**Why Now:**
- Recently implemented 3D tessellation - ready for AAA visuals
- User wants modern Paradox-quality graphics (Victoria 3/EU5 style)

---

## What We Did

### 1. Architecture Planning
**File:** `Assets/Archon-Engine/Docs/Planning/terrain-detail-mapping.md`

**Design:**
- Three-texture system: Macro (color) + Type (R8 indices) + Detail (Texture2DArray)
- World-space UVs for scale-independent tiling
- Moddable via `Assets/Data/textures/terrain_detail/{index}_{name}.png`

**Architecture Compliance:**
- ✅ Dual-layer (CPU simulation + GPU presentation)
- ✅ Engine-Game separation (mechanism vs policy)
- ✅ Explicit GraphicsFormat pattern
- ✅ Moddability first

### 2. Terrain Type Texture Generation
**Files:**
- `Assets/Archon-Engine/Scripts/Map/Loading/TerrainTypeTextureGenerator.cs` (NEW)
- `Assets/Archon-Engine/Scripts/Map/Rendering/VisualTextureSet.cs:145-162,205-227`
- `Assets/Archon-Engine/Scripts/Map/Loading/Bitmaps/TerrainBitmapLoader.cs:105-112`

**Implementation:**
- R8 texture (1 byte per pixel) stores terrain type index (0-255)
- Reverse color lookup using TerrainColorMapper
- Auto-generated after terrain.bmp loads
- Bound to shader as `_TerrainTypeTexture`

**Results:**
```
[Log] TerrainTypeTextureGenerator: Complete - Matched: 11534336, Unmatched: 0
[Log] VisualTextureSet: Generated terrain type texture 5632x2048 R8
```

### 3. Detail Texture Array Loader
**Files:**
- `Assets/Archon-Engine/Scripts/Map/Loading/DetailTextureArrayLoader.cs` (NEW)
- `Assets/Archon-Engine/Scripts/Map/Rendering/VisualTextureSet.cs:145-159,193,227`

**Implementation:**
- Scans `Assets/Data/textures/terrain_detail/` for `{index}_{name}.png/jpg`
- Builds Texture2DArray (512x512 per layer, BC7 compressed, mipmaps)
- Missing indices filled with neutral gray
- Supports PNG, JPG, JPEG

**Results:**
```
[Log] DetailTextureArrayLoader: Found 5 detail texture files
[Log] DetailTextureArrayLoader: Loaded layer 0 from 0_grasslands.jpg
[Log] DetailTextureArrayLoader: Loaded layer 3 from 3_desert.jpg
[Log] DetailTextureArrayLoader: Loaded layer 6 from 6_mountain.jpg
[Log] DetailTextureArrayLoader: Loaded layer 12 from 12_forest.jpg
[Log] DetailTextureArrayLoader: Loaded layer 16 from 16_snow.jpg
[Log] DetailTextureArrayLoader: Complete - Loaded: 5, Missing (neutral gray): 12
```

### 4. Shader Integration
**Files:**
- `Assets/Game/VisualStyles/EU3Classic/EU3MapShaderTessellated.shader:11-12,83-84,149-150,215-216,258-259`
- `Assets/Game/Shaders/MapModes/MapModeTerrain.hlsl:11-68`

**Implementation:**
- Added `_TerrainTypeTexture` and `_TerrainDetailArray` properties
- Added `_DetailTiling` and `_DetailStrength` controls
- Defined `TERRAIN_DETAIL_MAPPING` in tessellated shader only
- World-space UV calculation: `positionWS.xz * _DetailTiling`
- Height-based blend mode to preserve brightness

**Shader Logic:**
```hlsl
#ifdef TERRAIN_DETAIL_MAPPING
if (_DetailStrength > 0.0) {
    float terrainType = SAMPLE_TEXTURE2D(_TerrainTypeTexture, ...).r * 255.0;
    float2 worldUV = positionWS.xz * _DetailTiling;
    float4 detail = SAMPLE_TEXTURE2D_ARRAY(_TerrainDetailArray, ..., terrainType);
    // Blend with macro color
}
#endif
```

### 5. Visual Style Configuration Integration
**Files:**
- `Assets/Game/VisualStyles/VisualStyleConfiguration.cs:101-108`
- `Assets/Game/VisualStyles/VisualStyleManager.cs:233-240`

**Implementation:**
- Added `detailTiling` and `detailStrength` to MapModeColors
- Applied in `ApplyMapModeColors()` via `material.SetFloat()`
- Logged for debugging

**Results:**
```
[Log] VisualStyleManager: Detail mapping - Tiling: 100, Strength: 1
```

---

## Problems Encountered & Solutions

### Problem 1: Shader Doesn't Support JPG Files
**Symptom:** `DetailTextureArrayLoader: Found 0 detail texture files` despite JPGs present

**Root Cause:**
```csharp
var pngFiles = Directory.GetFiles(DetailTextureFolder, "*.png");  // Only PNG!
```

**Solution:**
```csharp
var imageFiles = new List<string>();
imageFiles.AddRange(Directory.GetFiles(DetailTextureFolder, "*.png"));
imageFiles.AddRange(Directory.GetFiles(DetailTextureFolder, "*.jpg"));
imageFiles.AddRange(Directory.GetFiles(DetailTextureFolder, "*.jpeg"));
```

**Pattern:** Always support multiple image formats for moddability

### Problem 2: HLSL Function Overloading Forward Reference
**Symptom:** `'RenderTerrain': no matching 3 parameter function`

**Root Cause:** HLSL doesn't support forward references for overloaded functions

**Solution:** Internal implementation + wrapper pattern
```hlsl
float4 RenderTerrainInternal(uint provinceID, float2 uv, float3 positionWS) { ... }
float4 RenderTerrain(uint provinceID, float2 uv, float3 positionWS) { return RenderTerrainInternal(...); }
float4 RenderTerrain(uint provinceID, float2 uv) { return RenderTerrainInternal(..., float3(0,0,0)); }
```

### Problem 3: Terrain Too Dark with Overlay Blend
**Symptom:** Terrain map mode dimmer than Political mode

**Investigation:**
- Tried multiply blend: `macroColor * detail * 2.0` → Too dark
- Tried overlay blend → Still too dark
- Photo textures from Poly Haven are full-range (not centered at 50% gray)

**Solution:** Height-based variation blend
```hlsl
float detailHeight = dot(microDetail.rgb, float3(0.299, 0.587, 0.114));
float detailVariation = (detailHeight - 0.5) * 0.3;
macroColor.rgb = macroColor.rgb * (1.0 + detailVariation * _DetailStrength);
```

**Status:** ❌ Still subtle/invisible at strength=1.0

### Problem 4: Detail Mapping Not Visible (BLOCKED)
**Symptom:** Macro texture only, no detail visible even with debug return statements

**Investigation:**
- ✅ Textures loading correctly (5 files loaded)
- ✅ Parameters set correctly (Tiling: 100, Strength: 1)
- ✅ Shader compiles without errors
- ❌ Debug `return microDetail;` shows macro only
- ❌ Debug `return float4(1,0,1,1);` shows macro only (magenta test)

**Root Cause:** UNKNOWN - `#ifdef TERRAIN_DETAIL_MAPPING` block not executing

**Attempted Solutions:**
1. `#ifdef _TerrainTypeTexture` → Doesn't work (textures don't create defines)
2. `if (_DetailStrength > 0.0)` → Not reached
3. `#define TERRAIN_DETAIL_MAPPING` → Defined correctly before includes
4. Debug return statements → Not executing

**Current Status:**
- Define appears in correct location (line 150, before includes at 286)
- Code inside `#ifdef TERRAIN_DETAIL_MAPPING` never executes
- Even simple `return float4(1,0,1,1);` shows macro texture

**Next Steps:**
- Verify shader variant compilation
- Check if tessellated shader is actually being used
- Verify MapMode.Terrain is using RenderTerrain() correctly
- Consider shader keyword approach instead of define

---

## What Worked ✅

1. **Texture2DArray Pattern**
   - Single shader property for unlimited terrain types
   - GPU-efficient indexing
   - Supports hot-reload

2. **Reverse Color Lookup**
   - TerrainColorMapper as single source of truth
   - Automatic terrain type detection
   - No manual authoring required

3. **Moddable File Structure**
   - `{index}_{name}.ext` naming pattern
   - Auto-discovery
   - Multiple format support

4. **Explicit GraphicsFormat**
   - Followed established pattern
   - R8_UNorm for indices
   - R8G8B8A8_SRGB for detail textures

---

## What Didn't Work ❌

1. **Multiply Blend for Photo Textures**
   - Photo textures not authored with 50% gray as neutral
   - Causes darkening
   - Lesson: Need blend mode that preserves brightness

2. **#ifdef with Texture Name**
   - `#ifdef _TerrainTypeTexture` doesn't work
   - HLSL textures don't create preprocessor defines
   - Lesson: Use explicit `#define TERRAIN_DETAIL_MAPPING` keyword

3. **Overlay Blend Mode**
   - Still caused dimming
   - Too complex for desired effect
   - Lesson: Simple height variation is better for detail

---

## Architecture Impact

### Documentation Updates Required
- [x] Created `Assets/Archon-Engine/Docs/Planning/terrain-detail-mapping.md`
- [ ] Update Map/FILE_REGISTRY.md - Add DetailTextureArrayLoader, TerrainTypeTextureGenerator
- [ ] Document blend mode trade-offs

### New Patterns Discovered
**Pattern: Texture Type Index via Reverse Lookup**
- Use existing color mapping as source of truth
- Generate index texture programmatically
- Avoids manual authoring
- Add to: Map loading patterns

**Pattern: Moddable Asset Auto-Discovery**
- Filename-based indexing: `{index}_{name}.ext`
- Multi-format support
- Missing indices auto-filled
- Add to: Modding design patterns

---

## Code Quality Notes

### Performance
- **Memory:** ~2-4 MB for detail array (BC7 compressed, 8 layers)
- **Runtime:** +2 texture samples per pixel in Terrain mode
- **Target:** <5% frame time impact
- **Status:** ⚠️ Unable to verify (feature not working)

### Testing
- **Manual Tests Needed:**
  1. Verify detail textures load from folder
  2. Verify terrain type texture generation
  3. Verify shader receives parameters
  4. Verify detail visible at zoom

### Technical Debt
- **Created:**
  - Blend mode may need refinement
  - Shader define mechanism unclear
  - No anti-tiling (Inigo Quilez method) yet

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Debug why `#ifdef TERRAIN_DETAIL_MAPPING` block not executing**
   - Verify shader variant compilation
   - Check preprocessor define propagation
   - Try shader keyword approach (#pragma multi_compile)

2. **Verify tessellated shader is active in Terrain mode**
   - Check material binding
   - Verify RenderTerrain(3-param) is being called
   - Check map mode shader ID

3. **Add shader variant debugging**
   - Use #pragma multi_compile for TERRAIN_DETAIL_MAPPING
   - Enable via material keyword
   - Verify in Unity Frame Debugger

### Blocked Items
- **Blocker:** Detail mapping code not executing in shader
- **Needs:** Understand why `#ifdef TERRAIN_DETAIL_MAPPING` fails
- **Owner:** Need shader debugging expertise

### Questions to Resolve
1. Why does `#define TERRAIN_DETAIL_MAPPING` not enable `#ifdef` block?
2. Is the tessellated shader actually being used in Terrain mode?
3. Should we use `#pragma multi_compile` keyword instead of define?
4. Is there a shader compilation variant issue?

---

## Session Statistics

**Files Changed:** 8
**Files Created:** 3 (TerrainTypeTextureGenerator, DetailTextureArrayLoader, planning doc)
**Lines Added:** ~600
**Tests Added:** 0 (manual testing only)
**Bugs Fixed:** 2 (JPG support, function overloading)
**Status:** Implementation complete but not functional

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- System loads textures correctly (verified in logs)
- Shader parameters set correctly (verified in logs)
- Code inside `#ifdef TERRAIN_DETAIL_MAPPING` never executes (debug returns fail)
- Tessellated shader used: `Hegemon/EU3ClassicTessellated`

**Critical Files:**
- Detail logic: `Assets/Game/Shaders/MapModes/MapModeTerrain.hlsl:33-68`
- Define location: `Assets/Game/VisualStyles/EU3Classic/EU3MapShaderTessellated.shader:150`
- Include location: `Assets/Game/VisualStyles/EU3Classic/EU3MapShaderTessellated.shader:286`

**Gotchas for Next Session:**
- User cannot compile - must tell user to build
- Debug returns inside `#ifdef` block show macro texture (block not executing!)
- Even simple `return float4(1,0,1,1);` at line 37 doesn't show magenta

---

## Links & References

### Related Documentation
- [Planning Doc](../../Planning/terrain-detail-mapping.md)
- [Paradox Graphics Analysis](../personal/PARADOX_GRAPHICS_ARCHITECTURE.md)

### Code References
- Terrain detail shader: `Assets/Game/Shaders/MapModes/MapModeTerrain.hlsl:33-68`
- Texture loading: `Assets/Archon-Engine/Scripts/Map/Loading/DetailTextureArrayLoader.cs`
- Type generation: `Assets/Archon-Engine/Scripts/Map/Loading/TerrainTypeTextureGenerator.cs`

---

*Session log: 2025-11-02 - Terrain detail mapping implementation (blocked on shader execution)*
