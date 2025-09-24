Shader "GrandStrategy/StylizedWater"
{
    Properties
    {
        _ShallowColor ("Shallow Water Color", Color) = (0.42, 0.55, 0.68, 1)
        _DeepColor ("Deep Water Color", Color) = (0.17, 0.29, 0.42, 1)
        _DepthGradient ("Depth Gradient Distance", Float) = 50.0
        _WaveTexture ("Wave Normal Map", 2D) = "bump" {}
        _WaveSpeed ("Wave Speed", Range(0, 0.1)) = 0.01
        _WaveScale ("Wave Scale", Float) = 10.0
        _FoamTexture ("Foam Texture", 2D) = "white" {}
        _FoamDistance ("Foam Distance from Shore", Float) = 5.0
        _WavePattern ("Stylized Wave Pattern", 2D) = "white" {}
        _PatternOpacity ("Pattern Opacity", Range(0, 1)) = 0.3
    }
    
    SubShader
    {
        Tags { 
            "RenderType"="Transparent" 
            "Queue"="Transparent-100"
            "RenderPipeline"="UniversalPipeline" 
        }
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        
        Pass
        {
            Name "WaterPass"
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float distanceToShore : TEXCOORD1; // Custom attribute
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float distanceToShore : TEXCOORD2;
            };
            
            TEXTURE2D(_WaveTexture);
            SAMPLER(sampler_WaveTexture);
            TEXTURE2D(_FoamTexture);
            SAMPLER(sampler_FoamTexture);
            TEXTURE2D(_WavePattern);
            SAMPLER(sampler_WavePattern);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float _DepthGradient;
                float _WaveSpeed;
                float _WaveScale;
                float _FoamDistance;
                float _PatternOpacity;
                float4 _WaveTexture_ST;
                float4 _FoamTexture_ST;
                float4 _WavePattern_ST;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.uv = input.uv;
                output.distanceToShore = input.distanceToShore;
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // Animated UV for wave movement
                float2 waveUV1 = input.uv * _WaveScale + _Time.y * _WaveSpeed;
                float2 waveUV2 = input.uv * _WaveScale * 0.8 - _Time.y * _WaveSpeed * 0.7;
                
                // Sample wave normals (simplified, not using actual normal mapping here)
                float3 wave1 = SAMPLE_TEXTURE2D(_WaveTexture, sampler_WaveTexture, waveUV1).rgb;
                float3 wave2 = SAMPLE_TEXTURE2D(_WaveTexture, sampler_WaveTexture, waveUV2).rgb;
                float waveHeight = (wave1.b + wave2.b) * 0.5;
                
                // Depth-based color gradient
                float depthFactor = saturate(input.distanceToShore / _DepthGradient);
                float3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthFactor);
                
                // Add wave brightness variation
                waterColor += (waveHeight - 0.5) * 0.1;
                
                // Foam near shores
                float foamFactor = 1.0 - saturate(input.distanceToShore / _FoamDistance);
                if (foamFactor > 0.01)
                {
                    float2 foamUV = input.uv * 20.0 + _Time.y * 0.02;
                    float foam = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV).r;
                    foam *= foamFactor;
                    waterColor = lerp(waterColor, float3(1, 1, 1), foam * 0.7);
                }
                
                // Stylized wave pattern overlay (for artistic look)
                float2 patternUV = TRANSFORM_TEX(input.uv, _WavePattern);
                float pattern = SAMPLE_TEXTURE2D(_WavePattern, sampler_WavePattern, patternUV).r;
                waterColor = lerp(waterColor, waterColor * 0.8, pattern * _PatternOpacity);
                
                return half4(waterColor, 0.95); // Slight transparency
            }
            ENDHLSL
        }
    }
}