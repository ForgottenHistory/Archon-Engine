// MapPassFlat.hlsl
// ENGINE: Complete flat (2D) map pass: vertex + fragment.
// Include LAST, with #include_with_pragmas so the entry point pragmas apply:
//
//   HLSLPROGRAM
//   #include "<engine>/Includes/MapShaderSetup.hlsl"
//   #include "<your map mode dispatcher>.hlsl"
//   #include_with_pragmas "<engine>/Includes/MapPassFlat.hlsl"
//   ENDHLSL
//
// Render state (Tags, ZWrite, Cull) stays in the .shader file.

#ifndef MAP_PASS_FLAT_INCLUDED
#define MAP_PASS_FLAT_INCLUDED

#pragma vertex ArchonMapVert
#pragma fragment ArchonMapFrag

#include "MapFragment.hlsl"

struct ArchonMapAttributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ArchonMapVaryings
{
    float4 positionHCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    UNITY_VERTEX_OUTPUT_STEREO
};

ArchonMapVaryings ArchonMapVert(ArchonMapAttributes input)
{
    ArchonMapVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.positionHCS = TransformWorldToHClip(output.positionWS);
    output.uv = input.uv;

    return output;
}

float4 ArchonMapFrag(ArchonMapVaryings input) : SV_Target
{
    return ArchonMapFragment(input.uv, input.positionWS);
}

#endif // MAP_PASS_FLAT_INCLUDED
