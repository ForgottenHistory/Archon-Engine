using Unity.Collections;
using UnityEngine;
using Core.Systems;
using Core.Data;

namespace Core.Queries
{
    /// <summary>
    /// High-performance country data access layer
    /// Provides optimized queries for country/nation information
    /// Performance: All basic queries &lt;0.01ms, cached complex queries
    /// </summary>
    public class CountryQueries
    {
        private readonly Systems.CountrySystem countrySystem;
        private readonly Systems.ProvinceSystem provinceSystem;
        private readonly Systems.AdjacencySystem adjacencySystem;

        // Cache for expensive calculations
        private readonly System.Collections.Generic.Dictionary<ushort, CachedCountryData> cachedData;
        private float lastCacheUpdateTime;
        private const float CACHE_LIFETIME = 1.0f; // 1 second cache lifetime

        // Reusable buffer for neighbor queries (avoids allocation in hot paths)
        private readonly NativeList<ushort> neighborBuffer;

        public CountryQueries(Systems.CountrySystem countrySystem, Systems.ProvinceSystem provinceSystem, Systems.AdjacencySystem adjacencySystem)
        {
            this.countrySystem = countrySystem;
            this.provinceSystem = provinceSystem;
            this.adjacencySystem = adjacencySystem;
            this.cachedData = new System.Collections.Generic.Dictionary<ushort, CachedCountryData>();
            this.neighborBuffer = new NativeList<ushort>(32, Allocator.Persistent);
        }

        #region Basic Queries (Ultra-fast, direct access)

        /// <summary>
        /// Get country color - most common query (must be ultra-fast)
        /// Performance target: &lt;0.001ms
        /// </summary>
        public Color32 GetColor(ushort countryId)
        {
            return countrySystem.GetCountryColor(countryId);
        }

        /// <summary>
        /// Get country tag (3-letter code like "ENG", "FRA")
        /// Performance target: &lt;0.001ms
        /// </summary>
        public string GetTag(ushort countryId)
        {
            return countrySystem.GetCountryTag(countryId);
        }

        /// <summary>
        /// Get country ID from tag
        /// Performance target: &lt;0.001ms
        /// </summary>
        public ushort GetIdFromTag(string tag)
        {
            return countrySystem.GetCountryIdFromTag(tag);
        }

        /// <summary>
        /// Get graphical culture ID for unit graphics
        /// Performance target: &lt;0.001ms
        /// </summary>
        public byte GetGraphicalCulture(ushort countryId)
        {
            return countrySystem.GetCountryGraphicalCulture(countryId);
        }

        /// <summary>
        /// Get complete country hot data (8-byte struct)
        /// Performance target: &lt;0.001ms
        /// </summary>
        public CountryHotData GetHotData(ushort countryId)
        {
            return countrySystem.GetCountryHotData(countryId);
        }

        /// <summary>
        /// Get country cold data (detailed information, lazy-loaded)
        /// Performance target: &lt;0.1ms if cached, variable if loading
        /// </summary>
        public CountryColdData GetColdData(ushort countryId)
        {
            return countrySystem.GetCountryColdData(countryId);
        }

        /// <summary>
        /// Check if country exists
        /// Performance target: &lt;0.001ms
        /// </summary>
        public bool Exists(ushort countryId)
        {
            return countrySystem.HasCountry(countryId);
        }

        /// <summary>
        /// Check if country has specific flag/feature
        /// Performance target: &lt;0.001ms
        /// </summary>
        public bool HasFlag(ushort countryId, byte flag)
        {
            return countrySystem.HasCountryFlag(countryId, flag);
        }

        // Convenience flag methods removed - use GetHotData().HasHistoricalIdeas etc.
        // or HasFlag() for explicit flag checks

        #endregion

        #region Cached Complex Queries (Cross-system, expensive calculations)

        // REMOVED: GetTotalDevelopment()
        // Development is game-specific and belongs in Game layer
        // This method has been moved to Game/Queries/HegemonCountryQueries.cs
        // Migration: Use HegemonCountryQueries.GetTotalDevelopment(countryId) instead

        /// <summary>
        /// Get number of provinces owned by this country
        /// Performance target: &lt;0.01ms if cached, &lt;5ms if calculating
        /// </summary>
        public int GetProvinceCount(ushort countryId)
        {
            var cached = GetCachedData(countryId);
            if (cached.IsValid && Time.time - cached.CalculationTime < CACHE_LIFETIME)
            {
                return cached.ProvinceCount;
            }

            // Calculate fresh
            var provinces = provinceSystem.GetCountryProvinces(countryId, Allocator.Temp);
            int count = provinces.Length;
            provinces.Dispose();

            // Update cache
            UpdateCachedData(countryId, provinceCount: count);

            return count;
        }

        /// <summary>
        /// Get provinces owned by this country
        /// Returns native array that must be disposed by caller
        /// Performance target: &lt;5ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetProvinces(ushort countryId, Allocator allocator = Allocator.TempJob)
        {
            return provinceSystem.GetCountryProvinces(countryId, allocator);
        }

        // REMOVED: GetAverageDevelopment()
        // Development is game-specific and belongs in Game layer
        // Migration: Use HegemonCountryQueries.GetAverageDevelopment(countryId) instead

        /// <summary>
        /// Get total land area (non-ocean provinces) owned by this country
        /// Performance target: &lt;0.01ms if cached, &lt;5ms if calculating
        /// </summary>
        public int GetLandProvinceCount(ushort countryId)
        {
            var cached = GetCachedData(countryId);
            if (cached.IsValid && Time.time - cached.CalculationTime < CACHE_LIFETIME)
            {
                return cached.LandProvinceCount;
            }

            // Calculate fresh
            var provinces = GetProvinces(countryId, Allocator.Temp);
            int landCount = 0;

            for (int i = 0; i < provinces.Length; i++)
            {
                if (provinceSystem.GetProvinceTerrain(provinces[i]) != 0) // Not ocean
                {
                    landCount++;
                }
            }

            provinces.Dispose();

            // Update cache
            UpdateCachedData(countryId, landProvinceCount: landCount);

            return landCount;
        }

        #endregion

        #region Country Relationships & Comparisons

        /// <summary>
        /// Check if two countries share any border provinces.
        /// Uses AdjacencySystem to check if any province of country1 is adjacent to any province of country2.
        /// Performance target: O(P × N) where P = provinces, N = ~6 neighbors. Target: less than 10ms.
        /// </summary>
        public bool SharesBorder(ushort countryId1, ushort countryId2)
        {
            if (countryId1 == countryId2)
                return false;

            if (countryId1 == 0 || countryId2 == 0)
                return false; // Unowned doesn't share border

            var provinces1 = GetProvinces(countryId1, Allocator.Temp);

            try
            {
                for (int i = 0; i < provinces1.Length; i++)
                {
                    ushort provinceId = provinces1[i];

                    // Get neighbors using reusable buffer
                    adjacencySystem.GetNeighbors(provinceId, neighborBuffer);

                    for (int j = 0; j < neighborBuffer.Length; j++)
                    {
                        ushort neighborOwner = provinceSystem.GetProvinceOwner(neighborBuffer[j]);
                        if (neighborOwner == countryId2)
                        {
                            return true; // Early exit on first match
                        }
                    }
                }
                return false;
            }
            finally
            {
                provinces1.Dispose();
            }
        }

        /// <summary>
        /// Get all countries that share a border with the specified country.
        /// Returns native list that must be disposed by caller.
        /// Performance target: O(P × N) where P = provinces, N = ~6 neighbors. Target: less than 15ms.
        /// </summary>
        public NativeList<ushort> GetBorderingCountries(ushort countryId, Allocator allocator)
        {
            var result = new NativeList<ushort>(16, allocator);
            GetBorderingCountries(countryId, result);
            return result;
        }

        /// <summary>
        /// Get all countries that share a border with the specified country.
        /// Zero-allocation variant - fills existing buffer.
        /// Performance target: O(P × N) where P = provinces, N = ~6 neighbors. Target: less than 15ms.
        /// </summary>
        public void GetBorderingCountries(ushort countryId, NativeList<ushort> resultBuffer)
        {
            resultBuffer.Clear();

            if (countryId == 0)
                return; // Unowned has no meaningful borders

            var provinces = GetProvinces(countryId, Allocator.Temp);

            // Use a temporary set for deduplication
            var uniqueNeighbors = new NativeHashSet<ushort>(32, Allocator.Temp);

            try
            {
                for (int i = 0; i < provinces.Length; i++)
                {
                    ushort provinceId = provinces[i];

                    // Get neighbors using reusable buffer
                    adjacencySystem.GetNeighbors(provinceId, neighborBuffer);

                    for (int j = 0; j < neighborBuffer.Length; j++)
                    {
                        ushort neighborOwner = provinceSystem.GetProvinceOwner(neighborBuffer[j]);

                        // Skip self and unowned, add unique neighbors
                        if (neighborOwner != countryId && neighborOwner != 0 && neighborOwner != ushort.MaxValue)
                        {
                            uniqueNeighbors.Add(neighborOwner);
                        }
                    }
                }

                // Copy unique neighbors to result
                foreach (var neighbor in uniqueNeighbors)
                {
                    resultBuffer.Add(neighbor);
                }
            }
            finally
            {
                provinces.Dispose();
                uniqueNeighbors.Dispose();
            }
        }

        /// <summary>
        /// Get the number of countries that share a border with the specified country.
        /// Performance target: less than 15ms.
        /// </summary>
        public int GetBorderingCountryCount(ushort countryId)
        {
            using var neighbors = GetBorderingCountries(countryId, Allocator.Temp);
            return neighbors.Length;
        }

        // REMOVED: CompareDevelopment()
        // Development is game-specific and belongs in Game layer
        // Migration: Use HegemonCountryQueries.CompareDevelopment(countryId1, countryId2) instead

        /// <summary>
        /// Compare two countries by province count
        /// Performance target: &lt;0.1ms
        /// </summary>
        public int CompareProvinceCount(ushort countryId1, ushort countryId2)
        {
            int count1 = GetProvinceCount(countryId1);
            int count2 = GetProvinceCount(countryId2);

            return count1.CompareTo(count2);
        }

        #endregion

        #region Utility Queries

        /// <summary>
        /// Get all active country IDs
        /// Returns native array that must be disposed by caller
        /// Performance target: &lt;1ms
        /// </summary>
        public NativeArray<ushort> GetAllCountryIds(Allocator allocator = Allocator.TempJob)
        {
            return countrySystem.GetAllCountryIds(allocator);
        }

        /// <summary>
        /// Get total number of countries in the system
        /// Performance target: &lt;0.001ms
        /// </summary>
        public int GetTotalCountryCount()
        {
            return countrySystem.CountryCount;
        }

        // REMOVED: GetCountriesByDevelopment()
        // Development is game-specific and belongs in Game layer
        // Migration: Use HegemonCountryQueries.GetCountriesByDevelopment(ascending) instead

        /// <summary>
        /// Get country statistics for debugging/UI (engine-only data - no game-specific fields)
        /// Performance target: &lt;50ms for 256 countries
        /// Note: Development statistics removed (game-specific). Use HegemonCountryQueries for game stats.
        /// </summary>
        public CountryStatistics GetCountryStatistics()
        {
            var stats = new CountryStatistics();
            var allCountries = GetAllCountryIds(Allocator.Temp);

            stats.TotalCountries = allCountries.Length;

            for (int i = 0; i < allCountries.Length; i++)
            {
                var countryId = allCountries[i];

                if (countryId == 0) // Skip "unowned" country
                    continue;

                int provinceCount = GetProvinceCount(countryId);

                if (provinceCount > 0)
                {
                    stats.CountriesWithProvinces++;
                    stats.TotalProvinces += provinceCount;

                    if (provinceCount > stats.LargestCountryProvinces)
                    {
                        stats.LargestCountryProvinces = provinceCount;
                        stats.LargestCountryId = countryId;
                    }
                }
            }

            stats.AverageProvincesPerCountry = stats.CountriesWithProvinces > 0 ?
                (float)stats.TotalProvinces / stats.CountriesWithProvinces : 0f;

            allCountries.Dispose();
            return stats;
        }

        #endregion

        #region Cache Management

        private CachedCountryData GetCachedData(ushort countryId)
        {
            if (cachedData.TryGetValue(countryId, out var cached))
                return cached;

            return new CachedCountryData(); // Invalid by default
        }

        private void UpdateCachedData(ushort countryId, int? provinceCount = null, int? landProvinceCount = null)
        {
            var cached = GetCachedData(countryId);
            cached.IsValid = true;
            cached.CalculationTime = Time.time;

            if (provinceCount.HasValue)
                cached.ProvinceCount = provinceCount.Value;

            if (landProvinceCount.HasValue)
                cached.LandProvinceCount = landProvinceCount.Value;

            cachedData[countryId] = cached;
        }

        /// <summary>
        /// Clear cache for a specific country (call when country data changes)
        /// </summary>
        public void InvalidateCache(ushort countryId)
        {
            cachedData.Remove(countryId);
        }

        /// <summary>
        /// Clear all cached data
        /// </summary>
        public void ClearCache()
        {
            cachedData.Clear();
        }

        #endregion
    }

    /// <summary>
    /// Cached country data for expensive calculations (engine-only data)
    /// Note: TotalDevelopment removed (game-specific)
    /// </summary>
    internal struct CachedCountryData
    {
        public bool IsValid;
        public float CalculationTime;
        public int ProvinceCount;
        public int LandProvinceCount;
    }

    /// <summary>
    /// Country statistics for debugging and UI (engine-only data)
    /// Note: Development-related fields removed (game-specific)
    /// Use Game/Queries/HegemonCountryQueries for game statistics
    /// </summary>
    public struct CountryStatistics
    {
        public int TotalCountries;
        public int CountriesWithProvinces;
        public int TotalProvinces;
        public float AverageProvincesPerCountry;
        public int LargestCountryProvinces;
        public ushort LargestCountryId;
    }
}