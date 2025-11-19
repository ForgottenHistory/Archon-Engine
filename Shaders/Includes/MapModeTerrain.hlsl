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

    // PROVINCE TERRAIN TYPE: Get terrain type for this province
    // Each province shows a solid color based on its assigned terrain type
    uint terrainTypeIndex = 0;
    if (provinceID > 0 && provinceID < 65536)
    {
        terrainTypeIndex = _ProvinceTerrainBuffer[provinceID];
    }

    // DEBUG: Show magenta to verify this code is executing
    // return float4(1, 0, 1, 1);

    // Map terrain type index to color
    // Indices based on ORDER in terrain_rgb.json5
    // Colors converted from RGB(r,g,b) to float3(r/255, g/255, b/255)
    float3 terrainColor = float3(0.5, 0.5, 0.5); // Default: gray

    // Terrain indices from terrain_rgb.json5 (in order):
    if (terrainTypeIndex == 0)       terrainColor = float3(86.0/255.0, 124.0/255.0, 27.0/255.0);      // grasslands RGB(86,124,27)
    else if (terrainTypeIndex == 1)  terrainColor = float3(0.0/255.0, 86.0/255.0, 6.0/255.0);         // hills RGB(0,86,6)
    else if (terrainTypeIndex == 2)  terrainColor = float3(112.0/255.0, 74.0/255.0, 31.0/255.0);      // desert_mountain RGB(112,74,31)
    else if (terrainTypeIndex == 3)  terrainColor = float3(206.0/255.0, 169.0/255.0, 99.0/255.0);     // desert RGB(206,169,99)
    else if (terrainTypeIndex == 4)  terrainColor = float3(200.0/255.0, 214.0/255.0, 107.0/255.0);    // plains RGB(200,214,107)
    else if (terrainTypeIndex == 5)  terrainColor = float3(13.0/255.0, 96.0/255.0, 62.0/255.0);       // terrain_5 RGB(13,96,62)
    else if (terrainTypeIndex == 6)  terrainColor = float3(65.0/255.0, 42.0/255.0, 17.0/255.0);       // mountain RGB(65,42,17)
    else if (terrainTypeIndex == 7)  terrainColor = float3(158.0/255.0, 130.0/255.0, 77.0/255.0);     // desert_mountain_low RGB(158,130,77)
    else if (terrainTypeIndex == 8)  terrainColor = float3(53.0/255.0, 77.0/255.0, 17.0/255.0);       // terrain_8 RGB(53,77,17)
    else if (terrainTypeIndex == 9)  terrainColor = float3(75.0/255.0, 147.0/255.0, 174.0/255.0);     // marsh RGB(75,147,174)
    else if (terrainTypeIndex == 10) terrainColor = float3(155.0/255.0, 155.0/255.0, 155.0/255.0);    // terrain_10 RGB(155,155,155)
    else if (terrainTypeIndex == 11) terrainColor = float3(255.0/255.0, 0.0/255.0, 0.0/255.0);        // terrain_11 RGB(255,0,0)
    else if (terrainTypeIndex == 12) terrainColor = float3(42.0/255.0, 55.0/255.0, 22.0/255.0);       // forest_12 RGB(42,55,22)
    else if (terrainTypeIndex == 13) terrainColor = float3(213.0/255.0, 144.0/255.0, 199.0/255.0);    // forest_13 RGB(213,144,199)
    else if (terrainTypeIndex == 14) terrainColor = float3(127.0/255.0, 24.0/255.0, 60.0/255.0);      // forest_14 RGB(127,24,60)
    else if (terrainTypeIndex == 15) terrainColor = float3(8.0/255.0, 31.0/255.0, 130.0/255.0);       // ocean RGB(8,31,130)
    else if (terrainTypeIndex == 16) terrainColor = float3(255.0/255.0, 255.0/255.0, 255.0/255.0);    // snow RGB(255,255,255)
    else if (terrainTypeIndex == 17) terrainColor = float3(55.0/255.0, 90.0/255.0, 220.0/255.0);      // inland_ocean_17 RGB(55,90,220)
    else if (terrainTypeIndex == 18) terrainColor = float3(203.0/255.0, 191.0/255.0, 103.0/255.0);    // coastal_desert_18 RGB(203,191,103)
    else if (terrainTypeIndex == 19) terrainColor = float3(255.0/255.0, 247.0/255.0, 0.0/255.0);      // coastline RGB(255,247,0)
    else if (terrainTypeIndex == 20) terrainColor = float3(0.0/255.0, 0.0/255.0, 0.0/255.0);          // savannah RGB(0,0,0)
    else if (terrainTypeIndex == 21) terrainColor = float3(23.0/255.0, 23.0/255.0, 23.0/255.0);       // highlands RGB(23,23,23)
    else if (terrainTypeIndex == 22) terrainColor = float3(24.0/255.0, 24.0/255.0, 24.0/255.0);       // dry_highlands RGB(24,24,24)
    else if (terrainTypeIndex == 23) terrainColor = float3(254.0/255.0, 254.0/255.0, 254.0/255.0);    // jungle RGB(254,254,254)
    else if (terrainTypeIndex == 24) terrainColor = float3(21.0/255.0, 21.0/255.0, 21.0/255.0);       // terrain_21 RGB(21,21,21)

    float4 macroColor = float4(terrainColor, 1.0);

    // ============================================================================
    // DEBUG MODE: Show raw province terrain assignments (DISABLED)
    // Was showing debug colors instead of actual terrain texture
    // Uncomment the code below to enable debug visualization
    // ============================================================================
    /*
    #ifdef TERRAIN_DETAIL_MAPPING
    uint rawTerrain = (provinceID > 0 && provinceID < 65536) ? _ProvinceTerrainBuffer[provinceID] : 0;

    // Map terrain types to distinct colors for debugging
    float3 debugColor = float3(0, 0, 0);
    if (rawTerrain == 0)       debugColor = float3(0.4, 0.8, 0.3);   // Grasslands = green
    else if (rawTerrain == 1)  debugColor = float3(0.6, 0.5, 0.3);   // Hills = tan/brown
    else if (rawTerrain == 2)  debugColor = float3(0.7, 0.4, 0.2);   // Desert mountain = orange-brown
    else if (rawTerrain == 3)  debugColor = float3(0.95, 0.85, 0.5); // Desert = sandy yellow
    else if (rawTerrain == 4)  debugColor = float3(0.5, 0.75, 0.4);  // Plains = light green
    else if (rawTerrain == 6)  debugColor = float3(0.5, 0.4, 0.35);  // Mountain = dark brown
    else if (rawTerrain == 7)  debugColor = float3(0.85, 0.75, 0.45);// Desert mountain low = pale desert
    else if (rawTerrain == 9)  debugColor = float3(0.3, 0.5, 0.4);   // Marsh = murky green-gray
    else if (rawTerrain == 12) debugColor = float3(0.2, 0.5, 0.2);   // Forest = dark green
    else if (rawTerrain == 15) debugColor = float3(0.1, 0.3, 0.6);   // Ocean = deep blue
    else if (rawTerrain == 16) debugColor = float3(0.95, 0.95, 1.0); // Snow = bright white
    else if (rawTerrain == 17) debugColor = float3(0.5, 0.75, 0.85); // Inland ocean = light blue
    else if (rawTerrain == 18) debugColor = float3(0.6, 0.85, 0.4);  // Farmlands = fertile green
    else if (rawTerrain == 19) debugColor = float3(0.95, 0.8, 0.55); // Coastal desert = pale sand
    else if (rawTerrain == 20) debugColor = float3(0.9, 0.75, 0.4);  // Savannah = golden yellow
    else if (rawTerrain == 22) debugColor = float3(0.8, 0.7, 0.5);   // Drylands = beige
    else if (rawTerrain == 23) debugColor = float3(0.65, 0.5, 0.35); // Highlands = brown
    else if (rawTerrain == 35) debugColor = float3(0.4, 0.7, 0.85);  // Coastline = light blue
    else if (rawTerrain == 254) debugColor = float3(0.3, 0.6, 0.25); // Jungle = deep green
    else if (rawTerrain == 255) debugColor = float3(0.35, 0.55, 0.3);// Woods = medium green
    else                       debugColor = float3(rawTerrain / 255.0, 0.0, 1.0 - rawTerrain / 255.0); // Unknown = purple gradient

    return float4(debugColor, 1.0);
    #endif
    */

    // ============================================================================
    // TERRAIN DETAIL MAPPING (DISABLED - user request for simple raw colors)
    // All texture sampling, height blending, and detail effects disabled
    // ============================================================================
    /*
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

            // Generate noise from world position for variation (DISABLED - user request)
            // float2 noiseCoord = positionWS.xz * 0.1;
            // float noise = frac(sin(dot(noiseCoord, float2(12.9898, 78.233))) * 43758.5453);

            // Use height only (noise disabled for cleaner look)
            // This creates smooth variation while respecting province assignment
            float heightNoise = height; // No noise variation

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
    */

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