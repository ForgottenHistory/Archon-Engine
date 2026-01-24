using UnityEngine;
using UnityEngine.Rendering;
using Utils;
using Core.Modding;

namespace Map.Rendering
{
    /// <summary>
    /// Generates normal map from heightmap using GPU compute shader.
    /// Based on EU5's approach - calculates gradients on GPU for maximum performance.
    ///
    /// Algorithm:
    /// 1. Sample heightmap at each pixel and 4 neighbors (LRUD)
    /// 2. Calculate gradients using central difference
    /// 3. Construct normal vector from gradients
    /// 4. Pack normal into RG8 texture (XZ components, reconstruct Y in fragment shader)
    ///
    /// Result: High-quality lighting with depth perception at any zoom level
    /// </summary>
    public class NormalMapGenerator
    {
        private readonly ComputeShader generateNormalMapCompute;
        private readonly int generateNormalsKernel;

        // Thread group size - must match compute shader [numthreads(8,8,1)]
        private const int THREAD_GROUP_SIZE = 8;

        public NormalMapGenerator()
        {
            // Load compute shader - check mods first, then fall back to Resources
            generateNormalMapCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                "GenerateNormalMap",
                "Shaders/GenerateNormalMap"
            );

            if (generateNormalMapCompute == null)
            {
                ArchonLogger.LogError("NormalMapGenerator: Compute shader not found!", "map_initialization");
                return;
            }

            // Validate kernel exists
            if (!generateNormalMapCompute.HasKernel("GenerateNormals"))
            {
                ArchonLogger.LogError("NormalMapGenerator: Compute shader missing GenerateNormals kernel!", "map_initialization");
                return;
            }

            generateNormalsKernel = generateNormalMapCompute.FindKernel("GenerateNormals");
        }

        /// <summary>
        /// Generates normal map from heightmap texture using GPU compute shader.
        /// CRITICAL: Follows GPU coordination patterns from unity-compute-shader-coordination.md
        /// </summary>
        /// <param name="heightmapTexture">Input heightmap (R8 or R16 format)</param>
        /// <param name="normalMapTexture">Output normal map (RG8 format)</param>
        /// <param name="heightScale">Height scale multiplier for normal calculation (default: 10.0)</param>
        /// <param name="logProgress">Enable performance logging</param>
        public void GenerateNormalMap(
            Texture2D heightmapTexture,
            Texture2D normalMapTexture,
            float heightScale = 10.0f,
            bool logProgress = false)
        {
            if (generateNormalMapCompute == null)
            {
                ArchonLogger.LogError("NormalMapGenerator: Compute shader not available", "map_rendering");
                return;
            }

            if (heightmapTexture == null || normalMapTexture == null)
            {
                ArchonLogger.LogError("NormalMapGenerator: Heightmap or normal map texture is null", "map_rendering");
                return;
            }

            int width = heightmapTexture.width;
            int height = heightmapTexture.height;

            if (logProgress)
            {
                ArchonLogger.Log($"NormalMapGenerator: Generating {width}x{height} normal map from heightmap", "map_rendering");
            }

            // DEBUG: Check if heightmap has actual data
            if (logProgress)
            {
                Color h1 = heightmapTexture.GetPixel(width / 4, height / 4);
                Color h2 = heightmapTexture.GetPixel(width / 2, height / 2);
                Color h3 = heightmapTexture.GetPixel(width * 3 / 4, height * 3 / 4);
                ArchonLogger.Log($"NormalMapGenerator: Heightmap samples - [{h1.r:F3}] [{h2.r:F3}] [{h3.r:F3}] (should vary 0.0-1.0)", "map_rendering");
            }

            // Create RenderTexture for normal map output
            // CRITICAL: Use explicit GraphicsFormat (see: explicit-graphics-format.md)
            var normalMapDesc = new RenderTextureDescriptor(
                width, height,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                0
            );
            normalMapDesc.enableRandomWrite = true;
            RenderTexture normalMapRT = new RenderTexture(normalMapDesc);
            normalMapRT.Create();

            // Extract heightmap pixels from Texture2D
            Color[] heightmapPixels = heightmapTexture.GetPixels();

            // Create float array for GPU buffer (R channel only)
            float[] heightmapData = new float[heightmapPixels.Length];
            for (int i = 0; i < heightmapPixels.Length; i++)
            {
                heightmapData[i] = heightmapPixels[i].r;
            }

            // Create GPU buffer and upload data
            // CRITICAL: Use ComputeBuffer instead of Graphics.Blit (see: unity-compute-shader-coordination.md)
            ComputeBuffer heightmapBuffer = new ComputeBuffer(heightmapData.Length, sizeof(float));
            heightmapBuffer.SetData(heightmapData);

            var startTime = System.Diagnostics.Stopwatch.StartNew();

            // Set compute shader parameters
            generateNormalMapCompute.SetInt("MapWidth", width);
            generateNormalMapCompute.SetInt("MapHeight", height);
            generateNormalMapCompute.SetFloat("HeightScale", heightScale);

            // CRITICAL: Use SetBuffer for StructuredBuffer, SetTexture for RWTexture2D
            // See: unity-compute-shader-coordination.md
            generateNormalMapCompute.SetBuffer(generateNormalsKernel, "HeightmapData", heightmapBuffer);
            generateNormalMapCompute.SetTexture(generateNormalsKernel, "NormalMapTexture", normalMapRT);

            // Calculate thread groups (round up division)
            int threadGroupsX = (width + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (height + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;

            // Dispatch compute shader
            generateNormalMapCompute.Dispatch(generateNormalsKernel, threadGroupsX, threadGroupsY, 1);

            // CRITICAL: GPU synchronization before reading results
            // See: unity-compute-shader-coordination.md - Issue 1 (GPU Race Conditions)
            var syncRequest = AsyncGPUReadback.Request(normalMapRT);
            syncRequest.WaitForCompletion();

            if (logProgress)
            {
                ArchonLogger.Log($"NormalMapGenerator: GPU execution took {startTime.ElapsedMilliseconds}ms", "map_rendering");
            }

            // Copy RenderTexture back to Texture2D for material binding
            RenderTexture.active = normalMapRT;
            normalMapTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            normalMapTexture.Apply();
            RenderTexture.active = null;

            // DEBUG: Sample some pixels to verify generation
            if (logProgress)
            {
                Color32 sample1 = normalMapTexture.GetPixel(width / 4, height / 4);
                Color32 sample2 = normalMapTexture.GetPixel(width / 2, height / 2);
                Color32 sample3 = normalMapTexture.GetPixel(width * 3 / 4, height * 3 / 4);
                ArchonLogger.Log($"NormalMapGenerator: Sample normals - RG[{sample1.r},{sample1.g}] RG[{sample2.r},{sample2.g}] RG[{sample3.r},{sample3.g}]", "map_rendering");
            }

            // Cleanup temporary resources
            heightmapBuffer.Release();
            normalMapRT.Release();

            if (logProgress)
            {
                startTime.Stop();
                ArchonLogger.Log($"NormalMapGenerator: Total generation time {startTime.ElapsedMilliseconds}ms", "map_rendering");
            }
        }
    }
}
