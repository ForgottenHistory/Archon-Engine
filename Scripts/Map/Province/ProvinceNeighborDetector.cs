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
    /// High-performance province neighbor detection using scanline algorithm
    /// Optimized for 10,000+ provinces with Burst compilation
    /// </summary>
    public static class ProvinceNeighborDetector
    {
        /// <summary>
        /// Neighbor detection result
        /// </summary>
        public struct NeighborResult
        {
            public bool Success;
            public NativeHashMap<ushort, ProvinceNeighborData> ProvinceNeighbors;
            public NativeHashMap<ushort, ProvinceBounds> ProvinceBounds;
            public NativeHashSet<ushort> CoastalProvinces;
            public int TotalNeighborPairs;
            public string ErrorMessage;

            public void Dispose()
            {
                if (ProvinceNeighbors.IsCreated) ProvinceNeighbors.Dispose();
                if (ProvinceBounds.IsCreated) ProvinceBounds.Dispose();
                if (CoastalProvinces.IsCreated) CoastalProvinces.Dispose();
            }
        }

        /// <summary>
        /// Province neighbor data storage
        /// </summary>
        public struct ProvinceNeighborData
        {
            public ushort ProvinceID;
            public NativeList<ushort> Neighbors;
            public bool IsCoastal;
            public int NeighborCount => Neighbors.IsCreated ? Neighbors.Length : 0;

            public void Dispose()
            {
                if (Neighbors.IsCreated) Neighbors.Dispose();
            }
        }

        /// <summary>
        /// Province bounding rectangle
        /// </summary>
        public struct ProvinceBounds
        {
            public ushort ProvinceID;
            public int2 Min;
            public int2 Max;
            public int2 Size => Max - Min + new int2(1, 1);
            public int Area => Size.x * Size.y;
            public int2 Center => (Min + Max) / 2;
        }

        /// <summary>
        /// Detect all province neighbors using optimized scanline algorithm
        /// </summary>
        /// <param name="loadResult">Province map load result</param>
        /// <param name="includeOceanNeighbors">Include ocean (ID 0) as neighbor</param>
        /// <returns>Neighbor detection result</returns>
        public static NeighborResult DetectNeighbors(ProvinceMapLoader.LoadResult loadResult, bool includeOceanNeighbors = true)
        {
            var result = new NeighborResult();

            if (!loadResult.Success || loadResult.ProvincePixels.Length == 0)
            {
                result.ErrorMessage = "Invalid load result provided";
                return result;
            }

            // Only log in non-test scenarios (reduce test overhead)
            bool verboseLogging = loadResult.ProvinceCount < 1000;

            if (verboseLogging)
                ArchonLogger.LogMapTextures($"Detecting neighbors for {loadResult.ProvinceCount} provinces...");

            // Initialize data structures
            var neighborPairs = new NativeHashSet<NeighborPair>(loadResult.ProvincePixels.Length / 4, Allocator.TempJob);
            var provinceBounds = new NativeHashMap<ushort, ProvinceBounds>(loadResult.ProvinceCount, Allocator.TempJob);
            var coastalProvinces = new NativeHashSet<ushort>(loadResult.ProvinceCount / 10, Allocator.TempJob);

            // Convert pixel array to 2D lookup for efficiency
            var pixelLookup = CreatePixelLookup(loadResult);

            try
            {
                // Perform horizontal scanline pass
                if (verboseLogging)
                    ArchonLogger.LogMapTextures("Performing horizontal neighbor detection...");
                DetectHorizontalNeighbors(pixelLookup, neighborPairs, loadResult.Width, loadResult.Height, includeOceanNeighbors);

                // Perform vertical scanline pass
                if (verboseLogging)
                    ArchonLogger.LogMapTextures("Performing vertical neighbor detection...");
                DetectVerticalNeighbors(pixelLookup, neighborPairs, loadResult.Width, loadResult.Height, includeOceanNeighbors);

                // Calculate bounding boxes
                if (verboseLogging)
                    ArchonLogger.LogMapTextures("Calculating province bounding boxes...");
                CalculateBoundingBoxes(loadResult.ProvincePixels, provinceBounds);

                // Identify coastal provinces
                if (verboseLogging)
                    ArchonLogger.LogMapTextures("Identifying coastal provinces...");
                IdentifyCoastalProvinces(neighborPairs, coastalProvinces);

                // Convert neighbor pairs to per-province neighbor lists
                if (verboseLogging)
                    ArchonLogger.LogMapTextures("Building neighbor lists...");
                var provinceNeighbors = BuildNeighborLists(neighborPairs, loadResult.ProvinceCount);

                // Mark coastal flags in neighbor data
                MarkCoastalFlags(provinceNeighbors, coastalProvinces);

                result.Success = true;
                result.ProvinceNeighbors = provinceNeighbors;
                result.ProvinceBounds = provinceBounds;
                result.CoastalProvinces = coastalProvinces;
                result.TotalNeighborPairs = neighborPairs.Count;

                if (verboseLogging)
                {
                    ArchonLogger.LogMapTextures($"Neighbor detection complete:");
                    ArchonLogger.LogMapTextures($"  - Neighbor pairs: {result.TotalNeighborPairs}");
                    ArchonLogger.LogMapTextures($"  - Coastal provinces: {coastalProvinces.Count}");
                    ArchonLogger.LogMapTextures($"  - Provinces with bounds: {provinceBounds.Count}");
                }

            }
            catch (System.Exception e)
            {
                result.ErrorMessage = $"Neighbor detection failed: {e.Message}";
                result.Dispose();
            }
            finally
            {
                // Clean up temporary structures
                neighborPairs.Dispose();
                pixelLookup.Dispose();
            }

            return result;
        }

        /// <summary>
        /// Job to initialize pixel lookup with zeros in parallel
        /// </summary>
        [BurstCompile]
        private struct InitializePixelLookupJob : IJobParallelFor
        {
            [WriteOnly]
            public NativeArray<ushort> PixelLookup;

            public void Execute(int index)
            {
                PixelLookup[index] = 0;
            }
        }

        /// <summary>
        /// Job to fill pixel lookup with province data
        /// </summary>
        [BurstCompile]
        private struct FillPixelLookupJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<ProvinceMapLoader.ProvincePixel> ProvincePixels;

            [NativeDisableParallelForRestriction]
            [WriteOnly]
            public NativeArray<ushort> PixelLookup;

            public int Width;
            public int TotalPixels;

            public void Execute(int index)
            {
                var pixel = ProvincePixels[index];
                int lookupIndex = pixel.Position.y * Width + pixel.Position.x;

                if (lookupIndex >= 0 && lookupIndex < TotalPixels)
                {
                    PixelLookup[lookupIndex] = pixel.ProvinceID;
                }
            }
        }

        /// <summary>
        /// Create 2D lookup array from province pixels for efficient neighbor checking
        /// </summary>
        private static NativeArray<ushort> CreatePixelLookup(ProvinceMapLoader.LoadResult loadResult)
        {
            int totalPixels = loadResult.Width * loadResult.Height;
            var pixelLookup = new NativeArray<ushort>(totalPixels, Allocator.TempJob);

            // Initialize with ocean (ID 0) in parallel
            var initJob = new InitializePixelLookupJob
            {
                PixelLookup = pixelLookup
            };
            var initHandle = initJob.Schedule(totalPixels, 1024);

            // Fill with province data in parallel
            var fillJob = new FillPixelLookupJob
            {
                ProvincePixels = loadResult.ProvincePixels,
                PixelLookup = pixelLookup,
                Width = loadResult.Width,
                TotalPixels = totalPixels
            };
            var fillHandle = fillJob.Schedule(loadResult.ProvincePixels.Length, 128, initHandle);

            // Wait for completion
            fillHandle.Complete();

            return pixelLookup;
        }

        /// <summary>
        /// Detect horizontal neighbors using scanline algorithm
        /// </summary>
        private static void DetectHorizontalNeighbors(NativeArray<ushort> pixelLookup, NativeHashSet<NeighborPair> neighborPairs,
            int width, int height, bool includeOceanNeighbors)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width - 1; x++)
                {
                    int currentIndex = y * width + x;
                    int rightIndex = currentIndex + 1;

                    ushort currentID = pixelLookup[currentIndex];
                    ushort rightID = pixelLookup[rightIndex];

                    // Skip if same province
                    if (currentID == rightID)
                        continue;

                    // Skip ocean neighbors if not desired
                    if (!includeOceanNeighbors && (currentID == 0 || rightID == 0))
                        continue;

                    // Add neighbor pair (sorted for deduplication)
                    var pair = CreateNeighborPair(currentID, rightID);
                    neighborPairs.Add(pair);
                }
            }
        }

        /// <summary>
        /// Detect vertical neighbors using scanline algorithm
        /// </summary>
        private static void DetectVerticalNeighbors(NativeArray<ushort> pixelLookup, NativeHashSet<NeighborPair> neighborPairs,
            int width, int height, bool includeOceanNeighbors)
        {
            for (int y = 0; y < height - 1; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int currentIndex = y * width + x;
                    int belowIndex = (y + 1) * width + x;

                    ushort currentID = pixelLookup[currentIndex];
                    ushort belowID = pixelLookup[belowIndex];

                    // Skip if same province
                    if (currentID == belowID)
                        continue;

                    // Skip ocean neighbors if not desired
                    if (!includeOceanNeighbors && (currentID == 0 || belowID == 0))
                        continue;

                    // Add neighbor pair (sorted for deduplication)
                    var pair = CreateNeighborPair(currentID, belowID);
                    neighborPairs.Add(pair);
                }
            }
        }

        /// <summary>
        /// Calculate bounding boxes for all provinces
        /// </summary>
        private static void CalculateBoundingBoxes(NativeArray<ProvinceMapLoader.ProvincePixel> provincePixels,
            NativeHashMap<ushort, ProvinceBounds> provinceBounds)
        {
            var boundsTemp = new NativeHashMap<ushort, BoundingBoxAccumulator>(1000, Allocator.Temp);

            // Accumulate min/max coordinates for each province
            for (int i = 0; i < provincePixels.Length; i++)
            {
                var pixel = provincePixels[i];
                ushort id = pixel.ProvinceID;

                if (id == 0) continue; // Skip ocean

                if (boundsTemp.TryGetValue(id, out var existing))
                {
                    existing.Min = math.min(existing.Min, pixel.Position);
                    existing.Max = math.max(existing.Max, pixel.Position);
                    boundsTemp[id] = existing;
                }
                else
                {
                    boundsTemp.TryAdd(id, new BoundingBoxAccumulator
                    {
                        Min = pixel.Position,
                        Max = pixel.Position
                    });
                }
            }

            // Convert to final bounds format
            foreach (var kvp in boundsTemp)
            {
                provinceBounds.TryAdd(kvp.Key, new ProvinceBounds
                {
                    ProvinceID = kvp.Key,
                    Min = kvp.Value.Min,
                    Max = kvp.Value.Max
                });
            }

            boundsTemp.Dispose();
        }

        /// <summary>
        /// Identify coastal provinces (those that neighbor ocean)
        /// </summary>
        private static void IdentifyCoastalProvinces(NativeHashSet<NeighborPair> neighborPairs, NativeHashSet<ushort> coastalProvinces)
        {
            foreach (var pair in neighborPairs)
            {
                // If one province is ocean (ID 0), the other is coastal
                if (pair.ID1 == 0 && pair.ID2 != 0)
                {
                    coastalProvinces.Add(pair.ID2);
                }
                else if (pair.ID2 == 0 && pair.ID1 != 0)
                {
                    coastalProvinces.Add(pair.ID1);
                }
            }
        }

        /// <summary>
        /// Build per-province neighbor lists from neighbor pairs
        /// </summary>
        private static NativeHashMap<ushort, ProvinceNeighborData> BuildNeighborLists(NativeHashSet<NeighborPair> neighborPairs, int provinceCount)
        {
            var provinceNeighbors = new NativeHashMap<ushort, ProvinceNeighborData>(provinceCount, Allocator.Persistent);

            // Count neighbors per province first
            var neighborCounts = new NativeHashMap<ushort, int>(provinceCount, Allocator.Temp);

            foreach (var pair in neighborPairs)
            {
                if (pair.ID1 != 0) // Skip ocean
                {
                    neighborCounts.TryGetValue(pair.ID1, out int count1);
                    neighborCounts[pair.ID1] = count1 + 1;
                }

                if (pair.ID2 != 0) // Skip ocean
                {
                    neighborCounts.TryGetValue(pair.ID2, out int count2);
                    neighborCounts[pair.ID2] = count2 + 1;
                }
            }

            // Allocate neighbor lists
            foreach (var kvp in neighborCounts)
            {
                var neighborData = new ProvinceNeighborData
                {
                    ProvinceID = kvp.Key,
                    Neighbors = new NativeList<ushort>(kvp.Value, Allocator.Persistent),
                    IsCoastal = false
                };

                provinceNeighbors.TryAdd(kvp.Key, neighborData);
            }

            // Populate neighbor lists
            foreach (var pair in neighborPairs)
            {
                if (pair.ID1 != 0 && provinceNeighbors.TryGetValue(pair.ID1, out var data1))
                {
                    data1.Neighbors.Add(pair.ID2);
                    provinceNeighbors[pair.ID1] = data1;
                }

                if (pair.ID2 != 0 && provinceNeighbors.TryGetValue(pair.ID2, out var data2))
                {
                    data2.Neighbors.Add(pair.ID1);
                    provinceNeighbors[pair.ID2] = data2;
                }
            }

            neighborCounts.Dispose();
            return provinceNeighbors;
        }

        /// <summary>
        /// Mark coastal flags in neighbor data
        /// </summary>
        private static void MarkCoastalFlags(NativeHashMap<ushort, ProvinceNeighborData> provinceNeighbors, NativeHashSet<ushort> coastalProvinces)
        {
            foreach (var coastalID in coastalProvinces)
            {
                if (provinceNeighbors.TryGetValue(coastalID, out var neighborData))
                {
                    neighborData.IsCoastal = true;
                    provinceNeighbors[coastalID] = neighborData;
                }
            }
        }

        /// <summary>
        /// Create sorted neighbor pair for deduplication
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static NeighborPair CreateNeighborPair(ushort id1, ushort id2)
        {
            // Always store smaller ID first for consistent deduplication
            return id1 < id2 ? new NeighborPair { ID1 = id1, ID2 = id2 } : new NeighborPair { ID1 = id2, ID2 = id1 };
        }

        /// <summary>
        /// Neighbor pair structure for deduplication
        /// </summary>
        public struct NeighborPair : System.IEquatable<NeighborPair>
        {
            public ushort ID1;
            public ushort ID2;

            public bool Equals(NeighborPair other)
            {
                return ID1 == other.ID1 && ID2 == other.ID2;
            }

            public override int GetHashCode()
            {
                return ID1.GetHashCode() ^ (ID2.GetHashCode() << 16);
            }
        }

        /// <summary>
        /// Temporary structure for bounding box calculation
        /// </summary>
        private struct BoundingBoxAccumulator
        {
            public int2 Min;
            public int2 Max;
        }

        /// <summary>
        /// Get neighbor count for a province
        /// </summary>
        public static int GetNeighborCount(NativeHashMap<ushort, ProvinceNeighborData> provinceNeighbors, ushort provinceID)
        {
            return provinceNeighbors.TryGetValue(provinceID, out var data) ? data.NeighborCount : 0;
        }

        /// <summary>
        /// Check if two provinces are neighbors
        /// </summary>
        public static bool AreNeighbors(NativeHashMap<ushort, ProvinceNeighborData> provinceNeighbors, ushort provinceID1, ushort provinceID2)
        {
            if (!provinceNeighbors.TryGetValue(provinceID1, out var data))
                return false;

            for (int i = 0; i < data.Neighbors.Length; i++)
            {
                if (data.Neighbors[i] == provinceID2)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Get all neighbors of a province
        /// </summary>
        public static NativeArray<ushort> GetNeighbors(NativeHashMap<ushort, ProvinceNeighborData> provinceNeighbors,
            ushort provinceID, Allocator allocator)
        {
            if (provinceNeighbors.TryGetValue(provinceID, out var data) && data.Neighbors.IsCreated)
            {
                return new NativeArray<ushort>(data.Neighbors.AsArray(), allocator);
            }

            return new NativeArray<ushort>(0, allocator);
        }

        /// <summary>
        /// Log neighbor statistics for debugging
        /// </summary>
        public static void LogNeighborStatistics(NeighborResult result)
        {
            if (!result.Success) return;

            int totalNeighbors = 0;
            int minNeighbors = int.MaxValue;
            int maxNeighbors = 0;
            int provinceCount = result.ProvinceNeighbors.Count;

            foreach (var kvp in result.ProvinceNeighbors)
            {
                int neighborCount = kvp.Value.NeighborCount;
                totalNeighbors += neighborCount;
                minNeighbors = math.min(minNeighbors, neighborCount);
                maxNeighbors = math.max(maxNeighbors, neighborCount);
            }

            float avgNeighbors = provinceCount > 0 ? (float)totalNeighbors / provinceCount : 0f;

            ArchonLogger.LogMapTextures($"Province Neighbor Statistics:");
            ArchonLogger.LogMapTextures($"  Provinces analyzed: {provinceCount}");
            ArchonLogger.LogMapTextures($"  Total neighbor relationships: {totalNeighbors}");
            ArchonLogger.LogMapTextures($"  Unique neighbor pairs: {result.TotalNeighborPairs}");
            ArchonLogger.LogMapTextures($"  Average neighbors per province: {avgNeighbors:F1}");
            ArchonLogger.LogMapTextures($"  Min/Max neighbors: {minNeighbors}/{maxNeighbors}");
            ArchonLogger.LogMapTextures($"  Coastal provinces: {result.CoastalProvinces.Count} ({(float)result.CoastalProvinces.Count / provinceCount * 100f:F1}%)");
        }
    }
}