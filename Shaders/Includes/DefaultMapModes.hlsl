// DefaultMapModes.hlsl
// ENGINE: Map mode rendering logic (political, terrain, custom GAME modes)
// Part of Default visual style shader architecture
//
// Architecture:
// - Mode 0: Political (ENGINE built-in)
// - Mode 1: Terrain (ENGINE built-in)
// - Mode 2+: Custom GAME map modes via texture array
//
// GAME map modes write their visualization to a texture, ENGINE displays it.
// Switching modes = changing _CustomMapModeIndex (instant, no recomputation).

#ifndef DEFAULT_MAP_MODES_INCLUDED
#define DEFAULT_MAP_MODES_INCLUDED

// Map mode includes - ENGINE DEFAULT RENDERERS
// CRITICAL: Include MapModeCommon.hlsl FIRST - it defines SampleProvinceID(), SampleOwnerID(), etc.
#include "MapModeCommon.hlsl"
#include "MapModeTerrainSimple.hlsl"
#include "MapModePolitical.hlsl"

// Render custom GAME map mode from province palette texture
// ProvinceID -> palette lookup -> color (no per-pixel texture needed)
// Memory efficient: 100k provinces * 16 modes = ~6.4MB vs 6.24GB
float4 RenderCustomMapMode(uint provinceID, float2 uv)
{
    // Ocean check
    if (provinceID == 0)
    {
        return _OceanColor;
    }

    // Province palette layout:
    // - 256 columns (provinceID % 256)
    // - Rows = (provinceID / 256) + (modeIndex * rowsPerMode)
    // - rowsPerMode = ceil(maxProvinces / 256)

    // Calculate palette coordinates
    // _MaxProvinceID is set by C# to define rowsPerMode
    int rowsPerMode = (_MaxProvinceID + 255) / 256; // ceil division
    int col = provinceID % 256;
    int row = (provinceID / 256) + (_CustomMapModeIndex * rowsPerMode);

    // Convert to UV coordinates (add 0.5 for pixel center sampling)
    float2 paletteSize;
    _ProvincePaletteTexture.GetDimensions(paletteSize.x, paletteSize.y);
    float2 paletteUV = float2((col + 0.5) / paletteSize.x, (row + 0.5) / paletteSize.y);

    // Sample palette with point filtering (no interpolation)
    float4 color = SAMPLE_TEXTURE2D_LOD(_ProvincePaletteTexture, sampler_ProvincePaletteTexture, paletteUV, 0);

    // Alpha of 0 means "use default" (ocean, unowned, etc.)
    if (color.a < 0.01)
    {
        return _OceanColor;
    }

    return color;
}

// Render base color based on current map mode
// Returns base color before lighting/effects are applied
// ENGINE provides: Political (0), Terrain (1), Custom (2+)
// GAME layer registers custom map modes that write to texture array
float4 RenderMapMode(int mapMode, uint provinceID, float2 uv, float3 positionWS)
{
    float4 baseColor;

    if (mapMode == 0) // Political (ENGINE built-in)
    {
        baseColor = RenderPolitical(provinceID, uv);
        float luminance = dot(baseColor.rgb, float3(0.299, 0.587, 0.114));
        float3 grayscale = float3(luminance, luminance, luminance);
        baseColor.rgb = lerp(grayscale, baseColor.rgb, _CountryColorSaturation);
    }
    else if (mapMode == 1) // Terrain (ENGINE built-in)
    {
        baseColor = RenderTerrain(provinceID, uv, positionWS);
    }
    else if (mapMode == 12) // Province Colors (debug)
    {
        baseColor = RenderProvinceColors(provinceID, uv);
    }
    else if (mapMode >= 2 && mapMode < 100) // Custom GAME map modes (2-99)
    {
        // GAME map modes use texture array - instant switching
        baseColor = RenderCustomMapMode(provinceID, uv);
    }
    else // Default to political
    {
        baseColor = RenderPolitical(provinceID, uv);
        float luminance = dot(baseColor.rgb, float3(0.299, 0.587, 0.114));
        float3 grayscale = float3(luminance, luminance, luminance);
        baseColor.rgb = lerp(grayscale, baseColor.rgb, _CountryColorSaturation);
    }

    return baseColor;
}

#endif // DEFAULT_MAP_MODES_INCLUDED
