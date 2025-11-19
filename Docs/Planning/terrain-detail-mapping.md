# Terrain Detail Mapping Architecture - Imperator Rome Approach

**Status:** 🚧 Implementation In Progress
**Date:** 2025-11-19
**Approach:** Imperator Rome's 4-channel bilinear material blending system
**Goal:** AAA+ quality terrain with ultra-smooth transitions at province boundaries

---

## Principle: Multi-Material Blending at Province Boundaries

**Problem:** Sharp terrain transitions at province borders look digital and unrealistic.

**Solution:** Imperator Rome's approach - pre-compute blend masks that store up to 4 materials per pixel with smooth falloff at boundaries.

**Key Insight:** By storing multiple materials and their blend weights per pixel, we can create watercolor-like smooth transitions between provinces while maintaining distinct terrain types.

---

## Architecture: 4-Texture System (Imperator Approach)

### 1. Province ID Texture (Existing: `_ProvinceIDTexture`)
- **Source:** Generated from provinces.bmp
- **Size:** 5632x2048 (EU4 data resolution)
- **Format:** RG16 (16-bit province ID encoding)
- **Purpose:** Identifies which province each pixel belongs to

### 2. Province Terrain Buffer (Existing: `_ProvinceTerrainBuffer`)
- **Type:** ComputeBuffer (StructuredBuffer in shader)
- **Size:** 65536 entries (indexed by province ID)
- **Format:** uint per province (terrain type 0-26)
- **Purpose:** Maps province ID → terrain type

### 3. Detail Index Texture (NEW: `_DetailIndexTexture`)
- **Format:** RGBA8 (4 material indices per pixel)
- **Size:** 5632x2048 (same as province texture)
- **Generation:** GPU compute shader at load time
- **Purpose:** Stores which 4 materials to blend at each pixel
- **Example pixel:** (0, 12, 6, 0) = grassland, forest, mountain, empty

### 4. Detail Mask Texture (NEW: `_DetailMaskTexture`)
- **Format:** RGBA8 (4 blend weights per pixel, 0-1 normalized)
- **Size:** 5632x2048 (same as province texture)
- **Generation:** GPU compute shader at load time
- **Purpose:** Stores blend weight for each of the 4 materials
- **Example pixel:** (0.6, 0.3, 0.1, 0.0) = 60% grass, 30% forest, 10% mountain

### 5. Terrain Texture Arrays (NEW)
- **Type:** Texture2DArray (one array each for albedo, normal, material properties)
- **Layers:** 27 (one per terrain type from terrain_rgb.json5)
- **Format:**
  - Albedo: RGBA8_sRGB (BC7 compressed)
  - Normal: RG16 (BC5 compressed, reconstruct Z in shader)
  - Material: RGB8 (BC7 compressed, R=AO, G=Roughness, B=Metallic)
- **Size per layer:** 1024x1024 (tiled, seamless)
- **Source:** Polyhaven textures (already in Assets/Data/)
- **UVs:** World-space (`positionWS.xz * _DetailTiling`)

---

## Imperator's 4-Channel Bilinear Blending

### How It Works

**Step 1: Pre-compute at Load Time (GPU)**
```
For each pixel in DetailIndexTexture/DetailMaskTexture:
  1. Sample ProvinceIDTexture in 5x5 radius (25 samples for smooth falloff)
  2. Get terrain type for each sample via ProvinceTerrainBuffer
  3. Count occurrences: {grass: 14, forest: 8, mountain: 3}
  4. Normalize to weights: {grass: 0.56, forest: 0.32, mountain: 0.12}
  5. Sort by weight, take top 4
  6. Write indices to DetailIndexTexture.RGBA
  7. Write weights to DetailMaskTexture.RGBA
```

**Step 2: Runtime Shader (4-Channel Sampling)**
```hlsl
// Sample DetailIndex + DetailMask
uint4 indices = _DetailIndexTexture.Sample(uv);  // (0, 12, 6, 0)
float4 weights = _DetailMaskTexture.Sample(uv);  // (0.6, 0.3, 0.1, 0.0)

// Sample each material from texture arrays
float4 albedo0 = _TerrainAlbedoArray[indices.r];  // Grass
float4 albedo1 = _TerrainAlbedoArray[indices.g];  // Forest
float4 albedo2 = _TerrainAlbedoArray[indices.b];  // Mountain
float4 albedo3 = _TerrainAlbedoArray[indices.a];  // Empty (unused)

// Blend with weights
float4 finalAlbedo = albedo0 * weights.r +
                     albedo1 * weights.g +
                     albedo2 * weights.b +
                     albedo3 * weights.a;
```

**Step 3: Bilinear Filtering (Ultra-Smooth Transitions)**
```hlsl
// Sample DetailIndex/DetailMask at 4 corners (bilinear quad)
// This gives up to 16 materials contributing per pixel!
// 4 samples × 4 channels = 16 possible materials

// Imperator does this automatically via hardware bilinear filtering
// on the index/mask textures
```

### Why This Creates Smooth Transitions

**At a province border:**
- Pixel A (inside province): 100% grass (1, 0, 0, 0) + (1.0, 0, 0, 0)
- Pixel B (1 pixel from border): 80% grass, 20% forest (1, 12, 0, 0) + (0.8, 0.2, 0, 0)
- Pixel C (at border): 50% grass, 50% forest (1, 12, 0, 0) + (0.5, 0.5, 0, 0)
- Pixel D (other side): 20% grass, 80% forest (1, 12, 0, 0) + (0.2, 0.8, 0, 0)
- Pixel E (deep inside): 100% forest (12, 0, 0, 0) + (1.0, 0, 0, 0)

**Result:** Gradual, natural transition like watercolor painting!

---

## Generation Algorithm (GPU Compute Shader)

### TerrainBlendMapGenerator.compute

**Kernel: GenerateBlendMaps**
```
[numthreads(8, 8, 1)]
void GenerateBlendMaps(uint3 id : SV_DispatchThreadID)
{
    // Sample ProvinceIDTexture in 5x5 radius
    float2 uv = id.xy / float2(mapWidth, mapHeight);

    // Count terrain types in neighborhood
    uint terrainCounts[27] = {0}; // One per terrain type

    for (int y = -2; y <= 2; y++) {
        for (int x = -2; x <= 2; x++) {
            float2 sampleUV = uv + float2(x, y) * texelSize;
            uint provinceID = SampleProvinceID(sampleUV);
            uint terrainType = _ProvinceTerrainBuffer[provinceID];
            terrainCounts[terrainType]++;
        }
    }

    // Find top 4 terrain types by count
    uint4 topIndices = FindTop4(terrainCounts);
    float4 topWeights = NormalizeWeights(terrainCounts, topIndices);

    // Apply smoothstep falloff for smooth transitions
    topWeights = smoothstep(0.0, 1.0, topWeights);

    // Write to output textures
    _DetailIndexTexture[id.xy] = topIndices;
    _DetailMaskTexture[id.xy] = topWeights;
}
```

**Optimizations:**
- 8x8 thread groups for GPU efficiency
- Shared memory for province ID samples (reduce texture fetches)
- Early exit for interior provinces (all samples same terrain)
- Smoothstep falloff for artistic control over blend width

---

## Shader Integration

### New Shader Properties
```hlsl
// Imperator-style blend system
Texture2D _DetailIndexTexture;       // RGBA8: 4 material indices
Texture2D _DetailMaskTexture;        // RGBA8: 4 blend weights
SamplerState sampler_DetailIndexTexture;  // Point filtering (no interpolation of indices)
SamplerState sampler_DetailMaskTexture;   // Bilinear filtering (smooth weight transitions)

// Terrain texture arrays
Texture2DArray _TerrainAlbedoArray;  // Diffuse colors
Texture2DArray _TerrainNormalArray;  // Tangent-space normals
Texture2DArray _TerrainMaterialArray; // AO/Roughness/Metallic

// Tiling parameters
float _DetailTiling;                 // World-space tiling factor (e.g., 0.01)
float _BlendSharpness;              // Smoothstep exponent for transition control
```

### Updated RenderTerrain() Function
```hlsl
float4 RenderTerrain(uint provinceID, float2 uv, float3 positionWS)
{
    // Sample blend maps
    uint4 indices = _DetailIndexTexture.Sample(sampler_DetailIndexTexture, uv);
    float4 weights = _DetailMaskTexture.Sample(sampler_DetailMaskTexture, uv);

    // Calculate world-space UVs for tiling detail textures
    float2 worldUV = positionWS.xz * _DetailTiling;

    // Calculate world-space normal from heightmap for tri-planar
    float3 terrainNormal = CalculateTerrainNormal(uv, _HeightScale);

    // Sample and blend up to 4 materials
    float4 finalAlbedo = float4(0, 0, 0, 0);
    float3 finalNormal = float3(0, 0, 1);
    float3 finalMaterial = float3(0, 0, 0);

    // Blend channel R
    if (weights.r > 0.01) {
        finalAlbedo += TriPlanarSampleArray(positionWS, terrainNormal, _TerrainAlbedoArray, indices.r, _DetailTiling) * weights.r;
        finalNormal += SampleNormal(_TerrainNormalArray, indices.r, worldUV) * weights.r;
        finalMaterial += _TerrainMaterialArray[indices.r].Sample(worldUV) * weights.r;
    }

    // Repeat for G, B, A channels...

    return finalAlbedo;
}
```

---

## Performance Characteristics

### Memory Cost (5632x2048 resolution)
- **DetailIndexTexture:** 5632×2048×4 = 46 MB uncompressed
  - With BC7 compression: ~12 MB
- **DetailMaskTexture:** 5632×2048×4 = 46 MB uncompressed
  - With BC7 compression: ~12 MB
- **Terrain Texture Arrays:** 27 layers × 1024×1024×4 bytes
  - Albedo: ~27 MB uncompressed → 7 MB BC7
  - Normal: ~27 MB uncompressed → 7 MB BC5
  - Material: ~27 MB uncompressed → 7 MB BC7
- **Total:** ~45 MB compressed (AAA+ quality, acceptable)

### Runtime Cost
- **Generation time:** ~50-100ms on modern GPU (one-time at load)
- **Shader cost:** +4 texture samples per pixel (index, mask, albedo, normal)
- **Frame time impact:** ~10-15% (acceptable for AAA+ visuals)
- **Mipmaps:** Automatic LOD reduces bandwidth at distance

### Scalability
- Supports 27 terrain types (current terrain_rgb.json5)
- Can expand to 256 types (8-bit indices)
- Resolution-independent (works at any map size)

---

## Implementation Phases

### Phase 1: TerrainBlendMapGenerator Component ✅ NEXT
**Location:** `Assets/Archon-Engine/Scripts/Map/Rendering/Terrain/TerrainBlendMapGenerator.cs`
- Create GPU compute shader for blend map generation
- Integrate with MapDataLoader initialization
- Generate DetailIndexTexture + DetailMaskTexture at load time

### Phase 2: Terrain Texture Array Loader
**Location:** `Assets/Archon-Engine/Scripts/Map/Rendering/Terrain/TerrainTextureArrayLoader.cs`
- Load terrain textures from `Assets/Data/textures/terrain/`
- Build Texture2DArray for albedo, normal, material
- Apply BC compression
- Generate mipmaps

### Phase 3: Shader Integration
- Update `MapModeTerrain.hlsl` with 4-channel blending
- Keep tri-planar projection for each material sample
- Add material property support (PBR workflow)

### Phase 4: Integration & Testing
- Wire into MapDataLoader + MapTextureManager
- Test at various zoom levels
- Performance profiling
- Visual quality validation

---

## Comparison: Simple vs Imperator Approach

### Simple Approach (MapModeTerrainSimple.hlsl)
- ✅ Low memory (~0 MB additional)
- ✅ Fast shader (height-based blending only)
- ✅ Works in world-space
- ❌ No province-aware transitions
- ❌ Single terrain type per pixel
- **Use case:** Quick terrain mapmode, debugging

### Imperator Approach (MapModeTerrain.hlsl)
- ✅ Ultra-smooth province boundary transitions
- ✅ Up to 4 materials per pixel (16 with bilinear)
- ✅ AAA+ visual quality
- ✅ Procedurally generated (no hand-authoring)
- ❌ Higher memory cost (~45 MB)
- ❌ Generation time at load (~50-100ms)
- **Use case:** Production-quality terrain rendering

---

## Related Documents

- **Imperator Analysis:** `Assets/Archon-Engine/Docs/Log/learnings/imperator-rome-terrain-rendering-analysis.md`
- **Tri-Planar Projection:** Already implemented in MapModeTerrainSimple.hlsl
- **Province Terrain Analysis:** `Assets/Archon-Engine/Scripts/Map/Rendering/Terrain/ProvinceTerrainAnalyzer.cs`

---

## Success Metrics

**Visual Quality:**
- ✅ Smooth watercolor-like transitions at province borders
- ✅ Distinct detail per terrain type (grass ≠ desert ≠ mountain)
- ✅ No visible tiling patterns
- ✅ Sharp detail at all zoom levels

**Performance:**
- ✅ <100ms generation time at load
- ✅ <15% frame time impact
- ✅ No stuttering when zooming

**Technical:**
- ✅ Procedurally generated (zero hand-authoring)
- ✅ Moddable (texture array system)
- ✅ Scales to any map size

---

*Implementation Started: 2025-11-19*
*Based on: Imperator Rome shader analysis (RenderDoc reverse-engineering)*
*Target: AAA+ terrain quality with smooth province transitions*
