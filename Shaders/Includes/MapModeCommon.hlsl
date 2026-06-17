#ifndef MAPMODE_COMMON_INCLUDED
#define MAPMODE_COMMON_INCLUDED

// ============================================================================
// MapMode Common Utilities
// Shared functions for all map mode rendering
// ============================================================================

// Province ID encoding/decoding functions
uint DecodeProvinceID(float2 encoded)
{
    // Convert from float [0,1] back to uint16 values
    uint r = (uint)(encoded.r * 255.0 + 0.5);
    uint g = (uint)(encoded.g * 255.0 + 0.5);

    // Reconstruct 16-bit province ID from RG channels
    return (g << 8) | r;
}

float2 EncodeProvinceID(uint provinceID)
{
    // Split 16-bit ID into two 8-bit values
    uint r = provinceID & 0xFF;
    uint g = (provinceID >> 8) & 0xFF;

    // Convert to float [0,1] range
    return float2(r / 255.0, g / 255.0);
}

// Get owner UV coordinate for province ID lookup
float2 GetOwnerUV(uint provinceID, float2 baseUV)
{
    return baseUV;
}

// Get color palette UV coordinate for owner ID
float2 GetColorUV(uint ownerID)
{
    // Map owner ID to palette texture coordinate
    // Palette is 4096x1, so X = (ownerID + 0.5)/4096 to hit pixel center, Y = 0.5
    return float2((ownerID + 0.5) / 4096.0, 0.5);
}

// Sample province ID with corrected UV coordinates
uint SampleProvinceID(float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float2 provinceID_encoded = SAMPLE_TEXTURE2D(_ProvinceIDTexture, sampler_ProvinceIDTexture, correctedUV).rg;
    return DecodeProvinceID(provinceID_encoded);
}

// Sample owner ID for a given province
uint SampleOwnerID(float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float ownerData = SAMPLE_TEXTURE2D(_ProvinceOwnerTexture, sampler_ProvinceOwnerTexture, correctedUV).r;
    uint ownerID = (uint)(ownerData + 0.5);
    return ownerID;
}

// DEBUG: Visualize owner texture as grayscale
float4 VisualizeOwnerTexture(float2 uv)
{
    uint ownerID = SampleOwnerID(uv);
    float gray = ownerID / 4096.0;
    return float4(gray, gray, gray, 1.0);
}

// ============================================================================
// BORDER RENDERING - Clean Mode Separation
// ============================================================================
// Mode 0: None - No borders rendered
// Mode 1: ShaderDistanceField - JFA distance field, smooth anti-aliased borders
// Mode 2: ShaderPixelPerfect - Sharp 1-pixel borders, retro aesthetic
// Mode 3: MeshGeometry - CPU mesh rendering (no shader borders)
//
// Each mode has its own dedicated texture and function:
// - _DistanceFieldBorderTexture: Mode 1 (Bilinear, R=country dist, G=province dist)
// - _PixelPerfectBorderTexture: Mode 2 (Point, R=country, G=province)
// ============================================================================

// ============================================================================
// MODE 1: Distance Field Borders (Smooth Anti-Aliased)
// ============================================================================
// Uses JFA distance field for smooth curved borders
// Texture: _DistanceFieldBorderTexture (R=country distance, G=province distance)
// Filter: Bilinear for smooth gradients
// ============================================================================

/// <summary>
/// Apply smooth anti-aliased borders using distance field (Mode 1)
/// Self-contained - only uses _DistanceFieldBorderTexture
/// </summary>
float4 ApplyDistanceFieldBorders(float4 baseColor, float2 correctedUV)
{
    // Sample distance field texture (R=country distance, G=province distance)
    // Values are normalized [0,1] representing distance in pixels / maxDistance
    float2 normalizedDistances = SAMPLE_TEXTURE2D(_DistanceFieldBorderTexture, sampler_DistanceFieldBorderTexture, correctedUV).rg;

    // Convert normalized [0, 1] back to pixel distances
    // Max distance is 32 pixels (set in compute shader)
    float maxDistance = 32.0;
    float countryDist = normalizedDistances.r * maxDistance;
    float provinceDist = normalizedDistances.g * maxDistance;

    // EU5-style smooth borders using distance field
    // Core width 0.0-0.3px, AA falloff 0.3-1.0px = sharp but smooth

    // Province borders (same owner) - subtle gray lines
    float provinceAlpha = 1.0 - smoothstep(0.3, 1.0, provinceDist);
    provinceAlpha *= _ProvinceBorderStrength;
    baseColor.rgb = lerp(baseColor.rgb, _ProvinceBorderColor.rgb, provinceAlpha);

    // Country borders - same smoothstep for consistency
    float countryAlpha = 1.0 - smoothstep(0.3, 1.0, countryDist);
    countryAlpha *= _CountryBorderStrength;
    baseColor.rgb = lerp(baseColor.rgb, _CountryBorderColor.rgb, countryAlpha);

    return baseColor;
}

// ============================================================================
// MODE 2: Pixel Perfect Borders (Sharp 1px)
// ============================================================================
// Uses compute shader generated border mask for sharp retro borders
// Texture: _PixelPerfectBorderTexture (R=country border, G=province border)
// Filter: Point for sharp edges
// ============================================================================

/// <summary>
/// Apply sharp 1-pixel borders using pixel-perfect mask (Mode 2)
/// Self-contained - only uses _PixelPerfectBorderTexture
/// </summary>
float4 ApplyPixelPerfectBorders(float4 baseColor, float2 correctedUV)
{
    // Sample pixel-perfect border texture (R=country, G=province)
    // Values: 0.0 = no border, 1.0 = border present
    float2 borderMask = SAMPLE_TEXTURE2D(_PixelPerfectBorderTexture, sampler_PixelPerfectBorderTexture, correctedUV).rg;

    float countryBorder = borderMask.r;
    float provinceBorder = borderMask.g;

    // Country borders take priority (rendered on top)
    if (countryBorder > 0.5 && _CountryBorderStrength > 0.01)
    {
        baseColor.rgb = _CountryBorderColor.rgb;
    }
    else if (provinceBorder > 0.5 && _ProvinceBorderStrength > 0.01)
    {
        baseColor.rgb = _ProvinceBorderColor.rgb;
    }

    return baseColor;
}

// ============================================================================
// Main Border Dispatch Function
// ============================================================================

/// <summary>
/// Apply borders based on current rendering mode
/// Dispatches to mode-specific functions for clean separation
///
/// _BorderRenderingMode values:
///   0 = None (skip borders)
///   1 = ShaderDistanceField (smooth JFA)
///   2 = ShaderPixelPerfect (sharp 1px)
///   3 = MeshGeometry (handled by CPU mesh renderer, skip here)
/// </summary>
float4 ApplyBorders(float4 baseColor, float2 uv)
{
    // Mode 0 (None) or Mode 3 (MeshGeometry): No shader borders
    if (_BorderRenderingMode == 0 || _BorderRenderingMode == 3)
    {
        return baseColor;
    }

    // Early return when border strength is 0
    if (_CountryBorderStrength < 0.01 && _ProvinceBorderStrength < 0.01)
    {
        return baseColor;
    }

    // Convert UV to texture space (Y-flip for RenderTexture)
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Dispatch to mode-specific function
    if (_BorderRenderingMode == 1) // ShaderDistanceField
    {
        return ApplyDistanceFieldBorders(baseColor, correctedUV);
    }
    else if (_BorderRenderingMode == 2) // ShaderPixelPerfect
    {
        return ApplyPixelPerfectBorders(baseColor, correctedUV);
    }

    return baseColor;
}

// ============================================================================
// Highlight Rendering
// ============================================================================

float4 ApplyHighlights(float4 baseColor, float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float4 highlight = SAMPLE_TEXTURE2D(_HighlightTexture, sampler_HighlightTexture, correctedUV);
    baseColor.rgb = lerp(baseColor.rgb, highlight.rgb, highlight.a * _HighlightStrength);
    return baseColor;
}

// ============================================================================
// Fog of War Rendering
// ============================================================================

float ValueNoise(float2 uv)
{
    float2 i = floor(uv);
    float2 f = frac(uv);
    f = f * f * (3.0 - 2.0 * f);

    float a = frac(sin(dot(i, float2(12.9898, 78.233))) * 43758.5453);
    float b = frac(sin(dot(i + float2(1.0, 0.0), float2(12.9898, 78.233))) * 43758.5453);
    float c = frac(sin(dot(i + float2(0.0, 1.0), float2(12.9898, 78.233))) * 43758.5453);
    float d = frac(sin(dot(i + float2(1.0, 1.0), float2(12.9898, 78.233))) * 43758.5453);

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float4 ApplyFogOfWar(float4 baseColor, float2 uv)
{
    if (_FogOfWarEnabled < 0.5)
        return baseColor;

    float zoomFade = saturate(_FogOfWarZoomDisable);
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float visibility = SAMPLE_TEXTURE2D(_FogOfWarTexture, sampler_FogOfWarTexture, correctedUV).r;

    float2 timeOffset = float2(_Time.y * _FogNoiseSpeed * 0.1, _Time.y * _FogNoiseSpeed * 0.05);
    float noise = ValueNoise(uv * _FogNoiseScale + timeOffset);

    float4 fogColor = baseColor;

    // Unexplored: visibility < 0.25
    if (visibility < 0.25)
    {
        float blendStrength = _FogUnexploredColor.a;
        blendStrength = saturate(blendStrength + (noise - 0.5) * _FogNoiseStrength);
        fogColor.rgb = lerp(baseColor.rgb, _FogUnexploredColor.rgb, blendStrength);
    }
    // Explored but not visible: visibility 0.25-0.75
    else if (visibility < 0.75)
    {
        float luminance = dot(baseColor.rgb, float3(0.299, 0.587, 0.114));
        float3 grayscale = float3(luminance, luminance, luminance);
        float3 desaturated = lerp(baseColor.rgb, grayscale, _FogExploredDesaturation);
        fogColor.rgb = desaturated * _FogExploredColor.rgb;
        float fogCloudStrength = noise * _FogNoiseStrength;
        fogColor.rgb = lerp(fogColor.rgb, _FogNoiseColor.rgb, fogCloudStrength);
    }

    return lerp(fogColor, baseColor, zoomFade);
}

// ============================================================================
// Terrain Utilities
// ============================================================================

uint SampleTerrainType(float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float4 terrainColor = SAMPLE_TEXTURE2D(_ProvinceTerrainTexture, sampler_ProvinceTerrainTexture, correctedUV);

    if (terrainColor.b > 0.6 && terrainColor.b > terrainColor.r && terrainColor.b > terrainColor.g)
        return 0; // Water
    else
        return 1; // Land
}

float4 SampleTerrainColor(uint terrainType)
{
    float2 terrainUV = float2((terrainType + 0.5) / 32.0, 0.5);
    return SAMPLE_TEXTURE2D(_TerrainColorPalette, sampler_TerrainColorPalette, terrainUV);
}

float4 SampleTerrainColorDirect(float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    return SAMPLE_TEXTURE2D(_ProvinceTerrainTexture, sampler_ProvinceTerrainTexture, correctedUV);
}

#endif // MAPMODE_COMMON_INCLUDED
