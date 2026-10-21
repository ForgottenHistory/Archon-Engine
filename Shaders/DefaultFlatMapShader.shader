Shader "Archon/DefaultFlat"
{
    Properties
    {
        // Engine-owned values (textures, map mode, borders, fog, colors, terrain look) are
        // shader globals set by MapShaderGlobals. Do NOT declare them here: a material
        // property shadows the global. Only per-material values below; each must match the
        // UnityPerMaterial CBUFFER in DefaultCommon.hlsl.

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
        _TriPlanarTightenFactor ("Tri-Planar Tighten Factor", Range(0, 20)) = 4.0
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
            Name "ArchonFlat"
            Tags { "LightMode"="UniversalForward" }

            // Proper depth testing for map rendering
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Map mode switching is now int-based (_MapMode), not keyword-based
            // This enables unlimited GAME-defined map modes via texture array

            // URP includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ENGINE shader includes - MODULAR ARCHITECTURE
            #include "Includes/DefaultCommon.hlsl"
            #include "Includes/DefaultMapModes.hlsl"
            #include "Includes/DefaultLighting.hlsl"
            #include "Includes/DefaultEffects.hlsl"
            #include "Includes/DefaultDebugModes.hlsl"

            // Vertex input structure
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Fragment input structure
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Vertex shader
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;

                return output;
            }

            // Fragment shader - composition of modular includes
            float4 frag(Varyings input) : SV_Target
            {
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

    // Games can create custom ShaderGUI editors if needed
    // CustomEditor "YourGame.Shaders.CustomMapShaderGUI"
}
