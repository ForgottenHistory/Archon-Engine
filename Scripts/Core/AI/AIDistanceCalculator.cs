using Core.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Core.AI
{
    /// <summary>
    /// ENGINE LAYER - Calculates distance from player for AI priority tiers.
    ///
    /// Algorithm: BFS from player's provinces through adjacency graph.
    /// Distance = minimum province hops from any player province to any country province.
    ///
    /// Performance:
    /// - O(P) where P = total provinces (single BFS traversal)
    /// - Burst-compiled for SIMD and native code optimization
    /// - Called infrequently (game start, monthly, on major border changes)
    /// - Pre-allocated buffers for zero gameplay allocations
    ///
    /// Usage:
    /// 1. Call CalculateDistances() with player country ID
    /// 2. Call AssignTiers() to update AIState array based on distances
    /// </summary>
    public class AIDistanceCalculator
    {
        private const byte MAX_DISTANCE = 255;

        // Pre-allocated buffers (persistent, reused across calculations)
        private NativeArray<byte> provinceDistances;
        private NativeArray<byte> countryDistances;
        private NativeList<ushort> bfsQueue;
        private NativeList<ushort> playerProvincesBuffer;

        private int provinceCount;
        private int countryCount;
        private bool isInitialized;

        public AIDistanceCalculator()
        {
            isInitialized = false;
        }

        /// <summary>
        /// Initialize buffers for distance calculation.
        /// Call once after province/country counts are known.
        /// </summary>
        public void Initialize(int provinceCount, int countryCount)
        {
            if (isInitialized)
            {
                Dispose();
            }

            this.provinceCount = provinceCount;
            this.countryCount = countryCount;

            provinceDistances = new NativeArray<byte>(provinceCount, Allocator.Persistent);
            countryDistances = new NativeArray<byte>(countryCount, Allocator.Persistent);
            bfsQueue = new NativeList<ushort>(provinceCount, Allocator.Persistent);
            playerProvincesBuffer = new NativeList<ushort>(100, Allocator.Persistent);

            isInitialized = true;

            ArchonLogger.Log($"AIDistanceCalculator initialized: {provinceCount} provinces, {countryCount} countries (Burst-enabled)", "core_ai");
        }

        /// <summary>
        /// Calculate distances from player country using Burst-compiled BFS.
        /// Updates internal distance arrays.
        /// </summary>
        public void CalculateDistances(ushort playerCountryID, ProvinceSystem provinceSystem, AdjacencySystem adjacencySystem)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogWarning("AIDistanceCalculator not initialized", "core_ai");
                return;
            }

            // Get player's provinces as BFS starting points
            playerProvincesBuffer.Clear();
            provinceSystem.GetCountryProvinces(playerCountryID, playerProvincesBuffer);

            // Get native data for Burst job
            var provinceData = provinceSystem.GetNativeData();
            var adjacencyData = adjacencySystem.GetNativeData();

            // Schedule and complete the Burst job
            var job = new BurstBFSDistanceJob
            {
                playerCountryID = playerCountryID,
                playerProvinces = playerProvincesBuffer.AsArray(),
                provinceData = provinceData,
                adjacencyData = adjacencyData,
                provinceDistances = provinceDistances,
                countryDistances = countryDistances,
                bfsQueue = bfsQueue,
                maxDistance = MAX_DISTANCE
            };

            // Execute immediately (Complete blocks until done)
            job.Schedule().Complete();

            // Count countries by distance tier for logging
            int tier0 = 0, tier1 = 0, tier2 = 0, tier3 = 0;
            for (int i = 0; i < countryDistances.Length; i++)
            {
                byte dist = countryDistances[i];
                if (dist <= 1) tier0++;
                else if (dist <= 4) tier1++;
                else if (dist <= 8) tier2++;
                else tier3++;
            }

            ArchonLogger.Log($"Distance calculation complete - Tier distribution: Near({tier0}) Medium({tier1}) Far({tier2}) VeryFar({tier3})", "core_ai");
        }

        /// <summary>
        /// Get distance for a specific country.
        /// </summary>
        public byte GetCountryDistance(ushort countryID)
        {
            if (!isInitialized || countryID >= countryDistances.Length)
                return MAX_DISTANCE;

            return countryDistances[countryID];
        }

        /// <summary>
        /// Assign tiers to all AI states based on calculated distances.
        /// </summary>
        public void AssignTiers(NativeArray<AIState> aiStates, AISchedulingConfig config)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogWarning("AIDistanceCalculator not initialized", "core_ai");
                return;
            }

            for (int i = 0; i < aiStates.Length; i++)
            {
                var state = aiStates[i];
                ushort countryID = state.countryID;

                byte distance = GetCountryDistance(countryID);
                byte tier = config.GetTierForDistance(distance);

                state.tier = tier;
                aiStates[i] = state;
            }

            ArchonLogger.Log($"Assigned tiers to {aiStates.Length} AI states", "core_ai");
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

            if (playerProvincesBuffer.IsCreated)
                playerProvincesBuffer.Dispose();

            isInitialized = false;
        }
    }

    /// <summary>
    /// Burst-compiled BFS job for distance calculation.
    /// Traverses the province adjacency graph from player provinces.
    /// </summary>
    [BurstCompile]
    public struct BurstBFSDistanceJob : IJob
    {
        public ushort playerCountryID;
        [ReadOnly] public NativeArray<ushort> playerProvinces;
        [ReadOnly] public NativeProvinceData provinceData;
        [ReadOnly] public NativeAdjacencyData adjacencyData;

        public NativeArray<byte> provinceDistances;
        public NativeArray<byte> countryDistances;
        public NativeList<ushort> bfsQueue;
        public byte maxDistance;

        public void Execute()
        {
            // Reset distances to max
            for (int i = 0; i < provinceDistances.Length; i++)
            {
                provinceDistances[i] = maxDistance;
            }

            for (int i = 0; i < countryDistances.Length; i++)
            {
                countryDistances[i] = maxDistance;
            }

            // Player country has distance 0
            if (playerCountryID < countryDistances.Length)
            {
                countryDistances[playerCountryID] = 0;
            }

            // Initialize BFS queue with player provinces
            bfsQueue.Clear();
            for (int i = 0; i < playerProvinces.Length; i++)
            {
                ushort provinceID = playerProvinces[i];
                if (provinceID < provinceDistances.Length)
                {
                    provinceDistances[provinceID] = 0;
                    bfsQueue.Add(provinceID);
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
                        ushort ownerID = provinceData.GetProvinceOwner(neighborProvince);
                        if (ownerID < countryDistances.Length && nextDistance < countryDistances[ownerID])
                        {
                            countryDistances[ownerID] = nextDistance;
                        }
                    }
                }
            }
        }
    }
}
