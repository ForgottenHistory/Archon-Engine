// MapPassTerrain.hlsl
// ENGINE: Complete tessellated terrain map pass: vertex + hull + domain + fragment.
// Include LAST, with #include_with_pragmas so the entry point pragmas apply:
//
//   HLSLPROGRAM
//   #include "<engine>/Includes/MapShaderSetup.hlsl"
//   #include "<your map mode dispatcher>.hlsl"
//   #include_with_pragmas "<engine>/Includes/MapPassTerrain.hlsl"
//   ENDHLSL
//
// Requires material properties: _HeightScale, _TessellationFactor,
// _TessellationMinDistance, _TessellationMaxDistance (UnityPerMaterial in DefaultCommon.hlsl).
// Render state (Tags, ZWrite, Cull) stays in the .shader file.

#ifndef MAP_PASS_TERRAIN_INCLUDED
#define MAP_PASS_TERRAIN_INCLUDED

// Tessellation requires shader model 4.6+
#pragma target 4.6

#pragma vertex ArchonMapTessVert
#pragma hull ArchonMapHull
#pragma domain ArchonMapDomain
#pragma fragment ArchonMapFrag

// Terrain detail texture blending (toggled per material by the terrain map mode)
#pragma multi_compile_local _ TERRAIN_DETAIL_MAPPING

#include "MapFragment.hlsl"

// Vertex input (pre-tessellation)
struct ArchonMapAttributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ArchonMapControlPoint
{
    float4 positionOS : INTERNALTESSPOS;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ArchonMapTessFactors
{
    float edge[3] : SV_TessFactor;
    float inside : SV_InsideTessFactor;
};

// Domain output / fragment input
struct ArchonMapVaryings
{
    float4 positionHCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    UNITY_VERTEX_OUTPUT_STEREO
};

ArchonMapControlPoint ArchonMapTessVert(ArchonMapAttributes input)
{
    ArchonMapControlPoint output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    output.positionOS = input.positionOS;
    output.uv = input.uv;

    return output;
}

// Distance-based tessellation LOD
float ArchonMapTessFactor(float3 positionWS)
{
    float distance = length(GetCameraPositionWS() - positionWS);
    float t = saturate((distance - _TessellationMinDistance) / (_TessellationMaxDistance - _TessellationMinDistance));
    return lerp(_TessellationFactor, 1.0, t);
}

ArchonMapTessFactors ArchonMapPatchConstant(InputPatch<ArchonMapControlPoint, 3> patch)
{
    float3 p0 = TransformObjectToWorld(patch[0].positionOS.xyz);
    float3 p1 = TransformObjectToWorld(patch[1].positionOS.xyz);
    float3 p2 = TransformObjectToWorld(patch[2].positionOS.xyz);
    float tessFactor = ArchonMapTessFactor((p0 + p1 + p2) / 3.0);

    ArchonMapTessFactors factors;
    factors.edge[0] = tessFactor;
    factors.edge[1] = tessFactor;
    factors.edge[2] = tessFactor;
    factors.inside = tessFactor;
    return factors;
}

[domain("tri")]
[partitioning("fractional_odd")]
[outputtopology("triangle_cw")]
[patchconstantfunc("ArchonMapPatchConstant")]
[outputcontrolpoints(3)]
ArchonMapControlPoint ArchonMapHull(InputPatch<ArchonMapControlPoint, 3> patch, uint id : SV_OutputControlPointID)
{
    return patch[id];
}

[domain("tri")]
ArchonMapVaryings ArchonMapDomain(ArchonMapTessFactors factors,
                                  const OutputPatch<ArchonMapControlPoint, 3> patch,
                                  float3 barycentricCoords : SV_DomainLocation)
{
    ArchonMapVaryings output;
    UNITY_SETUP_INSTANCE_ID(patch[0]);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float4 positionOS = patch[0].positionOS * barycentricCoords.x
                      + patch[1].positionOS * barycentricCoords.y
                      + patch[2].positionOS * barycentricCoords.z;
    output.uv = patch[0].uv * barycentricCoords.x
              + patch[1].uv * barycentricCoords.y
              + patch[2].uv * barycentricCoords.z;

    // Heightmap displacement (flip Y to match texture orientation; 0.5 = sea level)
    float2 heightUV = float2(output.uv.x, 1.0 - output.uv.y);
    float height = SAMPLE_TEXTURE2D_LOD(_HeightmapTexture, sampler_HeightmapTexture, heightUV, 0).r;
    positionOS.y += (height - 0.5) * _HeightScale;

    output.positionWS = TransformObjectToWorld(positionOS.xyz);
    output.positionHCS = TransformWorldToHClip(output.positionWS);

    return output;
}

float4 ArchonMapFrag(ArchonMapVaryings input) : SV_Target
{
    return ArchonMapFragment(input.uv, input.positionWS);
}

#endif // MAP_PASS_TERRAIN_INCLUDED
