#ifndef MAPMODE_POLITICAL_INCLUDED
#define MAPMODE_POLITICAL_INCLUDED

// ============================================================================
// Political Map Mode
// Renders provinces colored by their owning country
// Uses owner ID to sample from country color palette
// Includes HSV color grading for vibrant, distinct colors (Imperator Rome style)
// ============================================================================

// ============================================================================
// HSV Color Space Conversion (Imperator Rome technique)
// Makes province colors more vibrant and visually distinct
// Based on Imperator pixel.txt:471-507
// ============================================================================

float3 RGBtoHSV(float3 rgb)
{
    float maxComponent = max(max(rgb.r, rgb.g), rgb.b);
    float minComponent = min(min(rgb.r, rgb.g), rgb.b);
    float chroma = maxComponent - minComponent;

    float hue = 0.0;
    if (chroma > 0.0001)
    {
        if (maxComponent == rgb.r)
        {
            hue = (rgb.g - rgb.b) / chroma;
            if (hue < 0.0) hue += 6.0;
        }
        else if (maxComponent == rgb.g)
        {
            hue = (rgb.b - rgb.r) / chroma + 2.0;
        }
        else // maxComponent == rgb.b
        {
            hue = (rgb.r - rgb.g) / chroma + 4.0;
        }
        hue /= 6.0; // Normalize to [0, 1]
    }

    float saturation = (maxComponent > 0.0001) ? (chroma / maxComponent) : 0.0;
    float value = maxComponent;

    return float3(hue, saturation, value);
}

float3 HSVtoRGB(float3 hsv)
{
    float hue = hsv.x * 6.0; // Scale to [0, 6]
    float saturation = hsv.y;
    float value = hsv.z;

    // Generate color palette from hue (triangle wave function)
    float3 hueOffsets = abs(frac(float3(hue, hue - 2.0/3.0, hue - 1.0/3.0)) * 6.0 - 3.0);
    float3 rgb = saturate(hueOffsets - 1.0);

    // Apply saturation
    rgb = lerp(float3(1.0, 1.0, 1.0), rgb, saturation);

    // Apply value
    rgb *= value;

    return rgb;
}

float3 ApplyHSVColorGrading(float3 color)
{
    // Calculate luminance (Imperator line 517)
    float luminance = saturate(dot(color, float3(0.2125, 0.7154, 0.0721)));
    float blendFactor = 1.0 - luminance;

    // Use the standard RGB→HSV conversion (my original implementation was correct)
    float3 hsv = RGBtoHSV(color);

    float hue = hsv.x;
    float saturation = hsv.y * 0.88; // Imperator multiplies saturation by 0.88
    float value = hsv.z;

    // Generate RGB from hue using Imperator's triangle wave (lines 538-543)
    float3 hueOffsets = abs(hue) + float3(1.0, 0.667, 0.333);
    hueOffsets = frac(hueOffsets);
    hueOffsets = hueOffsets * 6.0 - 3.0;
    hueOffsets = saturate(abs(hueOffsets) - 1.0);
    hueOffsets = hueOffsets - 1.0;
    hueOffsets = saturation * hueOffsets + 1.0;

    // Multiply by value (line 544)
    float3 adjustedColor = value * hueOffsets;

    // Blend with original (line 545)
    return lerp(color, adjustedColor, blendFactor);
}

float4 RenderPolitical(uint provinceID, float2 uv)
{
    // Handle ocean/invalid provinces
    if (provinceID == 0)
    {
        return _OceanColor; // Configurable from GAME layer
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

    // Apply HSV color grading (Imperator Rome technique - exact implementation)
    countryColor.rgb = ApplyHSVColorGrading(countryColor.rgb);

    // DEBUG: Show raw palette color to verify palette is populated
    // Uncomment to check if palette has correct colors
    // return float4(1, 0, 1, 1); // Magenta = shader is running

    return countryColor;
}

#endif // MAPMODE_POLITICAL_INCLUDED