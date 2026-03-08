Shader "Archon/BorderMesh"
{
    Properties
    {
        _BorderTex ("Border Texture", 2D) = "white" {}
        _HeightmapTexture ("Heightmap", 2D) = "gray" {}
        _HeightScale ("Height Scale", Range(0, 100)) = 10.0
        _HeightOffset ("Height Offset (above terrain)", Float) = 0.01
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "BorderMesh"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BorderTex);
            SAMPLER(sampler_BorderTex);
            TEXTURE2D(_HeightmapTexture);
            SAMPLER(sampler_HeightmapTexture);

            float _HeightScale;
            float _HeightOffset;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 posOS = input.positionOS.xyz;

                // Derive heightmap UV from local mesh position
                // Local space: -5 to +5 (Unity default plane)
                // Flip Y to match terrain shader's heightmap sampling
                float2 heightUV = (posOS.xz + 5.0) / 10.0;
                heightUV.y = 1.0 - heightUV.y;

                // Sample heightmap and apply same displacement as terrain shader
                // Terrain shader: positionOS.y += (height - 0.5) * _HeightScale
                float height = SAMPLE_TEXTURE2D_LOD(_HeightmapTexture, sampler_HeightmapTexture, heightUV, 0).r;
                posOS.y = (height - 0.5) * _HeightScale + _HeightOffset;

                output.positionCS = TransformObjectToHClip(posOS);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Sample the border texture
                half4 texColor = SAMPLE_TEXTURE2D(_BorderTex, sampler_BorderTex, input.uv);

                // Use luminance of texture as alpha (line shape from texture)
                half luminance = dot(texColor.rgb, half3(0.299, 0.587, 0.114));
                half alpha = luminance * input.color.a;

                // Final color: tint with vertex color
                half3 finalColor = input.color.rgb;

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
