#ifndef MAPMODE_TERRAIN_INCLUDED
#define MAPMODE_TERRAIN_INCLUDED

// ============================================================================
// Terrain Map Mode with Hybrid Detail Mapping
// Renders terrain using dual-layer architecture:
// - Coarse: Province terrain category from terrain.bmp (via GPU analysis)
// - Fine: World-space procedural detail (resolution-independent)
// - Anti-tiling: Tri-planar mapping to eliminate stretching
// ============================================================================
// Province terrain lookup declared in DefaultCommon.hlsl
// Enables hybrid terrain: coarse category from terrain.bmp + fine procedural detail

#ifdef TERRAIN_DETAIL_MAPPING
// ============================================================================
// Tri-Planar Texture Sampling
// Eliminates stretching on steep slopes by sampling from 3 axes and blending
// Based on EU5's implementation (pixel.txt:613)
// ============================================================================

float4 TriPlanarSampleArray(float3 positionWS, float3 normalWS, uint arrayIndex, float tiling)
{
    // Calculate world-space UVs for each axis projection
    float2 uvX = positionWS.zy * tiling; // YZ plane (side view)
    float2 uvY = positionWS.xz * tiling; // XZ plane (top/bottom view)
    float2 uvZ = positionWS.xy * tiling; // XY plane (front/back view)

    // Sample texture from all 3 axes
    float4 colX = SAMPLE_TEXTURE2D_ARRAY(_TerrainDetailArray, sampler_TerrainDetailArray, uvX, arrayIndex);
    float4 colY = SAMPLE_TEXTURE2D_ARRAY(_TerrainDetailArray, sampler_TerrainDetailArray, uvY, arrayIndex);
    float4 colZ = SAMPLE_TEXTURE2D_ARRAY(_TerrainDetailArray, sampler_TerrainDetailArray, uvZ, arrayIndex);

    // Calculate blend weights based on surface normal
    // abs(normal) gives weight for each axis
    // pow(weight, tightenFactor) sharpens the transition
    // _TriPlanarTightenFactor: Higher values = sharper transitions (1.0-8.0, EU5 uses ~4.0)
    float3 blendWeights = abs(normalWS);
    blendWeights = pow(blendWeights, _TriPlanarTightenFactor);

    // Normalize weights so they sum to 1.0
    blendWeights /= (blendWeights.x + blendWeights.y + blendWeights.z);

    // Blend the 3 samples
    return colX * blendWeights.x + colY * blendWeights.y + colZ * blendWeights.z;
}

// Calculate world-space normal from heightmap gradients
// This gives us the terrain surface normal for tri-planar blending
float3 CalculateTerrainNormal(float2 uv, float heightScale)
{
    // Correct UV for texture orientation
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Sample heightmap at current position and neighbors
    // Use small offset for gradient calculation
    float2 texelSize = float2(1.0 / 2048.0, 1.0 / 2048.0); // Adjust based on heightmap resolution

    float heightC = SAMPLE_TEXTURE2D(_HeightmapTexture, sampler_HeightmapTexture, correctedUV).r;
    float heightL = SAMPLE_TEXTURE2D(_HeightmapTexture, sampler_HeightmapTexture, correctedUV + float2(-texelSize.x, 0)).r;
    float heightR = SAMPLE_TEXTURE2D(_HeightmapTexture, sampler_HeightmapTexture, correctedUV + float2(texelSize.x, 0)).r;
    float heightD = SAMPLE_TEXTURE2D(_HeightmapTexture, sampler_HeightmapTexture, correctedUV + float2(0, -texelSize.y)).r;
    float heightU = SAMPLE_TEXTURE2D(_HeightmapTexture, sampler_HeightmapTexture, correctedUV + float2(0, texelSize.y)).r;

    // Calculate gradients (central difference)
    float dx = (heightR - heightL) * heightScale;
    float dy = (heightU - heightD) * heightScale;

    // Construct normal vector
    // In Unity: Y is up, so normal is (dx, 1, dy) unnormalized
    float3 normal = normalize(float3(-dx, 1.0, -dy));

    return normal;
}

// Legacy function for backward compatibility
// Now just wraps TriPlanarSampleArray with a default normal
float4 NoTilingTextureArray(float2 uv, uint arrayIndex)
{
    // For backward compatibility, use simple planar sampling
    // This will be replaced by TriPlanarSampleArray calls
    return SAMPLE_TEXTURE2D_ARRAY(_TerrainDetailArray, sampler_TerrainDetailArray, uv, arrayIndex);
}

// ============================================================================
// Province Terrain Category Lookup (Hybrid Mode)
// ============================================================================

/// <summary>
/// Get terrain category for a province from lookup texture
/// Returns terrain type index (0-255) from terrain.bmp majority vote
/// Falls back to 0 (grassland) if lookup texture not bound
/// </summary>
uint GetProvinceTerrainCategory(uint provinceID)
{
    // Look up terrain type from buffer (same pattern as PopulateOwnerTexture.compute)
    // ProvinceTerrainBuffer[provinceID] = terrainTypeIndex (0-255)
    uint terrainCategory = 0;  // Default to grassland

    // Bounds check (assuming max 65536 provinces)
    if (provinceID > 0 && provinceID < 65536)
    {
        terrainCategory = _ProvinceTerrainBuffer[provinceID];
    }

    return terrainCategory;
}

// ============================================================================
#endif

float4 RenderTerrainInternal(uint provinceID, float2 uv, float3 positionWS)
{
    // Handle ocean/invalid provinces
    if (provinceID == 0)
    {
        return _OceanColor; // Configurable from GAME layer
    }

    // Per-province terrain color from buffer + palette
    // ProvinceTerrainBuffer: province ID → terrain type index (from GPU analysis + overrides)
    // TerrainColorPalette: terrain type index → color (from terrain.json5)
    uint terrainType = 0;
    if (provinceID > 0 && provinceID < 65536)
    {
        terrainType = _ProvinceTerrainBuffer[provinceID];
    }

    // Sample color from terrain palette (32x1 texture, same as SampleTerrainColor)
    float2 paletteUV = float2((terrainType + 0.5) / 32.0, 0.5);
    float3 terrainColor = SAMPLE_TEXTURE2D(_TerrainColorPalette, sampler_TerrainColorPalette, paletteUV).rgb;

    float4 macroColor = float4(terrainColor, 1.0);

    // Optional: detail texture overlay when TERRAIN_DETAIL_MAPPING is enabled
    #ifdef TERRAIN_DETAIL_MAPPING
    if (_DetailStrength > 0.0 && positionWS.x != 0.0)
    {
        float2 worldUV = positionWS.xz * _DetailTiling;
        float4 detail = SAMPLE_TEXTURE2D_ARRAY(_TerrainDetailArray, sampler_TerrainDetailArray, worldUV, terrainType);
        macroColor.rgb = macroColor.rgb * (detail.rgb * 2.0);
    }
    #endif

    return macroColor;
}

// 3-parameter version (with detail mapping)
float4 RenderTerrain(uint provinceID, float2 uv, float3 positionWS)
{
    return RenderTerrainInternal(provinceID, uv, positionWS);
}

// 2-parameter version (backward compatibility, no detail mapping)
float4 RenderTerrain(uint provinceID, float2 uv)
{
    return RenderTerrainInternal(provinceID, uv, float3(0, 0, 0));
}

// ============================================================================
// Province Color Map Mode (Simple)
// Renders provinces using original colors from provinces.bmp
// Useful for debugging province boundaries and IDs
// ============================================================================

float4 RenderProvinceColors(uint provinceID, float2 uv)
{
    // Handle ocean/invalid provinces
    if (provinceID == 0)
    {
        return _OceanColor;
    }

    // Fix flipped UV coordinates
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Sample province colors directly from provinces.bmp
    float4 provinceColor = SAMPLE_TEXTURE2D(_ProvinceColorTexture, sampler_ProvinceColorTexture, correctedUV);

    return provinceColor;
}

#endif // MAPMODE_TERRAIN_INCLUDED