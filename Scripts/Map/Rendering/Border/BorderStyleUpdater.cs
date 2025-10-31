using UnityEngine;
using Core.Systems;
using ProvinceSystemType = Core.Systems.ProvinceSystem;

namespace Map.Rendering.Border
{
    /// <summary>
    /// Updates border styles based on province ownership changes
    /// Extracted from BorderComputeDispatcher for single responsibility
    ///
    /// Responsibilities:
    /// - Classify borders as country or province borders
    /// - Update border colors based on owner changes
    /// - Coordinate with BorderCurveCache for style updates
    /// </summary>
    public class BorderStyleUpdater
    {
        private readonly BorderCurveCache curveCache;
        private readonly ProvinceSystemType provinceSystem;
        private readonly CountrySystem countrySystem;

        public BorderStyleUpdater(BorderCurveCache cache, ProvinceSystemType provinces, CountrySystem countries)
        {
            curveCache = cache;
            provinceSystem = provinces;
            countrySystem = countries;
        }

        /// <summary>
        /// Update all border styles based on current province ownership
        /// Classifies borders as country or province borders and assigns colors
        /// </summary>
        public void UpdateAllBorderStyles()
        {
            if (curveCache == null || provinceSystem == null || countrySystem == null)
                return;

            // Get all unique provinces from province system
            var provinceCount = provinceSystem.ProvinceCount;
            int countryBorders = 0;
            int provinceBorders = 0;

            // Update border styles for each province
            for (ushort provinceID = 1; provinceID < provinceCount; provinceID++)
            {
                var state = provinceSystem.GetProvinceState(provinceID);
                ushort ownerID = state.ownerID;

                curveCache.UpdateProvinceBorderStyles(
                    provinceID,
                    ownerID,
                    (id) => provinceSystem.GetProvinceState(id).ownerID,  // getOwner delegate
                    (id) => (Color)countrySystem.GetCountryColor(id)      // getCountryColor delegate (Color32 -> Color)
                );
            }

            // Count border types for logging
            foreach (var (_, style) in curveCache.GetAllBorderStyles())
            {
                if (style.type == BorderType.Country)
                    countryBorders++;
                else if (style.type == BorderType.Province)
                    provinceBorders++;
            }

            ArchonLogger.Log($"BorderStyleUpdater: Classified borders - Country: {countryBorders}, Province: {provinceBorders}", "map_initialization");
        }

        /// <summary>
        /// Update border style for a single province (when ownership changes)
        /// </summary>
        public void UpdateProvinceBorderStyle(ushort provinceID)
        {
            if (curveCache == null || provinceSystem == null || countrySystem == null)
                return;

            var state = provinceSystem.GetProvinceState(provinceID);
            ushort ownerID = state.ownerID;

            curveCache.UpdateProvinceBorderStyles(
                provinceID,
                ownerID,
                (id) => provinceSystem.GetProvinceState(id).ownerID,
                (id) => (Color)countrySystem.GetCountryColor(id)
            );
        }
    }
}
