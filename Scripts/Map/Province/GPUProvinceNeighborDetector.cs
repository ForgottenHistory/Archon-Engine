using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Map.Loading;
using System.Collections.Generic;

namespace Map.Province
{
    /// <summary>
    /// GPU-accelerated province neighbor detection using compute shaders
    /// Processes millions of pixels in parallel on the GPU instead of CPU
    /// </summary>
    public static class GPUProvinceNeighborDetector
    {
        private static ComputeShader s_computeShader;
        private const string COMPUTE_SHADER_PATH = "Shaders/ProvinceNeighborDetection";

        /// <summary>
        /// Initialize the compute shader
        /// </summary>
        public static bool Initialize()
        {
            if (s_computeShader != null)
                return true;

            s_computeShader = Resources.Load<ComputeShader>(COMPUTE_SHADER_PATH);
            if (s_computeShader == null)
            {
                ArchonLogger.LogError($"Failed to load compute shader at Resources/{COMPUTE_SHADER_PATH}", "map_textures");
                return false;
            }

            ArchonLogger.Log($"GPUProvinceNeighborDetector: Loaded compute shader", "map_textures");
            return true;
        }

        public static bool IsAvailable => s_computeShader != null || Initialize();

        /// <summary>
        /// Detect neighbors using GPU compute shader - orders of magnitude faster than CPU
        /// Uses AppendStructuredBuffer for dynamic sizing (no capacity limits)
        /// </summary>
        public static ProvinceNeighborDetector.NeighborResult DetectNeighborsGPU(
            Texture provinceIDTexture,
            int provinceCount)
        {
            var result = new ProvinceNeighborDetector.NeighborResult();

            if (!Initialize())
            {
                result.ErrorMessage = "Compute shader not loaded";
                return result;
            }

            if (provinceIDTexture == null)
            {
                result.ErrorMessage = "Province ID texture is null";
                return result;
            }

            int width = provinceIDTexture.width;
            int height = provinceIDTexture.height;

            float startTime = Time.realtimeSinceStartup;
            ArchonLogger.Log($"[GPU] Detecting neighbors for {provinceCount} provinces on {width}x{height} texture", "map_textures");

            // AppendStructuredBuffer needs capacity, but GPU handles overflow gracefully
            // Estimate: each pixel can generate max 2 pairs (right + bottom neighbors)
            // But most pixels are interior, so actual is much less
            // Use provinceCount * 50 as safe upper bound for unique pairs
            int maxNeighborPairs = Mathf.Max(provinceCount * 50, 1000000);
            int maxCoastalProvinces = provinceCount;

            // Create GPU buffers with ComputeBufferType.Append for dynamic appending
            var neighborPairsBuffer = new ComputeBuffer(maxNeighborPairs, sizeof(uint) * 2, ComputeBufferType.Append);
            var provinceBoundsBuffer = new ComputeBuffer(provinceCount + 1, sizeof(int) * 4);
            var coastalProvincesBuffer = new ComputeBuffer(maxCoastalProvinces, sizeof(uint), ComputeBufferType.Append);

            // Reset append buffer counters to 0
            neighborPairsBuffer.SetCounterValue(0);
            coastalProvincesBuffer.SetCounterValue(0);

            // Initialize bounds with extreme values
            var initialBounds = new int4[provinceCount + 1];
            for (int i = 0; i < initialBounds.Length; i++)
            {
                initialBounds[i] = new int4(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
            }
            provinceBoundsBuffer.SetData(initialBounds);

            // Buffer to read back append counts
            var countBuffer = new ComputeBuffer(2, sizeof(int), ComputeBufferType.IndirectArguments);
            countBuffer.SetData(new int[] { 0, 0 });

            try
            {
                // Find kernel indices
                int detectNeighborsKernel = s_computeShader.FindKernel("DetectNeighbors");
                int calculateBoundsKernel = s_computeShader.FindKernel("CalculateBounds");

                // Set shader parameters for neighbor detection
                s_computeShader.SetTexture(detectNeighborsKernel, "ProvinceIDTexture", provinceIDTexture);
                s_computeShader.SetInts("TextureSize", width, height);
                s_computeShader.SetBuffer(detectNeighborsKernel, "NeighborPairs", neighborPairsBuffer);
                s_computeShader.SetBuffer(detectNeighborsKernel, "CoastalProvinces", coastalProvincesBuffer);

                // Set bounds calculation parameters
                s_computeShader.SetTexture(calculateBoundsKernel, "ProvinceIDTexture", provinceIDTexture);
                s_computeShader.SetBuffer(calculateBoundsKernel, "ProvinceBounds", provinceBoundsBuffer);

                // Calculate thread groups (8x8 threads per group)
                int threadGroupsX = Mathf.CeilToInt(width / 8.0f);
                int threadGroupsY = Mathf.CeilToInt(height / 8.0f);

                // Dispatch neighbor detection kernel
                s_computeShader.Dispatch(detectNeighborsKernel, threadGroupsX, threadGroupsY, 1);

                // Dispatch bounds calculation kernel
                s_computeShader.Dispatch(calculateBoundsKernel, threadGroupsX, threadGroupsY, 1);

                // Read back append buffer counts using CopyCount
                ComputeBuffer.CopyCount(neighborPairsBuffer, countBuffer, 0);
                ComputeBuffer.CopyCount(coastalProvincesBuffer, countBuffer, sizeof(int));

                var countData = new int[2];
                countBuffer.GetData(countData);
                int actualPairCount = Mathf.Min(countData[0], maxNeighborPairs);
                int actualCoastalCount = Mathf.Min(countData[1], maxCoastalProvinces);

                ArchonLogger.Log($"[GPU] Found {actualPairCount} neighbor pairs and {actualCoastalCount} coastal provinces", "map_textures");

                // Read neighbor pairs
                var neighborPairsData = new uint2[actualPairCount];
                if (actualPairCount > 0)
                {
                    neighborPairsBuffer.GetData(neighborPairsData, 0, 0, actualPairCount);
                }

                // Read coastal provinces
                var coastalProvincesData = new uint[actualCoastalCount];
                if (actualCoastalCount > 0)
                {
                    coastalProvincesBuffer.GetData(coastalProvincesData, 0, 0, actualCoastalCount);
                }

                // Read bounds
                var boundsData = new int4[provinceCount + 1];
                provinceBoundsBuffer.GetData(boundsData);

                // Convert to result format
                result = ConvertToResult(neighborPairsData, coastalProvincesData, boundsData, provinceCount);

                float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
                ArchonLogger.Log($"[GPU] Neighbor detection complete in {elapsed:F2}ms", "map_textures");
            }
            finally
            {
                // Clean up GPU buffers
                neighborPairsBuffer.Release();
                provinceBoundsBuffer.Release();
                coastalProvincesBuffer.Release();
                countBuffer.Release();
            }

            return result;
        }

        /// <summary>
        /// Convert GPU results to CPU data structures
        /// Returns adjacency dictionary that can be directly used by AdjacencySystem
        /// </summary>
        private static ProvinceNeighborDetector.NeighborResult ConvertToResult(
            uint2[] neighborPairs,
            uint[] coastalProvinces,
            int4[] bounds,
            int provinceCount)
        {
            var result = new ProvinceNeighborDetector.NeighborResult();

            // Process neighbor pairs - build managed adjacency dictionary
            // This is what AdjacencySystem.SetAdjacencies expects
            var adjacencyDict = new Dictionary<int, HashSet<int>>();

            foreach (var pair in neighborPairs)
            {
                if (pair.x == 0 || pair.y == 0) continue; // Skip ocean pairs
                if (pair.x == pair.y) continue; // Skip self-pairs

                int id1 = (int)pair.x;
                int id2 = (int)pair.y;

                // Add bidirectional adjacency
                if (!adjacencyDict.TryGetValue(id1, out var neighbors1))
                {
                    neighbors1 = new HashSet<int>();
                    adjacencyDict[id1] = neighbors1;
                }
                neighbors1.Add(id2);

                if (!adjacencyDict.TryGetValue(id2, out var neighbors2))
                {
                    neighbors2 = new HashSet<int>();
                    adjacencyDict[id2] = neighbors2;
                }
                neighbors2.Add(id1);
            }

            // Store the adjacency dictionary in the result
            result.AdjacencyDictionary = adjacencyDict;

            // Process coastal provinces - deduplicate
            var coastalSet = new NativeHashSet<ushort>(Mathf.Max(1, coastalProvinces.Length), Allocator.Persistent);
            foreach (uint coastal in coastalProvinces)
            {
                if (coastal != 0)
                {
                    coastalSet.Add((ushort)coastal);
                }
            }

            // Process bounds
            var provinceBounds = new NativeHashMap<ushort, ProvinceNeighborDetector.ProvinceBounds>(
                provinceCount + 1, Allocator.Persistent);

            for (int i = 1; i <= provinceCount; i++)
            {
                var b = bounds[i];
                if (b.x != int.MaxValue) // Valid bounds
                {
                    provinceBounds[(ushort)i] = new ProvinceNeighborDetector.ProvinceBounds
                    {
                        Min = new int2(b.x, b.y),
                        Max = new int2(b.z, b.w)
                    };
                }
            }

            // Create empty ProvinceNeighbors (not used, adjacency dict is preferred)
            var provinceNeighbors = new NativeHashMap<ushort, ProvinceNeighborDetector.ProvinceNeighborData>(1, Allocator.Persistent);

            result.IsSuccess = true;
            result.ProvinceNeighbors = provinceNeighbors;
            result.ProvinceBounds = provinceBounds;
            result.CoastalProvinces = coastalSet;
            result.TotalNeighborPairs = adjacencyDict.Count;

            return result;
        }

        /// <summary>
        /// Create province ID texture from load result for GPU processing
        /// </summary>
        public static Texture2D CreateProvinceIDTexture(ProvinceMapLoader.LoadResult loadResult)
        {
            // Create R16G16 texture for province IDs (supports up to 65535 provinces)
            var texture = new Texture2D(loadResult.Width, loadResult.Height, TextureFormat.RGFloat, false);
            texture.filterMode = FilterMode.Point; // No filtering - we need exact values
            texture.wrapMode = TextureWrapMode.Clamp;

            // Convert province pixels to texture
            var pixels = new Color[loadResult.Width * loadResult.Height];

            // Initialize all pixels to ocean (ID 0)
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(0, 0, 0, 1);
            }

            // Fill with province IDs
            for (int i = 0; i < loadResult.ProvincePixels.Length; i++)
            {
                var provincePixel = loadResult.ProvincePixels[i];
                int index = provincePixel.Position.y * loadResult.Width + provincePixel.Position.x;

                if (index >= 0 && index < pixels.Length)
                {
                    // Encode province ID into RG channels
                    uint id = provincePixel.ProvinceID;
                    float r = (id & 0xFFFF) / 65535f;        // Low 16 bits
                    float g = ((id >> 16) & 0xFFFF) / 65535f; // High 16 bits

                    pixels[index] = new Color(r, g, 0, 1);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false); // Don't generate mipmaps, don't make unreadable

            return texture;
        }
    }
}