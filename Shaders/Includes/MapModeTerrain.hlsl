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

    // Fix flipped UV coordinates
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // MACRO TEXTURE: Sample terrain color (only used as fallback if detail mapping disabled)
    // The macro texture (terrain.bmp) is pixelated and only for boundaries/simulation data
    float4 macroColor = SAMPLE_TEXTURE2D(_ProvinceTerrainTexture, sampler_ProvinceTerrainTexture, correctedUV);

    // ============================================================================
    // DEBUG MODE: Show raw province terrain assignments
    // Enabled to verify terrain assignments are working correctly
    // Each terrain type gets a distinct debug color for easy visual inspection
    // ============================================================================
    #ifdef TERRAIN_DETAIL_MAPPING
    uint rawTerrain = (provinceID > 0 && provinceID < 65536) ? _ProvinceTerrainBuffer[provinceID] : 0;

    // Map terrain types to distinct colors for debugging
    // From terrain.json5 "terrain" section palette indices
    float3 debugColor = float3(0, 0, 0);
    if (rawTerrain == 0)       debugColor = float3(0.2, 0.8, 0.2);   // Grasslands = bright green
    else if (rawTerrain == 1)  debugColor = float3(0.4, 0.6, 0.5);   // Hills = teal
    else if (rawTerrain == 2)  debugColor = float3(0.5, 0.3, 0.2);   // Desert mountain = dark brown
    else if (rawTerrain == 3)  debugColor = float3(0.9, 0.9, 0.4);   // Desert = yellow
    else if (rawTerrain == 6)  debugColor = float3(0.6, 0.4, 0.3);   // Mountain = brown
    else if (rawTerrain == 9)  debugColor = float3(0.2, 0.6, 0.5);   // Marsh = cyan
    else if (rawTerrain == 12) debugColor = float3(0.1, 0.5, 0.1);   // Forest = dark green
    else if (rawTerrain == 16) debugColor = float3(1.0, 1.0, 1.0);   // Snow = white
    else if (rawTerrain == 17) debugColor = float3(0.7, 1.0, 0.3);   // Farmlands = light green
    else if (rawTerrain == 35) debugColor = float3(0.3, 0.7, 0.9);   // Coastline = light blue
    else if (rawTerrain == 254) debugColor = float3(0.4, 0.8, 0.3);  // Jungle = lime green
    else if (rawTerrain == 255) debugColor = float3(0.2, 0.6, 0.2);  // Woods = medium green
    else                       debugColor = float3(rawTerrain / 255.0, 0.0, 1.0 - rawTerrain / 255.0); // Unknown = purple gradient

    return float4(debugColor, 1.0);
    #endif

    #ifdef TERRAIN_DETAIL_MAPPING
    // TERRAIN DETAIL MAPPING (only in shaders with TERRAIN_DETAIL_MAPPING defined)

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
            // PROVINCE-BASED TERRAIN with HEIGHT MODULATION
            // Province defines base terrain (from terrain.bmp majority vote)
            // Height modulates within province for smooth variation
            // Uses world-space noise for organic blending

            // Get province's assigned terrain type
            uint provinceTerrainType = 0;
            if (provinceID > 0 && provinceID < 65536)
            {
                provinceTerrainType = _ProvinceTerrainBuffer[provinceID];
            }

            // Generate noise from world position for variation
            float2 noiseCoord = positionWS.xz * 0.1;
            float noise = frac(sin(dot(noiseCoord, float2(12.9898, 78.233))) * 43758.5453);

            // Use height + noise to modulate terrain type within province
            // This creates smooth variation while respecting province assignment
            float heightNoise = height + noise * 0.05; // Add small noise variation

            // Define common terrain transition types
            const uint TERRAIN_GRASS = 0;
            const uint TERRAIN_FOREST = 12;
            const uint TERRAIN_MOUNTAIN = 6;
            const uint TERRAIN_SNOW = 16;

            // Determine blend neighbors based on height
            uint terrain1 = provinceTerrainType; // Base
            uint terrain2 = provinceTerrainType; // Variation
            float blendFactor = 0.0;

            // Height-based variation: modulate towards mountain/snow at high elevations
            if (heightNoise > 0.20)
            {
                terrain2 = TERRAIN_SNOW;
                blendFactor = saturate((heightNoise - 0.20) / 0.10);
            }
            else if (heightNoise > 0.15)
            {
                terrain2 = TERRAIN_MOUNTAIN;
                blendFactor = saturate((heightNoise - 0.15) / 0.05);
            }
            else if (heightNoise < 0.14 && provinceTerrainType != TERRAIN_GRASS)
            {
                // Blend towards grassland at low elevations
                terrain2 = TERRAIN_GRASS;
                blendFactor = saturate((0.14 - heightNoise) / 0.04);
            }

            // Apply smoothstep to blend factor
            blendFactor = blendFactor * blendFactor * (3.0 - 2.0 * blendFactor);

            // Sample both terrain types
            float4 baseTerrain = TriPlanarSampleArray(positionWS, terrainNormal, terrain1, _DetailTiling);
            float4 variationTerrain = TriPlanarSampleArray(positionWS, terrainNormal, terrain2, _DetailTiling);

            // Blend based on height/noise
            float4 blendedDetail = lerp(baseTerrain, variationTerrain, blendFactor);

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