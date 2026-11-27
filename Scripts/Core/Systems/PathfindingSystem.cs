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

            // No calculator in the options: use the default one (SetDefaultCostCalculator)
            if (options.CostCalculator == null && defaultCostCalculator != UniformCostCalculator.Instance)
                options.CostCalculator = defaultCostCalculator;

            // The cache is keyed on (start, goal) only, so it is only valid for uniform costs: a cost
            // calculator may depend on the unit (owner, type) and on state that changes (ownership, wars)
            bool cacheable = options.UseCache && pathCache != null &&
                             options.ForbiddenProvinces == null && options.AvoidProvinces == null &&
                             (options.CostCalculator == null || options.CostCalculator == UniformCostCalculator.Instance);

            // Try cache first
            if (cacheable)
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
            if (result.Success && cacheable)
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
        ///
        /// Runs on flat managed arrays indexed by province id (the adjacency graph in compact form,
        /// g-scores, parents, a binary heap) rather than the Native collections the Burst job uses:
        /// outside Burst every NativeHashMap/NativeList access pays safety checks and a hash, and a
        /// search visits thousands of provinces. Per-search clearing is a stamp bump, not a wipe.
        /// </summary>
        private PathResult FindPathManaged(ushort start, ushort goal, PathOptions options)
        {
            EnsureManagedBuffers();
            managedPathResult.Clear();

            int size = mSeen.Length;
            if (start >= size || goal >= size)
                return PathResult.NotFound(0);

            if (++mStamp == int.MaxValue)
            {
                System.Array.Clear(mSeen, 0, mSeen.Length);
                System.Array.Clear(mClosed, 0, mClosed.Length);
                mStamp = 1;
            }
            int stamp = mStamp;
            mHeapCount = 0;
            int explored = 0;

            var costCalculator = options.GetEffectiveCostCalculator();
            var context = options.Context;
            var forbidden = options.ForbiddenProvinces;
            var avoid = options.AvoidProvinces;
            if (forbidden != null && forbidden.Count == 0) forbidden = null;
            if (avoid != null && avoid.Count == 0) avoid = null;
            int maxLength = options.MaxPathLength;
            var offsets = mOffsets;
            var neighbors = mNeighbors;

            // The start is not checked with CanTraverse: the unit is already there and may always leave
            // (e.g. it stands in a country whose access it just lost)

            mSeen[start] = stamp;
            mG[start] = 0;
            mParent[start] = 0; // province 0 is never valid: marks the start for ReconstructPathManaged
            HeapPush(costCalculator.GetHeuristic(start, goal).RawValue, start);

            int maxIterations = MAX_PATH_LENGTH * 100; // Safety limit
            int iterations = 0;

            while (mHeapCount > 0 && iterations++ < maxIterations)
            {
                ushort current = HeapPop();

                if (current == goal)
                {
                    ReconstructPathManaged(goal, managedPathResult);
                    return PathResult.Found(new List<ushort>(managedPathResult), FixedPoint64.FromRaw(mG[goal]), explored);
                }

                if (mClosed[current] == stamp)
                    continue; // stale heap entry
                mClosed[current] = stamp;
                explored++;

                long currentG = mG[current];

                // Check max path length
                if (maxLength > 0 && FixedPoint64.FromRaw(currentG).ToInt() >= maxLength)
                    continue;

                for (int i = offsets[current], end = offsets[current + 1]; i < end; i++)
                {
                    ushort neighbor = neighbors[i];

                    if (mClosed[neighbor] == stamp)
                        continue;

                    if (forbidden != null && forbidden.Contains(neighbor))
                        continue;

                    if (legacyValidator != null && !legacyValidator(neighbor, context.UnitOwnerCountryId, context.UnitTypeId))
                        continue;

                    if (!costCalculator.CanTraverse(neighbor, context))
                        continue;

                    FixedPoint64 moveCost = costCalculator.GetMovementCost(current, neighbor, context);
                    if (moveCost == FixedPoint64.MaxValue)
                        continue; // this step is impassable (IMovementCostCalculator's contract), though the province isn't
                    if (avoid != null && avoid.Contains(neighbor))
                        moveCost = moveCost * options.AvoidPenalty;

                    long tentativeG = currentG + moveCost.RawValue;

                    if (mSeen[neighbor] != stamp || tentativeG < mG[neighbor])
                    {
                        mSeen[neighbor] = stamp;
                        mG[neighbor] = tentativeG;
                        mParent[neighbor] = current;
                        HeapPush(tentativeG + costCalculator.GetHeuristic(neighbor, goal).RawValue, neighbor);
                    }
                }
            }

            return PathResult.NotFound(explored);
        }

        // Managed search state, indexed by province id (see FindPathManaged)
        private int[] mOffsets;     // compact graph from AdjacencySystem.GetCompactGraph
        private ushort[] mNeighbors;
        private int mGraphVersion = -1;
        private long[] mG;          // g-score (FixedPoint64 raw), valid where mSeen == mStamp
        private ushort[] mParent;   // valid where mSeen == mStamp; 0 at the start
        private int[] mSeen;
        private int[] mClosed;
        private int mStamp;
        // Binary min-heap on (f, province id); ties go to the lower id so results don't depend on push order
        private long[] mHeapF = new long[INITIAL_CAPACITY];
        private ushort[] mHeapId = new ushort[INITIAL_CAPACITY];
        private int mHeapCount;

        private void EnsureManagedBuffers()
        {
            if (mOffsets != null && mGraphVersion == adjacencySystem.Version) return;
            adjacencySystem.GetCompactGraph(out mOffsets, out mNeighbors);
            mGraphVersion = adjacencySystem.Version;
            int size = mOffsets.Length - 1;
            mG = new long[size];
            mParent = new ushort[size];
            mSeen = new int[size];
            mClosed = new int[size];
            mStamp = 0;
        }

        private void HeapPush(long f, ushort id)
        {
            if (mHeapCount == mHeapF.Length)
            {
                System.Array.Resize(ref mHeapF, mHeapCount * 2);
                System.Array.Resize(ref mHeapId, mHeapCount * 2);
            }
            int i = mHeapCount++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                long pf = mHeapF[parent];
                if (pf < f || (pf == f && mHeapId[parent] <= id)) break;
                mHeapF[i] = pf;
                mHeapId[i] = mHeapId[parent];
                i = parent;
            }
            mHeapF[i] = f;
            mHeapId[i] = id;
        }

        private ushort HeapPop()
        {
            ushort top = mHeapId[0];
            int n = --mHeapCount;
            if (n == 0) return top;
            long f = mHeapF[n];
            ushort id = mHeapId[n];
            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= n) break;
                int right = child + 1;
                if (right < n && (mHeapF[right] < mHeapF[child] || (mHeapF[right] == mHeapF[child] && mHeapId[right] < mHeapId[child])))
                    child = right;
                long cf = mHeapF[child];
                if (f < cf || (f == cf && id <= mHeapId[child])) break;
                mHeapF[i] = cf;
                mHeapId[i] = mHeapId[child];
                i = child;
            }
            mHeapF[i] = f;
            mHeapId[i] = id;
            return top;
        }

        private void ReconstructPathManaged(ushort goal, List<ushort> result)
        {
            result.Clear();
            for (ushort current = goal; current != 0; current = mParent[current])
                result.Add(current);
            result.Reverse();
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
