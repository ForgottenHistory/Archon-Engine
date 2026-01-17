Shader "Archon/DefaultTerrain"
{
    Properties
    {
        // Core map textures from simulation
        _ProvinceIDTexture ("Province ID Texture (RG16)", 2D) = "black" {}
        _ProvinceOwnerTexture ("Province Owner Texture (R16)", 2D) = "black" {}
        _ProvinceColorTexture ("Province Color Texture (RGBA32)", 2D) = "white" {}
        _ProvinceDevelopmentTexture ("Province Development Texture (R8)", 2D) = "black" {}
        _ProvinceTerrainTexture ("Province Terrain Texture (RGBA32)", 2D) = "white" {}
        _TerrainTypeTexture ("Terrain Type Texture (R8)", 2D) = "black" {}
        _ProvinceTerrainLookup ("Province Terrain Lookup (R8)", 2D) = "black" {}
        _TerrainDetailArray ("Terrain Detail Array", 2DArray) = "" {}
        _DetailNoiseTexture ("Detail Anti-Tiling Noise", 2D) = "gray" {}
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

        // Tessellation properties (ENGINE feature)
        _HeightScale ("Height Scale", Range(0, 100)) = 10.0
        _TessellationFactor ("Tessellation Factor", Range(1, 64)) = 16.0
        _TessellationMaxDistance ("Tessellation Max Distance", Float) = 500.0
        _TessellationMinDistance ("Tessellation Min Distance", Float) = 50.0

        // Map visualization settings
        // Mode 0: Political, Mode 1: Terrain, Mode 2+: Custom GAME modes via texture array
        [Enum(Political, 0, Terrain, 1, Custom, 2)] _MapMode ("Map Mode", Int) = 0
        _CustomMapModeIndex ("Custom Map Mode Index", Int) = 0
        _MapModeTextureCount ("Map Mode Texture Count", Int) = 0

        // Map Mode Texture Array - GAME modes write their visualization here
        // ENGINE samples from this array when _MapMode >= 2
        _MapModeTextureArray ("Map Mode Texture Array", 2DArray) = "" {}

        // Border visualization (configurable from GAME layer)
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

        // Terrain detail mapping (scale-independent detail)
        _DetailTiling ("Detail Tiling (world-space)", Range(1, 500)) = 100.0
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.5
        _TriPlanarTightenFactor ("Tri-Planar Blend Sharpness", Range(1, 8)) = 4.0

        // Normal mapping lighting (configurable from GAME layer)
        _NormalMapStrength ("Normal Map Strength", Range(0, 2)) = 1.0
        _NormalMapAmbient ("Normal Map Ambient (Shadow Level)", Range(0, 1)) = 0.4
        _NormalMapHighlight ("Normal Map Highlight (Lit Level)", Range(1, 2)) = 1.4

        _HighlightStrength ("Highlight Strength", Range(0, 2)) = 1.0

        // Advanced Effects: Texture overlay (parchment, paper, etc.)
        _OverlayTexture ("Overlay Texture", 2D) = "white" {}
        _OverlayStrength ("Overlay Strength", Range(0, 1)) = 0.5

        // Advanced Effects: Color adjustments
        _CountryColorSaturation ("Country Color Saturation", Range(0, 1)) = 0.5

        // Fog of War (configurable from GAME layer)
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
            Name "MapCoreTessellated"
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
                heightUV = TRANSFORM_TEX(heightUV, _HeightmapTexture);
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

    // Fallback to non-tessellated EU3 shader
    FallBack "Hegemon/EU3Classic"

    // Custom inspector GUI
    CustomEditor "Game.Shaders.EU3MapShaderGUI"
}
