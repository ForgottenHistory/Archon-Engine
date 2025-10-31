Shader "Archon/MapCoreTessellated"
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

        // Tessellation properties (ENGINE feature)
        _HeightmapTexture ("Heightmap Texture (R8)", 2D) = "gray" {}
        _HeightScale ("Height Scale", Range(0, 100)) = 10.0
        _TessellationFactor ("Tessellation Factor", Range(1, 64)) = 16.0
        _TessellationMaxDistance ("Tessellation Max Distance", Float) = 500.0
        _TessellationMinDistance ("Tessellation Min Distance", Float) = 50.0

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
                float4 _CountryColorPalette_ST;
                float4 _BorderTexture_ST;
                float4 _HighlightTexture_ST;
                float4 _MainTex_ST;
                float4 _HeightmapTexture_ST;

                int _MapMode;

                // Tessellation parameters
                float _HeightScale;
                float _TessellationFactor;
                float _TessellationMaxDistance;
                float _TessellationMinDistance;

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
            TEXTURE2D(_HeightmapTexture); SAMPLER(sampler_HeightmapTexture);

            // Map mode includes (after texture declarations)
            #include "MapModeCommon.hlsl"  // ENGINE utilities
            // GAME-specific map mode shaders (GAME POLICY - visualization rules)
            #include "../../Game/Shaders/MapModes/MapModeTerrain.hlsl"
            #include "../../Game/Shaders/MapModes/MapModePolitical.hlsl"
            #include "../../Game/Shaders/MapModes/MapModeDevelopment.hlsl"

            // Vertex input structure (pre-tessellation)
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Tessellation control point structure (output of TessVert, input to Hull)
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

            // Tessellation vertex shader - minimal processing, pass data to hull shader
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

                // Lerp tessellation factor based on distance (high detail near, low detail far)
                float t = saturate((distance - _TessellationMinDistance) / (_TessellationMaxDistance - _TessellationMinDistance));
                return lerp(_TessellationFactor, 1.0, t);
            }

            // Hull shader constant function - determines tessellation factors per patch
            TessellationFactors PatchConstantFunction(InputPatch<TessellationControlPoint, 3> patch)
            {
                TessellationFactors factors;

                // Calculate average world position of the triangle
                float3 p0 = TransformObjectToWorld(patch[0].positionOS.xyz);
                float3 p1 = TransformObjectToWorld(patch[1].positionOS.xyz);
                float3 p2 = TransformObjectToWorld(patch[2].positionOS.xyz);
                float3 center = (p0 + p1 + p2) / 3.0;

                // Distance-based LOD
                float tessFactor = CalculateTessellationFactor(center);

                // Apply same tessellation factor to all edges and inside
                factors.edge[0] = tessFactor;
                factors.edge[1] = tessFactor;
                factors.edge[2] = tessFactor;
                factors.inside = tessFactor;

                return factors;
            }

            // Hull shader - control point processing
            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [patchconstantfunc("PatchConstantFunction")]
            [outputcontrolpoints(3)]
            TessellationControlPoint Hull(InputPatch<TessellationControlPoint, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            // Domain shader - generates new vertices from tessellation
            [domain("tri")]
            Varyings Domain(TessellationFactors factors,
                           const OutputPatch<TessellationControlPoint, 3> patch,
                           float3 barycentricCoords : SV_DomainLocation)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(patch[0]);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Barycentric interpolation macro
                #define BARYCENTRIC_INTERPOLATE(fieldName) \
                    patch[0].fieldName * barycentricCoords.x + \
                    patch[1].fieldName * barycentricCoords.y + \
                    patch[2].fieldName * barycentricCoords.z

                // Interpolate position and UV from original triangle vertices
                float4 positionOS = BARYCENTRIC_INTERPOLATE(positionOS);
                output.uv = BARYCENTRIC_INTERPOLATE(uv);

                // Sample heightmap with bilinear filtering (GPU automatically smooths low-res heightmap)
                // Apply TRANSFORM_TEX to ensure UV tiling/offset is respected
                float2 heightUV = TRANSFORM_TEX(output.uv, _HeightmapTexture);
                float height = SAMPLE_TEXTURE2D_LOD(_HeightmapTexture, sampler_HeightmapTexture, heightUV, 0).r;

                // Displace vertex vertically (Y-up in Unity)
                // Height is normalized 0-1, scale by _HeightScale
                positionOS.y += (height - 0.5) * _HeightScale; // Center around 0.5 (sea level)

                // Transform to world space (needed for lighting in future)
                output.positionWS = TransformObjectToWorld(positionOS.xyz);

                // Transform to clip space for rendering
                output.positionHCS = TransformWorldToHClip(output.positionWS);

                return output;
            }

            // Fragment shader (identical to MapCore.shader - preserved UV coordinates work perfectly)
            float4 frag(Varyings input) : SV_Target
            {
                // Special debug modes that return early
                if (_MapMode == 100) // Border debug mode
                {
                    float2 correctedUV = float2(input.uv.x, 1.0 - input.uv.y);
                    float2 borderValues = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, correctedUV).rg;
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
                        return float4(0.1, 0.1, 0.1, 1.0);
                    else
                        return float4(r, g, b, 1.0);
                }

                // Sample province ID once for all modes
                uint provinceID = SampleProvinceID(input.uv);

                // Handle ocean/invalid provinces (ID 0)
                if (provinceID == 0)
                {
                    return _OceanColor;
                }

                // Base color determination based on map mode
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
                    baseColor = RenderTerrain(provinceID, input.uv);
                    baseColor.rgb *= float3(1.2, 0.8, 1.0);
                }
                else
                {
                    baseColor = RenderPolitical(provinceID, input.uv);
                }

                // Apply borders and highlights
                baseColor = ApplyBorders(baseColor, input.uv);
                baseColor = ApplyHighlights(baseColor, input.uv);

                return baseColor;
            }
            ENDHLSL
        }
    }

    // Fallback to non-tessellated version if tessellation not supported
    FallBack "Archon/MapCore"
}
