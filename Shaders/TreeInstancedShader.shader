Shader "Archon/TreeInstanced"
{
    Properties
    {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma instancing_options procedural:setup
        #pragma target 4.5

        sampler2D _MainTex;
        fixed4 _Color;
        float _Cutoff;

        struct Input
        {
            float2 uv_MainTex;
        };

        #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            StructuredBuffer<float4x4> _TreeMatrices;
        #endif

        void setup()
        {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                unity_ObjectToWorld = _TreeMatrices[unity_InstanceID];
                unity_WorldToObject = unity_ObjectToWorld;
                unity_WorldToObject._14_24_34 *= -1;
                unity_WorldToObject._11_22_33 = 1.0f / unity_WorldToObject._11_22_33;
            #endif
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;

            // Alpha cutout for leaves
            clip(c.a - _Cutoff);
        }
        ENDCG
    }

    FallBack "Diffuse"
}
