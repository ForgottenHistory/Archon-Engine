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
    // For now, use direct UV mapping - this could be optimized
    // In a real implementation, this might index into a compact owner lookup table
    return baseUV;
}

// Get color palette UV coordinate for owner ID
float2 GetColorUV(uint ownerID)
{
    // Map owner ID to palette texture coordinate
    // Palette is 256x1, so X = ownerID/256, Y = 0.5
    return float2(ownerID / 256.0, 0.5);
}

// Sample province ID with corrected UV coordinates
uint SampleProvinceID(float2 uv)
{
    // Fix flipped UV coordinates - flip only Y
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Sample province ID texture with point filtering
    float2 provinceID_encoded = SAMPLE_TEXTURE2D(_ProvinceIDTexture, sampler_ProvinceIDTexture, correctedUV).rg;
    return DecodeProvinceID(provinceID_encoded);
}

// Sample owner ID for a given province
uint SampleOwnerID(float2 uv)
{
    // Fix flipped UV coordinates
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Sample owner texture
    float ownerData = SAMPLE_TEXTURE2D(_ProvinceOwnerTexture, sampler_ProvinceOwnerTexture, correctedUV).r;
    return (uint)(ownerData * 255.0 + 0.5);
}

// Apply borders to base color
float4 ApplyBorders(float4 baseColor, float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float borderStrength = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, correctedUV).r;
    baseColor.rgb = lerp(baseColor.rgb, _BorderColor.rgb, borderStrength * _BorderStrength);
    return baseColor;
}

// Apply highlights to base color
float4 ApplyHighlights(float4 baseColor, float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float4 highlight = SAMPLE_TEXTURE2D(_HighlightTexture, sampler_HighlightTexture, correctedUV);
    baseColor.rgb = lerp(baseColor.rgb, highlight.rgb, highlight.a * _HighlightStrength);
    return baseColor;
}

// Ocean color constant
#define OCEAN_COLOR float4(0.2, 0.4, 0.8, 1.0)

#endif // MAPMODE_COMMON_INCLUDED