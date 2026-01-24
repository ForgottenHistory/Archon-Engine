using UnityEngine;
using Core.Modding;

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
            // Use provided shaders or attempt to load from Resources
            borderDetectionCompute = borderDetection;
            borderCurveRasterizerCompute = borderCurveRasterizer;
            borderSDFCompute = borderSDF;

            // Load BorderDetection compute shader - check mods first, then fall back to Resources
            if (borderDetectionCompute == null)
            {
                borderDetectionCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                    "BorderDetection",
                    "Shaders/BorderDetection"
                );

                if (borderDetectionCompute == null)
                {
                    ArchonLogger.LogWarning("BorderShaderManager: BorderDetection not found!", "map_rendering");
                    return;
                }
            }

            // Load BorderCurveRasterizer compute shader - check mods first, then fall back to Resources
            if (borderCurveRasterizerCompute == null)
            {
                borderCurveRasterizerCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                    "BorderCurveRasterizer",
                    "Shaders/BorderCurveRasterizer"
                );

                if (borderCurveRasterizerCompute == null)
                {
                    ArchonLogger.LogWarning("BorderShaderManager: BorderCurveRasterizer not found - rasterization rendering will not be available", "map_initialization");
                }
            }

            // Load BorderSDF compute shader - check mods first, then fall back to Resources
            if (borderSDFCompute == null)
            {
                borderSDFCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                    "BorderSDF",
                    "Shaders/BorderSDF"
                );

                if (borderSDFCompute == null)
                {
                    ArchonLogger.LogWarning("BorderShaderManager: BorderSDF not found - SDF rendering will not be available", "map_initialization");
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
