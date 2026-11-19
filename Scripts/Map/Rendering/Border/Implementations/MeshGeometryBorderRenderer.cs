using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// ENGINE: Mesh-based border renderer.
    /// Builds the border graph (BorderGraph) from the province ID texture once, then renders
    /// each arc as a triangle strip. Ownership changes only restyle arcs and rebuild meshes;
    /// the graph itself is static.
    /// </summary>
    public class MeshGeometryBorderRenderer : BorderRendererBase
    {
        public override string RendererId => "MeshGeometry";
        public override string DisplayName => "Mesh Geometry (3D)";
        public override bool RequiresPerFrameUpdate => true;

        private BorderCurveCache curveCache;
        private BorderMeshGenerator meshGenerator;
        private BorderMeshRenderer meshRenderer;
        private BorderStyleUpdater styleUpdater;

        // Full province border width in map pixels (country borders use BorderStyle.thickness x this)
        private float borderWidth;
        private BorderSmoothingSettings smoothing = BorderSmoothingSettings.Default;

        public MeshGeometryBorderRenderer(float widthPixels = 0.6f)
        {
            borderWidth = widthPixels;
        }

        protected override void OnInitialize()
        {
            if (context.ProvinceSystem == null || textureManager.ProvinceIDTexture == null)
            {
                ArchonLogger.LogWarning("MeshGeometryBorderRenderer: Missing ProvinceSystem or ProvinceIDTexture", "map_rendering");
                return;
            }

            curveCache = new BorderCurveCache();
            BuildGraph();

            styleUpdater = new BorderStyleUpdater(curveCache, context.ProvinceSystem, context.CountrySystem);
            styleUpdater.UpdateAllBorderStyles();

            float heightScale = GetTerrainHeightScale();
            meshGenerator = CreateMeshGenerator(heightScale);
            meshRenderer = new BorderMeshRenderer(context.MapPlaneTransform);

            if (context.MapPlaneTransform != null)
            {
                var mr = context.MapPlaneTransform.GetComponent<MeshRenderer>();
                if (mr != null)
                    meshRenderer.SetMapWorldBounds(mr.bounds.min, mr.bounds.size);
            }
            if (textureManager.HeightmapTexture != null)
                meshRenderer.SetHeightmapParams(textureManager.HeightmapTexture, heightScale, 0.05f);

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
            if (context.MapPlaneTransform != null)
            {
                var mr = context.MapPlaneTransform.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_HeightScale"))
                    return mr.sharedMaterial.GetFloat("_HeightScale");
            }
            return 10f;
        }

        private BorderMeshGenerator CreateMeshGenerator(float heightScale)
        {
            return new BorderMeshGenerator(borderWidth, textureManager.MapWidth, textureManager.MapHeight,
                context.MapPlaneTransform, textureManager.HeightmapTexture, heightScale);
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

            // Ownership may have changed: restyle every arc (O(arcs)), then rebuild meshes
            styleUpdater?.UpdateAllBorderStyles();
            meshGenerator.GenerateBorderMeshes(curveCache);
            meshRenderer.SetMeshes(
                meshGenerator.GetProvinceBorderMeshes(),
                meshGenerator.GetCountryBorderMeshes()
            );
        }

        public override void OnRenderFrame()
        {
            meshRenderer?.RenderBorders();
        }

        /// <summary>Update border styles when ownership changes (call GenerateBorders to rebuild meshes).</summary>
        public void UpdateBorderStyles()
        {
            styleUpdater?.UpdateAllBorderStyles();
        }

        /// <summary>Set the province border width in map pixels.</summary>
        public void SetBorderWidth(float widthPixels)
        {
            borderWidth = Mathf.Max(0.05f, widthPixels);
            if (meshGenerator != null)
                meshGenerator = CreateMeshGenerator(GetTerrainHeightScale());
        }

        public override void Dispose()
        {
            meshGenerator?.Dispose();
            meshRenderer?.Dispose();
            curveCache?.Clear();
            curveCache = null;
            meshGenerator = null;
            meshRenderer = null;
            styleUpdater = null;
        }
    }
}
