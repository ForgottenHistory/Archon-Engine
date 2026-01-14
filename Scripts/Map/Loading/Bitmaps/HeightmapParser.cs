using System;
using System.Runtime.CompilerServices;
using Unity.Collections;

namespace Map.Loading.Bitmaps
{
    /// <summary>
    /// Heightmap parser for terrain elevation data
    /// Typically uses grayscale values where darker = lower elevation
    /// </summary>
    public static class HeightmapParser
    {
        /// <summary>
        /// Heightmap parsing result
        /// </summary>
        public struct HeightmapResult
        {
            public BMPParser.BMPPixelData PixelData;
            public NativeArray<float> HeightData; // Normalized height values (0.0 - 1.0)
            public float MinHeight;
            public float MaxHeight;
            public float HeightRange;
            public bool IsSuccess;

            public int Width => PixelData.Header.Width;
            public int Height => PixelData.Header.Height;

            public void Dispose()
            {
                PixelData.Dispose();
                if (HeightData.IsCreated) HeightData.Dispose();
            }
        }

        /// <summary>
        /// Parse heightmap from grayscale BMP data
        /// </summary>
        public static HeightmapResult ParseHeightmap(NativeArray<byte> bmpFileData, Allocator allocator)
        {
            return ParseHeightmap(new NativeSlice<byte>(bmpFileData), allocator);
        }

        /// <summary>
        /// Parse heightmap from grayscale BMP data
        /// </summary>
        public static HeightmapResult ParseHeightmap(NativeSlice<byte> bmpFileData, Allocator allocator)
        {
            // Parse BMP header
            var bmpHeader = BMPParser.ParseHeader(bmpFileData);
            if (!bmpHeader.IsValid)
            {
                return new HeightmapResult { IsSuccess = false };
            }

            // Get pixel data
            var pixelData = BMPParser.GetPixelData(bmpFileData, bmpHeader);
            if (!pixelData.IsSuccess)
            {
                return new HeightmapResult { IsSuccess = false };
            }

            int width = pixelData.Header.Width;
            int height = pixelData.Header.Height;
            var heightData = new NativeArray<float>(width * height, allocator);

            // Extract height values from pixels
            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    float heightValue = ExtractHeightFromPixel(pixelData, x, y);

                    heightData[index] = heightValue;
                    minHeight = Math.Min(minHeight, heightValue);
                    maxHeight = Math.Max(maxHeight, heightValue);
                }
            }

            // Normalize height data to 0.0 - 1.0 range
            float heightRange = maxHeight - minHeight;
            if (heightRange > 0)
            {
                for (int i = 0; i < heightData.Length; i++)
                {
                    heightData[i] = (heightData[i] - minHeight) / heightRange;
                }
            }

            return new HeightmapResult
            {
                PixelData = pixelData,
                HeightData = heightData,
                MinHeight = minHeight,
                MaxHeight = maxHeight,
                HeightRange = heightRange,
                IsSuccess = true
            };
        }

        /// <summary>
        /// Get normalized height at specific coordinates (0.0 - 1.0)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetHeightAt(HeightmapResult heightmap, int x, int y, out float height)
        {
            height = 0f;

            if (!heightmap.IsSuccess || x < 0 || x >= heightmap.Width || y < 0 || y >= heightmap.Height)
                return false;

            int index = y * heightmap.Width + x;
            height = heightmap.HeightData[index];
            return true;
        }

        /// <summary>
        /// Get absolute height at specific coordinates (in original scale)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetAbsoluteHeightAt(HeightmapResult heightmap, int x, int y, out float absoluteHeight)
        {
            if (TryGetHeightAt(heightmap, x, y, out float normalizedHeight))
            {
                absoluteHeight = heightmap.MinHeight + (normalizedHeight * heightmap.HeightRange);
                return true;
            }

            absoluteHeight = 0f;
            return false;
        }

        /// <summary>
        /// Sample height with bilinear interpolation for smooth values
        /// </summary>
        public static bool TryGetInterpolatedHeight(HeightmapResult heightmap, float x, float y, out float height)
        {
            height = 0f;

            if (!heightmap.IsSuccess)
                return false;

            // Get integer coordinates and fractional parts
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            int x1 = x0 + 1;
            int y1 = y0 + 1;

            float fx = x - x0;
            float fy = y - y0;

            // Sample four corner heights
            bool h00Valid = TryGetHeightAt(heightmap, x0, y0, out float h00);
            bool h10Valid = TryGetHeightAt(heightmap, x1, y0, out float h10);
            bool h01Valid = TryGetHeightAt(heightmap, x0, y1, out float h01);
            bool h11Valid = TryGetHeightAt(heightmap, x1, y1, out float h11);

            // Need at least one valid sample
            if (!h00Valid && !h10Valid && !h01Valid && !h11Valid)
                return false;

            // Fill missing samples with valid ones (or 0)
            if (!h00Valid) h00 = h10Valid ? h10 : (h01Valid ? h01 : h11);
            if (!h10Valid) h10 = h00Valid ? h00 : (h11Valid ? h11 : h01);
            if (!h01Valid) h01 = h00Valid ? h00 : (h11Valid ? h11 : h10);
            if (!h11Valid) h11 = h10Valid ? h10 : (h01Valid ? h01 : h00);

            // Bilinear interpolation
            float h0 = h00 * (1 - fx) + h10 * fx;
            float h1 = h01 * (1 - fx) + h11 * fx;
            height = h0 * (1 - fy) + h1 * fy;

            return true;
        }

        /// <summary>
        /// Calculate terrain slope at specific coordinates
        /// Returns slope magnitude (0 = flat, 1+ = steep)
        /// </summary>
        public static bool TryGetSlope(HeightmapResult heightmap, int x, int y, out float slope)
        {
            slope = 0f;

            if (!heightmap.IsSuccess)
                return false;

            // Sample neighboring heights for gradient calculation
            bool centerValid = TryGetHeightAt(heightmap, x, y, out float center);
            bool leftValid = TryGetHeightAt(heightmap, x - 1, y, out float left);
            bool rightValid = TryGetHeightAt(heightmap, x + 1, y, out float right);
            bool upValid = TryGetHeightAt(heightmap, x, y - 1, out float up);
            bool downValid = TryGetHeightAt(heightmap, x, y + 1, out float down);

            if (!centerValid)
                return false;

            // Calculate gradients (use center value if neighbor is missing)
            float dx = 0f;
            if (leftValid && rightValid)
                dx = (right - left) * 0.5f;
            else if (rightValid)
                dx = right - center;
            else if (leftValid)
                dx = center - left;

            float dy = 0f;
            if (upValid && downValid)
                dy = (down - up) * 0.5f;
            else if (downValid)
                dy = down - center;
            else if (upValid)
                dy = center - up;

            // Calculate slope magnitude
            slope = (float)Math.Sqrt(dx * dx + dy * dy);
            return true;
        }

        /// <summary>
        /// Classify terrain based on height and slope
        /// </summary>
        public enum TerrainType : byte
        {
            Water = 0,      // Very low height
            Plains = 1,     // Low height, low slope
            Hills = 2,      // Medium height, medium slope
            Mountains = 3,  // High height or high slope
            Peaks = 4       // Very high height
        }

        /// <summary>
        /// Classify terrain type at specific coordinates
        /// </summary>
        public static bool TryGetTerrainType(HeightmapResult heightmap, int x, int y, out TerrainType terrainType)
        {
            terrainType = TerrainType.Water;

            if (!TryGetHeightAt(heightmap, x, y, out float height) ||
                !TryGetSlope(heightmap, x, y, out float slope))
                return false;

            // Simple classification thresholds
            if (height < 0.1f)
                terrainType = TerrainType.Water;
            else if (height < 0.3f && slope < 0.1f)
                terrainType = TerrainType.Plains;
            else if (height < 0.7f && slope < 0.3f)
                terrainType = TerrainType.Hills;
            else if (height < 0.9f || slope < 0.5f)
                terrainType = TerrainType.Mountains;
            else
                terrainType = TerrainType.Peaks;

            return true;
        }

        /// <summary>
        /// Extract height value from pixel (handles different formats)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ExtractHeightFromPixel(BMPParser.BMPPixelData pixelData, int x, int y)
        {
            if (BMPParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
            {
                // For grayscale heightmaps, use the average or just red channel
                // Higher values = higher elevation
                return (r + g + b) / (3.0f * 255.0f);
            }

            return 0f; // Default to sea level
        }

        /// <summary>
        /// Generate heightmap statistics
        /// </summary>
        public struct HeightmapStats
        {
            public float AverageHeight;
            public float MedianHeight;
            public int WaterPixels;
            public int PlainPixels;
            public int HillPixels;
            public int MountainPixels;
            public int PeakPixels;
            public bool IsValid;
        }

        /// <summary>
        /// Calculate heightmap statistics
        /// </summary>
        public static HeightmapStats CalculateStats(HeightmapResult heightmap)
        {
            if (!heightmap.IsSuccess)
                return new HeightmapStats { IsValid = false };

            float totalHeight = 0f;
            int totalPixels = heightmap.HeightData.Length;
            var terrainCounts = new int[5]; // For each TerrainType

            // Calculate average and terrain distribution
            for (int y = 0; y < heightmap.Height; y++)
            {
                for (int x = 0; x < heightmap.Width; x++)
                {
                    if (TryGetHeightAt(heightmap, x, y, out float height))
                    {
                        totalHeight += height;

                        if (TryGetTerrainType(heightmap, x, y, out TerrainType terrain))
                        {
                            terrainCounts[(int)terrain]++;
                        }
                    }
                }
            }

            return new HeightmapStats
            {
                AverageHeight = totalHeight / totalPixels,
                MedianHeight = 0.5f, // TODO: Implement proper median calculation
                WaterPixels = terrainCounts[0],
                PlainPixels = terrainCounts[1],
                HillPixels = terrainCounts[2],
                MountainPixels = terrainCounts[3],
                PeakPixels = terrainCounts[4],
                IsValid = true
            };
        }
    }
}