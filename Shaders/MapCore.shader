Shader "Archon/MapCore"
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
        _CountryColorPalette ("Country Color Palette (1024x1 RGBA32)", 2D) = "white" {}

        // Generated render textures
        _BorderTexture ("Border Texture (R8)", 2D) = "black" {}
        _HighlightTexture ("Highlight Texture (ARGB32)", 2D) = "black" {}

        // Map visualization settings
        [Enum(Political, 0, Terrain, 1, Development, 2, Culture, 3)] _MapMode ("Map Mode", Int) = 0

        // Border visualization (configurable from GAME layer)
        _CountryBorderStrength ("Country Border Strength", Range(0, 1)) = 1.0
        _CountryBorderColor ("Country Border Color", Color) = (0, 0, 0, 1)
        _ProvinceBorderStrength ("Province Border Strength", Range(0, 1)) = 0.5
        _ProvinceBorderColor ("Province Border Color", Color) = (0.3, 0.3, 0.3, 1)

        // Map mode colors (configurable from GAME layer)
        _OceanColor ("Ocean Color", Color) = (0.098, 0.157, 0.439, 1)
        _UnownedLandColor ("Unowned Land Color", Color) = (0.8, 0.7, 0.5, 1)

        // Development gradient (configurable from GAME layer)
        _DevVeryLow ("Development: Very Low", Color) = (0.545, 0, 0, 1)
        _DevLow ("Development: Low", Color) = (0.863, 0.078, 0.078, 1)
        _DevMedium ("Development: Medium", Color) = (1, 0.549, 0, 1)
        _DevHigh ("Development: High", Color) = (1, 0.843, 0, 1)
        _DevVeryHigh ("Development: Very High", Color) = (1, 1, 0, 1)

        // Development tier thresholds
        _DevTier1 ("Dev Tier 1 Threshold", Range(0, 1)) = 0.2
        _DevTier2 ("Dev Tier 2 Threshold", Range(0, 1)) = 0.4
        _DevTier3 ("Dev Tier 3 Threshold", Range(0, 1)) = 0.6
        _DevTier4 ("Dev Tier 4 Threshold", Range(0, 1)) = 0.8

        // Terrain adjustments (configurable from GAME layer)
        _TerrainBrightness ("Terrain Brightness", Range(0.5, 2)) = 1.0
        _TerrainSaturation ("Terrain Saturation", Range(0, 2)) = 1.0

        // Terrain detail mapping (resolution-independent detail)
        _TerrainTypeTexture ("Terrain Type Texture (R8)", 2D) = "black" {}
        _TerrainDetailArray ("Terrain Detail Array", 2DArray) = "" {}
        _HeightmapTexture ("Heightmap Texture (R8)", 2D) = "gray" {}
        _DetailTiling ("Detail Tiling (world-space)", Range(1, 500)) = 100.0
        _DetailStrength ("Detail Strength", Range(0, 1)) = 1.0
        _TriPlanarTightenFactor ("Tri-Planar Blend Sharpness", Range(1, 8)) = 4.0

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

            // Terrain detail mapping feature
            #pragma multi_compile_local _ TERRAIN_DETAIL_MAPPING

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
                float4 _CountryColorPalette_ST;
                float4 _BorderTexture_ST;
                float4 _HighlightTexture_ST;
                float4 _MainTex_ST;

                int _MapMode;

                // Border parameters (configurable from GAME layer)
                float _CountryBorderStrength;
                float4 _CountryBorderColor;
                float _ProvinceBorderStrength;
                float4 _ProvinceBorderColor;

                // Map mode colors (configurable from GAME layer)
                float4 _OceanColor;
                float4 _UnownedLandColor;

                // Development gradient colors (configurable from GAME layer)
                float4 _DevVeryLow;
                float4 _DevLow;
                float4 _DevMedium;
                float4 _DevHigh;
                float4 _DevVeryHigh;

                // Development tier thresholds
                float _DevTier1;
                float _DevTier2;
                float _DevTier3;
                float _DevTier4;

                // Terrain adjustments
                float _TerrainBrightness;
                float _TerrainSaturation;

                // Terrain detail mapping parameters
                float _DetailTiling;
                float _DetailStrength;
                float _TriPlanarTightenFactor;
                float _HeightScale;

                float _HighlightStrength;
            CBUFFER_END

            // Texture declarations - MUST come before includes that use them
            TEXTURE2D(_ProvinceIDTexture); SAMPLER(sampler_ProvinceIDTexture);
            TEXTURE2D(_ProvinceOwnerTexture); SAMPLER(sampler_ProvinceOwnerTexture);
            TEXTURE2D(_ProvinceColorTexture); SAMPLER(sampler_ProvinceColorTexture);
            TEXTURE2D(_ProvinceDevelopmentTexture); SAMPLER(sampler_ProvinceDevelopmentTexture);
            TEXTURE2D(_ProvinceTerrainTexture); SAMPLER(sampler_ProvinceTerrainTexture);
            TEXTURE2D(_ProvinceColorPalette); SAMPLER(sampler_ProvinceColorPalette);
            TEXTURE2D(_CountryColorPalette); SAMPLER(sampler_CountryColorPalette);
            TEXTURE2D(_TerrainColorPalette); SAMPLER(sampler_TerrainColorPalette);
            TEXTURE2D(_BorderTexture); SAMPLER(sampler_BorderTexture);
            TEXTURE2D(_HighlightTexture); SAMPLER(sampler_HighlightTexture);
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); // For SRP Batcher

            // Terrain detail mapping textures
            TEXTURE2D(_TerrainTypeTexture); SAMPLER(sampler_TerrainTypeTexture);
            TEXTURE2D_ARRAY(_TerrainDetailArray); SAMPLER(sampler_TerrainDetailArray);
            TEXTURE2D(_HeightmapTexture); SAMPLER(sampler_HeightmapTexture);
            StructuredBuffer<uint> _ProvinceTerrainBuffer;

            // Map mode includes (after texture declarations)
            #include "MapModeCommon.hlsl"  // ENGINE utilities
            // GAME-specific map mode shaders (GAME POLICY - visualization rules)
            #include "../../Game/Shaders/MapModes/MapModeTerrain.hlsl"
            #include "../../Game/Shaders/MapModes/MapModePolitical.hlsl"
            #include "../../Game/Shaders/MapModes/MapModeDevelopment.hlsl"

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
                float3 positionWS : TEXCOORD1;  // World position for detail mapping
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

                // Calculate world position for terrain detail mapping
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);

                return output;
            }

            // Fragment shader
            float4 frag(Varyings input) : SV_Target
            {
                // Special debug modes that return early
                if (_MapMode == 100) // Border debug mode
                {
                    float2 correctedUV = float2(input.uv.x, 1.0 - input.uv.y);
                    float2 borderValues = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, correctedUV).rg;
                    // R = country borders (red), G = province borders (green)
                    // Show both: white = no borders, red = country, green = province, yellow = both
                    return float4(borderValues.r, borderValues.g, 0.0, 1.0);
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
                    return _OceanColor; // Configurable from GAME layer
                }

                // Base color determination based on map mode using modular functions
                float4 baseColor;

                if (_MapMode == 0) // Political mode
                {
                    baseColor = RenderPolitical(provinceID, input.uv);
                }
                else if (_MapMode == 1) // Terrain mode
                {
                    baseColor = RenderTerrain(provinceID, input.uv, input.positionWS);
                }
                else if (_MapMode == 2) // Development mode
                {
                    baseColor = RenderDevelopment(provinceID, input.uv);
                }
                else if (_MapMode == 3) // Culture mode
                {
                    // Culture mode: would need culture data
                    // For now, use terrain mode with a tint
                    baseColor = RenderTerrain(provinceID, input.uv, input.positionWS);
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