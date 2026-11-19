Shader "Archon/MapAtmosphere"
{
    // Map atmosphere (MapAtmosphereRenderer). Two layers of the same shader:
    //   _Layer 0 (ground): haze tint that builds up when zoomed out + cloud shadows
    //   _Layer 1 (clouds): drifting procedural clouds, lifted above the map
    // Zoom is measured as screen px per map px (same as border/label fades); effects are full at
    // or below .x of their zoom range and gone at or above .y. Output is premultiplied alpha.
    Properties
    {
        _NoiseTex ("Noise (tileable, 4 channels)", 2D) = "gray" {}
        _Layer ("Layer (0 ground, 1 clouds)", Float) = 0
        _WorldPerMapPx ("World Units per Map Pixel", Float) = 0.01
        _HazeColor ("Haze Color", Color) = (0.36, 0.42, 0.52, 1)
        _HazeParams ("Haze (max, zoom full, zoom none)", Vector) = (0.3, 0.3, 1.5, 0)
        _CloudColor ("Cloud Color", Color) = (0.95, 0.95, 0.97, 1)
        _CloudShade ("Cloud Shade", Color) = (0.6, 0.64, 0.72, 1)
        _CloudParams ("Clouds (coverage, softness, opacity, scale map px)", Vector) = (0.42, 0.22, 0.85, 1400)
        _CloudMotion ("Cloud Wind (map px / s)", Vector) = (5, 1.5, 0, 0)
        _CloudZoom ("Cloud Zoom (full, none)", Vector) = (0.35, 0.9, 0, 0)
        _CloudShadow ("Shadow (strength, offset x, offset y map px)", Vector) = (0.35, -40, 30, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-60"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "MapAtmosphere"

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Includes/DayNight.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                float _Layer;
                float _WorldPerMapPx;
                float4 _HazeColor;
                float4 _HazeParams;
                float4 _CloudColor;
                float4 _CloudShade;
                float4 _CloudParams;
                float4 _CloudMotion;
                float4 _CloudZoom;
                float4 _CloudShadow;
                float4 _MapWorldBounds;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float4 Noise(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv);
            }

            // Cloud noise value (0..1) at a map pixel position
            float CloudField(float2 mapPx)
            {
                float t = _Time.y;
                float2 uv = (mapPx - _CloudMotion.xy * t) / _CloudParams.w;
                // Slow domain warp for billowy, evolving shapes
                float2 warp = (Noise(uv * 0.7 + t * 0.002).ga - 0.5) * 0.35;
                float n = 0.55 * Noise(uv + warp).r
                        + 0.30 * Noise(uv * 1.9 + warp * 1.5).a
                        + 0.15 * Noise(uv * 4.3 - t * 0.004).b;
                return n;
            }

            // Coverage (0..1) and thickness (0..1) from the noise value
            float2 CloudDensity(float n)
            {
                float threshold = 1.0 - _CloudParams.x;
                float soft = _CloudParams.y * 0.5;
                float density = smoothstep(threshold - soft, threshold + soft, n);
                float thickness = saturate((n - threshold) / max(_CloudParams.y, 1e-3));
                return float2(density, thickness);
            }

            // Effects are full at or below zoom.x, gone at or above zoom.y
            float ZoomFactor(float screenPxPerMapPx, float2 zoom)
            {
                return (zoom.y > zoom.x) ? 1.0 - smoothstep(zoom.x, zoom.y, screenPxPerMapPx) : 1.0;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 posWS = input.positionWS;
                float4 cs = TransformWorldToHClip(posWS);
                float worldPerScreenPx = 2.0 * max(cs.w, 1e-3) / (abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);
                float screenPxPerMapPx = _WorldPerMapPx / worldPerScreenPx;

                // Map pixel position (x right, y down)
                float2 mapPx = float2(posWS.x - _MapWorldBounds.x, _MapWorldBounds.y + _MapWorldBounds.w - posWS.z) / _WorldPerMapPx;
                float cloudFade = ZoomFactor(screenPxPerMapPx, _CloudZoom.xy);
                float2 mapUV01 = ArchonWorldToMapUV(posWS, _MapWorldBounds);

                float3 rgb = 0;
                float alpha = 0;
                if (_Layer < 0.5)
                {
                    // Haze
                    float haze = _HazeParams.x * ZoomFactor(screenPxPerMapPx, _HazeParams.yz);
                    rgb = ArchonApplyNight(_HazeColor.rgb, mapUV01, 1.0) * haze;
                    alpha = haze;

                    // Cloud shadow: the cloud above a ground point sits back along the shadow offset
                    if (cloudFade > 0.001 && _CloudShadow.x > 0)
                    {
                        float shade = CloudDensity(CloudField(mapPx - _CloudShadow.yz)).x * _CloudShadow.x * cloudFade;
                        rgb *= 1.0 - shade;
                        alpha = shade + alpha * (1.0 - shade);
                    }
                }
                else
                {
                    if (cloudFade <= 0.001) discard;
                    float2 d = CloudDensity(CloudField(mapPx));
                    float a = d.x * _CloudParams.z * cloudFade;
                    float3 color = lerp(_CloudShade.rgb, _CloudColor.rgb, pow(d.y, 0.7));
                    color = ArchonApplyNight(color, mapUV01, 1.0);
                    rgb = color * a;
                    alpha = a;
                }
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
