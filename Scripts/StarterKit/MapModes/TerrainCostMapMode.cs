using UnityEngine;
using Core;
using Core.Queries;
using Core.Registries;
using Core.Systems;
using Map.MapModes;
using Map.Rendering;
using TerrainData = Core.Registries.TerrainData;

namespace StarterKit.MapModes
{
    /// <summary>
    /// STARTERKIT: Terrain Movement Cost Map Mode
    ///
    /// Visualizes terrain-based movement costs from terrain.json5.
    /// Demonstrates ENGINE-GAME separation for pathfinding costs.
    ///
    /// Color scale:
    /// - Green = fast movement (cost 1.0, grasslands/plains)
    /// - Yellow = moderate (cost 1.1-1.3, desert/marsh/highlands)
    /// - Orange = slow (cost 1.4-1.5, hills/mountains/jungle)
    /// - Red = very slow (cost 1.6+, snow)
    /// - Blue = water (impassable for land units)
    ///
    /// Architecture:
    /// - ENGINE provides TerrainData with MovementCost
    /// - GAME visualizes costs for player understanding
    /// </summary>
    public class TerrainCostMapMode : GradientMapMode
    {
        private readonly GameState gameState;
        private readonly Registry<TerrainData> terrainRegistry;

        // Cached costs per terrain ID for fast lookup
        private readonly float[] terrainCosts;
        private readonly bool[] terrainIsWater;
        private readonly float minCost;
        private readonly float maxCost;

        public TerrainCostMapMode(GameState gameStateRef, MapModeManager mapModeManagerRef)
        {
            gameState = gameStateRef;
            terrainRegistry = gameStateRef?.Registries?.Terrains;

            if (terrainRegistry == null || terrainRegistry.Count == 0)
            {
                ArchonLogger.LogWarning("TerrainCostMapMode: No terrain registry available", "starter_kit");
                terrainCosts = new float[1];
                terrainIsWater = new bool[1];
                minCost = 1f;
                maxCost = 1f;
                return;
            }

            // Find max terrain ID
            int maxId = 0;
            foreach (var terrain in terrainRegistry.GetAll())
            {
                if (terrain.TerrainId > maxId)
                    maxId = terrain.TerrainId;
            }

            // Cache terrain costs and find min/max for normalization
            terrainCosts = new float[maxId + 1];
            terrainIsWater = new bool[maxId + 1];
            float foundMin = float.MaxValue;
            float foundMax = float.MinValue;

            foreach (var terrain in terrainRegistry.GetAll())
            {
                int id = terrain.TerrainId;
                terrainCosts[id] = terrain.MovementCost;
                terrainIsWater[id] = terrain.IsWater;

                // Only consider land terrain for min/max (water is separate)
                if (!terrain.IsWater)
                {
                    if (terrain.MovementCost < foundMin) foundMin = terrain.MovementCost;
                    if (terrain.MovementCost > foundMax) foundMax = terrain.MovementCost;
                }
            }

            minCost = foundMin != float.MaxValue ? foundMin : 1f;
            maxCost = foundMax != float.MinValue ? foundMax : 2f;

            // Register with MapModeManager
            RegisterWithMapModeManager(mapModeManagerRef);

            ArchonLogger.Log($"TerrainCostMapMode: Initialized with {terrainRegistry.Count} terrains (cost range: {minCost:F2} - {maxCost:F2})", "starter_kit");
        }

        #region IMapModeHandler Implementation

        public override MapMode Mode => MapMode.Terrain;

        public override string Name => "Movement Cost";

        // Use ShaderModeID 3 (after Farm Density which uses 2)
        public override int ShaderModeID => 3;

        public override UpdateFrequency GetUpdateFrequency() => UpdateFrequency.Never; // Terrain doesn't change

        public override void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures)
        {
            OnMapModeActivated();
            LogActivation("Showing terrain movement costs");
        }

        public override void OnDeactivate(Material mapMaterial)
        {
            LogDeactivation();
        }

        public override string GetProvinceTooltip(ushort provinceId, ProvinceQueries provinceQueries, CountryQueries countryQueries)
        {
            ushort terrainId = provinceQueries.GetTerrain(provinceId);

            // Get terrain name from registry
            string terrainName = "Unknown";
            float cost = 1f;
            bool isWater = false;

            if (terrainId < terrainCosts.Length)
            {
                cost = terrainCosts[terrainId];
                isWater = terrainIsWater[terrainId];
            }

            // Look up terrain name
            foreach (var terrain in terrainRegistry.GetAll())
            {
                if (terrain.TerrainId == terrainId)
                {
                    terrainName = terrain.Name;
                    break;
                }
            }

            if (isWater)
            {
                return $"Province {provinceId}\nTerrain: {terrainName}\nMovement: Impassable (water)";
            }

            string speedCategory = GetSpeedCategory(cost);
            return $"Province {provinceId}\nTerrain: {terrainName}\nMovement Cost: {cost:F2}x ({speedCategory})";
        }

        #endregion

        #region GradientMapMode Implementation

        // Water provinces get blue
        protected override Color32 OceanColor => new Color32(30, 80, 180, 255);

        /// <summary>
        /// Color gradient: Green (fast) -> Yellow -> Orange -> Red (slow)
        /// </summary>
        protected override ColorGradient GetGradient()
        {
            return new ColorGradient(
                new Color32(50, 180, 50, 255),    // Green (cost 1.0 - fastest)
                new Color32(150, 200, 50, 255),  // Yellow-green (cost ~1.1)
                new Color32(220, 200, 50, 255),  // Yellow (cost ~1.25)
                new Color32(220, 140, 40, 255),  // Orange (cost ~1.4)
                new Color32(180, 60, 40, 255)    // Red-orange (cost 1.6+ - slowest)
            );
        }

        /// <summary>
        /// Get normalized movement cost for a province (0-1 range).
        /// </summary>
        protected override float GetValueForProvince(ushort provinceId, ProvinceQueries provinceQueries, object gameProvinceSystem)
        {
            ushort terrainId = provinceQueries.GetTerrain(provinceId);

            // Check if water
            if (terrainId < terrainIsWater.Length && terrainIsWater[terrainId])
            {
                return -1f; // Negative = use ocean color
            }

            // Get cost and normalize to 0-1 range
            float cost = 1f;
            if (terrainId < terrainCosts.Length)
            {
                cost = terrainCosts[terrainId];
            }

            // Normalize: minCost -> 0, maxCost -> 1
            if (maxCost > minCost)
            {
                return (cost - minCost) / (maxCost - minCost);
            }

            return 0f;
        }

        /// <summary>
        /// Category names for movement speed
        /// </summary>
        protected override string GetValueCategory(float normalizedValue)
        {
            // Convert back to cost for categorization
            float cost = minCost + normalizedValue * (maxCost - minCost);
            return GetSpeedCategory(cost);
        }

        private string GetSpeedCategory(float cost)
        {
            if (cost <= 1.0f) return "Fast";
            if (cost <= 1.15f) return "Normal";
            if (cost <= 1.3f) return "Slow";
            if (cost <= 1.45f) return "Difficult";
            return "Very Difficult";
        }

        #endregion
    }
}
