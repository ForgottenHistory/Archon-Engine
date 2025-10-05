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
    // Palette is 1024x1, so X = (ownerID + 0.5)/1024 to hit pixel center, Y = 0.5
    // The +0.5 offset ensures we sample the pixel center, not the boundary
    return float2((ownerID + 0.5) / 1024.0, 0.5);
}

// Sample province ID with corrected UV coordinates
uint SampleProvinceID(float2 uv)
{
    // Fragment shader UVs: (0,0)=bottom-left, (1,1)=top-right
    // RenderTexture: (0,0)=top-left
    // Need Y-flip to convert UV space to texture space
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Sample province ID texture with point filtering
    float2 provinceID_encoded = SAMPLE_TEXTURE2D(_ProvinceIDTexture, sampler_ProvinceIDTexture, correctedUV).rg;
    return DecodeProvinceID(provinceID_encoded);
}

// Sample owner ID for a given province
uint SampleOwnerID(float2 uv)
{
    // Fragment shader UVs: (0,0)=bottom-left, (1,1)=top-right
    // RenderTexture: (0,0)=top-left
    // Need Y-flip to convert UV space to texture space
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Sample owner texture - R16 format stores 16-bit uint directly
    // Read as float (normalized 0.0-1.0), then convert to uint16 (0-65535)
    float ownerData = SAMPLE_TEXTURE2D(_ProvinceOwnerTexture, sampler_ProvinceOwnerTexture, correctedUV).r;

    // Convert normalized float to uint16
    // R16 format: 0.0-1.0 maps to 0-65535
    uint ownerID = (uint)(ownerData * 65535.0 + 0.5);

    return ownerID;
}

// Apply borders to base color
// BorderTexture format: R=country borders, G=province borders
// Colors and strengths are configurable from GAME layer
float4 ApplyBorders(float4 baseColor, float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float2 borders = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, correctedUV).rg;

    float countryBorder = borders.r;   // Country borders (between different owners)
    float provinceBorder = borders.g;  // Province borders (same owner)

    // Apply province borders first (configurable from GAME layer)
    float provinceBorderStrength = provinceBorder * _ProvinceBorderStrength;
    baseColor.rgb = lerp(baseColor.rgb, _ProvinceBorderColor.rgb, provinceBorderStrength);

    // Apply country borders on top (configurable from GAME layer)
    float countryBorderStrength = countryBorder * _CountryBorderStrength;
    baseColor.rgb = lerp(baseColor.rgb, _CountryBorderColor.rgb, countryBorderStrength);

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

// Sample terrain type for a given province
// NOTE: Since _ProvinceTerrainTexture now contains colors, we determine type from color
uint SampleTerrainType(float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Sample terrain color and determine type from it
    float4 terrainColor = SAMPLE_TEXTURE2D(_ProvinceTerrainTexture, sampler_ProvinceTerrainTexture, correctedUV);

    // Simple heuristic: if color is very blue, it's water (type 0), else land (type 1)
    // This is a quick fix - could be improved with better color classification
    if (terrainColor.b > 0.6 && terrainColor.b > terrainColor.r && terrainColor.b > terrainColor.g)
    {
        return 0; // Water
    }
    else
    {
        return 1; // Land
    }
}

// Sample terrain color from terrain palette
float4 SampleTerrainColor(uint terrainType)
{
    // Map terrain type to palette texture coordinate
    // Terrain palette is 32x1, so X = (terrainType + 0.5)/32 to hit pixel center, Y = 0.5
    // The +0.5 offset ensures we sample the pixel center, not the boundary
    float2 terrainUV = float2((terrainType + 0.5) / 32.0, 0.5);
    return SAMPLE_TEXTURE2D(_TerrainColorPalette, sampler_TerrainColorPalette, terrainUV);
}

// Sample terrain color directly from terrain.bmp texture at given UV
float4 SampleTerrainColorDirect(float2 uv)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Sample terrain color directly from terrain bitmap texture
    return SAMPLE_TEXTURE2D(_ProvinceTerrainTexture, sampler_ProvinceTerrainTexture, correctedUV);
}

// Ocean color (configurable from GAME layer via _OceanColor parameter)
// Unowned land color (configurable from GAME layer via _UnownedLandColor parameter)
// These are accessed directly from the shader parameters, not as defines

#endif // MAPMODE_COMMON_INCLUDED