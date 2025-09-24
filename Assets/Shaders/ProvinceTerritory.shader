Shader "GrandStrategy/ProvinceTerritory"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _CountryColor ("Country Color", Color) = (1,1,1,1)
        _ProvinceVariation ("Province Color Variation", Range(0, 0.1)) = 0.05
        _OverlayTexture ("Overlay Texture (Parchment)", 2D) = "white" {}
        _OverlayStrength ("Overlay Strength", Range(0, 0.5)) = 0.15
        _BorderDistance ("Border Distance", Float) = 0
        _Saturation ("Color Saturation", Range(0, 1)) = 0.7
        _BorderDarkening ("Border Darkening", Range(0, 1)) = 0.15
        _BorderFalloff ("Border Falloff Distance", Float) = 5.0
        _NoiseTexture ("Noise Texture", 2D) = "white" {}
        _Selected ("Is Selected", Float) = 0
        _Hovered ("Is Hovered", Float) = 0
        _SelectionGlow ("Selection Glow Strength", Range(0, 0.5)) = 0.2
        _TerrainHeight ("Terrain Height Influence", Range(0, 1)) = 0.2
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float2 provinceIDData : TEXCOORD2; // For per-province variation (x = normalized ID, y = unused)
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float provinceID : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
            };
            
            TEXTURE2D(_OverlayTexture);
            SAMPLER(sampler_OverlayTexture);
            TEXTURE2D(_NoiseTexture);
            SAMPLER(sampler_NoiseTexture);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _CountryColor;
                float _ProvinceVariation;
                float _OverlayStrength;
                float _Saturation;
                float _BorderDarkening;
                float _BorderFalloff;
                float _Selected;
                float _Hovered;
                float _SelectionGlow;
                float _TerrainHeight;
                float4 _OverlayTexture_ST;
                float4 _NoiseTexture_ST;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                
                output.uv = input.uv;
                output.provinceID = input.provinceIDData.x; // Extract normalized province ID from x component
                
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.normalWS = normalInputs.normalWS;
                
                return output;
            }
            
            float3 DesaturateColor(float3 color, float saturation)
            {
                float gray = dot(color, float3(0.299, 0.587, 0.114));
                return lerp(float3(gray, gray, gray), color, saturation);
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // Use _Color as primary (for MapModes compatibility), _CountryColor as fallback
                float3 baseColor = _Color.rgb;
                
                // Add province-specific color variation using noise
                float2 noiseUV = float2(input.provinceID * 0.1, input.provinceID * 0.1);
                float3 noise = SAMPLE_TEXTURE2D(_NoiseTexture, sampler_NoiseTexture, noiseUV).rgb;
                baseColor = lerp(baseColor, baseColor * (0.9 + noise.r * 0.2), _ProvinceVariation);
                
                // Apply parchment overlay texture
                float2 overlayUV = TRANSFORM_TEX(input.uv, _OverlayTexture) * 10.0;
                float3 overlayColor = SAMPLE_TEXTURE2D(_OverlayTexture, sampler_OverlayTexture, overlayUV).rgb;
                baseColor = lerp(baseColor, baseColor * overlayColor, _OverlayStrength);
                
                // Terrain height influence (darken based on elevation)
                float terrainDarkening = 1.0 - (_TerrainHeight * 0.2);
                baseColor *= terrainDarkening;
                
                // Selection and hover effects
                if (_Selected > 0.5)
                {
                    baseColor *= 1.0 + _SelectionGlow;
                    // Add pulse effect (you'd pass time from script)
                    float pulse = sin(_Time.y * 3.0) * 0.5 + 0.5;
                    baseColor += float3(1, 1, 1) * pulse * 0.05;
                }
                else if (_Hovered > 0.5)
                {
                    baseColor *= 1.1;
                }
                
                // Apply desaturation for that muted Imperator look
                baseColor = DesaturateColor(baseColor, _Saturation);
                
                // Simple lighting
                Light mainLight = GetMainLight();
                float3 lighting = dot(input.normalWS, mainLight.direction) * 0.5 + 0.5;
                baseColor *= lighting;
                
                return half4(baseColor, 1.0);
            }
            ENDHLSL
        }
    }
}