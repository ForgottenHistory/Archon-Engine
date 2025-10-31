using UnityEngine;
using UnityEngine.Rendering;
using Core.Systems;
using Map.Rendering.Border;
using ProvinceSystemType = Core.Systems.ProvinceSystem;

namespace Map.Rendering
{
    /// <summary>
    /// Coordinator for border rendering systems
    /// Orchestrates multiple rendering modes: distance field, mesh geometry, and pixel-perfect
    ///
    /// REFACTORED: Now uses specialized helper classes for single responsibility
    /// - BorderShaderManager: Compute shader loading and kernel management
    /// - BorderParameterBinder: Rendering parameters and shader binding
    /// - BorderStyleUpdater: Border style classification and updates
    /// - BorderDebugUtility: Debug utilities and benchmarking
    ///
    /// Architecture: Facade pattern - provides unified interface to border rendering subsystems
    /// </summary>
    public class BorderComputeDispatcher : MonoBehaviour
    {
        [Header("Compute Shaders")]
        [SerializeField] private ComputeShader borderDetectionCompute;
        [SerializeField] private ComputeShader borderCurveRasterizerCompute;
        [SerializeField] private ComputeShader borderSDFCompute;

        [Header("Border Settings")]
        [SerializeField] private BorderMode borderMode = BorderMode.Province;
        [SerializeField] private int countryBorderThickness = 1;
        [SerializeField] private int provinceBorderThickness = 0;
        [SerializeField] private float borderAntiAliasing = 1.0f;
        [SerializeField] private bool autoUpdateBorders = true;

        [Header("Rendering Mode")]
        [SerializeField] private BorderRenderingMode renderingMode = BorderRenderingMode.ShaderDistanceField;
        [SerializeField] private float countryBorderWidth = 0.5f;
        [SerializeField] private float provinceBorderWidth = 0.5f;

        [Header("AAA Distance Field Border Settings")]
        [Tooltip("Sharp border thickness in pixels (0.5 = razor-thin, 2.0 = thick)")]
        [SerializeField] private float edgeWidth = 0.5f;

        [Tooltip("Soft gradient falloff distance in pixels (creates outer glow effect)")]
        [SerializeField] private float gradientWidth = 2.0f;

        [Tooltip("Anti-aliasing smoothness factor (0.1 = crisp, 0.5 = soft)")]
        [SerializeField] private float edgeSmoothness = 0.2f;

        [Tooltip("Edge color darkening multiplier (0.0 = black, 1.0 = province color)")]
        [Range(0f, 1f)]
        [SerializeField] private float edgeColorMul = 0.7f;

        [Tooltip("Gradient color darkening multiplier (0.0 = black, 1.0 = province color)")]
        [Range(0f, 1f)]
        [SerializeField] private float gradientColorMul = 0.85f;

        [Tooltip("Edge layer opacity (0.0 = transparent, 1.0 = opaque)")]
        [Range(0f, 1f)]
        [SerializeField] private float edgeAlpha = 1.0f;

        [Tooltip("Gradient layer opacity inside border (0.0 = transparent, 1.0 = opaque)")]
        [Range(0f, 1f)]
        [SerializeField] private float gradientAlphaInside = 0.5f;

        [Tooltip("Gradient layer opacity outside border (0.0 = transparent, 1.0 = opaque)")]
        [Range(0f, 1f)]
        [SerializeField] private float gradientAlphaOutside = 0.3f;

        public BorderMode CurrentBorderMode => borderMode;

        [Header("Debug")]
        [SerializeField] private bool logPerformance = false;

        // Thread group sizes (must match compute shader)
        private const int THREAD_GROUP_SIZE = 8;

        // Core references
        private MapTextureManager textureManager;
        private BorderDistanceFieldGenerator distanceFieldGenerator;
        private ProvinceSystemType provinceSystem;
        private CountrySystem countrySystem;

        // Smooth curve border system components
        private BorderCurveExtractor curveExtractor;
        private BorderCurveCache curveCache;
        private BorderMeshGenerator meshGenerator;
        private BorderMeshRenderer meshRenderer;
        private bool smoothBordersInitialized = false;

        // Helper classes (extracted for single responsibility)
        private BorderShaderManager shaderManager;
        private BorderParameterBinder parameterBinder;
        private BorderStyleUpdater styleUpdater;
        private BorderDebugUtility debugUtility;

        void Awake()
        {
            // Initialize helper classes
            shaderManager = new BorderShaderManager(logPerformance);
            parameterBinder = new BorderParameterBinder();

            // Debug: Check if compute shaders are assigned
            ArchonLogger.Log($"BorderComputeDispatcher.Awake: borderDetectionCompute={borderDetectionCompute != null}, borderCurveRasterizerCompute={borderCurveRasterizerCompute != null}, borderSDFCompute={borderSDFCompute != null}", "map_initialization");

            // Initialize shaders
            shaderManager.InitializeKernels(borderDetectionCompute, borderCurveRasterizerCompute, borderSDFCompute);

            // Debug: Check if initialization succeeded
            ArchonLogger.Log($"BorderComputeDispatcher.Awake: shaderManager.IsInitialized()={shaderManager.IsInitialized()}", "map_initialization");

            // Sync serialized parameters to parameter binder
            SyncParametersToHelper();
        }

        void Update()
        {
            // Render mesh-based borders every frame if enabled
            if (renderingMode == BorderRenderingMode.MeshGeometry && meshRenderer != null)
            {
                meshRenderer.RenderBorders();
            }
        }

        /// <summary>
        /// Sync Unity serialized fields to parameter binder
        /// Call this after changing parameters in inspector
        /// </summary>
        private void SyncParametersToHelper()
        {
            if (parameterBinder == null) return;

            parameterBinder.BorderMode = borderMode;
            parameterBinder.CountryBorderThickness = countryBorderThickness;
            parameterBinder.ProvinceBorderThickness = provinceBorderThickness;
            parameterBinder.BorderAntiAliasing = borderAntiAliasing;
            parameterBinder.AutoUpdateBorders = autoUpdateBorders;
            parameterBinder.RenderingMode = renderingMode;
            parameterBinder.CountryBorderWidth = countryBorderWidth;
            parameterBinder.ProvinceBorderWidth = provinceBorderWidth;

            parameterBinder.SetDistanceFieldParameters(
                edgeWidth,
                gradientWidth,
                edgeSmoothness,
                edgeColorMul,
                gradientColorMul,
                edgeAlpha,
                gradientAlphaInside,
                gradientAlphaOutside
            );
        }

        /// <summary>
        /// Set the texture manager reference
        /// </summary>
        public void SetTextureManager(MapTextureManager manager)
        {
            textureManager = manager;

            // Initialize debug utility once texture manager is set
            debugUtility = new BorderDebugUtility(textureManager, logPerformance);
        }

        /// <summary>
        /// Initialize smooth curve-based border rendering system
        /// MUST be called after AdjacencySystem, ProvinceSystem, and CountrySystem are ready
        /// </summary>
        public void InitializeSmoothBorders(AdjacencySystem adjacencySystem, ProvinceSystemType provinceSystem, CountrySystem countrySystem, ProvinceMapping provinceMapping, Transform mapPlaneTransform = null)
        {
            if (textureManager == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: Cannot initialize smooth borders - texture manager is null", "map_initialization");
                return;
            }

            if (adjacencySystem == null || provinceSystem == null || countrySystem == null || provinceMapping == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: Cannot initialize smooth borders - missing dependencies", "map_initialization");
                return;
            }

            if (shaderManager.BorderCurveRasterizerShader == null)
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: Border curve rasterizer compute shader not assigned - smooth borders disabled", "map_initialization");
                return;
            }

            float startTime = Time.realtimeSinceStartup;

            // Store systems for border style updates
            this.provinceSystem = provinceSystem;
            this.countrySystem = countrySystem;

            // Initialize curve extractor with province pixel lists
            curveExtractor = new BorderCurveExtractor(textureManager, adjacencySystem, provinceSystem, provinceMapping);

            // Extract all border curves (CPU intensive, done once at startup)
            ArchonLogger.Log("BorderComputeDispatcher: Extracting border curves...", "map_initialization");
            var borderCurves = curveExtractor.ExtractAllBorders();

            // Initialize curve cache to store extracted curves
            curveCache = new BorderCurveCache();
            curveCache.Initialize(borderCurves);

            // Initialize distance field generator (for ShaderDistanceField mode)
            // Note: BorderDistanceFieldGenerator is a MonoBehaviour, should already exist on GameObject
            if (distanceFieldGenerator == null)
            {
                distanceFieldGenerator = GetComponent<BorderDistanceFieldGenerator>();
                if (distanceFieldGenerator == null)
                {
                    ArchonLogger.LogWarning("BorderComputeDispatcher: BorderDistanceFieldGenerator component not found", "map_initialization");
                }
            }

            // Pass texture manager to distance field generator
            if (distanceFieldGenerator != null)
            {
                distanceFieldGenerator.SetTextureManager(textureManager);
            }

            // Initialize style updater
            styleUpdater = new BorderStyleUpdater(curveCache, provinceSystem, countrySystem);

            // Update all border styles (classify as country vs province borders)
            styleUpdater.UpdateAllBorderStyles();

            // Initialize mesh generator and renderer (for MeshGeometry mode)
            meshGenerator = new BorderMeshGenerator(1.0f, textureManager.MapWidth, textureManager.MapHeight);
            meshRenderer = new BorderMeshRenderer(mapPlaneTransform);

            smoothBordersInitialized = true;

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderComputeDispatcher: Smooth borders initialized in {elapsed:F0}ms", "map_initialization");

            // Generate borders based on current rendering mode
            switch (renderingMode)
            {
                case BorderRenderingMode.ShaderDistanceField:
                    if (distanceFieldGenerator != null)
                        distanceFieldGenerator.GenerateDistanceField();
                    break;

                case BorderRenderingMode.MeshGeometry:
                    meshGenerator.GenerateBorderMeshes(curveCache);
                    meshRenderer.SetMeshes(meshGenerator.GetProvinceBorderMeshes(), meshGenerator.GetCountryBorderMeshes());
                    break;

                case BorderRenderingMode.ShaderPixelPerfect:
                    GeneratePixelPerfectBorders();
                    break;
            }
        }

        /// <summary>
        /// Set border rendering mode and regenerate borders
        /// </summary>
        public void SetBorderRenderingMode(BorderRenderingMode mode)
        {
            if (renderingMode == mode)
                return;

            renderingMode = mode;
            parameterBinder.RenderingMode = mode;

            if (!smoothBordersInitialized)
                return;

            // Bind correct border texture to material based on mode
            Material mapMaterial = textureManager?.GetComponent<MeshRenderer>()?.sharedMaterial;
            if (mapMaterial == null)
            {
                // Try to find map plane renderer
                var mapPlane = GameObject.Find("MapPlane");
                if (mapPlane != null)
                {
                    mapMaterial = mapPlane.GetComponent<MeshRenderer>()?.sharedMaterial;
                }
            }

            if (mapMaterial != null && textureManager?.DynamicTextures != null)
            {
                bool usePixelPerfect = (mode == BorderRenderingMode.ShaderPixelPerfect);
                textureManager.DynamicTextures.BindBorderTexture(mapMaterial, usePixelPerfect);
            }

            // Regenerate borders for new mode
            switch (mode)
            {
                case BorderRenderingMode.ShaderDistanceField:
                    if (distanceFieldGenerator != null)
                        distanceFieldGenerator.GenerateDistanceField();
                    break;

                case BorderRenderingMode.MeshGeometry:
                    if (meshGenerator != null && meshRenderer != null)
                    {
                        meshGenerator.GenerateBorderMeshes(curveCache);
                        meshRenderer.SetMeshes(meshGenerator.GetProvinceBorderMeshes(), meshGenerator.GetCountryBorderMeshes());
                    }
                    break;

                case BorderRenderingMode.ShaderPixelPerfect:
                    GeneratePixelPerfectBorders();
                    break;
            }

            ArchonLogger.Log($"BorderComputeDispatcher: Switched to {mode} rendering mode", "map_rendering");
        }

        /// <summary>
        /// Generate pixel-perfect borders using DetectDualBorders kernel
        /// Writes to DualBorderTexture (R=country, G=province)
        /// </summary>
        public void GeneratePixelPerfectBorders()
        {
            ArchonLogger.Log($"BorderComputeDispatcher.GeneratePixelPerfectBorders: Called! shaderManager.IsInitialized()={shaderManager.IsInitialized()}", "map_rendering");

            if (!shaderManager.IsInitialized())
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: Cannot generate pixel-perfect borders - shaders not initialized", "map_rendering");
                return;
            }

            if (textureManager == null || textureManager.DualBorderTexture == null)
            {
                ArchonLogger.LogWarning($"BorderComputeDispatcher: Cannot generate pixel-perfect borders - textureManager={textureManager != null}, DualBorderTexture={textureManager?.DualBorderTexture != null}", "map_rendering");
                return;
            }

            var shader = shaderManager.BorderDetectionShader;
            int kernel = shaderManager.DetectDualBordersKernel;

            // Bind textures
            shader.SetTexture(kernel, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
            shader.SetTexture(kernel, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);
            shader.SetTexture(kernel, "DualBorderTexture", textureManager.DualBorderTexture);

            // Dispatch compute shader
            int threadGroupsX = Mathf.CeilToInt(textureManager.MapWidth / (float)THREAD_GROUP_SIZE);
            int threadGroupsY = Mathf.CeilToInt(textureManager.MapHeight / (float)THREAD_GROUP_SIZE);
            shader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

            if (logPerformance)
            {
                ArchonLogger.Log("BorderComputeDispatcher: Generated pixel-perfect borders", "map_rendering");
            }
        }

        /// <summary>
        /// Detect and update borders (called per-frame or on-demand)
        /// </summary>
        public void DetectBorders()
        {
            // Only generate distance field if using ShaderDistanceField mode
            if (renderingMode == BorderRenderingMode.ShaderDistanceField)
            {
                if (distanceFieldGenerator != null)
                    distanceFieldGenerator.GenerateDistanceField();
            }
            else if (renderingMode == BorderRenderingMode.ShaderPixelPerfect)
            {
                GeneratePixelPerfectBorders();
            }
            // MeshGeometry mode doesn't need per-frame updates
        }

        /// <summary>
        /// Bind distance field border parameters to material
        /// </summary>
        public void BindDistanceFieldBorderParams(Material material)
        {
            SyncParametersToHelper();
            parameterBinder.BindDistanceFieldBorderParams(textureManager, material);
        }

        /// <summary>
        /// Set multiple border parameters at once
        /// </summary>
        public void SetBorderParameters(BorderMode mode, int countryThickness, int provinceThickness, float antiAliasing, bool updateBorders = true)
        {
            borderMode = mode;
            countryBorderThickness = countryThickness;
            provinceBorderThickness = provinceThickness;
            borderAntiAliasing = antiAliasing;

            SyncParametersToHelper();

            if (updateBorders && autoUpdateBorders)
            {
                DetectBorders();
            }
        }

        /// <summary>
        /// Update borders when provinces change
        /// </summary>
        public void UpdateBorders()
        {
            if (autoUpdateBorders)
            {
                DetectBorders();
            }
        }

        /// <summary>
        /// Set border mode
        /// </summary>
        public void SetBorderMode(BorderMode mode)
        {
            borderMode = mode;
            parameterBinder.BorderMode = mode;

            if (autoUpdateBorders)
            {
                DetectBorders();
            }
        }

        /// <summary>
        /// Clear all borders
        /// </summary>
        public void ClearBorders()
        {
            debugUtility?.ClearBorders();
        }

        /// <summary>
        /// Fill borders with white for debugging
        /// </summary>
        public void DebugFillBordersWhite()
        {
            debugUtility?.FillBordersWhite();
        }

        /// <summary>
        /// Toggle border mode for testing
        /// </summary>
        [ContextMenu("Toggle Border Mode")]
        public void ToggleBorderMode()
        {
            borderMode = (BorderMode)(((int)borderMode + 1) % 5);
            SyncParametersToHelper();
            DetectBorders();
            ArchonLogger.Log($"BorderComputeDispatcher: Toggled to border mode: {borderMode}", "map_rendering");
        }

        /// <summary>
        /// Force border mode to province for testing
        /// </summary>
        [ContextMenu("Force Province Mode")]
        public void ForceProvinceMode()
        {
            SetBorderMode(BorderMode.Province);
            ArchonLogger.Log($"BorderComputeDispatcher: Forced border mode to {borderMode}", "map_rendering");
        }

        /// <summary>
        /// Generate debug texture showing curve points
        /// </summary>
        public Texture2D GenerateCurveDebugTexture()
        {
            return debugUtility?.GenerateCurveDebugTexture();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Save curve points debug visualization
        /// </summary>
        [ContextMenu("DEBUG: Save Curve Points Visualization")]
        private void DebugSaveCurvePoints()
        {
            debugUtility?.SaveCurvePointsDebug();
        }

        /// <summary>
        /// Benchmark border detection performance
        /// </summary>
        [ContextMenu("Benchmark Border Detection")]
        private void BenchmarkBorderDetection()
        {
            debugUtility?.BenchmarkBorderDetection((mode) =>
            {
                borderMode = mode;
                SyncParametersToHelper();
                DetectBorders();
            });
        }
#endif

        /// <summary>
        /// Clean up GPU resources
        /// </summary>
        void OnDestroy()
        {
            if (curveCache != null)
            {
                curveCache.Clear();
            }
        }
    }
}
