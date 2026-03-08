using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// ENGINE: Mesh-based border renderer implementation.
    /// Generates actual geometry for borders using GPU-accelerated curve extraction.
    /// Falls back to CPU extraction if GPU compute is unavailable.
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
        private float borderWidth = 0.05f;

        public MeshGeometryBorderRenderer(float width = 0.015f)
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

            // Initialize curve extractor (needed for both GPU and CPU paths, and for force-regenerate)
            curveExtractor = new BorderCurveExtractor(
                textureManager,
                context.AdjacencySystem,
                context.ProvinceSystem,
                provinceMapping
            );

            // Extract border curves: GPU path with disk cache, CPU fallback
            ArchonLogger.Log("MeshGeometryBorderRenderer: Extracting border curves...", "map_rendering");
            var borderCurves = ExtractBorderCurvesWithGPU();

            // Initialize curve cache
            curveCache = new BorderCurveCache();
            curveCache.Initialize(borderCurves);

            // Initialize style updater
            styleUpdater = new BorderStyleUpdater(curveCache, context.ProvinceSystem, context.CountrySystem);
            styleUpdater.UpdateAllBorderStyles();

            // Initialize mesh generator and renderer
            meshGenerator = new BorderMeshGenerator(borderWidth, textureManager.MapWidth, textureManager.MapHeight, context.MapPlaneTransform);
            meshRenderer = new BorderMeshRenderer(context.MapPlaneTransform);

            // Pass world-space map bounds to the shader for heightmap UV derivation
            if (context.MapPlaneTransform != null)
            {
                var mr = context.MapPlaneTransform.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    meshRenderer.SetMapWorldBounds(mr.bounds.min, mr.bounds.size);
                }
            }

            // Pass heightmap so borders follow tessellated terrain
            if (textureManager.HeightmapTexture != null)
            {
                // Read _HeightScale from the map material to match terrain displacement exactly
                float heightScale = 10f;
                if (context.MapPlaneTransform != null)
                {
                    var mr = context.MapPlaneTransform.GetComponent<MeshRenderer>();
                    if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_HeightScale"))
                    {
                        heightScale = mr.sharedMaterial.GetFloat("_HeightScale");
                    }
                }
                meshRenderer.SetHeightmapParams(textureManager.HeightmapTexture, heightScale, 0f);
            }

            ArchonLogger.Log($"MeshGeometryBorderRenderer: Initialized with {borderCurves?.Count ?? 0} border curves", "map_rendering");
        }

        /// <summary>
        /// Extract border curves using GPU-accelerated pipeline with disk caching.
        /// Falls back to CPU extraction if GPU is unavailable.
        /// </summary>
        private Dictionary<(ushort, ushort), List<Vector2>> ExtractBorderCurvesWithGPU()
        {
            // Build cache path from map dimensions (unique per map)
            string cachePath = GetBorderCachePath();

            // 1. Try disk cache first (fastest: ~50ms)
            if (cachePath != null)
            {
                var cacheResult = GPUBorderExtractor.TryLoadCache(cachePath);
                if (cacheResult.IsSuccess)
                {
                    ArchonLogger.Log("MeshGeometryBorderRenderer: Using cached border data (disk cache hit)", "map_rendering");
                    return curveExtractor.ExtractAllBordersFromGPUResult(
                        cacheResult.BorderPixelsByPair, cacheResult.JunctionPixels);
                }
            }

            // 2. Try GPU extraction (fast: ~300ms for 11.5M pixels)
            if (GPUBorderExtractor.IsAvailable && textureManager.ProvinceIDTexture != null)
            {
                ArchonLogger.Log("MeshGeometryBorderRenderer: Using GPU-accelerated border extraction", "map_rendering");
                var gpuResult = GPUBorderExtractor.ExtractBorderPixelsGPU(textureManager.ProvinceIDTexture);

                if (gpuResult.IsSuccess)
                {
                    // Save to disk cache for next time
                    if (cachePath != null)
                    {
                        GPUBorderExtractor.SaveCache(cachePath, gpuResult.BorderPixelsByPair, gpuResult.JunctionPixels);
                    }

                    return curveExtractor.ExtractAllBordersFromGPUResult(
                        gpuResult.BorderPixelsByPair, gpuResult.JunctionPixels);
                }

                ArchonLogger.LogWarning($"MeshGeometryBorderRenderer: GPU extraction failed ({gpuResult.ErrorMessage}), falling back to CPU", "map_rendering");
            }

            // 3. CPU fallback (slow: ~7s for 11.5M pixels)
            ArchonLogger.Log("MeshGeometryBorderRenderer: Using CPU border extraction (fallback)", "map_rendering");
            return curveExtractor.ExtractAllBorders();
        }

        /// <summary>
        /// Get disk cache path for border data.
        /// Stored alongside provinces.png in Template-Data/map/ as provinces.png.borders
        /// </summary>
        private string GetBorderCachePath()
        {
            try
            {
                // Follow same pattern as provinces.png.adjacency
                string mapDir = System.IO.Path.Combine(Application.dataPath, "Archon-Engine", "Template-Data", "map");
                string provincesPath = System.IO.Path.Combine(mapDir, "provinces.png");
                if (System.IO.File.Exists(provincesPath))
                    return provincesPath;

                ArchonLogger.LogWarning("MeshGeometryBorderRenderer: provinces.png not found for cache path", "map_rendering");
                return null;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogWarning($"MeshGeometryBorderRenderer: Could not resolve cache path: {e.Message}", "map_rendering");
                return null;
            }
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
                meshGenerator = new BorderMeshGenerator(borderWidth, textureManager.MapWidth, textureManager.MapHeight, context.MapPlaneTransform);
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
