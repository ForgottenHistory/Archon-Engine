using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// ENGINE: Mesh-based border renderer.
    /// Builds the border graph (BorderGraph) from the province ID texture once. Each arc gets a
    /// class from the IBorderClassifier (MapRendererRegistry.BorderClassifier); each class is one
    /// mesh, styled by BorderClassStyles (visual style data file, hot reloaded).
    /// Ownership changes only reclassify arcs and rebuild meshes; the graph is static.
    /// </summary>
    public class MeshGeometryBorderRenderer : BorderRendererBase
    {
        public override string RendererId => "MeshGeometry";
        public override string DisplayName => "Mesh Geometry (3D)";
        public override bool RequiresPerFrameUpdate => true;

        private BorderCurveCache curveCache;
        private BorderMeshGenerator meshGenerator;
        private BorderMeshRenderer meshRenderer;
        private BorderSmoothingSettings smoothing = BorderSmoothingSettings.Default;
        private int appliedStyleVersion = -1;

        protected override void OnInitialize()
        {
            if (context.ProvinceSystem == null || textureManager.ProvinceIDTexture == null)
            {
                ArchonLogger.LogWarning("MeshGeometryBorderRenderer: Missing ProvinceSystem or ProvinceIDTexture", "map_rendering");
                return;
            }

            curveCache = new BorderCurveCache();
            BuildGraph();

            meshGenerator = new BorderMeshGenerator(textureManager.MapWidth, textureManager.MapHeight, context.MapPlaneTransform);
            meshRenderer = new BorderMeshRenderer();

            var mr = context.MapPlaneTransform != null ? context.MapPlaneTransform.GetComponent<MeshRenderer>() : null;
            if (mr != null)
                meshRenderer.SetMapWorldBounds(mr.bounds.min, mr.bounds.size);
            if (textureManager.HeightmapTexture != null)
                meshRenderer.SetHeightmapParams(textureManager.HeightmapTexture, GetTerrainHeightScale());

            ArchonLogger.Log($"MeshGeometryBorderRenderer: Initialized with {curveCache.BorderCount} border arcs", "map_rendering");
        }

        /// <summary>Read back province IDs and trace the border graph.</summary>
        private void BuildGraph()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int width = textureManager.MapWidth, height = textureManager.MapHeight;
            ushort[] ids = ReadProvinceIDs(width, height);
            long readbackMs = sw.ElapsedMilliseconds;

            List<BorderArc> arcs = BorderGraph.Build(ids, width, height, smoothing);
            curveCache.Initialize(arcs);

            int points = 0;
            foreach (var arc in arcs) points += arc.Points.Length;
            ArchonLogger.Log($"MeshGeometryBorderRenderer: Border graph {arcs.Count} arcs, {points} points " +
                             $"(readback {readbackMs}ms, trace+smooth {sw.ElapsedMilliseconds - readbackMs}ms)", "map_initialization");
        }

        private ushort[] ReadProvinceIDs(int width, int height)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = textureManager.ProvinceIDTexture;
            var temp = new Texture2D(width, height, TextureFormat.ARGB32, false);
            temp.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            temp.Apply();
            RenderTexture.active = previous;

            Color32[] pixels = temp.GetPixels32();
            Object.Destroy(temp);

            var ids = new ushort[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
                ids[i] = Province.ProvinceIDEncoder.UnpackProvinceID(pixels[i]);
            return ids;
        }

        private float GetTerrainHeightScale()
        {
            var mr = context.MapPlaneTransform != null ? context.MapPlaneTransform.GetComponent<MeshRenderer>() : null;
            if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_HeightScale"))
                return mr.sharedMaterial.GetFloat("_HeightScale");
            return 10f;
        }

        private void Classify()
        {
            var classifier = MapRendererRegistry.Instance != null && MapRendererRegistry.Instance.BorderClassifier != null
                ? MapRendererRegistry.Instance.BorderClassifier
                : new DefaultBorderClassifier();
            var provinces = context.ProvinceSystem;
            curveCache.ClassifyAll(arc => classifier.Classify(arc, provinces));

            var counts = curveCache.CountPerClass();
            var parts = new List<string>();
            for (int i = 0; i < counts.Length; i++) parts.Add($"{curveCache.ClassNames[i]} {counts[i]}");
            ArchonLogger.Log($"MeshGeometryBorderRenderer: Classified arcs - {string.Join(", ", parts)}", "map_rendering");
        }

        public override void GenerateBorders(BorderGenerationParams parameters)
        {
            if (!isInitialized || meshGenerator == null || meshRenderer == null)
            {
                ArchonLogger.LogWarning("MeshGeometryBorderRenderer: Not properly initialized", "map_rendering");
                return;
            }

            if (parameters.Mode == BorderMode.None)
                return;

            if (parameters.ForceRegenerate)
                BuildGraph();

            // Ownership may have changed: reclassify every arc (O(arcs)), then rebuild meshes
            Classify();
            meshGenerator.GenerateBorderMeshes(curveCache);
            ApplyStyles();
        }

        private void ApplyStyles()
        {
            meshRenderer.SetMeshes(meshGenerator.Meshes, meshGenerator.MeshClassIds, curveCache.ClassNames);
            appliedStyleVersion = BorderClassStyles.Version;
        }

        public override void OnRenderFrame()
        {
            if (meshRenderer == null) return;
            // Visual style data file reloaded: restyle without touching geometry
            if (appliedStyleVersion != BorderClassStyles.Version && meshGenerator.Meshes.Count > 0)
                ApplyStyles();
            meshRenderer.RenderBorders();
        }

        public override void Dispose()
        {
            meshGenerator?.Dispose();
            meshRenderer?.Dispose();
            curveCache?.Clear();
            curveCache = null;
            meshGenerator = null;
            meshRenderer = null;
        }
    }
}
