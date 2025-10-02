#ifndef MAPMODE_POLITICAL_INCLUDED
#define MAPMODE_POLITICAL_INCLUDED

// ============================================================================
// Political Map Mode
// Renders provinces colored by their owning country
// Uses owner ID to sample from country color palette
// ============================================================================

float4 RenderPolitical(uint provinceID, float2 uv)
{
    // Handle ocean/invalid provinces
    if (provinceID == 0)
    {
        return OCEAN_COLOR;
    }

    // GPU Palette System: ProvinceOwnerTexture → owner ID → CountryColorPalette → color
    // This matches the C# architecture where PoliticalMapMode updates CountryColorPalette
    uint ownerID = SampleOwnerID(uv);

    // Handle unowned provinces - show terrain color
    if (ownerID == 0)
    {
        return SampleTerrainColorDirect(uv); // Show terrain for unowned provinces
    }

    // DEBUG: Visualize owner ID to verify encoding/decoding
    // Uncomment this to see owner IDs as colors (for debugging)
    // return float4(ownerID / 1000.0, 0, 0, 1.0); // Red intensity = owner ID

    // Lookup country color in palette using owner ID
    // CountryColorPalette is dynamically sized based on max country ID (979+ countries)
    float2 paletteUV = GetColorUV(ownerID);
    float4 countryColor = SAMPLE_TEXTURE2D(_CountryColorPalette, sampler_CountryColorPalette, paletteUV);

    // DEBUG: Show raw palette color to verify palette is populated
    // Uncomment to check if palette has correct colors
    // return float4(1, 0, 1, 1); // Magenta = shader is running

    return countryColor;
}

#endif // MAPMODE_POLITICAL_INCLUDED