// MapFragment.hlsl
// ENGINE: Map pixel composition shared by every map pass (flat and terrain).
//
// Requires (defined before the pass include):
//   float4 RenderMapMode(int mapMode, uint provinceID, float2 uv, float3 positionWS)
//     - GAME map mode dispatcher (or ENGINE DefaultMapModes.hlsl)
//
// Stage order: debug modes -> ocean -> map mode -> lighting -> borders -> highlights -> fog -> overlay

#ifndef MAP_FRAGMENT_INCLUDED
#define MAP_FRAGMENT_INCLUDED

#include "MapModeCommon.hlsl"   // province ID sampling, borders, highlights, fog (guarded; dispatchers usually include it too)
#include "DefaultLighting.hlsl"
#include "DefaultEffects.hlsl"
#include "DefaultDebugModes.hlsl"

float4 ArchonMapFragment(float2 uv, float3 positionWS)
{
    // Debug modes first (100+)
    float4 debugOutput;
    if (TryRenderDebugMode(_MapMode, uv, debugOutput))
    {
        return debugOutput;
    }

    uint provinceID = SampleProvinceID(uv);

    if (provinceID == 0)
    {
        return _OceanColor;
    }

    // Base color (GAME visual policy)
    float4 baseColor = RenderMapMode(_MapMode, provinceID, uv, positionWS);

    // ENGINE effects
    baseColor = ApplyNormalMapLighting(baseColor, uv);
    baseColor = ApplyBorders(baseColor, uv);
    baseColor = ApplyHighlights(baseColor, uv);
    baseColor = ApplyFogOfWar(baseColor, uv);
    baseColor = ApplyOverlayTexture(baseColor, uv);

    return baseColor;
}

#endif // MAP_FRAGMENT_INCLUDED
