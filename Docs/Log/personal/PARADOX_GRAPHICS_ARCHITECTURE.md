# Paradox Graphics Architecture Analysis
**Subject:** Europa Universalis 5 (EU5) Graphics Techniques
**Analysis Date:** 2025-10-27
**Reference:** `Archon-Engine/Docs/eu5.png`

## Executive Summary

Analysis of EU5's graphics reveals a sophisticated rendering architecture built on **resolution-independent techniques**. Three core systems work together to deliver sharp, detailed visuals at any zoom level:

1. **3D Terrain** - GPU tessellation with heightmap displacement
2. **Detail Mapping** - World-space texture tiling for infinite resolution textures
3. **GPU Instancing** - Thousands of 3D objects rendered in single draw calls

All three techniques are **scale-independent** - they maintain quality regardless of zoom level, similar to Archon's vector curve border solution.

---

## 1. 3D Terrain Rendering

### The Problem
Traditional 2D map rendering (EU4-style) lacks depth, shadows, and realistic terrain representation. Mountains appear flat regardless of camera angle.

### The Solution: GPU Tessellation + Displacement Mapping

**Core Technique:**
- Start with flat quad mesh
- GPU subdivides into thousands of triangles (tessellation)
- Each vertex displaced vertically based on heightmap
- Result: Actual 3D geometry with real depth

**Key Insight from EU5 Screenshot:**
> Mountains covering only 15-50 pixels in source heightmap look smooth and detailed on screen. The tessellation doesn't add detail - it adds **smoothness**. GPU texture filtering interpolates the low-res heightmap across high-density geometry.

### Implementation Details

**Shader Pipeline:**
```
Vertex Shader → Hull Shader → Tessellator → Domain Shader → Fragment Shader
```

**Hull Shader** - Determines tessellation factors:
```hlsl
[domain("tri")]
[partitioning("fractional_odd")]
[patchconstantfunc("PatchConstant")]
PatchConstantOutput PatchConstant(InputPatch<VertexOutput, 3> patch)
{
    // Distance-based LOD
    float distance = CalculateCameraDistance(patch);
    float tessFactor = lerp(32, 4, saturate(distance / _MaxDistance));

    output.edge[0] = tessFactor;
    output.edge[1] = tessFactor;
    output.edge[2] = tessFactor;
    output.inside = tessFactor;

    return output;
}
```

**Domain Shader** - Generates new vertices with heightmap displacement:
```hlsl
[domain("tri")]
VertexOutput Domain(PatchConstantOutput patchConst,
                    float3 barycentricCoords : SV_DomainLocation,
                    const OutputPatch<HullOutput, 3> patch)
{
    // Interpolate position and UV from original triangle
    output.positionOS = BARYCENTRIC_INTERPOLATE(positionOS);
    output.uv = BARYCENTRIC_INTERPOLATE(uv);

    // Sample heightmap with GPU filtering
    float height = SAMPLE_TEXTURE2D_LOD(_HeightMap, sampler_HeightMap, output.uv, 0).r;

    // Displace vertex vertically
    output.positionOS.y += height * _HeightScale;

    output.positionCS = TransformObjectToHClip(output.positionOS);
    return output;
}
```

### Technical Requirements

- **Shader Model:** 4.6 minimum (DX11+)
- **Pipeline Support:** Works in Unity URP with custom HLSL
- **Tessellation Factors:** Conservative (4-32 range) - not millions of triangles
- **Heightmap Resolution:** 2K-4K sufficient for entire world map
- **Performance:** GPU-bound, automatic LOD via distance-based factors

### Key Benefits

1. **Real 3D geometry** - Mountains occlude, cast shadows, have silhouettes
2. **Smooth curves** - No faceted/blocky appearance despite low-res heightmap
3. **Automatic LOD** - Tessellation factor adjusts with camera distance
4. **Memory efficient** - Single quad on CPU, expanded on GPU
5. **Province selection preserved** - UV coordinates maintained through tessellation

### Unity URP Implementation

**Proven Working Examples:**
- CJT-Jackton/URP-Geometry-Shader-Example (GitHub)
- bearworks/URPOceanTessellation (GitHub)
- Multiple commercial projects using custom HLSL

**Shader Pragma Requirements:**
```hlsl
#pragma target 4.6
#pragma vertex TessVert
#pragma hull Hull
#pragma domain Domain
#pragma fragment frag
```

---

## 2. Detail Texture Mapping

### The Problem
Single terrain texture stretched across map becomes pixelated when zoomed in. EU4 uses seasonal textures (512×512 to 2048×2048) that look acceptable zoomed out but blurry up close.

### The Solution: Macro/Micro Texture Blending

**Two-Layer System:**

1. **Macro Texture (Color Map)** - 2K-4K for entire world
   - Low resolution but unique colors per region
   - Stretched across map using standard 0-1 UVs
   - Provides overall "painting" of terrain types

2. **Micro Texture (Detail Map)** - 512×512 to 1K, tiled
   - High resolution detail (grass blades, dirt, rock grain)
   - **Tiled hundreds of times using world-space UVs**
   - Generic but infinitely sharp

**The Magic: World-Space UV Coordinates**

Traditional approach (pixelated when zoomed):
```hlsl
float2 uv = input.mapUV; // 0-1 across entire map
float4 color = SAMPLE_TEXTURE2D(_TerrainTexture, sampler, uv);
// Single texture stretched - gets blurry
```

Resolution-independent approach:
```hlsl
// Macro - unique colors (same as before)
float4 macroColor = SAMPLE_TEXTURE2D(_TerrainMacro, sampler, input.mapUV);

// Micro - tiled detail using world position
float2 worldUV = input.worldPos.xz * _DetailTiling; // e.g., * 100
float4 microDetail = SAMPLE_TEXTURE2D(_DetailTexture, sampler, worldUV);

// Blend
float4 finalColor = macroColor * microDetail;
```

**Why This Works:**
- Camera zooms in → world-space distance per pixel decreases
- Detail texture samples more densely → stays sharp
- Like vector curves: **scale-independent**

### Advanced: Terrain Splatting

Multiple detail textures blended based on terrain type:

```hlsl
// Control map (R=grass, G=dirt, B=rock, A=snow)
float4 control = SAMPLE_TEXTURE2D(_TerrainControl, sampler, input.mapUV);

// Sample tiled detail textures (world-space UVs)
float2 worldUV = input.worldPos.xz * _DetailTiling;
float4 grassDetail = SAMPLE_TEXTURE2D(_GrassDetail, sampler, worldUV);
float4 dirtDetail = SAMPLE_TEXTURE2D(_DirtDetail, sampler, worldUV);
float4 rockDetail = SAMPLE_TEXTURE2D(_RockDetail, sampler, worldUV);
float4 snowDetail = SAMPLE_TEXTURE2D(_SnowDetail, sampler, worldUV);

// Blend based on control map weights
float4 finalDetail = grassDetail * control.r
                   + dirtDetail * control.g
                   + rockDetail * control.b
                   + snowDetail * control.a;

// Apply to macro color
finalColor = macroColor * finalDetail;
```

### Implementation for Archon

Archon already has macro texture (`_ProvinceTerrainTexture`). Adding detail mapping requires:

```hlsl
// Properties
_TerrainDetailTexture ("Terrain Detail (Tiled)", 2D) = "white" {}
_DetailTiling ("Detail Tiling", Float) = 100.0
_DetailStrength ("Detail Strength", Range(0, 1)) = 0.5

// Fragment shader addition
float4 frag(Varyings input) : SV_Target
{
    // Existing terrain rendering
    float4 baseColor = RenderTerrain(provinceID, input.uv);

    // Add detail mapping
    float2 worldUV = input.positionWS.xz * _DetailTiling;
    float4 detail = SAMPLE_TEXTURE2D(_TerrainDetailTexture, sampler, worldUV);

    // Blend (detail texture should be neutral gray in mid-tones)
    baseColor.rgb = baseColor.rgb * lerp(1.0, detail.rgb * 2.0, _DetailStrength);

    return baseColor;
}
```

### Anti-Tiling Enhancement (Optional)

**The Tiling Problem:**
Repeating detail textures create obvious patterns that break immersion. Even high-quality tileable textures show repetition at large scales.

**Solution: Inigo Quilez's Texture Repetition Technique**

Used by Symphony of Empires (see `SYMPHONY_OF_EMPIRES_ANALYSIS.md` section 6.4) and many AAA games. Breaks up tiling by:
1. Sampling noise texture for random offset
2. Taking **two** samples of detail texture with different offsets
3. Blending samples based on noise and color difference

**Implementation:**
```hlsl
// Additional properties
_NoiseTexture ("Anti-Tiling Noise", 2D) = "gray" {}

// Anti-tiling function (based on Inigo Quilez method)
// Reference: https://iquilezles.org/articles/texturerepetition/
float4 NoTilingTexture(sampler2D tex, float2 uv, sampler2D noiseTex)
{
    // Low-frequency noise lookup
    float k = SAMPLE_TEXTURE2D(noiseTex, sampler_LinearRepeat, 0.005 * uv).r;

    // Generate two random offset values
    float l = k * 8.0;
    float f = frac(l);
    float ia = floor(l);
    float ib = ia + 1.0;

    // Hash to 2D offsets (breaks up patterns)
    float2 offa = sin(float2(3.0, 7.0) * ia);
    float2 offb = sin(float2(3.0, 7.0) * ib);

    // Sample texture twice with different offsets
    // Use explicit gradients to maintain proper mipmapping
    float2 dx = ddx(uv);
    float2 dy = ddy(uv);
    float4 cola = SAMPLE_TEXTURE2D_GRAD(tex, sampler_LinearRepeat, uv + offa, dx, dy);
    float4 colb = SAMPLE_TEXTURE2D_GRAD(tex, sampler_LinearRepeat, uv + offb, dx, dy);

    // Blend based on noise and color difference (reduces seams)
    float4 diff = cola - colb;
    float blend = smoothstep(0.2, 0.8, f - 0.1 * (diff.x + diff.y + diff.z));
    return lerp(cola, colb, blend);
}

// Fragment shader with anti-tiling
float4 frag(Varyings input) : SV_Target
{
    float4 baseColor = RenderTerrain(provinceID, input.uv);

    // World-space UVs for detail
    float2 worldUV = input.positionWS.xz * _DetailTiling;

    // Anti-tiled detail sample
    float4 detail = NoTilingTexture(_TerrainDetailTexture, worldUV, _NoiseTexture);

    baseColor.rgb = baseColor.rgb * lerp(1.0, detail.rgb * 2.0, _DetailStrength);
    return baseColor;
}
```

**Performance Cost:**
- +1 noise texture sample (low-res, very cheap)
- 2× detail texture samples (instead of 1)
- Additional math (sin, smoothstep)
- **Total: ~2-3ms on modern GPUs**

**Visual Benefit:**
- Completely eliminates obvious tiling patterns
- Industry-standard technique (used in AAA games)
- Worth the cost for professional visual quality

**Noise Texture Requirements:**
- 256×256 or 512×512 grayscale texture
- Low-frequency noise (Perlin or simplex)
- Can be reused across all detail textures
- Generate once, use everywhere

### Key Benefits

1. **Resolution independence** - Stays sharp at any zoom level
2. **Memory efficient** - Small tiled textures vs massive unique textures
3. **Artist friendly** - Standard tileable texture creation workflow
4. **Performance** - One additional texture sample, minimal cost
5. **Flexible** - Easy to add seasonal variation, biome-specific details

---

## 3. GPU Instanced Detail Objects

### The Problem
Thousands of cities, buildings, units, and decorative objects (gravestones, trees) need to render without performance collapse.

### The Solution: GPU Instancing

**Traditional Rendering (Unacceptable):**
```csharp
// 10,000 draw calls - kills performance
for (int i = 0; i < 10000; i++)
{
    DrawMesh(soldierModel, positions[i], rotations[i]);
}
```

**GPU Instancing (Single Draw Call):**
```csharp
// ONE draw call for 10,000 instances
Graphics.DrawMeshInstanced(
    soldierModel,      // Mesh sent once
    positions,         // 10,000 positions
    rotations,         // 10,000 rotations
    10000             // Instance count
);
```

### How It Works

**GPU receives:**
1. One mesh (soldier model)
2. Array of transformation matrices (position, rotation, scale)
3. Per-instance properties (colors, texture atlas coordinates)

**GPU automatically:**
1. Duplicates the mesh 10,000 times
2. Applies different transformation to each
3. Renders all in one operation

### LOD System for Cities

EU5 uses distance-based LOD with instancing:

**Far (Strategic View):**
- City = single icon billboard
- 5,000 cities = 1 draw call

**Medium Zoom:**
- City = 5-10 major building models
- 100 visible cities × 10 buildings = 1,000 instances
- Grouped by model type = ~10 draw calls

**Close Zoom (Detail View):**
- City = hundreds of detail objects
- 100 cities × 40 objects per city = 4,000 instances
- Churches: 500 instances = 1 draw call
- Houses: 2,000 instances = 1 draw call
- Gravestones: 1,000 instances = 1 draw call
- Trees: 500 instances = 1 draw call
- **Total: 4 draw calls for all city detail**

### Archon Implementation

Already implemented in `Map.Rendering.InstancedBillboardRenderer`:

```csharp
public abstract class InstancedBillboardRenderer : MonoBehaviour
{
    protected List<Matrix4x4> matrices;
    protected MaterialPropertyBlock propertyBlock;

    protected virtual void LateUpdate()
    {
        if (matrices.Count > 0 && material != null)
        {
            Graphics.DrawMeshInstanced(
                quadMesh,
                0,
                material,
                matrices,
                propertyBlock
            );
        }
    }
}
```

**Shader Support:**
```hlsl
#pragma multi_compile_instancing

UNITY_INSTANCING_BUFFER_START(Props)
    UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceColor)
    UNITY_DEFINE_INSTANCED_PROP(float4, _AtlasRect)
UNITY_INSTANCING_BUFFER_END(Props)

// Access per-instance data
float4 color = UNITY_ACCESS_INSTANCED_PROP(Props, _InstanceColor);
```

### Billboarding Technique

For sprites/icons that should face camera:

```hlsl
// Billboard: face camera (Y-axis rotation only, stay upright)
float3 positionWS = TransformObjectToWorld(float3(0, 0, 0));

// Get camera direction (flatten Y to keep upright)
float3 cameraForward = normalize(GetCameraPositionWS() - positionWS);
cameraForward.y = 0;
cameraForward = normalize(cameraForward);

float3 up = float3(0, 1, 0);
float3 right = normalize(cross(up, cameraForward));

// Reconstruct billboard matrix
float3 localPos = input.positionOS.x * right + input.positionOS.y * up;
float3 billboardedPos = positionWS + localPos;
```

### Texture Atlas Support

Multiple sprites in single texture, per-instance UV offset:

```hlsl
// Per-instance atlas rectangle (x, y, width, height)
float4 atlasRect = UNITY_ACCESS_INSTANCED_PROP(Props, _AtlasRect);

// Remap UVs to atlas region
output.uv = input.uv * atlasRect.zw + atlasRect.xy;
```

### Key Benefits

1. **Massive instance counts** - 10,000+ objects in single draw call
2. **Per-instance variation** - Colors, atlas coordinates, scales
3. **Automatic batching** - GPU handles all duplication
4. **CPU efficient** - Send data once, GPU does the work
5. **Scalable** - Same code for 10 or 10,000 instances

---

## Architecture Comparison

### Archon Current State (Before Investigation)

✅ **Already Solved:**
- Vector curve borders (resolution-independent, sharp at any zoom)
- GPU instanced billboards for units/cities
- Hot/cold data separation in simulation
- Single draw call map rendering
- Province selection via texture lookup

❌ **Missing (Now Identified):**
- 3D terrain geometry (tessellation + displacement)
- Detail texture mapping (world-space tiling)
- LOD system for city details
- Multiple instanced object types

### EU5 Graphics Stack

1. **Terrain:** Tessellated mesh + heightmap displacement
2. **Textures:** Macro color map + micro detail tiles (world-space UVs)
3. **Objects:** GPU instanced 3D models with LOD
4. **Borders/Rivers:** Vector curves (resolution-independent)

### Technology Parity Path

Archon can achieve EU5-level graphics by adding:

| Feature | Status | Implementation Path |
|---------|--------|---------------------|
| **Vector Borders** | ✅ Complete | Already solved with Bézier curves |
| **GPU Instancing** | ✅ Complete | InstancedBillboardRenderer implemented |
| **Tessellation** | ⚠️ Needed | Add hull/domain shaders to MapCore |
| **Detail Mapping** | ⚠️ Needed | Add world-space UV tiling to fragment shader |
| **City LOD** | ⚠️ Needed | Expand instancing with distance-based model swapping |

---

## Implementation Priority

### Phase 1: Detail Texture Mapping (Easiest)
**Effort:** ~2-4 hours (basic), +2-3 hours (with anti-tiling)
**Impact:** Immediate visual quality improvement

Add to existing `MapCore.shader`:
- Detail texture property
- World-space UV calculation
- Blend operation in fragment shader
- **Optional:** Anti-tiling (Quilez method) for professional quality

No architecture changes required.

**Recommended:** Implement basic detail mapping first, add anti-tiling later if tiling patterns are noticeable.

### Phase 2: GPU Tessellation (Medium)
**Effort:** ~8-12 hours
**Impact:** True 3D terrain with depth and shadows

Create `MapCoreTessellated.shader`:
- Hull shader for tessellation factors
- Domain shader for heightmap displacement
- Distance-based LOD system
- Preserve existing province selection

Uses existing heightmap, no data changes required.

### Phase 3: City Detail LOD (Complex)
**Effort:** ~16-24 hours
**Impact:** Rich city visualization at close zoom

Extend `InstancedBillboardRenderer`:
- Multiple model types (buildings, decorations)
- Distance-based LOD switching
- Procedural city layout generation
- Integration with city development data

Requires 3D model assets and placement logic.

---

## Technical Constraints

### Hardware Requirements

**Minimum (Current Archon):**
- DX10 / Shader Model 4.0
- URP support
- Basic instancing

**For Full EU5-Parity Graphics:**
- DX11 / Shader Model 4.6 (tessellation requirement)
- 2GB+ VRAM recommended
- Modern GPU (2016+)

**Platform Impact:**
- ✅ PC: Full support
- ✅ Mac: Full support (Metal tessellation)
- ❌ Mobile: Tessellation not supported (fallback to flat terrain)
- ❌ Web: Limited tessellation support

### Performance Characteristics

**Detail Mapping:**
- Cost: +1 texture sample per pixel
- Impact: <5% frame time
- Memory: +10-50MB (detail textures)

**Tessellation:**
- Cost: Variable GPU load based on tessellation factors
- Impact: 10-30% frame time (distance-dependent)
- Memory: Minimal (+0MB, computed on GPU)

**GPU Instancing:**
- Cost: Nearly free compared to individual draw calls
- Impact: 10,000 instances ≈ cost of 10 draw calls
- Memory: Per-instance data buffers (~1KB per 100 instances)

---

## Key Insights

### Resolution Independence Philosophy

All three techniques share a common principle: **scale-independent rendering**.

1. **Vector borders** - Mathematical curves, not pixels
2. **Detail mapping** - World-space tiling, not stretched textures
3. **Tessellation** - GPU subdivision, not fixed geometry

This philosophy allows rendering at any resolution or zoom level while maintaining quality.

### The "15-50 Pixel" Revelation

> Mountains in EU5 covering only 15-50 pixels in source data look detailed because:
> - GPU bilinear filtering smooths low-res heightmap
> - Tessellation provides geometry density for smooth curves
> - Normal maps add perceived micro-detail
> - Lighting reveals 3D form

**Lesson:** Don't need massive source resolution - need smart rendering techniques.

### Instancing Scales Linearly

Draw call count determined by **unique model types**, not instance count:

- 10 soldier instances = 1 draw call
- 10,000 soldier instances = 1 draw call
- 10,000 soldiers + 5,000 archers = 2 draw calls

**Lesson:** Asset variety has more performance impact than quantity.

### Shader Complexity Budget

Modern GPUs handle complex shaders efficiently:

- Detail mapping: +1 texture sample ≈ negligible
- Anti-tiling (Quilez method): +1 noise + 2× detail samples ≈ 2-3ms
- Tessellation: Scales with distance (automatic LOD)
- Instancing: Same shader cost regardless of count

**Lesson:** Shader complexity less important than draw call count. Even "expensive" techniques like anti-tiling are cheap compared to CPU-side operations.

---

## Comparison with Other Solutions

### Mesh Generation (Alternative to Tessellation)

**Pros:**
- Works on all hardware
- Predictable performance
- Easier debugging

**Cons:**
- CPU mesh generation cost
- Fixed LOD levels (popping during transitions)
- Memory overhead (multiple LOD meshes)
- Manual LOD management

**Verdict:** Tessellation superior for terrain - automatic, smooth, GPU-based.

### Mega-Textures (Alternative to Detail Mapping)

**Pros:**
- Completely unique texturing
- No tiling patterns

**Cons:**
- Massive memory requirements (GBs)
- Streaming complexity
- Loading times
- Overkill for grand strategy

**Verdict:** Detail mapping better fit - efficient, sharp, simple.

### Manual Draw Calls (Alternative to Instancing)

**Pros:**
- Simple to understand
- Maximum flexibility per-object

**Cons:**
- CPU bottleneck at >100 draw calls
- Doesn't scale to thousands of objects
- Kills performance

**Verdict:** Instancing essential for grand strategy scale.

---

## References

### Proven Working Examples

**Tessellation in Unity URP:**
- [CJT-Jackton/URP-Geometry-Shader-Example](https://github.com/CJT-Jackton/URP-Geometry-Shader-Example) - Complete hull/domain shader example
- [bearworks/URPOceanTessellation](https://github.com/bearworks/URPOceanTessellation) - Ocean waves with tessellation
- [Daniel Ilett's Stylized Grass Tutorial](https://danielilett.com/2021-08-24-tut5-17-stylised-grass/) - Grass with tessellation

**Detail Mapping Techniques:**
- [Catlike Coding: Triplanar Mapping](https://catlikecoding.com/unity/tutorials/advanced-rendering/triplanar-mapping/)
- [Rastertek: Terrain Detail Mapping](https://www.rastertek.com/tertut13.html)
- [Unity HDRP: Detail Maps Documentation](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@13.1/manual/Mask-Map-and-Detail-Map.html)

**GPU Instancing:**
- Unity Documentation: [Graphics.DrawMeshInstanced](https://docs.unity3d.com/ScriptReference/Graphics.DrawMeshInstanced.html)
- [GPU Instancing in URP](https://docs.unity3d.com/Manual/GPUInstancing.html)

### Educational Resources

- [Ben Golus: Normal Mapping for Triplanar Shader](https://bgolus.medium.com/normal-mapping-for-a-triplanar-shader-10bf39dca05a)
- [NedMakesGames: Mastering Tessellation Shaders](https://nedmakesgames.medium.com/mastering-tessellation-shaders-and-their-many-uses-in-unity-9caeb760150e)
- [Alan Zucconi: Interactive Map Shader](https://www.alanzucconi.com/2019/07/03/interactive-map-01/)

---

## Conclusion

Paradox's modern graphics architecture (EU5) achieves high visual fidelity through three core resolution-independent techniques:

1. **GPU Tessellation** - Smooth 3D terrain from low-res heightmaps
2. **Detail Mapping** - Sharp textures via world-space tiling
3. **GPU Instancing** - Thousands of objects in minimal draw calls

All three techniques are **proven to work in Unity URP** with custom HLSL shaders. Archon Engine already implements GPU instancing and has the architectural foundation (single draw call rendering, texture-based systems) to integrate the remaining techniques.

**The path to graphical parity with Paradox is clear and achievable.**

---

*Analysis conducted 2025-10-27*
*Reference material: `Archon-Engine/Docs/eu5.png`*
*Archon Engine: 80,000 lines in 109 hours*
