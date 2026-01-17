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

// Render custom GAME map mode from texture array
// Each GAME map mode pre-computes its visualization into a texture slice
// This just samples that texture - instant switching between modes
float4 RenderCustomMapMode(uint provinceID, float2 uv)
{
    // Y-flip UV for RenderTexture sampling
    // Fragment UVs: (0,0)=bottom-left, RenderTexture storage: (0,0)=top-left
    // See: unity-compute-shader-coordination.md - "Y-flip ONLY in Fragment Shaders"
    float2 flippedUV = float2(uv.x, 1.0 - uv.y);

    // Sample from the texture array at the current custom map mode index
    // The texture contains RGBA color per pixel, already computed by GAME
    float4 color = SAMPLE_TEXTURE2D_ARRAY(_MapModeTextureArray, sampler_MapModeTextureArray, flippedUV, _CustomMapModeIndex);

    // Alpha of 0 means "use default" (ocean, unowned, etc.)
    // This allows GAME to skip provinces and let ENGINE handle them
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
