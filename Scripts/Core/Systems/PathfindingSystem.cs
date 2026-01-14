using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using System.Collections.Generic;
using Core.Data;
using Core.Collections;

namespace Core.Systems
{
    /// <summary>
    /// ENGINE LAYER: Pathfinding system for multi-province unit movement
    ///
    /// Uses A* algorithm to find shortest path between provinces.
    /// Burst-compiled for high performance with large maps.
    ///
    /// Features:
    /// - IMovementCostCalculator for terrain/ownership-based costs
    /// - PathOptions for forbidden zones and avoid preferences
    /// - LRU path caching for repeated requests
    /// - Burst path when using uniform costs (fast)
    /// - Managed path with custom costs (flexible)
    ///
    /// Performance:
    /// - Burst: O(E log V) with binary heap, ~0.1ms typical
    /// - Pre-allocated collections, zero gameplay allocations
    /// - LRU cache reduces redundant searches
    /// </summary>
    public delegate bool MovementValidator(ushort provinceID, ushort unitOwnerCountryID, ushort unitTypeID);

    public class PathfindingSystem : System.IDisposable
    {
        private AdjacencySystem adjacencySystem;
        private MovementValidator legacyValidator; // For backward compatibility
        private IMovementCostCalculator defaultCostCalculator;
        private PathCache pathCache;
        private bool isInitialized;

        // Native collections for Burst job (persistent, reused)
        private NativeMinHeap<PathfindingNode> openHeap;
        private NativeHashSet<ushort> closedSet;
        private NativeHashMap<ushort, ushort> cameFrom;
        private NativeHashMap<ushort, FixedPoint64> gScore;
        private NativeList<ushort> pathResult;
        private NativeList<ushort> neighborBuffer;

        // Managed fallback collections (for validator/cost calculator case)
        private List<ushort> managedPathResult;

        private const int INITIAL_CAPACITY = 1024;
        private const int MAX_PATH_LENGTH = 256;
        private const int DEFAULT_CACHE_SIZE = 256;

        // Statistics
        private int totalSearches;
        private int cacheHits;

        #region Initialization

        /// <summary>
        /// Initialize pathfinding system with adjacency data.
        /// </summary>
        /// <param name="adjacencies">Adjacency system for neighbor lookups</param>
        /// <param name="validator">Legacy validator (deprecated, use IMovementCostCalculator)</param>
        /// <param name="cacheSize">Size of path cache (0 to disable)</param>
        public void Initialize(AdjacencySystem adjacencies, MovementValidator validator = null, int cacheSize = DEFAULT_CACHE_SIZE)
        {
            if (adjacencies == null || !adjacencies.IsInitialized)
            {
                ArchonLogger.LogError("PathfindingSystem: Cannot initialize with null or uninitialized AdjacencySystem", "core_simulation");
                return;
            }

            this.adjacencySystem = adjacencies;
            this.legacyValidator = validator;
            this.defaultCostCalculator = UniformCostCalculator.Instance;

            // Allocate native collections (persistent, reused across calls)
            openHeap = new NativeMinHeap<PathfindingNode>(INITIAL_CAPACITY, Allocator.Persistent);
            closedSet = new NativeHashSet<ushort>(INITIAL_CAPACITY, Allocator.Persistent);
            cameFrom = new NativeHashMap<ushort, ushort>(INITIAL_CAPACITY, Allocator.Persistent);
            gScore = new NativeHashMap<ushort, FixedPoint64>(INITIAL_CAPACITY, Allocator.Persistent);
            pathResult = new NativeList<ushort>(MAX_PATH_LENGTH, Allocator.Persistent);
            neighborBuffer = new NativeList<ushort>(16, Allocator.Persistent);

            // Managed fallback
            managedPathResult = new List<ushort>(MAX_PATH_LENGTH);

            // Path cache
            if (cacheSize > 0)
            {
                pathCache = new PathCache(cacheSize);
            }

            this.isInitialized = true;
            this.totalSearches = 0;
            this.cacheHits = 0;

            string validatorInfo = validator != null ? " with legacy validator" : "";
            string cacheInfo = cacheSize > 0 ? $", cache size {cacheSize}" : ", no cache";
            ArchonLogger.Log($"PathfindingSystem: Initialized{validatorInfo}{cacheInfo}", "core_simulation");
        }

        /// <summary>
        /// Set the default cost calculator for all pathfinding requests.
        /// </summary>
        public void SetDefaultCostCalculator(IMovementCostCalculator calculator)
        {
            defaultCostCalculator = calculator ?? UniformCostCalculator.Instance;
        }

        #endregion

        #region Main API

        /// <summary>
        /// Find shortest path from start to goal using default options.
        /// </summary>
        public List<ushort> FindPath(ushort start, ushort goal, ushort unitOwnerCountryID = 0, ushort unitTypeID = 0)
        {
            var options = PathOptions.Default;
            options.Context = PathContext.Create(unitOwnerCountryID, unitTypeID);
            return FindPathWithOptions(start, goal, options).Path;
        }

        /// <summary>
        /// Find shortest path with full options.
        /// </summary>
        public PathResult FindPathWithOptions(ushort start, ushort goal, PathOptions options)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("PathfindingSystem: Not initialized", "core_simulation");
                return PathResult.Error(PathStatus.NotInitialized);
            }

            if (start == 0)
                return PathResult.Error(PathStatus.InvalidStart);

            if (goal == 0)
                return PathResult.Error(PathStatus.InvalidGoal);

            if (start == goal)
                return PathResult.TrivialPath(start);

            totalSearches++;

            // Try cache first (only for simple paths without forbidden/avoid)
            if (options.UseCache && pathCache != null &&
                options.ForbiddenProvinces == null && options.AvoidProvinces == null)
            {
                if (pathCache.TryGet(start, goal, out var cachedPath, out var cachedCost))
                {
                    cacheHits++;
                    return PathResult.Found(cachedPath, cachedCost, 0, cached: true);
                }
            }

            // Determine which pathfinding method to use
            PathResult result;

            bool hasCustomCosts = options.CostCalculator != null && options.CostCalculator != UniformCostCalculator.Instance;
            bool hasForbidden = options.ForbiddenProvinces != null && options.ForbiddenProvinces.Count > 0;
            bool hasAvoid = options.AvoidProvinces != null && options.AvoidProvinces.Count > 0;
            bool hasLegacyValidator = legacyValidator != null;

            if (hasCustomCosts || hasForbidden || hasAvoid || hasLegacyValidator)
            {
                // Use managed pathfinding for complex cases
                result = FindPathManaged(start, goal, options);
            }
            else
            {
                // Use Burst for simple uniform-cost paths
                result = FindPathBurst(start, goal);
            }

            // Cache successful results
            if (result.Success && options.UseCache && pathCache != null &&
                options.ForbiddenProvinces == null && options.AvoidProvinces == null)
            {
                pathCache.Add(start, goal, result.Path, result.TotalCost);
            }

            return result;
        }

        /// <summary>
        /// Find path avoiding specific provinces (convenience method).
        /// </summary>
        public PathResult FindPathAvoiding(ushort start, ushort goal, HashSet<ushort> avoidProvinces)
        {
            var options = PathOptions.Default;
            options.AvoidProvinces = avoidProvinces;
            options.UseCache = false; // Don't cache paths with avoid set
            return FindPathWithOptions(start, goal, options);
        }

        /// <summary>
        /// Find path with forbidden provinces (convenience method).
        /// </summary>
        public PathResult FindPathWithForbidden(ushort start, ushort goal, HashSet<ushort> forbiddenProvinces)
        {
            var options = PathOptions.Default;
            options.ForbiddenProvinces = forbiddenProvinces;
            options.UseCache = false;
            return FindPathWithOptions(start, goal, options);
        }

        /// <summary>
        /// Check if a path exists between two provinces.
        /// </summary>
        public bool PathExists(ushort start, ushort goal)
        {
            return FindPathWithOptions(start, goal, PathOptions.Default).Success;
        }

        /// <summary>
        /// Get the distance (number of provinces) between two provinces.
        /// Returns -1 if no path exists.
        /// </summary>
        public int GetDistance(ushort start, ushort goal)
        {
            var result = FindPathWithOptions(start, goal, PathOptions.Default);
            return result.Success ? result.Length - 1 : -1; // -1 because path includes start
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Clear the path cache.
        /// </summary>
        public void ClearCache()
        {
            pathCache?.Clear();
        }

        /// <summary>
        /// Invalidate cached paths that pass through a province.
        /// Call when a province becomes blocked/unblocked.
        /// </summary>
        public void InvalidateCacheForProvince(ushort provinceId)
        {
            pathCache?.InvalidateProvince(provinceId);
        }

        /// <summary>
        /// Get cache statistics.
        /// </summary>
        public string GetCacheStats()
        {
            if (pathCache == null)
                return "PathCache: disabled";

            return pathCache.GetStats();
        }

        #endregion

        #region Burst Pathfinding

        /// <summary>
        /// Burst-compiled pathfinding (uniform costs, no validation).
        /// </summary>
        private PathResult FindPathBurst(ushort start, ushort goal)
        {
            // Clear collections
            openHeap.Clear();
            closedSet.Clear();
            cameFrom.Clear();
            gScore.Clear();
            pathResult.Clear();

            // Get native adjacency data
            var adjacencyData = adjacencySystem.GetNativeData();

            // Schedule and complete job
            var job = new BurstPathfindingJob
            {
                start = start,
                goal = goal,
                adjacencyData = adjacencyData,
                openHeap = openHeap,
                closedSet = closedSet,
                cameFrom = cameFrom,
                gScore = gScore,
                pathResult = pathResult,
                neighborBuffer = neighborBuffer
            };

            job.Schedule().Complete();

            // Convert to managed result
            if (pathResult.Length == 0)
            {
                return PathResult.NotFound(closedSet.Count);
            }

            var result = new List<ushort>(pathResult.Length);
            for (int i = 0; i < pathResult.Length; i++)
            {
                result.Add(pathResult[i]);
            }

            // Cost = path length - 1 (uniform cost)
            var cost = FixedPoint64.FromInt(result.Count - 1);
            return PathResult.Found(result, cost, closedSet.Count);
        }

        #endregion

        #region Managed Pathfinding

        /// <summary>
        /// Managed pathfinding with cost calculator and options.
        /// </summary>
        private PathResult FindPathManaged(ushort start, ushort goal, PathOptions options)
        {
            // Clear collections
            openHeap.Clear();
            closedSet.Clear();
            cameFrom.Clear();
            gScore.Clear();
            managedPathResult.Clear();

            var costCalculator = options.GetEffectiveCostCalculator();
            var context = options.Context;

            // Check if start is traversable
            if (!costCalculator.CanTraverse(start, context))
            {
                return PathResult.Error(PathStatus.InvalidStart);
            }

            // Initialize start
            gScore[start] = FixedPoint64.Zero;
            var startH = costCalculator.GetHeuristic(start, goal);
            openHeap.Push(new PathfindingNode { provinceID = start, fScore = startH });

            int maxIterations = MAX_PATH_LENGTH * 100; // Safety limit
            int iterations = 0;

            while (!openHeap.IsEmpty && iterations++ < maxIterations)
            {
                var current = openHeap.Pop();

                if (current.provinceID == goal)
                {
                    var totalCost = gScore.TryGetValue(goal, out var cost) ? cost : FixedPoint64.Zero;
                    ReconstructPath(goal, managedPathResult);
                    return PathResult.Found(new List<ushort>(managedPathResult), totalCost, closedSet.Count);
                }

                if (closedSet.Contains(current.provinceID))
                    continue;

                closedSet.Add(current.provinceID);

                if (!gScore.TryGetValue(current.provinceID, out FixedPoint64 currentG))
                    continue;

                // Check max path length
                if (options.MaxPathLength > 0 && currentG.ToInt() >= options.MaxPathLength)
                    continue;

                // Get neighbors
                neighborBuffer.Clear();
                adjacencySystem.GetNeighbors(current.provinceID, neighborBuffer);

                for (int i = 0; i < neighborBuffer.Length; i++)
                {
                    ushort neighbor = neighborBuffer[i];

                    if (closedSet.Contains(neighbor))
                        continue;

                    // Check forbidden
                    if (options.ForbiddenProvinces != null && options.ForbiddenProvinces.Contains(neighbor))
                        continue;

                    // Check legacy validator
                    if (legacyValidator != null && !legacyValidator(neighbor, context.UnitOwnerCountryId, context.UnitTypeId))
                        continue;

                    // Check cost calculator traversability
                    if (!costCalculator.CanTraverse(neighbor, context))
                        continue;

                    // Calculate cost
                    FixedPoint64 moveCost = costCalculator.GetMovementCost(current.provinceID, neighbor, context);

                    // Apply avoid penalty
                    if (options.AvoidProvinces != null && options.AvoidProvinces.Contains(neighbor))
                    {
                        moveCost = moveCost * options.AvoidPenalty;
                    }

                    FixedPoint64 tentativeG = currentG + moveCost;

                    if (!gScore.TryGetValue(neighbor, out FixedPoint64 existingG) || tentativeG < existingG)
                    {
                        cameFrom[neighbor] = current.provinceID;
                        gScore[neighbor] = tentativeG;

                        var h = costCalculator.GetHeuristic(neighbor, goal);
                        var f = tentativeG + h;
                        openHeap.Push(new PathfindingNode { provinceID = neighbor, fScore = f });
                    }
                }
            }

            return PathResult.NotFound(closedSet.Count);
        }

        private void ReconstructPath(ushort goal, List<ushort> result)
        {
            result.Clear();
            ushort current = goal;

            while (true)
            {
                result.Insert(0, current);
                if (!cameFrom.TryGetValue(current, out ushort parent))
                    break;
                current = parent;
            }
        }

        #endregion

        #region Properties & Statistics

        public bool IsInitialized => isInitialized;

        /// <summary>Total pathfinding searches performed</summary>
        public int TotalSearches => totalSearches;

        /// <summary>Number of cache hits</summary>
        public int CacheHits => cacheHits;

        /// <summary>Cache hit rate (0-1)</summary>
        public float CacheHitRate => totalSearches > 0 ? (float)cacheHits / totalSearches : 0f;

        /// <summary>
        /// Get system statistics as string.
        /// </summary>
        public string GetStats()
        {
            return $"PathfindingSystem: {totalSearches} searches, {cacheHits} cache hits ({CacheHitRate:P1})\n{GetCacheStats()}";
        }

        /// <summary>
        /// Reset statistics.
        /// </summary>
        public void ResetStats()
        {
            totalSearches = 0;
            cacheHits = 0;
            pathCache?.ResetStats();
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (openHeap.IsCreated) openHeap.Dispose();
            if (closedSet.IsCreated) closedSet.Dispose();
            if (cameFrom.IsCreated) cameFrom.Dispose();
            if (gScore.IsCreated) gScore.Dispose();
            if (pathResult.IsCreated) pathResult.Dispose();
            if (neighborBuffer.IsCreated) neighborBuffer.Dispose();

            pathCache?.Clear();
            pathCache = null;

            isInitialized = false;
        }

        #endregion
    }

    /// <summary>
    /// Burst-compiled A* pathfinding job.
    /// Uses binary min-heap for O(log n) priority queue operations.
    /// </summary>
    [BurstCompile]
    public struct BurstPathfindingJob : IJob
    {
        public ushort start;
        public ushort goal;

        [ReadOnly] public NativeAdjacencyData adjacencyData;

        public NativeMinHeap<PathfindingNode> openHeap;
        public NativeHashSet<ushort> closedSet;
        public NativeHashMap<ushort, ushort> cameFrom;
        public NativeHashMap<ushort, FixedPoint64> gScore;
        public NativeList<ushort> pathResult;
        public NativeList<ushort> neighborBuffer;

        public void Execute()
        {
            // Initialize start
            gScore[start] = FixedPoint64.Zero;
            openHeap.Push(new PathfindingNode { provinceID = start, fScore = FixedPoint64.Zero });

            while (!openHeap.IsEmpty)
            {
                var current = openHeap.Pop();

                // Goal reached
                if (current.provinceID == goal)
                {
                    ReconstructPath(goal);
                    return;
                }

                // Skip if already processed (heap may have duplicates)
                if (closedSet.Contains(current.provinceID))
                    continue;

                closedSet.Add(current.provinceID);

                // Get current gScore
                if (!gScore.TryGetValue(current.provinceID, out FixedPoint64 currentG))
                    continue;

                // Explore neighbors
                var neighborEnumerator = adjacencyData.GetNeighbors(current.provinceID);
                while (neighborEnumerator.MoveNext())
                {
                    ushort neighbor = neighborEnumerator.Current;

                    if (closedSet.Contains(neighbor))
                        continue;

                    FixedPoint64 tentativeG = currentG + FixedPoint64.One;

                    bool isBetter = false;
                    if (!gScore.TryGetValue(neighbor, out FixedPoint64 existingG))
                    {
                        isBetter = true;
                    }
                    else if (tentativeG < existingG)
                    {
                        isBetter = true;
                    }

                    if (isBetter)
                    {
                        cameFrom[neighbor] = current.provinceID;
                        gScore[neighbor] = tentativeG;
                        openHeap.Push(new PathfindingNode { provinceID = neighbor, fScore = tentativeG });
                    }
                }
            }

            // No path found - pathResult remains empty
        }

        private void ReconstructPath(ushort goalProvince)
        {
            // Build path in reverse
            pathResult.Clear();
            ushort current = goalProvince;

            while (true)
            {
                pathResult.Add(current);
                if (!cameFrom.TryGetValue(current, out ushort parent))
                    break;
                current = parent;
            }

            // Reverse to get start-to-goal order
            int left = 0;
            int right = pathResult.Length - 1;
            while (left < right)
            {
                ushort temp = pathResult[left];
                pathResult[left] = pathResult[right];
                pathResult[right] = temp;
                left++;
                right--;
            }
        }
    }
}
