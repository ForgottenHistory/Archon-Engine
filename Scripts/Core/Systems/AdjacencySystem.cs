using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Core.Systems
{
    /// <summary>
    /// Queryable adjacency statistics struct.
    /// </summary>
    public struct AdjacencyStats
    {
        public int ProvinceCount;
        public int TotalAdjacencyPairs;
        public int MinNeighbors;
        public int MaxNeighbors;
        public int TotalNeighborEntries;

        /// <summary>
        /// Average neighbors per province.
        /// </summary>
        public float AverageNeighbors => ProvinceCount > 0 ? (float)TotalNeighborEntries / ProvinceCount : 0f;

        /// <summary>
        /// Get formatted summary string.
        /// </summary>
        public string GetSummary()
        {
            return $"AdjacencyStats: {ProvinceCount} provinces, {TotalAdjacencyPairs} pairs, avg {AverageNeighbors:F1} neighbors, range [{MinNeighbors}-{MaxNeighbors}]";
        }
    }
    /// <summary>
    /// Read-only native adjacency data for Burst jobs.
    /// Use this struct in IJob implementations for parallel graph algorithms.
    /// </summary>
    public struct NativeAdjacencyData
    {
        [ReadOnly] public NativeParallelMultiHashMap<ushort, ushort> adjacencyMap;

        public bool IsCreated => adjacencyMap.IsCreated;

        /// <summary>
        /// Get neighbors for a province. Use in Burst jobs.
        /// </summary>
        public NativeParallelMultiHashMap<ushort, ushort>.Enumerator GetNeighbors(ushort provinceId)
        {
            return adjacencyMap.GetValuesForKey(provinceId);
        }

        /// <summary>
        /// Check if two provinces are adjacent. Use in Burst jobs.
        /// </summary>
        public bool IsAdjacent(ushort province1, ushort province2)
        {
            foreach (var neighbor in adjacencyMap.GetValuesForKey(province1))
            {
                if (neighbor == province2)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// ENGINE LAYER: Province adjacency system
    ///
    /// Stores which provinces are adjacent (share a border) for movement validation.
    /// Populated from FastAdjacencyScanner during map initialization.
    ///
    /// Architecture:
    /// - Dictionary<ushort, HashSet<ushort>>: provinceID → set of neighbor IDs
    /// - Bidirectional: if A is adjacent to B, then B is adjacent to A
    /// - Read-only after initialization
    ///
    /// Usage:
    /// - Initialize with SetAdjacencies(adjacencyData)
    /// - Query with IsAdjacent(province1, province2)
    /// - Get all neighbors with GetNeighbors(provinceID)
    ///
    /// Performance:
    /// - IsAdjacent: O(1) HashSet lookup
    /// - GetNeighbors: O(1) dictionary lookup
    /// - Memory: ~6 neighbors × 13,350 provinces × 2 bytes = ~160 KB
    /// </summary>
    public class AdjacencySystem : System.IDisposable
    {
        // Storage: provinceID → set of adjacent province IDs (managed, for convenience)
        private Dictionary<ushort, HashSet<ushort>> adjacencies;

        // Native storage for Burst jobs (populated from managed data)
        private NativeParallelMultiHashMap<ushort, ushort> nativeAdjacencies;

        // Statistics
        private int totalAdjacencyPairs = 0;

        public AdjacencySystem()
        {
            adjacencies = new Dictionary<ushort, HashSet<ushort>>();
        }

        /// <summary>
        /// Get read-only native adjacency data for Burst jobs.
        /// </summary>
        public NativeAdjacencyData GetNativeData()
        {
            return new NativeAdjacencyData { adjacencyMap = nativeAdjacencies };
        }

        /// <summary>
        /// Initialize adjacency data from FastAdjacencyScanner results
        /// Converts from Dictionary<int, HashSet<int>> to Dictionary<ushort, HashSet<ushort>>
        /// Also builds native MultiHashMap for Burst job compatibility
        /// </summary>
        public void SetAdjacencies(Dictionary<int, HashSet<int>> scanResults)
        {
            if (scanResults == null)
            {
                ArchonLogger.LogError("AdjacencySystem: Cannot set null adjacency data", "core_simulation");
                return;
            }

            // Dispose existing native data if re-initializing
            if (nativeAdjacencies.IsCreated)
                nativeAdjacencies.Dispose();

            adjacencies.Clear();
            totalAdjacencyPairs = 0;

            // First pass: count total adjacencies for native allocation
            int totalEntries = 0;
            foreach (var kvp in scanResults)
            {
                totalEntries += kvp.Value.Count;
            }

            // Allocate native storage (capacity = total adjacency entries)
            nativeAdjacencies = new NativeParallelMultiHashMap<ushort, ushort>(totalEntries, Allocator.Persistent);

            // Second pass: populate both managed and native storage
            foreach (var kvp in scanResults)
            {
                ushort provinceId = (ushort)kvp.Key;
                HashSet<ushort> neighbors = new HashSet<ushort>();

                foreach (int neighborId in kvp.Value)
                {
                    ushort neighborUshort = (ushort)neighborId;
                    neighbors.Add(neighborUshort);
                    nativeAdjacencies.Add(provinceId, neighborUshort);
                    totalAdjacencyPairs++;
                }

                adjacencies[provinceId] = neighbors;
            }

            // Divide by 2 since each adjacency is counted twice (A→B and B→A)
            totalAdjacencyPairs /= 2;

            ArchonLogger.Log($"AdjacencySystem: Initialized with {adjacencies.Count} provinces, {totalAdjacencyPairs} adjacency pairs (native MultiHashMap ready)", "core_simulation");
        }

        /// <summary>
        /// Dispose native allocations
        /// </summary>
        public void Dispose()
        {
            if (nativeAdjacencies.IsCreated)
                nativeAdjacencies.Dispose();
        }

        /// <summary>
        /// Check if two provinces are adjacent (share a border)
        /// </summary>
        public bool IsAdjacent(ushort province1, ushort province2)
        {
            if (province1 == province2)
                return false; // Province is not adjacent to itself

            if (!adjacencies.TryGetValue(province1, out HashSet<ushort> neighbors))
                return false;

            return neighbors.Contains(province2);
        }

        /// <summary>
        /// Get all neighbors for a province
        /// Returns empty array if province has no neighbors or doesn't exist
        /// Caller must dispose the returned NativeArray
        /// </summary>
        public NativeArray<ushort> GetNeighbors(ushort provinceId, Allocator allocator = Allocator.TempJob)
        {
            if (!adjacencies.TryGetValue(provinceId, out HashSet<ushort> neighbors))
            {
                return new NativeArray<ushort>(0, allocator);
            }

            NativeArray<ushort> result = new NativeArray<ushort>(neighbors.Count, allocator);
            int index = 0;
            foreach (ushort neighbor in neighbors)
            {
                result[index++] = neighbor;
            }

            return result;
        }

        /// <summary>
        /// Get all neighbors for a province, filling an existing NativeList (zero allocations)
        /// Clears the list before filling it with neighbors
        /// Used by PathfindingSystem for allocation-free pathfinding
        /// </summary>
        public void GetNeighbors(ushort provinceId, NativeList<ushort> resultBuffer)
        {
            resultBuffer.Clear();

            if (!adjacencies.TryGetValue(provinceId, out HashSet<ushort> neighbors))
            {
                return; // No neighbors, buffer remains empty
            }

            foreach (ushort neighbor in neighbors)
            {
                resultBuffer.Add(neighbor);
            }
        }

        /// <summary>
        /// Get neighbor count for a province
        /// </summary>
        public int GetNeighborCount(ushort provinceId)
        {
            if (!adjacencies.TryGetValue(provinceId, out HashSet<ushort> neighbors))
                return 0;

            return neighbors.Count;
        }

        // ========== FILTERED QUERIES ==========

        /// <summary>
        /// Get neighbors that match a predicate.
        /// Example: GetNeighborsWhere(provinceId, id => provinceSystem.GetOwner(id) == enemyCountry)
        /// </summary>
        /// <param name="provinceId">Province to get neighbors for</param>
        /// <param name="predicate">Filter function - returns true for neighbors to include</param>
        /// <param name="resultBuffer">Pre-allocated list to fill (cleared before use)</param>
        public void GetNeighborsWhere(ushort provinceId, Func<ushort, bool> predicate, List<ushort> resultBuffer)
        {
            resultBuffer.Clear();

            if (!adjacencies.TryGetValue(provinceId, out HashSet<ushort> neighbors))
                return;

            foreach (ushort neighbor in neighbors)
            {
                if (predicate(neighbor))
                    resultBuffer.Add(neighbor);
            }
        }

        /// <summary>
        /// Get neighbors that match a predicate (allocating version).
        /// Prefer the buffer version for hot paths.
        /// </summary>
        public List<ushort> GetNeighborsWhere(ushort provinceId, Func<ushort, bool> predicate)
        {
            var result = new List<ushort>();
            GetNeighborsWhere(provinceId, predicate, result);
            return result;
        }

        // ========== REGION QUERIES ==========

        /// <summary>
        /// Get all provinces connected to a starting province that match a predicate.
        /// Uses BFS flood fill. Returns the connected region including the start province.
        ///
        /// Example: Get all provinces owned by a country connected to capitalProvince
        ///   GetConnectedRegion(capital, id => provinceSystem.GetOwner(id) == countryId)
        /// </summary>
        /// <param name="startProvince">Starting province for flood fill</param>
        /// <param name="predicate">Filter - only provinces passing this are included and traversed</param>
        /// <param name="resultBuffer">Pre-allocated set to fill (cleared before use)</param>
        public void GetConnectedRegion(ushort startProvince, Func<ushort, bool> predicate, HashSet<ushort> resultBuffer)
        {
            resultBuffer.Clear();

            // Start province must pass predicate
            if (!predicate(startProvince))
                return;

            // BFS flood fill
            var queue = new Queue<ushort>();
            queue.Enqueue(startProvince);
            resultBuffer.Add(startProvince);

            while (queue.Count > 0)
            {
                ushort current = queue.Dequeue();

                if (!adjacencies.TryGetValue(current, out HashSet<ushort> neighbors))
                    continue;

                foreach (ushort neighbor in neighbors)
                {
                    // Skip already visited
                    if (resultBuffer.Contains(neighbor))
                        continue;

                    // Only include if predicate passes
                    if (predicate(neighbor))
                    {
                        resultBuffer.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        /// <summary>
        /// Get all provinces connected to a starting province (allocating version).
        /// </summary>
        public HashSet<ushort> GetConnectedRegion(ushort startProvince, Func<ushort, bool> predicate)
        {
            var result = new HashSet<ushort>();
            GetConnectedRegion(startProvince, predicate, result);
            return result;
        }

        /// <summary>
        /// Get provinces where two countries share a border.
        /// Returns provinces from country1 that are adjacent to any province in country2.
        ///
        /// Example: Find where France borders Germany
        ///   GetSharedBorderProvinces(frenchProvinces, germanProvinces, borderBuffer)
        /// </summary>
        /// <param name="ownedProvinces">Provinces belonging to first country</param>
        /// <param name="foreignProvinces">Provinces belonging to second country</param>
        /// <param name="resultBuffer">Pre-allocated list to fill with border provinces from ownedProvinces</param>
        public void GetSharedBorderProvinces(
            IEnumerable<ushort> ownedProvinces,
            HashSet<ushort> foreignProvinces,
            List<ushort> resultBuffer)
        {
            resultBuffer.Clear();

            foreach (ushort owned in ownedProvinces)
            {
                if (!adjacencies.TryGetValue(owned, out HashSet<ushort> neighbors))
                    continue;

                foreach (ushort neighbor in neighbors)
                {
                    if (foreignProvinces.Contains(neighbor))
                    {
                        resultBuffer.Add(owned);
                        break; // Found at least one foreign neighbor, move to next owned
                    }
                }
            }
        }

        /// <summary>
        /// Get provinces where two countries share a border (allocating version).
        /// </summary>
        public List<ushort> GetSharedBorderProvinces(IEnumerable<ushort> ownedProvinces, HashSet<ushort> foreignProvinces)
        {
            var result = new List<ushort>();
            GetSharedBorderProvinces(ownedProvinces, foreignProvinces, result);
            return result;
        }

        // ========== BRIDGE DETECTION ==========

        /// <summary>
        /// Check if a province is a "bridge" - removing it would disconnect the region.
        ///
        /// A bridge province has the property that the region becomes disconnected
        /// if it's removed. Useful for strategic AI (critical choke points).
        ///
        /// Algorithm: Check if any two neighbors of the province can still reach
        /// each other without going through this province.
        /// </summary>
        /// <param name="province">Province to check</param>
        /// <param name="regionPredicate">Predicate defining the region (e.g., same owner)</param>
        /// <returns>True if removing this province would disconnect the region</returns>
        public bool IsBridgeProvince(ushort province, Func<ushort, bool> regionPredicate)
        {
            if (!adjacencies.TryGetValue(province, out HashSet<ushort> neighbors))
                return false;

            // Get neighbors that are in the same region
            var regionNeighbors = new List<ushort>();
            foreach (ushort neighbor in neighbors)
            {
                if (regionPredicate(neighbor))
                    regionNeighbors.Add(neighbor);
            }

            // Need at least 2 neighbors in region for bridge detection to matter
            if (regionNeighbors.Count < 2)
                return false;

            // Check if first neighbor can reach all others without going through this province
            ushort start = regionNeighbors[0];

            // Modified predicate that excludes the test province
            bool ExcludingProvince(ushort p) => p != province && regionPredicate(p);

            var reachable = GetConnectedRegion(start, ExcludingProvince);

            // If any region neighbor is not reachable, this is a bridge
            for (int i = 1; i < regionNeighbors.Count; i++)
            {
                if (!reachable.Contains(regionNeighbors[i]))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Find all bridge provinces within a region.
        /// These are strategic choke points where losing control would split territory.
        /// </summary>
        /// <param name="regionProvinces">Set of provinces to check</param>
        /// <param name="regionPredicate">Predicate defining what's in the region</param>
        /// <param name="resultBuffer">Pre-allocated list to fill with bridge provinces</param>
        public void FindBridgeProvinces(
            IEnumerable<ushort> regionProvinces,
            Func<ushort, bool> regionPredicate,
            List<ushort> resultBuffer)
        {
            resultBuffer.Clear();

            foreach (ushort province in regionProvinces)
            {
                if (IsBridgeProvince(province, regionPredicate))
                    resultBuffer.Add(province);
            }
        }

        // ========== STATISTICS ==========

        /// <summary>
        /// Get queryable adjacency statistics.
        /// </summary>
        public AdjacencyStats GetStats()
        {
            if (adjacencies.Count == 0)
            {
                return new AdjacencyStats
                {
                    ProvinceCount = 0,
                    TotalAdjacencyPairs = 0,
                    MinNeighbors = 0,
                    MaxNeighbors = 0,
                    TotalNeighborEntries = 0
                };
            }

            int minNeighbors = int.MaxValue;
            int maxNeighbors = 0;
            int totalNeighbors = 0;

            foreach (var kvp in adjacencies)
            {
                int count = kvp.Value.Count;
                minNeighbors = Math.Min(minNeighbors, count);
                maxNeighbors = Math.Max(maxNeighbors, count);
                totalNeighbors += count;
            }

            return new AdjacencyStats
            {
                ProvinceCount = adjacencies.Count,
                TotalAdjacencyPairs = totalAdjacencyPairs,
                MinNeighbors = minNeighbors,
                MaxNeighbors = maxNeighbors,
                TotalNeighborEntries = totalNeighbors
            };
        }

        /// <summary>
        /// Check if system is initialized
        /// </summary>
        public bool IsInitialized => adjacencies.Count > 0;

        /// <summary>
        /// Get total province count
        /// </summary>
        public int ProvinceCount => adjacencies.Count;

        /// <summary>
        /// Get total adjacency pair count
        /// </summary>
        public int TotalAdjacencyPairs => totalAdjacencyPairs;

        /// <summary>
        /// Get adjacency statistics for debugging (string format).
        /// Prefer GetStats() for programmatic access.
        /// </summary>
        public string GetStatistics()
        {
            var stats = GetStats();
            if (stats.ProvinceCount == 0)
                return "AdjacencySystem: Not initialized";

            return $"AdjacencySystem Statistics:\n" +
                   $"  Provinces: {stats.ProvinceCount}\n" +
                   $"  Adjacency Pairs: {stats.TotalAdjacencyPairs}\n" +
                   $"  Avg Neighbors: {stats.AverageNeighbors:F1}\n" +
                   $"  Min/Max Neighbors: {stats.MinNeighbors}/{stats.MaxNeighbors}";
        }
    }
}
