using Core.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Core.Graph
{
    /// <summary>
    /// General-purpose BFS distance calculator for province graph.
    /// Burst-compiled for performance.
    ///
    /// Use cases:
    /// - Distance from player (AI tiering)
    /// - Distance between any two provinces
    /// - Finding provinces within N hops
    /// - Pathfinding prep
    ///
    /// Performance:
    /// - O(P) where P = total provinces (single BFS traversal)
    /// - Burst-compiled for SIMD and native code optimization
    /// - Pre-allocated buffers for zero gameplay allocations
    /// </summary>
    public class GraphDistanceCalculator : System.IDisposable
    {
        private const byte MAX_DISTANCE = 255;

        // Pre-allocated buffers (persistent, reused across calculations)
        private NativeArray<byte> provinceDistances;
        private NativeArray<byte> countryDistances;
        private NativeList<ushort> bfsQueue;
        private NativeList<ushort> sourceProvincesBuffer;

        private int maxProvinceId;
        private int maxCountryId;
        private bool isInitialized;

        /// <summary>
        /// Whether the calculator is initialized and ready for use.
        /// </summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Initialize buffers for distance calculation.
        /// Call once after province/country counts are known.
        /// </summary>
        /// <param name="maxProvinceId">Maximum province ID (buffer size)</param>
        /// <param name="maxCountryId">Maximum country ID (buffer size), or 0 to skip country tracking</param>
        public void Initialize(int maxProvinceId, int maxCountryId = 0)
        {
            if (isInitialized)
            {
                Dispose();
            }

            this.maxProvinceId = maxProvinceId;
            this.maxCountryId = maxCountryId;

            provinceDistances = new NativeArray<byte>(maxProvinceId, Allocator.Persistent);
            bfsQueue = new NativeList<ushort>(maxProvinceId, Allocator.Persistent);
            sourceProvincesBuffer = new NativeList<ushort>(100, Allocator.Persistent);

            if (maxCountryId > 0)
            {
                countryDistances = new NativeArray<byte>(maxCountryId, Allocator.Persistent);
            }

            isInitialized = true;

            ArchonLogger.Log($"GraphDistanceCalculator initialized: {maxProvinceId} provinces, {maxCountryId} countries", "core_simulation");
        }

        /// <summary>
        /// Calculate distances from a single source province.
        /// </summary>
        public void CalculateDistancesFromProvince(
            ushort sourceProvinceId,
            NativeAdjacencyData adjacencyData,
            NativeProvinceData provinceData = default)
        {
            sourceProvincesBuffer.Clear();
            sourceProvincesBuffer.Add(sourceProvinceId);
            CalculateDistancesFromProvinces(sourceProvincesBuffer.AsArray(), adjacencyData, provinceData);
        }

        /// <summary>
        /// Calculate distances from multiple source provinces using Burst-compiled BFS.
        /// Updates internal distance arrays.
        /// </summary>
        /// <param name="sourceProvinces">Starting provinces (distance 0)</param>
        /// <param name="adjacencyData">Province adjacency graph</param>
        /// <param name="provinceData">Optional province data for country distance tracking</param>
        public void CalculateDistancesFromProvinces(
            NativeArray<ushort> sourceProvinces,
            NativeAdjacencyData adjacencyData,
            NativeProvinceData provinceData = default)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogWarning("GraphDistanceCalculator not initialized", "core_simulation");
                return;
            }

            bool trackCountries = maxCountryId > 0 && provinceData.IsCreated;

            var job = new BFSDistanceJob
            {
                sourceProvinces = sourceProvinces,
                adjacencyData = adjacencyData,
                provinceData = provinceData,
                provinceDistances = provinceDistances,
                countryDistances = trackCountries ? countryDistances : default,
                bfsQueue = bfsQueue,
                maxDistance = MAX_DISTANCE,
                trackCountries = trackCountries
            };

            job.Schedule().Complete();
        }

        /// <summary>
        /// Calculate distances from all provinces owned by a specific country.
        /// Useful for AI distance tiering.
        /// </summary>
        public void CalculateDistancesFromCountry(
            ushort countryId,
            ProvinceSystem provinceSystem,
            AdjacencySystem adjacencySystem)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogWarning("GraphDistanceCalculator not initialized", "core_simulation");
                return;
            }

            sourceProvincesBuffer.Clear();
            provinceSystem.GetCountryProvinces(countryId, sourceProvincesBuffer);

            CalculateDistancesFromProvinces(
                sourceProvincesBuffer.AsArray(),
                adjacencySystem.GetNativeData(),
                provinceSystem.GetNativeData());
        }

        /// <summary>
        /// Get distance for a specific province from the last calculation.
        /// Returns byte.MaxValue if not calculated or unreachable.
        /// </summary>
        public byte GetProvinceDistance(ushort provinceId)
        {
            if (!isInitialized || provinceId >= provinceDistances.Length)
                return MAX_DISTANCE;

            return provinceDistances[provinceId];
        }

        /// <summary>
        /// Get distance for a specific country from the last calculation.
        /// Returns byte.MaxValue if country tracking not enabled or unreachable.
        /// </summary>
        public byte GetCountryDistance(ushort countryId)
        {
            if (!isInitialized || maxCountryId == 0 || countryId >= countryDistances.Length)
                return MAX_DISTANCE;

            return countryDistances[countryId];
        }

        /// <summary>
        /// Get the full province distances array.
        /// Only valid until next calculation.
        /// </summary>
        public NativeArray<byte> GetAllProvinceDistances()
        {
            return provinceDistances;
        }

        /// <summary>
        /// Get the full country distances array.
        /// Only valid until next calculation.
        /// Returns default if country tracking not enabled.
        /// </summary>
        public NativeArray<byte> GetAllCountryDistances()
        {
            return maxCountryId > 0 ? countryDistances : default;
        }

        /// <summary>
        /// Calculate distance between two specific provinces.
        /// This runs a fresh BFS from source to find target.
        /// For repeated queries, use CalculateDistancesFromProvince then GetProvinceDistance.
        /// </summary>
        public byte GetDistanceBetween(
            ushort sourceProvinceId,
            ushort targetProvinceId,
            AdjacencySystem adjacencySystem)
        {
            if (!isInitialized)
                return MAX_DISTANCE;

            if (sourceProvinceId == targetProvinceId)
                return 0;

            // Run BFS from source
            CalculateDistancesFromProvince(sourceProvinceId, adjacencySystem.GetNativeData());

            return GetProvinceDistance(targetProvinceId);
        }

        /// <summary>
        /// Get all provinces within a specified distance.
        /// Caller must dispose the returned list.
        /// </summary>
        public NativeList<ushort> GetProvincesWithinDistance(byte maxDist, Allocator allocator)
        {
            var result = new NativeList<ushort>(64, allocator);

            if (!isInitialized)
                return result;

            for (int i = 0; i < provinceDistances.Length; i++)
            {
                if (provinceDistances[i] <= maxDist && provinceDistances[i] < MAX_DISTANCE)
                {
                    result.Add((ushort)i);
                }
            }

            return result;
        }

        /// <summary>
        /// Cleanup native allocations.
        /// </summary>
        public void Dispose()
        {
            if (provinceDistances.IsCreated)
                provinceDistances.Dispose();

            if (countryDistances.IsCreated)
                countryDistances.Dispose();

            if (bfsQueue.IsCreated)
                bfsQueue.Dispose();

            if (sourceProvincesBuffer.IsCreated)
                sourceProvincesBuffer.Dispose();

            isInitialized = false;
        }
    }

    /// <summary>
    /// Burst-compiled BFS job for distance calculation.
    /// Traverses the province adjacency graph from source provinces.
    /// </summary>
    [BurstCompile]
    public struct BFSDistanceJob : IJob
    {
        [ReadOnly] public NativeArray<ushort> sourceProvinces;
        [ReadOnly] public NativeAdjacencyData adjacencyData;
        [ReadOnly] public NativeProvinceData provinceData;

        public NativeArray<byte> provinceDistances;
        public NativeArray<byte> countryDistances;
        public NativeList<ushort> bfsQueue;
        public byte maxDistance;
        public bool trackCountries;

        public void Execute()
        {
            // Reset province distances to max
            for (int i = 0; i < provinceDistances.Length; i++)
            {
                provinceDistances[i] = maxDistance;
            }

            // Reset country distances if tracking
            if (trackCountries && countryDistances.IsCreated)
            {
                for (int i = 0; i < countryDistances.Length; i++)
                {
                    countryDistances[i] = maxDistance;
                }
            }

            // Initialize BFS queue with source provinces
            bfsQueue.Clear();
            for (int i = 0; i < sourceProvinces.Length; i++)
            {
                ushort provinceId = sourceProvinces[i];
                if (provinceId < provinceDistances.Length)
                {
                    provinceDistances[provinceId] = 0;
                    bfsQueue.Add(provinceId);

                    // Mark source province owners as distance 0
                    if (trackCountries && provinceData.IsCreated)
                    {
                        ushort ownerId = provinceData.GetProvinceOwner(provinceId);
                        if (ownerId < countryDistances.Length)
                        {
                            countryDistances[ownerId] = 0;
                        }
                    }
                }
            }

            // BFS traversal
            int queueIndex = 0;
            while (queueIndex < bfsQueue.Length)
            {
                ushort currentProvince = bfsQueue[queueIndex++];
                byte currentDistance = provinceDistances[currentProvince];

                // Don't explore beyond max distance
                if (currentDistance >= maxDistance - 1)
                    continue;

                byte nextDistance = (byte)(currentDistance + 1);

                // Get neighbors from adjacency map
                var neighborEnumerator = adjacencyData.GetNeighbors(currentProvince);
                while (neighborEnumerator.MoveNext())
                {
                    ushort neighborProvince = neighborEnumerator.Current;

                    if (neighborProvince >= provinceDistances.Length)
                        continue;

                    // Only update if we found a shorter path
                    if (nextDistance < provinceDistances[neighborProvince])
                    {
                        provinceDistances[neighborProvince] = nextDistance;
                        bfsQueue.Add(neighborProvince);

                        // Update country distance (minimum of any province)
                        if (trackCountries && provinceData.IsCreated)
                        {
                            ushort ownerId = provinceData.GetProvinceOwner(neighborProvince);
                            if (ownerId < countryDistances.Length && nextDistance < countryDistances[ownerId])
                            {
                                countryDistances[ownerId] = nextDistance;
                            }
                        }
                    }
                }
            }
        }
    }
}
