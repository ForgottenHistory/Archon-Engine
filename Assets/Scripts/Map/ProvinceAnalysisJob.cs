using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace ProvinceSystem.Jobs
{
    /// <summary>
    /// Job for analyzing province map pixels in parallel
    /// </summary>
    [BurstCompile]
    public struct ProvincePixelAnalysisJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> pixels;
        [ReadOnly] public int width;
        [ReadOnly] public int height;
        [ReadOnly] public float blackThreshold;
        
        [WriteOnly]
        [NativeDisableParallelForRestriction]
        public NativeParallelHashMap<uint, int>.ParallelWriter provincePixelCounts;
        
        public void Execute(int index)
        {
            Color32 pixel = pixels[index];
            
            // Skip near-black pixels (ocean/borders)
            if (pixel.r < blackThreshold && pixel.g < blackThreshold && pixel.b < blackThreshold)
                return;
            
            // Round colors to avoid floating point issues
            uint colorKey = ColorToUInt(pixel);
            
            // Try to add the color (will fail if it already exists, which is fine)
            provincePixelCounts.TryAdd(colorKey, 1);
        }
        
        private uint ColorToUInt(Color32 c)
        {
            return ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | (uint)c.a;
        }
    }
    
    /// <summary>
    /// Single-threaded job for accurate pixel counting
    /// </summary>
    [BurstCompile]
    public struct ProvincePixelCountJob : IJob
    {
        [ReadOnly] public NativeArray<Color32> pixels;
        [ReadOnly] public float blackThreshold;
        
        public NativeHashMap<uint, int> provincePixelCounts;
        
        public void Execute()
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                
                // Skip near-black pixels
                if (pixel.r < blackThreshold && pixel.g < blackThreshold && pixel.b < blackThreshold)
                    continue;
                
                uint colorKey = ColorToUInt(pixel);
                
                if (provincePixelCounts.TryGetValue(colorKey, out int count))
                {
                    provincePixelCounts[colorKey] = count + 1;
                }
                else
                {
                    provincePixelCounts.Add(colorKey, 1);
                }
            }
        }
        
        private uint ColorToUInt(Color32 c)
        {
            return ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | (uint)c.a;
        }
    }
    
    /// <summary>
    /// Job for collecting pixels belonging to each province
    /// </summary>
    [BurstCompile]
    public struct ProvincePixelCollectionJob : IJob
    {
        [ReadOnly] public NativeArray<Color32> pixels;
        [ReadOnly] public NativeArray<uint> provinceColorKeys;
        [ReadOnly] public int width;
        [ReadOnly] public int height;
        [ReadOnly] public float blackThreshold;
        
        public NativeParallelMultiHashMap<uint, int2> provincePixels;
        
        public void Execute()
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    Color32 pixel = pixels[index];
                    
                    // Skip near-black pixels
                    if (pixel.r < blackThreshold && pixel.g < blackThreshold && pixel.b < blackThreshold)
                        continue;
                    
                    uint colorKey = ColorToUInt(pixel);
                    
                    // Check if this color is in our province list
                    for (int i = 0; i < provinceColorKeys.Length; i++)
                    {
                        if (provinceColorKeys[i] == colorKey)
                        {
                            provincePixels.Add(colorKey, new int2(x, y));
                            break;
                        }
                    }
                }
            }
        }
        
        private uint ColorToUInt(Color32 c)
        {
            return ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | (uint)c.a;
        }
    }
    
    /// <summary>
    /// Job for calculating province centers and bounds
    /// </summary>
    [BurstCompile]
    public struct ProvinceMetricsJob : IJobParallelFor
    {
        [ReadOnly] public NativeParallelMultiHashMap<uint, int2> provincePixels;
        [ReadOnly] public NativeArray<uint> provinceColorKeys;
        
        [WriteOnly] public NativeArray<float2> provinceCenters;
        [WriteOnly] public NativeArray<int4> provinceBounds; // minX, minY, maxX, maxY
        
        public void Execute(int index)
        {
            uint colorKey = provinceColorKeys[index];
            
            float2 sum = float2.zero;
            int count = 0;
            int4 bounds = new int4(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
            
            if (provincePixels.TryGetFirstValue(colorKey, out int2 pixel, out var iterator))
            {
                do
                {
                    sum += new float2(pixel.x, pixel.y);
                    count++;
                    
                    bounds.x = math.min(bounds.x, pixel.x); // minX
                    bounds.y = math.min(bounds.y, pixel.y); // minY
                    bounds.z = math.max(bounds.z, pixel.x); // maxX
                    bounds.w = math.max(bounds.w, pixel.y); // maxY
                }
                while (provincePixels.TryGetNextValue(out pixel, ref iterator));
            }
            
            if (count > 0)
            {
                provinceCenters[index] = sum / count;
                provinceBounds[index] = bounds;
            }
            else
            {
                provinceCenters[index] = float2.zero;
                provinceBounds[index] = int4.zero;
            }
        }
    }
    
    /// <summary>
    /// Job for finding border pixels
    /// </summary>
    [BurstCompile]
    public struct BorderDetectionJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Color32> pixels;
        [ReadOnly] public int width;
        [ReadOnly] public int height;
        
        [WriteOnly] public NativeArray<bool> isBorderPixel;
        
        public void Execute(int index)
        {
            int x = index % width;
            int y = index / width;
            
            Color32 currentPixel = pixels[index];
            
            // Check 4-connectivity neighbors
            bool isBorder = false;
            
            // Check right
            if (x < width - 1)
            {
                Color32 rightPixel = pixels[index + 1];
                if (!ColorsEqual(currentPixel, rightPixel))
                    isBorder = true;
            }
            
            // Check bottom
            if (!isBorder && y < height - 1)
            {
                Color32 bottomPixel = pixels[index + width];
                if (!ColorsEqual(currentPixel, bottomPixel))
                    isBorder = true;
            }
            
            // Check left
            if (!isBorder && x > 0)
            {
                Color32 leftPixel = pixels[index - 1];
                if (!ColorsEqual(currentPixel, leftPixel))
                    isBorder = true;
            }
            
            // Check top
            if (!isBorder && y > 0)
            {
                Color32 topPixel = pixels[index - width];
                if (!ColorsEqual(currentPixel, topPixel))
                    isBorder = true;
            }
            
            isBorderPixel[index] = isBorder;
        }
        
        private bool ColorsEqual(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b;
        }
    }
    
    /// <summary>
    /// Job for merging adjacent pixels into rectangles for optimized mesh generation
    /// </summary>
    [BurstCompile]
    public struct RectangleMergeJob : IJob
    {
        [ReadOnly] public NativeParallelMultiHashMap<uint, int2> provincePixels;
        [ReadOnly] public uint colorKey;
        [ReadOnly] public int width;
        [ReadOnly] public int height;
        
        public NativeList<int4> mergedRectangles; // x, y, width, height
        
        public void Execute()
        {
            // Create a grid to track processed pixels
            NativeHashSet<int2> processedPixels = new NativeHashSet<int2>(1024, Allocator.Temp);
            NativeHashSet<int2> provincePixelSet = new NativeHashSet<int2>(1024, Allocator.Temp);
            
            // Build pixel set for this province
            if (provincePixels.TryGetFirstValue(colorKey, out int2 pixel, out var iterator))
            {
                do
                {
                    provincePixelSet.Add(pixel);
                }
                while (provincePixels.TryGetNextValue(out pixel, ref iterator));
            }
            
            // Scan and merge rectangles
            var enumerator = provincePixelSet.GetEnumerator();
            while (enumerator.MoveNext())
            {
                pixel = enumerator.Current;
                
                if (processedPixels.Contains(pixel))
                    continue;
                
                // Find horizontal run
                int runLength = 1;
                while (provincePixelSet.Contains(new int2(pixel.x + runLength, pixel.y)) &&
                       !processedPixels.Contains(new int2(pixel.x + runLength, pixel.y)))
                {
                    runLength++;
                }
                
                // Try to extend vertically
                int runHeight = 1;
                bool canExtend = true;
                
                while (canExtend && runHeight < 10) // Limit for performance
                {
                    for (int x = pixel.x; x < pixel.x + runLength; x++)
                    {
                        int2 testPixel = new int2(x, pixel.y + runHeight);
                        if (!provincePixelSet.Contains(testPixel) || processedPixels.Contains(testPixel))
                        {
                            canExtend = false;
                            break;
                        }
                    }
                    if (canExtend) runHeight++;
                }
                
                // Mark pixels as processed
                for (int y = pixel.y; y < pixel.y + runHeight; y++)
                {
                    for (int x = pixel.x; x < pixel.x + runLength; x++)
                    {
                        processedPixels.Add(new int2(x, y));
                    }
                }
                
                // Add merged rectangle
                mergedRectangles.Add(new int4(pixel.x, pixel.y, runLength, runHeight));
            }
            
            enumerator.Dispose();
            processedPixels.Dispose();
            provincePixelSet.Dispose();
        }
    }
}