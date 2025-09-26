Shader "Dominion/MapCore"
{
    Properties
    {
        // Core map textures from simulation
        _ProvinceIDTexture ("Province ID Texture (RG16)", 2D) = "black" {}
        _ProvinceOwnerTexture ("Province Owner Texture (R16)", 2D) = "black" {}
        _ProvinceColorTexture ("Province Color Texture (RGBA32)", 2D) = "white" {}
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
                float4 _ProvinceColorPalette_ST;
                float4 _BorderTexture_ST;
                float4 _HighlightTexture_ST;
                float4 _MainTex_ST;

                int _MapMode;
                float _BorderStrength;
                float4 _BorderColor;
                float _HighlightStrength;
            CBUFFER_END

            // Texture declarations
            TEXTURE2D(_ProvinceIDTexture); SAMPLER(sampler_ProvinceIDTexture);
            TEXTURE2D(_ProvinceOwnerTexture); SAMPLER(sampler_ProvinceOwnerTexture);
            TEXTURE2D(_ProvinceColorTexture); SAMPLER(sampler_ProvinceColorTexture);
            TEXTURE2D(_ProvinceColorPalette); SAMPLER(sampler_ProvinceColorPalette);
            TEXTURE2D(_BorderTexture); SAMPLER(sampler_BorderTexture);
            TEXTURE2D(_HighlightTexture); SAMPLER(sampler_HighlightTexture);
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); // For SRP Batcher

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

            // Province ID encoding/decoding functions
            uint DecodeProvinceID(float2 encoded)
            {
                // Convert from float [0,1] back to uint16 values
                uint r = (uint)(encoded.r * 255.0 + 0.5);
                uint g = (uint)(encoded.g * 255.0 + 0.5);

                // Reconstruct 16-bit province ID from RG channels
                return (g << 8) | r;
            }

            float2 EncodeProvinceID(uint provinceID)
            {
                // Split 16-bit ID into two 8-bit values
                uint r = provinceID & 0xFF;
                uint g = (provinceID >> 8) & 0xFF;

                // Convert to float [0,1] range
                return float2(r / 255.0, g / 255.0);
            }

            // Get owner UV coordinate for province ID lookup
            float2 GetOwnerUV(uint provinceID, float2 baseUV)
            {
                // For now, use direct UV mapping - this could be optimized
                // In a real implementation, this might index into a compact owner lookup table
                return baseUV;
            }

            // Get color palette UV coordinate for owner ID
            float2 GetColorUV(uint ownerID)
            {
                // Map owner ID to palette texture coordinate
                // Palette is 256x1, so X = ownerID/256, Y = 0.5
                return float2(ownerID / 256.0, 0.5);
            }

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
                // Fix flipped UV coordinates - flip only Y
                float2 correctedUV = float2(input.uv.x, 1.0 - input.uv.y);

                // BORDER DEBUG MODE: Show just the border texture
                #if defined(MAP_MODE_BORDERS)
                    float borderValue = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, correctedUV).r;
                    return float4(borderValue, borderValue, borderValue, 1.0);
                #endif

                // DEBUG MODE: Show raw province IDs as colors
                #if defined(MAP_MODE_DEBUG)
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
                #endif

                // TERRAIN MODE: Show province colors from the bitmap with borders
                #if defined(MAP_MODE_TERRAIN)
                    float4 directColor = SAMPLE_TEXTURE2D(_ProvinceColorTexture, sampler_ProvinceColorTexture, correctedUV);

                    // Apply borders
                    float terrainBorderStrength = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, correctedUV).r;
                    directColor.rgb = lerp(directColor.rgb, _BorderColor.rgb, terrainBorderStrength * _BorderStrength);

                    return directColor;
                #endif

                // Sample province ID texture with point filtering (no interpolation)
                float2 provinceID_encoded = SAMPLE_TEXTURE2D(_ProvinceIDTexture, sampler_ProvinceIDTexture, correctedUV).rg;
                uint provinceID = DecodeProvinceID(provinceID_encoded);

                // Handle ocean/invalid provinces (ID 0)
                if (provinceID == 0)
                {
                    return float4(0.2, 0.4, 0.8, 1.0); // Ocean blue
                }

                // Sample owner information
                float ownerData = SAMPLE_TEXTURE2D(_ProvinceOwnerTexture, sampler_ProvinceOwnerTexture,
                    GetOwnerUV(provinceID, correctedUV)).r;
                uint ownerID = (uint)(ownerData * 255.0 + 0.5);

                // Base color determination based on map mode
                float4 baseColor;

                #if defined(MAP_MODE_POLITICAL)
                    // Political mode: use owner colors from palette
                    if (ownerID == 0)
                    {
                        // Unowned province - use neutral color
                        baseColor = float4(0.7, 0.7, 0.7, 1.0);
                    }
                    else
                    {
                        // Owned province - sample from color palette
                        float2 colorUV = GetColorUV(ownerID);
                        baseColor = SAMPLE_TEXTURE2D(_ProvinceColorPalette, sampler_ProvinceColorPalette, colorUV);
                    }
                #elif defined(MAP_MODE_TERRAIN)
                    // Terrain mode: use terrain-based colors (would need terrain data)
                    // For now, sample from province color texture directly
                    baseColor = SAMPLE_TEXTURE2D(_ProvinceColorTexture, sampler_ProvinceColorTexture, correctedUV);
                #elif defined(MAP_MODE_DEVELOPMENT)
                    // Development mode: would need development level data
                    // For now, use grayscale based on owner data
                    float devLevel = ownerData;
                    baseColor = float4(devLevel, devLevel, devLevel, 1.0);
                #elif defined(MAP_MODE_CULTURE)
                    // Culture mode: would need culture data
                    // For now, use a different color scheme
                    baseColor = SAMPLE_TEXTURE2D(_ProvinceColorTexture, sampler_ProvinceColorTexture, correctedUV);
                    baseColor.rgb *= float3(1.2, 0.8, 1.0); // Tint for culture mode
                #else
                    // Default to political mode
                    if (ownerID == 0)
                    {
                        baseColor = float4(0.7, 0.7, 0.7, 1.0);
                    }
                    else
                    {
                        float2 colorUV = GetColorUV(ownerID);
                        baseColor = SAMPLE_TEXTURE2D(_ProvinceColorPalette, sampler_ProvinceColorPalette, colorUV);
                    }
                #endif

                // Apply borders
                float borderStrength = SAMPLE_TEXTURE2D(_BorderTexture, sampler_BorderTexture, correctedUV).r;
                baseColor.rgb = lerp(baseColor.rgb, _BorderColor.rgb, borderStrength * _BorderStrength);

                // Apply highlights
                float4 highlight = SAMPLE_TEXTURE2D(_HighlightTexture, sampler_HighlightTexture, correctedUV);
                baseColor.rgb = lerp(baseColor.rgb, highlight.rgb, highlight.a * _HighlightStrength);

                return baseColor;
            }
            ENDHLSL
        }
    }

    // Fallback for older graphics cards
    FallBack "Universal Render Pipeline/Unlit"
}