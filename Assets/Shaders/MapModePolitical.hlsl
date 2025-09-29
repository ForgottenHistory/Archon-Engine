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

    // Sample owner ID for this province
    uint ownerID = SampleOwnerID(uv);

    // Handle unowned provinces
    if (ownerID == 0)
    {
        // Unowned province - use neutral gray
        return float4(0.7, 0.7, 0.7, 1.0);
    }

    // Sample country color from palette
    float2 colorUV = GetColorUV(ownerID);
    float4 countryColor = SAMPLE_TEXTURE2D(_ProvinceColorPalette, sampler_ProvinceColorPalette, colorUV);

    // Ensure we have a valid color (not black/transparent)
    if (countryColor.r == 0.0 && countryColor.g == 0.0 && countryColor.b == 0.0)
    {
        // Fallback to a generated color based on owner ID
        float hue = (ownerID * 137.508) % 360.0; // Golden angle for good distribution
        float saturation = 0.7;
        float value = 0.8;

        // Simple HSV to RGB conversion
        float c = value * saturation;
        float x = c * (1.0 - abs(fmod(hue / 60.0, 2.0) - 1.0));
        float m = value - c;

        float3 rgb;
        if (hue < 60.0) rgb = float3(c, x, 0);
        else if (hue < 120.0) rgb = float3(x, c, 0);
        else if (hue < 180.0) rgb = float3(0, c, x);
        else if (hue < 240.0) rgb = float3(0, x, c);
        else if (hue < 300.0) rgb = float3(x, 0, c);
        else rgb = float3(c, 0, x);

        countryColor = float4(rgb + m, 1.0);
    }

    return countryColor;
}

#endif // MAPMODE_POLITICAL_INCLUDED