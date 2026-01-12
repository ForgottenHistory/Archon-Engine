Shader "Archon/DefaultFlat"
{
    Properties
    {
        // Core map textures from simulation
        _ProvinceIDTexture ("Province ID Texture (RG16)", 2D) = "black" {}
        _ProvinceOwnerTexture ("Province Owner Texture (R16)", 2D) = "black" {}
        _ProvinceColorTexture ("Province Color Texture (RGBA32)", 2D) = "white" {}
        _ProvinceDevelopmentTexture ("Province Development Texture (R8)", 2D) = "black" {}
        _ProvinceTerrainTexture ("Province Terrain Texture (RGBA32)", 2D) = "white" {}
        _HeightmapTexture ("Heightmap Texture (R8)", 2D) = "gray" {}
        _NormalMapTexture ("Normal Map Texture (RGB24)", 2D) = "bump" {}
        _ProvinceColorPalette ("Province Color Palette (256x1 RGBA32)", 2D) = "white" {}
        _CountryColorPalette ("Country Color Palette (1024x1 RGBA32)", 2D) = "white" {}

        // Border textures - each mode has dedicated texture
        _DistanceFieldBorderTexture ("Distance Field Borders (RG8)", 2D) = "black" {}
        _PixelPerfectBorderTexture ("Pixel Perfect Borders (RG8)", 2D) = "black" {}

        // Other generated render textures
        _HighlightTexture ("Highlight Texture (ARGB32)", 2D) = "black" {}
        _FogOfWarTexture ("Fog of War Texture (R8)", 2D) = "white" {}

        // Map visualization settings
        [Enum(Political, 0, Terrain, 1, Development, 2, Culture, 3, OwnerDebug, 4)] _MapMode ("Map Mode", Int) = 0

        // Border visualization (configurable from VisualStyleConfiguration)
        // 0=None, 1=DistanceField, 2=PixelPerfect, 3=MeshGeometry
        [Enum(None, 0, DistanceField, 1, PixelPerfect, 2, MeshGeometry, 3)] _BorderRenderingMode ("Border Rendering Mode", Int) = 1
        _CountryBorderStrength ("Country Border Strength", Range(0, 1)) = 1.0
        _CountryBorderColor ("Country Border Color", Color) = (0, 0, 0, 1)
        _ProvinceBorderStrength ("Province Border Strength", Range(0, 1)) = 0.5
        _ProvinceBorderColor ("Province Border Color", Color) = (0.3, 0.3, 0.3, 1)

        // AAA Distance Field Border Parameters (set from C#)
        _EdgeWidth ("Border Edge Width (pixels)", Float) = 0.0
        _GradientWidth ("Border Gradient Width (pixels)", Float) = 2.0
        _EdgeSmoothness ("Border Edge Smoothness", Float) = 2.0
        _EdgeColorMul ("Border Edge Color Multiplier", Range(0, 1)) = 0.7
        _GradientColorMul ("Border Gradient Color Multiplier", Range(0, 1)) = 0.85
        _EdgeAlpha ("Border Edge Alpha", Range(0, 1)) = 1.0
        _GradientAlphaInside ("Border Gradient Alpha Inside", Range(0, 1)) = 1.0
        _GradientAlphaOutside ("Border Gradient Alpha Outside", Range(0, 1)) = 0.0

        // Vector curve rendering (set from C#)
        [Toggle] _UseVectorCurves ("Use Vector Curve Borders", Float) = 0
        _BezierSegmentCount ("Bezier Segment Count", Int) = 0
        _MapWidth ("Map Width", Int) = 5632
        _MapHeight ("Map Height", Int) = 2048

        // Spatial grid parameters (set from C#)
        _GridWidth ("Grid Width", Int) = 88
        _GridHeight ("Grid Height", Int) = 32
        _GridCellSize ("Grid Cell Size", Int) = 64

        // Map mode colors (configurable from VisualStyleConfiguration)
        _OceanColor ("Ocean Color", Color) = (0.098, 0.157, 0.439, 1)
        _UnownedLandColor ("Unowned Land Color", Color) = (0.8, 0.7, 0.5, 1)

        // Development gradient (configurable from VisualStyleConfiguration)
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

        // Terrain adjustments (configurable from VisualStyleConfiguration)
        _TerrainBrightness ("Terrain Brightness", Range(0.5, 2)) = 1.0
        _TerrainSaturation ("Terrain Saturation", Range(0, 2)) = 1.0

        // Terrain detail mapping (3D only, but parameters available)
        _DetailTiling ("Detail Tiling", Range(0.001, 10)) = 0.05
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.5
        _TriPlanarTightenFactor ("Tri-Planar Tighten Factor", Range(0, 20)) = 4.0

        // Normal mapping lighting (configurable from VisualStyleConfiguration)
        _NormalMapStrength ("Normal Map Strength", Range(0, 2)) = 1.0
        _NormalMapAmbient ("Normal Map Ambient (Shadow Level)", Range(0, 1)) = 0.4
        _NormalMapHighlight ("Normal Map Highlight (Lit Level)", Range(1, 2)) = 1.4

        _HighlightStrength ("Highlight Strength", Range(0, 2)) = 1.0

        // Advanced Effects: Texture overlay (parchment, paper, etc.)
        _OverlayTexture ("Overlay Texture", 2D) = "white" {}
        _OverlayStrength ("Overlay Strength", Range(0, 1)) = 0.0

        // Advanced Effects: Color adjustments
        _CountryColorSaturation ("Country Color Saturation", Range(0, 1)) = 1.0

        // Fog of War (configurable from VisualStyleConfiguration)
        [Toggle] _FogOfWarEnabled ("Fog of War Enabled", Float) = 0
        _FogOfWarZoomDisable ("Fog of War Zoom Disable (set by camera)", Float) = 0
        _FogUnexploredColor ("Fog: Unexplored Color", Color) = (0.05, 0.05, 0.05, 1)
        _FogExploredColor ("Fog: Explored Tint", Color) = (0.5, 0.5, 0.5, 1)
        _FogExploredDesaturation ("Fog: Explored Desaturation", Range(0, 1)) = 0.7
        _FogNoiseScale ("Fog: Noise Scale", Range(0.1, 20)) = 5.0
        _FogNoiseStrength ("Fog: Noise Strength", Range(0, 1)) = 0.3
        _FogNoiseSpeed ("Fog: Noise Animation Speed", Range(0, 2)) = 0.1
        _FogNoiseColor ("Fog: Noise Color", Color) = (0.3, 0.3, 0.4, 1)

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
