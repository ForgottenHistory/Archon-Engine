// DefaultLighting.hlsl
// ENGINE: Normal mapping and lighting calculations
// Part of Default visual style shader architecture

#ifndef DEFAULT_LIGHTING_INCLUDED
#define DEFAULT_LIGHTING_INCLUDED

// Apply normal map lighting to base color
// Returns lit color based on heightmap-derived normal map
float4 ApplyNormalMapLighting(float4 baseColor, float2 uv)
{
    // Correct UV for texture orientation (flip Y)
    float2 correctedUV = float2(uv.x, 1.0 - uv.y);

    // Sample normal map (RG format - X and Z components)
    float2 normalRG = SAMPLE_TEXTURE2D(_NormalMapTexture, sampler_NormalMapTexture, correctedUV).rg;

    // Convert RG to normal vector XZ components (-1 to +1 range)
    float normalX = (normalRG.r - 0.5) * 2.0;
    float normalZ = (normalRG.g - 0.5) * 2.0;

    // Reconstruct Y component from X and Z (unit length sphere)
    // normal.x^2 + normal.y^2 + normal.z^2 = 1
    // normal.y = sqrt(1 - normal.x^2 - normal.z^2)
    float normalY = sqrt(max(0.0, 1.0 - normalX * normalX - normalZ * normalZ));

    // Construct full normal vector
    float3 normal = normalize(float3(normalX, normalY, normalZ));

    // Fixed directional light (top-down with slight angle)
    float3 lightDir = normalize(float3(-0.5, -0.5, 0.7));

    // Calculate diffuse lighting
    float diffuse = max(0.0, dot(normal, lightDir));

    // Lerp between ambient shadow and highlight based on diffuse
    float lighting = lerp(_NormalMapAmbient, _NormalMapHighlight, diffuse * _NormalMapStrength);

    // Apply lighting to base color
    baseColor.rgb *= lighting;

    return baseColor;
}

#endif // DEFAULT_LIGHTING_INCLUDED
