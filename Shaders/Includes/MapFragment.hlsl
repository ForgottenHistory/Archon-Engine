// MapFragment.hlsl
// ENGINE: Map pixel composition shared by every map pass (flat and terrain).
//
// Requires (defined before the pass include):
//   float4 RenderMapMode(int mapMode, uint provinceID, float2 uv, float3 positionWS)
//     - GAME map mode dispatcher (or ENGINE DefaultMapModes.hlsl)
//
// Stage order: debug modes -> ocean (early out) -> map mode -> lighting -> borders
//              -> highlights -> fog -> overlay -> post composite
//
// ============================================================================
// Stage overrides (compile-time, zero cost)
// ============================================================================
// To replace a stage, #define its macro and provide the function BEFORE the pass include
// (e.g. in your map mode dispatcher). Undefined stages use the ENGINE default below.
//
//   Macro                       Function you provide
//   ARCHON_OVERRIDE_OCEAN       float4 ArchonOcean(ArchonMapContext ctx)
//   ARCHON_OVERRIDE_LIGHTING    float4 ArchonLighting(float4 color, ArchonMapContext ctx)
//   ARCHON_OVERRIDE_BORDERS     float4 ArchonBorders(float4 color, ArchonMapContext ctx)
//   ARCHON_OVERRIDE_HIGHLIGHTS  float4 ArchonHighlights(float4 color, ArchonMapContext ctx)
//   ARCHON_OVERRIDE_FOG         float4 ArchonFog(float4 color, ArchonMapContext ctx)
//   ARCHON_OVERRIDE_OVERLAY     float4 ArchonOverlay(float4 color, ArchonMapContext ctx)
//   ARCHON_POST_COMPOSITE       float4 ArchonPostComposite(float4 color, ArchonMapContext ctx)
//                               (optional extra stage, runs last; no default)
//
// To wrap a default instead of replacing it, include the ENGINE file that defines it
// (MapModeCommon.hlsl, DefaultLighting.hlsl, DefaultEffects.hlsl) in your code and call
// e.g. ApplyFogOfWar(color, ctx.uv) inside your override. All are include-guarded.

#ifndef MAP_FRAGMENT_INCLUDED
#define MAP_FRAGMENT_INCLUDED

#include "MapContext.hlsl"
#include "MapModeCommon.hlsl"   // province ID sampling, borders, highlights, fog (guarded; dispatchers usually include it too)
#include "DefaultLighting.hlsl"
#include "DefaultEffects.hlsl"
#include "DefaultDebugModes.hlsl"

// ===== ENGINE default stages =====

#ifndef ARCHON_OVERRIDE_OCEAN
float4 ArchonOcean(ArchonMapContext ctx) { return _OceanColor; }
#endif

#ifndef ARCHON_OVERRIDE_LIGHTING
float4 ArchonLighting(float4 color, ArchonMapContext ctx) { return ApplyNormalMapLighting(color, ctx.uv); }
#endif

#ifndef ARCHON_OVERRIDE_BORDERS
float4 ArchonBorders(float4 color, ArchonMapContext ctx) { return ApplyBorders(color, ctx.uv); }
#endif

#ifndef ARCHON_OVERRIDE_HIGHLIGHTS
float4 ArchonHighlights(float4 color, ArchonMapContext ctx) { return ApplyHighlights(color, ctx.uv); }
#endif

#ifndef ARCHON_OVERRIDE_FOG
float4 ArchonFog(float4 color, ArchonMapContext ctx) { return ApplyFogOfWar(color, ctx.uv); }
#endif

#ifndef ARCHON_OVERRIDE_OVERLAY
float4 ArchonOverlay(float4 color, ArchonMapContext ctx) { return ApplyOverlayTexture(color, ctx.uv); }
#endif

// ===== Composition =====

float4 ArchonMapFragment(float2 uv, float3 positionWS)
{
    // Debug modes first (100+)
    float4 debugOutput;
    if (TryRenderDebugMode(_MapMode, uv, debugOutput))
    {
        return debugOutput;
    }

    ArchonMapContext ctx;
    ctx.uv = uv;
    ctx.positionWS = positionWS;
    ctx.provinceID = SampleProvinceID(uv);
    ctx.mapMode = _MapMode;

    if (ctx.provinceID == 0)
    {
        return ArchonOcean(ctx);
    }

    // Base color (GAME visual policy)
    float4 color = RenderMapMode(ctx.mapMode, ctx.provinceID, uv, positionWS);

    color = ArchonLighting(color, ctx);
    color = ArchonBorders(color, ctx);
    color = ArchonHighlights(color, ctx);
    color = ArchonFog(color, ctx);
    color = ArchonOverlay(color, ctx);

#ifdef ARCHON_POST_COMPOSITE
    color = ArchonPostComposite(color, ctx);
#endif

    return color;
}

#endif // MAP_FRAGMENT_INCLUDED
