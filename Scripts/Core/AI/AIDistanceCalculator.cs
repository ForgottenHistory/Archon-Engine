using System.Collections.Generic;
using Core.Systems;
using Unity.Collections;

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

        // Pre-allocated buffers
        private NativeArray<byte> provinceDistances;
        private NativeArray<byte> countryDistances;
        private NativeList<ushort> bfsQueue;
        private NativeList<ushort> neighborBuffer;

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
            neighborBuffer = new NativeList<ushort>(20, Allocator.Persistent);

            isInitialized = true;

            ArchonLogger.Log($"AIDistanceCalculator initialized: {provinceCount} provinces, {countryCount} countries", "core_ai");
        }

        /// <summary>
        /// Calculate distances from player country using BFS.
        /// Updates internal distance arrays.
        /// </summary>
        public void CalculateDistances(ushort playerCountryID, ProvinceSystem provinceSystem, AdjacencySystem adjacencySystem)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogWarning("AIDistanceCalculator not initialized", "core_ai");
                return;
            }

            // Reset distances to max
            for (int i = 0; i < provinceDistances.Length; i++)
            {
                provinceDistances[i] = MAX_DISTANCE;
            }

            for (int i = 0; i < countryDistances.Length; i++)
            {
                countryDistances[i] = MAX_DISTANCE;
            }

            // Player country has distance 0
            countryDistances[playerCountryID] = 0;

            // Get player's provinces as BFS starting points
            bfsQueue.Clear();
            var playerProvinces = provinceSystem.GetCountryProvinces(playerCountryID, Allocator.Temp);

            for (int i = 0; i < playerProvinces.Length; i++)
            {
                ushort provinceID = playerProvinces[i];
                if (provinceID < provinceDistances.Length)
                {
                    provinceDistances[provinceID] = 0;
                    bfsQueue.Add(provinceID);
                }
            }

            playerProvinces.Dispose();

            // BFS to calculate province distances
            int queueIndex = 0;
            while (queueIndex < bfsQueue.Length)
            {
                ushort currentProvince = bfsQueue[queueIndex++];
                byte currentDistance = provinceDistances[currentProvince];

                // Don't explore beyond max distance
                if (currentDistance >= MAX_DISTANCE - 1)
                    continue;

                byte nextDistance = (byte)(currentDistance + 1);

                // Get neighbors
                neighborBuffer.Clear();
                adjacencySystem.GetNeighbors(currentProvince, neighborBuffer);

                for (int i = 0; i < neighborBuffer.Length; i++)
                {
                    ushort neighborProvince = neighborBuffer[i];

                    if (neighborProvince >= provinceDistances.Length)
                        continue;

                    // Only update if we found a shorter path
                    if (nextDistance < provinceDistances[neighborProvince])
                    {
                        provinceDistances[neighborProvince] = nextDistance;
                        bfsQueue.Add(neighborProvince);

                        // Update country distance (minimum of any province)
                        ushort ownerID = provinceSystem.GetProvinceOwner(neighborProvince);
                        if (ownerID < countryDistances.Length && nextDistance < countryDistances[ownerID])
                        {
                            countryDistances[ownerID] = nextDistance;
                        }
                    }
                }
            }

            // Count countries by distance tier for logging
            int[] tierCounts = new int[4];
            for (int i = 0; i < countryDistances.Length; i++)
            {
                byte dist = countryDistances[i];
                if (dist <= 1) tierCounts[0]++;
                else if (dist <= 4) tierCounts[1]++;
                else if (dist <= 8) tierCounts[2]++;
                else tierCounts[3]++;
            }

            ArchonLogger.Log($"Distance calculation complete - Tier distribution: Near({tierCounts[0]}) Medium({tierCounts[1]}) Far({tierCounts[2]}) VeryFar({tierCounts[3]})", "core_ai");
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

            if (neighborBuffer.IsCreated)
                neighborBuffer.Dispose();

            isInitialized = false;
        }
    }
}
