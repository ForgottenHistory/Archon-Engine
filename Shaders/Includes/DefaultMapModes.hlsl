// DefaultMapModes.hlsl
// ENGINE: Map mode rendering logic (political, terrain, development, etc.)
// Part of Default visual style shader architecture

#ifndef DEFAULT_MAP_MODES_INCLUDED
#define DEFAULT_MAP_MODES_INCLUDED

// Map mode includes - ENGINE DEFAULT RENDERERS
// CRITICAL: Include MapModeCommon.hlsl FIRST - it defines SampleProvinceID(), SampleOwnerID(), etc.
#include "MapModeCommon.hlsl"
#include "MapModeTerrain.hlsl"
#include "MapModePolitical.hlsl"

// Render base color based on current map mode
// Returns base color before lighting/effects are applied
// ENGINE DEFAULT: Political (0) and Terrain (1) only
// GAME layer can extend with custom map modes by creating shader variants
float4 RenderMapMode(int mapMode, uint provinceID, float2 uv, float3 positionWS)
{
    float4 baseColor;

    if (mapMode == 0) // Political
    {
        baseColor = RenderPolitical(provinceID, uv);
        float luminance = dot(baseColor.rgb, float3(0.299, 0.587, 0.114));
        float3 grayscale = float3(luminance, luminance, luminance);
        baseColor.rgb = lerp(grayscale, baseColor.rgb, _CountryColorSaturation);
    }
    else if (mapMode == 1) // Terrain (terrain.bmp with detail mapping)
    {
        baseColor = RenderTerrain(provinceID, uv, positionWS);
    }
    else if (mapMode == 12) // Province Colors (provinces.bmp debug)
    {
        baseColor = RenderProvinceColors(provinceID, uv);
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
