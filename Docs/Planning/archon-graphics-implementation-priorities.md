# Archon Engine - Graphics Implementation Priorities
**Date**: 2025-11-18 (updated as features are implemented)
**Goal**: Imperator Rome's "painted map" aesthetic
**Source**: Analysis of Imperator Rome rendering (see imperator-rome-terrain-rendering-analysis.md)
**Purpose**: Track what we have, what we need, and priority order for implementation

---

## Current State Assessment

### Already Implemented ✅
**Strong Foundation**:
- ✅ GPU tessellation with distance-based LOD
- ✅ Detail texture system (256 materials via Texture2DArray)
- ✅ World-space UVs (scale-independent tiling)
- ✅ Procedural terrain assignment (height-based)
- ✅ Mesh-based province borders (sub-pixel width, 0.0002 world units, flat caps)
- ✅ Heightmap-based water detection
- ✅ Smooth coastlines via bilinear filtering
- ✅ Tri-planar mapping (eliminates stretching on slopes)
- ✅ Normal map generation from heightmap

**Architecture Strengths**:
- ✅ Dual-layer (CPU simulation + GPU presentation)
- ✅ Pre-computation at load time (static geometry, dynamic appearance)
- ✅ Single draw call rendering
- ✅ Scale-independent rendering (vectors, not bitmaps)

**Border System Status**:
- ✅ Province borders via triangle strip meshes (matches Imperator's approach)
- ⚠️ Proof-of-concept quality (95% working, junctions need polish)
- ❌ Country borders (distance field overlay) - not implemented

---

## Feature Priorities - Imperator Rome Style

**Visual Goal**: Hand-painted strategy map aesthetic
**Philosophy**: Artistic cohesion over technical precision

### Tier 1: Critical Features for "Imperator Look" (Highest Priority)

#### 1. Bilinear Province Color Interpolation ⭐ CRITICAL
**Status**: NOT IMPLEMENTED
**Why Critical**: THE signature feature of Imperator's painted aesthetic
**Effort**: Medium (indirection texture system + bilinear sampling)
**Impact**: 🔥 MASSIVE - transforms from digital to painted look
**Imperator Reference**: Principle 3 (pixel.txt:341-420)

**What Makes This Critical**:
- Creates watercolor gradients at province boundaries instead of hard edges
- Single most important visual difference between digital and painted maps
- Combined with HSV grading = Imperator's signature look

**Implementation**:
1. **ProvinceColorIndirectionTexture** (world pos → UV in color texture)
   - R8G8_UNorm texture, same resolution as province ID texture
   - Maps world position to coordinates in ProvinceColorTexture

2. **ProvinceColorTexture** (indexed color lookup)
   - Indexed by province ID
   - Contains all province colors

3. **Shader Bilinear Sampling**:
   - Sample indirection texture at 4 corners (bilinear quad)
   - Decode each indirection value → fetch from ProvinceColorTexture
   - Manually bilinear blend the 4 colors
   - Result: Smooth color gradients at borders

**Files to Create**:
- `ProvinceColorIndirectionGenerator.cs` - Generate indirection texture from province ID texture
- `ProvinceColorTextureManager.cs` - Manage indexed color texture

**Files to Modify**:
- Map shader - Add bilinear province color sampling
- `MapTextureManager.cs` - Integrate new texture system

**Technical Challenge**: Must avoid interpolating province IDs (would break lookup). Use indirection texture to map world space → color texture UVs, THEN bilinear sample colors.

---

#### 2. HSV Color Grading ⭐ HIGH PRIORITY
**Status**: NOT IMPLEMENTED
**Why Important**: Makes province colors pop, ensures visual distinction
**Effort**: Low (shader math only)
**Impact**: High - vibrant, distinct province colors without manual tuning
**Imperator Reference**: Principle 5 (pixel.txt:471-507)

**What to Implement**:
- RGB → HSV conversion in shader
- Hue rotation based on luminance
- Saturation boost for darker colors
- Blend adjusted color with original (luminance-based factor)

**Shader Logic**:
```hlsl
// 1. Convert province color RGB → HSV
float3 hsv = RGBtoHSV(provinceColor);

// 2. Calculate blend factor (darker colors get more grading)
float luminance = dot(provinceColor, float3(0.2125, 0.7154, 0.0721));
float blendFactor = 1.0 - luminance;

// 3. Generate adjusted color from hue
float3 adjustedColor = HSVtoRGB(hsv);

// 4. Blend original with adjusted
finalColor = lerp(provinceColor, adjustedColor, blendFactor);
```

**Files to Modify**:
- Map shader - Add HSV color grading after province color sampling

---

#### 3. Border Sine Noise Pattern ⭐ MEDIUM PRIORITY
**Status**: NOT IMPLEMENTED
**Why Important**: Adds organic, hand-drawn feel to borders
**Effort**: Low (shader math only)
**Impact**: Medium - borders feel painted, not computer-perfect
**Imperator Reference**: Principle 17 (pixel.txt:452-464)

**What to Implement**:
- Hash function from world position
- Dual-octave sine wave pattern
- Smoothstep blending
- Apply noise to border alpha

**Shader Logic**:
```hlsl
// 1. Hash from world position
float hash = dot(worldPos.xy, float2(11086.557, 4592.198));

// 2. Dual-octave sine pattern
float noise1 = sin(hash);
noise1 = saturate(noise1 * 2.2);
noise1 = smoothstep(0, 1, noise1);

float noise2 = sin(hash - 0.5);
noise2 = saturate(noise2 * 2.2);
noise2 = smoothstep(0, 1, noise2);

// 3. Combine and apply to border
float finalNoise = saturate(1.0 - borderAlpha * noise2 + noise1);
borderColor = lerp(terrainColor, borderColor, borderAlpha * finalNoise * 0.8);
```

**Files to Modify**:
- Border shader (`border-pixel` equivalent) - Add noise pattern

**Note**: Only affects province borders (mesh-based). Country borders would need separate distance field implementation.

---

### Tier 2: High Value Polish Features

#### 4. Distance Field Country Borders (Optional)
**Status**: NOT IMPLEMENTED
**Why Useful**: Thicker, gradient overlays for countries/alliances
**Effort**: High (distance field generation + 9-tap sampling)
**Impact**: Medium - secondary visual layer
**Imperator Reference**: Principle 4 (pixel.txt:377-464)

**What to Implement**:
- Generate BorderDistanceTexture from country boundaries
- 9-tap sampling pattern (±0.75 offset)
- Two-layer rendering (gradient + sharp edge)
- Integrate sine noise pattern
- Render as overlay on terrain

**Files to Create**:
- `BorderDistanceFieldGenerator.cs` - Generate distance field texture
- Country border shader integration in terrain shader

**When to Implement**: After province borders polished and basic Imperator look achieved

---

#### 5. Material Blend Smoothstep Transitions
**Status**: PARTIAL (have materials, need smoothstep)
**Why Useful**: Natural terrain transitions, not sharp cutoffs
**Effort**: Low (shader modification)
**Impact**: Medium - terrain feels organic
**Imperator Reference**: Principle 2 (pixel.txt:166-170)

**What to Implement**:
- DetailBlendRange threshold system
- Smoothstep falloff: `t² × (3 - 2t)`
- Re-normalize blend weights after threshold

**Shader Logic**:
```hlsl
// 1. Clamp blend weights to [0, 10]
float4 weights = saturate(rawWeights * 10.0);

// 2. Apply smoothstep
float4 smoothed = weights * weights * (3.0 - 2.0 * weights);

// 3. Find max, subtract threshold, re-normalize
float maxWeight = max(max(smoothed.x, smoothed.y), max(smoothed.z, smoothed.w));
float4 adjusted = max(smoothed - DetailBlendRange, 0.0);
adjusted /= (dot(adjusted, 1.0) + 0.0001);

// 4. Use adjusted weights for material blending
```

**Files to Modify**:
- Terrain shader - Modify material blending logic

---

#### 6. Forward PBR Lighting
**Status**: NOT IMPLEMENTED
**Why Useful**: Modern material appearance
**Effort**: Medium (shader refactor)
**Impact**: Medium-High - professional look
**Imperator Reference**: Principle 6 (pixel.txt:572-584)

**What to Implement**:
- Cook-Torrance BRDF (GGX distribution)
- Roughness/metallic workflow
- Single directional light (sun)
- Environment cubemap (IBL)
- Fresnel reflections

**Skip for Now**:
- Deferred rendering (overkill)
- Multiple lights
- Complex shadow systems

**Files to Create**:
- PBR shader functions (BRDF calculations)

**Files to Modify**:
- Terrain shader - Integrate PBR lighting

---

#### 7. Better Detail Textures
**Status**: PLACEHOLDER QUALITY
**Why Useful**: Current textures look basic
**Effort**: Low (asset work, no code changes)
**Impact**: High - professional visual quality

**What to Do**:
- Download 8-12 PBR texture sets from PolyHaven
- Focus on: grass, rock, snow, sand, forest, mountain, desert, farmland
- Convert to 512×512, BC7 compression
- Generate mipmaps
- Place in `Assets/Data/textures/terrain_detail/`
- Test tiling quality with world-space UVs

---

### Tier 3: Gameplay-Relevant Features

#### 8. Province-Based Terrain Assignment
**Status**: HEIGHT-BASED ONLY (geographically inaccurate)
**Why Important**: Geographic accuracy (Sahara should be desert)
**Effort**: Medium (data structure + blending algorithm)
**Impact**: High - map makes sense geographically

**What to Implement**:
- Store terrain type per province (byte in ProvinceState or separate texture)
- Multi-province sampling in shader (3-5 nearest provinces)
- Blend by distance weights
- Fallback to height-based when far from provinces

**Challenge**: Need efficient province-at-worldpos lookup (spatial grid or texture-based).

---

#### 9. Environment Lighting
**Status**: NOT IMPLEMENTED
**Why Useful**: Atmospheric depth, realistic ambient
**Effort**: Medium (cubemap generation)
**Impact**: Medium - scenes feel immersive

**What to Implement**:
- Cubemap for environment reflections (solid color or gradient initially)
- Sample at varying mip levels based on roughness
- Integrate with PBR shader

**Imperator Reference**: Uses environment cubemap (t14)

---

#### 10. Snow System with Slope Masking
**Status**: NOT IMPLEMENTED
**Why Useful**: Seasonal variation, visual feedback
**Effort**: Medium (WinterMap texture + slope masking)
**Impact**: Medium - dynamic visual variety
**Imperator Reference**: Principle 7 (pixel.txt:267-336)

**What to Implement**:
- WinterMap texture (coverage per province)
- Noise texture for variation
- Slope masking (snow on flat surfaces, not cliffs)
- Blend snow material with terrain

**When to Implement**: After core visual features (bilinear colors, HSV grading) done

---

### Tier 4: Optional Features

#### 11. Animated Fog of War
**Status**: NOT IMPLEMENTED
**Why Optional**: Nice-to-have visual polish
**Effort**: Low (shader animation)
**Impact**: Low - subtle effect
**Imperator Reference**: Principle 8 (pixel.txt:625-641)

**What to Implement**:
- Multi-octave noise pattern
- Animated over time
- Blend with base fog of war

---

#### 12. Water Shader Effects
**Status**: HEIGHTMAP DETECTION ONLY (static water)
**Why Optional**: Water currently functional
**Effort**: Low (shader animation)
**Impact**: Low-Medium - water looks alive

**What to Implement**:
- Sine wave displacement (animated ripples)
- Foam at coastlines (edge detection from heightmap)
- Basic reflections (cubemap)
- Transparency with depth fade

---

#### 13. Rotated PCF Shadows
**Status**: NO SHADOWS YET
**Why Optional**: Quality improvement over basic shadows
**Effort**: Low (add rotation to shadow sampling)
**Impact**: Low - subtle quality improvement
**Imperator Reference**: Principle 18 (pixel.txt:509-533)

**When to Implement**: After basic lighting system in place

---

### Tier 5: Advanced/Future Features

#### 14. Virtual Texturing
**Status**: NOT IMPLEMENTED
**Why Low Priority**: Only needed for 8192×8192+ maps
**Effort**: Very high (indirection system, page streaming, LOD)
**Impact**: Enables unlimited detail but adds massive complexity

**When to Reconsider**: If targeting maps larger than 8192×8192

---

#### 15. Deferred Rendering
**Status**: FORWARD RENDERING
**Why Low Priority**: Overkill for single light + ambient
**Effort**: High (G-buffer, lighting pass refactor)
**Impact**: Enables many lights but unnecessary complexity

**When to Reconsider**: If adding many dynamic lights

---

## Implementation Roadmap

**Immediate (Next 1-2 Weeks)** - "Imperator Core":
1. **Bilinear province color interpolation** (3-4 days) - 🔥 CRITICAL for painted look
2. **HSV color grading** (1 day) - Makes colors pop
3. **Border sine noise** (1 day) - Organic feel

**Short-Term (2-4 Weeks)** - "Visual Polish":
4. **Material smoothstep blending** (1 day) - Natural transitions
5. **Better detail textures** (2 days) - Professional quality
6. **Forward PBR lighting** (4 days) - Modern look

**Medium-Term (1-2 Months)** - "Gameplay Features":
7. **Province-based terrain** (2 days) - Geographic accuracy
8. **Environment lighting** (3 days) - Atmospheric depth
9. **Snow system** (3 days) - Seasonal variation

**Long-Term (3+ Months or Never)**:
10. Distance field country borders - Optional secondary layer
11. Virtual texturing - Only if massive scale needed
12. Deferred rendering - Only if many lights needed

---

## Success Metrics - Imperator Rome Style

**Visual Quality Target**: Imperator Rome's "painted map" aesthetic
**Complexity Budget**: Simpler than EU5, focus on artistic cohesion
**Performance Target**: 60 FPS at 5k+ provinces

**Critical Features for "Imperator Look"**:
1. ✅ Mesh province borders (razor-thin, flat caps)
2. ✅ Tri-planar mapping (eliminates stretching)
3. ✅ Normal map generation (lighting depth)
4. ⚠️ **Bilinear province colors** - IN PROGRESS (critical for painted look)
5. ⚠️ **HSV color grading** - TODO (makes colors pop)
6. ⚠️ **Border sine noise** - TODO (organic feel)
7. ⚠️ **Material smoothstep** - TODO (natural transitions)

**These 7 features** = Imperator's distinctive painted aesthetic achieved.

**Additional Features for Polish**:
- Forward PBR lighting (modern materials)
- Better detail textures (professional quality)
- Province-based terrain (geographic accuracy)

---

## Key Learnings from Imperator Rome

**What Makes Imperator "Best Looking"**:
1. **Bilinear province colors** = Watercolor gradients at boundaries (not hard edges)
2. **HSV color grading** = Vibrant, distinct colors without manual tuning
3. **Border noise patterns** = Organic, hand-drawn feel (not computer-perfect)
4. **Smoothstep everywhere** = Natural transitions (not sharp cutoffs)
5. **Artistic cohesion** = Fewer features, better integration, distinctive style

**Philosophy**:
- **Soften digital precision** - Bilinear filtering everywhere
- **Add subtle variation** - Sine noise, multi-octave patterns
- **Artistic color control** - HSV grading over raw data
- **Natural transitions** - Smoothstep for all blending

**What Imperator Does NOT Have** (that we can skip):
- Devastation system (war visualization)
- Flat map mode (3D-only acceptable)
- Deferred rendering (forward sufficient)
- Virtual texturing (not needed at our scale)
- Complex shadow systems (basic adequate)

---

## Border System Status

**Province Borders** (Triangle Strip Meshes):
- ✅ Mesh generation working (0.0002 world units width)
- ✅ RDP simplification + Chaikin smoothing pipeline
- ✅ Flat caps (not round like distance fields)
- ✅ Median filter + junction preservation (95% U-turn elimination)
- ⚠️ Junctions messy at razor-thin widths (5% remaining issue)
- ⚠️ Proof-of-concept quality, needs polish

**Decision**: Accept Paradox-level junction quality (only visible at extreme zoom). Focus on painted aesthetic features (bilinear colors, HSV grading) instead.

**Country Borders** (Distance Field, Optional):
- ❌ Not implemented
- Would be distance field overlay with 9-tap sampling + gradients
- Lower priority than painted aesthetic features

---

## Comparison: What We Have vs What We Need

### Strong Foundation (Already Working)
| Feature | Status | Notes |
|---------|--------|-------|
| Mesh province borders | ✅ | Matches Imperator approach |
| Tri-planar mapping | ✅ | Eliminates stretching |
| Normal generation | ✅ | Lighting depth |
| Detail texture system | ✅ | 256 materials ready |
| World-space UVs | ✅ | Scale-independent |

### Critical Gaps for "Imperator Look"
| Feature | Status | Impact | Priority |
|---------|--------|--------|----------|
| Bilinear province colors | ❌ | 🔥 MASSIVE | ⭐⭐⭐⭐⭐ |
| HSV color grading | ❌ | High | ⭐⭐⭐⭐ |
| Border sine noise | ❌ | Medium | ⭐⭐⭐ |
| Material smoothstep | ❌ | Medium | ⭐⭐⭐ |

### Nice-to-Have Polish
| Feature | Status | Impact | Priority |
|---------|--------|--------|----------|
| Forward PBR lighting | ❌ | Medium-High | ⭐⭐ |
| Better textures | ❌ | High | ⭐⭐ |
| Province terrain | ❌ | High | ⭐⭐ |
| Environment lighting | ❌ | Medium | ⭐ |

**Conclusion**: We have the technical foundation. Now need to implement Imperator's artistic features (bilinear colors, HSV grading, noise patterns) to achieve the painted aesthetic.

---

*Document created: 2025-11-04*
*Updated: 2025-11-18 - Refocused on Imperator Rome style*
*Goal: Hand-painted strategy map aesthetic*
*Companion to: imperator-rome-terrain-rendering-analysis.md (technical reference)*
