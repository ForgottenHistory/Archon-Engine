# EU5 Terrain Rendering System - Technical Analysis
**Date**: 2025-11-04
**Source**: RenderDoc analysis of Europa Universalis 5 shaders (vertex + pixel, 1729 lines compiled LLVM IR)
**Purpose**: Document EU5's rendering architecture for future reference

---

## Executive Summary

**Core Architecture**: Virtual texturing + 256-material biome system + deferred PBR + signed distance field borders + procedural effects

**Key Insight**: EU5 achieves AAA visual quality through **separation of concerns** - static geometry (terrain mesh), streaming detail (virtual textures), dynamic appearance (material blending), and political overlay (borders/colors) operate independently at different scales.

**Most Critical Discovery**: The "infinite scale" principle - **continuous data + procedural generation + sub-pixel precision** enables unlimited detail without memory/resolution constraints. Same principle across terrain height, material blending, border rendering, and effects.

**Shader Complexity**: 1729-line pixel shader (2-3× normal AAA complexity) processes 5 render targets simultaneously, integrating terrain materials, political borders, devastation overlays, fog of war, and flat map transitions into single unified pass.

---

## Core Rendering Architecture

### Principle 1: Virtual Texturing for Unlimited Detail
**What**: Sparse texture streaming via clipmap indirection

**How It Works**:
- **Indirection texture** (low-res, Texture2DArray) stores page IDs per mip level
- **Physical texture cache** (high-res, Texture2D) holds actually loaded pages
- **LOD calculation**: Distance-based mip selection (31 levels supported)
- **Page streaming**: Load/unload 512×512 tiles on-demand

**Shader Implementation** (pixel.txt:545-606):
- Calculate mip level from camera distance: `log2(distance² × scale²) × 0.5 + 1`
- Look up page ID in indirection texture at (world_x, world_y, mip_level)
- Calculate UV offset within physical page
- Sample from physical cache with bilinear filtering

**Why Effective**: Enables **16k+ heightmaps** without GPU memory explosion. Only loads detail where camera sees it.

**Memory Cost**: Indirection texture (few MB) + physical cache (fixed size, e.g., 4096×4096 = 64MB)

---

### Principle 2: 256-Material Biome System with Tri-Planar Projection
**What**: Arbitrary material count via Texture2DArray + weight blending

**Architecture**:
- **STerrain2MaterialHandles[256]**: Array of material texture indices (diffuse/normal/properties per material)
- **STerrain2Biome[200]**: 200 biomes, each with **int4[16]** = 16 material slots per biome
- **Material assignment**: Per-vertex or per-pixel biome ID → look up 16 materials → blend by weights
- **Total combinations**: 200 biomes × 16 materials = 3,200 possible material configurations

**Shader Constants** (pixel.txt:30-33):
```
STerrain2MaterialHandles[256]  // diffuse/normal/properties texture indices
STerrain2Biome[200]            // 200 biomes
int4[16]                       // 16 material slots per biome
```

**Tri-Planar Mapping** (pixel.txt:613):
- `_TriPlanarUVTightenFactor`: Controls blend sharpness between X/Y/Z projections
- Sample textures from 3 axes, blend by surface normal
- **Eliminates stretching** on steep slopes (mountains, cliffs)
- Formula: `weight = abs(normal)^tightenFactor`, normalized across XYZ

**Why Necessary**: Photo textures tile badly on vertical surfaces without tri-planar. Mountains would look smeared with only planar projection.

---

### Principle 3: Deferred Rendering Pipeline (5 Render Targets)
**What**: Separate geometry rendering from lighting calculation

**Outputs** (pixel.txt:50-54):
- **SV_Target0**: Albedo RGB + AO (base color + ambient occlusion)
- **SV_Target1**: World-space normals (for lighting)
- **SV_Target2**: Material properties (metallic, roughness, custom data)
- **SV_Target3**: Political overlay (province colors, borders, alpha for blending)
- **SV_Target4**: Emissive/effects (devastation, fog of war, special highlights)

**Why Deferred**: Decouples material complexity from lighting complexity. Can have 256 materials without 256× lighting calculations. Lighting pass processes screen once, reads from render targets.

**Memory Cost**: 5× screen resolution (e.g., 1080p = 5× 2MP ≈ 40MB uncompressed, can use BC compression)

**Trade-off**: More memory, slight CPU overhead for render target management, but enables complex lighting with minimal performance impact.

---

### Principle 4: Signed Distance Field Borders with Noise
**What**: Distance field texture for smooth borders, enhanced with procedural noise

**Border System** (pixel.txt:924-999):
- **BorderDistanceFieldTexture**: R8_UNORM, likely 1/4 to 1/2 resolution
- **9-tap sampling**: Center + 8 neighbors at ±0.75 offset (lines 924-959)
- **Average of 9 samples**: `(sum / 9)` creates anti-aliased edges
- **Gradient rendering**: Two-layer system (sharp edge + soft gradient)

**Multi-Tap Pattern Purpose**:
- **Anti-aliasing**: Averaging 9 samples smooths pixelation
- **Noise**: Samples at fractional offsets capture sub-pixel detail
- **Performance**: 9 taps acceptable cost for high-quality borders

**Gradient Parameters** (pixel.txt:960-999):
- `GB_GradientAlphaInside/Outside`: Alpha at inner/outer gradient bounds
- `GB_GradientWidth`: Distance over which gradient transitions
- `GB_EdgeWidth`: Sharp edge width (before gradient starts)
- `GB_EdgeSmoothness`: Smoothstep factor for edge transition

**Secondary Borders** (pixel.txt:1000-1088):
- **SecondaryProvinceColorsOffset**: Offset for alliance/region border colors
- **Cosine-based pattern**: `cos(uv × frequency)` creates noise/dashes (lines 1021-1050)
- **Blend with primary**: Smooth alpha blending between primary/secondary borders

**Why Sophisticated**: Creates signature Paradox border look - **soft gradient + crisp edge** simultaneously. Not just a line, but a designed visual element with artistic control.

---

### Principle 5: Procedural Devastation System with Bezier Curves
**What**: Artist-directable war damage via cubic Bezier curve blending

**Bezier Evaluation** (pixel.txt:625-723):
- **Control points**: DevastationBezierPoint1/Point2 (artist-authored curves)
- **Cubic Bezier formula**: Evaluates devastation value through parametric curve
- **Iterative refinement**: 5 iterations to find precise blend factor (lines 641-723)
- **Purpose**: Non-linear blending (sudden burn vs gradual decay)

**HSV Color Shifting** (pixel.txt:784-826):
- **Convert RGB → HSV**: Calculate hue/saturation/value from diffuse texture
- **Apply devastation offsets**: Shift hue (burnt brown), reduce saturation (grey), darken value
- **Convert HSV → RGB**: Reconstruct RGB from modified HSV
- **Blend with original**: Lerp by devastation factor

**Height-Based Masking** (pixel.txt:827-845):
- Devastation affects **high terrain more** (peaks burn first, valleys last)
- Formula: `(1 - height + heightScale) × devastation × 0.833`
- **Smoothstep blend**: Gradual transition, not hard cutoff

**Artist Control**: Bezier curves allow non-linear response. Linear blending (0.5 devastation = 50% burnt) too predictable. Bezier allows sudden transitions or gradual decay as needed.

---

### Principle 6: Flat Map Transition System
**What**: Smooth transition from 3D globe to 2D flat map

**Fog of War Integration** (vertex.txt:229-263, pixel.txt:393-427):
- **5-tap fog of war sampling**: Center + 4 cardinal directions at ±10/15 units
- **Average to smoothstep**: `(sum × 0.1667)` → smoothstep curve → flat map lerp factor
- **Height adjustment**: Vertex shader displaces Y position toward flat plane based on fog
- **Noise blending**: TerrainBlendNoise adds variation to transition zone

**Geometry Transition** (vertex.txt:286-312):
- **FlatMapHeight**: Target Y position for flat mode
- **Lerp calculation**: `currentHeight × (1 - flatMapLerp) + flatMapHeight × flatMapLerp`
- **Conditional thresholds**: Different blend rules based on height ranges
- **Noise variation**: Prevents uniform transition (looks more natural)

**Why This Works**: Fog of war naturally reveals "known" terrain (3D) vs "unknown" (flat). Transitioning to flat at fog boundary creates intuitive effect.

---

## Critical Performance Patterns

### Principle 7: Distance-Based LOD for Everything
**Virtual Texture LOD** (pixel.txt:545-554):
- Mip level = `log2(cameraDistance² × scale²) × 0.5 + 1.0`
- Clamp to available mip count (typically 31 levels)
- **Far away** = low mip (coarse detail), **close up** = high mip (fine detail)

**Tessellation LOD** (vertex shader, inferred):
- Triangle density scales with camera distance
- Far terrain = fewer triangles, near terrain = high subdivision
- Saves GPU vertex processing for distant terrain

**Material LOD** (not explicit in shader, likely CPU-side):
- Fewer material blend samples at distance
- Full 16-material blend only when close

**Why Critical**: Prevents processing detail player can't see. Performance scales with viewport, not world size.

---

### Principle 8: Pre-Computation at Load Time
**What**: Expensive calculations once at load, zero runtime cost

**Examples**:
- Virtual texture page table generation
- Distance field computation (if not pre-baked)
- Normal map generation from heightmap
- Material blend weight calculation per-vertex

**Trade-off**: Longer load times (5-15 seconds) for zero runtime overhead

**Pattern**: Matches "static geometry, dynamic appearance" principle - compute geometry once, update appearance only.

---

### Principle 9: Single Unified Shader Pass
**What**: All effects in one mega-shader, not multiple passes

**Integration** (pixel.txt flow):
1. Sample virtual heightmap → terrain height
2. Sample 16 materials → blend by biome weights → diffuse/normal/properties
3. Sample devastation texture → apply Bezier blending → HSV color shift
4. Sample border distance field → 9-tap anti-aliasing → gradient rendering
5. Sample province colors → blend with borders → political overlay
6. Calculate PBR lighting → apply to final color
7. Output to 5 render targets

**Why Single Pass**: Minimizes texture reads (GPU cache reuse), avoids render target switching overhead

**Complexity Cost**: 1729-line shader (hard to debug/maintain) but fastest execution

**Trade-off**: Maximum performance over maintainability. Justified for rendering entire Earth with complex effects.

---

## Material & Lighting Systems

### Principle 10: PBR with Specular Backlighting
**What**: Physically-based rendering enhanced with artistic backlighting

**Standard PBR Inputs** (pixel.txt:856-859):
- **PropertiesMap**: Roughness (G), Metallic (B), AO (R or A)
- **NormalMap**: Tangent-space normals (RG channels, reconstruct B)
- **DiffuseMap**: Base albedo color

**Specular Backlight** (constants, pixel.txt:75-79):
- `_GCSpecularBackLightDiffuse`: Backlight color (rim lighting)
- `_GCSpecularBacklightIntensityMin/Max`: Intensity range by roughness
- `_GCSpecularBacklightRoughnessMin/Max`: Roughness thresholds for effect

**Purpose**: Artistic enhancement - creates rim lighting on mountain ridges, coastal edges. Not physically accurate but visually striking.

---

### Principle 11: Normal Map Reconstruction
**What**: Store 2 channels (RG), reconstruct 3rd (B) in shader

**Reconstruction** (pixel.txt:863-873):
```
normalX = R × 2 - 1  // Remap [0,1] to [-1,1]
normalZ = G × 2 - 1
normalY = sqrt(1 - normalX² - normalZ²)  // Reconstruct from unit length
```

**Why**: Saves 1 texture channel (33% memory reduction). Common optimization.

**Tangent-Space Transform** (pixel.txt:893-902):
- Multiply reconstructed normal by TBN matrix (tangent/binormal/normal)
- Transforms from texture space to world space for lighting

---

## Political Overlay System

### Principle 12: Province Color Texture with Offsets
**What**: Texture atlas for province colors (primary, secondary, highlight, alternate)

**Layout** (pixel.txt:1000-1011):
- **Base province color**: At (provinceX, provinceY) in texture
- **SecondaryProvinceColorsOffset**: Offset for alliance/region colors
- **HighlightProvinceColorsOffset**: Offset for selection/hover
- **AlternateProvinceColorsOffset**: Offset for alternate map modes

**Lookup** (pixel.txt:908-923):
- ProvinceID encoded in instance data
- Decode to X/Y coordinates in province color texture
- Sample ProvinceColorTexture at computed coordinates
- Multiple samples for primary + secondary borders

**Why Texture**: GPU-friendly lookup (no CPU queries). Change colors = update texture pixels (fast).

---

### Principle 13: Gradient Border Blending
**What**: Smooth color transitions at province boundaries

**Two-Layer Rendering** (pixel.txt:960-999):
- **Layer 1 - Gradient**: Smooth alpha from inside to outside edge
  - `GB_GradientAlphaInside`: Alpha at province interior
  - `GB_GradientAlphaOutside`: Alpha at outer gradient edge
  - `GB_GradientWidth`: Distance over which transition occurs
  - `GB_GradientColorMul`: Color multiplier for gradient layer

- **Layer 2 - Sharp Edge**: Crisp line at border center
  - `GB_EdgeWidth`: Width of sharp edge
  - `GB_EdgeSmoothness`: Smoothstep factor for anti-aliasing
  - `GB_EdgeAlpha`: Alpha of sharp edge
  - `GB_EdgeColorMul`: Color multiplier for edge layer

**Blending Math** (pixel.txt:970-999):
- Distance from border (from SDF) determines layer weights
- Inside gradient zone: Lerp from inside alpha to outside alpha
- At edge: Sharp edge layer multiplied by smoothstep factor
- Final: `edgeColor × edgeAlpha + gradientColor × gradientAlpha`

**Separate Globe/Flat Parameters** (pixel.txt:43):
- `GB_Flatmap_*` variants for flat map mode
- Different visual styles for 3D vs 2D views

**Why Sophisticated**: Creates Paradox's signature border look - **soft gradient + crisp edge**. Not just a line, a **designed visual element**.

---

## Fog of War & Terra Incognita

### Principle 14: Fog of War as Geometry Modifier
**What**: Fog of war affects terrain height (vertex shader), not just alpha

**Vertex Shader Integration** (vertex.txt:229-263):
- Sample FogOfWarAlpha texture (5-tap pattern: center + 4 cardinals)
- Average samples → smoothstep curve → flat map lerp factor
- **Modify vertex Y position**: Known terrain = full height, unknown = flat

**Smooth Transitions**: 5-tap sampling creates smooth boundaries (not hard edges)

**Pixel Shader Use** (pixel.txt:393-442):
- Read same fog of war data
- Apply to flat map noise blending
- Affects both geometry AND appearance

**Why Both**: Vertex shader for geometry (performance - modify fewer vertices), pixel shader for effects (quality - per-pixel accuracy)

---

## Shadow System

### Principle 15: PCF Shadow Mapping with Rotated Sampling
**What**: Percentage-Closer Filtering with randomized rotation per pixel

**Implementation** (pixel.txt:1200-1299):
- Transform world position to shadow map space (4×4 matrix)
- Generate per-pixel rotation angle from screen coordinates (lines 1244-1251):
  - Hash function: `sin(dot(screenXY, vec2(12.9898, 78.2330))) × 43758.5`
  - Extract fractional part → multiply by 2π → cos/sin for rotation
- Sample shadow map multiple times with rotated kernel (8-16 samples)
- Average results → shadow factor

**Why Rotation**: Breaks up shadow map aliasing patterns. Each pixel has different sample pattern = noise instead of banding.

**Comparison Sampler** (pixel.txt:1285, 1295):
- `SampleCmpLevelZero`: Hardware depth comparison (GPU-accelerated)
- Returns 0 (in shadow) or 1 (lit) per sample

---

## Optimization Techniques

### Principle 16: Smoothstep Everywhere
**Pattern**: `t × t × (3 - 2×t)` appears throughout shader

**Uses** (examples):
- Fog of war blending (vertex.txt:274-278, pixel.txt:438-442)
- Flat map transitions (vertex.txt:286-292)
- Border alpha blending (pixel.txt:718-722, 981-984)
- Devastation masking (pixel.txt:831-836)

**Why**: Smooth interpolation with zero derivatives at ends (no visible seams). Cheaper than sin/cos, better than linear.

**Formula**: `smoothstep(t) = t² × (3 - 2t)` where t ∈ [0,1]

**Pattern**: Use smoothstep for ANY gradual transition between states. Never use linear lerp for visual effects.

---

### Principle 17: Fractional Wrapping for Tiling
**What**: `frac()` function for seamless coordinate wrapping

**Uses** (pixel.txt:583-584):
- World-space UV wrapping: `frac(worldPos / textureScale)`
- Prevents edge artifacts when textures tile
- Enables infinite tiling in any direction

**Pattern**: Always apply `frac()` to world-space coordinates before texture sampling. Ensures seamless tiling across boundaries.

---

### Principle 18: Saturation for Safety
**Pattern**: `saturate(value)` = `clamp(value, 0, 1)`

**Uses**: Nearly every calculated parameter (colors, alphas, blend factors)

**Why**: Prevents out-of-range values from causing artifacts. Defensive programming for shader math.

**Cost**: Nearly free on modern GPUs (part of ALU instruction set)

**Pattern**: Saturate ALL blend factors, alphas, and normalized values. Prevents rare edge case bugs.

---

## The Infinite Scale Principle

**Core Philosophy**: EU5's graphics scale infinitely because they use **continuous data sources + procedural generation + sub-pixel precision**

**Examples Throughout System**:
- **Terrain height**: Virtual texturing (LOD streaming) + bilinear filtering = unlimited zoom
- **Material blending**: World-space UVs + tri-planar projection = no stretching at any angle/distance
- **Border rendering**: Signed distance field + 9-tap sampling + gradient blending = smooth at all scales
- **Effects (devastation)**: Procedural Bezier curves + HSV color math = artist-directable without textures

**What This Means**:
- **Never chase resolution** - Use continuous data (heightmap, distance fields) instead of discrete textures
- **Procedural > pre-baked** - Generate detail from continuous inputs, don't store every possibility
- **Sub-pixel precision** - GPU anti-aliasing handles appearance, logic works at infinite precision

**Contrast with Traditional**:
- Traditional: 1024×1024 texture → zoom in → pixelation
- EU5: Virtual texture → distance-based LOD → always crisp

---

## Integration Depth

**EU5's Strength**: Every system connects seamlessly
- Fog of war affects geometry (vertex shader) AND appearance (pixel shader)
- Devastation affects color (HSV shift) AND alpha (blend factor) AND height masking
- Borders blend with province colors with separate parameters for globe/flat map modes
- Material system integrates with lighting, political overlay, and effects in single pass

**Trade-off**: 1729-line shader is complex but achieves maximum performance through tight integration.

---

*Document created: 2025-11-04*
*Source: EU5 vertex shader (374 lines) + pixel shader (1729 lines)*
*Analysis: Comprehensive reverse-engineering of modern Paradox rendering architecture*
*Purpose: Timeless reference for understanding AAA terrain rendering techniques*
