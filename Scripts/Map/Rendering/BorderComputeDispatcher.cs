using UnityEngine;
using UnityEngine.Rendering;
using Core.Systems;
using ProvinceSystemType = Core.Systems.ProvinceSystem;

namespace Map.Rendering
{
    /// <summary>
    /// Manages GPU compute shader for high-performance border detection.
    /// Processes entire map in parallel to detect province and country borders.
    /// Part of the texture-based map rendering system.
    ///
    /// Now includes smooth curve-based border rendering using pre-computed curves.
    /// </summary>
    public class BorderComputeDispatcher : MonoBehaviour
    {
        [Header("Compute Shaders")]
        [SerializeField] private ComputeShader borderDetectionCompute;
        [SerializeField] private ComputeShader borderCurveRasterizerCompute;

        [Header("Border Settings")]
        [SerializeField] private BorderMode borderMode = BorderMode.Province;
        [SerializeField] private int countryBorderThickness = 1;
        [SerializeField] private int provinceBorderThickness = 0;
        [SerializeField] private float borderAntiAliasing = 1.0f;
        [SerializeField] private bool autoUpdateBorders = true;

        public BorderMode CurrentBorderMode => borderMode;

        [Header("Debug")]
        [SerializeField] private bool logPerformance = false;

        // Kernel indices
        private int detectBordersKernel;
        private int detectBordersThickKernel;
        private int detectCountryBordersKernel;
        private int detectDualBordersKernel;
        private int generateBorderMaskKernel;
        private int copyBorderToMaskKernel;

        // Thread group sizes (must match compute shader)
        private const int THREAD_GROUP_SIZE = 8;

        // References
        private MapTextureManager textureManager;
        private BorderDistanceFieldGenerator distanceFieldGenerator;
        private ProvinceSystemType provinceSystem;
        private CountrySystem countrySystem;

        // Smooth curve border system components
        private BorderCurveExtractor curveExtractor;
        private BorderCurveCache curveCache;
        private BorderCurveRenderer curveRenderer;
        private bool smoothBordersInitialized = false;

        public enum BorderMode
        {
            Province,      // Show all province borders
            Country,       // Show only country/owner borders
            Thick,         // Show thick province borders
            Dual,          // Show BOTH country AND province borders (recommended)
            None           // No borders
        }

        void Awake()
        {
            InitializeKernels();
        }

        /// <summary>
        /// Initialize compute shader kernels
        /// </summary>
        private void InitializeKernels()
        {
            // Load BorderDetection compute shader
            if (borderDetectionCompute == null)
            {
                // Try to find the compute shader in the project
                #if UNITY_EDITOR
                string[] guids = UnityEditor.AssetDatabase.FindAssets("BorderDetection t:ComputeShader");
                if (guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    borderDetectionCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    ArchonLogger.Log($"BorderComputeDispatcher: Found BorderDetection shader at {path}", "map_initialization");
                }
                #endif

                if (borderDetectionCompute == null)
                {
                    ArchonLogger.LogWarning("BorderComputeDispatcher: Border detection compute shader not assigned. Borders will not be generated.", "map_rendering");
                    return;
                }
            }

            // Load BorderCurveRasterizer compute shader (for smooth curves)
            if (borderCurveRasterizerCompute == null)
            {
                #if UNITY_EDITOR
                string[] guids = UnityEditor.AssetDatabase.FindAssets("BorderCurveRasterizer t:ComputeShader");
                if (guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    borderCurveRasterizerCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    ArchonLogger.Log($"BorderComputeDispatcher: Found BorderCurveRasterizer shader at {path}", "map_initialization");
                }
                #endif

                if (borderCurveRasterizerCompute == null)
                {
                    ArchonLogger.LogWarning("BorderComputeDispatcher: BorderCurveRasterizer compute shader not found - smooth borders will not be available", "map_initialization");
                }
            }

            // Get kernel indices for border detection
            detectBordersKernel = borderDetectionCompute.FindKernel("DetectBorders");
            detectBordersThickKernel = borderDetectionCompute.FindKernel("DetectBordersThick");
            detectCountryBordersKernel = borderDetectionCompute.FindKernel("DetectCountryBorders");
            detectDualBordersKernel = borderDetectionCompute.FindKernel("DetectDualBorders");
            generateBorderMaskKernel = borderDetectionCompute.FindKernel("GenerateBorderMask");
            copyBorderToMaskKernel = borderDetectionCompute.FindKernel("CopyBorderToMask");

            if (logPerformance)
            {
                ArchonLogger.Log($"BorderComputeDispatcher: Initialized with kernels - " +
                    $"Borders: {detectBordersKernel}, Thick: {detectBordersThickKernel}, " +
                    $"Country: {detectCountryBordersKernel}, Dual: {detectDualBordersKernel}, Mask: {generateBorderMaskKernel}, CopyMask: {copyBorderToMaskKernel}", "map_initialization");
            }
        }

        /// <summary>
        /// Set the texture manager reference
        /// </summary>
        public void SetTextureManager(MapTextureManager manager)
        {
            textureManager = manager;
        }

        /// <summary>
        /// Initialize smooth curve-based border rendering system
        /// MUST be called after AdjacencySystem, ProvinceSystem, and CountrySystem are ready
        /// </summary>
        public void InitializeSmoothBorders(AdjacencySystem adjacencySystem, ProvinceSystemType provinceSystem, CountrySystem countrySystem, ProvinceMapping provinceMapping)
        {
            if (textureManager == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: Cannot initialize smooth borders - texture manager is null", "map_initialization");
                return;
            }

            if (adjacencySystem == null || provinceSystem == null || countrySystem == null || provinceMapping == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: Cannot initialize smooth borders - missing dependencies (adjacency/province/country/mapping)", "map_initialization");
                return;
            }

            if (borderCurveRasterizerCompute == null)
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: Border curve rasterizer compute shader not assigned - smooth borders disabled", "map_initialization");
                return;
            }

            float startTime = Time.realtimeSinceStartup;

            // Initialize curve extractor with province pixel lists (efficient border extraction!)
            curveExtractor = new BorderCurveExtractor(textureManager, adjacencySystem, provinceSystem, provinceMapping);

            // Extract all border curves (CPU intensive, done once at startup)
            ArchonLogger.Log("BorderComputeDispatcher: Extracting border curves...", "map_initialization");
            var borderCurves = curveExtractor.ExtractAllBorders();

            // Store systems for border style updates
            this.provinceSystem = provinceSystem;
            this.countrySystem = countrySystem;

            // Initialize curve cache
            curveCache = new BorderCurveCache();
            curveCache.Initialize(borderCurves);

            // Update border styles based on province ownership
            ArchonLogger.Log("BorderComputeDispatcher: Classifying borders by ownership...", "map_initialization");
            UpdateAllBorderStyles();

            // Get or create distance field generator for smooth anti-aliasing
            if (distanceFieldGenerator == null)
            {
                distanceFieldGenerator = GetComponent<BorderDistanceFieldGenerator>();
                if (distanceFieldGenerator == null)
                {
                    distanceFieldGenerator = gameObject.AddComponent<BorderDistanceFieldGenerator>();
                }
                distanceFieldGenerator.SetTextureManager(textureManager);
            }

            // Initialize curve renderer with distance field generator
            curveRenderer = new BorderCurveRenderer(borderCurveRasterizerCompute, textureManager, curveCache, distanceFieldGenerator);

            // Upload curve data to GPU
            ArchonLogger.Log("BorderComputeDispatcher: Uploading curve data to GPU...", "map_initialization");
            curveRenderer.UploadCurveData();

            // CRITICAL: GPU synchronization - Wait for curve data upload to complete
            // Following unity-compute-shader-coordination.md pattern
            var syncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.BorderTexture);
            syncRequest.WaitForCompletion();

            smoothBordersInitialized = true;

            float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderComputeDispatcher: Smooth border system initialized in {elapsedMs:F0}ms ({curveCache.BorderCount} curves)", "map_initialization");
        }

        /// <summary>
        /// Generate border mask for sparse shader-based detection
        /// Marks pixels that are within 2-3 pixels of ANY border
        /// This enables resolution-independent borders with minimal per-frame cost
        /// IMPORTANT: Call ONCE at initialization after ProvinceIDTexture is populated
        /// </summary>
        public void GenerateBorderMask()
        {
            if (textureManager == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: Cannot generate border mask - missing dependencies", "map_initialization");
                return;
            }

            if (textureManager.BorderMaskTexture == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: BorderMaskTexture not created!", "map_initialization");
                return;
            }

            float startTime = Time.realtimeSinceStartup;

            // ALWAYS use simple edge detection for BorderMask (curve rasterization is obsolete)
            // Edge detection computes shader marks pixels where ProvinceID changes
            // This is fast, simple, and works perfectly with shader-based border rendering
            {
                // Simple edge detection (4-neighbor check)
                if (borderDetectionCompute == null)
                {
                    ArchonLogger.LogError("BorderComputeDispatcher: Cannot generate border mask - compute shader missing", "map_initialization");
                    return;
                }

                borderDetectionCompute.SetTexture(generateBorderMaskKernel, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
                borderDetectionCompute.SetTexture(generateBorderMaskKernel, "BorderMaskTexture", textureManager.BorderMaskTexture);
                borderDetectionCompute.SetInt("MapWidth", textureManager.MapWidth);
                borderDetectionCompute.SetInt("MapHeight", textureManager.MapHeight);

                int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
                int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

                borderDetectionCompute.Dispatch(generateBorderMaskKernel, threadGroupsX, threadGroupsY, 1);

                var syncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.BorderMaskTexture);
                syncRequest.WaitForCompletion();

                float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                ArchonLogger.Log($"BorderComputeDispatcher: Generated edge-detected BorderMask in {elapsedMs:F1}ms", "map_initialization");
            }
        }

        /// <summary>
        /// Rasterize smooth curves to BorderMaskTexture
        /// Uses the smooth curves already rendered to BorderTexture
        /// Copies BorderTexture data to BorderMaskTexture R channel
        /// </summary>
        private void RasterizeCurvesToMask()
        {
            if (textureManager.BorderTexture == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: Cannot copy borders - BorderTexture is null", "map_rendering");
                return;
            }

            // Use compute shader to copy BorderTexture (smooth curves) to BorderMaskTexture
            // BorderTexture.R = country border distance, BorderTexture.G = province border distance
            // BorderMaskTexture.R = combined border mask (1.0 = border, 0.0 = interior)

            if (borderDetectionCompute == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: Cannot copy borders - compute shader missing", "map_rendering");
                return;
            }

            // Set textures
            // Note: Use "BorderTextureFloat4" name to match kernel's local declaration
            borderDetectionCompute.SetTexture(copyBorderToMaskKernel, "BorderTextureFloat4", textureManager.BorderTexture);
            borderDetectionCompute.SetTexture(copyBorderToMaskKernel, "BorderMaskTexture", textureManager.BorderMaskTexture);
            borderDetectionCompute.SetInt("MapWidth", textureManager.MapWidth);
            borderDetectionCompute.SetInt("MapHeight", textureManager.MapHeight);

            // Dispatch
            int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

            borderDetectionCompute.Dispatch(copyBorderToMaskKernel, threadGroupsX, threadGroupsY, 1);

            // Force GPU sync
            var syncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.BorderMaskTexture);
            syncRequest.WaitForCompletion();

            ArchonLogger.Log($"BorderComputeDispatcher: Copied smooth curves from BorderTexture to BorderMaskTexture", "map_initialization");
        }

        /// <summary>
        /// Dispatch border detection on GPU
        /// Uses smooth curves if available, otherwise falls back to distance field
        /// </summary>
        [ContextMenu("Detect Borders")]
        public void DetectBorders()
        {
            if (textureManager == null)
            {
                textureManager = GetComponent<MapTextureManager>();
                if (textureManager == null)
                {
                    ArchonLogger.LogError("BorderComputeDispatcher: MapTextureManager not found!", "map_rendering");
                    return;
                }
            }

            if (borderMode == BorderMode.None)
            {
                ClearBorders();
                return;
            }

            // Start performance timing
            float startTime = Time.realtimeSinceStartup;

            // Use smooth curve rendering if initialized
            if (smoothBordersInitialized && curveRenderer != null)
            {
                ArchonLogger.Log($"BorderComputeDispatcher: Rasterizing {curveCache.BorderCount} smooth curves to BorderTexture", "map_rendering");

                // Rasterize pre-computed smooth curves
                curveRenderer.RasterizeCurves();

                if (logPerformance)
                {
                    float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                    ArchonLogger.Log($"BorderComputeDispatcher: Smooth curve border rendering completed in {elapsedMs:F2}ms " +
                        $"({curveCache.BorderCount} curves)", "map_rendering");
                }
            }
            else
            {
                // Fallback to distance field approach (legacy)
                // Get or create distance field generator
                if (distanceFieldGenerator == null)
                {
                    distanceFieldGenerator = GetComponent<BorderDistanceFieldGenerator>();
                    if (distanceFieldGenerator == null)
                    {
                        distanceFieldGenerator = gameObject.AddComponent<BorderDistanceFieldGenerator>();
                    }
                    distanceFieldGenerator.SetTextureManager(textureManager);
                }

                // Generate distance field
                distanceFieldGenerator.GenerateDistanceField();

                if (logPerformance)
                {
                    float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                    ArchonLogger.Log($"BorderComputeDispatcher: Distance field border generation (fallback) completed in {elapsedMs:F2}ms " +
                        $"({textureManager.MapWidth}x{textureManager.MapHeight} pixels)", "map_rendering");
                }
            }
        }

        /// <summary>
        /// Debug: Fill entire border texture with white to verify it's working
        /// </summary>
        [ContextMenu("Debug - Fill Borders White")]
        public void DebugFillBordersWhite()
        {
            if (textureManager == null || textureManager.BorderTexture == null)
                return;

            RenderTexture.active = textureManager.BorderTexture;
            GL.Clear(true, true, Color.white);
            RenderTexture.active = null;

            ArchonLogger.Log("BorderComputeDispatcher: Filled border texture with white for debugging", "map_rendering");
        }

        /// <summary>
        /// Clear all borders
        /// </summary>
        public void ClearBorders()
        {
            if (textureManager == null || textureManager.BorderTexture == null)
                return;

            RenderTexture.active = textureManager.BorderTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;

            if (logPerformance)
            {
                ArchonLogger.Log("BorderComputeDispatcher: Borders cleared", "map_rendering");
            }
        }

        /// <summary>
        /// Set border mode and update if auto-update is enabled
        /// </summary>
        public void SetBorderMode(BorderMode mode)
        {
            borderMode = mode;

            if (autoUpdateBorders)
            {
                DetectBorders();
            }
        }

        /// <summary>
        /// Set border thickness for country and province borders
        /// </summary>
        public void SetBorderThickness(int countryThickness, int provinceThickness)
        {
            countryBorderThickness = Mathf.Clamp(countryThickness, 0, 5);
            provinceBorderThickness = Mathf.Clamp(provinceThickness, 0, 5);

            if (autoUpdateBorders)
            {
                DetectBorders();
            }
        }

        /// <summary>
        /// Set border anti-aliasing strength
        /// </summary>
        public void SetBorderAntiAliasing(float antiAliasing)
        {
            borderAntiAliasing = Mathf.Clamp(antiAliasing, 0f, 2f);

            if (autoUpdateBorders)
            {
                DetectBorders();
            }
        }

        /// <summary>
        /// Set multiple border parameters at once without re-rendering each time
        /// Use this instead of calling SetBorderMode/SetBorderThickness/SetBorderAntiAliasing separately
        /// to avoid redundant DetectBorders() calls
        /// </summary>
        public void SetBorderParameters(BorderMode mode, int countryThickness, int provinceThickness, float antiAliasing, bool updateBorders = true)
        {
            borderMode = mode;
            countryBorderThickness = Mathf.Clamp(countryThickness, 0, 5);
            provinceBorderThickness = Mathf.Clamp(provinceThickness, 0, 5);
            borderAntiAliasing = Mathf.Clamp(antiAliasing, 0f, 2f);

            if (updateBorders && autoUpdateBorders)
            {
                DetectBorders();
            }
        }

        /// <summary>
        /// Update borders when provinces change
        /// </summary>
        public void OnProvincesChanged()
        {
            if (autoUpdateBorders)
            {
                DetectBorders();
            }
        }

        /// <summary>
        /// Force enable borders (useful for runtime testing)
        /// </summary>
        [ContextMenu("Force Enable Borders (Province Mode)")]
        public void ForceEnableBorders()
        {
            SetBorderMode(BorderMode.Province);
            ArchonLogger.Log($"BorderComputeDispatcher: Forced border mode to {borderMode}", "map_rendering");
        }

        /// <summary>
        /// Toggle border mode for testing
        /// </summary>
        [ContextMenu("Toggle Border Mode")]
        public void ToggleBorderMode()
        {
            borderMode = (BorderMode)(((int)borderMode + 1) % 5);
            DetectBorders();
            ArchonLogger.Log($"BorderComputeDispatcher: Toggled to border mode: {borderMode}", "map_rendering");
        }

        /// <summary>
        /// Async border detection using CommandBuffer
        /// </summary>
        public void DetectBordersAsync(CommandBuffer cmd)
        {
            if (textureManager == null || borderDetectionCompute == null)
                return;

            int kernelToUse = GetKernelForMode();

            // Set compute shader parameters via command buffer
            cmd.SetComputeTextureParam(borderDetectionCompute, kernelToUse, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
            cmd.SetComputeTextureParam(borderDetectionCompute, kernelToUse, "BorderTexture", textureManager.BorderTexture);
            cmd.SetComputeIntParam(borderDetectionCompute, "MapWidth", textureManager.MapWidth);
            cmd.SetComputeIntParam(borderDetectionCompute, "MapHeight", textureManager.MapHeight);

            // Set border thickness (applies to all modes)
            cmd.SetComputeIntParam(borderDetectionCompute, "CountryBorderThickness", countryBorderThickness);
            cmd.SetComputeIntParam(borderDetectionCompute, "ProvinceBorderThickness", provinceBorderThickness);

            // Set anti-aliasing
            cmd.SetComputeFloatParam(borderDetectionCompute, "BorderAntiAliasing", borderAntiAliasing);

            if (borderMode == BorderMode.Country || borderMode == BorderMode.Dual)
            {
                cmd.SetComputeTextureParam(borderDetectionCompute, kernelToUse, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);
            }

            // Calculate thread groups
            int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

            // Dispatch via command buffer
            cmd.DispatchCompute(borderDetectionCompute, kernelToUse, threadGroupsX, threadGroupsY, 1);
        }

        /// <summary>
        /// Get the appropriate kernel index for current mode
        /// </summary>
        private int GetKernelForMode()
        {
            switch (borderMode)
            {
                case BorderMode.Country:
                    return detectCountryBordersKernel;
                case BorderMode.Thick:
                    return detectBordersThickKernel;
                case BorderMode.Dual:
                    return detectDualBordersKernel;
                default:
                    return detectBordersKernel;
            }
        }

        /// <summary>
        /// DEBUG: Generate a texture showing extracted curve points as colored dots
        /// RED = Country borders, GREEN = Province borders
        /// This shows if the curve extraction algorithm is producing smooth points
        /// </summary>
        public Texture2D GenerateCurveDebugTexture()
        {
            if (curveRenderer == null)
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: Cannot generate curve debug - no renderer", "map_rendering");
                return null;
            }

            return curveRenderer.RenderCurvePointsDebug(textureManager.MapWidth, textureManager.MapHeight);
        }

        /// <summary>
        /// Update all border styles based on current province ownership
        /// Should be called during initialization and when ownership changes
        /// </summary>
        private void UpdateAllBorderStyles()
        {
            if (curveCache == null || provinceSystem == null || countrySystem == null)
                return;

            // Get all unique provinces from province system
            var provinceCount = provinceSystem.ProvinceCount;
            int countryBorders = 0;
            int provinceBorders = 0;

            // Update border styles for each province
            for (ushort provinceID = 1; provinceID < provinceCount; provinceID++)
            {
                var state = provinceSystem.GetProvinceState(provinceID);
                ushort ownerID = state.ownerID;

                curveCache.UpdateProvinceBorderStyles(
                    provinceID,
                    ownerID,
                    (id) => provinceSystem.GetProvinceState(id).ownerID,  // getOwner delegate
                    (id) => (Color)countrySystem.GetCountryColor(id)      // getCountryColor delegate (Color32 -> Color)
                );
            }

            // Count border types for logging
            foreach (var (_, style) in curveCache.GetAllBorderStyles())
            {
                if (style.type == BorderType.Country)
                    countryBorders++;
                else if (style.type == BorderType.Province)
                    provinceBorders++;
            }

            ArchonLogger.Log($"BorderComputeDispatcher: Classified borders - Country: {countryBorders}, Province: {provinceBorders}", "map_initialization");
        }

        /// <summary>
        /// Clean up GPU resources
        /// </summary>
        void OnDestroy()
        {
            if (curveRenderer != null)
            {
                curveRenderer.Dispose();
            }

            if (curveCache != null)
            {
                curveCache.Clear();
            }
        }

#if UNITY_EDITOR
        [ContextMenu("DEBUG: Save Curve Points Visualization")]
        private void DebugSaveCurvePoints()
        {
            var debugTexture = GenerateCurveDebugTexture();
            if (debugTexture == null)
            {
                ArchonLogger.LogError("Failed to generate curve debug texture", "map_rendering");
                return;
            }

            // Save to project root
            var bytes = debugTexture.EncodeToPNG();
            string path = "D:/Stuff/My Games/Hegemon/curve_points_debug.png";
            System.IO.File.WriteAllBytes(path, bytes);

            ArchonLogger.Log($"Saved curve points debug visualization to {path}", "map_rendering");
            ArchonLogger.Log("RED = Country borders, GREEN = Province borders", "map_rendering");
            ArchonLogger.Log("Each dot = one curve point from Chaikin smoothing", "map_rendering");

            Object.DestroyImmediate(debugTexture);
        }

        [ContextMenu("Benchmark Border Detection")]
        private void BenchmarkBorderDetection()
        {
            if (textureManager == null)
            {
                ArchonLogger.LogError("Cannot benchmark without texture manager", "map_rendering");
                return;
            }

            ArchonLogger.Log("=== Border Detection Benchmark ===", "map_rendering");
            ArchonLogger.Log($"Map Size: {textureManager.MapWidth}x{textureManager.MapHeight}", "map_rendering");

            // Test each mode
            var modes = new[] { BorderMode.Province, BorderMode.Country, BorderMode.Thick };
            foreach (var mode in modes)
            {
                borderMode = mode;

                // Warm up
                DetectBorders();

                // Measure
                float totalTime = 0;
                int iterations = 10;

                for (int i = 0; i < iterations; i++)
                {
                    float start = Time.realtimeSinceStartup;
                    DetectBorders();
                    totalTime += (Time.realtimeSinceStartup - start);
                }

                float avgMs = (totalTime / iterations) * 1000f;
                ArchonLogger.Log($"{mode} Mode: {avgMs:F2}ms average ({iterations} iterations)", "map_rendering");
            }

            ArchonLogger.Log("=== Benchmark Complete ===", "map_rendering");
        }
#endif

        // ============================================================================
        // PUBLIC API: Vector Curve Buffer Access
        // ============================================================================

        /// <summary>
        /// Get the Bézier segments buffer for binding to shaders
        /// Returns null if smooth borders not initialized
        /// </summary>
        public ComputeBuffer GetBezierSegmentsBuffer()
        {
            return curveRenderer?.GetBezierSegmentsBuffer();
        }

        /// <summary>
        /// Get the count of Bézier segments
        /// </summary>
        public int GetBezierSegmentCount()
        {
            return curveRenderer?.GetSegmentCount() ?? 0;
        }

        /// <summary>
        /// Check if vector curve rendering is available
        /// </summary>
        public bool IsVectorCurveRenderingAvailable()
        {
            return smoothBordersInitialized && curveRenderer != null && curveRenderer.IsInitialized();
        }

        /// <summary>
        /// Check if spatial grid acceleration is available
        /// </summary>
        public bool IsSpatialGridAvailable()
        {
            return smoothBordersInitialized && curveRenderer != null && curveRenderer.IsSpatialGridInitialized();
        }

        /// <summary>
        /// Get spatial grid parameters (gridWidth, gridHeight, cellSize)
        /// </summary>
        public (int gridWidth, int gridHeight, int cellSize) GetSpatialGridParams()
        {
            return curveRenderer?.GetSpatialGridParams() ?? (0, 0, 0);
        }

        /// <summary>
        /// Get spatial grid cell ranges buffer
        /// </summary>
        public ComputeBuffer GetGridCellRangesBuffer()
        {
            return curveRenderer?.GetGridCellRangesBuffer();
        }

        /// <summary>
        /// Get spatial grid segment indices buffer
        /// </summary>
        public ComputeBuffer GetGridSegmentIndicesBuffer()
        {
            return curveRenderer?.GetGridSegmentIndicesBuffer();
        }
    }
}