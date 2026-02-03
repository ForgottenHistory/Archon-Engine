using System;
using Unity.Collections;
using Core.Systems;
using Core.Graph;

namespace Core.Queries
{
    /// <summary>
    /// Fluent query builder for province filtering.
    /// Lazy evaluation - filters are applied when terminal operation is called.
    ///
    /// Usage:
    ///   using var results = new ProvinceQueryBuilder(provinceSystem)
    ///       .OwnedBy(countryId)
    ///       .IsLand()
    ///       .Execute(Allocator.Temp);
    ///
    /// Performance: O(P) where P = provinces, single pass with combined filters.
    /// </summary>
    public struct ProvinceQueryBuilder : IDisposable
    {
        private readonly ProvinceSystem provinceSystem;
        private readonly AdjacencySystem adjacencySystem;

        // Filter state (all optional, applied in combination)
        private ushort? filterOwnerId;
        private ushort? filterControllerId;
        private ushort? filterTerrainType;
        private bool? filterIsLand;
        private bool? filterIsOwned;
        private ushort? filterAdjacentTo;
        private ushort? filterBorderingCountry;
        private ushort? filterWithinDistanceSource;
        private byte? filterWithinDistanceMax;

        // For distance queries - lazy initialized
        private GraphDistanceCalculator distanceCalculator;
        private bool distanceCalculatorOwned;

        public ProvinceQueryBuilder(ProvinceSystem provinceSystem, AdjacencySystem adjacencySystem = null)
        {
            this.provinceSystem = provinceSystem;
            this.adjacencySystem = adjacencySystem;

            filterOwnerId = null;
            filterControllerId = null;
            filterTerrainType = null;
            filterIsLand = null;
            filterIsOwned = null;
            filterAdjacentTo = null;
            filterBorderingCountry = null;
            filterWithinDistanceSource = null;
            filterWithinDistanceMax = null;
            distanceCalculator = null;
            distanceCalculatorOwned = false;
        }

        #region Filter Methods (Chainable)

        /// <summary>
        /// Filter to provinces owned by specific country.
        /// </summary>
        public ProvinceQueryBuilder OwnedBy(ushort countryId)
        {
            filterOwnerId = countryId;
            return this;
        }

        /// <summary>
        /// Filter to provinces controlled by specific country.
        /// </summary>
        public ProvinceQueryBuilder ControlledBy(ushort countryId)
        {
            filterControllerId = countryId;
            return this;
        }

        /// <summary>
        /// Filter to provinces with specific terrain type.
        /// </summary>
        public ProvinceQueryBuilder WithTerrain(ushort terrainType)
        {
            filterTerrainType = terrainType;
            return this;
        }

        /// <summary>
        /// Filter to land provinces only (terrain != 0).
        /// </summary>
        public ProvinceQueryBuilder IsLand()
        {
            filterIsLand = true;
            return this;
        }

        /// <summary>
        /// Filter to ocean provinces only (terrain == 0).
        /// </summary>
        public ProvinceQueryBuilder IsOcean()
        {
            filterIsLand = false;
            return this;
        }

        /// <summary>
        /// Filter to owned provinces only (owner != 0).
        /// </summary>
        public ProvinceQueryBuilder IsOwned()
        {
            filterIsOwned = true;
            return this;
        }

        /// <summary>
        /// Filter to unowned provinces only (owner == 0).
        /// </summary>
        public ProvinceQueryBuilder IsUnowned()
        {
            filterIsOwned = false;
            return this;
        }

        /// <summary>
        /// Filter to provinces adjacent to a specific province.
        /// Requires AdjacencySystem.
        /// </summary>
        public ProvinceQueryBuilder AdjacentTo(ushort provinceId)
        {
            filterAdjacentTo = provinceId;
            return this;
        }

        /// <summary>
        /// Filter to provinces that border a specific country (owned by different country but adjacent).
        /// Requires AdjacencySystem.
        /// </summary>
        public ProvinceQueryBuilder BorderingCountry(ushort countryId)
        {
            filterBorderingCountry = countryId;
            return this;
        }

        /// <summary>
        /// Filter to provinces within N hops of source province.
        /// Requires AdjacencySystem.
        /// </summary>
        public ProvinceQueryBuilder WithinDistance(ushort sourceProvinceId, byte maxDistance)
        {
            filterWithinDistanceSource = sourceProvinceId;
            filterWithinDistanceMax = maxDistance;
            return this;
        }

        #endregion

        #region Terminal Operations

        /// <summary>
        /// Execute query and return matching province IDs.
        /// Caller must dispose the returned NativeList.
        /// </summary>
        public NativeList<ushort> Execute(Allocator allocator)
        {
            var results = new NativeList<ushort>(256, allocator);

            // Pre-calculate distance map if needed
            NativeArray<byte> distanceMap = default;
            if (filterWithinDistanceSource.HasValue && filterWithinDistanceMax.HasValue)
            {
                distanceMap = CalculateDistanceMap();
            }

            // Pre-calculate adjacent provinces if needed
            NativeHashSet<ushort> adjacentSet = default;
            if (filterAdjacentTo.HasValue && adjacencySystem != null)
            {
                adjacentSet = new NativeHashSet<ushort>(16, Allocator.Temp);
                using var neighbors = adjacencySystem.GetNeighbors(filterAdjacentTo.Value, Allocator.Temp);
                for (int i = 0; i < neighbors.Length; i++)
                {
                    adjacentSet.Add(neighbors[i]);
                }
            }

            // Pre-calculate provinces bordering country if needed
            NativeHashSet<ushort> borderingSet = default;
            if (filterBorderingCountry.HasValue && adjacencySystem != null)
            {
                borderingSet = CalculateBorderingProvinces(filterBorderingCountry.Value);
            }

            try
            {
                // Optimization: use narrowest pre-computed set as iteration source
                // instead of scanning all provinces
                if (borderingSet.IsCreated)
                {
                    // Iterate bordering set directly (typically small)
                    foreach (var provinceId in borderingSet)
                    {
                        if (MatchesAllFilters(provinceId, distanceMap, adjacentSet, borderingSet))
                        {
                            results.Add(provinceId);
                        }
                    }
                }
                else if (filterOwnerId.HasValue)
                {
                    // Use reverse index for owned provinces (O(k) not O(n))
                    using var ownedProvinces = provinceSystem.GetCountryProvinces(filterOwnerId.Value, Allocator.Temp);
                    for (int i = 0; i < ownedProvinces.Length; i++)
                    {
                        ushort provinceId = ownedProvinces[i];
                        if (MatchesAllFilters(provinceId, distanceMap, adjacentSet, borderingSet))
                        {
                            results.Add(provinceId);
                        }
                    }
                }
                else
                {
                    // Fallback: scan all provinces
                    using var allProvinces = provinceSystem.GetAllProvinceIds(Allocator.Temp);
                    for (int i = 0; i < allProvinces.Length; i++)
                    {
                        ushort provinceId = allProvinces[i];
                        if (MatchesAllFilters(provinceId, distanceMap, adjacentSet, borderingSet))
                        {
                            results.Add(provinceId);
                        }
                    }
                }
            }
            finally
            {
                if (adjacentSet.IsCreated) adjacentSet.Dispose();
                if (borderingSet.IsCreated) borderingSet.Dispose();
            }

            return results;
        }

        /// <summary>
        /// Count matching provinces without allocating result list.
        /// </summary>
        public int Count()
        {
            using var results = Execute(Allocator.Temp);
            return results.Length;
        }

        /// <summary>
        /// Check if any province matches the filters.
        /// </summary>
        public bool Any()
        {
            // Optimization: early exit on first match
            NativeArray<byte> distanceMap = default;
            if (filterWithinDistanceSource.HasValue && filterWithinDistanceMax.HasValue)
            {
                distanceMap = CalculateDistanceMap();
            }

            NativeHashSet<ushort> adjacentSet = default;
            if (filterAdjacentTo.HasValue && adjacencySystem != null)
            {
                adjacentSet = new NativeHashSet<ushort>(16, Allocator.Temp);
                using var neighbors = adjacencySystem.GetNeighbors(filterAdjacentTo.Value, Allocator.Temp);
                for (int i = 0; i < neighbors.Length; i++)
                {
                    adjacentSet.Add(neighbors[i]);
                }
            }

            NativeHashSet<ushort> borderingSet = default;
            if (filterBorderingCountry.HasValue && adjacencySystem != null)
            {
                borderingSet = CalculateBorderingProvinces(filterBorderingCountry.Value);
            }

            try
            {
                using var allProvinces = provinceSystem.GetAllProvinceIds(Allocator.Temp);

                for (int i = 0; i < allProvinces.Length; i++)
                {
                    if (MatchesAllFilters(allProvinces[i], distanceMap, adjacentSet, borderingSet))
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                if (adjacentSet.IsCreated) adjacentSet.Dispose();
                if (borderingSet.IsCreated) borderingSet.Dispose();
            }
        }

        /// <summary>
        /// Get first matching province, or default if none.
        /// </summary>
        public ushort FirstOrDefault()
        {
            NativeArray<byte> distanceMap = default;
            if (filterWithinDistanceSource.HasValue && filterWithinDistanceMax.HasValue)
            {
                distanceMap = CalculateDistanceMap();
            }

            NativeHashSet<ushort> adjacentSet = default;
            if (filterAdjacentTo.HasValue && adjacencySystem != null)
            {
                adjacentSet = new NativeHashSet<ushort>(16, Allocator.Temp);
                using var neighbors = adjacencySystem.GetNeighbors(filterAdjacentTo.Value, Allocator.Temp);
                for (int i = 0; i < neighbors.Length; i++)
                {
                    adjacentSet.Add(neighbors[i]);
                }
            }

            NativeHashSet<ushort> borderingSet = default;
            if (filterBorderingCountry.HasValue && adjacencySystem != null)
            {
                borderingSet = CalculateBorderingProvinces(filterBorderingCountry.Value);
            }

            try
            {
                using var allProvinces = provinceSystem.GetAllProvinceIds(Allocator.Temp);

                for (int i = 0; i < allProvinces.Length; i++)
                {
                    ushort provinceId = allProvinces[i];
                    if (MatchesAllFilters(provinceId, distanceMap, adjacentSet, borderingSet))
                    {
                        return provinceId;
                    }
                }

                return 0;
            }
            finally
            {
                if (adjacentSet.IsCreated) adjacentSet.Dispose();
                if (borderingSet.IsCreated) borderingSet.Dispose();
            }
        }

        #endregion

        #region Private Helpers

        private bool MatchesAllFilters(
            ushort provinceId,
            NativeArray<byte> distanceMap,
            NativeHashSet<ushort> adjacentSet,
            NativeHashSet<ushort> borderingSet)
        {
            // Owner filter
            if (filterOwnerId.HasValue)
            {
                if (provinceSystem.GetProvinceOwner(provinceId) != filterOwnerId.Value)
                    return false;
            }

            // Controller filter
            if (filterControllerId.HasValue)
            {
                if (provinceSystem.GetProvinceController(provinceId) != filterControllerId.Value)
                    return false;
            }

            // Terrain filter
            if (filterTerrainType.HasValue)
            {
                if (provinceSystem.GetProvinceTerrain(provinceId) != filterTerrainType.Value)
                    return false;
            }

            // Land/Ocean filter
            if (filterIsLand.HasValue)
            {
                bool isLand = provinceSystem.GetProvinceTerrain(provinceId) != 0;
                if (isLand != filterIsLand.Value)
                    return false;
            }

            // Owned/Unowned filter
            if (filterIsOwned.HasValue)
            {
                bool isOwned = provinceSystem.GetProvinceOwner(provinceId) != 0;
                if (isOwned != filterIsOwned.Value)
                    return false;
            }

            // Adjacent filter
            if (filterAdjacentTo.HasValue && adjacentSet.IsCreated)
            {
                if (!adjacentSet.Contains(provinceId))
                    return false;
            }

            // Bordering country filter
            if (filterBorderingCountry.HasValue && borderingSet.IsCreated)
            {
                if (!borderingSet.Contains(provinceId))
                    return false;
            }

            // Distance filter
            if (filterWithinDistanceSource.HasValue && filterWithinDistanceMax.HasValue && distanceMap.IsCreated)
            {
                if (provinceId >= distanceMap.Length)
                    return false;

                byte distance = distanceMap[provinceId];
                if (distance > filterWithinDistanceMax.Value || distance == 255)
                    return false;
            }

            return true;
        }

        private NativeArray<byte> CalculateDistanceMap()
        {
            if (adjacencySystem == null)
                return default;

            if (distanceCalculator == null)
            {
                distanceCalculator = new Graph.GraphDistanceCalculator();
                int capacity = provinceSystem.Capacity > 0 ? provinceSystem.Capacity : 20000;
                distanceCalculator.Initialize(capacity);
                distanceCalculatorOwned = true;
            }

            distanceCalculator.CalculateDistancesFromProvince(
                filterWithinDistanceSource.Value,
                adjacencySystem.GetNativeData());

            return distanceCalculator.GetAllProvinceDistances();
        }

        private NativeHashSet<ushort> CalculateBorderingProvinces(ushort countryId)
        {
            var result = new NativeHashSet<ushort>(64, Allocator.Temp);

            if (adjacencySystem == null)
                return result;

            // Get all provinces of the target country
            using var countryProvinces = provinceSystem.GetCountryProvinces(countryId, Allocator.Temp);

            // For each province, get neighbors that are NOT owned by the target country
            for (int i = 0; i < countryProvinces.Length; i++)
            {
                using var neighbors = adjacencySystem.GetNeighbors(countryProvinces[i], Allocator.Temp);

                for (int j = 0; j < neighbors.Length; j++)
                {
                    ushort neighbor = neighbors[j];
                    ushort neighborOwner = provinceSystem.GetProvinceOwner(neighbor);

                    // Province borders the country if it's adjacent but owned by someone else
                    if (neighborOwner != countryId)
                    {
                        result.Add(neighbor);
                    }
                }
            }

            return result;
        }

        #endregion

        /// <summary>
        /// Disposes resources allocated during query execution.
        /// Call this (or use 'using' statement) after Execute() if WithinDistance() filter was used,
        /// as it allocates a GraphDistanceCalculator internally.
        /// </summary>
        public void Dispose()
        {
            if (distanceCalculatorOwned && distanceCalculator != null)
            {
                distanceCalculator.Dispose();
                distanceCalculator = null;
            }
        }
    }
}
