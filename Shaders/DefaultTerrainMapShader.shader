Shader "Archon/DefaultTerrain"
{
    Properties
    {
        // Engine-owned values (textures, map mode, borders, fog, colors, terrain look) are
        // shader globals set by MapShaderGlobals. Do NOT declare them here: a material
        // property shadows the global. Only per-material values below; each must match the
        // UnityPerMaterial CBUFFER in DefaultCommon.hlsl.

        // Tessellation (per-material: describes this shader's geometry)
        _HeightScale ("Height Scale", Range(0, 100)) = 10.0
        _TessellationFactor ("Tessellation Factor", Range(1, 64)) = 16.0
        _TessellationMaxDistance ("Tessellation Max Distance", Float) = 500.0
        _TessellationMinDistance ("Tessellation Min Distance", Float) = 50.0

        // Development gradient (GAME policy, moves to GAME shaders later)
        _DevVeryLow ("Development: Very Low", Color) = (0.545, 0, 0, 1)
        _DevLow ("Development: Low", Color) = (0.863, 0.078, 0.078, 1)
        _DevMedium ("Development: Medium", Color) = (1, 0.549, 0, 1)
        _DevHigh ("Development: High", Color) = (1, 0.843, 0, 1)
        _DevVeryHigh ("Development: Very High", Color) = (1, 1, 0, 1)
        _DevTier1 ("Dev Tier 1 Threshold", Range(0, 1)) = 0.2
        _DevTier2 ("Dev Tier 2 Threshold", Range(0, 1)) = 0.4
        _DevTier3 ("Dev Tier 3 Threshold", Range(0, 1)) = 0.6
        _DevTier4 ("Dev Tier 4 Threshold", Range(0, 1)) = 0.8

        // Material tuning
        _TriPlanarTightenFactor ("Tri-Planar Blend Sharpness", Range(1, 8)) = 4.0
        _HighlightStrength ("Highlight Strength", Range(0, 2)) = 1.0

        // SRP Batcher placeholder
        _MainTex ("Main Texture (unused - for SRP Batcher)", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry"
        }

        LOD 100

        Pass
        {
            Name "ArchonTerrain"
            Tags { "LightMode"="UniversalForward" }

            // Proper depth testing for map rendering
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            // Tessellation requires shader model 4.6+
            #pragma target 4.6

            // Tessellation pipeline stages
            #pragma vertex TessVert
            #pragma hull Hull
            #pragma domain Domain
            #pragma fragment frag

            // Map mode switching is now int-based (_MapMode), not keyword-based
            // This enables unlimited GAME-defined map modes via texture array

            // Terrain detail mapping (only in tessellated shader)
            #pragma multi_compile_local _ TERRAIN_DETAIL_MAPPING

            // URP includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Default shader includes - MODULAR ARCHITECTURE
            #include "Includes/DefaultCommon.hlsl"
            #include "Includes/DefaultMapModes.hlsl"
            #include "Includes/DefaultLighting.hlsl"
            #include "Includes/DefaultEffects.hlsl"
            #include "Includes/DefaultDebugModes.hlsl"

            // Vertex input structure (pre-tessellation)
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Tessellation control point structure
            struct TessellationControlPoint
            {
                float4 positionOS : INTERNALTESSPOS;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Hull shader constant data (per-patch)
            struct TessellationFactors
            {
                float edge[3] : SV_TessFactor;
                float inside : SV_InsideTessFactor;
            };

            // Domain shader output / Fragment shader input
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Tessellation vertex shader
            TessellationControlPoint TessVert(Attributes input)
            {
                TessellationControlPoint output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionOS = input.positionOS;
                output.uv = input.uv;

                return output;
            }

            // Calculate tessellation factor based on camera distance
            float CalculateTessellationFactor(float3 positionWS)
            {
                float3 cameraPos = GetCameraPositionWS();
                float distance = length(cameraPos - positionWS);

                // Lerp tessellation factor based on distance
                float t = saturate((distance - _TessellationMinDistance) / (_TessellationMaxDistance - _TessellationMinDistance));
                return lerp(_TessellationFactor, 1.0, t);
            }

            // Hull shader constant function
            TessellationFactors PatchConstantFunction(InputPatch<TessellationControlPoint, 3> patch)
            {
                TessellationFactors factors;

                // Calculate average world position
                float3 p0 = TransformObjectToWorld(patch[0].positionOS.xyz);
                float3 p1 = TransformObjectToWorld(patch[1].positionOS.xyz);
                float3 p2 = TransformObjectToWorld(patch[2].positionOS.xyz);
                float3 center = (p0 + p1 + p2) / 3.0;

                // Distance-based LOD
                float tessFactor = CalculateTessellationFactor(center);

                factors.edge[0] = tessFactor;
                factors.edge[1] = tessFactor;
                factors.edge[2] = tessFactor;
                factors.inside = tessFactor;

                return factors;
            }

            // Hull shader
            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [patchconstantfunc("PatchConstantFunction")]
            [outputcontrolpoints(3)]
            TessellationControlPoint Hull(InputPatch<TessellationControlPoint, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            // Domain shader
            [domain("tri")]
            Varyings Domain(TessellationFactors factors,
                           const OutputPatch<TessellationControlPoint, 3> patch,
                           float3 barycentricCoords : SV_DomainLocation)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(patch[0]);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Barycentric interpolation
                #define BARYCENTRIC_INTERPOLATE(fieldName) \
                    patch[0].fieldName * barycentricCoords.x + \
                    patch[1].fieldName * barycentricCoords.y + \
                    patch[2].fieldName * barycentricCoords.z

                float4 positionOS = BARYCENTRIC_INTERPOLATE(positionOS);
                output.uv = BARYCENTRIC_INTERPOLATE(uv);

                // Sample heightmap with bilinear filtering (flip Y to match texture orientation)
                float2 heightUV = float2(output.uv.x, 1.0 - output.uv.y);
                float height = SAMPLE_TEXTURE2D_LOD(_HeightmapTexture, sampler_HeightmapTexture, heightUV, 0).r;

                // Displace vertex vertically (center around 0.5 = sea level)
                positionOS.y += (height - 0.5) * _HeightScale;

                // Transform to world and clip space
                output.positionWS = TransformObjectToWorld(positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);

                return output;
            }

            // Fragment shader - composition of modular includes
            float4 frag(Varyings input) : SV_Target
            {
                // DEBUG: Directly sample terrain texture to verify it's bound
                // Uncomment to test:
                // float2 debugUV = float2(input.uv.x, 1.0 - input.uv.y);
                // return SAMPLE_TEXTURE2D(_ProvinceTerrainTexture, sampler_ProvinceTerrainTexture, debugUV);

                // Check for debug modes first (100-103)
                float4 debugOutput;
                if (TryRenderDebugMode(_MapMode, input.uv, debugOutput))
                {
                    return debugOutput;
                }

                // Sample province ID
                uint provinceID = SampleProvinceID(input.uv);

                if (provinceID == 0)
                {
                    return _OceanColor;
                }

                // Render base color based on map mode
                float4 baseColor = RenderMapMode(_MapMode, provinceID, input.uv, input.positionWS);

                // Apply lighting (normal map based)
                baseColor = ApplyNormalMapLighting(baseColor, input.uv);

                // Apply borders (handled by BorderComputeDispatcher + distance field texture)
                baseColor = ApplyBorders(baseColor, input.uv);

                // Apply highlights (selection, etc.)
                baseColor = ApplyHighlights(baseColor, input.uv);

                // Apply fog of war
                baseColor = ApplyFogOfWar(baseColor, input.uv);

                // Apply overlay texture (parchment, paper effects)
                baseColor = ApplyOverlayTexture(baseColor, input.uv);

                return baseColor;
            }
            ENDHLSL
        }
    }

    // Fallback to non-tessellated ENGINE shader (no hull/domain support)
    FallBack "Archon/DefaultFlat"

    // Games can create custom ShaderGUI editors if needed
    // CustomEditor "YourGame.Shaders.CustomMapShaderGUI"
}
