Shader "Archon/MapLabel"
{
    // Map labels (MapLabelRenderer): SDF glyph quads lying on the map, draped on the heightmap.
    // uv1 = (label centre world x, z, font size in map px, world units per map px). Labels smaller
    // than _LabelScreen.x screen px are scaled up about their centre, at most uv2.x times (per label:
    // capped by the room it has, so long names in small countries don't spill over); labels that
    // would need more fade out, and all labels fade out when zoomed in (_LabelFade).
    Properties
    {
        _MainTex ("SDF Atlas (alpha)", 2D) = "black" {}
        _FaceColor ("Face Color", Color) = (0.91, 0.9, 0.95, 1)
        _ShadowColor ("Shadow Color", Color) = (0.04, 0.04, 0.08, 0.75)
        _ShadowParams ("Shadow (uv offset xy, softness, sharpness)", Vector) = (-0.002, 0.002, 0.6, 1)
        _LabelScreen ("Screen (min height px, max scale)", Vector) = (11, 2.5, 0, 0)
        _LabelFade ("Zoom Fade (screen px per map px: visible below x, hidden above y)", Vector) = (1.5, 2.5, 0, 0)
        _HeightmapTexture ("Heightmap", 2D) = "gray" {}
        _HeightScale ("Height Scale", Float) = 10.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-50"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "MapLabel"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_HeightmapTexture);
            SAMPLER(sampler_HeightmapTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _HeightmapTexture_ST;
                float4 _FaceColor;
                float4 _ShadowColor;
                float4 _ShadowParams;
                float4 _LabelScreen;
                float4 _LabelFade;
                float _HeightScale;
                float4 _MapWorldBounds;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;   // world position on the map plane (y ignored)
                float2 uv : TEXCOORD0;          // atlas uv
                float4 anchor : TEXCOORD1;      // label centre x, z; font size (map px); world per map px
                float2 grow : TEXCOORD2;        // x = largest scale-up for this label
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

                float3 center = float3(input.anchor.x, 0, input.anchor.y);
                center.y = TerrainHeight(center);

                // Screen pixels per map pixel at the label centre (same measure as border fades)
                float4 centerCS = TransformWorldToHClip(center);
                float worldPerScreenPx = 2.0 * max(centerCS.w, 1e-3) / (abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);
                float screenPxPerMapPx = input.anchor.w / worldPerScreenPx;

                // Minimum readable size: scale the whole label about its centre
                float screenHeight = input.anchor.z * screenPxPerMapPx;
                float need = _LabelScreen.x / max(screenHeight, 1e-4);
                float cap = max(min(input.grow.x, _LabelScreen.y), 1.0);
                float scale = clamp(need, 1.0, cap);

                float3 posWS = input.positionOS.xyz;
                posWS.xz = center.xz + (posWS.xz - center.xz) * scale;
                posWS.y = TerrainHeight(posWS);

                output.positionCS = TransformWorldToHClip(posWS);
                output.uv = input.uv;

                // Too small even when scaled up: fade out. Zoomed in past the fade range: fade out.
                float sizeFade = 1.0 - smoothstep(cap, cap * 1.3, need);
                float zoomFade = (_LabelFade.y > _LabelFade.x)
                    ? 1.0 - smoothstep(_LabelFade.x, _LabelFade.y, screenPxPerMapPx)
                    : 1.0;
                output.fade = sizeFade * zoomFade;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (input.fade <= 0.001) discard;

                float sdf = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                float aa = max(fwidth(sdf), 1e-4) * 0.75 / _ShadowParams.w;
                float face = smoothstep(0.5 - aa, 0.5 + aa, sdf);

                float shadowSdf = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + _ShadowParams.xy).a;
                float soft = _ShadowParams.z * 0.45;
                float shadow = smoothstep(0.5 - soft - aa, 0.5 + aa, shadowSdf) * _ShadowColor.a;

                // Face over shadow
                float alpha = face * _FaceColor.a + shadow * (1.0 - face * _FaceColor.a);
                float3 rgb = (_FaceColor.rgb * face * _FaceColor.a + _ShadowColor.rgb * shadow * (1.0 - face * _FaceColor.a))
                             / max(alpha, 1e-4);
                return half4(rgb, alpha * input.fade);
            }
            ENDHLSL
        }
    }
}
