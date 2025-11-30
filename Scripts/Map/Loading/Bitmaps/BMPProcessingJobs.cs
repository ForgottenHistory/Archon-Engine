using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Map.Loading.Bitmaps
{
    /// <summary>
    /// Burst-compiled job for parallel BMP pixel processing
    /// Processes chunks of pixels to collect unique colors
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct BMPColorCollectionJob : IJobParallelForBatch
    {
        [ReadOnly] public BMPParser.BMPPixelData PixelData;
        [ReadOnly] public int Width;
        [ReadOnly] public int Height;

        // Use a thread-safe approach for batch results
        public NativeArray<int> TempResults;
        public NativeArray<int> ResultCounts;

        public void Execute(int startIndex, int count)
        {
            var localColors = new NativeHashSet<int>(100, Allocator.Temp);

            try
            {
                int endIndex = startIndex + count;

                for (int pixelIndex = startIndex; pixelIndex < endIndex && pixelIndex < Width * Height; pixelIndex++)
                {
                    int x = pixelIndex % Width;
                    int y = pixelIndex / Width;

                    if (BMPParser.TryGetPixelRGBPacked(PixelData, x, y, out int rgb))
                    {
                        localColors.Add(rgb);
                    }
                }

                // Write unique colors to temp array starting at batch offset
                int batchIndex = startIndex / count;
                int writeIndex = batchIndex * 256; // Assume max 256 unique colors per batch
                int colorIndex = 0;

                var enumerator = localColors.GetEnumerator();
                while (enumerator.MoveNext() && colorIndex < 256)
                {
                    TempResults[writeIndex + colorIndex] = enumerator.Current;
                    colorIndex++;
                }

                ResultCounts[batchIndex] = colorIndex;
            }
            finally
            {
                if (localColors.IsCreated)
                    localColors.Dispose();
            }
        }
    }

    /// <summary>
    /// Burst-compiled job for finding pixels with specific color
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct BMPPixelSearchJob : IJobParallelForBatch
    {
        [ReadOnly] public BMPParser.BMPPixelData PixelData;
        [ReadOnly] public int Width;
        [ReadOnly] public int Height;
        [ReadOnly] public int TargetRGB;

        // Each batch writes matches to its own list
        [WriteOnly] public NativeArray<NativeList<PixelCoord>> BatchResults;

        public void Execute(int startIndex, int count)
        {
            var batchMatches = new NativeList<PixelCoord>(50, Allocator.Temp);

            int endIndex = startIndex + count;

            for (int pixelIndex = startIndex; pixelIndex < endIndex && pixelIndex < Width * Height; pixelIndex++)
            {
                int x = pixelIndex % Width;
                int y = pixelIndex / Width;

                if (BMPParser.TryGetPixelRGBPacked(PixelData, x, y, out int rgb) && rgb == TargetRGB)
                {
                    batchMatches.Add(new PixelCoord(x, y));
                }
            }

            // Store the batch result
            BatchResults[Unity.Jobs.LowLevel.Unsafe.JobsUtility.ThreadIndex] = batchMatches;
        }
    }

    /// <summary>
    /// Burst-compiled job for parallel province statistics calculation
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct BMPProvinceStatsJob : IJobParallelForBatch
    {
        [ReadOnly] public BMPParser.BMPPixelData PixelData;
        [ReadOnly] public int Width;
        [ReadOnly] public int Height;
        [ReadOnly] public NativeHashMap<int, int> ColorToProvinceID;

        // Results for each batch
        [WriteOnly] public NativeArray<BMPProvinceStatsData> BatchResults;

        public void Execute(int startIndex, int count)
        {
            var batchStats = new BMPProvinceStatsData
            {
                ProvincePixelCounts = new NativeHashMap<int, int>(100, Allocator.Temp),
                ProvinceBounds = new NativeHashMap<int, ProvinceBounds>(100, Allocator.Temp)
            };

            int endIndex = startIndex + count;

            for (int pixelIndex = startIndex; pixelIndex < endIndex && pixelIndex < Width * Height; pixelIndex++)
            {
                int x = pixelIndex % Width;
                int y = pixelIndex / Width;

                if (BMPParser.TryGetPixelRGBPacked(PixelData, x, y, out int rgb) &&
                    ColorToProvinceID.TryGetValue(rgb, out int provinceID))
                {
                    // Update pixel count
                    if (batchStats.ProvincePixelCounts.TryGetValue(provinceID, out int pixelCount))
                        batchStats.ProvincePixelCounts[provinceID] = pixelCount + 1;
                    else
                        batchStats.ProvincePixelCounts[provinceID] = 1;

                    // Update bounds
                    if (batchStats.ProvinceBounds.TryGetValue(provinceID, out var bounds))
                    {
                        bounds.MinX = x < bounds.MinX ? x : bounds.MinX;
                        bounds.MinY = y < bounds.MinY ? y : bounds.MinY;
                        bounds.MaxX = x > bounds.MaxX ? x : bounds.MaxX;
                        bounds.MaxY = y > bounds.MaxY ? y : bounds.MaxY;
                        bounds.SumX += x;
                        bounds.SumY += y;
                        batchStats.ProvinceBounds[provinceID] = bounds;
                    }
                    else
                    {
                        batchStats.ProvinceBounds[provinceID] = new ProvinceBounds
                        {
                            MinX = x, MinY = y, MaxX = x, MaxY = y,
                            SumX = x, SumY = y
                        };
                    }
                }
            }

            BatchResults[Unity.Jobs.LowLevel.Unsafe.JobsUtility.ThreadIndex] = batchStats;
        }
    }

    /// <summary>
    /// Data structure for collecting province statistics across job batches
    /// </summary>
    public struct BMPProvinceStatsData
    {
        public NativeHashMap<int, int> ProvincePixelCounts;
        public NativeHashMap<int, ProvinceBounds> ProvinceBounds;

        public void Dispose()
        {
            if (ProvincePixelCounts.IsCreated) ProvincePixelCounts.Dispose();
            if (ProvinceBounds.IsCreated) ProvinceBounds.Dispose();
        }
    }

    /// <summary>
    /// Bounding box data for a province
    /// </summary>
    public struct ProvinceBounds
    {
        public int MinX, MinY, MaxX, MaxY;
        public long SumX, SumY; // For centroid calculation
    }
}