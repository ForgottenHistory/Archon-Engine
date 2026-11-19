Shader "Archon/BorderMesh"
{
    Properties
    {
        _BorderTex ("Border Texture", 2D) = "white" {}
        _HeightmapTexture ("Heightmap", 2D) = "gray" {}
        _HeightScale ("Height Scale", Range(0, 100)) = 10.0
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

            TEXTURE2D(_HeightmapTexture);
            SAMPLER(sampler_HeightmapTexture);
            float4 _HeightmapTexture_ST;

            float _HeightScale;
            float4 _MapWorldBounds;

            // Engine border globals (set from the visual style)
            float4 _CountryBorderColor;
            float4 _ProvinceBorderColor;
            float _CountryBorderStrength;
            float _ProvinceBorderStrength;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;     // r = 1 country border, 0 province border
                float2 uv : TEXCOORD0;    // x = distance along border (map px), y = 0..1 across
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float isCountry : TEXCOORD1;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 posWS = input.positionOS.xyz;

                // Sample heightmap to position border approximately at terrain height
                float2 terrainUV;
                terrainUV.x = (posWS.x - _MapWorldBounds.x) / _MapWorldBounds.z;
                terrainUV.y = (posWS.z - _MapWorldBounds.y) / _MapWorldBounds.w;
                float2 heightUV = float2(terrainUV.x, 1.0 - terrainUV.y);
                heightUV = TRANSFORM_TEX(heightUV, _HeightmapTexture);
                float height = SAMPLE_TEXTURE2D_LOD(_HeightmapTexture, sampler_HeightmapTexture, heightUV, 0).r;
                posWS.y = (height - 0.5) * _HeightScale;

                output.positionCS = TransformWorldToHClip(posWS);
                output.isCountry = input.color.r;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                bool country = input.isCountry > 0.5;
                float4 color = country ? _CountryBorderColor : _ProvinceBorderColor;
                float strength = country ? _CountryBorderStrength : _ProvinceBorderStrength;

                // Analytic coverage across the strip: anti-aliased edges, and strips narrower than
                // a screen pixel fade out instead of flickering. Junction caps use v = 0.5 (solid).
                float v = input.uv.y;
                float edge = 0.5 - abs(v - 0.5);
                float coverage = saturate(edge / max(fwidth(v), 1e-5));

                return half4(color.rgb, color.a * strength * coverage);
            }
            ENDHLSL
        }
    }
}
