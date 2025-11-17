#ifndef MAPMODE_TERRAIN_INCLUDED
#define MAPMODE_TERRAIN_INCLUDED

// ============================================================================
// Terrain Map Mode with Detail Mapping
// Renders terrain using dual-layer architecture:
// - Macro: Terrain colors from terrain.bmp (unique per region)
// - Micro: Tiled detail textures (scale-independent sharpness)
// - Anti-tiling: Inigo Quilez method to break up repetition
// ============================================================================

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
#endif

float4 RenderTerrainInternal(uint provinceID, float2 uv, float3 positionWS)
{
    // Handle ocean/invalid provinces
    if (provinceID == 0)
    {
        return _OceanColor; // Configurable from GAME layer
    }

    // Fix flipped UV coordinates
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // MACRO TEXTURE: Sample terrain color (only used as fallback if detail mapping disabled)
    // The macro texture (terrain.bmp) is pixelated and only for boundaries/simulation data
    float4 macroColor = SAMPLE_TEXTURE2D(_ProvinceTerrainTexture, sampler_ProvinceTerrainTexture, correctedUV);

    // DEBUG: Uncomment to verify shader code is executing
    // return float4(1, 1, 0, 1);  // Yellow = shader code reached this point

    #ifdef TERRAIN_DETAIL_MAPPING
    // TERRAIN DETAIL MAPPING (only in shaders with TERRAIN_DETAIL_MAPPING defined)

    // DEBUG: Uncomment to verify TERRAIN_DETAIL_MAPPING is defined
    // return float4(1, 0, 1, 1);  // Magenta = define is active

    // Check if detail strength > 0 to enable detail mapping
    if (_DetailStrength > 0.0)
    {
        // Calculate world-space normal from heightmap for tri-planar blending
        float3 terrainNormal = CalculateTerrainNormal(uv, _HeightScale);

        // PROCEDURAL TERRAIN TYPE ASSIGNMENT: Calculate terrain type from world position
        // Resolution-independent because we use continuous world coordinates (like tessellation)
        // Use positionWS.xz for horizontal plane tiling
        float2 worldUV = positionWS.xz * _DetailTiling;

        // Sample heightmap at current world position (BEFORE terrain type checks)
        float height = SAMPLE_TEXTURE2D(_HeightmapTexture, sampler_HeightmapTexture, correctedUV).r;

        // WATER DETECTION (height-based, like EU4)
        // Our heightmap range: mountain peaks at ~0.25, so sea level must be lower
        // Water threshold should be below land (0.15 = grassland threshold)
        float waterThreshold = 0.09; // Below grassland level
        float beachThreshold = 0.12; // Small blend zone to grassland

        if (height < waterThreshold)
        {
            // Pure water - return ocean color
            macroColor.rgb = lerp(macroColor.rgb, _OceanColor.rgb, _DetailStrength);
        }
        else if (height < beachThreshold)
        {
            // Beach/coastal blend zone (0.05 - 0.08)
            float blendFactor = (height - waterThreshold) / (beachThreshold - waterThreshold);
            blendFactor = blendFactor * blendFactor * (3.0 - 2.0 * blendFactor); // Smoothstep

            // Blend water → grassland (beach terrain)
            float4 waterColor = _OceanColor;
            float4 beachDetail = TriPlanarSampleArray(positionWS, terrainNormal, 0, _DetailTiling); // Grassland as beach
            float4 coastalBlend = lerp(waterColor, beachDetail, blendFactor);

            macroColor.rgb = lerp(macroColor.rgb, coastalBlend.rgb, _DetailStrength);
        }
        else
        {
            // PROCEDURAL TERRAIN RULES (height-based)
            // Terrain type indices: 16=snow, 6=mountain, 0=grassland
            uint proceduralTerrainType;

            if (height > 0.25)
            {
                proceduralTerrainType = 16; // Snow
            }
            else if (height > 0.15)
            {
                proceduralTerrainType = 6; // Mountain
            }
            else
            {
                proceduralTerrainType = 0; // Grassland
            }

        // SMOOTH BLENDING between terrain types
        // Use height proximity to blend between adjacent terrain types
        float4 blendedDetail;

        if (height > 0.23 && height < 0.27)
        {
            // Blend zone between mountain and snow (0.23 - 0.27)
            float blendFactor = (height - 0.23) / 0.04;
            blendFactor = blendFactor * blendFactor * (3.0 - 2.0 * blendFactor); // Smoothstep

            float4 mountainDetail = TriPlanarSampleArray(positionWS, terrainNormal, 6, _DetailTiling);
            float4 snowDetail = TriPlanarSampleArray(positionWS, terrainNormal, 16, _DetailTiling);
            blendedDetail = lerp(mountainDetail, snowDetail, blendFactor);
        }
        else if (height > 0.13 && height < 0.17)
        {
            // Blend zone between grassland and mountain (0.13 - 0.17)
            float blendFactor = (height - 0.13) / 0.04;
            blendFactor = blendFactor * blendFactor * (3.0 - 2.0 * blendFactor); // Smoothstep

            float4 grassDetail = TriPlanarSampleArray(positionWS, terrainNormal, 0, _DetailTiling);
            float4 mountainDetail = TriPlanarSampleArray(positionWS, terrainNormal, 6, _DetailTiling);
            blendedDetail = lerp(grassDetail, mountainDetail, blendFactor);
        }
        else
        {
            // No blending - use single terrain type
            blendedDetail = TriPlanarSampleArray(positionWS, terrainNormal, proceduralTerrainType, _DetailTiling);
        }

            // REPLACE macro with procedural detail texture entirely
            // DetailStrength controls blend: 0 = macro only, 1 = detail only
            macroColor.rgb = lerp(macroColor.rgb, blendedDetail.rgb, _DetailStrength);
        } // End else (height >= beachThreshold - land terrain)
    } // End if (_DetailStrength > 0.0)
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