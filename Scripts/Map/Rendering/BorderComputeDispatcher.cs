using UnityEngine;
using UnityEngine.Rendering;

namespace Map.Rendering
{
    /// <summary>
    /// Manages GPU compute shader for high-performance border detection.
    /// Processes entire map in parallel to detect province and country borders.
    /// Part of the texture-based map rendering system.
    /// </summary>
    public class BorderComputeDispatcher : MonoBehaviour
    {
        [Header("Compute Shader")]
        [SerializeField] private ComputeShader borderDetectionCompute;

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

        // Thread group sizes (must match compute shader)
        private const int THREAD_GROUP_SIZE = 8;

        // References
        private MapTextureManager textureManager;

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
            if (borderDetectionCompute == null)
            {
                // Try to find the compute shader in the project
                #if UNITY_EDITOR
                string[] guids = UnityEditor.AssetDatabase.FindAssets("BorderDetection t:ComputeShader");
                if (guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    borderDetectionCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    ArchonLogger.Log($"BorderComputeDispatcher: Found compute shader at {path}", "map_initialization");
                }
                #endif

                if (borderDetectionCompute == null)
                {
                    ArchonLogger.LogWarning("BorderComputeDispatcher: Border detection compute shader not assigned. Borders will not be generated.", "map_rendering");
                    return;
                }
            }

            // Get kernel indices
            detectBordersKernel = borderDetectionCompute.FindKernel("DetectBorders");
            detectBordersThickKernel = borderDetectionCompute.FindKernel("DetectBordersThick");
            detectCountryBordersKernel = borderDetectionCompute.FindKernel("DetectCountryBorders");
            detectDualBordersKernel = borderDetectionCompute.FindKernel("DetectDualBorders");

            if (logPerformance)
            {
                ArchonLogger.Log($"BorderComputeDispatcher: Initialized with kernels - " +
                    $"Borders: {detectBordersKernel}, Thick: {detectBordersThickKernel}, " +
                    $"Country: {detectCountryBordersKernel}, Dual: {detectDualBordersKernel}", "map_initialization");
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
        /// Dispatch border detection on GPU
        /// </summary>
        [ContextMenu("Detect Borders")]
        public void DetectBorders()
        {
            if (borderDetectionCompute == null)
            {
                ArchonLogger.LogWarning("BorderComputeDispatcher: Compute shader not loaded. Skipping border detection.", "map_rendering");
                return;
            }

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

            // Select kernel based on mode
            int kernelToUse = detectBordersKernel;
            switch (borderMode)
            {
                case BorderMode.Province:
                    kernelToUse = detectBordersKernel;
                    break;
                case BorderMode.Country:
                    kernelToUse = detectCountryBordersKernel;
                    break;
                case BorderMode.Thick:
                    kernelToUse = detectBordersThickKernel;
                    break;
                case BorderMode.Dual:
                    kernelToUse = detectDualBordersKernel;
                    break;
            }

            // Set textures
            borderDetectionCompute.SetTexture(kernelToUse, "ProvinceIDTexture", textureManager.ProvinceIDTexture);

            // Dual mode uses DualBorderTexture, others use BorderTexture
            if (borderMode == BorderMode.Dual)
            {
                borderDetectionCompute.SetTexture(kernelToUse, "DualBorderTexture", textureManager.BorderTexture);
            }
            else
            {
                borderDetectionCompute.SetTexture(kernelToUse, "BorderTexture", textureManager.BorderTexture);
            }

            // Set dimensions
            borderDetectionCompute.SetInt("MapWidth", textureManager.MapWidth);
            borderDetectionCompute.SetInt("MapHeight", textureManager.MapHeight);

            // Set border thickness (applies to all modes)
            borderDetectionCompute.SetInt("CountryBorderThickness", countryBorderThickness);
            borderDetectionCompute.SetInt("ProvinceBorderThickness", provinceBorderThickness);

            // Set anti-aliasing
            borderDetectionCompute.SetFloat("BorderAntiAliasing", borderAntiAliasing);

            // Set additional parameters for specific modes
            if (borderMode == BorderMode.Country || borderMode == BorderMode.Dual)
            {
                borderDetectionCompute.SetTexture(kernelToUse, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);
            }

            // Calculate thread groups (round up division)
            int threadGroupsX = (textureManager.MapWidth + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (textureManager.MapHeight + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

            // Dispatch compute shader
            borderDetectionCompute.Dispatch(kernelToUse, threadGroupsX, threadGroupsY, 1);

            // Log performance
            if (logPerformance)
            {
                float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
                ArchonLogger.Log($"BorderComputeDispatcher: Border detection completed in {elapsedMs:F2}ms " +
                    $"({textureManager.MapWidth}x{textureManager.MapHeight} pixels, {threadGroupsX}x{threadGroupsY} thread groups)", "map_rendering");
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

#if UNITY_EDITOR
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