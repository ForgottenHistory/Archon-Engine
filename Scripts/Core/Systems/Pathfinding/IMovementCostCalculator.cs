using Core.Data;

namespace Core.Systems
{
    /// <summary>
    /// ENGINE: Interface for calculating movement costs between provinces.
    ///
    /// Allows GAME layer to provide custom cost calculations based on:
    /// - Terrain type (mountains cost more than plains)
    /// - Province ownership (enemy territory costs more)
    /// - Unit type (cavalry faster on plains, slower in forests)
    /// - Weather, supply, fortifications, etc.
    ///
    /// Default implementation: UniformCostCalculator (all costs = 1)
    /// </summary>
    public interface IMovementCostCalculator
    {
        /// <summary>
        /// Get movement cost from one province to an adjacent province.
        /// Higher cost = less desirable path.
        /// Return FixedPoint64.MaxValue to indicate impassable.
        /// </summary>
        /// <param name="fromProvinceId">Source province</param>
        /// <param name="toProvinceId">Destination province (adjacent to source)</param>
        /// <param name="context">Optional context (unit owner, unit type, etc.)</param>
        FixedPoint64 GetMovementCost(ushort fromProvinceId, ushort toProvinceId, PathContext context);

        /// <summary>
        /// Check if a province can be traversed at all.
        /// Called before GetMovementCost - return false to skip entirely.
        /// </summary>
        bool CanTraverse(ushort provinceId, PathContext context);

        /// <summary>
        /// Get heuristic estimate from province to goal (for A*).
        /// Must be admissible (never overestimate actual cost).
        /// Default: return FixedPoint64.One (safe but slow).
        /// Better: return distance-based estimate if coordinates available.
        /// </summary>
        FixedPoint64 GetHeuristic(ushort fromProvinceId, ushort goalProvinceId);
    }

    /// <summary>
    /// Context passed to cost calculator for unit-specific pathfinding.
    /// </summary>
    public struct PathContext
    {
        /// <summary>Country that owns the unit</summary>
        public ushort UnitOwnerCountryId;

        /// <summary>Type of unit (for unit-specific costs)</summary>
        public ushort UnitTypeId;

        /// <summary>Optional flags for special pathfinding modes</summary>
        public PathContextFlags Flags;

        public static PathContext Default => new PathContext();

        public static PathContext Create(ushort ownerCountryId, ushort unitTypeId = 0)
        {
            return new PathContext
            {
                UnitOwnerCountryId = ownerCountryId,
                UnitTypeId = unitTypeId,
                Flags = PathContextFlags.None
            };
        }
    }

    /// <summary>
    /// Flags for special pathfinding behavior.
    /// </summary>
    [System.Flags]
    public enum PathContextFlags
    {
        None = 0,

        /// <summary>Ignore enemy territory penalties</summary>
        IgnoreEnemyTerritory = 1 << 0,

        /// <summary>Allow paths through impassable terrain (for debugging)</summary>
        IgnoreImpassable = 1 << 1,

        /// <summary>Prefer roads/infrastructure</summary>
        PreferRoads = 1 << 2,

        /// <summary>Avoid combat (higher cost for provinces with enemy units)</summary>
        AvoidCombat = 1 << 3
    }

    /// <summary>
    /// Default cost calculator - all provinces cost 1.
    /// Use when no terrain or ownership modifiers needed.
    /// </summary>
    public class UniformCostCalculator : IMovementCostCalculator
    {
        public static readonly UniformCostCalculator Instance = new UniformCostCalculator();

        public FixedPoint64 GetMovementCost(ushort fromProvinceId, ushort toProvinceId, PathContext context)
        {
            return FixedPoint64.One;
        }

        public bool CanTraverse(ushort provinceId, PathContext context)
        {
            return true;
        }

        public FixedPoint64 GetHeuristic(ushort fromProvinceId, ushort goalProvinceId)
        {
            // No heuristic - Dijkstra's algorithm (safe but slower)
            return FixedPoint64.Zero;
        }
    }
}
