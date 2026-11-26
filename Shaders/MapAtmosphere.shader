Shader "Archon/MapAtmosphere"
{
    // Map atmosphere (MapAtmosphereRenderer). Two layers of the same shader:
    //   _Layer 0 (ground): haze tint that builds up when zoomed out + cloud shadows
    //   _Layer 1 (clouds): drifting procedural clouds, lifted above the map
    // Zoom is measured as screen px per map px (same as border/label fades); effects are full at
    // or below .x of their zoom range and gone at or above .y. Output is premultiplied alpha.
    // The clouds fade on ONE zoom for the whole screen, the one where the camera looks (the middle of the screen). A
    // tilted perspective camera sees the far edge of the view at a smaller scale than the near edge; fading each pixel
    // on its own scale left clouds at the top of the screen and none at the bottom. (The haze stays per pixel: more
    // of it in the distance is what haze does.)
    //
    // Clouds have volume: the noise is a HEIGHT FIELD over a flat cloud base (a cumulus: flat underneath, domed on
    // top), drawn the way a terrain shader draws a heightmap.
    //   shape    Perlin-Worley noise (_NoiseTex r) above the coverage threshold, raised into a dome, with round
    //            billows (b) on top that also eat the thin edge. CloudTop(): 3 samples. The shape noise is
    //            equalised, so a coverage of 0.25 is a quarter of the sky.
    //   parallax the view ray is marched down through the cloud slab (_CloudVolume.x steps, .z/.w map px thick):
    //            towers stand up, hide what is behind them and shift against each other as the camera moves.
    //            The cloud quad is the slab's top. 0 steps: no parallax (the height straight under the pixel).
    //   light    sun (_CloudSun, the direction the ground shadows come from) on the top surface, whose normal is the
    //            height gradient; self shadow from three height samples toward the sun; less sky light in the
    //            valleys between billows. _CloudShade is the sky (ambient) light, the cloud's colour its albedo.
    //   alpha    as light through smoke: 1 - exp(-thickness / softness). Clouds are never solid: the map shows
    //            through all but the thickest cloud, and they thin out toward their outline (no cut-out edge).
    //   change   a swirling flow stirs the noise (_CloudShape.z map px per second, see MakeField): every part of a
    //            cloud moves, in eddies, with no common direction, and clouds form and dissolve as it goes, instead
    //            of sliding across the sky as solid objects. The wind only carries the regular clouds; masked ones
    //            have their own (_CloudMaskShape.zw, none by default), so weather is stirred in place over its region.
    //   veil     thin cloud around the towers' base (_CloudShape.y), so they don't end in a cut-out edge.
    //   variance a cloud is not one colour (_CloudShape.w): its dense heart is darker, broad patches drift between
    //            lighter and darker and between a cooler and a warmer grey, the wisps are lighter. Full on masked
    //            clouds (rain, storms), less on the regular ones.
    // All noise is read with an explicit mip level (the samples sit in a loop), from the zoom.
    //
    // Cloud mask (optional, MapAtmosphereRenderer.SetCloudMask): a map-space texture the GAME paints with where extra
    // clouds gather and their colour (rgb = cloud albedo, a = coverage), e.g. weather. Masked clouds are the same
    // volumes at their own, smaller noise size (_CloudMask.y map px), thickness and edge softness (_CloudMaskShape.x);
    // the mask is read through a noise warp (_CloudMaskShape.y map px) so their outline is billowy rather than the
    // mask's shapes. They have their own zoom range (_CloudMaskZoom.xy; they come IN over .zw, by default only
    // once the regular clouds are fully gone, so the two are never on screen together) and cast a shadow that is darker under dark clouds.
    // The regular clouds part where the mask has cloud, so the two fields never show through each other.
    //
    // Cloud lights (optional, MapAtmosphereRenderer.SetCloudLights): up to 8 lights inside the clouds, e.g. lightning.
    // Each glows through the cloud around it, brightest where the cloud is thin; the towers stay dark against it.
    Properties
    {
        _NoiseTex ("Noise (tileable, 4 channels)", 2D) = "gray" {}
        _Layer ("Layer (0 ground, 1 clouds)", Float) = 0
        _WorldPerMapPx ("World Units per Map Pixel", Float) = 0.01
        _HazeColor ("Haze Color", Color) = (0.36, 0.42, 0.52, 1)
        _HazeParams ("Haze (max, zoom full, zoom none)", Vector) = (0.3, 0.3, 1.5, 0)
        _CloudColor ("Cloud Color", Color) = (0.95, 0.95, 0.97, 1)
        _CloudShade ("Cloud Shade (sky light)", Color) = (0.6, 0.64, 0.72, 1)
        _CloudParams ("Clouds (coverage, softness, opacity, scale map px)", Vector) = (0.42, 0.22, 0.85, 1400)
        _CloudMotion ("Cloud Wind (map px / s)", Vector) = (5, 1.5, 0, 0)
        _CloudZoom ("Cloud Zoom (full, none)", Vector) = (0.35, 0.9, 0, 0)
        _CloudShadow ("Shadow (strength, offset x, offset y map px)", Vector) = (0.35, -40, 30, 0)
        _CloudMask ("Masked clouds (opacity, scale map px, shadow strength)", Vector) = (0.85, 300, 0.5, 0)
        _CloudMaskZoom ("Masked cloud zoom (full, none; coming in: none, full)", Vector) = (2, 3.5, 0.35, 0.9)
        _CloudMaskShape ("Masked cloud shape (softness, warp map px, wind x, wind y map px / s)", Vector) = (0.45, 60, 0, 0)
        _CloudVolume ("Volume (parallax steps, relief, thickness, masked thickness map px)", Vector) = (10, 0.6, 75, 14)
        _CloudShape ("Shape (billows, veil, churn map px / s, colour variance)", Vector) = (0.5, 0.6, 2, 0.6)
        _CloudSun ("Toward the sun (map east, south, up)", Vector) = (0.5, -0.4, 0.77, 0)
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
            float4 _ArchonCloudLights[8];       // global, from MapAtmosphereRenderer.SetCloudLights: map px x, y (down), radius, intensity
            float4 _ArchonCloudLightColor;
            float _ArchonCloudLightCount;

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                float4 _NoiseTex_TexelSize;
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
                float4 _CloudVolume;
                float4 _CloudShape;
                float4 _CloudSun;
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

            #define CLOUD_DETAIL 1.8        // billow noise frequency x the shape noise
            #define CLOUD_FINE 5.3          // the small billows on the lit surface
            #define CLOUD_FLOW_REACH 0.15   // noise tiles the flow carries a reading before it restarts (more smears)
            #define CLOUD_MAX_STEPS 24

            float4 Noise(float2 uv, float lod)
            {
                return SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, uv, lod);
            }

            // One cloud field: its noise tile (map px), mip level at this zoom, its drift with its wind (uv), and the
            // FLOW that stirs it, all once per pixel and reused by every height sample.
            //
            // The flow is a slow swirling velocity field (two noise channels, broad eddies): every part of a cloud
            // moves, but with no common direction. A flow can't be followed forever (the noise would smear into
            // ribbons), so the shape noise is read twice, each reading carried along the flow for CLOUD_FLOW_REACH
            // tiles and then restarted on a new piece of noise, half a cycle apart; a reading is faded out around its
            // restart while the other covers for it (a flow map). Each place is at its own point of the cycle, so
            // nothing pulses. Clouds are stirred, and form and dissolve as the readings trade.
            struct CloudField
            {
                float scale;
                float lod;
                float2 drift;
                float2 warp;
                float w1;       // weight of the first reading (the second has the rest)
                float2 o1;      // uv offset of the first reading
                float2 o2;      // and of the second
                float2 ob;      // the billows go with whichever reading shows
            };

            CloudField MakeField(float2 mapPx, float scale, float2 wind, float screenPxPerMapPx)
            {
                CloudField f;
                f.scale = scale;
                f.lod = max(0.0, log2(_NoiseTex_TexelSize.z / scale / max(screenPxPerMapPx, 1e-4)));
                f.drift = wind * _Time.y / scale;
                float2 uv = mapPx / scale - f.drift;
                float4 w = Noise(uv * 0.35, f.lod);
                float2 flow = (w.ga - 0.5) * 2.0;
                f.warp = flow * 0.04;
                float cycle = CLOUD_FLOW_REACH * scale / max(_CloudShape.z, 1e-3);   // seconds
                float tt = _Time.y / cycle + w.r;
                float p1 = frac(tt), p2 = frac(tt + 0.5);
                float c1 = floor(tt), c2 = floor(tt + 0.5);
                f.w1 = 1.0 - abs(2.0 * p1 - 1.0);
                float2 carry1 = -flow * (CLOUD_FLOW_REACH * (p1 - 0.5));
                float2 carry2 = -flow * (CLOUD_FLOW_REACH * (p2 - 0.5));
                f.o1 = carry1 + frac(c1 * float2(0.37, 0.71));
                f.o2 = carry2 * 0.83 + frac(c2 * float2(0.53, 0.29)) + float2(0.37, 0.11);
                f.ob = carry1 * f.w1 + carry2 * (1.0 - f.w1);
                return f;
            }

            float2 FieldUV(CloudField f, float2 mapPx)
            {
                return mapPx / f.scale - f.drift + f.warp;
            }

            // The shape noise (0..1, uniform): the two flow readings, blended. A weighted mean of two uniform values is
            // trapezoidal; its CDF makes it uniform again, so a coverage stays a share of the sky at any weights.
            float ShapeNoise(CloudField f, float2 uv)
            {
                float a = Noise(uv + f.o1, f.lod).r;
                float b = Noise(uv * 0.83 + f.o2, f.lod).r;
                float m = a * f.w1 + b * (1.0 - f.w1);
                float lo = max(min(f.w1, 1.0 - f.w1), 1e-3);
                float hi = 1.0 - lo;
                if (m < lo) return m * m / (2.0 * lo * hi);
                if (m < hi) return (2.0 * m - lo) / (2.0 * hi);
                return 1.0 - (1.0 - m) * (1.0 - m) / (2.0 * lo * hi);
            }

            // Cloud top height (0..1 of the slab, over the flat base) at a map pixel, for a coverage (0 clear .. 1 overcast).
            // fine: with the small billows too (one more sample): for the lit surface, not for the march.
            // shape: how far inside the coverage threshold the point is (0 at the cloud's outline .. 1).
            float CloudTopShape(CloudField f, float2 mapPx, float cover, bool fine, out float shape)
            {
                float2 uv = FieldUV(f, mapPx);
                float base = ShapeNoise(f, uv);
                // the billows are stirred with the shapes
                float bill = Noise((uv + f.ob) * CLOUD_DETAIL, f.lod + 0.85).b;
                if (fine) bill = bill * 0.7 + Noise((uv + f.ob) * CLOUD_FINE, f.lod + 2.4).b * 0.3;
                shape = saturate((base - (1.0 - cover)) / max(cover, 1e-3));
                float edge = 1.0 - saturate(shape * 1.4);
                float dome = 1.0 - edge * edge;                  // rises from the edge, flattens into a plateau
                float e = _CloudShape.x;
                // billows: bumps on the dome that also eat the thin edge (a cloud ends in puffs, not in a ramp)
                float h = saturate(dome * (1.0 - e + e * bill) - (1.0 - bill) * e * 0.35);
                return h * saturate((cover - 0.02) * 10.0);      // no cloud pops in where the coverage starts
            }

            float CloudTopDetail(CloudField f, float2 mapPx, float cover, bool fine)
            {
                float shape;
                return CloudTopShape(f, mapPx, cover, fine, shape);
            }

            float CloudTop(CloudField f, float2 mapPx, float cover) { return CloudTopDetail(f, mapPx, cover, false); }

            // The cloud mask at a map pixel (y down): rgb = colour, a = coverage (0 when no mask is set)
            float4 CloudMaskAt(float2 mapPx)
            {
                if (_ArchonCloudMaskOn < 0.5) return 0;
                float2 mapSize = _MapWorldBounds.zw / _WorldPerMapPx;
                float2 uv = float2(mapPx.x / mapSize.x, 1.0 - mapPx.y / mapSize.y);
                return SAMPLE_TEXTURE2D_LOD(_ArchonCloudMask, sampler_ArchonCloudMask, uv, 0);
            }

            // The mask read through a slowly wandering noise warp, so the cloud outline billows instead of following it
            float4 WarpedMask(float2 mapPx)
            {
                float scale = max(_CloudMask.y, 1.0);
                float slide = _CloudShape.z / scale * _Time.y * 0.25;
                float2 warp = (Noise(mapPx / (scale * 1.7) + float2(1.0, -0.7) * slide, 0).ag - 0.5) * 2.0 * _CloudMaskShape.y;
                return CloudMaskAt(mapPx + warp);
            }

            // Effects are full at or below zoom.x, gone at or above zoom.y
            float ZoomFactor(float screenPxPerMapPx, float2 zoom)
            {
                return (zoom.y > zoom.x) ? 1.0 - smoothstep(zoom.x, zoom.y, screenPxPerMapPx) : 1.0;
            }

            // Lights inside the clouds (lightning): what reaches the eye from a surface point at height z
            float3 CloudLights(float2 mapPx, float z)
            {
                float3 sum = 0;
                int count = (int)min(_ArchonCloudLightCount, 8.0);
                for (int i = 0; i < count; i++)
                {
                    float4 l = _ArchonCloudLights[i];
                    float2 d = (mapPx - l.xy) / max(l.z, 1.0);
                    float glow = exp(-dot(d, d) * 2.0) * l.w;
                    sum += glow * (0.35 + 0.65 * exp(-z * 2.2));   // thin cloud over it glows, towers stay dark
                }
                return sum * _ArchonCloudLightColor.rgb;
            }

            // One cloud field as lit volumes. mapPx: where the view ray enters the slab's top; lean: map px the ray
            // travels per map px it descends. Returns premultiplied colour and alpha.
            float4 CloudVolume(CloudField f, float2 mapPx, float2 lean, float cover, float3 albedo,
                               float slab, float softness, float opacity, float screenPxPerMapPx, float variance)
            {
                // parallax: the first step whose ray height is under the cloud top, refined along that step
                int steps = (int)clamp(_CloudVolume.x, 0.0, CLOUD_MAX_STEPS);
                float2 across = lean * slab;
                float sHit = 1.0;
                bool hit = false;
                if (steps < 1)
                {
                    float h0 = CloudTop(f, mapPx, cover);
                    hit = h0 > 0.004;
                    sHit = 1.0 - h0;
                }
                else
                {
                    float prev = 1.0;
                    [loop] for (int i = 0; i <= steps; i++)
                    {
                        float s = i / (float)steps;
                        float h = CloudTop(f, mapPx + across * s, cover);
                        float d = (1.0 - s) - h;
                        if (d <= 0.0 && h > 0.004)
                        {
                            float t = prev / max(prev - d, 1e-5);
                            sHit = saturate(s - (1.0 - t) / steps);
                            hit = true;
                            break;
                        }
                        prev = d;
                    }
                }

                float3 ambient = _CloudShade.rgb;                // sky light
                float3 sunlight = float3(1.0, 0.93, 0.78);
                float3 colour = 0;
                float alpha = 0;
                float z = 0;
                float2 at = mapPx;
                if (hit)
                {
                    at = mapPx + across * sHit;
                    z = 1.0 - sHit;
                    // the top surface's normal from the height gradient (east, south, up)
                    float e = max(f.scale * 0.006, 0.75 / max(screenPxPerMapPx, 1e-4));
                    float k = slab * _CloudVolume.y / (2.0 * e);
                    float dx = (CloudTopDetail(f, at + float2(e, 0), cover, true) - CloudTopDetail(f, at - float2(e, 0), cover, true)) * k;
                    float dy = (CloudTopDetail(f, at + float2(0, e), cover, true) - CloudTopDetail(f, at - float2(0, e), cover, true)) * k;
                    float3 n = normalize(float3(-dx, -dy, 1.0));
                    float3 sun = normalize(_CloudSun.xyz);
                    float diffuse = saturate(dot(n, sun) * 0.5 + 0.5);   // wrapped: clouds scatter, no hard terminator

                    // self shadow: is there cloud between this point and the sun?
                    float2 toSun = sun.xy / max(sun.z, 0.2) * slab;      // map px per slab height
                    float occ = saturate((CloudTop(f, at + toSun * 0.12, cover) - (z + 0.12)) / 0.25)
                              + saturate((CloudTop(f, at + toSun * 0.3, cover) - (z + 0.3)) / 0.25) * 0.8
                              + saturate((CloudTop(f, at + toSun * 0.6, cover) - (z + 0.6)) / 0.25) * 0.6;
                    float sunLit = diffuse * exp(-occ * 1.1);
                    float valley = 0.55 + 0.45 * smoothstep(0.0, 0.7, z);  // the gaps between billows get less sky

                    // colour variance: the dense heart darker, broad patches of lighter and darker, cooler and warmer
                    float inside;
                    CloudTopShape(f, at, cover, false, inside);
                    float4 patches = Noise((FieldUV(f, at) + f.ob) * 2.3 + float2(0.19, 0.43), f.lod + 1.2);
                    float tone = 1.0 + variance * ((patches.g - 0.5) * 0.9 - smoothstep(0.2, 1.0, inside) * 0.45);
                    float hue = (patches.a - 0.5) * variance * 0.16;
                    albedo = saturate(albedo * tone * (1.0 + hue * float3(1.0, 0.1, -1.0)));

                    // dark clouds keep some of the light: the crests of a thunderstorm are lit, not black
                    float lum = dot(albedo, float3(0.333, 0.333, 0.334));
                    colour = (albedo + (1.0 - lum) * 0.05 * sunLit * sunLit) * (ambient * valley + sunlight * sunLit);
                    // smoke: see-through unless thick, and thinning out toward the cloud's outline
                    alpha = (1.0 - exp(-z / max(softness, 1e-3))) * smoothstep(0.0, 0.45, inside) * opacity;
                }

                // a veil around the base: thin cloud wider than the towers
                float veil = 0;
                if (_CloudShape.y > 0.0)
                {
                    float2 uv = FieldUV(f, mapPx);
                    float wide = saturate((ShapeNoise(f, uv) - (1.0 - cover) + 0.14) / 0.3) * saturate((cover - 0.02) * 10.0);
                    float fine = Noise((uv + f.ob) * 6.0, f.lod + 2.6).b;
                    veil = smoothstep(0.0, 1.0, wide) * (0.1 + 0.4 * fine) * _CloudShape.y * opacity;   // wisps well beyond the towers
                }
                float3 veilColour = saturate(albedo * (1.0 + variance * 0.5)) * (ambient * 0.85 + sunlight * 0.3);   // wisps are lighter
                float3 rgb = colour * alpha + veilColour * veil * (1.0 - alpha);
                alpha = alpha + veil * (1.0 - alpha);
                rgb += CloudLights(at, z) * alpha;
                return float4(rgb, alpha);
            }

            // Shadow (0..1) a cloud field casts on the ground at a map pixel: the cloud sits back along the shadow offset.
            // As soft as the cloud is: deeper under thick cloud, fading out toward its outline.
            float CloudShadowAt(CloudField f, float2 mapPx, float cover)
            {
                float inside;
                float h = CloudTopShape(f, mapPx - _CloudShadow.yz, cover, false, inside);
                return smoothstep(0.0, 0.8, h) * smoothstep(0.0, 0.45, inside);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 posWS = input.positionWS;
                float4 cs = TransformWorldToHClip(posWS);
                float worldPerScreenPx = 2.0 * max(cs.w, 1e-3) / (abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);
                float screenPxPerMapPx = _WorldPerMapPx / worldPerScreenPx;

                // Map pixel position (x right, y down)
                float2 mapPx = float2(posWS.x - _MapWorldBounds.x, _MapWorldBounds.y + _MapWorldBounds.w - posWS.z) / _WorldPerMapPx;
                // the zoom where the camera looks: its forward ray down to this layer's height
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                float viewZoom = screenPxPerMapPx;                       // looking along or above the horizon: per pixel
                if (forward.y < -0.05)
                {
                    float depth = (posWS.y - _WorldSpaceCameraPos.y) / forward.y;
                    viewZoom = _WorldPerMapPx * abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y / (2.0 * max(depth, 1e-3));
                }
                float cloudFade = ZoomFactor(viewZoom, _CloudZoom.xy);
                // masked clouds come in over .zw (none at or below z, full at or above w) and go out over .xy
                float maskFade = ZoomFactor(viewZoom, _CloudMaskZoom.xy) * (1.0 - ZoomFactor(viewZoom, _CloudMaskZoom.zw));
                float2 mapUV01 = ArchonWorldToMapUV(posWS, _MapWorldBounds);

                float3 rgb = 0;
                float alpha = 0;
                if (_Layer < 0.5)
                {
                    // Haze
                    float haze = _HazeParams.x * ZoomFactor(screenPxPerMapPx, _HazeParams.yz);
                    rgb = ArchonApplyNight(_HazeColor.rgb, mapUV01, 1.0) * haze;
                    alpha = haze;

                    // the clouds whose shadow lands here; the regular ones part where the mask has cloud
                    float4 m = maskFade > 0.001 ? WarpedMask(mapPx - _CloudShadow.yz) : 0;
                    float bankCover = _CloudParams.x * (1.0 - saturate(m.a * 3.0) * maskFade);

                    // Cloud shadow
                    if (cloudFade > 0.001 && _CloudShadow.x > 0)
                    {
                        CloudField f = MakeField(mapPx - _CloudShadow.yz, _CloudParams.w, _CloudMotion.xy, screenPxPerMapPx);
                        float shade = CloudShadowAt(f, mapPx, bankCover) * _CloudShadow.x * cloudFade;
                        rgb *= 1.0 - shade;
                        alpha = shade + alpha * (1.0 - shade);
                    }

                    // Masked cloud shadow, darker under dark clouds
                    if (maskFade > 0.001 && _CloudMask.z > 0)
                    {
                        if (m.a > 0.02)
                        {
                            CloudField f = MakeField(mapPx - _CloudShadow.yz, max(_CloudMask.y, 1.0), _CloudMaskShape.zw, screenPxPerMapPx);
                            float darkness = 1.2 - dot(m.rgb, float3(0.333, 0.333, 0.334));
                            float shade = saturate(CloudShadowAt(f, mapPx, m.a) * _CloudMask.z * darkness * maskFade);
                            rgb *= 1.0 - shade;
                            alpha = shade + alpha * (1.0 - shade);
                        }
                    }
                }
                else
                {
                    // the view ray: map px travelled (x right, y down) per map px of descent
                    float3 view = normalize(posWS - _WorldSpaceCameraPos);
                    float2 lean = float2(view.x, -view.z) / max(-view.y, 0.25);
                    lean /= max(length(lean) / 0.7, 1.0);        // at most 35 degrees: a flatter ray smears the towers into stripes

                    // the regular clouds part where the mask has cloud
                    float4 m = maskFade > 0.001 ? WarpedMask(mapPx) : 0;
                    float bankCover = _CloudParams.x * (1.0 - saturate(m.a * 3.0) * maskFade);

                    float4 c = 0;
                    if (cloudFade > 0.001 && bankCover > 0.02)
                    {
                        CloudField f = MakeField(mapPx, _CloudParams.w, _CloudMotion.xy, screenPxPerMapPx);
                        c = CloudVolume(f, mapPx, lean, bankCover, _CloudColor.rgb, _CloudVolume.z,
                                        _CloudParams.y, _CloudParams.z * cloudFade, screenPxPerMapPx, _CloudShape.w * 0.4);
                    }
                    if (maskFade > 0.001)
                    {
                        if (m.a > 0.02)
                        {
                            CloudField f = MakeField(mapPx, max(_CloudMask.y, 1.0), _CloudMaskShape.zw, screenPxPerMapPx);
                            float4 cm = CloudVolume(f, mapPx, lean, m.a, m.rgb, _CloudVolume.w,
                                                    _CloudMaskShape.x, _CloudMask.x * maskFade, screenPxPerMapPx, _CloudShape.w);
                            c = cm + c * (1.0 - cm.a);           // masked clouds over the regular ones
                        }
                    }
                    if (c.a <= 0.001) discard;
                    rgb = ArchonApplyNight(c.rgb, mapUV01, 1.0);
                    alpha = c.a;
                }
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
