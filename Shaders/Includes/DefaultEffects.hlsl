// DefaultEffects.hlsl
// ENGINE: Post-processing effects (overlay textures, fog of war, highlights)
// Part of Default visual style shader architecture

#ifndef DEFAULT_EFFECTS_INCLUDED
#define DEFAULT_EFFECTS_INCLUDED

// NOTE: ApplyHighlights() and ApplyFogOfWar() are defined in MapModeCommon.hlsl
// We rely on DefaultMapModes.hlsl including MapModeCommon.hlsl first

// Apply overlay texture (parchment, paper effects)
float4 ApplyOverlayTexture(float4 baseColor, float2 uv)
{
    float2 overlayUV = TRANSFORM_TEX(uv, _OverlayTexture);
    float4 overlayColor = SAMPLE_TEXTURE2D(_OverlayTexture, sampler_OverlayTexture, overlayUV);

    // Multiplicative blend with strength control
    float3 overlayBlend = lerp(float3(1, 1, 1), overlayColor.rgb, _OverlayStrength);
    baseColor.rgb *= overlayBlend;

    return baseColor;
}

#endif // DEFAULT_EFFECTS_INCLUDED
