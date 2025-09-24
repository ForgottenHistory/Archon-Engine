Shader "GrandStrategy/ProvinceBorders"
{
    Properties
    {
        _BorderColor ("Border Color", Color) = (0.1, 0.1, 0.1, 1)
        _BorderWidth ("Border Width", Range(0.5, 5.0)) = 2.0
        _BorderType ("Border Type (0=Province, 1=Country)", Float) = 0
        _FadeDistance ("Fade Distance", Float) = 1000
        _GradientTexture ("Border Gradient", 2D) = "white" {}
        _Opacity ("Border Opacity", Range(0, 1)) = 1.0
    }
    
    SubShader
    {
        Tags { 
            "RenderType"="Transparent" 
            "Queue"="Overlay"
            "RenderPipeline"="UniversalPipeline" 
        }
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        
        Pass
        {
            Name "BorderPass"
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float borderStrength : TEXCOORD1; // 0 at edge, 1 at center of border line
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float borderStrength : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };
            
            TEXTURE2D(_GradientTexture);
            SAMPLER(sampler_GradientTexture);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _BorderColor;
                float _BorderWidth;
                float _BorderType;
                float _FadeDistance;
                float _Opacity;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.uv = input.uv;
                output.borderStrength = input.borderStrength;
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // Sample gradient texture for smooth edges
                float gradient = SAMPLE_TEXTURE2D(_GradientTexture, sampler_GradientTexture, 
                                                 float2(input.borderStrength, 0.5)).r;
                
                // Calculate distance fade for LOD
                float cameraDistance = length(_WorldSpaceCameraPos - input.positionWS);
                float distanceFade = 1.0 - saturate(cameraDistance / _FadeDistance);
                
                // Different opacity for different border types
                float opacity = _Opacity;
                if (_BorderType < 0.5) // Province border
                {
                    opacity *= 0.7; // Province borders are more subtle
                    distanceFade = step(0.5, distanceFade); // Hide at distance
                }
                
                // Final color with gradient alpha
                float4 color = _BorderColor;
                color.a = gradient * opacity * distanceFade;
                
                // Anti-aliasing through gradient
                color.a = smoothstep(0.0, 0.1, color.a);
                
                return color;
            }
            ENDHLSL
        }
    }
}