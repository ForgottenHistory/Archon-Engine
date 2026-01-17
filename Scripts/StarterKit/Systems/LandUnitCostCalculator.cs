using Core.Data;
using Core.Systems;

namespace StarterKit
{
    /// <summary>
    /// STARTERKIT: Movement cost calculator for land units.
    ///
    /// Wraps TerrainMovementCostCalculator and adds GAME policy:
    /// - Land units cannot traverse water provinces
    /// - Uses terrain movement costs from terrain.json5
    ///
    /// For naval units, create NavalUnitCostCalculator that blocks land instead.
    /// </summary>
    public class LandUnitCostCalculator : IMovementCostCalculator
    {
        private readonly TerrainMovementCostCalculator terrainCalculator;

        public LandUnitCostCalculator(TerrainMovementCostCalculator terrainCalculator)
        {
            this.terrainCalculator = terrainCalculator;
        }

        /// <summary>
        /// Get movement cost based on destination terrain.
        /// Delegates to terrain calculator.
        /// </summary>
        public FixedPoint64 GetMovementCost(ushort fromProvinceId, ushort toProvinceId, PathContext context)
        {
            return terrainCalculator.GetMovementCost(fromProvinceId, toProvinceId, context);
        }

        /// <summary>
        /// Land units cannot traverse water provinces.
        /// </summary>
        public bool CanTraverse(ushort provinceId, PathContext context)
        {
            // Block water provinces for land units
            return !terrainCalculator.IsProvinceWater(provinceId);
        }

        /// <summary>
        /// Heuristic for A* - delegate to terrain calculator.
        /// </summary>
        public FixedPoint64 GetHeuristic(ushort fromProvinceId, ushort goalProvinceId)
        {
            return terrainCalculator.GetHeuristic(fromProvinceId, goalProvinceId);
        }
    }
}
