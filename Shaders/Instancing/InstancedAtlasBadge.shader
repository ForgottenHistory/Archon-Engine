Shader "Engine/InstancedAtlasBadge"
{
    Properties
    {
        _NumberAtlas ("Numeric Atlas Texture", 2D) = "white" {}
        _BackgroundColor ("Background Color", Color) = (0, 0, 0, 0.7)
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_NumberAtlas);
            SAMPLER(sampler_NumberAtlas);

            CBUFFER_START(UnityPerMaterial)
                float4 _NumberAtlas_ST;
                half4 _BackgroundColor;
            CBUFFER_END

            // Per-instance properties
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _DisplayValue)
                UNITY_DEFINE_INSTANCED_PROP(float, _Scale)
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings vert(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // Billboard: tilt toward camera (X-axis rotation only for top-down view)
                float3 positionWS = TransformObjectToWorld(float3(0, 0, 0));

                // Get scale from per-instance property
                float scale = UNITY_ACCESS_INSTANCED_PROP(Props, _Scale);

                // Get camera direction
                float3 toCamera = GetCameraPositionWS() - positionWS;
                float horizontalDist = length(float2(toCamera.x, toCamera.z));
                float tiltAngle = atan2(toCamera.y, horizontalDist);

                // Rotate around X-axis (tilt up/down)
                float cosAngle = cos(tiltAngle);
                float sinAngle = sin(tiltAngle);

                // Apply scale to local position
                float3 localPos = input.positionOS.xyz * scale;
                float3 tiltedPos;
                tiltedPos.x = localPos.x;
                tiltedPos.y = localPos.y * cosAngle - localPos.z * sinAngle;
                tiltedPos.z = localPos.y * sinAngle + localPos.z * cosAngle;

                float3 billboardedPos = positionWS + tiltedPos;
                output.positionCS = TransformWorldToHClip(billboardedPos);

                // Calculate atlas UV based on display value
                float value = UNITY_ACCESS_INSTANCED_PROP(Props, _DisplayValue);

                // Atlas layout: 10 columns (0-9), 10 rows (0-9, 10-19, ..., 90-99)
                float row = floor(value / 10.0);
                float col = value - (row * 10.0);

                // Map UV to atlas cell (0.1 size per digit)
                output.uv.x = (input.uv.x + col) * 0.1;
                output.uv.y = 1.0 - ((input.uv.y + row) * 0.1); // Flip Y for texture coordinates

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 numberColor = SAMPLE_TEXTURE2D(_NumberAtlas, sampler_NumberAtlas, input.uv);

                // If number alpha is low, show background
                half4 finalColor = lerp(_BackgroundColor, numberColor, numberColor.a);

                return finalColor;
            }
            ENDHLSL
        }
    }
}
