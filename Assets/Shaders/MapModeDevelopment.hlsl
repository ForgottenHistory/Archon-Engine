#ifndef MAPMODE_DEVELOPMENT_INCLUDED
#define MAPMODE_DEVELOPMENT_INCLUDED

// ============================================================================
// Development Map Mode
// Renders provinces with development level gradient from red to yellow
// Uses development texture or generates gradient based on development level
// ============================================================================

float4 RenderDevelopment(uint provinceID, float2 uv)
{
    // Handle ocean/invalid provinces
    if (provinceID == 0)
    {
        return OCEAN_COLOR;
    }

    // Fix flipped UV coordinates
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Try to sample from development texture first
    float4 devTextureColor = SAMPLE_TEXTURE2D(_ProvinceDevelopmentTexture, sampler_ProvinceDevelopmentTexture, correctedUV);

    // Check if the development texture has valid data (not black/purple)
    if (devTextureColor.r > 0.01 || devTextureColor.g > 0.01 || devTextureColor.b > 0.01)
    {
        // Development texture has data, use it
        return devTextureColor;
    }

    // Fallback: Generate development colors based on a simple pattern
    // This would normally use actual development data from simulation

    // For now, create a simple gradient based on province ID
    float developmentLevel = (provinceID % 100) / 100.0; // 0.0 to 1.0

    // Development color gradient: Dark Red → Red → Orange → Yellow
    float3 veryLowDev = float3(0.545, 0.0, 0.0);    // Dark red (139, 0, 0)
    float3 lowDev = float3(0.863, 0.078, 0.078);    // Red (220, 20, 20)
    float3 mediumDev = float3(1.0, 0.549, 0.0);     // Orange (255, 140, 0)
    float3 highDev = float3(1.0, 0.843, 0.0);       // Yellow (255, 215, 0)

    float3 color;
    if (developmentLevel < 0.33)
    {
        // Very low to low development
        float t = developmentLevel / 0.33;
        color = lerp(veryLowDev, lowDev, t);
    }
    else if (developmentLevel < 0.66)
    {
        // Low to medium development
        float t = (developmentLevel - 0.33) / 0.33;
        color = lerp(lowDev, mediumDev, t);
    }
    else
    {
        // Medium to high development
        float t = (developmentLevel - 0.66) / 0.34;
        color = lerp(mediumDev, highDev, t);
    }

    return float4(color, 1.0);
}

#endif // MAPMODE_DEVELOPMENT_INCLUDED