using UnityEngine;
using UnityEngine.Rendering;

namespace Map.Rendering.Atmosphere
{
    /// <summary>
    /// ENGINE: Map atmosphere: haze that builds up as you zoom out, and procedural clouds drifting
    /// above the map with soft shadows on the ground (style: MapAtmosphereStyles, "atmosphere" section).
    ///
    /// Two map-sized quads drawn with Archon/MapAtmosphere over the map and borders, below labels:
    /// a ground layer (haze, cloud shadows) and a cloud layer CloudHeight map px above it, which
    /// gives parallax with a perspective camera. Cloud shapes come from a tileable noise texture
    /// generated at startup.
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
        /// Tileable fractal value noise, one independent field per channel (R: large shapes,
        /// G: billows / warp, B: fine detail, A: second large field).
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
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                pixels[y * size + x] = new Color32(
                    ToByte(Fbm(u, v, 4, 5, 11)),
                    ToByte(Fbm(u, v, 6, 4, 23)),
                    ToByte(Fbm(u, v, 16, 3, 37)),
                    ToByte(Fbm(u, v, 3, 5, 51)));
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        /// <summary>Fractal sum of periodic value noise, normalized to 0..1 and contrast-stretched.</summary>
        private static float Fbm(float u, float v, int baseFreq, int octaves, int seed)
        {
            float sum = 0, amp = 1, norm = 0;
            int freq = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * ValueNoise(u * freq, v * freq, freq, seed + o * 101);
                norm += amp;
                amp *= 0.5f;
                freq *= 2;
            }
            float n = sum / norm;
            return Mathf.Clamp01((n - 0.5f) * 1.6f + 0.5f); // fractal sums bunch around 0.5
        }

        private static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * fx * (fx * (fx * 6 - 15) + 10);
            float sy = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
            float a = Hash(x0, y0, period, seed), b = Hash(x0 + 1, y0, period, seed);
            float c = Hash(x0, y0 + 1, period, seed), d = Hash(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
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
