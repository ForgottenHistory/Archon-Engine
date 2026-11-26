using UnityEngine;
using UnityEngine.Rendering;

namespace Map.Rendering.Atmosphere
{
    /// <summary>
    /// ENGINE: Map atmosphere: haze that builds up as you zoom out, and procedural clouds drifting
    /// above the map with soft shadows on the ground (style: MapAtmosphereStyles, "atmosphere" section).
    ///
    /// Two map-sized quads drawn with Archon/MapAtmosphere over the map and borders, below labels:
    /// a ground layer (haze, cloud shadows) and a cloud layer CloudHeight map px above it. The clouds
    /// have volume: the shader treats the noise as a height field over a flat cloud base, marches the
    /// view ray through it (parallax: towers stand up and shift as the camera moves) and lights the
    /// top from the sun, with self shadow. The sun is where the ground shadows come from (ShadowOffset
    /// and CloudHeight). Cloud shapes come from a tileable noise texture generated at startup.
    /// </summary>
    public class MapAtmosphereRenderer : MonoBehaviour
    {
        private const int GroundQueue = 2940; // above border lines (2900+)
        private const int CloudQueue = 2945;  // below labels (2950)
        private const int NoiseSize = 256;

        private static readonly int NoiseTexId = Shader.PropertyToID("_NoiseTex");
        private static readonly int LayerId = Shader.PropertyToID("_Layer");
        private static readonly int MapWorldBoundsId = Shader.PropertyToID("_MapWorldBounds");
        private static readonly int WorldPerMapPxId = Shader.PropertyToID("_WorldPerMapPx");
        private static readonly int HazeColorId = Shader.PropertyToID("_HazeColor");
        private static readonly int HazeParamsId = Shader.PropertyToID("_HazeParams");
        private static readonly int CloudColorId = Shader.PropertyToID("_CloudColor");
        private static readonly int CloudShadeId = Shader.PropertyToID("_CloudShade");
        private static readonly int CloudParamsId = Shader.PropertyToID("_CloudParams");
        private static readonly int CloudMotionId = Shader.PropertyToID("_CloudMotion");
        private static readonly int CloudZoomId = Shader.PropertyToID("_CloudZoom");
        private static readonly int ShadowParamsId = Shader.PropertyToID("_CloudShadow");
        private static readonly int CloudMaskParamsId = Shader.PropertyToID("_CloudMask");
        private static readonly int CloudMaskZoomId = Shader.PropertyToID("_CloudMaskZoom");
        private static readonly int CloudMaskShapeId = Shader.PropertyToID("_CloudMaskShape");
        private static readonly int CloudMaskTexId = Shader.PropertyToID("_ArchonCloudMask");
        private static readonly int CloudMaskOnId = Shader.PropertyToID("_ArchonCloudMaskOn");
        private static readonly int CloudVolumeId = Shader.PropertyToID("_CloudVolume");
        private static readonly int CloudShapeId = Shader.PropertyToID("_CloudShape");
        private static readonly int CloudSunId = Shader.PropertyToID("_CloudSun");
        private static readonly int CloudLightsId = Shader.PropertyToID("_ArchonCloudLights");
        private static readonly int CloudLightColorId = Shader.PropertyToID("_ArchonCloudLightColor");
        private static readonly int CloudLightCountId = Shader.PropertyToID("_ArchonCloudLightCount");

        /// <summary>Masked clouds come in between the zoom where the banks are gone and this many times it.</summary>
        private const float MaskZoomInSpan = 1.6f;

        /// <summary>The most lights SetCloudLights takes.</summary>
        public const int MaxCloudLights = 8;
        private static readonly Vector4[] cloudLights = new Vector4[MaxCloudLights];

        /// <summary>
        /// GAME hook: a map-space texture (row 0 = south, bilinear, any resolution) saying where extra clouds gather:
        /// rgb = cloud colour, a = coverage (0 none .. 1 overcast), e.g. from weather. Null removes it.
        /// Styled by the atmosphere section's maskOpacity, maskScale, maskShadow, maskZoom, maskSoftness and maskWarp.
        /// </summary>
        public static void SetCloudMask(Texture mask)
        {
            Shader.SetGlobalTexture(CloudMaskTexId, mask != null ? mask : Texture2D.blackTexture);
            Shader.SetGlobalFloat(CloudMaskOnId, mask != null ? 1f : 0f);
        }

        /// <summary>
        /// GAME hook: lights inside the clouds, e.g. lightning. Each is (map px x, map px y from the north edge,
        /// radius map px, intensity); the first `count` (at most MaxCloudLights) are used, in one colour. A light
        /// glows through the cloud around it, brightest where the cloud is thin. Call each frame while any is lit;
        /// count 0 turns them off.
        /// </summary>
        public static void SetCloudLights(Vector4[] lights, int count, Color color)
        {
            count = lights == null ? 0 : Mathf.Clamp(count, 0, Mathf.Min(lights.Length, MaxCloudLights));
            for (int i = 0; i < MaxCloudLights; i++) cloudLights[i] = i < count ? lights[i] : Vector4.zero;
            Shader.SetGlobalVectorArray(CloudLightsId, cloudLights);
            Shader.SetGlobalColor(CloudLightColorId, color);
            Shader.SetGlobalFloat(CloudLightCountId, count);
        }

        private Material groundMaterial, cloudMaterial;
        private Mesh groundMesh, cloudMesh;
        private Texture2D noise;
        private Vector3 worldMin, worldSize;
        private float worldPerMapPx;
        private int appliedStyleVersion = -1;

        public bool Initialize(MapTextureManager textureManager, Transform mapPlane)
        {
            Dispose();
            var shader = Shader.Find("Archon/MapAtmosphere");
            if (shader == null)
            {
                ArchonLogger.LogError("MapAtmosphereRenderer: Archon/MapAtmosphere shader not found", "map_rendering");
                return false;
            }
            var mr = mapPlane != null ? mapPlane.GetComponent<MeshRenderer>() : null;
            if (mr == null || textureManager == null) return false;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            worldMin = mr.bounds.min;
            worldSize = mr.bounds.size;
            worldPerMapPx = 0.5f * (worldSize.x / textureManager.MapWidth + worldSize.z / textureManager.MapHeight);

            noise = GenerateNoise(NoiseSize);
            groundMaterial = CreateMaterial(shader, 0, GroundQueue);
            cloudMaterial = CreateMaterial(shader, 1, CloudQueue);
            groundMesh = CreateQuad("MapAtmosphereGround");
            cloudMesh = CreateQuad("MapAtmosphereClouds");
            ApplyStyle();

            ArchonLogger.Log($"MapAtmosphereRenderer: initialized ({sw.ElapsedMilliseconds}ms)", "map_initialization");
            return true;
        }

        private Material CreateMaterial(Shader shader, int layer, int queue)
        {
            var m = new Material(shader) { renderQueue = queue, name = layer == 0 ? "MapAtmosphereGround" : "MapAtmosphereClouds" };
            m.SetTexture(NoiseTexId, noise);
            m.SetFloat(LayerId, layer);
            m.SetVector(MapWorldBoundsId, new Vector4(worldMin.x, worldMin.z, worldSize.x, worldSize.z));
            m.SetFloat(WorldPerMapPxId, worldPerMapPx);
            return m;
        }

        /// <summary>Quad over the map at y = 0 (moved to cloud height by the draw matrix).</summary>
        private Mesh CreateQuad(string name)
        {
            float x0 = worldMin.x, x1 = worldMin.x + worldSize.x;
            float z0 = worldMin.z, z1 = worldMin.z + worldSize.z;
            var m = new Mesh { name = name };
            m.SetVertices(new[] { new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), new Vector3(x1, 0, z0) });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            m.bounds = new Bounds(worldMin + worldSize * 0.5f, worldSize + Vector3.up * 1000f);
            return m;
        }

        private void ApplyStyle()
        {
            appliedStyleVersion = MapAtmosphereStyles.Version;
            var s = MapAtmosphereStyles.Current;
            foreach (var m in new[] { groundMaterial, cloudMaterial })
            {
                if (m == null) continue;
                m.SetColor(HazeColorId, s.HazeColor);
                m.SetVector(HazeParamsId, new Vector4(Mathf.Clamp01(s.HazeMax), s.HazeZoom.x, s.HazeZoom.y, 0));
                m.SetColor(CloudColorId, s.CloudColor);
                m.SetColor(CloudShadeId, s.CloudShade);
                m.SetVector(CloudParamsId, new Vector4(Mathf.Clamp01(s.CloudCoverage), Mathf.Max(s.CloudSoftness, 1e-3f),
                    Mathf.Clamp01(s.CloudOpacity), Mathf.Max(s.CloudScale, 1f)));
                m.SetVector(CloudMotionId, new Vector4(s.CloudWind.x, s.CloudWind.y, 0, 0));
                m.SetVector(CloudZoomId, new Vector4(s.CloudZoom.x, s.CloudZoom.y, 0, 0));
                m.SetVector(ShadowParamsId, new Vector4(Mathf.Clamp01(s.ShadowStrength), s.ShadowOffset.x, s.ShadowOffset.y, 0));
                m.SetVector(CloudMaskParamsId, new Vector4(Mathf.Clamp01(s.MaskOpacity), Mathf.Max(s.MaskScale, 1f), Mathf.Clamp01(s.MaskShadow), 0));
                // by default only once the banks are fully gone (CloudZoom.y), full a little closer in
                var zoomIn = s.MaskZoomIn.x < 0f ? new Vector2(s.CloudZoom.y, s.CloudZoom.y * MaskZoomInSpan) : s.MaskZoomIn;
                // hidden: a range that every zoom is past, so the shader never reaches the masked clouds
                var zoomOut = s.MaskVisible ? s.MaskZoom : new Vector2(-2f, -1f);
                m.SetVector(CloudMaskZoomId, new Vector4(zoomOut.x, zoomOut.y, zoomIn.x, zoomIn.y));
                m.SetVector(CloudMaskShapeId, new Vector4(Mathf.Max(s.MaskSoftness, 1e-3f), Mathf.Max(s.MaskWarp, 0f), s.MaskWind.x, s.MaskWind.y));
                m.SetVector(CloudVolumeId, new Vector4(Mathf.Clamp(s.CloudSteps, 0, 24), Mathf.Max(s.CloudRelief, 0f),
                    Mathf.Max(s.CloudThickness, 0f), Mathf.Max(s.MaskThickness, 0f)));
                m.SetVector(CloudShapeId, new Vector4(Mathf.Clamp01(s.CloudBillows), Mathf.Max(s.CloudVeil, 0f), Mathf.Max(s.CloudChurn, 0f), Mathf.Clamp01(s.CloudVariance)));
                // toward the sun (map east, south, up): a cloud's shadow falls ShadowOffset away from under it
                var sun = new Vector3(-s.ShadowOffset.x, -s.ShadowOffset.y, Mathf.Max(s.CloudHeight, 1f)).normalized;
                m.SetVector(CloudSunId, new Vector4(sun.x, sun.y, sun.z, 0));
            }
        }

        private void LateUpdate()
        {
            if (groundMaterial == null) return;
            if (appliedStyleVersion != MapAtmosphereStyles.Version)
                ApplyStyle();
            var s = MapAtmosphereStyles.Current;
            if (!s.Visible) return;

            Graphics.DrawMesh(groundMesh, Matrix4x4.identity, groundMaterial, 0, null, 0, null, ShadowCastingMode.Off, false);
            var lift = Matrix4x4.Translate(new Vector3(0, s.CloudHeight * worldPerMapPx, 0));
            Graphics.DrawMesh(cloudMesh, lift, cloudMaterial, 0, null, 0, null, ShadowCastingMode.Off, false);
        }

        // ------------------------------------------------------------------ noise

        /// <summary>
        /// Tileable noise, one field per channel. R: Perlin-Worley (Perlin's large shapes with Worley's round
        /// bumps pushed into them): the cloud shapes, equalised so that a coverage is a share of the sky.
        /// B: Worley billows (round bumps with smaller ones on them):
        /// the lumps on a cloud's top and edge. G, A: Perlin: the warps.
        /// </summary>
        private static Texture2D GenerateNoise(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "MapAtmosphereNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2
            };
            float[] perlin = Stretch(Fbm(size, 4, 5, 11, 0.5f, false));
            float[] worley = Stretch(Fbm(size, 4, 3, 23, 0.4f, true));
            var shapes = new float[size * size];
            for (int i = 0; i < shapes.Length; i++)
            {
                float cut = (1f - worley[i]) * 0.6f;
                shapes[i] = (perlin[i] - cut) / (1f - cut + 1e-6f);
            }
            shapes = Equalise(shapes);
            float[] warpA = Stretch(Fbm(size, 3, 4, 37, 0.5f, false));
            float[] billows = Stretch(Fbm(size, 8, 3, 51, 0.55f, true));
            float[] warpB = Stretch(Fbm(size, 3, 4, 67, 0.5f, false));

            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(ToByte(shapes[i]), ToByte(warpA[i]), ToByte(billows[i]), ToByte(warpB[i]));
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        /// <summary>Fractal sum of periodic Perlin or Worley noise, baseFreq cells across, each octave twice as fine.</summary>
        private static float[] Fbm(int size, int baseFreq, int octaves, int seed, float gain, bool cells)
        {
            var sum = new float[size * size];
            float amp = 1f, norm = 0f;
            int freq = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                int s = seed + o * 101;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * freq, v = (y + 0.5f) / size * freq;
                    sum[y * size + x] += amp * (cells ? Worley(u, v, freq, s) : Perlin(u, v, freq, s));
                }
                norm += amp;
                amp *= gain;
                freq *= 2;
            }
            for (int i = 0; i < sum.Length; i++) sum[i] /= norm;
            return sum;
        }

        /// <summary>To 0..1 between the 1st and 99th percentile (fractal sums bunch around their middle).</summary>
        private static float[] Stretch(float[] values)
        {
            var sorted = (float[])values.Clone();
            System.Array.Sort(sorted);
            float lo = sorted[sorted.Length / 100], hi = sorted[sorted.Length - 1 - sorted.Length / 100];
            float range = Mathf.Max(hi - lo, 1e-6f);
            var result = new float[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = Mathf.Clamp01((values[i] - lo) / range);
            return result;
        }

        /// <summary>Rank transform: the values become uniform in 0..1, so a threshold of 1 - c leaves exactly c of the area.</summary>
        private static float[] Equalise(float[] values)
        {
            var order = new int[values.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            var keys = (float[])values.Clone();
            System.Array.Sort(keys, order);
            var result = new float[values.Length];
            for (int rank = 0; rank < order.Length; rank++) result[order[rank]] = rank / (float)(order.Length - 1);
            return result;
        }

        /// <summary>Periodic gradient noise, about -0.7..0.7.</summary>
        private static float Perlin(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * fx * (fx * (fx * 6 - 15) + 10);
            float sy = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
            float a = Mathf.Lerp(Gradient(x0, y0, fx, fy, period, seed), Gradient(x0 + 1, y0, fx - 1, fy, period, seed), sx);
            float b = Mathf.Lerp(Gradient(x0, y0 + 1, fx, fy - 1, period, seed), Gradient(x0 + 1, y0 + 1, fx - 1, fy - 1, period, seed), sx);
            return Mathf.Lerp(a, b, sy);
        }

        private static float Gradient(int ix, int iy, float dx, float dy, int period, int seed)
        {
            float angle = Hash(ix, iy, period, seed) * 2f * Mathf.PI;
            return Mathf.Cos(angle) * dx + Mathf.Sin(angle) * dy;
        }

        /// <summary>Periodic inverted cell noise: 1 at a cell's point, falling off with the squared distance (a
        /// round top, not a cone), 0 one cell away.</summary>
        private static float Worley(float x, float y, int period, int seed)
        {
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float best = 9f;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int ix = cx + i, iy = cy + j;
                float dx = ix + Hash(ix, iy, period, seed) - x;
                float dy = iy + Hash(ix, iy, period, seed + 7919) - y;
                best = Mathf.Min(best, dx * dx + dy * dy);
            }
            return 1f - Mathf.Clamp01(best);
        }

        private static float Hash(int x, int y, int period, int seed)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        private void Dispose()
        {
            if (groundMaterial != null) Destroy(groundMaterial);
            if (cloudMaterial != null) Destroy(cloudMaterial);
            if (groundMesh != null) Destroy(groundMesh);
            if (cloudMesh != null) Destroy(cloudMesh);
            if (noise != null) Destroy(noise);
            groundMaterial = cloudMaterial = null;
            groundMesh = cloudMesh = null;
            noise = null;
        }

        private void OnDestroy() => Dispose();
    }
}
