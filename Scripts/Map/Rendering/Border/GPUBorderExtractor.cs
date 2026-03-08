using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// GPU-accelerated border pixel extraction using compute shaders.
    /// Replaces CPU-heavy median filter + junction detection + border pixel extraction.
    ///
    /// Pipeline:
    ///   1. MedianFilter kernel - Smooth province ID noise (3x3 mode filter) on GPU
    ///   2. DetectBorderPixels kernel - Find all border pixels with province pair IDs
    ///   3. DetectJunctions kernel - Find pixels where 3+ provinces meet
    ///   4. CPU readback - Group border pixels by province pair for chaining
    ///
    /// Follows GPUProvinceNeighborDetector patterns:
    ///   - Static class, AppendStructuredBuffer, CopyCount readback
    ///   - Binary disk cache with magic + version header
    /// </summary>
    public static class GPUBorderExtractor
    {
        private static ComputeShader s_computeShader;
        private const string COMPUTE_SHADER_PATH = "Shaders/BorderExtractionPipeline";

        // Cache format
        private const uint CACHE_MAGIC = 0x42524452; // "BRDR"
        private const uint CACHE_VERSION = 2;

        /// <summary>
        /// Initialize the compute shader.
        /// </summary>
        public static bool Initialize()
        {
            if (s_computeShader != null)
                return true;

            s_computeShader = Resources.Load<ComputeShader>(COMPUTE_SHADER_PATH);
            if (s_computeShader == null)
            {
                ArchonLogger.LogError($"GPUBorderExtractor: Failed to load compute shader at Resources/{COMPUTE_SHADER_PATH}", "map_initialization");
                return false;
            }

            ArchonLogger.Log("GPUBorderExtractor: Loaded compute shader", "map_initialization");
            return true;
        }

        public static bool IsAvailable => s_computeShader != null || Initialize();

        /// <summary>
        /// Result of GPU border extraction.
        /// Contains border pixels grouped by province pair, plus junction data.
        /// </summary>
        public struct ExtractionResult
        {
            public bool IsSuccess;
            public string ErrorMessage;

            /// <summary>Border pixels grouped by province pair (provinceA, provinceB) -> list of pixel positions</summary>
            public Dictionary<(ushort, ushort), List<Vector2>> BorderPixelsByPair;

            /// <summary>Junction pixels where 3+ provinces meet: position -> province count</summary>
            public Dictionary<Vector2, int> JunctionPixels;
        }

        /// <summary>
        /// Extract border pixels using GPU compute shaders.
        /// Returns border pixels grouped by province pair, ready for chaining/smoothing.
        /// </summary>
        public static ExtractionResult ExtractBorderPixelsGPU(Texture provinceIDTexture)
        {
            var result = new ExtractionResult();

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
            int totalPixels = width * height;

            float startTime = Time.realtimeSinceStartup;
            ArchonLogger.Log($"GPUBorderExtractor: Starting GPU extraction on {width}x{height} texture ({totalPixels / 1000000f:F1}M pixels)", "map_initialization");

            // Buffer sizing: border pixels are ~5-10% of total pixels
            // Each border pixel can emit up to 2 entries (right + bottom neighbor)
            int maxBorderPixels = Mathf.Max(totalPixels / 5, 500000);
            int maxJunctions = Mathf.Max(totalPixels / 100, 50000);

            // Create GPU buffers
            // BorderPixelOutput: 2 x uint = 8 bytes
            var borderPixelsBuffer = new ComputeBuffer(maxBorderPixels, sizeof(uint) * 2, ComputeBufferType.Append);
            // JunctionOutput: 2 x uint = 8 bytes
            var junctionsBuffer = new ComputeBuffer(maxJunctions, sizeof(uint) * 2, ComputeBufferType.Append);
            var countBuffer = new ComputeBuffer(2, sizeof(int), ComputeBufferType.IndirectArguments);

            borderPixelsBuffer.SetCounterValue(0);
            junctionsBuffer.SetCounterValue(0);
            countBuffer.SetData(new int[] { 0, 0 });

            try
            {
                int threadGroupsX = Mathf.CeilToInt(width / 8.0f);
                int threadGroupsY = Mathf.CeilToInt(height / 8.0f);

                s_computeShader.SetInts("TextureSize", width, height);

                // Skip GPU median filter - use original province ID texture directly.
                // The CPU chaining/smoothing pipeline handles noise adequately,
                // and the median filter can shift boundaries causing pair mismatches.

                // ---- Kernel 1: Detect Border Pixels ----
                float borderStart = Time.realtimeSinceStartup;
                int borderKernel = s_computeShader.FindKernel("DetectBorderPixels");
                s_computeShader.SetTexture(borderKernel, "BorderProvinceIDTexture", provinceIDTexture);
                s_computeShader.SetBuffer(borderKernel, "BorderPixels", borderPixelsBuffer);
                s_computeShader.Dispatch(borderKernel, threadGroupsX, threadGroupsY, 1);

                float borderElapsed = (Time.realtimeSinceStartup - borderStart) * 1000f;
                ArchonLogger.Log($"GPUBorderExtractor: DetectBorderPixels completed in {borderElapsed:F1}ms", "map_initialization");

                // ---- Kernel 2: Detect Junctions ----
                float junctionStart = Time.realtimeSinceStartup;
                int junctionKernel = s_computeShader.FindKernel("DetectJunctions");
                s_computeShader.SetTexture(junctionKernel, "BorderProvinceIDTexture", provinceIDTexture);
                s_computeShader.SetBuffer(junctionKernel, "Junctions", junctionsBuffer);
                s_computeShader.Dispatch(junctionKernel, threadGroupsX, threadGroupsY, 1);

                float junctionElapsed = (Time.realtimeSinceStartup - junctionStart) * 1000f;
                ArchonLogger.Log($"GPUBorderExtractor: DetectJunctions completed in {junctionElapsed:F1}ms", "map_initialization");

                // ---- Readback counts ----
                ComputeBuffer.CopyCount(borderPixelsBuffer, countBuffer, 0);
                ComputeBuffer.CopyCount(junctionsBuffer, countBuffer, sizeof(int));

                var countData = new int[2];
                countBuffer.GetData(countData);

                int rawBorderCount = countData[0];
                int rawJunctionCount = countData[1];
                int actualBorderCount = Mathf.Min(rawBorderCount, maxBorderPixels);
                int actualJunctionCount = Mathf.Min(rawJunctionCount, maxJunctions);

                if (rawBorderCount >= maxBorderPixels)
                {
                    ArchonLogger.LogError($"GPUBorderExtractor: Border pixel buffer OVERFLOW: {rawBorderCount} exceeded capacity {maxBorderPixels}. Some borders may be incomplete!", "map_initialization");
                }

                if (rawJunctionCount >= maxJunctions)
                {
                    ArchonLogger.LogError($"GPUBorderExtractor: Junction buffer OVERFLOW: {rawJunctionCount} exceeded capacity {maxJunctions}.", "map_initialization");
                }

                ArchonLogger.Log($"GPUBorderExtractor: Found {actualBorderCount} border pixel entries, {actualJunctionCount} junction pixels", "map_initialization");

                // ---- Readback border pixels ----
                float readbackStart = Time.realtimeSinceStartup;

                // Use uint2[] to match struct layout (positionPacked, pairPacked)
                var borderPixelData = new uint2[actualBorderCount];
                if (actualBorderCount > 0)
                {
                    borderPixelsBuffer.GetData(borderPixelData, 0, 0, actualBorderCount);
                }

                // Group border pixels by province pair
                var bordersByPair = new Dictionary<(ushort, ushort), List<Vector2>>();

                for (int i = 0; i < actualBorderCount; i++)
                {
                    uint positionPacked = borderPixelData[i].x;
                    uint pairPacked = borderPixelData[i].y;

                    int x = (int)(positionPacked & 0xFFFF);
                    int y = (int)(positionPacked >> 16);
                    ushort provinceA = (ushort)(pairPacked & 0xFFFF);
                    ushort provinceB = (ushort)(pairPacked >> 16);

                    if (provinceA == 0 || provinceB == 0)
                        continue;

                    var key = (provinceA, provinceB);
                    if (!bordersByPair.TryGetValue(key, out var pixels))
                    {
                        pixels = new List<Vector2>();
                        bordersByPair[key] = pixels;
                    }
                    pixels.Add(new Vector2(x, y));
                }

                // ---- Readback junctions ----
                var junctionData = new uint2[actualJunctionCount];
                if (actualJunctionCount > 0)
                {
                    junctionsBuffer.GetData(junctionData, 0, 0, actualJunctionCount);
                }

                var junctions = new Dictionary<Vector2, int>();
                for (int i = 0; i < actualJunctionCount; i++)
                {
                    uint positionPacked = junctionData[i].x;
                    uint provinceCount = junctionData[i].y;

                    int x = (int)(positionPacked & 0xFFFF);
                    int y = (int)(positionPacked >> 16);

                    junctions[new Vector2(x, y)] = (int)provinceCount;
                }

                float readbackElapsed = (Time.realtimeSinceStartup - readbackStart) * 1000f;
                float totalElapsed = (Time.realtimeSinceStartup - startTime) * 1000f;

                ArchonLogger.Log($"GPUBorderExtractor: Readback + grouping in {readbackElapsed:F1}ms", "map_initialization");
                ArchonLogger.Log($"GPUBorderExtractor: Total GPU extraction in {totalElapsed:F1}ms ({bordersByPair.Count} border pairs, {junctions.Count} junctions)", "map_initialization");

                result.IsSuccess = true;
                result.BorderPixelsByPair = bordersByPair;
                result.JunctionPixels = junctions;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"GPUBorderExtractor: Exception during extraction: {e.Message}", "map_initialization");
                result.ErrorMessage = e.Message;
            }
            finally
            {
                borderPixelsBuffer.Release();
                junctionsBuffer.Release();
                countBuffer.Release();
            }

            return result;
        }

        // =====================================================================
        // Disk Cache
        // =====================================================================

        /// <summary>
        /// Try to load border extraction data from disk cache.
        /// Cache invalidates when provinces.png is newer than the cache file.
        /// Format: [BRDR 4B][version 4B][pairCount 4B][junctionCount 4B]
        ///         [pairs: provinceA(2B) provinceB(2B) pixelCount(4B) pixels(x:2B,y:2B)...]
        ///         [junctions: x(2B) y(2B) count(4B)...]
        /// </summary>
        /// <param name="provincesImagePath">Path to provinces.png - cache stored as provinces.png.borders</param>
        public static ExtractionResult TryLoadCache(string provincesImagePath)
        {
            var result = new ExtractionResult();
            string fullCachePath = provincesImagePath + ".borders";

            if (!File.Exists(fullCachePath))
                return result;

            // Invalidate if source image is newer than cache
            if (File.GetLastWriteTimeUtc(provincesImagePath) > File.GetLastWriteTimeUtc(fullCachePath))
            {
                ArchonLogger.Log("GPUBorderExtractor: Cache stale — provinces.png modified", "map_initialization");
                return result;
            }

            float startTime = Time.realtimeSinceStartup;

            try
            {
                using (var stream = new FileStream(fullCachePath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(stream))
                {
                    // Validate header
                    uint magic = reader.ReadUInt32();
                    uint version = reader.ReadUInt32();
                    if (magic != CACHE_MAGIC || version != CACHE_VERSION)
                        return result;

                    int pairCount = reader.ReadInt32();
                    int junctionCount = reader.ReadInt32();

                    // Read border pixels by pair
                    var bordersByPair = new Dictionary<(ushort, ushort), List<Vector2>>(pairCount);

                    for (int p = 0; p < pairCount; p++)
                    {
                        ushort provinceA = reader.ReadUInt16();
                        ushort provinceB = reader.ReadUInt16();
                        int pixelCount = reader.ReadInt32();

                        var pixels = new List<Vector2>(pixelCount);
                        for (int i = 0; i < pixelCount; i++)
                        {
                            ushort x = reader.ReadUInt16();
                            ushort y = reader.ReadUInt16();
                            pixels.Add(new Vector2(x, y));
                        }

                        bordersByPair[(provinceA, provinceB)] = pixels;
                    }

                    // Read junctions
                    var junctions = new Dictionary<Vector2, int>(junctionCount);
                    for (int j = 0; j < junctionCount; j++)
                    {
                        ushort x = reader.ReadUInt16();
                        ushort y = reader.ReadUInt16();
                        int count = reader.ReadInt32();
                        junctions[new Vector2(x, y)] = count;
                    }

                    float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
                    long fileSizeKB = stream.Length / 1024;

                    ArchonLogger.Log($"GPUBorderExtractor: Cache hit — {pairCount} pairs, {junctionCount} junctions loaded in {elapsed:F1}ms ({fileSizeKB}KB)", "map_initialization");

                    result.IsSuccess = true;
                    result.BorderPixelsByPair = bordersByPair;
                    result.JunctionPixels = junctions;
                }
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogWarning($"GPUBorderExtractor: Cache read failed: {e.Message}", "map_initialization");
            }

            return result;
        }

        /// <summary>
        /// Save border extraction data to disk cache.
        /// Stored as provinces.png.borders alongside the source image.
        /// </summary>
        /// <param name="provincesImagePath">Path to provinces.png - cache stored as provinces.png.borders</param>
        public static void SaveCache(
            string provincesImagePath,
            Dictionary<(ushort, ushort), List<Vector2>> bordersByPair,
            Dictionary<Vector2, int> junctions)
        {
            string fullCachePath = provincesImagePath + ".borders";

            try
            {
                float startTime = Time.realtimeSinceStartup;

                using (var stream = new FileStream(fullCachePath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(stream))
                {
                    // Header
                    writer.Write(CACHE_MAGIC);
                    writer.Write(CACHE_VERSION);
                    writer.Write(bordersByPair.Count);
                    writer.Write(junctions.Count);

                    // Border pixels by pair
                    foreach (var kvp in bordersByPair)
                    {
                        writer.Write(kvp.Key.Item1); // provinceA
                        writer.Write(kvp.Key.Item2); // provinceB
                        writer.Write(kvp.Value.Count); // pixel count

                        foreach (var pixel in kvp.Value)
                        {
                            writer.Write((ushort)pixel.x);
                            writer.Write((ushort)pixel.y);
                        }
                    }

                    // Junctions
                    foreach (var kvp in junctions)
                    {
                        writer.Write((ushort)kvp.Key.x);
                        writer.Write((ushort)kvp.Key.y);
                        writer.Write(kvp.Value);
                    }

                    float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
                    long fileSizeKB = stream.Length / 1024;

                    ArchonLogger.Log($"GPUBorderExtractor: Saved cache — {bordersByPair.Count} pairs, {junctions.Count} junctions ({fileSizeKB}KB) in {elapsed:F1}ms", "map_initialization");
                }
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogWarning($"GPUBorderExtractor: Cache save failed: {e.Message}", "map_initialization");
            }
        }
    }
}
