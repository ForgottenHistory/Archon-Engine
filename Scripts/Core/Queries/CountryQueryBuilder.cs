using System;
using Unity.Collections;
using Core.Systems;

namespace Core.Queries
{
    /// <summary>
    /// Fluent query builder for country filtering.
    /// Lazy evaluation - filters are applied when terminal operation is called.
    ///
    /// Usage:
    ///   using var results = new CountryQueryBuilder(countrySystem, provinceSystem)
    ///       .WithMinProvinces(5)
    ///       .BorderingCountry(targetCountryId)
    ///       .Execute(Allocator.Temp);
    ///
    /// Performance: O(C) where C = countries, single pass with combined filters.
    /// </summary>
    public struct CountryQueryBuilder : IDisposable
    {
        private readonly CountrySystem countrySystem;
        private readonly ProvinceSystem provinceSystem;
        private readonly AdjacencySystem adjacencySystem;

        // Filter state (all optional, applied in combination)
        private int? filterMinProvinces;
        private int? filterMaxProvinces;
        private ushort? filterBorderingCountry;
        private bool? filterHasProvinces;
        private byte? filterGraphicalCulture;

        // Cached data for expensive queries
        private NativeHashSet<ushort> borderingCountriesSet;
        private bool borderingCountriesCalculated;

        public CountryQueryBuilder(CountrySystem countrySystem, ProvinceSystem provinceSystem = null, AdjacencySystem adjacencySystem = null)
        {
            this.countrySystem = countrySystem;
            this.provinceSystem = provinceSystem;
            this.adjacencySystem = adjacencySystem;

            filterMinProvinces = null;
            filterMaxProvinces = null;
            filterBorderingCountry = null;
            filterHasProvinces = null;
            filterGraphicalCulture = null;

            borderingCountriesSet = default;
            borderingCountriesCalculated = false;
        }

        #region Filter Methods (Chainable)

        /// <summary>
        /// Filter to countries with at least N provinces.
        /// Requires ProvinceSystem.
        /// </summary>
        public CountryQueryBuilder WithMinProvinces(int minCount)
        {
            filterMinProvinces = minCount;
            return this;
        }

        /// <summary>
        /// Filter to countries with at most N provinces.
        /// Requires ProvinceSystem.
        /// </summary>
        public CountryQueryBuilder WithMaxProvinces(int maxCount)
        {
            filterMaxProvinces = maxCount;
            return this;
        }

        /// <summary>
        /// Filter to countries with province count in range.
        /// Requires ProvinceSystem.
        /// </summary>
        public CountryQueryBuilder WithProvinceCount(int min, int max)
        {
            filterMinProvinces = min;
            filterMaxProvinces = max;
            return this;
        }

        /// <summary>
        /// Filter to countries that border a specific country.
        /// Requires ProvinceSystem and AdjacencySystem.
        /// </summary>
        public CountryQueryBuilder BorderingCountry(ushort countryId)
        {
            filterBorderingCountry = countryId;
            return this;
        }

        /// <summary>
        /// Filter to countries that have at least one province.
        /// Requires ProvinceSystem.
        /// </summary>
        public CountryQueryBuilder HasProvinces()
        {
            filterHasProvinces = true;
            return this;
        }

        /// <summary>
        /// Filter to countries with no provinces.
        /// Requires ProvinceSystem.
        /// </summary>
        public CountryQueryBuilder HasNoProvinces()
        {
            filterHasProvinces = false;
            return this;
        }

        /// <summary>
        /// Filter to countries with specific graphical culture.
        /// </summary>
        public CountryQueryBuilder WithGraphicalCulture(byte cultureId)
        {
            filterGraphicalCulture = cultureId;
            return this;
        }

        #endregion

        #region Terminal Operations

        /// <summary>
        /// Execute query and return matching country IDs.
        /// Caller must dispose the returned NativeList.
        /// </summary>
        public NativeList<ushort> Execute(Allocator allocator)
        {
            var results = new NativeList<ushort>(64, allocator);

            // Pre-calculate bordering countries if needed
            EnsureBorderingCountriesCalculated();

            // Single pass through all countries
            using var allCountries = countrySystem.GetAllCountryIds(Allocator.Temp);

            for (int i = 0; i < allCountries.Length; i++)
            {
                ushort countryId = allCountries[i];

                // Skip "unowned" country (ID 0)
                if (countryId == 0)
                    continue;

                if (MatchesAllFilters(countryId))
                {
                    results.Add(countryId);
                }
            }

            return results;
        }

        /// <summary>
        /// Count matching countries without allocating result list.
        /// </summary>
        public int Count()
        {
            using var results = Execute(Allocator.Temp);
            return results.Length;
        }

        /// <summary>
        /// Check if any country matches the filters.
        /// </summary>
        public bool Any()
        {
            EnsureBorderingCountriesCalculated();

            using var allCountries = countrySystem.GetAllCountryIds(Allocator.Temp);

            for (int i = 0; i < allCountries.Length; i++)
            {
                ushort countryId = allCountries[i];
                if (countryId == 0) continue;

                if (MatchesAllFilters(countryId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Get first matching country, or 0 if none.
        /// </summary>
        public ushort FirstOrDefault()
        {
            EnsureBorderingCountriesCalculated();

            using var allCountries = countrySystem.GetAllCountryIds(Allocator.Temp);

            for (int i = 0; i < allCountries.Length; i++)
            {
                ushort countryId = allCountries[i];
                if (countryId == 0) continue;

                if (MatchesAllFilters(countryId))
                {
                    return countryId;
                }
            }

            return 0;
        }

        #endregion

        #region Private Helpers

        private bool MatchesAllFilters(ushort countryId)
        {
            // Graphical culture filter (fast, no system dependency)
            if (filterGraphicalCulture.HasValue)
            {
                if (countrySystem.GetCountryGraphicalCulture(countryId) != filterGraphicalCulture.Value)
                    return false;
            }

            // Province count filters (require ProvinceSystem)
            if ((filterMinProvinces.HasValue || filterMaxProvinces.HasValue || filterHasProvinces.HasValue) && provinceSystem != null)
            {
                using var provinces = provinceSystem.GetCountryProvinces(countryId, Allocator.Temp);
                int provinceCount = provinces.Length;

                if (filterHasProvinces.HasValue)
                {
                    bool hasProvinces = provinceCount > 0;
                    if (hasProvinces != filterHasProvinces.Value)
                        return false;
                }

                if (filterMinProvinces.HasValue && provinceCount < filterMinProvinces.Value)
                    return false;

                if (filterMaxProvinces.HasValue && provinceCount > filterMaxProvinces.Value)
                    return false;
            }

            // Bordering country filter
            if (filterBorderingCountry.HasValue && borderingCountriesSet.IsCreated)
            {
                if (!borderingCountriesSet.Contains(countryId))
                    return false;
            }

            return true;
        }

        private void EnsureBorderingCountriesCalculated()
        {
            if (borderingCountriesCalculated || !filterBorderingCountry.HasValue)
                return;

            if (provinceSystem == null || adjacencySystem == null)
            {
                borderingCountriesCalculated = true;
                return;
            }

            borderingCountriesSet = new NativeHashSet<ushort>(32, Allocator.Temp);
            ushort targetCountryId = filterBorderingCountry.Value;

            // Get all provinces of target country
            using var targetProvinces = provinceSystem.GetCountryProvinces(targetCountryId, Allocator.Temp);

            for (int i = 0; i < targetProvinces.Length; i++)
            {
                using var neighbors = adjacencySystem.GetNeighbors(targetProvinces[i], Allocator.Temp);

                for (int j = 0; j < neighbors.Length; j++)
                {
                    ushort neighborOwner = provinceSystem.GetProvinceOwner(neighbors[j]);

                    // Add owner if different from target and not unowned
                    if (neighborOwner != targetCountryId && neighborOwner != 0)
                    {
                        borderingCountriesSet.Add(neighborOwner);
                    }
                }
            }

            borderingCountriesCalculated = true;
        }

        #endregion

        public void Dispose()
        {
            if (borderingCountriesSet.IsCreated)
            {
                borderingCountriesSet.Dispose();
            }
        }
    }
}
