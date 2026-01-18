using Unity.Collections;
using Unity.Mathematics;
using Core.Systems;
using Core.Data;
using Core.Graph;

namespace Core.Queries
{
    /// <summary>
    /// High-performance province data access layer
    /// Provides optimized queries for province information
    /// Performance: All basic queries &lt;0.01ms, cached complex queries
    /// </summary>
    public class ProvinceQueries : System.IDisposable
    {
        private readonly Systems.ProvinceSystem provinceSystem;
        private readonly Systems.CountrySystem countrySystem;
        private readonly Systems.AdjacencySystem adjacencySystem;

        // Lazy-initialized distance calculator for graph queries
        private GraphDistanceCalculator distanceCalculator;

        // Reusable buffer for connected region queries
        private NativeList<ushort> connectedRegionBuffer;

        // Performance monitoring
        private static int queryCount;
        private static float totalQueryTime;

        public ProvinceQueries(Systems.ProvinceSystem provinceSystem, Systems.CountrySystem countrySystem, Systems.AdjacencySystem adjacencySystem = null)
        {
            this.provinceSystem = provinceSystem;
            this.countrySystem = countrySystem;
            this.adjacencySystem = adjacencySystem;
        }

        #region Basic Queries (Ultra-fast, direct access)

        /// <summary>
        /// Get province owner - most common query (must be ultra-fast)
        /// Performance target: &lt;0.001ms
        /// </summary>
        public ushort GetOwner(ushort provinceId)
        {
            return provinceSystem.GetProvinceOwner(provinceId);
        }

        // REMOVED: GetDevelopment() - game-specific, moved to Game layer

        /// <summary>
        /// Get province terrain type (now ushort)
        /// Performance target: &lt;0.001ms
        /// </summary>
        public ushort GetTerrain(ushort provinceId)
        {
            return provinceSystem.GetProvinceTerrain(provinceId);
        }

        /// <summary>
        /// Get complete province state (8-byte struct)
        /// Performance target: &lt;0.001ms
        /// </summary>
        public ProvinceState GetProvinceState(ushort provinceId)
        {
            return provinceSystem.GetProvinceState(provinceId);
        }

        /// <summary>
        /// Check if province exists
        /// Performance target: &lt;0.001ms
        /// </summary>
        public bool Exists(ushort provinceId)
        {
            return provinceSystem.HasProvince(provinceId);
        }

        /// <summary>
        /// Check if province is owned by any country
        /// Performance target: &lt;0.001ms
        /// </summary>
        public bool IsOwned(ushort provinceId)
        {
            return GetOwner(provinceId) != 0;
        }

        /// <summary>
        /// Check if province is ocean
        /// Performance target: &lt;0.001ms
        /// </summary>
        public bool IsOcean(ushort provinceId)
        {
            return GetTerrain(provinceId) == 0; // Terrain 0 = Ocean
        }

        #endregion

        #region Computed Queries (Calculate on demand)

        /// <summary>
        /// Get all provinces owned by a specific country
        /// Returns native array that must be disposed by caller
        /// Performance target: &lt;5ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetCountryProvinces(ushort countryId, Allocator allocator = Allocator.TempJob)
        {
            return provinceSystem.GetCountryProvinces(countryId, allocator);
        }

        /// <summary>
        /// Get all provinces owned by a specific country (fills existing NativeList)
        /// Zero-allocation overload for hot paths (monthly tick, etc.)
        /// Performance target: &lt;5ms for 10k provinces
        /// </summary>
        public void GetCountryProvinces(ushort countryId, NativeList<ushort> resultBuffer)
        {
            provinceSystem.GetCountryProvinces(countryId, resultBuffer);
        }

        // REMOVED: GetCountryTotalDevelopment() - game-specific, moved to Game layer

        /// <summary>
        /// Get number of provinces owned by a country
        /// Performance target: &lt;2ms for 10k provinces
        /// </summary>
        public int GetCountryProvinceCount(ushort countryId)
        {
            var provinces = GetCountryProvinces(countryId, Allocator.Temp);
            int count = provinces.Length;
            provinces.Dispose();
            return count;
        }

        /// <summary>
        /// Get provinces by terrain type
        /// Returns native array that must be disposed by caller
        /// Performance target: &lt;5ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetProvincesByTerrain(ushort terrainType, Allocator allocator = Allocator.TempJob)
        {
            var allProvinces = provinceSystem.GetAllProvinceIds(Allocator.Temp);
            var result = new NativeList<ushort>(allProvinces.Length / 4, Allocator.Temp);

            for (int i = 0; i < allProvinces.Length; i++)
            {
                if (GetTerrain(allProvinces[i]) == terrainType)
                {
                    result.Add(allProvinces[i]);
                }
            }

            allProvinces.Dispose();

            var resultArray = new NativeArray<ushort>(result.Length, allocator);
            result.AsArray().CopyTo(resultArray);
            result.Dispose();

            return resultArray;
        }

        /// <summary>
        /// Get unowned provinces (available for colonization)
        /// Returns native array that must be disposed by caller
        /// Performance target: &lt;5ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetUnownedProvinces(Allocator allocator = Allocator.TempJob)
        {
            return GetCountryProvinces(0, allocator); // Country 0 = unowned
        }

        /// <summary>
        /// Get ocean provinces
        /// Returns native array that must be disposed by caller
        /// Performance target: &lt;5ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetOceanProvinces(Allocator allocator = Allocator.TempJob)
        {
            return GetProvincesByTerrain(0, allocator); // Terrain 0 = Ocean
        }

        /// <summary>
        /// Get land provinces (non-ocean)
        /// Returns native array that must be disposed by caller
        /// Performance target: &lt;5ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetLandProvinces(Allocator allocator = Allocator.TempJob)
        {
            var allProvinces = provinceSystem.GetAllProvinceIds(Allocator.Temp);
            var result = new NativeList<ushort>(allProvinces.Length / 2, Allocator.Temp);

            for (int i = 0; i < allProvinces.Length; i++)
            {
                if (!IsOcean(allProvinces[i]))
                {
                    result.Add(allProvinces[i]);
                }
            }

            allProvinces.Dispose();

            var resultArray = new NativeArray<ushort>(result.Length, allocator);
            result.AsArray().CopyTo(resultArray);
            result.Dispose();

            return resultArray;
        }

        #endregion

        #region Cross-System Queries (Province + Country data)

        /// <summary>
        /// Get the color of the country that owns this province
        /// Performance target: &lt;0.01ms
        /// </summary>
        public UnityEngine.Color32 GetProvinceOwnerColor(ushort provinceId)
        {
            ushort ownerId = GetOwner(provinceId);
            return countrySystem.GetCountryColor(ownerId);
        }

        /// <summary>
        /// Get the tag of the country that owns this province
        /// Performance target: &lt;0.01ms
        /// </summary>
        public string GetProvinceOwnerTag(ushort provinceId)
        {
            ushort ownerId = GetOwner(provinceId);
            return countrySystem.GetCountryTag(ownerId);
        }

        /// <summary>
        /// Check if two provinces are owned by the same country
        /// Performance target: &lt;0.01ms
        /// </summary>
        public bool ShareSameOwner(ushort provinceId1, ushort provinceId2)
        {
            return GetOwner(provinceId1) == GetOwner(provinceId2);
        }

        /// <summary>
        /// Get all provinces that border a specific country (adjacent to but not owned by).
        /// Useful for finding invasion targets, border fortification candidates, etc.
        /// Returns native list that must be disposed by caller.
        /// Performance target: less than 15ms
        /// </summary>
        public NativeList<ushort> GetProvincesBorderingCountry(ushort countryId, Allocator allocator = Allocator.TempJob)
        {
            var result = new NativeList<ushort>(64, allocator);

            if (adjacencySystem == null)
            {
                ArchonLogger.LogWarning("ProvinceQueries: AdjacencySystem not available for border queries", "core_simulation");
                return result;
            }

            // Get all provinces of the target country
            using var countryProvinces = GetCountryProvinces(countryId, Allocator.Temp);

            // Track unique bordering provinces to avoid duplicates
            var borderingSet = new NativeHashSet<ushort>(64, Allocator.Temp);

            try
            {
                for (int i = 0; i < countryProvinces.Length; i++)
                {
                    using var neighbors = adjacencySystem.GetNeighbors(countryProvinces[i], Allocator.Temp);

                    for (int j = 0; j < neighbors.Length; j++)
                    {
                        ushort neighbor = neighbors[j];
                        ushort neighborOwner = GetOwner(neighbor);

                        // Province borders the country if adjacent but owned by someone else
                        if (neighborOwner != countryId && !borderingSet.Contains(neighbor))
                        {
                            borderingSet.Add(neighbor);
                            result.Add(neighbor);
                        }
                    }
                }
            }
            finally
            {
                borderingSet.Dispose();
            }

            return result;
        }

        /// <summary>
        /// Get all provinces that border a specific country, filtered by owner.
        /// Example: Get all YOUR provinces that border enemy country X.
        /// Returns native list that must be disposed by caller.
        /// Performance target: less than 15ms
        /// </summary>
        public NativeList<ushort> GetProvincesBorderingCountry(ushort countryId, ushort filterOwnerId, Allocator allocator = Allocator.TempJob)
        {
            var result = new NativeList<ushort>(32, allocator);

            if (adjacencySystem == null)
            {
                ArchonLogger.LogWarning("ProvinceQueries: AdjacencySystem not available for border queries", "core_simulation");
                return result;
            }

            using var countryProvinces = GetCountryProvinces(countryId, Allocator.Temp);
            var borderingSet = new NativeHashSet<ushort>(64, Allocator.Temp);

            try
            {
                for (int i = 0; i < countryProvinces.Length; i++)
                {
                    using var neighbors = adjacencySystem.GetNeighbors(countryProvinces[i], Allocator.Temp);

                    for (int j = 0; j < neighbors.Length; j++)
                    {
                        ushort neighbor = neighbors[j];
                        ushort neighborOwner = GetOwner(neighbor);

                        // Only include if owned by the filter owner
                        if (neighborOwner == filterOwnerId && !borderingSet.Contains(neighbor))
                        {
                            borderingSet.Add(neighbor);
                            result.Add(neighbor);
                        }
                    }
                }
            }
            finally
            {
                borderingSet.Dispose();
            }

            return result;
        }

        /// <summary>
        /// Get provinces owned by countries with a specific tag pattern
        /// Example: Get all provinces owned by countries with tags starting with "GER"
        /// Returns native array that must be disposed by caller
        /// Performance target: &lt;10ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetProvincesByOwnerTag(string tagPattern, Allocator allocator = Allocator.TempJob)
        {
            var allCountries = countrySystem.GetAllCountryIds(Allocator.Temp);
            var matchingCountries = new NativeList<ushort>(allCountries.Length / 10, Allocator.Temp);

            // Find countries with matching tags
            for (int i = 0; i < allCountries.Length; i++)
            {
                var tag = countrySystem.GetCountryTag(allCountries[i]);
                if (tag.StartsWith(tagPattern))
                {
                    matchingCountries.Add(allCountries[i]);
                }
            }

            allCountries.Dispose();

            // Collect all provinces owned by matching countries
            var result = new NativeList<ushort>(1000, Allocator.Temp);

            for (int i = 0; i < matchingCountries.Length; i++)
            {
                var provinces = GetCountryProvinces(matchingCountries[i], Allocator.Temp);
                for (int j = 0; j < provinces.Length; j++)
                {
                    result.Add(provinces[j]);
                }
                provinces.Dispose();
            }

            matchingCountries.Dispose();

            var resultArray = new NativeArray<ushort>(result.Length, allocator);
            result.AsArray().CopyTo(resultArray);
            result.Dispose();

            return resultArray;
        }

        #endregion

        #region Utility Queries

        /// <summary>
        /// Get all active province IDs
        /// Returns native array that must be disposed by caller
        /// Performance target: &lt;1ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetAllProvinceIds(Allocator allocator = Allocator.TempJob)
        {
            return provinceSystem.GetAllProvinceIds(allocator);
        }

        /// <summary>
        /// Get total number of provinces in the system
        /// Performance target: &lt;0.001ms
        /// </summary>
        public int GetTotalProvinceCount()
        {
            return provinceSystem.ProvinceCount;
        }

        /// <summary>
        /// Get province statistics for debugging/UI (engine-only data)
        /// Performance target: &lt;10ms for 10k provinces
        /// Note: Development stats removed (game-specific)
        /// </summary>
        public ProvinceStatistics GetProvinceStatistics()
        {
            var stats = new ProvinceStatistics();
            var allProvinces = GetAllProvinceIds(Allocator.Temp);

            stats.TotalProvinces = allProvinces.Length;

            for (int i = 0; i < allProvinces.Length; i++)
            {
                var provinceId = allProvinces[i];

                if (IsOcean(provinceId))
                    stats.OceanProvinces++;
                else
                    stats.LandProvinces++;

                if (IsOwned(provinceId))
                    stats.OwnedProvinces++;
                else
                    stats.UnownedProvinces++;
            }

            allProvinces.Dispose();
            return stats;
        }

        #endregion

        #region Performance Monitoring

        /// <summary>
        /// Get query performance statistics
        /// </summary>
        public static QueryPerformanceStats GetPerformanceStats()
        {
            return new QueryPerformanceStats
            {
                QueryCount = queryCount,
                TotalQueryTime = totalQueryTime,
                AverageQueryTime = queryCount > 0 ? totalQueryTime / queryCount : 0f
            };
        }

        /// <summary>
        /// Reset performance statistics
        /// </summary>
        public static void ResetPerformanceStats()
        {
            queryCount = 0;
            totalQueryTime = 0f;
        }

        #endregion

        #region Distance Queries (Graph-based)

        /// <summary>
        /// Get all provinces within N hops of a source province.
        /// Requires AdjacencySystem to be available.
        /// Returns native list that must be disposed by caller.
        /// Performance target: less than 20ms for distance=10
        /// </summary>
        public NativeList<ushort> GetProvincesWithinDistance(
            ushort sourceProvinceId,
            byte maxDistance,
            Allocator allocator = Allocator.TempJob)
        {
            var result = new NativeList<ushort>(64, allocator);

            if (adjacencySystem == null)
            {
                ArchonLogger.LogWarning("ProvinceQueries: AdjacencySystem not available for distance queries", "core_simulation");
                return result;
            }

            EnsureDistanceCalculatorInitialized();

            distanceCalculator.CalculateDistancesFromProvince(
                sourceProvinceId,
                adjacencySystem.GetNativeData());

            // Get all provinces within distance
            using var tempResult = distanceCalculator.GetProvincesWithinDistance(maxDistance, Allocator.Temp);
            for (int i = 0; i < tempResult.Length; i++)
            {
                result.Add(tempResult[i]);
            }

            return result;
        }

        /// <summary>
        /// Get distance between two provinces (BFS hops).
        /// Returns byte.MaxValue if unreachable or AdjacencySystem not available.
        /// Performance target: less than 5ms
        /// </summary>
        public byte GetDistanceBetween(ushort province1, ushort province2)
        {
            if (adjacencySystem == null)
            {
                ArchonLogger.LogWarning("ProvinceQueries: AdjacencySystem not available for distance queries", "core_simulation");
                return byte.MaxValue;
            }

            if (province1 == province2)
                return 0;

            EnsureDistanceCalculatorInitialized();

            return distanceCalculator.GetDistanceBetween(province1, province2, adjacencySystem);
        }

        /// <summary>
        /// Get distances from all provinces of a country.
        /// Useful for influence calculations.
        /// Returns byte.MaxValue for provinces that are unreachable.
        /// Performance target: less than 30ms
        /// </summary>
        public void CalculateDistancesFromCountry(ushort countryId)
        {
            if (adjacencySystem == null)
            {
                ArchonLogger.LogWarning("ProvinceQueries: AdjacencySystem not available for distance queries", "core_simulation");
                return;
            }

            EnsureDistanceCalculatorInitialized();

            distanceCalculator.CalculateDistancesFromCountry(countryId, provinceSystem, adjacencySystem);
        }

        /// <summary>
        /// Get province distance from the last CalculateDistancesFromCountry call.
        /// Must call CalculateDistancesFromCountry first.
        /// </summary>
        public byte GetCachedProvinceDistance(ushort provinceId)
        {
            if (distanceCalculator == null || !distanceCalculator.IsInitialized)
                return byte.MaxValue;

            return distanceCalculator.GetProvinceDistance(provinceId);
        }

        private void EnsureDistanceCalculatorInitialized()
        {
            if (distanceCalculator == null)
            {
                distanceCalculator = new GraphDistanceCalculator();
            }

            if (!distanceCalculator.IsInitialized)
            {
                // Use Capacity for buffer size (accounts for sparse province IDs)
                int maxProvinceId = provinceSystem.Capacity > 0 ? provinceSystem.Capacity : 20000;
                int countryCount = countrySystem.CountryCount > 0 ? countrySystem.CountryCount : 500;
                distanceCalculator.Initialize(maxProvinceId, countryCount);
            }
        }

        #endregion

        #region Connected Region Queries (Flood Fill)

        /// <summary>
        /// Find all provinces connected to source that belong to the same country.
        /// Uses flood-fill through adjacency graph.
        /// Returns native list that must be disposed by caller.
        /// Performance target: less than 20ms
        /// </summary>
        public NativeList<ushort> GetConnectedProvincesOfSameOwner(
            ushort sourceProvinceId,
            Allocator allocator = Allocator.TempJob)
        {
            ushort ownerId = GetOwner(sourceProvinceId);
            return GetConnectedProvincesWithOwner(sourceProvinceId, ownerId, allocator);
        }

        /// <summary>
        /// Find all provinces connected to source that have a specific owner.
        /// Uses flood-fill through adjacency graph.
        /// Returns native list that must be disposed by caller.
        /// Performance target: less than 20ms
        /// </summary>
        public NativeList<ushort> GetConnectedProvincesWithOwner(
            ushort sourceProvinceId,
            ushort targetOwnerId,
            Allocator allocator = Allocator.TempJob)
        {
            var result = new NativeList<ushort>(64, allocator);

            if (adjacencySystem == null)
            {
                ArchonLogger.LogWarning("ProvinceQueries: AdjacencySystem not available for connected region queries", "core_simulation");
                return result;
            }

            // Check if source matches the target owner
            if (GetOwner(sourceProvinceId) != targetOwnerId)
                return result;

            // Initialize buffer if needed
            if (!connectedRegionBuffer.IsCreated)
            {
                connectedRegionBuffer = new NativeList<ushort>(256, Allocator.Persistent);
            }

            // BFS flood fill
            var visited = new NativeHashSet<ushort>(256, Allocator.Temp);
            connectedRegionBuffer.Clear();
            connectedRegionBuffer.Add(sourceProvinceId);
            visited.Add(sourceProvinceId);
            result.Add(sourceProvinceId);

            int queueIndex = 0;
            while (queueIndex < connectedRegionBuffer.Length)
            {
                ushort current = connectedRegionBuffer[queueIndex++];

                // Get neighbors
                using var neighbors = adjacencySystem.GetNeighbors(current, Allocator.Temp);

                for (int i = 0; i < neighbors.Length; i++)
                {
                    ushort neighbor = neighbors[i];

                    if (visited.Contains(neighbor))
                        continue;

                    visited.Add(neighbor);

                    // Check if neighbor has the target owner
                    if (GetOwner(neighbor) == targetOwnerId)
                    {
                        connectedRegionBuffer.Add(neighbor);
                        result.Add(neighbor);
                    }
                }
            }

            visited.Dispose();
            return result;
        }

        /// <summary>
        /// Find all connected landmasses owned by a country.
        /// Returns a list of province groups (each group is a connected landmass).
        /// Each inner list must be disposed by caller.
        /// Performance target: less than 30ms
        /// </summary>
        public NativeList<NativeList<ushort>> GetConnectedLandmasses(
            ushort countryId,
            Allocator allocator = Allocator.TempJob)
        {
            var result = new NativeList<NativeList<ushort>>(4, allocator);

            if (adjacencySystem == null)
            {
                ArchonLogger.LogWarning("ProvinceQueries: AdjacencySystem not available for connected region queries", "core_simulation");
                return result;
            }

            // Get all provinces of the country
            using var countryProvinces = GetCountryProvinces(countryId, Allocator.Temp);

            if (countryProvinces.Length == 0)
                return result;

            // Track which provinces we've already assigned to a landmass
            var assigned = new NativeHashSet<ushort>(countryProvinces.Length, Allocator.Temp);

            for (int i = 0; i < countryProvinces.Length; i++)
            {
                ushort provinceId = countryProvinces[i];

                if (assigned.Contains(provinceId))
                    continue;

                // Skip ocean provinces
                if (IsOcean(provinceId))
                {
                    assigned.Add(provinceId);
                    continue;
                }

                // Found a new landmass - flood fill to get all connected provinces
                var landmass = GetConnectedProvincesWithOwner(provinceId, countryId, allocator);

                // Mark all as assigned
                for (int j = 0; j < landmass.Length; j++)
                {
                    assigned.Add(landmass[j]);
                }

                result.Add(landmass);
            }

            assigned.Dispose();
            return result;
        }

        /// <summary>
        /// Get number of separate landmasses owned by a country.
        /// Useful for detecting if a country has disconnected territories.
        /// Performance target: less than 30ms
        /// </summary>
        public int GetLandmassCount(ushort countryId)
        {
            using var landmasses = GetConnectedLandmasses(countryId, Allocator.Temp);
            int count = landmasses.Length;

            // Dispose inner lists
            for (int i = 0; i < landmasses.Length; i++)
            {
                landmasses[i].Dispose();
            }

            return count;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            distanceCalculator?.Dispose();
            distanceCalculator = null;

            if (connectedRegionBuffer.IsCreated)
            {
                connectedRegionBuffer.Dispose();
            }
        }

        #endregion
    }

    /// <summary>
    /// Province statistics for debugging and UI (engine-only data)
    /// Note: Development fields removed (game-specific)
    /// </summary>
    public struct ProvinceStatistics
    {
        public int TotalProvinces;
        public int LandProvinces;
        public int OceanProvinces;
        public int OwnedProvinces;
        public int UnownedProvinces;
    }

    /// <summary>
    /// Query performance statistics
    /// </summary>
    public struct QueryPerformanceStats
    {
        public int QueryCount;
        public float TotalQueryTime;
        public float AverageQueryTime;
    }
}