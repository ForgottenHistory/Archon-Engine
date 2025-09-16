using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using ProvinceSystem.Jobs;

namespace ProvinceSystem.Services
{
    /// <summary>
    /// Service for analyzing province maps using Unity Job System
    /// </summary>
    public class ProvinceAnalysisService
    {
        private const float BLACK_THRESHOLD = 2.55f; // 1% of 255
        
        public struct AnalysisResult
        {
            public Dictionary<Color, ProvinceInfo> provinces;
            public int totalProvinces;
            public JobHandle completionHandle;
        }
        
        public struct ProvinceInfo
        {
            public Color color;
            public List<Vector2Int> pixels;
            public Vector2 center;
            public Bounds bounds;
            public int pixelCount;
        }
        
        /// <summary>
        /// Analyze province map using parallel jobs
        /// </summary>
        public AnalysisResult AnalyzeProvinceMapParallel(Texture2D provinceMap, Texture2D politicalMap, 
            int minPixelsForProvince, int maxProvinces, bool limitProvinceCount)
        {
            int width = provinceMap.width;
            int height = provinceMap.height;
            Color32[] pixels = provinceMap.GetPixels32();
            
            // Step 1: Parallel pixel analysis to find unique province colors
            var uniqueColors = FindUniqueProvinceColors(pixels, width, height);
            
            // Step 2: Filter provinces by size
            var filteredProvinces = FilterProvincesBySize(uniqueColors, pixels, width, height, 
                minPixelsForProvince, maxProvinces, limitProvinceCount);
            
            // Step 3: Collect pixels for each province and calculate metrics
            var result = CollectProvinceDataParallel(filteredProvinces, pixels, politicalMap, width, height);
            
            return result;
        }
        
        private HashSet<uint> FindUniqueProvinceColors(Color32[] pixels, int width, int height)
        {
            // Create native arrays for job
            var nativePixels = new NativeArray<Color32>(pixels, Allocator.TempJob);
            var provincePixelCounts = new NativeHashMap<uint, int>(1000, Allocator.TempJob);
            
            // For accurate counting, use single-threaded job
            var countJob = new ProvincePixelCountJob
            {
                pixels = nativePixels,
                blackThreshold = BLACK_THRESHOLD,
                provincePixelCounts = provincePixelCounts
            };
            
            JobHandle countHandle = countJob.Schedule();
            countHandle.Complete();
            
            // Extract unique colors
            var uniqueColors = new HashSet<uint>();
            var keys = provincePixelCounts.GetKeyArray(Allocator.Temp);
            
            for (int i = 0; i < keys.Length; i++)
            {
                uniqueColors.Add(keys[i]);
            }
            
            keys.Dispose();
            nativePixels.Dispose();
            provincePixelCounts.Dispose();
            
            Debug.Log($"Found {uniqueColors.Count} unique province colors using Job System");
            
            return uniqueColors;
        }
        
        private List<uint> FilterProvincesBySize(HashSet<uint> uniqueColors, Color32[] pixels, 
            int width, int height, int minPixels, int maxProvinces, bool limitCount)
        {
            // Count pixels for each province
            Dictionary<uint, int> pixelCounts = new Dictionary<uint, int>();
            
            foreach (uint color in uniqueColors)
            {
                pixelCounts[color] = 0;
            }
            
            // Count pixels (this could also be parallelized if needed)
            for (int i = 0; i < pixels.Length; i++)
            {
                uint colorKey = ColorToUInt(pixels[i]);
                if (pixelCounts.ContainsKey(colorKey))
                {
                    pixelCounts[colorKey]++;
                }
            }
            
            // Filter by minimum size
            var filtered = pixelCounts.Where(kvp => kvp.Value >= minPixels)
                                     .OrderByDescending(kvp => kvp.Value)
                                     .Select(kvp => kvp.Key)
                                     .ToList();
            
            // Limit count if requested
            if (limitCount && filtered.Count > maxProvinces)
            {
                filtered = filtered.Take(maxProvinces).ToList();
                Debug.Log($"Limited to {maxProvinces} largest provinces");
            }
            
            return filtered;
        }
        
        private AnalysisResult CollectProvinceDataParallel(List<uint> provinceColors, Color32[] pixels,
            Texture2D politicalMap, int width, int height)
        {
            var result = new AnalysisResult
            {
                provinces = new Dictionary<Color, ProvinceInfo>(),
                totalProvinces = provinceColors.Count
            };
            
            // Create native collections
            var nativePixels = new NativeArray<Color32>(pixels, Allocator.TempJob);
            var nativeProvinceColors = new NativeArray<uint>(provinceColors.ToArray(), Allocator.TempJob);
            var provincePixels = new NativeParallelMultiHashMap<uint, int2>(pixels.Length, Allocator.TempJob);
            var provinceCenters = new NativeArray<float2>(provinceColors.Count, Allocator.TempJob);
            var provinceBounds = new NativeArray<int4>(provinceColors.Count, Allocator.TempJob);
            
            // Job 1: Collect pixels for each province
            var collectionJob = new ProvincePixelCollectionJob
            {
                pixels = nativePixels,
                provinceColorKeys = nativeProvinceColors,
                width = width,
                height = height,
                blackThreshold = BLACK_THRESHOLD,
                provincePixels = provincePixels
            };
            
            JobHandle collectionHandle = collectionJob.Schedule();
            
            // Job 2: Calculate metrics for each province
            var metricsJob = new ProvinceMetricsJob
            {
                provincePixels = provincePixels,
                provinceColorKeys = nativeProvinceColors,
                provinceCenters = provinceCenters,
                provinceBounds = provinceBounds
            };
            
            int metricsBatchSize = Mathf.Max(1, provinceColors.Count / SystemInfo.processorCount);
            JobHandle metricsHandle = metricsJob.Schedule(provinceColors.Count, metricsBatchSize, collectionHandle);
            
            // Wait for completion
            metricsHandle.Complete();
            
            // Convert results back to managed data
            for (int i = 0; i < provinceColors.Count; i++)
            {
                uint colorKey = provinceColors[i];
                Color color = UIntToColor(colorKey);
                
                var provinceInfo = new ProvinceInfo
                {
                    color = color,
                    center = new Vector2(provinceCenters[i].x, provinceCenters[i].y),
                    pixels = new List<Vector2Int>()
                };
                
                // Collect pixels for this province  
                if (provincePixels.TryGetFirstValue(colorKey, out int2 pixel, out var iterator))
                {
                    do
                    {
                        provinceInfo.pixels.Add(new Vector2Int(pixel.x, pixel.y));
                    }
                    while (provincePixels.TryGetNextValue(out pixel, ref iterator));
                }
                
                provinceInfo.pixelCount = provinceInfo.pixels.Count;
                
                // Calculate bounds in world space
                int4 bounds = provinceBounds[i];
                float pixelToWorldX = 1.0f; // This should be passed in
                float pixelToWorldZ = 1.0f;
                
                Vector3 min = new Vector3(bounds.x * pixelToWorldX, 0, bounds.y * pixelToWorldZ);
                Vector3 max = new Vector3(bounds.z * pixelToWorldX, 0, bounds.w * pixelToWorldZ);
                provinceInfo.bounds = new Bounds();
                provinceInfo.bounds.SetMinMax(min, max);
                
                result.provinces[color] = provinceInfo;
            }
            
            // Cleanup
            nativePixels.Dispose();
            nativeProvinceColors.Dispose();
            provincePixels.Dispose();
            provinceCenters.Dispose();
            provinceBounds.Dispose();
            
            result.completionHandle = metricsHandle;
            
            return result;
        }
        
        /// <summary>
        /// Find border pixels using parallel job
        /// </summary>
        public List<Vector2Int> FindBorderPixelsParallel(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            int width = texture.width;
            int height = texture.height;
            
            var nativePixels = new NativeArray<Color32>(pixels, Allocator.TempJob);
            var isBorderPixel = new NativeArray<bool>(pixels.Length, Allocator.TempJob);
            
            var borderJob = new BorderDetectionJob
            {
                pixels = nativePixels,
                width = width,
                height = height,
                isBorderPixel = isBorderPixel
            };
            
            int batchSize = Mathf.Max(1, pixels.Length / (SystemInfo.processorCount * 4));
            JobHandle borderHandle = borderJob.Schedule(pixels.Length, batchSize);
            borderHandle.Complete();
            
            // Collect border pixels
            List<Vector2Int> borderPixels = new List<Vector2Int>();
            for (int i = 0; i < isBorderPixel.Length; i++)
            {
                if (isBorderPixel[i])
                {
                    int x = i % width;
                    int y = i / width;
                    borderPixels.Add(new Vector2Int(x, y));
                }
            }
            
            nativePixels.Dispose();
            isBorderPixel.Dispose();
            
            return borderPixels;
        }
        
        private uint ColorToUInt(Color32 c)
        {
            return ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | (uint)c.a;
        }
        
        private Color UIntToColor(uint value)
        {
            byte r = (byte)((value >> 24) & 0xFF);
            byte g = (byte)((value >> 16) & 0xFF);
            byte b = (byte)((value >> 8) & 0xFF);
            byte a = (byte)(value & 0xFF);
            return new Color32(r, g, b, a);
        }
    }
}