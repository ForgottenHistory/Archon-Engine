using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// Manages compute shader loading and kernel initialization for border rendering
    /// Extracted from BorderComputeDispatcher for single responsibility
    ///
    /// Responsibilities:
    /// - Lazy loading of compute shaders (BorderDetection, BorderCurveRasterizer, BorderSDF)
    /// - Kernel index caching
    /// - Shader validation and error reporting
    /// </summary>
    public class BorderShaderManager
    {
        // Compute shader references
        private ComputeShader borderDetectionCompute;
        private ComputeShader borderCurveRasterizerCompute;
        private ComputeShader borderSDFCompute;

        // Kernel indices (only actually used kernel)
        private int detectDualBordersKernel = -1;

        // Configuration
        private readonly bool logPerformance;

        public ComputeShader BorderDetectionShader => borderDetectionCompute;
        public ComputeShader BorderCurveRasterizerShader => borderCurveRasterizerCompute;
        public ComputeShader BorderSDFShader => borderSDFCompute;
        public int DetectDualBordersKernel => detectDualBordersKernel;

        public BorderShaderManager(bool enablePerformanceLogging = false)
        {
            logPerformance = enablePerformanceLogging;
        }

        /// <summary>
        /// Initialize compute shader kernels
        /// Lazy-loads shaders if not already assigned
        /// </summary>
        public void InitializeKernels(ComputeShader borderDetection = null, ComputeShader borderCurveRasterizer = null, ComputeShader borderSDF = null)
        {
            // Use provided shaders or attempt to load them
            borderDetectionCompute = borderDetection;
            borderCurveRasterizerCompute = borderCurveRasterizer;
            borderSDFCompute = borderSDF;

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
                    ArchonLogger.Log($"BorderShaderManager: Found BorderDetection shader at {path}", "map_initialization");
                }
                #endif

                if (borderDetectionCompute == null)
                {
                    ArchonLogger.LogWarning("BorderShaderManager: Border detection compute shader not assigned. Borders will not be generated.", "map_rendering");
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
                    ArchonLogger.Log($"BorderShaderManager: Found BorderCurveRasterizer shader at {path}", "map_initialization");
                }
                #endif

                if (borderCurveRasterizerCompute == null)
                {
                    ArchonLogger.LogWarning("BorderShaderManager: BorderCurveRasterizer compute shader not found - rasterization rendering will not be available", "map_initialization");
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
                    ArchonLogger.Log($"BorderShaderManager: Found BorderSDF shader at {path}", "map_initialization");
                }
                #endif

                if (borderSDFCompute == null)
                {
                    ArchonLogger.LogWarning("BorderShaderManager: BorderSDF compute shader not found - SDF rendering will not be available", "map_initialization");
                }
            }

            // Get kernel index for dual border detection (only used kernel)
            if (borderDetectionCompute != null)
            {
                detectDualBordersKernel = borderDetectionCompute.FindKernel("DetectDualBorders");

                if (logPerformance)
                {
                    ArchonLogger.Log($"BorderShaderManager: Initialized with DetectDualBorders kernel (ID: {detectDualBordersKernel})", "map_initialization");
                }
            }
        }

        /// <summary>
        /// Check if shader manager is properly initialized
        /// </summary>
        public bool IsInitialized()
        {
            return borderDetectionCompute != null && detectDualBordersKernel >= 0;
        }

        /// <summary>
        /// Check if specific rendering mode is supported (has required shaders)
        /// </summary>
        public bool SupportsRenderingMode(BorderRenderingMode mode)
        {
            switch (mode)
            {
                case BorderRenderingMode.None:
                    return true;

                case BorderRenderingMode.ShaderDistanceField:
                    return borderDetectionCompute != null;

                case BorderRenderingMode.MeshGeometry:
                    return true; // Mesh rendering doesn't need compute shaders

                case BorderRenderingMode.ShaderPixelPerfect:
                    return borderDetectionCompute != null;

                default:
                    return false;
            }
        }
    }
}
