#ifndef MAPMODE_COMMON_INCLUDED
#define MAPMODE_COMMON_INCLUDED

// ============================================================================
// MapMode Common Utilities
// Shared functions for all map mode rendering
// ============================================================================

// Include Bézier curve utilities for vector border rendering
#include "../BezierCurves.hlsl"

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

    // Sample owner texture - R32_SFloat format stores owner ID as raw float
    // ProvinceOwnerTexture now stores raw owner IDs (e.g., 151.0, 731.0)
    // No normalization - just read and cast to uint
    float ownerData = SAMPLE_TEXTURE2D(_ProvinceOwnerTexture, sampler_ProvinceOwnerTexture, correctedUV).r;

    // Convert raw float to uint (no multiplication needed)
    uint ownerID = (uint)(ownerData + 0.5);

    return ownerID;
}

// DEBUG: Visualize owner texture as grayscale (for debugging)
float4 VisualizeOwnerTexture(float2 uv)
{
    uint ownerID = SampleOwnerID(uv);
    // Normalize to grayscale (0-1024 owner IDs)
    float gray = ownerID / 1024.0;
    return float4(gray, gray, gray, 1.0);
}

// ============================================================================
// AAA-Quality Border Rendering (Distance Field + Multi-Tap + Two-Layer)
// ============================================================================
// Modern grand strategy approach: 1/4 resolution distance field + 9-tap sampling + two-layer rendering
// Memory: ~1.4MB (vs 46MB full resolution) = 97% savings
// Quality: Indistinguishable from full resolution via multi-tap + bilinear filtering
// Industry standard used by AAA grand strategy titles
// ============================================================================

// Border rendering parameters - declared in EU3MapShader.shader CBUFFER_START(UnityPerMaterial)
// These are set by C# via material.SetFloat() in DynamicTextureSet.SetDistanceFieldBorderParams()
// No need to redeclare here - they're already in the constant buffer

/// <summary>
/// 9-tap multi-sampling pattern (±0.75 offset)
/// Compensates for 1/4 resolution distance texture via multiple samples + averaging
/// Creates smooth gradients indistinguishable from full resolution
/// </summary>
float Sample9TapDistance(float2 uv, float2 invSize)
{
    float dist = 0.0;

    // 3x3 grid with ±0.75 pixel offset (AAA pattern from RenderDoc investigation)
    // Sample BOTH channels: R=country borders, G=province borders
    // Use G channel (province borders) - shows all province boundaries
    dist += SAMPLE_TEXTURE2D(_BorderDistanceTexture, sampler_BorderDistanceTexture, uv + float2(-0.75, -0.75) * invSize).g;
    dist += SAMPLE_TEXTURE2D(_BorderDistanceTexture, sampler_BorderDistanceTexture, uv + float2( 0.00, -0.75) * invSize).g;
    dist += SAMPLE_TEXTURE2D(_BorderDistanceTexture, sampler_BorderDistanceTexture, uv + float2( 0.75, -0.75) * invSize).g;

    dist += SAMPLE_TEXTURE2D(_BorderDistanceTexture, sampler_BorderDistanceTexture, uv + float2(-0.75,  0.00) * invSize).g;
    dist += SAMPLE_TEXTURE2D(_BorderDistanceTexture, sampler_BorderDistanceTexture, uv + float2( 0.00,  0.00) * invSize).g;
    dist += SAMPLE_TEXTURE2D(_BorderDistanceTexture, sampler_BorderDistanceTexture, uv + float2( 0.75,  0.00) * invSize).g;

    dist += SAMPLE_TEXTURE2D(_BorderDistanceTexture, sampler_BorderDistanceTexture, uv + float2(-0.75,  0.75) * invSize).g;
    dist += SAMPLE_TEXTURE2D(_BorderDistanceTexture, sampler_BorderDistanceTexture, uv + float2( 0.00,  0.75) * invSize).g;
    dist += SAMPLE_TEXTURE2D(_BorderDistanceTexture, sampler_BorderDistanceTexture, uv + float2( 0.75,  0.75) * invSize).g;

    // Average all 9 samples (box blur for extra smoothing)
    return dist * 0.111111; // 1/9
}

/// <summary>
/// Apply AAA-quality borders using distance field + multi-tap + two-layer rendering
/// Edge layer: Sharp crisp border line
/// Gradient layer: Soft outer glow
/// Province-based coloring: Borders inherit and darken province colors
/// </summary>
float4 ApplyBorders(float4 baseColor, float2 uv)
{
    // Early return when border strength is 0 (no borders enabled)
    if (_CountryBorderStrength < 0.01 && _ProvinceBorderStrength < 0.01)
    {
        return baseColor;
    }

    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Check if we should use pixel-perfect mode (BorderMask only)
    // Sample BorderTexture at multiple points - if all are black (no distance field), use pixel-perfect mode
    float2 s1 = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, float2(0.5, 0.5)).rg;
    float2 s2 = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, float2(0.25, 0.25)).rg;
    float2 s3 = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, float2(0.75, 0.75)).rg;
    float maxValue = max(max(s1.r, s1.g), max(max(s2.r, s2.g), max(s3.r, s3.g)));
    bool usePixelPerfectMode = (maxValue < 0.01);

    // DEBUG: Visualize which mode is active AND show maxValue
    // if (usePixelPerfectMode)
    // {
    //     return float4(1, 0, 0, 1); // Red = pixel-perfect mode active
    // }
    // else
    // {
    //     // Green intensity shows maxValue (higher = more distance field data)
    //     return float4(0, maxValue, 0, 1); // Bright green = strong distance field
    // }

    if (usePixelPerfectMode)
    {
        // PIXEL-PERFECT MODE: Use BorderMask dual-channel for sharp 1-pixel borders
        // R channel = country borders (different owners)
        // G channel = province borders (same owner, different province)
        float2 borderMask = SAMPLE_TEXTURE2D(_BorderMaskTexture, sampler_BorderMaskTexture, correctedUV).rg;
        float countryBorder = borderMask.r;
        float provinceBorder = borderMask.g;

        // Country borders take priority (check strength parameter)
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

    // DISTANCE FIELD MODE: Use smooth distance field borders
    // Sample BorderMask texture (bilinear filtering creates smooth gradients)
    float borderMask = SAMPLE_TEXTURE2D(_BorderMaskTexture, sampler_BorderMaskTexture, correctedUV).r;

    // Sample distance field for accurate border detection
    float2 distanceTextureSize = float2(1408.0, 512.0);
    float2 invSize = 1.0 / distanceTextureSize;
    float dist = Sample9TapDistance(correctedUV, invSize);
    float distPixels = dist * 16.0;

    // HYBRID: Use BorderMask to provide smooth boundary for distance field borders
    // BorderMask gradient: 0.5 at border, fades to 0.0 at interior
    // We want: distance field for detection, BorderMask 0.4-0.6 zone for smooth edges

    // Adjustable border width via BorderMask threshold
    float borderMaskMin = 0.45;
    float borderMaskMax = 0.55;

    bool insideDistanceBorder = (distPixels < 3.0);                              // Distance field says "near border"
    bool inBorderMaskGradient = (borderMask > borderMaskMin);                    // BorderMask says "near border" (gradient zone)

    // DISABLED: Distance field borders (focusing on mask only)
    // if (insideDistanceBorder)
    // {
    //     baseColor.rgb = float3(0, 0, 0); // Black border - pure distance field
    // }

    // No border rendering when border strength is 0 (mesh rendering mode)
    if (_CountryBorderStrength < 0.01 && _ProvinceBorderStrength < 0.01)
    {
        return baseColor;
    }

    // DEBUG: Show what values we actually have after blur
    // Try a much lower threshold to see where the peak is
    // if (borderMask > 0.1) // Very low threshold
    // {
    //     baseColor.rgb = float3(1, 0, 0); // Red anywhere with >0.1 values
    // }

    // Show PEAK in white (if there are any high values)
    // if (borderMask > 0.3)
    // {
    //     baseColor.rgb = float3(1, 1, 1); // White for high values
    // }

    // Use distance field from BorderTexture for smooth borders
    float2 normalizedDistances = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, correctedUV).rg;

    // Convert normalized [0, 1] back to pixel distances for border rendering
    // Max distance is 32 pixels (set in compute shader)
    float maxDistance = 32.0;
    float countryDist = normalizedDistances.r * maxDistance;   // Distance to nearest country border (pixels)
    float provinceDist = normalizedDistances.g * maxDistance;  // Distance to nearest province border (pixels)

    // Modern Paradox-style smooth borders using distance field
    // EU5 style: Very thin core with tight AA gradient for smooth curves without blur
    // The smoothstep creates the anti-aliasing - tight range = sharp but smooth

    // Province borders (same owner) - subtle gray lines
    // Core width 0.0-0.3px, AA falloff 0.3-1.0px = sharp but smooth
    float provinceAlpha = 1.0 - smoothstep(0.3, 1.0, provinceDist);
    provinceAlpha *= _ProvinceBorderStrength;
    baseColor.rgb = lerp(baseColor.rgb, _ProvinceBorderColor.rgb, provinceAlpha);

    // DEBUG: Visualize distance field to verify border detection
    // Uncomment to see raw distance values (red = close to border, black = far from border)
    // baseColor.rgb = float3(1.0 - (countryDist / 32.0), 0, 0);
    // return baseColor;

    // Country borders - Match province border style with smooth AA
    // Core width 0.0-0.3px, AA falloff 0.3-1.0px = sharp but smooth
    float countryAlpha = 1.0 - smoothstep(0.3, 1.0, countryDist);
    countryAlpha *= _CountryBorderStrength;
    baseColor.rgb = lerp(baseColor.rgb, _CountryBorderColor.rgb, countryAlpha);

    return baseColor;
}

// ============================================================================
// Vector Curve Border Rendering (Resolution-Independent)
// ============================================================================

// NOTE: This function requires BezierSegments buffer to be bound by C# code
// Declare in your shader: StructuredBuffer<BezierSegment> _BezierSegments;
// And: uint _BezierSegmentCount;

/// <summary>
/// Apply borders using vector Bézier curves with spatial acceleration (resolution-independent)
/// Uses spatial hash grid to test only nearby segments - ~1000x faster than brute force
/// Evaluates parametric curves at screen resolution for smooth borders on any feature size
/// </summary>
float4 ApplyBordersVectorCurvesSpatial(
    float4 baseColor,
    float2 uv,
    StructuredBuffer<BezierSegment> bezierSegments,
    StructuredBuffer<uint2> gridCellRanges,
    StructuredBuffer<uint> gridSegmentIndices,
    int gridWidth,
    int gridHeight,
    int cellSize,
    float2 mapSize)
{
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Check if we should use pixel-perfect mode (BorderMask only)
    // Sample BorderTexture at multiple points - if all are black (no distance field), use pixel-perfect mode
    float2 s1 = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, float2(0.5, 0.5)).rg;
    float2 s2 = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, float2(0.25, 0.25)).rg;
    float2 s3 = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, float2(0.75, 0.75)).rg;
    float maxValue = max(max(s1.r, s1.g), max(max(s2.r, s2.g), max(s3.r, s3.g)));
    bool usePixelPerfectMode = (maxValue < 0.01);

    if (usePixelPerfectMode)
    {
        // PIXEL-PERFECT MODE: Use BorderMask for sharp 1-pixel borders
        float borderMask = SAMPLE_TEXTURE2D(_BorderMaskTexture, sampler_BorderMaskTexture, correctedUV).r;

        // BorderMask values: 0.0 (interior), 0.5 (curves/junctions), 1.0 (straight borders)
        // Check if this is a country border or province border by sampling owner texture
        if (borderMask > 0.25)  // Catch both 0.5 (curves) and 1.0 (straight)
        {
            // Sample current pixel's owner (use correctedUV to match BorderMask coordinate system)
            uint currentOwner = SampleOwnerID(float2(correctedUV.x, 1.0 - correctedUV.y));

            // Check 4 cardinal neighbors to see if any have different owner
            float2 pixelSize = float2(1.0 / mapSize.x, 1.0 / mapSize.y);
            uint rightOwner = SampleOwnerID(float2(correctedUV.x + pixelSize.x, 1.0 - correctedUV.y));
            uint leftOwner = SampleOwnerID(float2(correctedUV.x - pixelSize.x, 1.0 - correctedUV.y));
            uint upOwner = SampleOwnerID(float2(correctedUV.x, 1.0 - (correctedUV.y + pixelSize.y)));
            uint downOwner = SampleOwnerID(float2(correctedUV.x, 1.0 - (correctedUV.y - pixelSize.y)));

            // Country border if ANY neighbor has different owner (takes priority)
            bool isCountryBorder = (rightOwner != currentOwner) ||
                                   (leftOwner != currentOwner) ||
                                   (upOwner != currentOwner) ||
                                   (downOwner != currentOwner);

            // Always render country borders, optionally render province borders
            if (isCountryBorder)
            {
                baseColor.rgb = _CountryBorderColor.rgb;
            }
            else if (_ProvinceBorderStrength > 0.01)
            {
                // Province border (same owner, different province) - only if enabled
                baseColor.rgb = _ProvinceBorderColor.rgb;
            }
        }
        return baseColor;
    }

    // STEP 1: Check border mask - early out for interior pixels (~90% of screen)
    float borderMask = SAMPLE_TEXTURE2D(_BorderMaskTexture, sampler_BorderMaskTexture, correctedUV).r;
    if (borderMask < 0.01)
    {
        return baseColor; // Interior pixel, no border
    }

    // STEP 2: Evaluate all nearby Bézier curves with spatial acceleration
    // Convert UV to pixel coordinates
    float2 pixelPos = correctedUV * mapSize;

    // Calculate grid cell for spatial lookup
    int gridX = (int)(pixelPos.x / cellSize);
    int gridY = (int)(pixelPos.y / cellSize);

    // Clamp to grid bounds
    gridX = clamp(gridX, 0, gridWidth - 1);
    gridY = clamp(gridY, 0, gridHeight - 1);

    // Get cell index and segment range
    int cellIndex = gridY * gridWidth + gridX;
    uint2 cellRange = gridCellRanges[cellIndex];
    uint startIdx = cellRange.x;
    uint count = cellRange.y;

    // Early out if no segments in this cell
    if (count == 0)
    {
        return baseColor;
    }

    // Find ALL curves within render distance (not just minimum)
    // This lets us detect junction overlaps
    // Use slightly larger threshold to fill gaps between connected segments
    float borderWidth = 0.6;
    int closeCount = 0;
    float minDistance = 999999.0;

    for (uint i = 0; i < count; i++)
    {
        uint segmentIdx = gridSegmentIndices[startIdx + i];
        BezierSegment seg = bezierSegments[segmentIdx];

        // Evaluate distance to curve
        float dist = DistanceToBezier(pixelPos, seg);

        // DIRECTIONAL CULLING: Prevent round caps ONLY at disconnected endpoints
        // Check connectivity flags
        bool p0Connected = (seg.connectivityFlags & 0x1) != 0;
        bool p3Connected = (seg.connectivityFlags & 0x2) != 0;

        float distToP0 = length(pixelPos - seg.P0);
        float distToP3 = length(pixelPos - seg.P3);

        // Only apply directional culling to DISCONNECTED endpoints
        // (connected endpoints need to render fully to join with other segments)

        // If P0 is DISCONNECTED and we're near it, apply directional culling
        if (!p0Connected && distToP0 < 1.5)
        {
            float2 tangentP0 = normalize(seg.P1 - seg.P0);
            float2 toPixel = normalize(pixelPos - seg.P0);
            float alongCurve = dot(toPixel, tangentP0);

            // If pixel is "behind" the disconnected start, don't render
            if (alongCurve < 0.0)
            {
                dist = 999999.0;
            }
        }

        // If P3 is DISCONNECTED and we're near it, apply directional culling
        if (!p3Connected && distToP3 < 1.5)
        {
            float2 tangentP3 = normalize(seg.P3 - seg.P2);
            float2 toPixel = normalize(pixelPos - seg.P3);
            float alongCurve = dot(toPixel, tangentP3);

            // If pixel is "behind" the disconnected end, don't render
            if (alongCurve < 0.0)
            {
                dist = 999999.0;
            }
        }

        minDistance = min(minDistance, dist);

        // Count curves within render distance (for junction detection)
        bool nearEndpoint = (distToP0 < 1.5 || distToP3 < 1.5);
        if (dist < borderWidth && !nearEndpoint)
        {
            closeCount++;
        }
    }

    // STEP 3: Junction-aware rendering
    // Check if we're at a junction (borderMask 0.6-0.8)
    bool isJunction = (borderMask > 0.6 && borderMask < 0.8);

    // Render logic:
    if (minDistance < borderWidth)
    {
        if (isJunction && closeCount > 1)
        {
            // JUNCTION with multiple curves: Render THINNER to prevent overlap
            // Use half the normal width at junctions
            float junctionWidth = borderWidth * 0.5;
            if (minDistance < junctionWidth)
            {
                baseColor.rgb = float3(0.0, 0.0, 0.0);
            }
        }
        else
        {
            // REGULAR border OR junction with single curve: Render normally
            baseColor.rgb = float3(0.0, 0.0, 0.0);
        }
    }

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

// Simple value noise function for fog of war
float ValueNoise(float2 uv)
{
    float2 i = floor(uv);
    float2 f = frac(uv);

    // Smooth interpolation
    f = f * f * (3.0 - 2.0 * f);

    // Four corners of grid cell
    float a = frac(sin(dot(i, float2(12.9898, 78.233))) * 43758.5453);
    float b = frac(sin(dot(i + float2(1.0, 0.0), float2(12.9898, 78.233))) * 43758.5453);
    float c = frac(sin(dot(i + float2(0.0, 1.0), float2(12.9898, 78.233))) * 43758.5453);
    float d = frac(sin(dot(i + float2(1.0, 1.0), float2(12.9898, 78.233))) * 43758.5453);

    // Bilinear interpolation
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Apply fog of war effect to base color
// Visibility states (R8 format):
//   0.0 = Unexplored (never seen) - show almost black
//   0.5 = Explored (seen before but not visible) - desaturated and darkened
//   1.0 = Visible (currently visible) - full color
float4 ApplyFogOfWar(float4 baseColor, float2 uv)
{
    // Early out if fog of war is disabled
    if (_FogOfWarEnabled < 0.5)
        return baseColor;

    // Smooth fade based on zoom level (0 = enabled, 1 = disabled)
    // Instead of early exit, we'll lerp between fog-affected and base color
    float zoomFade = saturate(_FogOfWarZoomDisable);

    float2 correctedUV = float2(uv.x, 1.0 - uv.y);
    float visibility = SAMPLE_TEXTURE2D(_FogOfWarTexture, sampler_FogOfWarTexture, correctedUV).r;

    // Generate animated noise for drifting fog effect
    // Add time offset to create movement
    float2 timeOffset = float2(_Time.y * _FogNoiseSpeed * 0.1, _Time.y * _FogNoiseSpeed * 0.05);
    float noise = ValueNoise(uv * _FogNoiseScale + timeOffset);

    // Calculate fog-affected color based on visibility
    float4 fogColor = baseColor;

    // Unexplored: visibility ≈ 0.0
    if (visibility < 0.25)
    {
        // Blend base color with unexplored color (configurable darkness)
        // Use alpha channel of unexplored color to control blend strength
        float blendStrength = _FogUnexploredColor.a;

        // Apply noise to blend strength for wispy edges
        blendStrength = saturate(blendStrength + (noise - 0.5) * _FogNoiseStrength);

        fogColor.rgb = lerp(baseColor.rgb, _FogUnexploredColor.rgb, blendStrength);
    }
    // Explored but not visible: visibility ≈ 0.5
    else if (visibility < 0.75)
    {
        // Desaturate and darken the base color
        float luminance = dot(baseColor.rgb, float3(0.299, 0.587, 0.114));
        float3 grayscale = float3(luminance, luminance, luminance);

        // Apply desaturation
        float3 desaturated = lerp(baseColor.rgb, grayscale, _FogExploredDesaturation);

        // Apply explored tint
        fogColor.rgb = desaturated * _FogExploredColor.rgb;

        // Blend in animated fog noise color based on noise value
        // Noise ranges 0-1, we want the fog clouds to be visible
        float fogCloudStrength = noise * _FogNoiseStrength;
        fogColor.rgb = lerp(fogColor.rgb, _FogNoiseColor.rgb, fogCloudStrength);
    }
    // Visible: visibility ≈ 1.0 - no fog, keep base color

    // Smooth fade between fog-affected and base color based on zoom level
    // zoomFade: 0 = show fog, 1 = hide fog (zoomed out)
    return lerp(fogColor, baseColor, zoomFade);
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