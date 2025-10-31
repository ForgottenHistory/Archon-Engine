using UnityEngine;
using System;

namespace Map.Rendering.Border
{
    /// <summary>
    /// Debug and benchmarking utilities for border rendering
    /// Extracted from BorderComputeDispatcher for single responsibility
    ///
    /// Responsibilities:
    /// - Benchmark border detection performance
    /// - Generate debug visualizations
    /// - Clear/fill border textures for debugging
    /// - Save debug textures to disk
    /// </summary>
    public class BorderDebugUtility
    {
        private readonly MapTextureManager textureManager;
        private readonly bool logPerformance;

        public BorderDebugUtility(MapTextureManager manager, bool enablePerformanceLogging = false)
        {
            textureManager = manager;
            logPerformance = enablePerformanceLogging;
        }

        /// <summary>
        /// Fill border texture with white for debugging
        /// </summary>
        public void FillBordersWhite()
        {
            if (textureManager == null || textureManager.DistanceFieldTexture == null)
                return;

            RenderTexture.active = textureManager.DistanceFieldTexture;
            GL.Clear(true, true, Color.white);
            RenderTexture.active = null;

            ArchonLogger.Log("BorderDebugUtility: Filled border texture with white for debugging", "map_rendering");
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
                ArchonLogger.Log("BorderDebugUtility: Borders cleared", "map_rendering");
            }
        }

        /// <summary>
        /// Generate debug texture showing extracted curve points as colored dots
        /// RED = Country borders, GREEN = Province borders
        /// </summary>
        public Texture2D GenerateCurveDebugTexture()
        {
            // Note: This feature is currently disabled as it depends on legacy rendering systems
            ArchonLogger.LogWarning("BorderDebugUtility: Curve debug texture not available with current rendering mode", "map_rendering");
            return null;
        }

        /// <summary>
        /// Save curve points debug visualization to disk
        /// </summary>
        public void SaveCurvePointsDebug(string outputPath = "D:/Stuff/My Games/Hegemon/curve_points_debug.png")
        {
            var debugTexture = GenerateCurveDebugTexture();
            if (debugTexture == null)
            {
                ArchonLogger.LogError("BorderDebugUtility: Failed to generate curve debug texture", "map_rendering");
                return;
            }

            // Save to specified path
            var bytes = debugTexture.EncodeToPNG();
            System.IO.File.WriteAllBytes(outputPath, bytes);

            ArchonLogger.Log($"BorderDebugUtility: Saved curve points debug visualization to {outputPath}", "map_rendering");
            ArchonLogger.Log("RED = Country borders, GREEN = Province borders", "map_rendering");
            ArchonLogger.Log("Each dot = one curve point from Chaikin smoothing", "map_rendering");

            UnityEngine.Object.DestroyImmediate(debugTexture);
        }

        /// <summary>
        /// Benchmark border detection performance across different modes
        /// </summary>
        public void BenchmarkBorderDetection(Action<BorderMode> detectBordersCallback)
        {
            if (textureManager == null)
            {
                ArchonLogger.LogError("BorderDebugUtility: Cannot benchmark without texture manager", "map_rendering");
                return;
            }

            ArchonLogger.Log("=== Border Detection Benchmark ===", "map_rendering");
            ArchonLogger.Log($"Map Size: {textureManager.MapWidth}x{textureManager.MapHeight}", "map_rendering");

            // Test each mode
            var modes = new[] { BorderMode.Province, BorderMode.Country, BorderMode.Thick };
            foreach (var mode in modes)
            {
                // Warm up
                detectBordersCallback(mode);

                // Measure
                float totalTime = 0;
                int iterations = 10;

                for (int i = 0; i < iterations; i++)
                {
                    float start = Time.realtimeSinceStartup;
                    detectBordersCallback(mode);
                    totalTime += (Time.realtimeSinceStartup - start);
                }

                float avgMs = (totalTime / iterations) * 1000f;
                ArchonLogger.Log($"{mode} Mode: {avgMs:F2}ms average ({iterations} iterations)", "map_rendering");
            }

            ArchonLogger.Log("=== Benchmark Complete ===", "map_rendering");
        }
    }
}
