using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using System.Runtime.CompilerServices;
using Map.Loading;

namespace Map.Province
{
    /// <summary>
    /// Advanced province metadata generation system
    /// Calculates size, center of mass, connectivity, convex hulls, and terrain classification
    /// </summary>
    public static class ProvinceMetadataGenerator
    {
        /// <summary>
        /// Complete province metadata
        /// </summary>
        public struct ProvinceMetadata
        {
            public ushort ProvinceID;
            public int PixelCount;
            public float2 CenterOfMass;
            public float2 GeometricCenter;
            public ProvinceBounds BoundingBox;
            public bool HasMultipleParts;
            public int DisconnectedPartsCount;
            public NativeArray<float2> ConvexHull;
            public TerrainType TerrainType;
            public float Compactness;
            public float AspectRatio;

            public void Dispose()
            {
                if (ConvexHull.IsCreated) ConvexHull.Dispose();
            }
        }

        /// <summary>
        /// Province bounding box information
        /// </summary>
        public struct ProvinceBounds
        {
            public int2 Min;
            public int2 Max;
            public int2 Size => Max - Min + new int2(1, 1);
            public int Area => Size.x * Size.y;
            public float2 Center => (float2)(Min + Max) / 2f;
        }

        /// <summary>
        /// Terrain classification for provinces
        /// </summary>
        public enum TerrainType : byte
        {
            Plains = 0,
            Hills = 1,
            Mountains = 2,
            Forest = 3,
            Desert = 4,
            Swamp = 5,
            Lake = 6,
            Coastal = 7,
            Island = 8,
            Impassable = 255
        }

        /// <summary>
        /// Metadata generation result
        /// </summary>
        public struct MetadataResult
        {
            public bool IsSuccess;
            public NativeHashMap<ushort, ProvinceMetadata> ProvinceMetadata;
            public string ErrorMessage;

            public void Dispose()
            {
                if (ProvinceMetadata.IsCreated)
                {
                    // Dispose individual metadata
                    foreach (var kvp in ProvinceMetadata)
                    {
                        var metadata = kvp.Value;
                        metadata.Dispose();
                    }
                    ProvinceMetadata.Dispose();
                }
            }
        }

        /// <summary>
        /// Generate comprehensive metadata for all provinces
        /// </summary>
        /// <param name="loadResult">Province map load result</param>
        /// <param name="neighborResult">Neighbor detection result</param>
        /// <param name="generateConvexHulls">Generate convex hulls for label placement</param>
        /// <returns>Complete metadata result</returns>
        public static MetadataResult GenerateMetadata(ProvinceMapLoader.LoadResult loadResult,
            ProvinceNeighborDetector.NeighborResult neighborResult, bool generateConvexHulls = true)
        {
            var result = new MetadataResult();

            if (!loadResult.IsSuccess)
            {
                result.ErrorMessage = "Invalid load result provided";
                return result;
            }

            ArchonLogger.Log($"Generating metadata for {loadResult.ProvinceCount} provinces...", "map_textures");

            try
            {
                // Initialize metadata storage
                result.ProvinceMetadata = new NativeHashMap<ushort, ProvinceMetadata>(loadResult.ProvinceCount, Allocator.Persistent);

                // Group pixels by province
                var provincePixelGroups = GroupPixelsByProvince(loadResult);

                // Process each province
                foreach (var kvp in provincePixelGroups)
                {
                    ushort provinceID = kvp.Key;
                    var pixels = kvp.Value;

                    if (provinceID == 0 || pixels.Length == 0) // Skip ocean
                        continue;

                    var metadata = GenerateProvinceMetadata(provinceID, pixels, neighborResult, generateConvexHulls);
                    result.ProvinceMetadata.TryAdd(provinceID, metadata);
                }

                // Clean up temporary data
                foreach (var kvp in provincePixelGroups)
                {
                    if (kvp.Value.IsCreated) kvp.Value.Dispose();
                }
                provincePixelGroups.Dispose();

                result.IsSuccess = true;
                LogMetadataStatistics(result);

            }
            catch (System.Exception e)
            {
                result.ErrorMessage = $"Metadata generation failed: {e.Message}";
                result.Dispose();
            }

            return result;
        }

        /// <summary>
        /// Group pixels by province ID for processing
        /// </summary>
        private static NativeHashMap<ushort, NativeList<int2>> GroupPixelsByProvince(ProvinceMapLoader.LoadResult loadResult)
        {
            var provincePixelGroups = new NativeHashMap<ushort, NativeList<int2>>(loadResult.ProvinceCount, Allocator.Temp);

            // Initialize lists for each province
            foreach (var kvp in loadResult.ColorToID)
            {
                ushort provinceID = kvp.Value;
                if (provinceID > 0) // Skip ocean
                {
                    provincePixelGroups.TryAdd(provinceID, new NativeList<int2>(Allocator.Temp));
                }
            }

            // Group pixels
            for (int i = 0; i < loadResult.ProvincePixels.Length; i++)
            {
                var pixel = loadResult.ProvincePixels[i];
                if (pixel.ProvinceID > 0 && provincePixelGroups.TryGetValue(pixel.ProvinceID, out var list))
                {
                    list.Add(pixel.Position);
                }
            }

            return provincePixelGroups;
        }

        /// <summary>
        /// Generate complete metadata for a single province
        /// </summary>
        private static ProvinceMetadata GenerateProvinceMetadata(ushort provinceID, NativeList<int2> pixels,
            ProvinceNeighborDetector.NeighborResult neighborResult, bool generateConvexHulls)
        {
            var metadata = new ProvinceMetadata
            {
                ProvinceID = provinceID,
                PixelCount = pixels.Length
            };

            // Calculate bounding box and centers
            CalculateBoundsAndCenters(pixels, out metadata.BoundingBox, out metadata.CenterOfMass, out metadata.GeometricCenter);

            // Calculate shape metrics
            CalculateShapeMetrics(pixels, metadata.BoundingBox, out metadata.Compactness, out metadata.AspectRatio);

            // Detect disconnected parts
            DetectDisconnectedParts(pixels, out metadata.HasMultipleParts, out metadata.DisconnectedPartsCount);

            // Generate convex hull for label placement
            if (generateConvexHulls)
            {
                metadata.ConvexHull = GenerateConvexHull(pixels);
            }
            else
            {
                metadata.ConvexHull = new NativeArray<float2>(0, Allocator.Persistent);
            }

            // Classify terrain type
            metadata.TerrainType = ClassifyTerrain(metadata, neighborResult);

            return metadata;
        }

        /// <summary>
        /// Calculate bounding box, center of mass, and geometric center
        /// </summary>
        private static void CalculateBoundsAndCenters(NativeList<int2> pixels, out ProvinceBounds bounds,
            out float2 centerOfMass, out float2 geometricCenter)
        {
            // Initialize bounds
            int2 min = new int2(int.MaxValue);
            int2 max = new int2(int.MinValue);

            // Calculate bounds and sum for center of mass
            long sumX = 0, sumY = 0;

            for (int i = 0; i < pixels.Length; i++)
            {
                int2 pixel = pixels[i];
                min = math.min(min, pixel);
                max = math.max(max, pixel);
                sumX += pixel.x;
                sumY += pixel.y;
            }

            bounds = new ProvinceBounds { Min = min, Max = max };
            centerOfMass = new float2((float)sumX / pixels.Length, (float)sumY / pixels.Length);
            geometricCenter = bounds.Center;
        }

        /// <summary>
        /// Calculate shape metrics (compactness and aspect ratio)
        /// </summary>
        private static void CalculateShapeMetrics(NativeList<int2> pixels, ProvinceBounds bounds,
            out float compactness, out float aspectRatio)
        {
            // Aspect ratio: width / height
            aspectRatio = (float)bounds.Size.x / bounds.Size.y;

            // Compactness: actual pixels / bounding box area (0-1, 1 = perfect rectangle)
            compactness = (float)pixels.Length / bounds.Area;
        }

        /// <summary>
        /// Detect if province has multiple disconnected parts using flood fill
        /// </summary>
        private static void DetectDisconnectedParts(NativeList<int2> pixels, out bool hasMultipleParts, out int partsCount)
        {
            if (pixels.Length == 0)
            {
                hasMultipleParts = false;
                partsCount = 0;
                return;
            }

            // Create pixel lookup for fast neighbor checking
            var pixelSet = new NativeHashSet<int2>(pixels.Length, Allocator.Temp);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixelSet.Add(pixels[i]);
            }

            var visited = new NativeHashSet<int2>(pixels.Length, Allocator.Temp);
            var floodFillStack = new NativeList<int2>(pixels.Length / 4, Allocator.Temp);
            partsCount = 0;

            // Find all connected components
            for (int i = 0; i < pixels.Length; i++)
            {
                int2 pixel = pixels[i];
                if (visited.Contains(pixel))
                    continue;

                // Start new connected component
                partsCount++;
                FloodFill(pixel, pixelSet, visited, floodFillStack);
            }

            hasMultipleParts = partsCount > 1;

            // Clean up
            pixelSet.Dispose();
            visited.Dispose();
            floodFillStack.Dispose();
        }

        /// <summary>
        /// Flood fill to find connected component
        /// </summary>
        private static void FloodFill(int2 startPixel, NativeHashSet<int2> pixelSet, NativeHashSet<int2> visited, NativeList<int2> stack)
        {
            stack.Clear();
            stack.Add(startPixel);

            // 4-way connectivity
            var directions = new NativeArray<int2>(4, Allocator.Temp)
            {
                [0] = new int2(1, 0),
                [1] = new int2(-1, 0),
                [2] = new int2(0, 1),
                [3] = new int2(0, -1)
            };

            while (stack.Length > 0)
            {
                int2 current = stack[stack.Length - 1];
                stack.RemoveAtSwapBack(stack.Length - 1);

                if (visited.Contains(current))
                    continue;

                visited.Add(current);

                // Check all 4 neighbors
                for (int i = 0; i < 4; i++)
                {
                    int2 neighbor = current + directions[i];
                    if (pixelSet.Contains(neighbor) && !visited.Contains(neighbor))
                    {
                        stack.Add(neighbor);
                    }
                }
            }

            directions.Dispose();
        }

        /// <summary>
        /// Generate convex hull using Graham scan algorithm
        /// </summary>
        private static NativeArray<float2> GenerateConvexHull(NativeList<int2> pixels)
        {
            if (pixels.Length < 3)
            {
                // Convert to float2 and return all points
                var allPoints = new NativeArray<float2>(pixels.Length, Allocator.Persistent);
                for (int i = 0; i < pixels.Length; i++)
                {
                    allPoints[i] = new float2(pixels[i].x, pixels[i].y);
                }
                return allPoints;
            }

            // Convert to float2 and find unique points
            var points = new NativeList<float2>(pixels.Length, Allocator.Temp);
            var pointSet = new NativeHashSet<float2>(pixels.Length, Allocator.Temp);

            for (int i = 0; i < pixels.Length; i++)
            {
                float2 point = new float2(pixels[i].x, pixels[i].y);
                if (pointSet.Add(point))
                {
                    points.Add(point);
                }
            }

            var hull = ComputeConvexHull(points);

            // Clean up
            points.Dispose();
            pointSet.Dispose();

            return hull;
        }

        /// <summary>
        /// Compute convex hull using Graham scan
        /// </summary>
        private static NativeArray<float2> ComputeConvexHull(NativeList<float2> points)
        {
            if (points.Length < 3)
            {
                return new NativeArray<float2>(points.AsArray(), Allocator.Persistent);
            }

            // Find bottom-most point (and leftmost if tie)
            int bottom = 0;
            for (int i = 1; i < points.Length; i++)
            {
                if (points[i].y < points[bottom].y ||
                    (math.abs(points[i].y - points[bottom].y) < 0.001f && points[i].x < points[bottom].x))
                {
                    bottom = i;
                }
            }

            // Swap to first position
            if (bottom != 0)
            {
                var temp = points[0];
                points[0] = points[bottom];
                points[bottom] = temp;
            }

            // Sort by polar angle
            SortByPolarAngle(points);

            // Build convex hull
            var hull = new NativeList<float2>(points.Length, Allocator.Temp);
            hull.Add(points[0]);
            hull.Add(points[1]);

            for (int i = 2; i < points.Length; i++)
            {
                // Remove points that create right turn
                while (hull.Length > 1 && CrossProduct(hull[hull.Length - 2], hull[hull.Length - 1], points[i]) <= 0)
                {
                    hull.RemoveAtSwapBack(hull.Length - 1);
                }
                hull.Add(points[i]);
            }

            var result = new NativeArray<float2>(hull.AsArray(), Allocator.Persistent);
            hull.Dispose();
            return result;
        }

        /// <summary>
        /// Sort points by polar angle relative to first point
        /// </summary>
        private static void SortByPolarAngle(NativeList<float2> points)
        {
            float2 pivot = points[0];

            // Simple insertion sort (efficient for typical province sizes)
            for (int i = 2; i < points.Length; i++)
            {
                float2 key = points[i];
                int j = i - 1;

                while (j >= 1 && PolarAngleCompare(pivot, points[j], key) > 0)
                {
                    points[j + 1] = points[j];
                    j--;
                }
                points[j + 1] = key;
            }
        }

        /// <summary>
        /// Compare two points by polar angle relative to pivot
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PolarAngleCompare(float2 pivot, float2 a, float2 b)
        {
            float cross = CrossProduct(pivot, a, b);
            if (math.abs(cross) < 0.001f) // Collinear
            {
                float distA = math.distancesq(pivot, a);
                float distB = math.distancesq(pivot, b);
                return distA.CompareTo(distB);
            }
            return cross > 0 ? -1 : 1;
        }

        /// <summary>
        /// Calculate cross product for three points
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float CrossProduct(float2 a, float2 b, float2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        /// <summary>
        /// Classify province terrain type based on characteristics and neighbors
        /// </summary>
        private static TerrainType ClassifyTerrain(ProvinceMetadata metadata, ProvinceNeighborDetector.NeighborResult neighborResult)
        {
            // Check if coastal
            if (neighborResult.IsSuccess && neighborResult.CoastalProvinces.Contains(metadata.ProvinceID))
            {
                // Small coastal provinces might be islands
                if (metadata.PixelCount < 50)
                    return TerrainType.Island;
                return TerrainType.Coastal;
            }

            // Check if very small (likely impassable)
            if (metadata.PixelCount < 10)
                return TerrainType.Impassable;

            // Check compactness and aspect ratio for terrain classification
            if (metadata.Compactness < 0.3f)
            {
                // Very fragmented - likely mountains or forest
                return metadata.AspectRatio > 3f ? TerrainType.Mountains : TerrainType.Forest;
            }

            // Check for elongated shapes (rivers, valleys)
            if (metadata.AspectRatio > 5f)
                return TerrainType.Swamp;

            // Default classification based on size
            if (metadata.PixelCount > 1000)
                return TerrainType.Plains;
            else if (metadata.PixelCount > 100)
                return TerrainType.Hills;

            return TerrainType.Plains;
        }

        /// <summary>
        /// Log comprehensive metadata statistics
        /// </summary>
        private static void LogMetadataStatistics(MetadataResult result)
        {
            if (!result.IsSuccess) return;

            int totalProvinces = result.ProvinceMetadata.Count;
            int totalPixels = 0;
            int disconnectedProvinces = 0;
            int coastalProvinces = 0;
            var terrainCounts = new System.Collections.Generic.Dictionary<TerrainType, int>();

            float minCompactness = float.MaxValue;
            float maxCompactness = 0f;
            float avgCompactness = 0f;

            foreach (var kvp in result.ProvinceMetadata)
            {
                var metadata = kvp.Value;
                totalPixels += metadata.PixelCount;

                if (metadata.HasMultipleParts)
                    disconnectedProvinces++;

                if (metadata.TerrainType == TerrainType.Coastal || metadata.TerrainType == TerrainType.Island)
                    coastalProvinces++;

                if (terrainCounts.ContainsKey(metadata.TerrainType))
                    terrainCounts[metadata.TerrainType]++;
                else
                    terrainCounts[metadata.TerrainType] = 1;

                minCompactness = math.min(minCompactness, metadata.Compactness);
                maxCompactness = math.max(maxCompactness, metadata.Compactness);
                avgCompactness += metadata.Compactness;
            }

            avgCompactness /= totalProvinces;

            ArchonLogger.Log($"Province Metadata Statistics:", "map_textures");
            ArchonLogger.Log($"  Total Provinces: {totalProvinces}", "map_textures");
            ArchonLogger.Log($"  Total Pixels: {totalPixels}", "map_textures");
            ArchonLogger.Log($"  Average Province Size: {(float)totalPixels / totalProvinces:F1} pixels", "map_textures");
            ArchonLogger.Log($"  Disconnected Provinces: {disconnectedProvinces} ({(float)disconnectedProvinces / totalProvinces * 100f:F1}%)", "map_textures");
            ArchonLogger.Log($"  Coastal Provinces: {coastalProvinces} ({(float)coastalProvinces / totalProvinces * 100f:F1}%)", "map_textures");
            ArchonLogger.Log($"  Compactness Range: {minCompactness:F3} - {maxCompactness:F3} (avg: {avgCompactness:F3})", "map_textures");

            ArchonLogger.Log($"  Terrain Distribution:", "map_textures");
            foreach (var terrainCount in terrainCounts)
            {
                float percent = (float)terrainCount.Value / totalProvinces * 100f;
                ArchonLogger.Log($"    {terrainCount.Key}: {terrainCount.Value} ({percent:F1}%)", "map_textures");
            }
        }

        /// <summary>
        /// Get optimal label position for province
        /// </summary>
        public static float2 GetOptimalLabelPosition(ProvinceMetadata metadata)
        {
            // If we have a convex hull with multiple points, use centroid
            if (metadata.ConvexHull.IsCreated && metadata.ConvexHull.Length > 2)
            {
                float2 centroid = float2.zero;
                for (int i = 0; i < metadata.ConvexHull.Length; i++)
                {
                    centroid += metadata.ConvexHull[i];
                }
                return centroid / metadata.ConvexHull.Length;
            }

            // Fall back to center of mass
            return metadata.CenterOfMass;
        }

        /// <summary>
        /// Check if province is suitable for placing large labels
        /// </summary>
        public static bool IsSuitableForLargeLabel(ProvinceMetadata metadata, float minSize = 500f)
        {
            return metadata.PixelCount >= minSize &&
                   metadata.Compactness > 0.4f &&
                   !metadata.HasMultipleParts;
        }
    }
}