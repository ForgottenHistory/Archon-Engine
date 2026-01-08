using System.Collections.Generic;
using Unity.Collections;

namespace Core.Systems
{
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
        /// Get adjacency statistics for debugging
        /// </summary>
        public string GetStatistics()
        {
            if (adjacencies.Count == 0)
                return "AdjacencySystem: Not initialized";

            int minNeighbors = int.MaxValue;
            int maxNeighbors = 0;
            int totalNeighbors = 0;

            foreach (var kvp in adjacencies)
            {
                int count = kvp.Value.Count;
                minNeighbors = System.Math.Min(minNeighbors, count);
                maxNeighbors = System.Math.Max(maxNeighbors, count);
                totalNeighbors += count;
            }

            float avgNeighbors = (float)totalNeighbors / adjacencies.Count;

            return $"AdjacencySystem Statistics:\n" +
                   $"  Provinces: {adjacencies.Count}\n" +
                   $"  Adjacency Pairs: {totalAdjacencyPairs}\n" +
                   $"  Avg Neighbors: {avgNeighbors:F1}\n" +
                   $"  Min/Max Neighbors: {minNeighbors}/{maxNeighbors}";
        }
    }
}
