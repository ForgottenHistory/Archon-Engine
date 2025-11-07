# Archon Engine - Graphics Implementation Priorities
**Date**: 2025-11-04 (updated as features are implemented)
**Source**: Analysis of EU5 rendering architecture (see eu5-terrain-rendering-analysis.md)
**Purpose**: Track what we have, what we need, and priority order for implementation

---

## Current State Assessment

### Already Implemented ✅
**Strong Foundation**:
- GPU tessellation with distance-based LOD
- Detail texture system (256 materials via Texture2DArray)
- World-space UVs (scale-independent tiling)
- Procedural terrain assignment (height-based)
- Mesh-based borders (sub-pixel width, 0.0002 world units)
- Heightmap-based water detection
- Smooth coastlines via bilinear filtering

**Architecture Strengths**:
- Dual-layer (CPU simulation + GPU presentation)
- Pre-computation at load time (static geometry, dynamic appearance)
- Single draw call rendering
- Scale-independent rendering (vectors, not bitmaps)

---

## Feature Priorities (Ranked by Effort vs Impact)

### Tier 1: Critical Features (Highest ROI)

#### 1. Tri-Planar Mapping
**Status**: NOT IMPLEMENTED
**Why Critical**: Currently mountains/cliffs have severe texture stretching
**Effort**: Low (shader math only, no new systems)
**Impact**: Immediate 30-40% visual quality improvement
**EU5 Reference**: Principle 2 (pixel.txt:613 - `_TriPlanarUVTightenFactor`)

**What to Implement**:
- Sample detail textures from X/Y/Z axes based on world-space position
- Weight by surface normal: `weight = abs(normal)^tightenFactor`
- Blend 3 samples together (normalized weights)
- No new textures needed - uses existing detail array

**Files to Modify**:
- `MapModeTerrain.hlsl` - Add tri-planar sampling function
- Shader properties - Add tighten factor control

---

#### 2. Normal Map Generation from Heightmap
**Status**: NOT IMPLEMENTED
**Why Critical**: Lighting currently too flat, terrain lacks depth perception
**Effort**: Low (compute shader, one-time generation)
**Impact**: Massive lighting quality improvement
**EU5 Reference**: Principle 11 (pixel.txt:863-873)

**What to Implement**:
- Compute shader: sample heightmap at (x-1, x+1, y-1, y+1)
- Calculate gradients: `normal = normalize(cross(dx, dy))`
- Store as R8G8_UNORM texture (2 channels, reconstruct B in shader)
- Generate once at load, bind to shader

**Files to Create**:
- `NormalMapGenerator.cs` - Compute shader wrapper
- `GenerateNormalsCompute.compute` - Gradient calculation

**Files to Modify**:
- `VisualTextureSet.cs` - Add normal map generation after heightmap load
- Shader - Sample normal map, apply to lighting

---

#### 3. Border Gradient Blending
**Status**: PARTIAL (mesh borders work, no gradients)
**Why Critical**: Signature Paradox look (soft gradient + crisp edge)
**Effort**: Medium (adapt existing mesh system)
**Impact**: Professional polish, immediately recognizable style
**EU5 Reference**: Principle 13 (pixel.txt:960-999)

**What to Implement**:
- Extend mesh border system to support vertex colors
- Two-layer approach: gradient vertices + edge vertices
- Smooth alpha from province interior to outer edge
- Province color blending at borders

**Files to Modify**:
- `BorderMeshGenerator.cs` - Add gradient vertex generation
- Border shader - Blend province colors by distance from border

**Challenge**: Mesh-based gradients need different approach than EU5's SDF. Use vertex color gradients instead of distance field sampling.

---

### Tier 2: High Value Features

#### 4. Basic PBR Lighting (Forward Rendering)
**Status**: NOT IMPLEMENTED
**Why Important**: Modern games expected to have realistic materials
**Effort**: Medium (shader refactor)
**Impact**: Professional look, materials feel realistic
**EU5 Reference**: Principle 10 (pixel.txt:856-859)

**What to Implement**:
- Forward PBR shader (not deferred - simpler)
- Diffuse + specular BRDF (GGX or similar)
- Roughness/metallic workflow
- Single directional light (sun)
- Simple ambient (solid color or gradient)

**Skip for Now**:
- Deferred rendering (overkill)
- Multiple lights
- Specular backlighting (artistic polish, not critical)

---

#### 5. Better Detail Textures
**Status**: PLACEHOLDER QUALITY
**Why Important**: Current textures look basic
**Effort**: Low (asset work, no code changes)
**Impact**: Professional visual quality

**What to Do**:
- Download 8-12 PBR texture sets from PolyHaven (grass, rock, snow, sand, forest, etc.)
- Convert to 512×512, BC7 compression
- Generate mipmaps
- Place in `Assets/Data/textures/terrain_detail/`
- Test tiling quality

---

#### 6. Province-Based Terrain Assignment
**Status**: HEIGHT-BASED ONLY (geographically inaccurate)
**Why Important**: Sahara should be desert, not grassland
**Effort**: Medium (data structure + blending algorithm)
**Impact**: Geographic accuracy, map makes sense

**What to Implement**:
- Store terrain type per province (in ProvinceState or separate data)
- Sample multiple provinces in world-space (3-5 nearest)
- Blend by distance weights
- Fallback to height-based when far from provinces

**Files to Create**:
- Province terrain data storage
- Multi-province sampling shader function

**Challenge**: Need efficient province-at-worldpos lookup (spatial grid or texture-based).

---

### Tier 3: Polish Features

#### 7. Environment Lighting
**Status**: NOT IMPLEMENTED
**Why Useful**: Atmospheric depth, realistic ambient
**Effort**: Medium (cubemap or SH generation)
**Impact**: Scenes feel immersive

**What to Implement**:
- Cubemap for environment reflections (can use solid color initially)
- Spherical harmonics for ambient lighting
- Or simpler: gradient ambient (zenith to horizon colors)

---

#### 8. Water Shader Effects
**Status**: HEIGHTMAP DETECTION ONLY (static water)
**Why Useful**: Water currently boring
**Effort**: Low (shader animation)
**Impact**: Water looks alive

**What to Implement**:
- Sine wave displacement (animated ripples)
- Foam at coastlines (edge detection from heightmap)
- Basic reflections (cubemap or screen-space)
- Transparency with depth fade

---

#### 9. Secondary Borders
**Status**: NOT IMPLEMENTED
**Why Useful**: Alliance/region visualization
**Effort**: Low (duplicate border system with different color)
**Impact**: Diplomatic relationships visible at glance

**What to Implement**:
- Second border layer rendered after primary
- Different color source (alliance, region, trade zone)
- Configurable visibility per map mode

---

### Tier 4: Advanced Features (Diminishing Returns)

#### 10. Virtual Texturing
**Status**: NOT IMPLEMENTED
**Why Low Priority**: Only needed for 8192×8192+ maps, current approach works fine
**Effort**: Very high (indirection system, page streaming, LOD management)
**Impact**: Enables unlimited detail but adds massive complexity
**EU5 Reference**: Principle 1 (pixel.txt:545-606)

**When to Reconsider**: If targeting maps larger than 8192×8192

---

#### 11. Deferred Rendering
**Status**: FORWARD RENDERING
**Why Low Priority**: Overkill for single directional light + ambient
**Effort**: High (5 render targets, lighting pass refactor)
**Impact**: Enables advanced effects but unnecessary complexity
**EU5 Reference**: Principle 3 (pixel.txt:50-54)

**When to Reconsider**: If adding many dynamic lights or complex post-processing

---

#### 12. Devastation System
**Status**: NOT IMPLEMENTED
**Why Low Priority**: War visualization nice-to-have, not gameplay-critical
**Effort**: Medium (Bezier system or simpler linear blending)
**Impact**: Visual feedback for war
**EU5 Reference**: Principle 5 (pixel.txt:625-723)

**When to Reconsider**: After core gameplay systems (economy, military, diplomacy) mature

---

### Tier 5: Optional Features (Can Skip)

#### 13. Flat Map Mode
**Status**: 3D ONLY
**Why Skip**: Aesthetic preference, 3D-only is acceptable
**Effort**: High (vertex shader height blending, fog integration)
**EU5 Reference**: Principle 6 (vertex.txt:286-312)

---

#### 14. Specular Backlighting
**Status**: NOT IMPLEMENTED
**Why Skip**: Artistic rim lighting, subtle effect
**Effort**: Low (add to PBR shader)
**EU5 Reference**: Principle 10 (pixel.txt:75-79)

---

#### 15. PCF Shadow Rotation
**Status**: NO SHADOWS YET
**Why Skip**: Better shadow quality, but basic shadows sufficient initially
**Effort**: Low (add rotation to shadow sampling)
**EU5 Reference**: Principle 15 (pixel.txt:1244-1299)

---

## Comparison: Archon vs EU5

### Rendering Approaches
| System | EU5 | Archon | Comparison |
|--------|-----|--------|------------|
| **Borders** | SDF texture + 9-tap sampling | Vector meshes (triangle strips) | **Archon superior** for thin lines (flat caps, sub-pixel precision) |
| **Terrain Detail** | Virtual texturing (streaming) | Single full-res heightmap | **EU5 superior** at massive scale (16k+), Archon sufficient for current maps |
| **Materials** | 256 materials, tri-planar | 256 materials, NO tri-planar | **Need tri-planar** (critical gap) |
| **Lighting** | Deferred PBR + backlight | None yet | **Need basic PBR** (forward rendering sufficient) |
| **Scale Independence** | Full (virtual textures, LOD) | Partial (world-space UVs, tessellation) | **Archon has core principle**, needs extension |

### Architecture Alignment
**Shared Principles**:
- ✅ Static geometry, dynamic appearance
- ✅ Pre-computation at load time
- ✅ Scale-independent rendering (continuous data + procedural generation)
- ✅ Distance-based LOD
- ✅ Single draw call (or minimal draws)

**Architectural Differences**:
- EU5: Deferred rendering (5 render targets) → Archon: Forward rendering (simpler)
- EU5: Virtual texturing (streaming) → Archon: Full-res heightmap (memory-bound but acceptable)
- EU5: SDF borders (texture-based) → Archon: Mesh borders (geometry-based)

**Philosophy Match**: Both engines embrace "infinite scale" principle. Archon's foundation is solid.

---

## Implementation Roadmap

**Immediate (Next 1-2 Weeks)**:
1. Tri-planar mapping (2 days) - Eliminates stretching
2. Normal map generation (2 days) - Lighting quality jump
3. Border gradient blending (3 days) - Signature look

**Short-Term (2-4 Weeks)**:
4. Basic PBR lighting (4 days) - Modern look
5. Better detail textures (2 days) - Professional quality
6. Province-based terrain (2 days) - Geographic accuracy

**Medium-Term (1-2 Months)**:
7. Environment lighting (3 days) - Atmospheric depth
8. Water shader effects (2 days) - Animated water
9. Secondary borders (2 days) - Alliance visualization

**Long-Term (3+ Months or Never)**:
10. Virtual texturing - Only if scaling to 8192×8192+
11. Deferred rendering - Only if adding many lights
12. Devastation system - After core gameplay mature

---

## Avoiding EU5's Complexity

**What to Skip**:
- 1729-line mega-shader (unmaintainable)
- Bezier curve devastation (linear blending sufficient)
- Flat map mode (3D-only acceptable)
- Specular backlighting (subtle effect)
- PCF shadow rotation (basic shadows fine)

**Philosophy**: Achieve 80-85% of EU5's visual quality with 10-20% of complexity. Focus on features with high visual impact and low maintenance burden.

**Target**: Professional AAA look without AAA studio complexity. Pragmatic choices for indie/small team development.

---

## Success Metrics

**Visual Quality Target**: 80-85% of EU5
**Complexity Budget**: 10-20% of EU5 (maintainable by small team)
**Performance Target**: 60 FPS at 5k+ provinces

**Critical Features for Target**:
- ✅ Tri-planar mapping (eliminates stretching)
- ✅ Normal map generation (lighting quality)
- ✅ Border gradients (signature look)
- ✅ PBR lighting (modern materials)
- ✅ Better textures (professional content)

**These 5 features** = 80% visual quality achieved.

---

*Document created: 2025-11-04*
*Updated: As features are implemented*
*Purpose: Track implementation progress and adjust priorities*
*Companion to: eu5-terrain-rendering-analysis.md (timeless technical reference)*
