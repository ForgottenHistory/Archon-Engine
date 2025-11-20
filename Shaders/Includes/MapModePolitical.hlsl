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

    // ============================================================================
    // BILINEAR BLENDING (Imperator Rome technique)
    // Samples 4 neighboring owner IDs and blends colors for smooth boundaries
    // ============================================================================

    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Manual bilinear sampling using texture-space offsets
    float2 texSize = float2(5632.0, 2048.0); // TODO: Make this a shader parameter
    float2 texelSize = 1.0 / texSize;

    float2 pixelPos = correctedUV * texSize;
    float2 pixelPosFloor = floor(pixelPos);
    float2 fractional = pixelPos - pixelPosFloor;

    // Sample 4 neighboring pixels
    float2 uv00 = (pixelPosFloor + float2(0.5, 0.5)) * texelSize;
    float2 uv10 = (pixelPosFloor + float2(1.5, 0.5)) * texelSize;
    float2 uv01 = (pixelPosFloor + float2(0.5, 1.5)) * texelSize;
    float2 uv11 = (pixelPosFloor + float2(1.5, 1.5)) * texelSize;

    // Sample owner IDs (R32_SFloat format - stores raw owner ID as float)
    float ownerData00 = SAMPLE_TEXTURE2D(_ProvinceOwnerTexture, sampler_ProvinceOwnerTexture, uv00).r;
    float ownerData10 = SAMPLE_TEXTURE2D(_ProvinceOwnerTexture, sampler_ProvinceOwnerTexture, uv10).r;
    float ownerData01 = SAMPLE_TEXTURE2D(_ProvinceOwnerTexture, sampler_ProvinceOwnerTexture, uv01).r;
    float ownerData11 = SAMPLE_TEXTURE2D(_ProvinceOwnerTexture, sampler_ProvinceOwnerTexture, uv11).r;

    // Decode owner IDs (R32_SFloat stores raw values - just cast, NO multiplication)
    // OwnerTextureDispatcher.cs line 239: uint decodedValue = (uint)(ownerRawFloat + 0.5f);
    uint owner00 = (uint)(ownerData00 + 0.5);
    uint owner10 = (uint)(ownerData10 + 0.5);
    uint owner01 = (uint)(ownerData01 + 0.5);
    uint owner11 = (uint)(ownerData11 + 0.5);

    // Handle unowned provinces
    if (owner00 == 0)
    {
        return RenderTerrain(provinceID, uv);
    }

    // Fetch country colors for each owner from palette
    float4 color00 = SAMPLE_TEXTURE2D(_CountryColorPalette, sampler_CountryColorPalette, GetColorUV(owner00));
    float4 color10 = SAMPLE_TEXTURE2D(_CountryColorPalette, sampler_CountryColorPalette, GetColorUV(owner10));
    float4 color01 = SAMPLE_TEXTURE2D(_CountryColorPalette, sampler_CountryColorPalette, GetColorUV(owner01));
    float4 color11 = SAMPLE_TEXTURE2D(_CountryColorPalette, sampler_CountryColorPalette, GetColorUV(owner11));

    // Apply HSV
    color00.rgb = ApplyHSVColorGrading(color00.rgb);
    color10.rgb = ApplyHSVColorGrading(color10.rgb);
    color01.rgb = ApplyHSVColorGrading(color01.rgb);
    color11.rgb = ApplyHSVColorGrading(color11.rgb);

    // Bilinear blend
    float4 colorTop = lerp(color00, color10, fractional.x);
    float4 colorBottom = lerp(color01, color11, fractional.x);
    float4 finalColor = lerp(colorTop, colorBottom, fractional.y);

    return finalColor;
}

#endif // MAPMODE_POLITICAL_INCLUDED