using System;
using Unity.Collections;
using Core.Units;

namespace Core.Queries
{
    /// <summary>
    /// Fluent query builder for unit filtering.
    /// Lazy evaluation - filters are applied when terminal operation is called.
    ///
    /// Usage:
    ///   using var results = new UnitQueryBuilder(unitSystem)
    ///       .OwnedBy(countryId)
    ///       .InProvince(provinceId)
    ///       .Execute(Allocator.Temp);
    ///
    /// Performance: O(U) where U = units, single pass with combined filters.
    ///
    /// NOTE: This provides ENGINE-level queries (location, ownership, type ID).
    /// GAME layer should post-filter for game-specific concepts (unit class, combat role, etc.)
    /// </summary>
    public struct UnitQueryBuilder : IDisposable
    {
        private readonly UnitSystem unitSystem;

        // Filter state (all optional, applied in combination)
        private ushort? filterOwnerId;
        private ushort? filterNotOwnerId;
        private ushort? filterProvinceId;
        private ushort? filterUnitTypeId;
        private ushort? filterMinTroopCount;
        private ushort? filterMaxTroopCount;

        public UnitQueryBuilder(UnitSystem unitSystem)
        {
            this.unitSystem = unitSystem;

            filterOwnerId = null;
            filterNotOwnerId = null;
            filterProvinceId = null;
            filterUnitTypeId = null;
            filterMinTroopCount = null;
            filterMaxTroopCount = null;
        }

        #region Filter Methods (Chainable)

        /// <summary>
        /// Filter to units owned by specific country.
        /// </summary>
        public UnitQueryBuilder OwnedBy(ushort countryId)
        {
            filterOwnerId = countryId;
            return this;
        }

        /// <summary>
        /// Filter to units NOT owned by specific country (enemies).
        /// </summary>
        public UnitQueryBuilder NotOwnedBy(ushort countryId)
        {
            filterNotOwnerId = countryId;
            return this;
        }

        /// <summary>
        /// Filter to units in a specific province.
        /// </summary>
        public UnitQueryBuilder InProvince(ushort provinceId)
        {
            filterProvinceId = provinceId;
            return this;
        }

        /// <summary>
        /// Filter to units of a specific type ID.
        /// </summary>
        public UnitQueryBuilder OfType(ushort unitTypeId)
        {
            filterUnitTypeId = unitTypeId;
            return this;
        }

        /// <summary>
        /// Filter to units with at least N troops.
        /// </summary>
        public UnitQueryBuilder WithMinTroops(ushort minCount)
        {
            filterMinTroopCount = minCount;
            return this;
        }

        /// <summary>
        /// Filter to units with at most N troops.
        /// </summary>
        public UnitQueryBuilder WithMaxTroops(ushort maxCount)
        {
            filterMaxTroopCount = maxCount;
            return this;
        }

        /// <summary>
        /// Filter to units with troop count in range.
        /// </summary>
        public UnitQueryBuilder WithTroopCount(ushort min, ushort max)
        {
            filterMinTroopCount = min;
            filterMaxTroopCount = max;
            return this;
        }

        #endregion

        #region Terminal Operations

        /// <summary>
        /// Execute query and return matching unit IDs.
        /// Caller must dispose the returned NativeList.
        /// </summary>
        public NativeList<ushort> Execute(Allocator allocator)
        {
            var results = new NativeList<ushort>(64, allocator);

            // Optimization: if filtering by province, only iterate units in that province
            if (filterProvinceId.HasValue)
            {
                var provinceUnits = unitSystem.GetUnitsInProvince(filterProvinceId.Value);
                foreach (ushort unitId in provinceUnits)
                {
                    if (MatchesAllFilters(unitId))
                    {
                        results.Add(unitId);
                    }
                }
            }
            // Optimization: if filtering by owner, only iterate units owned by that country
            else if (filterOwnerId.HasValue)
            {
                var countryUnits = unitSystem.GetCountryUnits(filterOwnerId.Value);
                foreach (ushort unitId in countryUnits)
                {
                    if (MatchesAllFilters(unitId))
                    {
                        results.Add(unitId);
                    }
                }
            }
            // Full scan - iterate all possible unit IDs
            else
            {
                int totalUnits = unitSystem.GetUnitCount();
                if (totalUnits == 0)
                    return results;

                // Iterate through unit IDs (start at 1, 0 is reserved)
                for (ushort unitId = 1; unitId <= ushort.MaxValue; unitId++)
                {
                    if (!unitSystem.HasUnit(unitId))
                        continue;

                    if (MatchesAllFilters(unitId))
                    {
                        results.Add(unitId);
                    }

                    // Early exit if we've found all units
                    if (results.Length >= totalUnits)
                        break;
                }
            }

            return results;
        }

        /// <summary>
        /// Count matching units without allocating result list.
        /// </summary>
        public int Count()
        {
            using var results = Execute(Allocator.Temp);
            return results.Length;
        }

        /// <summary>
        /// Check if any unit matches the filters.
        /// </summary>
        public bool Any()
        {
            // Optimization: if filtering by province, only iterate units in that province
            if (filterProvinceId.HasValue)
            {
                var provinceUnits = unitSystem.GetUnitsInProvince(filterProvinceId.Value);
                foreach (ushort unitId in provinceUnits)
                {
                    if (MatchesAllFilters(unitId))
                        return true;
                }
                return false;
            }

            // Optimization: if filtering by owner, only iterate units owned by that country
            if (filterOwnerId.HasValue)
            {
                var countryUnits = unitSystem.GetCountryUnits(filterOwnerId.Value);
                foreach (ushort unitId in countryUnits)
                {
                    if (MatchesAllFilters(unitId))
                        return true;
                }
                return false;
            }

            // Full scan
            for (ushort unitId = 1; unitId <= ushort.MaxValue; unitId++)
            {
                if (!unitSystem.HasUnit(unitId))
                    continue;

                if (MatchesAllFilters(unitId))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Get first matching unit, or 0 if none.
        /// </summary>
        public ushort FirstOrDefault()
        {
            // Optimization: if filtering by province, only iterate units in that province
            if (filterProvinceId.HasValue)
            {
                var provinceUnits = unitSystem.GetUnitsInProvince(filterProvinceId.Value);
                foreach (ushort unitId in provinceUnits)
                {
                    if (MatchesAllFilters(unitId))
                        return unitId;
                }
                return 0;
            }

            // Optimization: if filtering by owner, only iterate units owned by that country
            if (filterOwnerId.HasValue)
            {
                var countryUnits = unitSystem.GetCountryUnits(filterOwnerId.Value);
                foreach (ushort unitId in countryUnits)
                {
                    if (MatchesAllFilters(unitId))
                        return unitId;
                }
                return 0;
            }

            // Full scan
            for (ushort unitId = 1; unitId <= ushort.MaxValue; unitId++)
            {
                if (!unitSystem.HasUnit(unitId))
                    continue;

                if (MatchesAllFilters(unitId))
                    return unitId;
            }

            return 0;
        }

        /// <summary>
        /// Get total troop count of all matching units.
        /// </summary>
        public int TotalTroops()
        {
            int total = 0;
            using var units = Execute(Allocator.Temp);

            for (int i = 0; i < units.Length; i++)
            {
                var unit = unitSystem.GetUnit(units[i]);
                total += unit.unitCount;
            }

            return total;
        }

        #endregion

        #region Private Helpers

        private bool MatchesAllFilters(ushort unitId)
        {
            var unit = unitSystem.GetUnit(unitId);

            // Unit must be alive
            if (unit.unitCount == 0)
                return false;

            // Owner filter
            if (filterOwnerId.HasValue)
            {
                if (unit.countryID != filterOwnerId.Value)
                    return false;
            }

            // Not owner filter (for finding enemies)
            if (filterNotOwnerId.HasValue)
            {
                if (unit.countryID == filterNotOwnerId.Value)
                    return false;
            }

            // Province filter
            if (filterProvinceId.HasValue)
            {
                if (unit.provinceID != filterProvinceId.Value)
                    return false;
            }

            // Unit type filter
            if (filterUnitTypeId.HasValue)
            {
                if (unit.unitTypeID != filterUnitTypeId.Value)
                    return false;
            }

            // Min troop count filter
            if (filterMinTroopCount.HasValue)
            {
                if (unit.unitCount < filterMinTroopCount.Value)
                    return false;
            }

            // Max troop count filter
            if (filterMaxTroopCount.HasValue)
            {
                if (unit.unitCount > filterMaxTroopCount.Value)
                    return false;
            }

            return true;
        }

        #endregion

        public void Dispose()
        {
            // No resources to dispose currently
        }
    }
}
