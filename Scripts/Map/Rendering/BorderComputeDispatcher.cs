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
        [SerializeField] private ComputeShader borderSDFCompute;

        [Header("Border Settings")]
        [SerializeField] private BorderMode borderMode = BorderMode.Province;
        [SerializeField] private int countryBorderThickness = 1;
        [SerializeField] private int provinceBorderThickness = 0;
        [SerializeField] private float borderAntiAliasing = 1.0f;
        [SerializeField] private bool autoUpdateBorders = true;

        [Header("Rendering Mode")]
        [SerializeField] private BorderRenderingMode renderingMode = BorderRenderingMode.DistanceField;
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

        public enum BorderRenderingMode
        {
            None,                   // No border rendering at all
            ShaderDistanceField,    // Shader-based using JFA distance field (smooth, 3D tessellation compatible)
            MeshGeometry,           // CPU triangle strip geometry (resolution-independent, runtime style updates)
            ShaderPixelPerfect,     // Shader-based using 1-pixel BorderMask (retro aesthetic, planned)

            // Legacy/deprecated modes (kept for backwards compatibility)
            [System.Obsolete("Use ShaderDistanceField instead")]
            SDF = ShaderDistanceField,      // Old name: signed distance field
            [System.Obsolete("Use ShaderDistanceField instead")]
            DistanceField = ShaderDistanceField,  // Old name: AAA distance field
            [System.Obsolete("Use ShaderPixelPerfect instead")]
            Rasterization = ShaderPixelPerfect,   // Old name: not implemented yet
            [System.Obsolete("Use MeshGeometry instead")]
            Mesh = MeshGeometry             // Old name: mesh rendering
        }

        public BorderMode CurrentBorderMode => borderMode;

        [Header("Debug")]
        [SerializeField] private bool logPerformance = false;

        // Kernel indices (only actually used kernel)
        private int detectDualBordersKernel;

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
        private BorderMeshGenerator meshGenerator;
        private BorderMeshRenderer meshRenderer;
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

        void Update()
        {
            // Render mesh-based borders every frame if enabled
            if (renderingMode == BorderRenderingMode.Mesh && meshRenderer != null)
            {
                meshRenderer.RenderBorders();
            }
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
                    ArchonLogger.LogWarning("BorderComputeDispatcher: BorderCurveRasterizer compute shader not found - rasterization rendering will not be available", "map_initialization");
                }
            }

            // Load BorderSDF compute shader (for resolution-independent SDF rendering)
            if (borderSDFCompute == null)
            {
                #if UNITY_EDITOR
                string[] guids = UnityEditor.AssetDatabase.FindAssets("BorderSDF t:ComputeShader");
                if (guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    borderSDFCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    ArchonLogger.Log($"BorderComputeDispatcher: Found BorderSDF shader at {path}", "map_initialization");
                }
                #endif

                if (borderSDFCompute == null)
                {
                    ArchonLogger.LogWarning("BorderComputeDispatcher: BorderSDF compute shader not found - SDF rendering will not be available", "map_initialization");
                }
            }

            // Get kernel index for dual border detection (only used kernel)
            detectDualBordersKernel = borderDetectionCompute.FindKernel("DetectDualBorders");

            if (logPerformance)
            {
                ArchonLogger.Log($"BorderComputeDispatcher: Initialized with DetectDualBorders kernel (ID: {detectDualBordersKernel})", "map_initialization");
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
        public void InitializeSmoothBorders(AdjacencySystem adjacencySystem, ProvinceSystemType provinceSystem, CountrySystem countrySystem, ProvinceMapping provinceMapping, Transform mapPlaneTransform = null)
        {
            // Re-enabled: Bézier curves with junction detection
            // BorderMask marks junctions (0.66) so fragment shader can skip curve evaluation there
            // This combines vector curve smoothness with proper junction handling
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

            // Spatial grid removed - was only used by deleted BorderCurveRenderer

            // Choose rendering method based on mode
            if (renderingMode == BorderRenderingMode.None)
            {
                ArchonLogger.Log("BorderComputeDispatcher: Border rendering mode is None - no borders will be rendered", "map_initialization");
                // Don't initialize any border rendering systems
            }
            else if (renderingMode == BorderRenderingMode.ShaderPixelPerfect)
            {
                ArchonLogger.Log("BorderComputeDispatcher: Using shader-based pixel-perfect rendering (1-pixel DualBorder)", "map_initialization");

                // Pixel-perfect mode uses DualBorder texture for sharp 1-pixel borders
                // Clear DistanceField so shader knows to use pixel-perfect mode
                RenderTexture.active = textureManager.DistanceFieldTexture;
                GL.Clear(true, true, Color.black);
                RenderTexture.active = null;
                ArchonLogger.Log("BorderComputeDispatcher: Cleared DistanceFieldTexture for pixel-perfect mode", "map_initialization");
            }
            else if (renderingMode == BorderRenderingMode.ShaderDistanceField)
            {
                ArchonLogger.Log("BorderComputeDispatcher: Using shader-based distance field rendering (JFA, smooth borders)", "map_initialization");

                // Distance Field mode is fragment-shader based - no C# renderer needed!
                // All rendering happens in MapModeCommon.hlsl's ApplyBorders() function
                // We just need to generate the distance field texture and bind parameters

                // Distance field generation happens below
            }
            else if (renderingMode == BorderRenderingMode.MeshGeometry)
            {
                ArchonLogger.Log("BorderComputeDispatcher: Using mesh-based rendering (triangle strips - Paradox approach)", "map_initialization");

                // Initialize mesh generator with Paradox's border width (0.0002 world units)
                float borderWidthWorldUnits = 0.0006f;
                meshGenerator = new BorderMeshGenerator(borderWidthWorldUnits, textureManager.MapWidth, textureManager.MapHeight);

                // Generate triangle strip meshes from border curves
                ArchonLogger.Log("BorderComputeDispatcher: Generating triangle strip meshes...", "map_initialization");
                meshGenerator.GenerateBorderMeshes(curveCache);

                // Initialize mesh renderer with map plane transform
                // Use the mapPlaneTransform parameter passed to InitializeSmoothBorders
                meshRenderer = new BorderMeshRenderer(mapPlaneTransform);

                // Set meshes for rendering
                var provinceMeshes = meshGenerator.GetProvinceBorderMeshes();
                var countryMeshes = meshGenerator.GetCountryBorderMeshes();
                meshRenderer.SetMeshes(provinceMeshes, countryMeshes);

                ArchonLogger.Log($"BorderComputeDispatcher: Mesh rendering initialized - Province: {provinceMeshes.Count} meshes, Country: {countryMeshes.Count} meshes", "map_initialization");

                // CRITICAL: Disable shader-based border rendering when mesh mode is active
                // Set border strength to 0 so ApplyBorders() in shader doesn't render duplicate borders
                if (mapPlaneTransform != null)
                {
                    var mapMeshRenderer = mapPlaneTransform.GetComponent<MeshRenderer>();
                    if (mapMeshRenderer != null && mapMeshRenderer.material != null)
                    {
                        // Use .material (runtime instance) not .sharedMaterial (asset)
                        mapMeshRenderer.material.SetFloat("_CountryBorderStrength", 0f);
                        mapMeshRenderer.material.SetFloat("_ProvinceBorderStrength", 0f);
                        ArchonLogger.Log("BorderComputeDispatcher: Disabled shader-based borders (mesh rendering active)", "map_initialization");
                    }
                    else
                    {
                        ArchonLogger.LogWarning("BorderComputeDispatcher: Could not disable shader borders - MeshRenderer or material not found", "map_initialization");
                    }
                }
            }

            // Generate JFA distance field (for ShaderDistanceField mode)
            if (renderingMode == BorderRenderingMode.ShaderDistanceField)
            {
                if (distanceFieldGenerator == null)
                {
                    distanceFieldGenerator = GetComponent<BorderDistanceFieldGenerator>();
                    if (distanceFieldGenerator == null)
                    {
                        distanceFieldGenerator = gameObject.AddComponent<BorderDistanceFieldGenerator>();
                    }
                    distanceFieldGenerator.SetTextureManager(textureManager);
                }

                // Generate full-resolution distance field into DistanceFieldTexture
                ArchonLogger.Log("BorderComputeDispatcher: Generating FULL RESOLUTION distance field...", "map_initialization");
                float distFieldStartTime = Time.realtimeSinceStartup;
                distanceFieldGenerator.GenerateDistanceField();
                float distFieldElapsedMs = (Time.realtimeSinceStartup - distFieldStartTime) * 1000f;
                ArchonLogger.Log($"BorderComputeDispatcher: Full resolution distance field generation complete in {distFieldElapsedMs:F1}ms", "map_initialization");
            } // End of DistanceField mode block

            // CRITICAL: GPU synchronization - Wait for curve data upload to complete
            // Following unity-compute-shader-coordination.md pattern
            var syncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.DistanceFieldTexture);
            syncRequest.WaitForCompletion();

            smoothBordersInitialized = true;

            float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderComputeDispatcher: Smooth border system initialized in {elapsedMs:F0}ms ({curveCache.BorderCount} curves)", "map_initialization");
        }

        /// <summary>
        /// Generate border mask for sparse shader-based detection with DUAL borders
        /// R channel = Country borders (different owners)
        /// G channel = Province borders (same owner, different province)
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

            if (textureManager.DualBorderTexture == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: DualBorderTexture not created!", "map_initialization");
                return;
            }

            float startTime = Time.realtimeSinceStartup;

            // Use DetectDualBorders kernel for pixel-perfect mode
            // This separates country borders (R) from province borders (G)
            if (borderDetectionCompute == null)
            {
                ArchonLogger.LogError("BorderComputeDispatcher: Cannot generate border mask - compute shader missing", "map_initialization");
                return;
            }

            borderDetectionCompute.SetTexture(detectDualBordersKernel, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
            borderDetectionCompute.SetTexture(detectDualBordersKernel, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);
            borderDetectionCompute.SetTexture(detectDualBordersKernel, "DualBorderTexture", textureManager.DualBorderTexture);
            borderDetectionCompute.SetInt("MapWidth", textureManager.MapWidth);
            borderDetectionCompute.SetInt("MapHeight", textureManager.MapHeight);
            borderDetectionCompute.SetInt("CountryBorderThickness", 0); // 0 = 1-pixel borders
            borderDetectionCompute.SetInt("ProvinceBorderThickness", 0); // 0 = 1-pixel borders
            borderDetectionCompute.SetFloat("BorderAntiAliasing", 0.0f); // No AA for pixel-perfect

            int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

            borderDetectionCompute.Dispatch(detectDualBordersKernel, threadGroupsX, threadGroupsY, 1);

            var syncRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(textureManager.DualBorderTexture);
            syncRequest.WaitForCompletion();

            float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderComputeDispatcher: Generated dual-channel BorderMask (country/province) in {elapsedMs:F1}ms", "map_initialization");
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

            // Mesh rendering doesn't need per-frame updates - meshes are rendered automatically by Unity
            if (smoothBordersInitialized && renderingMode == BorderRenderingMode.MeshGeometry)
            {
                // Mesh rendering is handled by BorderMeshRenderer - nothing to do per frame
                return;
            }
            else if (renderingMode == BorderRenderingMode.ShaderPixelPerfect)
            {
                // Pixel-perfect mode uses BorderMask only - no distance field needed
                // BorderMask is already generated, nothing to do per frame
                return;
            }
            else if (renderingMode == BorderRenderingMode.ShaderDistanceField)
            {
                // Distance field rendering
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
            if (textureManager == null || textureManager.DistanceFieldTexture == null)
                return;

            RenderTexture.active = textureManager.DistanceFieldTexture;
            GL.Clear(true, true, Color.white);
            RenderTexture.active = null;

            ArchonLogger.Log("BorderComputeDispatcher: Filled border texture with white for debugging", "map_rendering");
        }

        /// <summary>
        /// Clear all borders
        /// </summary>
        public void ClearBorders()
        {
            if (textureManager == null || textureManager.DistanceFieldTexture == null)
                return;

            RenderTexture.active = textureManager.DistanceFieldTexture;
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
        /// Set border rendering mode (must be called BEFORE InitializeSmoothBorders)
        /// </summary>
        public void SetBorderRenderingMode(BorderRenderingMode mode)
        {
            renderingMode = mode;
            ArchonLogger.Log($"BorderComputeDispatcher: Border rendering mode set to {mode}", "map_initialization");
        }

        /// <summary>
        /// Bind AAA distance field border parameters to material
        /// Call this after BindTexturesToMaterial to set all border rendering parameters
        /// </summary>
        public void BindDistanceFieldBorderParams(Material material)
        {
            if (material == null)
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: Cannot bind distance field params - material is null", "map_rendering");
                return;
            }

            if (textureManager == null)
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: Cannot bind distance field params - textureManager is null", "map_rendering");
                return;
            }

            // Bind parameters via DynamicTextureSet
            textureManager.DynamicTextures?.SetDistanceFieldBorderParams(
                material,
                edgeWidth,
                gradientWidth,
                edgeSmoothness,
                edgeColorMul,
                gradientColorMul,
                edgeAlpha,
                gradientAlphaInside,
                gradientAlphaOutside
            );

            ArchonLogger.Log("BorderComputeDispatcher: Bound distance field border parameters to material", "map_rendering");
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
        /// DEBUG: Generate a texture showing extracted curve points as colored dots
        /// RED = Country borders, GREEN = Province borders
        /// This shows if the curve extraction algorithm is producing smooth points
        /// </summary>
        public Texture2D GenerateCurveDebugTexture()
        {
#if FALSE // Legacy rendering systems - disabled
            if (curveRenderer == null)
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: Cannot generate curve debug - no renderer", "map_rendering");
                return null;
            }

            return curveRenderer.RenderCurvePointsDebug(textureManager.MapWidth, textureManager.MapHeight);
#else
            ArchonLogger.LogWarning("BorderComputeDispatcher: Curve debug texture not available with mesh rendering", "map_rendering");
            return null;
#endif
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

    }
}