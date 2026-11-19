// DayNight.hlsl
// ENGINE: Day/night on the map (globals published by MapDayNight, from Archon time).
//
// The map is wrapped onto a globe: map u = longitude (Greenwich at _ArchonGlobe.x), map v (0 = south
// edge, 1 = north edge) = latitude between _ArchonGlobe.y and .z (fractions of pole-to-pole).
// Night factor = how far a point is on the dark side, feathered across the terminator.
// Night opacity 0 (disabled / fading out) makes every function here a no-op.
//
//   float  ArchonNightFactor(float2 mapUV)                     0 day .. 1 night (opacity applied)
//   float3 ArchonApplyNight(float3 color, float2 mapUV, float desaturation)
//   float2 ArchonWorldToMapUV(float3 positionWS, float4 mapWorldBounds)   (min x, min z, size x, size z)

#ifndef ARCHON_DAY_NIGHT_INCLUDED
#define ARCHON_DAY_NIGHT_INCLUDED

float4 _ArchonSun;        // x = hour of day / 24, yzw = sun direction on the globe
float4 _ArchonDayNight;   // x = night opacity (0 = off), y = darkness (night brightness multiplier), z = terminator feather, w = desaturation
float4 _ArchonGlobe;      // x = Greenwich (map u), y = south edge latitude, z = north edge latitude (fractions pole to pole)
float4 _ArchonNightTint;  // rgb = night colour tint of the desaturated grey

float3 ArchonGlobeNormal(float2 mapUV)
{
    float lon = frac(mapUV.x - _ArchonGlobe.x + _ArchonSun.x);
    float latFraction = lerp(_ArchonGlobe.y, _ArchonGlobe.z, saturate(mapUV.y));
    float lat = (latFraction - 0.5) * 3.14159265;
    float r = cos(lat);
    return float3(sin(lon * 6.2831853) * r, sin(lat), cos(lon * 6.2831853) * r);
}

float ArchonNightFactor(float2 mapUV)
{
    if (_ArchonDayNight.x <= 0.0) return 0.0;
    float sunDot = dot(ArchonGlobeNormal(mapUV), _ArchonSun.yzw);
    float feather = max(_ArchonDayNight.z, 1e-4);
    return smoothstep(feather, -feather, sunDot) * _ArchonDayNight.x;
}

// HOI4-style night: desaturate towards a cool grey and darken. desaturation scales the grey blend
// (1 = full, lower keeps more colour: borders, water).
float3 ArchonNightify(float3 color, float desaturation)
{
    float amount = _ArchonDayNight.w * desaturation;
    float grey = dot(color, float3(0.4, 0.3, 0.05)) * 1.333;
    float3 night = saturate(lerp(grey.xxx, grey * _ArchonNightTint.rgb, 0.25));
    return lerp(color, night, amount) * _ArchonDayNight.y;
}

float3 ArchonApplyNight(float3 color, float2 mapUV, float desaturation)
{
    float night = ArchonNightFactor(mapUV);
    if (night <= 0.0) return color;
    return lerp(color, ArchonNightify(color, desaturation), night);
}

float2 ArchonWorldToMapUV(float3 positionWS, float4 mapWorldBounds)
{
    return (positionWS.xz - mapWorldBounds.xy) / max(mapWorldBounds.zw, 1e-6);
}

#endif // ARCHON_DAY_NIGHT_INCLUDED
