using System.Collections.Generic;
using Unity.Collections;

namespace Core.Systems
{
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
    public class AdjacencySystem
    {
        // Storage: provinceID → set of adjacent province IDs
        private Dictionary<ushort, HashSet<ushort>> adjacencies;

        // Statistics
        private int totalAdjacencyPairs = 0;

        public AdjacencySystem()
        {
            adjacencies = new Dictionary<ushort, HashSet<ushort>>();
        }

        /// <summary>
        /// Initialize adjacency data from FastAdjacencyScanner results
        /// Converts from Dictionary<int, HashSet<int>> to Dictionary<ushort, HashSet<ushort>>
        /// </summary>
        public void SetAdjacencies(Dictionary<int, HashSet<int>> scanResults)
        {
            if (scanResults == null)
            {
                ArchonLogger.LogError("AdjacencySystem: Cannot set null adjacency data");
                return;
            }

            adjacencies.Clear();
            totalAdjacencyPairs = 0;

            foreach (var kvp in scanResults)
            {
                ushort provinceId = (ushort)kvp.Key;
                HashSet<ushort> neighbors = new HashSet<ushort>();

                foreach (int neighborId in kvp.Value)
                {
                    neighbors.Add((ushort)neighborId);
                    totalAdjacencyPairs++;
                }

                adjacencies[provinceId] = neighbors;
            }

            // Divide by 2 since each adjacency is counted twice (A→B and B→A)
            totalAdjacencyPairs /= 2;

            ArchonLogger.Log($"AdjacencySystem: Initialized with {adjacencies.Count} provinces, {totalAdjacencyPairs} adjacency pairs");
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
