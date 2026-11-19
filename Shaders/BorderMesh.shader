Shader "Archon/BorderMesh"
{
    // One material per border class (see BorderMeshRenderer / BorderClassStyle).
    // Vertices sit on the border center line; the normal is the world offset of one map pixel
    // towards that side of the strip. Width is expanded here, so it can depend on zoom.
    Properties
    {
        _BorderTex ("Border Texture (U along, V across)", 2D) = "white" {}
        _BorderUseTexture ("Use Texture", Float) = 0
        _BorderColor ("Color", Color) = (1, 1, 1, 1)
        _BorderColorSource ("Color Source (0 color, 1 country globals, 2 province globals)", Float) = 2
        _BorderWidth ("Width (map px)", Float) = 0.6
        _BorderWidthPerLevel ("Extra Width per Level (map px)", Float) = 0
        _BorderMinScreenWidth ("Min Width (screen px)", Float) = 1
        _BorderTile ("Texture Repeats per Map Pixel", Float) = 0.4
        _BorderFade ("Fade (screen px per map px: hidden at x, visible at y)", Vector) = (0, 0, 0, 0)
        _HeightmapTexture ("Heightmap", 2D) = "gray" {}
        _HeightScale ("Height Scale", Float) = 10.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-100"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "BorderMesh"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BorderTex);
            SAMPLER(sampler_BorderTex);
            TEXTURE2D(_HeightmapTexture);
            SAMPLER(sampler_HeightmapTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _BorderTex_ST;
                float4 _HeightmapTexture_ST;
                float4 _BorderColor;
                float4 _BorderFade;
                float _BorderUseTexture;
                float _BorderColorSource;
                float _BorderWidth;
                float _BorderWidthPerLevel;
                float _BorderMinScreenWidth;
                float _BorderTile;
                float _HeightScale;
                float4 _MapWorldBounds;
            CBUFFER_END

            // Engine border globals (from the visual style asset)
            float4 _CountryBorderColor;
            float4 _ProvinceBorderColor;
            float _CountryBorderStrength;
            float _ProvinceBorderStrength;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;     // world offset of one map pixel towards this side
                float3 uv : TEXCOORD0;        // x = distance along (map px), y = 0..1 across, z = width level
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fade : TEXCOORD1;
            };

            float TerrainHeight(float3 posWS)
            {
                float2 terrainUV = (posWS.xz - _MapWorldBounds.xy) / _MapWorldBounds.zw;
                float2 heightUV = TRANSFORM_TEX(float2(terrainUV.x, 1.0 - terrainUV.y), _HeightmapTexture);
                float h = SAMPLE_TEXTURE2D_LOD(_HeightmapTexture, sampler_HeightmapTexture, heightUV, 0).r;
                return (h - 0.5) * _HeightScale;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 center = input.positionOS.xyz;
                center.y = TerrainHeight(center);

                // World size of one screen pixel at this depth (perspective and orthographic)
                float4 centerCS = TransformWorldToHClip(center);
                // Clamp w: vertices behind or level with the camera have w <= 0, which would make
                // the screen-pixel floor blow the strip up to enormous (screen-covering) triangles
                float worldPerScreenPx = 2.0 * max(centerCS.w, 1e-3) / (abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);

                // World length of one map pixel along the offset direction
                float worldPerMapPx = max(length(input.normalOS.xz), 1e-6);
                float screenPxPerMapPx = worldPerMapPx / worldPerScreenPx;

                // Width in map pixels: class width plus per-level width (rivers taper). The screen-pixel
                // floor scales with it, so relative widths survive zooming out.
                float width = _BorderWidth + _BorderWidthPerLevel * input.uv.z;
                float floorScale = clamp(_BorderMinScreenWidth / max(_BorderWidth * screenPxPerMapPx, 1e-5), 1.0, 64.0);
                float halfWidth = 0.5 * width * floorScale;

                float3 posWS = center;
                posWS.xz += input.normalOS.xz * halfWidth;
                posWS.y = TerrainHeight(posWS);

                output.positionCS = TransformWorldToHClip(posWS);
                output.uv = input.uv.xy;
                output.fade = (_BorderFade.y > _BorderFade.x)
                    ? smoothstep(_BorderFade.x, _BorderFade.y, screenPxPerMapPx)
                    : 1.0;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float4 color;
                float strength;
                if (_BorderColorSource > 1.5)      { color = _ProvinceBorderColor; strength = _ProvinceBorderStrength; }
                else if (_BorderColorSource > 0.5) { color = _CountryBorderColor;  strength = _CountryBorderStrength; }
                else                               { color = _BorderColor;         strength = 1.0; }

                float v = input.uv.y;
                float alpha;
                if (_BorderUseTexture > 0.5)
                {
                    // Paradox layout: texture U runs along the border, V across it
                    float4 tex = SAMPLE_TEXTURE2D(_BorderTex, sampler_BorderTex, float2(input.uv.x * _BorderTile, v));
                    color.rgb *= tex.rgb;
                    alpha = tex.a;
                }
                else
                {
                    // Solid strip with analytic anti-aliased edges (junction cap centers have v = 0.5)
                    float edge = 0.5 - abs(v - 0.5);
                    alpha = saturate(edge / max(fwidth(v), 1e-5));
                }

                return half4(color.rgb, color.a * strength * alpha * input.fade);
            }
            ENDHLSL
        }
    }
}
