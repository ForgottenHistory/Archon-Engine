Shader "Archon/MapAtmosphere"
{
    // Map atmosphere (MapAtmosphereRenderer). Two layers of the same shader:
    //   _Layer 0 (ground): haze tint that builds up when zoomed out + cloud shadows
    //   _Layer 1 (clouds): drifting procedural clouds, lifted above the map
    // Zoom is measured as screen px per map px (same as border/label fades); effects are full at
    // or below .x of their zoom range and gone at or above .y. Output is premultiplied alpha.
    //
    // Cloud mask (optional, MapAtmosphereRenderer.SetCloudMask): a map-space texture the GAME paints with where extra
    // clouds gather and their colour (rgb = cloud colour, a = coverage), e.g. weather. Masked clouds use their own,
    // smaller noise (_CloudMask.y map px) and softness (_CloudMaskShape.x), sample the mask through a noise warp
    // (_CloudMaskShape.y map px) so their outline is billowy rather than the mask's shapes, are lighter in their thin
    // parts (wisps), have their own zoom range (_CloudMaskZoom, full at or below .x, gone at or above .y, like the
    // others) and cast a shadow that is darker under dark clouds.
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
        _CloudMask ("Masked clouds (opacity, scale map px, shadow strength)", Vector) = (0.85, 300, 0.5, 0)
        _CloudMaskZoom ("Masked cloud zoom (full, none)", Vector) = (2, 3.5, 0, 0)
        _CloudMaskShape ("Masked cloud shape (softness, warp map px)", Vector) = (0.45, 60, 0, 0)
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
            TEXTURE2D(_ArchonCloudMask);        // global, from MapAtmosphereRenderer.SetCloudMask
            SAMPLER(sampler_ArchonCloudMask);
            float _ArchonCloudMaskOn;

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
                float4 _CloudMask;
                float4 _CloudMaskZoom;
                float4 _CloudMaskShape;
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

            // Cloud noise value (0..1) at a map pixel position, noise tile `scale` map px
            float CloudFieldScaled(float2 mapPx, float scale)
            {
                float t = _Time.y;
                float2 uv = (mapPx - _CloudMotion.xy * t) / scale;
                // Slow domain warp for billowy, evolving shapes
                float2 warp = (Noise(uv * 0.7 + t * 0.002).ga - 0.5) * 0.35;
                float n = 0.55 * Noise(uv + warp).r
                        + 0.30 * Noise(uv * 1.9 + warp * 1.5).a
                        + 0.15 * Noise(uv * 4.3 - t * 0.004).b;
                return n;
            }

            float CloudField(float2 mapPx) { return CloudFieldScaled(mapPx, _CloudParams.w); }

            // Coverage (0..1) and thickness (0..1) from the noise value, at a coverage and edge softness
            float2 CloudDensityAt(float n, float coverage, float softness)
            {
                float threshold = 1.0 - coverage;
                float soft = softness * 0.5;
                float density = smoothstep(threshold - soft, threshold + soft, n);
                float thickness = saturate((n - threshold) / max(softness, 1e-3));
                return float2(density, thickness);
            }

            float2 CloudDensity(float n) { return CloudDensityAt(n, _CloudParams.x, _CloudParams.y); }

            // The cloud mask at a map pixel (y down): rgb = colour, a = coverage (0 when no mask is set)
            float4 CloudMaskAt(float2 mapPx)
            {
                if (_ArchonCloudMaskOn < 0.5) return 0;
                float2 mapSize = _MapWorldBounds.zw / _WorldPerMapPx;
                float2 uv = float2(mapPx.x / mapSize.x, 1.0 - mapPx.y / mapSize.y);
                return SAMPLE_TEXTURE2D_LOD(_ArchonCloudMask, sampler_ArchonCloudMask, uv, 0);
            }

            // Masked clouds at a map pixel: x = density (before opacity), y = thickness; the mask in m.
            // The mask is read through a drifting noise warp, so the cloud outline billows instead of following it.
            float2 MaskedCloud(float2 mapPx, out float4 m)
            {
                float scale = max(_CloudMask.y, 1.0);
                float2 drift = mapPx - _CloudMotion.xy * _Time.y;
                float2 warp = (Noise(drift / (scale * 1.7)).ag - 0.5) * 2.0 * _CloudMaskShape.y;
                m = CloudMaskAt(mapPx + warp);
                if (m.a <= 0.005) return 0;
                float2 d = CloudDensityAt(CloudFieldScaled(mapPx, scale), m.a, max(_CloudMaskShape.x, 1e-3));
                return float2(d.x * saturate(m.a * 4.0), d.y);
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
                float maskFade = ZoomFactor(screenPxPerMapPx, _CloudMaskZoom.xy);
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

                    // Masked cloud shadow, darker under dark clouds
                    if (maskFade > 0.001 && _CloudMask.z > 0)
                    {
                        float4 m;
                        float2 dm = MaskedCloud(mapPx - _CloudShadow.yz, m);
                        float darkness = 1.2 - dot(m.rgb, float3(0.333, 0.333, 0.334));
                        float shade = saturate(dm.x * _CloudMask.z * darkness * maskFade);
                        rgb *= 1.0 - shade;
                        alpha = shade + alpha * (1.0 - shade);
                    }
                }
                else
                {
                    float a = 0;
                    float3 color = 0;
                    if (cloudFade > 0.001)
                    {
                        float2 d = CloudDensity(CloudField(mapPx));
                        a = d.x * _CloudParams.z * cloudFade;
                        color = lerp(_CloudShade.rgb, _CloudColor.rgb, pow(d.y, 0.7));
                    }
                    if (maskFade > 0.001)
                    {
                        float4 m;
                        float2 dm = MaskedCloud(mapPx, m);
                        float am = dm.x * _CloudMask.x * maskFade;
                        // thin parts are wisps toward the cloud shade (light over dark storm clouds, grey under white ones)
                        float3 cm = lerp(lerp(m.rgb, _CloudShade.rgb, 0.55), m.rgb, pow(dm.y, 0.6));
                        // masked clouds over the regular ones ("over" in premultiplied terms)
                        float total = am + a * (1.0 - am);
                        color = (cm * am + color * a * (1.0 - am)) / max(total, 1e-4);
                        a = total;
                    }
                    if (a <= 0.001) discard;
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
