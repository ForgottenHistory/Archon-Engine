using Unity.Collections;
using Unity.Mathematics;
using Core.Systems;
using Core.Data;

namespace Core.Queries
{
    /// <summary>
    /// High-performance province data access layer
    /// Provides optimized queries for province information
    /// Performance: All basic queries <0.01ms, cached complex queries
    /// </summary>
    public class ProvinceQueries
    {
        private readonly Systems.ProvinceSystem provinceSystem;
        private readonly Systems.CountrySystem countrySystem;

        // Performance monitoring
        private static int queryCount;
        private static float totalQueryTime;

        public ProvinceQueries(Systems.ProvinceSystem provinceSystem, Systems.CountrySystem countrySystem)
        {
            this.provinceSystem = provinceSystem;
            this.countrySystem = countrySystem;
        }

        #region Basic Queries (Ultra-fast, direct access)

        /// <summary>
        /// Get province owner - most common query (must be ultra-fast)
        /// Performance target: <0.001ms
        /// </summary>
        public ushort GetOwner(ushort provinceId)
        {
            return provinceSystem.GetProvinceOwner(provinceId);
        }

        /// <summary>
        /// Get province development level
        /// Performance target: <0.001ms
        /// </summary>
        public byte GetDevelopment(ushort provinceId)
        {
            return provinceSystem.GetProvinceDevelopment(provinceId);
        }

        /// <summary>
        /// Get province terrain type
        /// Performance target: <0.001ms
        /// </summary>
        public byte GetTerrain(ushort provinceId)
        {
            return provinceSystem.GetProvinceTerrain(provinceId);
        }

        /// <summary>
        /// Get complete province state (8-byte struct)
        /// Performance target: <0.001ms
        /// </summary>
        public ProvinceState GetProvinceState(ushort provinceId)
        {
            return provinceSystem.GetProvinceState(provinceId);
        }

        /// <summary>
        /// Check if province exists
        /// Performance target: <0.001ms
        /// </summary>
        public bool Exists(ushort provinceId)
        {
            return provinceSystem.HasProvince(provinceId);
        }

        /// <summary>
        /// Check if province is owned by any country
        /// Performance target: <0.001ms
        /// </summary>
        public bool IsOwned(ushort provinceId)
        {
            return GetOwner(provinceId) != 0;
        }

        /// <summary>
        /// Check if province is ocean
        /// Performance target: <0.001ms
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
        /// Performance target: <5ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetCountryProvinces(ushort countryId, Allocator allocator = Allocator.TempJob)
        {
            return provinceSystem.GetCountryProvinces(countryId, allocator);
        }

        /// <summary>
        /// Get total development of all provinces owned by a country
        /// Performance target: <2ms for 10k provinces
        /// </summary>
        public int GetCountryTotalDevelopment(ushort countryId)
        {
            var provinces = GetCountryProvinces(countryId, Allocator.Temp);
            int totalDevelopment = 0;

            for (int i = 0; i < provinces.Length; i++)
            {
                totalDevelopment += GetDevelopment(provinces[i]);
            }

            provinces.Dispose();
            return totalDevelopment;
        }

        /// <summary>
        /// Get number of provinces owned by a country
        /// Performance target: <2ms for 10k provinces
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
        /// Performance target: <5ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetProvincesByTerrain(byte terrainType, Allocator allocator = Allocator.TempJob)
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
        /// Performance target: <5ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetUnownedProvinces(Allocator allocator = Allocator.TempJob)
        {
            return GetCountryProvinces(0, allocator); // Country 0 = unowned
        }

        /// <summary>
        /// Get ocean provinces
        /// Returns native array that must be disposed by caller
        /// Performance target: <5ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetOceanProvinces(Allocator allocator = Allocator.TempJob)
        {
            return GetProvincesByTerrain(0, allocator); // Terrain 0 = Ocean
        }

        /// <summary>
        /// Get land provinces (non-ocean)
        /// Returns native array that must be disposed by caller
        /// Performance target: <5ms for 10k provinces
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
        /// Performance target: <0.01ms
        /// </summary>
        public UnityEngine.Color32 GetProvinceOwnerColor(ushort provinceId)
        {
            ushort ownerId = GetOwner(provinceId);
            return countrySystem.GetCountryColor((byte)ownerId);
        }

        /// <summary>
        /// Get the tag of the country that owns this province
        /// Performance target: <0.01ms
        /// </summary>
        public string GetProvinceOwnerTag(ushort provinceId)
        {
            ushort ownerId = GetOwner(provinceId);
            return countrySystem.GetCountryTag((byte)ownerId);
        }

        /// <summary>
        /// Check if two provinces are owned by the same country
        /// Performance target: <0.01ms
        /// </summary>
        public bool ShareSameOwner(ushort provinceId1, ushort provinceId2)
        {
            return GetOwner(provinceId1) == GetOwner(provinceId2);
        }

        /// <summary>
        /// Get provinces owned by countries with a specific tag pattern
        /// Example: Get all provinces owned by countries with tags starting with "GER"
        /// Returns native array that must be disposed by caller
        /// Performance target: <10ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetProvincesByOwnerTag(string tagPattern, Allocator allocator = Allocator.TempJob)
        {
            var allCountries = countrySystem.GetAllCountryIds(Allocator.Temp);
            var matchingCountries = new NativeList<byte>(allCountries.Length / 10, Allocator.Temp);

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
        /// Performance target: <1ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetAllProvinceIds(Allocator allocator = Allocator.TempJob)
        {
            return provinceSystem.GetAllProvinceIds(allocator);
        }

        /// <summary>
        /// Get total number of provinces in the system
        /// Performance target: <0.001ms
        /// </summary>
        public int GetTotalProvinceCount()
        {
            return provinceSystem.ProvinceCount;
        }

        /// <summary>
        /// Get province statistics for debugging/UI
        /// Performance target: <10ms for 10k provinces
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

                stats.TotalDevelopment += GetDevelopment(provinceId);
            }

            stats.AverageDevelopment = stats.LandProvinces > 0 ? (float)stats.TotalDevelopment / stats.LandProvinces : 0f;

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
    }

    /// <summary>
    /// Province statistics for debugging and UI
    /// </summary>
    public struct ProvinceStatistics
    {
        public int TotalProvinces;
        public int LandProvinces;
        public int OceanProvinces;
        public int OwnedProvinces;
        public int UnownedProvinces;
        public int TotalDevelopment;
        public float AverageDevelopment;
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