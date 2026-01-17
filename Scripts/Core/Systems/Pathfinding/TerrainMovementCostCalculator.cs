using Core.Data;
using Core.Registries;

namespace Core.Systems
{
    /// <summary>
    /// ENGINE LAYER: Movement cost calculator based on terrain types.
    ///
    /// Provides MECHANISM only - looks up terrain costs from registry.
    /// Does NOT decide what's passable (that's GAME layer policy).
    ///
    /// Usage:
    ///   // ENGINE: Just terrain costs, everything passable
    ///   var calculator = new TerrainMovementCostCalculator(provinceSystem, terrainRegistry);
    ///
    ///   // GAME: Wrap with game-specific traversability rules
    ///   var gameCalculator = new LandUnitCostCalculator(calculator); // blocks water
    ///   var navalCalculator = new NavalUnitCostCalculator(calculator); // blocks land
    ///
    /// GAME layer implements IMovementCostCalculator with:
    /// - Unit type checks (land/naval/amphibious)
    /// - Ownership penalties (enemy territory)
    /// - Supply/attrition considerations
    /// </summary>
    public class TerrainMovementCostCalculator : IMovementCostCalculator
    {
        private readonly ProvinceSystem provinceSystem;
        private readonly Registry<TerrainData> terrainRegistry;

        // Cached terrain data for fast lookup (indexed by terrain ID)
        private readonly FixedPoint64[] terrainCosts;
        private readonly bool[] terrainIsWater;

        // Default cost for unknown terrain
        private static readonly FixedPoint64 DEFAULT_COST = FixedPoint64.One;

        public TerrainMovementCostCalculator(ProvinceSystem provinceSystem, Registry<TerrainData> terrainRegistry)
        {
            this.provinceSystem = provinceSystem;
            this.terrainRegistry = terrainRegistry;

            // Pre-cache terrain data for O(1) lookup
            int maxTerrainId = GetMaxTerrainId();
            terrainCosts = new FixedPoint64[maxTerrainId + 1];
            terrainIsWater = new bool[maxTerrainId + 1];

            // Initialize with defaults
            for (int i = 0; i <= maxTerrainId; i++)
            {
                terrainCosts[i] = DEFAULT_COST;
                terrainIsWater[i] = false;
            }

            // Populate from registry
            foreach (var terrainData in terrainRegistry.GetAll())
            {
                int id = terrainData.TerrainId;
                if (id >= 0 && id < terrainCosts.Length)
                {
                    terrainCosts[id] = FixedPoint64.FromFloat(terrainData.MovementCost);
                    terrainIsWater[id] = terrainData.IsWater;
                }
            }

            ArchonLogger.Log($"TerrainMovementCostCalculator: Cached {terrainRegistry.Count} terrain costs", "core_simulation");
        }

        /// <summary>
        /// Get movement cost based on destination terrain.
        /// Cost is determined by the terrain of the province being entered.
        /// </summary>
        public FixedPoint64 GetMovementCost(ushort fromProvinceId, ushort toProvinceId, PathContext context)
        {
            ushort terrainId = provinceSystem.GetProvinceTerrain(toProvinceId);

            if (terrainId >= 0 && terrainId < terrainCosts.Length)
            {
                return terrainCosts[terrainId];
            }

            return DEFAULT_COST;
        }

        /// <summary>
        /// ENGINE: All provinces are traversable.
        /// GAME layer wraps this and adds policy (water/land restrictions, etc.)
        /// </summary>
        public bool CanTraverse(ushort provinceId, PathContext context)
        {
            // Engine doesn't restrict - GAME layer decides what's passable
            return true;
        }

        /// <summary>
        /// Get heuristic estimate (for A*).
        /// Returns zero (Dijkstra) - safe but not optimal.
        /// GAME layer can provide distance-based heuristics if coordinates available.
        /// </summary>
        public FixedPoint64 GetHeuristic(ushort fromProvinceId, ushort goalProvinceId)
        {
            return FixedPoint64.Zero;
        }

        /// <summary>
        /// Get the movement cost for a specific terrain type.
        /// Useful for GAME layer wrappers that need raw terrain costs.
        /// </summary>
        public FixedPoint64 GetTerrainCost(ushort terrainId)
        {
            if (terrainId >= 0 && terrainId < terrainCosts.Length)
            {
                return terrainCosts[terrainId];
            }
            return DEFAULT_COST;
        }

        /// <summary>
        /// Get terrain ID for a province.
        /// Useful for GAME layer to make policy decisions.
        /// </summary>
        public ushort GetProvinceTerrain(ushort provinceId)
        {
            return provinceSystem.GetProvinceTerrain(provinceId);
        }

        /// <summary>
        /// Check if a terrain type is water.
        /// Useful for GAME layer to implement land/naval unit restrictions.
        /// </summary>
        public bool IsTerrainWater(ushort terrainId)
        {
            if (terrainId >= 0 && terrainId < terrainIsWater.Length)
            {
                return terrainIsWater[terrainId];
            }
            return false;
        }

        /// <summary>
        /// Check if a province is water terrain.
        /// Convenience method combining GetProvinceTerrain and IsTerrainWater.
        /// </summary>
        public bool IsProvinceWater(ushort provinceId)
        {
            ushort terrainId = provinceSystem.GetProvinceTerrain(provinceId);
            return IsTerrainWater(terrainId);
        }

        private int GetMaxTerrainId()
        {
            int max = 0;
            foreach (var terrain in terrainRegistry.GetAll())
            {
                if (terrain.TerrainId > max)
                    max = terrain.TerrainId;
            }
            // Ensure at least room for basic terrains
            return System.Math.Max(max, 16);
        }
    }
}
