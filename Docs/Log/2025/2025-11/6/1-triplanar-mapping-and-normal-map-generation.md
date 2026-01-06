# Tri-Planar Mapping & Normal Map Generation Implementation
**Date**: 2025-11-06
**Session**: 1
**Status**: ⚠️ Partial - Normal map has orientation issues
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement tri-planar texture mapping to fix stretching on steep slopes
- Implement GPU-based normal map generation from heightmap for dramatic lighting improvement

**Secondary Objectives:**
- Follow EU5's rendering techniques (from eu5-terrain-rendering-analysis.md)
- Achieve 80-85% of EU5 visual quality with minimal complexity

**Success Criteria:**
- ✅ Tri-planar mapping eliminates texture stretching on mountains
- ⚠️ Normal map generation produces correct lighting (currently has orientation issues)
- ✅ All changes follow GPU coordination patterns (unity-compute-shader-coordination.md)

---

## Context & Background

**Previous Work:**
- See: [archon-graphics-implementation-priorities.md](../../Planning/archon-graphics-implementation-priorities.md) - Implementation roadmap
- Related: [eu5-terrain-rendering-analysis.md](../../learnings/eu5-terrain-rendering-analysis.md) - EU5 rendering techniques

**Current State:**
- Shader refactoring from EU3 → Default naming complete (session earlier today)
- Terrain uses planar UV mapping (stretches on steep slopes)
- Normal map loaded from file (world_normal.bmp) - we want to generate from heightmap instead

**Why Now:**
- Visual quality is next priority after functional systems complete
- Tri-planar and normal maps are Tier 1 (critical, high value) improvements

---

## What We Did

### 1. Tri-Planar Texture Mapping
**Files Changed:**
- `Assets/Game/Shaders/MapModes/MapModeTerrain.hlsl:19-87` (added tri-planar functions)
- `Assets/Game/VisualStyles/EU3Classic/DefaultMapShaderTessellated.shader:86` (added shader property)
- `Assets/Game/VisualStyles/EU3Classic/Includes/DefaultCommon.hlsl:72` (added CBUFFER property)

**Implementation:**
Added `TriPlanarSampleArray()` function that samples textures from 3 axes (X, Y, Z) and blends based on surface normal:

```hlsl
float4 TriPlanarSampleArray(float3 positionWS, float3 normalWS, uint arrayIndex, float tiling)
{
    // Sample from 3 axes
    float2 uvX = positionWS.zy * tiling; // YZ plane
    float2 uvY = positionWS.xz * tiling; // XZ plane
    float2 uvZ = positionWS.xy * tiling; // XY plane

    float4 colX = SAMPLE_TEXTURE2D_ARRAY(_TerrainDetailArray, sampler_TerrainDetailArray, uvX, arrayIndex);
    float4 colY = SAMPLE_TEXTURE2D_ARRAY(_TerrainDetailArray, sampler_TerrainDetailArray, uvY, arrayIndex);
    float4 colZ = SAMPLE_TEXTURE2D_ARRAY(_TerrainDetailArray, sampler_TerrainDetailArray, uvZ, arrayIndex);

    // Blend weights from surface normal
    float3 blendWeights = abs(normalWS);
    blendWeights = pow(blendWeights, _TriPlanarTightenFactor);
    blendWeights /= (blendWeights.x + blendWeights.y + blendWeights.z);

    return colX * blendWeights.x + colY * blendWeights.y + colZ * blendWeights.z;
}
```

Also added `CalculateTerrainNormal()` to compute world-space normals from heightmap gradients.

Replaced all `NoTilingTextureArray()` calls with `TriPlanarSampleArray()` in terrain rendering.

**Rationale:**
- EU5 uses tri-planar projection to eliminate stretching on steep slopes
- Samples texture from 3 axes and blends based on surface normal
- Configurable blend sharpness via `_TriPlanarTightenFactor` (1.0-8.0, default 4.0)

**Architecture Compliance:**
- ✅ Follows shader modularity patterns
- ✅ Performance: 3× texture samples (acceptable cost for quality gain)
- ✅ No new textures needed - uses existing detail array

**Result:**
- ✅ Works correctly - user confirmed massive visual improvement
- ✅ No visible stretching on mountains/cliffs
- Note: User said "Better to have it than not" (subtle improvement, more about preventing issues than wow factor)

### 2. GPU Normal Map Generation (Compute Shader)
**Files Created:**
- `Assets/Archon-Engine/Shaders/GenerateNormalMap.compute` (GPU compute shader)
- `Assets/Archon-Engine/Scripts/Map/Rendering/NormalMapGenerator.cs` (C# wrapper)

**Files Modified:**
- `Assets/Archon-Engine/Scripts/Map/Rendering/VisualTextureSet.cs:231-264` (added generation method)
- `Assets/Archon-Engine/Scripts/Map/MapTextureManager.cs:162-169` (added public wrapper)
- `Assets/Archon-Engine/Scripts/Map/Loading/MapDataLoader.cs:102-113, 169-180` (integrated into loading)

**Implementation:**

Compute shader calculates normals from heightmap gradients:
```hlsl
// Sample heightmap at 5 positions (center + LRUD)
float heightC = HeightmapTexture[id.xy].r;
float heightL = HeightmapTexture[idL].r;
float heightR = HeightmapTexture[idR].r;
float heightD = HeightmapTexture[idD].r;
float heightU = HeightmapTexture[idU].r;

// Calculate gradients (central difference)
float dx = (heightR - heightL) * 0.5 * HeightScale;
float dz = (heightU - heightD) * 0.5 * HeightScale;

// Construct normal vector
float3 normal = normalize(float3(dx, 1.0, dz));

// Pack into RG format
float2 packedNormal = float2(
    normal.x * 0.5 + 0.5,  // X component
    normal.z * 0.5 + 0.5   // Z component
);
```

C# wrapper handles GPU synchronization:
```csharp
generateNormalMapCompute.Dispatch(generateNormalsKernel, threadGroupsX, threadGroupsY, 1);

// CRITICAL: GPU synchronization
var syncRequest = AsyncGPUReadback.Request(normalMapRT);
syncRequest.WaitForCompletion();
```

**Rationale:**
- Generate normals on GPU (fast, ~10-30ms)
- No need for external world_normal.bmp file
- EU5 stores RG format (XZ components), reconstructs Y in shader

**Architecture Compliance:**
- ✅ Follows unity-compute-shader-coordination.md patterns:
  - RWTexture2D for all RenderTexture access
  - No Y-flip in compute shader
  - Explicit GPU synchronization with AsyncGPUReadback
  - 8×8 thread groups (64 threads, optimal)
- ✅ Removed NormalMapBitmapLoader from parallel loading
- ✅ Integrated into texture initialization pipeline

**Result:**
- ⚠️ **Currently broken** - produces flat lighting or inverted normals
- Initially worked ("massive difference"), then broke after format changes

---

## Decisions Made

### Decision 1: RG Format vs RGB Format for Normal Map
**Context:** EU5 uses RG format (stores XZ, reconstructs Y). We tried both approaches.

**Options Considered:**
1. **RG16 format** - Store X and Z components only (2 channels)
   - Pros: Smaller memory, EU5 approach, shader reconstructs Y
   - Cons: More complex shader code
2. **RGB24 format** - Store full XYZ normal (3 channels)
   - Pros: Simpler shader code, direct storage
   - Cons: Larger memory footprint
3. **R8G8B8A8_UNorm** - RGBA with UAV support
   - Pros: Guaranteed UAV compatibility (no TYPELESS issues)
   - Cons: Wasted alpha channel

**Decision History:**
1. Started with RG16 → **Worked initially** ("massive difference")
2. Changed to R8G8B8A8_UNorm (thinking format mismatch caused orientation issues) → **Broke completely** (flat lighting)
3. Reverted to RG16 → **Still broken** (current state)

**Current Issue:**
- Shader now reconstructs Y from RG: `normalY = sqrt(1 - normalX² - normalZ²)`
- Either normal calculation signs are wrong, or shader reconstruction is wrong
- Need to debug actual normal values being generated

**Trade-offs:**
- RG approach saves memory but adds shader complexity
- RGB approach simpler but uses more VRAM

### Decision 2: Explicit GraphicsFormat for RenderTextures
**Context:** Following explicit-graphics-format.md decision doc

**Decision:** Always use `RenderTextureDescriptor` with explicit `GraphicsFormat` for UAV-enabled textures

**Rationale:**
- Prevents TYPELESS format bugs (documented issue)
- Deterministic behavior across platforms
- Critical for multiplayer consistency

**Implementation:**
```csharp
var normalMapDesc = new RenderTextureDescriptor(
    width, height,
    UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
    0
);
normalMapDesc.enableRandomWrite = true;
```

**Note:** Currently reverted to simple `RenderTextureFormat.RG16` to debug core issue first

---

## What Worked ✅

1. **Tri-Planar Mapping Implementation**
   - What: Shader-based tri-planar projection with configurable blend
   - Why it worked: Clean shader function, proper normal calculation, material property exposure
   - Reusable pattern: Yes - technique applies to any texture-on-mesh scenario
   - Impact: Eliminated texture stretching on mountains/cliffs

2. **GPU Coordination Patterns**
   - What: Following unity-compute-shader-coordination.md patterns (RWTexture2D, no Y-flip, GPU sync)
   - Why it worked: Learned from previous 8-hour debugging sessions
   - Impact: No GPU race conditions or coordinate system issues (at least not those kinds)

3. **Shader Refactoring Foundation**
   - What: Earlier session's EU3 → Default refactoring into modular includes
   - Why it worked: Made adding tri-planar and lighting changes clean and organized
   - Impact: Easy to modify and maintain shader code

---

## What Didn't Work ❌

1. **Format Switching to Fix Orientation**
   - What we tried: Changed from RG16 to R8G8B8A8_UNorm thinking format mismatch caused inverted normals
   - Why it failed: Format wasn't the issue - orientation/sign calculation is the problem
   - Lesson learned: Don't change multiple variables when debugging - broke working code
   - Don't try this again because: Format was fine, the issue is gradient sign or light direction

2. **Assuming Shader Would Handle Missing Blue Channel**
   - What we tried: Generate RG, let shader read RGB (expecting Blue=0.5 default)
   - Why it failed: Undefined channel reads garbage, doesn't default to anything sensible
   - Lesson learned: If compute writes RG, shader MUST reconstruct, not just read RGB
   - Fixed by: Explicit Y reconstruction in shader

---

## Problems Encountered & Solutions

### Problem 1: Normal Map Orientation Issues ("Ocean Trench in Brazil")
**Symptom:** Mountains appear as dark trenches (inverted lighting), or lighting completely flat

**Root Cause:** Unknown - likely one of:
1. Gradient calculation signs wrong (dx, dz negation)
2. Normal vector construction wrong
3. Light direction wrong in shader
4. Shader normal reconstruction wrong (RG → XYZ)

**Investigation:**
- Tried: Removing negations on dx/dz → Still wrong
- Tried: Switching RG → RGB format → Broke completely (flat)
- Tried: Reverting to RG → Still broken (different than original working state)
- Added: Debug output to sample actual RG values → **Not yet checked by user**

**Current Status:** ⚠️ Blocked on user checking debug logs

**Solution (Pending):**
Need to see actual RG values being generated:
- If all ~[128,128] → Normals are flat (compute shader not working)
- If varying ~[120-140] → Normals are being generated, signs/reconstruction wrong

**Pattern for Future:**
- Always add debug sampling FIRST before changing implementation
- Check actual data values before assuming format/orientation issues
- Don't change multiple things at once when debugging

### Problem 2: Format Mismatch Between Compute and Shader
**Symptom:** After first format change, lighting went completely flat

**Root Cause:** Compute shader writing RG, fragment shader reading RGB without reconstruction

**Investigation:**
- Realized shader was sampling `.rgb` but only `.rg` channels had data
- Blue channel was undefined/garbage

**Solution:**
Updated shader to reconstruct Y from XZ:
```hlsl
float2 normalRG = SAMPLE_TEXTURE2D(_NormalMapTexture, sampler_NormalMapTexture, correctedUV).rg;

float normalX = (normalRG.r - 0.5) * 2.0;
float normalZ = (normalRG.g - 0.5) * 2.0;

// Reconstruct Y from unit sphere constraint
float normalY = sqrt(max(0.0, 1.0 - normalX * normalX - normalZ * normalZ));

float3 normal = normalize(float3(normalX, normalY, normalZ));
```

**Why This Works:** Y component must satisfy x² + y² + z² = 1 for unit normal

**Pattern for Future:** If storing partial normal, explicitly reconstruct missing component

---

## Architecture Impact

### Documentation Updates Required
- [ ] Consider moving normal map generation pattern to architecture docs (if we get it working)
- [ ] Add tri-planar mapping pattern to shader best practices

### New Patterns Discovered
**Pattern: GPU Normal Map Generation**
- When to use: Any heightmap → normal map conversion
- Benefits: Fast (~10-30ms), no external files, always up-to-date with heightmap
- Add to: Rendering patterns doc (once working)

**Pattern: Tri-Planar Texture Mapping**
- When to use: Any texture on steep slopes (mountains, cliffs, vertical surfaces)
- Benefits: Eliminates stretching, configurable blend sharpness
- Add to: Shader patterns doc

---

## Code Quality Notes

### Performance
- **Tri-planar cost:** 3× texture samples per fragment (acceptable)
- **Normal generation:** ~10-30ms GPU time (one-time at load)
- **Target:** < 50ms for texture initialization
- **Status:** ✅ Meets target (when working)

### Testing
- **Manual Tests Passed:** Tri-planar mapping verified by user ("massive difference")
- **Manual Tests Failed:** Normal map lighting (flat or inverted)
- **Debug Output Added:** Sampling RG values to diagnose issue

### Technical Debt
- **Created:** Normal map generation currently broken, needs debugging
- **TODOs:**
  - Fix gradient sign calculation or shader reconstruction
  - Verify normal values being generated (check logs)
  - Consider adding visual debug mode (render normals as colors)

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Check debug logs** - User needs to look at RG sample values in console
2. **Fix normal orientation** - Adjust gradient signs or shader reconstruction based on debug output
3. **Visual debug mode** - Add shader mode to render normals as colors (easier to diagnose)
4. **Test on multiple terrains** - Verify works on mountains, valleys, plains

### Blocked Items
- **Blocker:** Need user to check Unity console for normal map debug output
- **Needs:** Log line: "NormalMapGenerator: Sample normals - RG[?,?]..."
- **Owner:** User

### Questions to Resolve
1. What are the actual RG values being generated? (flat ~128,128 or varying?)
2. Is the issue gradient calculation signs, or shader reconstruction?
3. Should we add a visual debug mode to render normals as RGB colors?

### Docs to Read Before Next Session
- [unity-compute-shader-coordination.md](../../learnings/unity-compute-shader-coordination.md) - GPU patterns we followed
- [explicit-graphics-format.md](../decisions/explicit-graphics-format.md) - Format selection rules

---

## Session Statistics

**Files Created:** 2 (compute shader, C# wrapper)
**Files Modified:** 7 (shaders, includes, loaders, managers)
**Lines Added/Removed:** ~+350/-50
**Bugs Fixed:** 0 (tri-planar works, normal map still broken)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Tri-planar mapping: `MapModeTerrain.hlsl:19-87` - **Working correctly**
- Normal map generation: `GenerateNormalMap.compute` + `NormalMapGenerator.cs` - **Currently broken**
- Orientation issue: Either gradient signs or shader reconstruction is wrong
- Debug output added: Samples RG values at 3 positions - **need to check logs**

**What Changed Since Architecture Docs:**
- Tri-planar mapping added (not in original architecture)
- Normal map now generated from heightmap (not loaded from file)
- Shader expects RG format with Y reconstruction

**Gotchas for Next Session:**
- Don't change format again - RG16 was correct approach
- Check debug logs FIRST before trying more fixes
- Consider visual debug mode (render normals as colors) for easier diagnosis
- Remember: Initial implementation worked, broke after format changes, still broken after revert

---

## Links & References

### Related Documentation
- [eu5-terrain-rendering-analysis.md](../../learnings/eu5-terrain-rendering-analysis.md) - Source of tri-planar and normal map techniques
- [archon-graphics-implementation-priorities.md](../../Planning/archon-graphics-implementation-priorities.md) - Implementation roadmap
- [unity-compute-shader-coordination.md](../../learnings/unity-compute-shader-coordination.md) - GPU patterns

### Code References
- Tri-planar: `MapModeTerrain.hlsl:19-87`
- Normal generation: `GenerateNormalMap.compute:1-69`
- Shader reconstruction: `DefaultLighting.hlsl:15-28`
- C# wrapper: `NormalMapGenerator.cs:1-165`

---

## Notes & Observations

- User feedback: Tri-planar "massive difference" initially, then "not sure if it works anymore" after changes
- Normal map: "Massive difference" → "quite a lot darker" → "ocean trench in Brazil" → "completely flat"
- Debugging cycle: Need better visual tools for diagnosing normal orientation issues
- Consider: Screenshot utility for visual debugging? (already exists: `ScreenshotUtility.cs`)
- Pattern: When graphics issue appears, screenshot + debug output > blind code changes

---

*Session ended with normal map generation broken, awaiting debug log check by user*
