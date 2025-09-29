#ifndef MAPMODE_TERRAIN_INCLUDED
#define MAPMODE_TERRAIN_INCLUDED

// ============================================================================
// Terrain Map Mode
// Renders provinces using terrain colors from the original provinces.bmp
// This is the simplest mode - direct color sampling from ProvinceColorTexture
// ============================================================================

float4 RenderTerrain(uint provinceID, float2 uv)
{
    // Handle ocean/invalid provinces
    if (provinceID == 0)
    {
        return OCEAN_COLOR;
    }

    // Fix flipped UV coordinates
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Sample terrain colors directly from the province color texture
    // This texture contains the original colors from provinces.bmp
    float4 terrainColor = SAMPLE_TEXTURE2D(_ProvinceColorTexture, sampler_ProvinceColorTexture, correctedUV);

    return terrainColor;
}

#endif // MAPMODE_TERRAIN_INCLUDED