// BorderMeshCore.hlsl
// ENGINE: the mesh line pass (borders, rivers, map line layers) shared by Archon/BorderMesh and by custom line
// shaders a visual style names per line class (`shader: "..."`). A custom shader copies BorderMesh.shader's
// Properties and pass state, includes this file, uses ArchonBorderMeshVert as its vertex function and writes its own
// fragment function from ArchonBorderMeshColor (strip colour and shape) and ArchonBorderMeshOutput (night, fade,
// blend). Varyings.mapUV is the map UV (v = 1 north) for sampling map-space data.

#ifndef ARCHON_BORDER_MESH_CORE_INCLUDED
#define ARCHON_BORDER_MESH_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "DayNight.hlsl"

TEXTURE2D(_BorderTex);
SAMPLER(sampler_BorderTex);
TEXTURE2D(_HeightmapTexture);
SAMPLER(sampler_HeightmapTexture);

CBUFFER_START(UnityPerMaterial)
    float4 _BorderTex_ST;
    float4 _HeightmapTexture_ST;
    float4 _BorderColor;
    float4 _BorderFade;
    float _BorderUseTexture;
    float _BorderTexAlphaOnly;
    float _BorderSoftness;
    float _BorderPremultiply;
    float _BorderColorSource;
    float _BorderWidth;
    float _BorderWidthPerLevel;
    float _BorderMinScreenWidth;
    float _BorderTile;
    float _HeightScale;
    float4 _MapWorldBounds;
CBUFFER_END

// Engine border globals (from the visual style asset)
float4 _CountryBorderColor;
float4 _ProvinceBorderColor;
float _CountryBorderStrength;
float _ProvinceBorderStrength;

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;     // world offset of one map pixel towards this side
    float3 uv : TEXCOORD0;        // x = distance along (map px), y = 0..1 across, z = width level
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float fade : TEXCOORD1;
    float2 mapUV : TEXCOORD2;
};

float TerrainHeight(float3 posWS)
{
    float2 terrainUV = (posWS.xz - _MapWorldBounds.xy) / _MapWorldBounds.zw;
    float2 heightUV = TRANSFORM_TEX(float2(terrainUV.x, 1.0 - terrainUV.y), _HeightmapTexture);
    float h = SAMPLE_TEXTURE2D_LOD(_HeightmapTexture, sampler_HeightmapTexture, heightUV, 0).r;
    return (h - 0.5) * _HeightScale;
}

Varyings ArchonBorderMeshVert(Attributes input)
{
    Varyings output;

    float3 center = input.positionOS.xyz;
    center.y = TerrainHeight(center);

    // World size of one screen pixel at this depth (perspective and orthographic)
    float4 centerCS = TransformWorldToHClip(center);
    // Clamp w: vertices behind or level with the camera have w <= 0, which would make
    // the screen-pixel floor blow the strip up to enormous (screen-covering) triangles
    float worldPerScreenPx = 2.0 * max(centerCS.w, 1e-3) / (abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);

    // World length of one map pixel along the offset direction
    float worldPerMapPx = max(length(input.normalOS.xz), 1e-6);
    float screenPxPerMapPx = worldPerMapPx / worldPerScreenPx;

    // Width in map pixels: class width plus per-level width (rivers taper). The screen-pixel
    // floor scales with it, so relative widths survive zooming out.
    float width = _BorderWidth + _BorderWidthPerLevel * input.uv.z;
    float floorScale = clamp(_BorderMinScreenWidth / max(_BorderWidth * screenPxPerMapPx, 1e-5), 1.0, 64.0);
    float halfWidth = 0.5 * width * floorScale;

    float3 posWS = center;
    // Side from V: left edge -1, center line 0, right edge +1. The normal is always the
    // one-pixel offset towards the right; center vertices keep it (for the width floor
    // and fade above) but do not move.
    posWS.xz += input.normalOS.xz * halfWidth * (input.uv.y * 2.0 - 1.0);
    posWS.y = TerrainHeight(posWS);

    output.positionCS = TransformWorldToHClip(posWS);
    output.uv = input.uv.xy;
    output.mapUV = ArchonWorldToMapUV(posWS, _MapWorldBounds);
    output.fade = (_BorderFade.y > _BorderFade.x)
        ? smoothstep(_BorderFade.x, _BorderFade.y, screenPxPerMapPx)
        : 1.0;
    return output;
}

// Base colour and shape of the strip: class texture or solid anti-aliased strip, coloured by the class colour
// or the engine border colour globals (strength in color.a).
void ArchonBorderMeshColor(Varyings input, out float4 color, out float alpha)
{
    float strength;
    if (_BorderColorSource > 1.5)      { color = _ProvinceBorderColor; strength = _ProvinceBorderStrength; }
    else if (_BorderColorSource > 0.5) { color = _CountryBorderColor;  strength = _CountryBorderStrength; }
    else                               { color = _BorderColor;         strength = 1.0; }
    color.a *= strength;

    float v = input.uv.y;
    if (_BorderUseTexture > 0.5)
    {
        // Paradox layout: texture U runs along the border, V across it
        float4 tex = SAMPLE_TEXTURE2D(_BorderTex, sampler_BorderTex, float2(input.uv.x * _BorderTile, v));
        if (_BorderTexAlphaOnly < 0.5)
            color.rgb *= tex.rgb;
        alpha = tex.a;
    }
    else
    {
        // Solid strip with analytic anti-aliased edges (junction cap centers have v = 0.5)
        float edge = 0.5 - abs(v - 0.5);
        float crisp = saturate(edge / max(fwidth(v), 1e-5));
        // Soft: quadratic falloff from the center line to the strip edge (glow)
        float soft = saturate(edge * 2.0);
        alpha = lerp(crisp, soft * soft, saturate(_BorderSoftness));
    }
}

// Final output: night, zoom fade and blend mode
half4 ArchonBorderMeshOutput(Varyings input, float4 color, float alpha)
{
    color.rgb = ArchonApplyNight(color.rgb, input.mapUV, 1.0);
    float outAlpha = color.a * alpha * input.fade;
    // Lighten (Max blend ignores blend factors): fold the alpha into the color
    if (_BorderPremultiply > 0.5)
        return half4(color.rgb * outAlpha, outAlpha);
    return half4(color.rgb, outAlpha);
}

half4 ArchonBorderMeshFrag(Varyings input) : SV_Target
{
    float4 color;
    float alpha;
    ArchonBorderMeshColor(input, color, alpha);
    return ArchonBorderMeshOutput(input, color, alpha);
}

#endif // ARCHON_BORDER_MESH_CORE_INCLUDED
