using Unity.Collections;
using UnityEngine;
using Core.Systems;
using Core.Data;

namespace Core.Queries
{
    /// <summary>
    /// High-performance country data access layer
    /// Provides optimized queries for country/nation information
    /// Performance: All basic queries <0.01ms, cached complex queries
    /// </summary>
    public class CountryQueries
    {
        private readonly Systems.CountrySystem countrySystem;
        private readonly Systems.ProvinceSystem provinceSystem;

        // Cache for expensive calculations
        private readonly System.Collections.Generic.Dictionary<ushort, CachedCountryData> cachedData;
        private float lastCacheUpdateTime;
        private const float CACHE_LIFETIME = 1.0f; // 1 second cache lifetime

        public CountryQueries(Systems.CountrySystem countrySystem, Systems.ProvinceSystem provinceSystem)
        {
            this.countrySystem = countrySystem;
            this.provinceSystem = provinceSystem;
            this.cachedData = new System.Collections.Generic.Dictionary<ushort, CachedCountryData>();
        }

        #region Basic Queries (Ultra-fast, direct access)

        /// <summary>
        /// Get country color - most common query (must be ultra-fast)
        /// Performance target: <0.001ms
        /// </summary>
        public Color32 GetColor(ushort countryId)
        {
            return countrySystem.GetCountryColor(countryId);
        }

        /// <summary>
        /// Get country tag (3-letter code like "ENG", "FRA")
        /// Performance target: <0.001ms
        /// </summary>
        public string GetTag(ushort countryId)
        {
            return countrySystem.GetCountryTag(countryId);
        }

        /// <summary>
        /// Get country ID from tag
        /// Performance target: <0.001ms
        /// </summary>
        public ushort GetIdFromTag(string tag)
        {
            return countrySystem.GetCountryIdFromTag(tag);
        }

        /// <summary>
        /// Get graphical culture ID for unit graphics
        /// Performance target: <0.001ms
        /// </summary>
        public byte GetGraphicalCulture(ushort countryId)
        {
            return countrySystem.GetCountryGraphicalCulture(countryId);
        }

        /// <summary>
        /// Get complete country hot data (8-byte struct)
        /// Performance target: <0.001ms
        /// </summary>
        public CountryHotData GetHotData(ushort countryId)
        {
            return countrySystem.GetCountryHotData(countryId);
        }

        /// <summary>
        /// Get country cold data (detailed information, lazy-loaded)
        /// Performance target: <0.1ms if cached, variable if loading
        /// </summary>
        public CountryColdData GetColdData(ushort countryId)
        {
            return countrySystem.GetCountryColdData(countryId);
        }

        /// <summary>
        /// Check if country exists
        /// Performance target: <0.001ms
        /// </summary>
        public bool Exists(ushort countryId)
        {
            return countrySystem.HasCountry(countryId);
        }

        /// <summary>
        /// Check if country has specific flag/feature
        /// Performance target: <0.001ms
        /// </summary>
        public bool HasFlag(ushort countryId, byte flag)
        {
            return countrySystem.HasCountryFlag(countryId, flag);
        }

        // Convenience flag methods removed - use GetHotData().HasHistoricalIdeas etc.
        // or HasFlag() for explicit flag checks

        #endregion

        #region Cached Complex Queries (Cross-system, expensive calculations)

        /// <summary>
        /// Get total development of all provinces owned by this country
        /// Performance target: <0.01ms if cached, <5ms if calculating
        /// </summary>
        public int GetTotalDevelopment(ushort countryId)
        {
            var cached = GetCachedData(countryId);
            if (cached.IsValid && Time.time - cached.CalculationTime < CACHE_LIFETIME)
            {
                return cached.TotalDevelopment;
            }

            // Calculate fresh
            var provinces = provinceSystem.GetCountryProvinces(countryId, Allocator.Temp);
            int totalDevelopment = 0;

            for (int i = 0; i < provinces.Length; i++)
            {
                totalDevelopment += provinceSystem.GetProvinceDevelopment(provinces[i]);
            }

            provinces.Dispose();

            // Update cache
            UpdateCachedData(countryId, totalDevelopment: totalDevelopment);

            return totalDevelopment;
        }

        /// <summary>
        /// Get number of provinces owned by this country
        /// Performance target: <0.01ms if cached, <5ms if calculating
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
        /// Performance target: <5ms for 10k provinces
        /// </summary>
        public NativeArray<ushort> GetProvinces(ushort countryId, Allocator allocator = Allocator.TempJob)
        {
            return provinceSystem.GetCountryProvinces(countryId, allocator);
        }

        /// <summary>
        /// Get average development of provinces owned by this country
        /// Performance target: <0.01ms if cached, <5ms if calculating
        /// </summary>
        public float GetAverageDevelopment(ushort countryId)
        {
            int totalDev = GetTotalDevelopment(countryId);
            int provinceCount = GetProvinceCount(countryId);

            return provinceCount > 0 ? (float)totalDev / provinceCount : 0f;
        }

        /// <summary>
        /// Get total land area (non-ocean provinces) owned by this country
        /// Performance target: <0.01ms if cached, <5ms if calculating
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
        /// Check if two countries share any border provinces
        /// Performance target: <10ms for large countries
        /// </summary>
        public bool SharesBorder(ushort countryId1, ushort countryId2)
        {
            if (countryId1 == countryId2)
                return false;

            var provinces1 = GetProvinces(countryId1, Allocator.Temp);
            var provinces2 = GetProvinces(countryId2, Allocator.Temp);

            // For now, simple approach - check if any provinces are adjacent
            // In full implementation, this would use neighbor data
            bool sharesBorder = false;

            // This is a placeholder - real implementation would use adjacency data
            // from ProvinceNeighborDetector or similar system

            provinces1.Dispose();
            provinces2.Dispose();

            return sharesBorder;
        }

        /// <summary>
        /// Compare two countries by total development
        /// Returns: -1 if country1 < country2, 0 if equal, 1 if country1 > country2
        /// Performance target: <0.1ms
        /// </summary>
        public int CompareDevelopment(ushort countryId1, ushort countryId2)
        {
            int dev1 = GetTotalDevelopment(countryId1);
            int dev2 = GetTotalDevelopment(countryId2);

            return dev1.CompareTo(dev2);
        }

        /// <summary>
        /// Compare two countries by province count
        /// Performance target: <0.1ms
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
        /// Performance target: <1ms
        /// </summary>
        public NativeArray<ushort> GetAllCountryIds(Allocator allocator = Allocator.TempJob)
        {
            return countrySystem.GetAllCountryIds(allocator);
        }

        /// <summary>
        /// Get total number of countries in the system
        /// Performance target: <0.001ms
        /// </summary>
        public int GetTotalCountryCount()
        {
            return countrySystem.CountryCount;
        }

        /// <summary>
        /// Get countries sorted by total development
        /// Returns array of country IDs sorted by development (highest first)
        /// Performance target: <50ms for 256 countries
        /// </summary>
        public ushort[] GetCountriesByDevelopment(bool ascending = false)
        {
            var allCountries = GetAllCountryIds(Allocator.Temp);
            var countryList = new System.Collections.Generic.List<(ushort id, int development)>();

            for (int i = 0; i < allCountries.Length; i++)
            {
                var countryId = allCountries[i];
                var development = GetTotalDevelopment(countryId);
                countryList.Add((countryId, development));
            }

            allCountries.Dispose();

            // Sort by development
            if (ascending)
            {
                countryList.Sort((a, b) => a.development.CompareTo(b.development));
            }
            else
            {
                countryList.Sort((a, b) => b.development.CompareTo(a.development));
            }

            // Extract IDs
            var result = new ushort[countryList.Count];
            for (int i = 0; i < countryList.Count; i++)
            {
                result[i] = countryList[i].id;
            }

            return result;
        }

        /// <summary>
        /// Get country statistics for debugging/UI
        /// Performance target: <50ms for 256 countries
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
                int development = GetTotalDevelopment(countryId);

                if (provinceCount > 0)
                {
                    stats.CountriesWithProvinces++;
                    stats.TotalProvinces += provinceCount;
                    stats.TotalDevelopment += development;

                    if (provinceCount > stats.LargestCountryProvinces)
                    {
                        stats.LargestCountryProvinces = provinceCount;
                        stats.LargestCountryId = countryId;
                    }

                    if (development > stats.MostDevelopedCountryDevelopment)
                    {
                        stats.MostDevelopedCountryDevelopment = development;
                        stats.MostDevelopedCountryId = countryId;
                    }
                }
            }

            stats.AverageProvincesPerCountry = stats.CountriesWithProvinces > 0 ?
                (float)stats.TotalProvinces / stats.CountriesWithProvinces : 0f;

            stats.AverageDevelopmentPerCountry = stats.CountriesWithProvinces > 0 ?
                (float)stats.TotalDevelopment / stats.CountriesWithProvinces : 0f;

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

        private void UpdateCachedData(ushort countryId, int? totalDevelopment = null,
                                     int? provinceCount = null, int? landProvinceCount = null)
        {
            var cached = GetCachedData(countryId);
            cached.IsValid = true;
            cached.CalculationTime = Time.time;

            if (totalDevelopment.HasValue)
                cached.TotalDevelopment = totalDevelopment.Value;

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
    /// Cached country data for expensive calculations
    /// </summary>
    internal struct CachedCountryData
    {
        public bool IsValid;
        public float CalculationTime;
        public int TotalDevelopment;
        public int ProvinceCount;
        public int LandProvinceCount;
    }

    /// <summary>
    /// Country statistics for debugging and UI
    /// </summary>
    public struct CountryStatistics
    {
        public int TotalCountries;
        public int CountriesWithProvinces;
        public int TotalProvinces;
        public int TotalDevelopment;
        public float AverageProvincesPerCountry;
        public float AverageDevelopmentPerCountry;
        public int LargestCountryProvinces;
        public ushort LargestCountryId;
        public int MostDevelopedCountryDevelopment;
        public ushort MostDevelopedCountryId;
    }
}