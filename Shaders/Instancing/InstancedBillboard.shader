Shader "Engine/InstancedBillboard"
{
    Properties
    {
        _MainTex ("Texture Atlas", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1,1,1,1)
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
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            // Per-instance properties
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _AtlasRect)
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings vert(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                // Billboard: face camera (Y-axis rotation only, stay upright)
                float3 positionWS = TransformObjectToWorld(float3(0, 0, 0));

                // Get camera direction (flatten Y to keep upright)
                float3 cameraForward = normalize(GetCameraPositionWS() - positionWS);
                cameraForward.y = 0;
                cameraForward = normalize(cameraForward);

                float3 up = float3(0, 1, 0);
                float3 right = normalize(cross(up, cameraForward));

                // Reconstruct billboard matrix
                float3 localPos = input.positionOS.x * right + input.positionOS.y * up;
                float3 billboardedPos = positionWS + localPos;

                output.positionCS = TransformWorldToHClip(billboardedPos);

                // Apply atlas rect to UVs for texture atlas support
                float4 atlasRect = UNITY_ACCESS_INSTANCED_PROP(Props, _AtlasRect);
                output.uv = input.uv * atlasRect.zw + atlasRect.xy;

                // Get per-instance color
                output.color = UNITY_ACCESS_INSTANCED_PROP(Props, _InstanceColor);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 finalColor = texColor * input.color * _Color;

                return finalColor;
            }
            ENDHLSL
        }
    }
}
