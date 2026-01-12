// Compositing.hlsl
// ENGINE: Modular layer compositing utilities
// Supports pluggable IShaderCompositor pattern from C#

#ifndef COMPOSITING_INCLUDED
#define COMPOSITING_INCLUDED

// ============================================================================
// Blend Mode Constants (match C# BlendMode enum)
// ============================================================================
#define BLEND_NORMAL    0
#define BLEND_MULTIPLY  1
#define BLEND_SCREEN    2
#define BLEND_OVERLAY   3
#define BLEND_ADDITIVE  4
#define BLEND_SOFTLIGHT 5

// ============================================================================
// Layer Visibility Parameters (set from IShaderCompositor via material)
// ============================================================================
// These are set in DefaultCommon.hlsl CBUFFER if not already defined
#ifndef COMPOSITING_PARAMS_DEFINED
    float _EnableBaseColor;
    float _EnableLighting;
    float _EnableBorders;
    float _EnableHighlights;
    float _EnableFogOfWar;
    float _EnableOverlay;

    int _BorderBlendMode;
    int _HighlightBlendMode;
    int _FogBlendMode;
    int _OverlayBlendMode;
#endif

// ============================================================================
// Blend Mode Functions
// ============================================================================

// Normal blend (standard alpha lerp)
float3 BlendNormal(float3 base, float3 blend, float opacity)
{
    return lerp(base, blend, opacity);
}

// Multiply blend (darkens)
float3 BlendMultiply(float3 base, float3 blend, float opacity)
{
    float3 result = base * blend;
    return lerp(base, result, opacity);
}

// Screen blend (lightens)
float3 BlendScreen(float3 base, float3 blend, float opacity)
{
    float3 result = 1.0 - (1.0 - base) * (1.0 - blend);
    return lerp(base, result, opacity);
}

// Overlay blend (contrast)
float3 BlendOverlay(float3 base, float3 blend, float opacity)
{
    float3 result;
    result.r = base.r < 0.5 ? (2.0 * base.r * blend.r) : (1.0 - 2.0 * (1.0 - base.r) * (1.0 - blend.r));
    result.g = base.g < 0.5 ? (2.0 * base.g * blend.g) : (1.0 - 2.0 * (1.0 - base.g) * (1.0 - blend.g));
    result.b = base.b < 0.5 ? (2.0 * base.b * blend.b) : (1.0 - 2.0 * (1.0 - base.b) * (1.0 - blend.b));
    return lerp(base, result, opacity);
}

// Additive blend
float3 BlendAdditive(float3 base, float3 blend, float opacity)
{
    float3 result = saturate(base + blend);
    return lerp(base, result, opacity);
}

// Soft light blend
float3 BlendSoftLight(float3 base, float3 blend, float opacity)
{
    float3 result;
    result = blend < 0.5 ?
        (2.0 * base * blend + base * base * (1.0 - 2.0 * blend)) :
        (sqrt(base) * (2.0 * blend - 1.0) + 2.0 * base * (1.0 - blend));
    return lerp(base, result, opacity);
}

// ============================================================================
// Generic Blend Function (selects mode at runtime)
// ============================================================================
float3 ApplyBlend(float3 base, float3 blend, float opacity, int blendMode)
{
    // Early out if no opacity
    if (opacity <= 0.0) return base;

    switch (blendMode)
    {
        case BLEND_NORMAL:    return BlendNormal(base, blend, opacity);
        case BLEND_MULTIPLY:  return BlendMultiply(base, blend, opacity);
        case BLEND_SCREEN:    return BlendScreen(base, blend, opacity);
        case BLEND_OVERLAY:   return BlendOverlay(base, blend, opacity);
        case BLEND_ADDITIVE:  return BlendAdditive(base, blend, opacity);
        case BLEND_SOFTLIGHT: return BlendSoftLight(base, blend, opacity);
        default:              return BlendNormal(base, blend, opacity);
    }
}

// ============================================================================
// Layer Compositing Functions
// These wrap the existing Apply* functions with blend mode support
// ============================================================================

// Apply borders with configurable blend mode
float4 ApplyBordersComposited(float4 baseColor, float2 uv, int blendMode)
{
    // Skip if disabled
    if (_EnableBorders < 0.5) return baseColor;

    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Sample border texture based on rendering mode
    float2 borders;
    if (_BorderRenderingMode == 1) // Distance Field
    {
        borders = SAMPLE_TEXTURE2D(_DistanceFieldBorderTexture, sampler_DistanceFieldBorderTexture, correctedUV).rg;
    }
    else if (_BorderRenderingMode == 2) // Pixel Perfect
    {
        borders = SAMPLE_TEXTURE2D(_PixelPerfectBorderTexture, sampler_PixelPerfectBorderTexture, correctedUV).rg;
    }
    else
    {
        return baseColor; // No borders or mesh geometry (handled separately)
    }

    float countryBorder = borders.r;
    float provinceBorder = borders.g;

    // Apply province borders
    float provinceBorderStrength = provinceBorder * _ProvinceBorderStrength;
    baseColor.rgb = ApplyBlend(baseColor.rgb, _ProvinceBorderColor.rgb, provinceBorderStrength, blendMode);

    // Apply country borders
    float countryBorderStrength = countryBorder * _CountryBorderStrength;
    baseColor.rgb = ApplyBlend(baseColor.rgb, _CountryBorderColor.rgb, countryBorderStrength, blendMode);

    return baseColor;
}

// Apply highlights with configurable blend mode
float4 ApplyHighlightsComposited(float4 baseColor, float2 uv, int blendMode)
{
    // Skip if disabled
    if (_EnableHighlights < 0.5) return baseColor;

    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float4 highlight = SAMPLE_TEXTURE2D(_HighlightTexture, sampler_HighlightTexture, correctedUV);

    float opacity = highlight.a * _HighlightStrength;
    baseColor.rgb = ApplyBlend(baseColor.rgb, highlight.rgb, opacity, blendMode);

    return baseColor;
}

// Apply fog of war with configurable blend mode
float4 ApplyFogOfWarComposited(float4 baseColor, float2 uv, int blendMode)
{
    // Skip if disabled
    if (_EnableFogOfWar < 0.5 || _FogOfWarEnabled < 0.5) return baseColor;
    if (_FogOfWarZoomDisable > 0.5) return baseColor;

    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float fogValue = SAMPLE_TEXTURE2D(_FogOfWarTexture, sampler_FogOfWarTexture, correctedUV).r;

    // fogValue: 0 = unexplored, 0.5 = explored (not visible), 1 = visible
    if (fogValue < 0.25)
    {
        // Unexplored - blend to unexplored color
        baseColor.rgb = ApplyBlend(baseColor.rgb, _FogUnexploredColor.rgb, 1.0, blendMode);
    }
    else if (fogValue < 0.75)
    {
        // Explored but not visible - desaturate and tint
        float luminance = dot(baseColor.rgb, float3(0.299, 0.587, 0.114));
        float3 grayscale = float3(luminance, luminance, luminance);
        float3 desaturated = lerp(baseColor.rgb, grayscale, _FogExploredDesaturation);
        baseColor.rgb = ApplyBlend(baseColor.rgb, desaturated * _FogExploredColor.rgb, 1.0, blendMode);
    }
    // else: Visible - no fog applied

    return baseColor;
}

// Apply overlay texture with configurable blend mode
float4 ApplyOverlayComposited(float4 baseColor, float2 uv, int blendMode)
{
    // Skip if disabled
    if (_EnableOverlay < 0.5 || _OverlayStrength < 0.01) return baseColor;

    float2 overlayUV = TRANSFORM_TEX(uv, _OverlayTexture);
    float4 overlay = SAMPLE_TEXTURE2D(_OverlayTexture, sampler_OverlayTexture, overlayUV);

    baseColor.rgb = ApplyBlend(baseColor.rgb, overlay.rgb, _OverlayStrength, blendMode);

    return baseColor;
}

// ============================================================================
// Master Compositing Function
// Applies all layers in order with their configured blend modes
// ============================================================================
float4 CompositeAllLayers(float4 baseColor, float2 uv)
{
    // Borders
    baseColor = ApplyBordersComposited(baseColor, uv, _BorderBlendMode);

    // Highlights
    baseColor = ApplyHighlightsComposited(baseColor, uv, _HighlightBlendMode);

    // Fog of War
    baseColor = ApplyFogOfWarComposited(baseColor, uv, _FogBlendMode);

    // Overlay
    baseColor = ApplyOverlayComposited(baseColor, uv, _OverlayBlendMode);

    return baseColor;
}

#endif // COMPOSITING_INCLUDED
