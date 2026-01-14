using System.Collections.Generic;
using Core.Data;
using Unity.Collections;

namespace Core.Systems
{
    /// <summary>
    /// ENGINE: Options for pathfinding requests.
    /// Allows customization of cost calculation, forbidden zones, and preferences.
    /// </summary>
    public struct PathOptions
    {
        /// <summary>
        /// Cost calculator for terrain/ownership-based costs.
        /// If null, uses UniformCostCalculator (all costs = 1).
        /// </summary>
        public IMovementCostCalculator CostCalculator;

        /// <summary>
        /// Context for unit-specific pathfinding (owner, type, flags).
        /// </summary>
        public PathContext Context;

        /// <summary>
        /// Provinces that cannot be traversed (hard block).
        /// Path will fail if no route exists without these provinces.
        /// </summary>
        public HashSet<ushort> ForbiddenProvinces;

        /// <summary>
        /// Provinces to avoid if possible (soft block).
        /// Will be used if no alternative exists, but with high penalty.
        /// </summary>
        public HashSet<ushort> AvoidProvinces;

        /// <summary>
        /// Penalty multiplier for avoided provinces.
        /// Default: 10 (avoided provinces cost 10x normal).
        /// </summary>
        public FixedPoint64 AvoidPenalty;

        /// <summary>
        /// Maximum path length (provinces). 0 = no limit.
        /// Use to prevent extremely long paths.
        /// </summary>
        public int MaxPathLength;

        /// <summary>
        /// Whether to use caching for this request.
        /// Disable for paths that change frequently.
        /// </summary>
        public bool UseCache;

        /// <summary>
        /// Default options (uniform cost, no restrictions).
        /// </summary>
        public static PathOptions Default => new PathOptions
        {
            CostCalculator = null,
            Context = PathContext.Default,
            ForbiddenProvinces = null,
            AvoidProvinces = null,
            AvoidPenalty = FixedPoint64.FromInt(10),
            MaxPathLength = 0,
            UseCache = true
        };

        /// <summary>
        /// Create options with a specific cost calculator.
        /// </summary>
        public static PathOptions WithCostCalculator(IMovementCostCalculator calculator)
        {
            var options = Default;
            options.CostCalculator = calculator;
            return options;
        }

        /// <summary>
        /// Create options with forbidden provinces.
        /// </summary>
        public static PathOptions WithForbidden(HashSet<ushort> forbidden)
        {
            var options = Default;
            options.ForbiddenProvinces = forbidden;
            return options;
        }

        /// <summary>
        /// Create options for a specific unit.
        /// </summary>
        public static PathOptions ForUnit(ushort ownerCountryId, ushort unitTypeId = 0)
        {
            var options = Default;
            options.Context = PathContext.Create(ownerCountryId, unitTypeId);
            return options;
        }

        /// <summary>
        /// Get the effective cost calculator (default if null).
        /// </summary>
        public IMovementCostCalculator GetEffectiveCostCalculator()
        {
            return CostCalculator ?? UniformCostCalculator.Instance;
        }
    }

    /// <summary>
    /// ENGINE: Result of a pathfinding request.
    /// Contains path and metadata about the search.
    /// </summary>
    public struct PathResult
    {
        /// <summary>Path from start to goal (empty if no path found)</summary>
        public List<ushort> Path;

        /// <summary>Total cost of the path</summary>
        public FixedPoint64 TotalCost;

        /// <summary>Number of provinces explored during search</summary>
        public int NodesExplored;

        /// <summary>Whether the path was retrieved from cache</summary>
        public bool WasCached;

        /// <summary>Status of the pathfinding request</summary>
        public PathStatus Status;

        /// <summary>True if a valid path was found</summary>
        public bool Success => Status == PathStatus.Found;

        /// <summary>Number of provinces in the path (0 if no path)</summary>
        public int Length => Path?.Count ?? 0;

        public static PathResult NotFound(int nodesExplored = 0)
        {
            return new PathResult
            {
                Path = new List<ushort>(),
                TotalCost = FixedPoint64.Zero,
                NodesExplored = nodesExplored,
                WasCached = false,
                Status = PathStatus.NotFound
            };
        }

        public static PathResult Found(List<ushort> path, FixedPoint64 cost, int nodesExplored, bool cached = false)
        {
            return new PathResult
            {
                Path = path,
                TotalCost = cost,
                NodesExplored = nodesExplored,
                WasCached = cached,
                Status = PathStatus.Found
            };
        }

        public static PathResult TrivialPath(ushort province)
        {
            return new PathResult
            {
                Path = new List<ushort> { province },
                TotalCost = FixedPoint64.Zero,
                NodesExplored = 0,
                WasCached = false,
                Status = PathStatus.Found
            };
        }

        public static PathResult Error(PathStatus status)
        {
            return new PathResult
            {
                Path = new List<ushort>(),
                TotalCost = FixedPoint64.Zero,
                NodesExplored = 0,
                WasCached = false,
                Status = status
            };
        }
    }

    /// <summary>
    /// Status codes for pathfinding results.
    /// </summary>
    public enum PathStatus
    {
        /// <summary>Path found successfully</summary>
        Found,

        /// <summary>No path exists between start and goal</summary>
        NotFound,

        /// <summary>Start province is invalid</summary>
        InvalidStart,

        /// <summary>Goal province is invalid</summary>
        InvalidGoal,

        /// <summary>Path exceeds maximum length</summary>
        TooLong,

        /// <summary>System not initialized</summary>
        NotInitialized,

        /// <summary>Search timeout exceeded</summary>
        Timeout
    }
}
