Shader "Archon/BorderMesh"
{
    Properties
    {
        _BorderTex ("Border Texture", 2D) = "white" {}
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
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Sample the border texture
                // Texture has line in center (V = 0.5), black background to key out
                half4 texColor = SAMPLE_TEXTURE2D(_BorderTex, sampler_BorderTex, input.uv);

                // Use texture RGB for the line shape, vertex color for tinting
                // Key out black background: if texture is dark, make it transparent
                // The texture's non-black pixels define the visible line
                half luminance = dot(texColor.rgb, half3(0.299, 0.587, 0.114));

                // Alpha: combine texture luminance (line shape) with any existing alpha
                half alpha = luminance * input.color.a;

                // Final color: tint with vertex color
                half3 finalColor = input.color.rgb;

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
