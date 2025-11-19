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

// Helper function: Get terrain color by index
float3 GetTerrainColor(uint terrainTypeIndex)
{
    // Map terrain type index to color
    // Indices based on ORDER in terrain_rgb.json5
    // Colors converted from RGB(r,g,b) to float3(r/255, g/255, b/255)
    float3 terrainColor = float3(0.5, 0.5, 0.5); // Default: gray

    // Terrain indices from terrain_rgb.json5 (in order):
    // IMPORTANT: Must match ORDER in terrain_rgb.json5 exactly
    // Using natural, visually appealing colors for each terrain type
    if (terrainTypeIndex == 0)       terrainColor = float3(0.45, 0.65, 0.30);    // grasslands - lush green
    else if (terrainTypeIndex == 1)  terrainColor = float3(0.55, 0.50, 0.35);    // hills - earthy brown-green
    else if (terrainTypeIndex == 2)  terrainColor = float3(0.50, 0.40, 0.30);    // desert_mountain - brown mountain
    else if (terrainTypeIndex == 3)  terrainColor = float3(0.85, 0.75, 0.50);    // desert - sandy yellow
    else if (terrainTypeIndex == 4)  terrainColor = float3(0.60, 0.75, 0.45);    // plains - light green
    else if (terrainTypeIndex == 5)  terrainColor = float3(0.40, 0.60, 0.35);    // terrain_5 (grasslands) - medium green
    else if (terrainTypeIndex == 6)  terrainColor = float3(0.45, 0.40, 0.35);    // mountain - dark rocky brown
    else if (terrainTypeIndex == 7)  terrainColor = float3(0.75, 0.65, 0.45);    // desert_mountain_low - pale desert
    else if (terrainTypeIndex == 8)  terrainColor = float3(0.50, 0.55, 0.30);    // terrain_8 (hills) - olive green
    else if (terrainTypeIndex == 9)  terrainColor = float3(0.40, 0.55, 0.50);    // marsh - murky blue-green
    else if (terrainTypeIndex == 10) terrainColor = float3(0.65, 0.75, 0.50);    // terrain_10 (farmlands) - fertile yellow-green
    else if (terrainTypeIndex == 11) terrainColor = float3(0.70, 0.80, 0.55);    // terrain_11 (farmlands) - bright farmland
    else if (terrainTypeIndex == 12) terrainColor = float3(0.25, 0.40, 0.20);    // forest_12 - dark forest green
    else if (terrainTypeIndex == 13) terrainColor = float3(0.30, 0.45, 0.25);    // forest_13 - medium forest
    else if (terrainTypeIndex == 14) terrainColor = float3(0.35, 0.50, 0.30);    // forest_14 - lighter forest
    else if (terrainTypeIndex == 15) terrainColor = float3(0.10, 0.30, 0.55);    // ocean - deep blue
    else if (terrainTypeIndex == 16) terrainColor = float3(0.90, 0.92, 0.95);    // snow - bright white-blue
    else if (terrainTypeIndex == 17) terrainColor = float3(0.30, 0.50, 0.70);    // inland_ocean - medium blue
    else if (terrainTypeIndex == 18) terrainColor = float3(0.80, 0.70, 0.50);    // coastal_desert - pale sand
    else if (terrainTypeIndex == 19) terrainColor = float3(0.90, 0.85, 0.60);    // coastline - sandy beach
    else if (terrainTypeIndex == 20) terrainColor = float3(0.75, 0.70, 0.45);    // savannah - dry grassland yellow
    else if (terrainTypeIndex == 21) terrainColor = float3(0.70, 0.60, 0.40);    // drylands - arid brown-yellow
    else if (terrainTypeIndex == 22) terrainColor = float3(0.55, 0.50, 0.40);    // highlands - elevated rocky terrain
    else if (terrainTypeIndex == 23) terrainColor = float3(0.65, 0.55, 0.40);    // dry_highlands - arid highlands
    else if (terrainTypeIndex == 24) terrainColor = float3(0.35, 0.50, 0.30);    // woods - medium green woodland
    else if (terrainTypeIndex == 25) terrainColor = float3(0.20, 0.45, 0.25);    // jungle - deep tropical green
    else if (terrainTypeIndex == 26) terrainColor = float3(0.70, 0.78, 0.52);    // terrain_21 (farmlands) - cultivated land

    return terrainColor;
}

float4 RenderTerrainInternal(uint provinceID, float2 uv, float3 positionWS)
{
    // Handle ocean/invalid provinces
    if (provinceID == 0)
    {
        return _OceanColor; // Configurable from GAME layer
    }

    // ============================================================================
    // IMPERATOR ROME-STYLE 4-CHANNEL BLENDING WITH MANUAL BILINEAR INTERPOLATION
    // Sample DetailIndexTexture + DetailMaskTexture with manual 4-tap filtering
    // This creates ultra-smooth terrain transitions like Imperator Rome
    // ============================================================================

    float3 terrainColor;
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Get texture dimensions for manual bilinear sampling
    float2 texSize = float2(5632, 2048); // TODO: Pass as shader param
    float2 texelSize = 1.0 / texSize;

    // Calculate pixel position and fractional offset (Imperator Rome style, lines 45-55)
    float2 pixelPos = correctedUV * texSize;
    float2 pixelPosFloor = floor(pixelPos);
    float2 fractional = pixelPos - pixelPosFloor;

    // Calculate bilinear weights for 4 neighboring pixels
    float weight00 = (1.0 - fractional.x) * (1.0 - fractional.y); // Top-left
    float weight10 = fractional.x * (1.0 - fractional.y);         // Top-right
    float weight01 = (1.0 - fractional.x) * fractional.y;         // Bottom-left
    float weight11 = fractional.x * fractional.y;                 // Bottom-right

    // Sample 4 neighboring pixels from both textures (Imperator Rome lines 59-100)
    float2 uv00 = (pixelPosFloor + float2(0.5, 0.5)) * texelSize;
    float2 uv10 = (pixelPosFloor + float2(1.5, 0.5)) * texelSize;
    float2 uv01 = (pixelPosFloor + float2(0.5, 1.5)) * texelSize;
    float2 uv11 = (pixelPosFloor + float2(1.5, 1.5)) * texelSize;

    float4 indices00 = SAMPLE_TEXTURE2D(_DetailIndexTexture, sampler_DetailIndexTexture, uv00) * 255.0;
    float4 indices10 = SAMPLE_TEXTURE2D(_DetailIndexTexture, sampler_DetailIndexTexture, uv10) * 255.0;
    float4 indices01 = SAMPLE_TEXTURE2D(_DetailIndexTexture, sampler_DetailIndexTexture, uv01) * 255.0;
    float4 indices11 = SAMPLE_TEXTURE2D(_DetailIndexTexture, sampler_DetailIndexTexture, uv11) * 255.0;

    float4 mask00 = SAMPLE_TEXTURE2D(_DetailMaskTexture, sampler_DetailMaskTexture, uv00);
    float4 mask10 = SAMPLE_TEXTURE2D(_DetailMaskTexture, sampler_DetailMaskTexture, uv10);
    float4 mask01 = SAMPLE_TEXTURE2D(_DetailMaskTexture, sampler_DetailMaskTexture, uv01);
    float4 mask11 = SAMPLE_TEXTURE2D(_DetailMaskTexture, sampler_DetailMaskTexture, uv11);

    // Accumulate weights for all terrain types found in the 4 samples (max 27 types)
    float terrainWeights[27];
    for (int i = 0; i < 27; i++)
        terrainWeights[i] = 0.0;

    // Accumulate weights from all 4 neighboring pixels
    for (int channel = 0; channel < 4; channel++)
    {
        uint idx00 = (uint)(indices00[channel] + 0.5);
        uint idx10 = (uint)(indices10[channel] + 0.5);
        uint idx01 = (uint)(indices01[channel] + 0.5);
        uint idx11 = (uint)(indices11[channel] + 0.5);

        if (idx00 < 27) terrainWeights[idx00] += mask00[channel] * weight00;
        if (idx10 < 27) terrainWeights[idx10] += mask10[channel] * weight10;
        if (idx01 < 27) terrainWeights[idx01] += mask01[channel] * weight01;
        if (idx11 < 27) terrainWeights[idx11] += mask11[channel] * weight11;
    }

    // Find top 4 terrain types by weight (simple selection for top 4)
    uint topIndices[4] = {0, 0, 0, 0};
    float topWeights[4] = {0, 0, 0, 0};

    for (uint terrainIdx = 0; terrainIdx < 27; terrainIdx++)
    {
        float weight = terrainWeights[terrainIdx];
        if (weight > topWeights[3])
        {
            // Insert into top 4
            if (weight > topWeights[0])
            {
                topWeights[3] = topWeights[2]; topIndices[3] = topIndices[2];
                topWeights[2] = topWeights[1]; topIndices[2] = topIndices[1];
                topWeights[1] = topWeights[0]; topIndices[1] = topIndices[0];
                topWeights[0] = weight; topIndices[0] = terrainIdx;
            }
            else if (weight > topWeights[1])
            {
                topWeights[3] = topWeights[2]; topIndices[3] = topIndices[2];
                topWeights[2] = topWeights[1]; topIndices[2] = topIndices[1];
                topWeights[1] = weight; topIndices[1] = terrainIdx;
            }
            else if (weight > topWeights[2])
            {
                topWeights[3] = topWeights[2]; topIndices[3] = topIndices[2];
                topWeights[2] = weight; topIndices[2] = terrainIdx;
            }
            else
            {
                topWeights[3] = weight; topIndices[3] = terrainIdx;
            }
        }
    }

    // Get terrain colors and blend with accumulated weights
    float3 color0 = GetTerrainColor(topIndices[0]);
    float3 color1 = GetTerrainColor(topIndices[1]);
    float3 color2 = GetTerrainColor(topIndices[2]);
    float3 color3 = GetTerrainColor(topIndices[3]);

    terrainColor = color0 * topWeights[0] +
                   color1 * topWeights[1] +
                   color2 * topWeights[2] +
                   color3 * topWeights[3];

    float4 macroColor = float4(terrainColor, 1.0);

    // ============================================================================
    // TERRAIN DETAIL TEXTURE MAPPING (Imperator Rome-style)
    // Sample terrain detail textures using same indices and weights
    // This adds micro-level sharp texture detail on top of smooth macro colors
    // ============================================================================
    #ifdef TERRAIN_DETAIL_MAPPING
    if (_DetailStrength > 0.0)
    {
        // Calculate world-space tiling for detail textures
        float2 worldUV = positionWS.xz * _DetailTiling;

        // Sample detail textures for top 4 terrain types
        // Use simple UV tiling (no tri-planar for now, can add later if needed)
        float4 detail0 = SAMPLE_TEXTURE2D_ARRAY(_TerrainDetailArray, sampler_TerrainDetailArray, worldUV, topIndices[0]);
        float4 detail1 = SAMPLE_TEXTURE2D_ARRAY(_TerrainDetailArray, sampler_TerrainDetailArray, worldUV, topIndices[1]);
        float4 detail2 = SAMPLE_TEXTURE2D_ARRAY(_TerrainDetailArray, sampler_TerrainDetailArray, worldUV, topIndices[2]);
        float4 detail3 = SAMPLE_TEXTURE2D_ARRAY(_TerrainDetailArray, sampler_TerrainDetailArray, worldUV, topIndices[3]);

        // Blend detail textures with same weights as colors
        float3 detailColor = detail0.rgb * topWeights[0] +
                             detail1.rgb * topWeights[1] +
                             detail2.rgb * topWeights[2] +
                             detail3.rgb * topWeights[3];

        // Multiply detail onto macro color (Imperator Rome approach)
        // Detail textures are authored as "multiply" textures (neutral gray = 128,128,128)
        // This preserves the smooth color blending while adding crisp texture detail
        macroColor.rgb = macroColor.rgb * (detailColor * 2.0); // *2.0 to convert 0.5 neutral gray to 1.0

        // Optional: Blend between smooth and detailed based on _DetailStrength
        // macroColor.rgb = lerp(terrainColor, macroColor.rgb, _DetailStrength);
    }
    #endif

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