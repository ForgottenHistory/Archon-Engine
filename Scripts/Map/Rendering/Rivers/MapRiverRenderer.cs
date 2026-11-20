using System.Collections.Generic;
using System.IO;
using Core.Modding;
using Map.Rendering.Border;
using UnityEngine;

namespace Map.Rendering.Rivers
{
    /// <summary>
    /// ENGINE: Renders rivers from map/rivers.png (optional).
    ///
    /// Data: one-pixel-wide river lines, R channel = width level (0 = no river, 1..255 = narrow..wide).
    /// The lines are traced by RiverGraph and drawn as strips with the same mesh pipeline and shader
    /// as mesh borders, styled by the "rivers" section of the visual style data file (line class
    /// "river": texture, width, widthPerLevel, minScreenWidth, tile, color, fade). Rivers draw below
    /// borders.
    /// </summary>
    public class MapRiverRenderer : MonoBehaviour
    {
        public const string RiversFile = "map/rivers.png";
        private const string ClassName = "river";
        private const int RiverQueue = 2890; // below border lines (2900+)

        private BorderMeshGenerator meshGenerator;
        private BorderMeshRenderer meshRenderer;
        private readonly List<string> classNames = new List<string> { ClassName };
        private int appliedStyleVersion = -1;

        public int ArcCount { get; private set; }

        /// <summary>Load and build rivers. No-op (returns false) when the map has no river file.</summary>
        public bool Initialize(MapTextureManager textureManager, Transform mapPlane)
        {
            Dispose();
            if (!DataFileResolver.IsInitialized || textureManager == null)
                return false;

            string path = DataFileResolver.Resolve(RiversFile);
            if (!File.Exists(path))
                return false;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int width = textureManager.MapWidth, height = textureManager.MapHeight;
            byte[] levels = LoadLevels(path, width, height);
            if (levels == null)
                return false;
            long tDecode = sw.ElapsedMilliseconds;

            List<BorderArc> arcs = RiverGraph.Build(levels, width, height, BorderSmoothingSettings.Default);
            ArcCount = arcs.Count;
            long tTrace = sw.ElapsedMilliseconds;

            meshGenerator = new BorderMeshGenerator(width, height, mapPlane);
            var classified = new List<(BorderArc, int)>(arcs.Count);
            foreach (var arc in arcs) classified.Add((arc, 0));
            meshGenerator.GenerateMeshes(classified, classNames);
            long tMesh = sw.ElapsedMilliseconds;

            meshRenderer = new BorderMeshRenderer(RiverQueue);
            var mr = mapPlane != null ? mapPlane.GetComponent<MeshRenderer>() : null;
            if (mr != null)
            {
                meshRenderer.SetMapWorldBounds(mr.bounds.min, mr.bounds.size);
                float heightScale = mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_HeightScale")
                    ? mr.sharedMaterial.GetFloat("_HeightScale") : 10f;
                if (textureManager.HeightmapTexture != null)
                    meshRenderer.SetHeightmapParams(textureManager.HeightmapTexture, heightScale);
            }
            ApplyStyle();

            ArchonLogger.Log($"MapRiverRenderer: {arcs.Count} river arcs from {path} ({sw.ElapsedMilliseconds}ms: " +
                $"decode {tDecode}, trace {tTrace - tDecode} [{RiverGraph.LastTimings}], mesh {tMesh - tTrace}, setup {sw.ElapsedMilliseconds - tMesh})", "map_initialization");
            return true;
        }

        /// <summary>Width levels, top-down (index y * width + x), from the R channel.</summary>
        private static byte[] LoadLevels(string path, int width, int height)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(path)))
                {
                    ArchonLogger.LogError($"MapRiverRenderer: could not decode {path}", "map_initialization");
                    return null;
                }
                if (tex.width != width || tex.height != height)
                {
                    ArchonLogger.LogError($"MapRiverRenderer: {path} is {tex.width}x{tex.height}, map is {width}x{height}", "map_initialization");
                    return null;
                }
                Color32[] pixels = tex.GetPixels32();
                var levels = new byte[width * height];
                // LoadImage rows start at the bottom; flip to top-down to match province pixel space
                for (int y = 0; y < height; y++)
                {
                    int src = (height - 1 - y) * width, dst = y * width;
                    for (int x = 0; x < width; x++)
                        levels[dst + x] = pixels[src + x].r;
                }
                return levels;
            }
            finally
            {
                Destroy(tex);
            }
        }

        private void ApplyStyle()
        {
            if (meshGenerator == null || meshRenderer == null) return;
            meshRenderer.SetMeshes(meshGenerator.Meshes, meshGenerator.MeshClassIds, classNames);
            appliedStyleVersion = BorderClassStyles.Version;
        }

        private void LateUpdate()
        {
            if (meshRenderer == null) return;
            if (appliedStyleVersion != BorderClassStyles.Version)
                ApplyStyle(); // style file reloaded
            meshRenderer.RenderBorders();
        }

        private void Dispose()
        {
            meshGenerator?.Dispose();
            meshRenderer?.Dispose();
            meshGenerator = null;
            meshRenderer = null;
        }

        private void OnDestroy() => Dispose();
    }
}
