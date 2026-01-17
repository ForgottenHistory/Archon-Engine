using UnityEngine;
using Core.Queries;
using Map.MapModes;
using Map.Rendering;

namespace StarterKit.MapModes
{
    /// <summary>
    /// STARTERKIT: Farm Density Map Mode
    ///
    /// Demonstrates how to create a custom GAME-layer map mode that extends
    /// the ENGINE's GradientMapMode base class.
    ///
    /// Shows farms built per province as a heatmap:
    /// - Cream/off-white = no farms (owned land)
    /// - Yellow/Orange = some farms
    /// - Dark orange = many farms (max density)
    ///
    /// Architecture:
    /// - ENGINE provides mechanism (GradientMapMode, texture array)
    /// - GAME provides policy (farm data, gradient colors)
    /// - Mode switching is instant (just changes shader int)
    /// </summary>
    public class FarmDensityMapMode : GradientMapMode
    {
        // Reference to StarterKit's BuildingSystem (injected via constructor)
        private readonly BuildingSystem buildingSystem;
        private readonly ushort farmBuildingTypeId;
        private readonly int maxFarmsPerProvince;

        public FarmDensityMapMode(BuildingSystem buildingSystemRef, MapModeManager mapModeManagerRef)
        {
            buildingSystem = buildingSystemRef;

            // Cache farm building type ID for fast lookups
            var farmType = buildingSystem?.GetBuildingType("farm");
            farmBuildingTypeId = farmType?.ID ?? 0;
            maxFarmsPerProvince = farmType?.MaxPerProvince ?? 3;

            if (farmBuildingTypeId == 0)
            {
                ArchonLogger.LogWarning("FarmDensityMapMode: 'farm' building type not found", "starter_kit");
            }

            // Register with MapModeManager to get a texture array slot
            RegisterWithMapModeManager(mapModeManagerRef);

            ArchonLogger.Log($"FarmDensityMapMode: Initialized (farm ID={farmBuildingTypeId}, max={maxFarmsPerProvince})", "starter_kit");
        }

        #region IMapModeHandler Implementation

        public override MapMode Mode => MapMode.Economic;

        public override string Name => "Farm Density";

        // Custom map modes use ShaderModeID >= 2 (0=Political, 1=Terrain, 2+=Custom)
        public override int ShaderModeID => 2;

        public override UpdateFrequency GetUpdateFrequency() => UpdateFrequency.PerConquest;

        public override void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures)
        {
            OnMapModeActivated();
            LogActivation("Showing farm density heatmap");
        }

        public override void OnDeactivate(Material mapMaterial)
        {
            LogDeactivation();
        }

        public override string GetProvinceTooltip(ushort provinceId, ProvinceQueries provinceQueries, CountryQueries countryQueries)
        {
            if (buildingSystem == null || farmBuildingTypeId == 0)
                return $"Province {provinceId}";

            int farmCount = buildingSystem.GetBuildingCount(provinceId, farmBuildingTypeId);
            string category = GetValueCategory(farmCount);

            ushort ownerId = provinceQueries.GetOwner(provinceId);
            string ownerName = ownerId > 0 ? countryQueries.GetTag(ownerId) : "Unowned";

            return $"Province {provinceId} ({ownerName})\nFarms: {farmCount}/{maxFarmsPerProvince} ({category})";
        }

        #endregion

        #region GradientMapMode Implementation

        /// <summary>
        /// Define the color gradient: cream (no farms) -> yellow -> orange (max farms)
        /// </summary>
        protected override ColorGradient GetGradient()
        {
            return new ColorGradient(
                new Color32(240, 240, 220, 255),  // Cream/off-white (0 farms)
                new Color32(255, 255, 150, 255),  // Light yellow (1 farm)
                new Color32(255, 200, 50, 255),   // Golden yellow (2 farms)
                new Color32(255, 140, 0, 255),    // Orange (high density)
                new Color32(200, 80, 0, 255)      // Dark orange/brown (max density)
            );
        }

        /// <summary>
        /// Get the farm count for a province.
        /// </summary>
        protected override float GetValueForProvince(ushort provinceId, ProvinceQueries provinceQueries, object gameProvinceSystem)
        {
            if (buildingSystem == null || farmBuildingTypeId == 0)
                return 0f;

            // Only show owned provinces (skip unowned/ocean)
            ushort ownerId = provinceQueries.GetOwner(provinceId);
            if (ownerId == 0)
                return -1f; // Negative = skip (will use ocean color)

            int farmCount = buildingSystem.GetBuildingCount(provinceId, farmBuildingTypeId);

            // Return farm count (will be normalized by base class)
            // Use small positive value for 0 farms so it shows as "low" not skipped
            return farmCount > 0 ? farmCount : 0.001f;
        }

        /// <summary>
        /// Custom category names for farm density
        /// </summary>
        protected override string GetValueCategory(float value)
        {
            int farmCount = Mathf.RoundToInt(value);
            if (farmCount >= maxFarmsPerProvince) return "Fully Developed";
            if (farmCount >= 2) return "Well Developed";
            if (farmCount >= 1) return "Developing";
            return "Undeveloped";
        }

        #endregion
    }
}
