using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// ENGINE: Mesh-based border renderer implementation.
    /// Generates actual geometry for borders using CPU curve extraction.
    ///
    /// Best for 3D map effects where borders need depth/thickness.
    /// Requires per-frame rendering via OnRenderFrame().
    /// </summary>
    public class MeshGeometryBorderRenderer : BorderRendererBase
    {
        public override string RendererId => "MeshGeometry";
        public override string DisplayName => "Mesh Geometry (3D)";
        public override bool RequiresPerFrameUpdate => true;

        private BorderCurveExtractor curveExtractor;
        private BorderCurveCache curveCache;
        private BorderMeshGenerator meshGenerator;
        private BorderMeshRenderer meshRenderer;
        private BorderStyleUpdater styleUpdater;

        private ProvinceMapping provinceMapping;
        private float borderWidth = 1.0f;

        public MeshGeometryBorderRenderer(float width = 1.0f)
        {
            this.borderWidth = width;
        }

        protected override void OnInitialize()
        {
            provinceMapping = context.ProvinceMapping;

            if (context.AdjacencySystem == null || context.ProvinceSystem == null ||
                context.CountrySystem == null || provinceMapping == null)
            {
                ArchonLogger.LogWarning("MeshGeometryBorderRenderer: Missing required systems for curve extraction", "map_rendering");
                return;
            }

            // Initialize curve extractor with province pixel lists
            curveExtractor = new BorderCurveExtractor(
                textureManager,
                context.AdjacencySystem,
                context.ProvinceSystem,
                provinceMapping
            );

            // Extract all border curves (CPU intensive, done once)
            ArchonLogger.Log("MeshGeometryBorderRenderer: Extracting border curves...", "map_rendering");
            var borderCurves = curveExtractor.ExtractAllBorders();

            // Initialize curve cache
            curveCache = new BorderCurveCache();
            curveCache.Initialize(borderCurves);

            // Initialize style updater
            styleUpdater = new BorderStyleUpdater(curveCache, context.ProvinceSystem, context.CountrySystem);
            styleUpdater.UpdateAllBorderStyles();

            // Initialize mesh generator and renderer
            meshGenerator = new BorderMeshGenerator(borderWidth, textureManager.MapWidth, textureManager.MapHeight);
            meshRenderer = new BorderMeshRenderer(context.MapPlaneTransform);

            ArchonLogger.Log($"MeshGeometryBorderRenderer: Initialized with {borderCurves?.Count ?? 0} border curves", "map_rendering");
        }

        public override void GenerateBorders(BorderGenerationParams parameters)
        {
            if (!isInitialized || meshGenerator == null || meshRenderer == null)
            {
                ArchonLogger.LogWarning("MeshGeometryBorderRenderer: Not properly initialized", "map_rendering");
                return;
            }

            if (parameters.Mode == BorderMode.None)
            {
                return;
            }

            if (parameters.ForceRegenerate && curveExtractor != null)
            {
                // Full regeneration: re-extract curves
                var borderCurves = curveExtractor.ExtractAllBorders();
                curveCache.Initialize(borderCurves);
                styleUpdater?.UpdateAllBorderStyles();
            }

            // Generate meshes from curves
            meshGenerator.GenerateBorderMeshes(curveCache);
            meshRenderer.SetMeshes(
                meshGenerator.GetProvinceBorderMeshes(),
                meshGenerator.GetCountryBorderMeshes()
            );

            ArchonLogger.Log("MeshGeometryBorderRenderer: Generated border meshes", "map_rendering");
        }

        public override void OnRenderFrame()
        {
            // Mesh geometry mode requires per-frame rendering
            meshRenderer?.RenderBorders();
        }

        public override void ApplyToMaterial(Material material, BorderStyleParams styleParams)
        {
            if (material == null) return;

            // Set shader mode for mesh geometry
            material.SetInt("_BorderRenderingMode", GetShaderModeValue(RendererId));

            // Apply common style parameters
            ApplyCommonStyleParams(material, styleParams);

            // Mesh geometry doesn't use texture-based borders in the main shader
            // but we still set colors for consistency
        }

        /// <summary>
        /// Update border styles when ownership changes.
        /// </summary>
        public void UpdateBorderStyles()
        {
            styleUpdater?.UpdateAllBorderStyles();
        }

        /// <summary>
        /// Set the border width for mesh generation.
        /// </summary>
        public void SetBorderWidth(float width)
        {
            borderWidth = Mathf.Max(0.1f, width);
            if (meshGenerator != null)
            {
                meshGenerator = new BorderMeshGenerator(borderWidth, textureManager.MapWidth, textureManager.MapHeight);
            }
        }

        public override void Dispose()
        {
            curveCache?.Clear();
            curveExtractor = null;
            curveCache = null;
            meshGenerator = null;
            meshRenderer = null;
            styleUpdater = null;
        }
    }
}
