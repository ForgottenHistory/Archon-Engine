Shader "Dominion/MapCore"
{
    Properties
    {
        // Core map textures from simulation
        _ProvinceIDTexture ("Province ID Texture (RG16)", 2D) = "black" {}
        _ProvinceOwnerTexture ("Province Owner Texture (R16)", 2D) = "black" {}
        _ProvinceColorTexture ("Province Color Texture (RGBA32)", 2D) = "white" {}
        _ProvinceDevelopmentTexture ("Province Development Texture (R8)", 2D) = "black" {}
        _ProvinceTerrainTexture ("Province Terrain Texture (RGBA32)", 2D) = "white" {}
        _ProvinceColorPalette ("Province Color Palette (256x1 RGBA32)", 2D) = "white" {}

        // Generated render textures
        _BorderTexture ("Border Texture (R8)", 2D) = "black" {}
        _HighlightTexture ("Highlight Texture (ARGB32)", 2D) = "black" {}

        // Map visualization settings
        [Enum(Political, 0, Terrain, 1, Development, 2, Culture, 3)] _MapMode ("Map Mode", Int) = 0
        _BorderStrength ("Border Strength", Range(0, 1)) = 1.0
        _BorderColor ("Border Color", Color) = (0, 0, 0, 1)
        _HighlightStrength ("Highlight Strength", Range(0, 2)) = 1.0

        // Performance settings
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
            Name "MapCore"
            Tags { "LightMode"="UniversalForward" }

            // Proper depth testing for map rendering
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Shader variants for different map modes
            #pragma multi_compile_local _ MAP_MODE_POLITICAL MAP_MODE_TERRAIN MAP_MODE_DEVELOPMENT MAP_MODE_CULTURE MAP_MODE_DEBUG MAP_MODE_BORDERS

            // URP includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // SRP Batcher compatibility
            CBUFFER_START(UnityPerMaterial)
                float4 _ProvinceIDTexture_ST;
                float4 _ProvinceOwnerTexture_ST;
                float4 _ProvinceColorTexture_ST;
                float4 _ProvinceDevelopmentTexture_ST;
                float4 _ProvinceTerrainTexture_ST;
                float4 _ProvinceColorPalette_ST;
                float4 _BorderTexture_ST;
                float4 _HighlightTexture_ST;
                float4 _MainTex_ST;

                int _MapMode;
                float _BorderStrength;
                float4 _BorderColor;
                float _HighlightStrength;
            CBUFFER_END

            // Texture declarations - MUST come before includes that use them
            TEXTURE2D(_ProvinceIDTexture); SAMPLER(sampler_ProvinceIDTexture);
            TEXTURE2D(_ProvinceOwnerTexture); SAMPLER(sampler_ProvinceOwnerTexture);
            TEXTURE2D(_ProvinceColorTexture); SAMPLER(sampler_ProvinceColorTexture);
            TEXTURE2D(_ProvinceDevelopmentTexture); SAMPLER(sampler_ProvinceDevelopmentTexture);
            TEXTURE2D(_ProvinceTerrainTexture); SAMPLER(sampler_ProvinceTerrainTexture);
            TEXTURE2D(_ProvinceColorPalette); SAMPLER(sampler_ProvinceColorPalette);
            TEXTURE2D(_TerrainColorPalette); SAMPLER(sampler_TerrainColorPalette);
            TEXTURE2D(_BorderTexture); SAMPLER(sampler_BorderTexture);
            TEXTURE2D(_HighlightTexture); SAMPLER(sampler_HighlightTexture);
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); // For SRP Batcher

            // Map mode includes - same directory (after texture declarations)
            #include "MapModeCommon.hlsl"
            #include "MapModeTerrain.hlsl"
            #include "MapModePolitical.hlsl"
            #include "MapModeDevelopment.hlsl"

            // Vertex input structure
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Vertex output structure
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Utility functions now in MapModeCommon.hlsl

            // Vertex shader
            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Transform to clip space
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);

                // Pass through UV coordinates with tiling and offset
                output.uv = TRANSFORM_TEX(input.uv, _ProvinceIDTexture);

                return output;
            }

            // Fragment shader
            float4 frag(Varyings input) : SV_Target
            {
                // Special debug modes that return early
                if (_MapMode == 100) // Border debug mode
                {
                    float2 correctedUV = float2(input.uv.x, 1.0 - input.uv.y);
                    float borderValue = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, correctedUV).r;
                    return float4(borderValue, borderValue, borderValue, 1.0);
                }
                else if (_MapMode == 101) // Province ID debug mode
                {
                    float2 correctedUV = float2(input.uv.x, 1.0 - input.uv.y);
                    float2 provinceID_raw = SAMPLE_TEXTURE2D(_ProvinceIDTexture, sampler_ProvinceIDTexture, correctedUV).rg;
                    uint debugID = DecodeProvinceID(provinceID_raw);

                    // Convert province ID to a visible color
                    float r = (debugID % 256) / 255.0;
                    float g = ((debugID / 256) % 256) / 255.0;
                    float b = ((debugID / 65536) % 256) / 255.0;

                    if (debugID == 0)
                        return float4(0.1, 0.1, 0.1, 1.0); // Dark gray for ID 0
                    else
                        return float4(r, g, b, 1.0);
                }

                // Sample province ID once for all modes
                uint provinceID = SampleProvinceID(input.uv);

                // Handle ocean/invalid provinces (ID 0)
                if (provinceID == 0)
                {
                    return OCEAN_COLOR;
                }

                // Base color determination based on map mode using modular functions
                float4 baseColor;

                if (_MapMode == 0) // Political mode
                {
                    baseColor = RenderPolitical(provinceID, input.uv);
                }
                else if (_MapMode == 1) // Terrain mode
                {
                    baseColor = RenderTerrain(provinceID, input.uv);
                }
                else if (_MapMode == 2) // Development mode
                {
                    baseColor = RenderDevelopment(provinceID, input.uv);
                }
                else if (_MapMode == 3) // Culture mode
                {
                    // Culture mode: would need culture data
                    // For now, use terrain mode with a tint
                    baseColor = RenderTerrain(provinceID, input.uv);
                    baseColor.rgb *= float3(1.2, 0.8, 1.0); // Tint for culture mode
                }
                else
                {
                    // Default to political mode
                    baseColor = RenderPolitical(provinceID, input.uv);
                }

                // Apply borders and highlights using common functions
                baseColor = ApplyBorders(baseColor, input.uv);
                baseColor = ApplyHighlights(baseColor, input.uv);

                return baseColor;
            }
            ENDHLSL
        }
    }

    // Fallback for older graphics cards
    FallBack "Universal Render Pipeline/Unlit"
}