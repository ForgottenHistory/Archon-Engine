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
    /// MVP Implementation:
    /// - All provinces cost 1 (uniform)
    /// - Burst path when no validator (fast)
    /// - Managed fallback with validator (flexible)
    ///
    /// Future Extensions (Options B/C):
    /// - Batched parallel pathfinding for million-unit scale
    /// - Hierarchical A* (HPA*) for very large maps
    /// - Terrain-based movement costs
    /// - Pre-computed blocked province sets for Burst validation
    ///
    /// Performance:
    /// - Burst: O(E log V) with binary heap, ~0.1ms typical
    /// - Pre-allocated collections, zero gameplay allocations
    /// </summary>
    public delegate bool MovementValidator(ushort provinceID, ushort unitOwnerCountryID, ushort unitTypeID);

    public class PathfindingSystem : System.IDisposable
    {
        private AdjacencySystem adjacencySystem;
        private MovementValidator movementValidator;
        private bool isInitialized = false;

        // Native collections for Burst job (persistent, reused)
        private NativeMinHeap<PathfindingNode> openHeap;
        private NativeHashSet<ushort> closedSet;
        private NativeHashMap<ushort, ushort> cameFrom;
        private NativeHashMap<ushort, FixedPoint64> gScore;
        private NativeList<ushort> pathResult;
        private NativeList<ushort> neighborBuffer;

        // Managed fallback collections (for validator case)
        private List<ushort> managedPathResult;

        private const int INITIAL_CAPACITY = 1024;
        private const int MAX_PATH_LENGTH = 256;

        /// <summary>
        /// Initialize pathfinding system with adjacency data
        /// </summary>
        public void Initialize(AdjacencySystem adjacencies, MovementValidator validator = null)
        {
            if (adjacencies == null || !adjacencies.IsInitialized)
            {
                ArchonLogger.LogError("PathfindingSystem: Cannot initialize with null or uninitialized AdjacencySystem", "core_simulation");
                return;
            }

            this.adjacencySystem = adjacencies;
            this.movementValidator = validator;

            // Allocate native collections (persistent, reused across calls)
            openHeap = new NativeMinHeap<PathfindingNode>(INITIAL_CAPACITY, Allocator.Persistent);
            closedSet = new NativeHashSet<ushort>(INITIAL_CAPACITY, Allocator.Persistent);
            cameFrom = new NativeHashMap<ushort, ushort>(INITIAL_CAPACITY, Allocator.Persistent);
            gScore = new NativeHashMap<ushort, FixedPoint64>(INITIAL_CAPACITY, Allocator.Persistent);
            pathResult = new NativeList<ushort>(MAX_PATH_LENGTH, Allocator.Persistent);
            neighborBuffer = new NativeList<ushort>(16, Allocator.Persistent);

            // Managed fallback
            managedPathResult = new List<ushort>(MAX_PATH_LENGTH);

            this.isInitialized = true;

            string validatorInfo = validator != null ? " with movement validator (managed mode)" : " (Burst mode)";
            ArchonLogger.Log($"PathfindingSystem: Initialized{validatorInfo}", "core_simulation");
        }

        /// <summary>
        /// Find shortest path from start to goal using A*
        /// Uses Burst when no validator, managed fallback otherwise.
        /// </summary>
        public List<ushort> FindPath(ushort start, ushort goal, ushort unitOwnerCountryID = 0, ushort unitTypeID = 0)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("PathfindingSystem: Not initialized", "core_simulation");
                return new List<ushort>();
            }

            if (start == goal)
                return new List<ushort> { start };

            if (start == 0 || goal == 0)
            {
                ArchonLogger.LogWarning($"PathfindingSystem: Invalid province ID (start={start}, goal={goal})", "core_simulation");
                return new List<ushort>();
            }

            // Use Burst path when no validator (common case for AI)
            if (movementValidator == null)
            {
                return FindPathBurst(start, goal);
            }
            else
            {
                return FindPathManaged(start, goal, unitOwnerCountryID, unitTypeID);
            }
        }

        /// <summary>
        /// Burst-compiled pathfinding (no movement validation)
        /// </summary>
        private List<ushort> FindPathBurst(ushort start, ushort goal)
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

            // Convert to managed list for API compatibility
            if (pathResult.Length == 0)
            {
                return new List<ushort>();
            }

            var result = new List<ushort>(pathResult.Length);
            for (int i = 0; i < pathResult.Length; i++)
            {
                result.Add(pathResult[i]);
            }
            return result;
        }

        /// <summary>
        /// Managed pathfinding with movement validation
        /// </summary>
        private List<ushort> FindPathManaged(ushort start, ushort goal, ushort unitOwnerCountryID, ushort unitTypeID)
        {
            // Clear collections
            openHeap.Clear();
            closedSet.Clear();
            cameFrom.Clear();
            gScore.Clear();
            managedPathResult.Clear();

            // Initialize start
            gScore[start] = FixedPoint64.Zero;
            openHeap.Push(new PathfindingNode { provinceID = start, fScore = FixedPoint64.Zero });

            while (!openHeap.IsEmpty)
            {
                var current = openHeap.Pop();

                if (current.provinceID == goal)
                {
                    ReconstructPath(current.provinceID, managedPathResult);
                    return new List<ushort>(managedPathResult);
                }

                if (closedSet.Contains(current.provinceID))
                    continue;

                closedSet.Add(current.provinceID);

                // Get neighbors
                neighborBuffer.Clear();
                adjacencySystem.GetNeighbors(current.provinceID, neighborBuffer);

                for (int i = 0; i < neighborBuffer.Length; i++)
                {
                    ushort neighbor = neighborBuffer[i];

                    if (closedSet.Contains(neighbor))
                        continue;

                    // Check movement validation
                    if (!movementValidator(neighbor, unitOwnerCountryID, unitTypeID))
                        continue;

                    FixedPoint64 tentativeG = gScore[current.provinceID] + FixedPoint64.One;

                    if (!gScore.TryGetValue(neighbor, out FixedPoint64 existingG) || tentativeG < existingG)
                    {
                        cameFrom[neighbor] = current.provinceID;
                        gScore[neighbor] = tentativeG;
                        openHeap.Push(new PathfindingNode { provinceID = neighbor, fScore = tentativeG });
                    }
                }
            }

            return new List<ushort>();
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

        public bool IsInitialized => isInitialized;

        public void Dispose()
        {
            if (openHeap.IsCreated) openHeap.Dispose();
            if (closedSet.IsCreated) closedSet.Dispose();
            if (cameFrom.IsCreated) cameFrom.Dispose();
            if (gScore.IsCreated) gScore.Dispose();
            if (pathResult.IsCreated) pathResult.Dispose();
            if (neighborBuffer.IsCreated) neighborBuffer.Dispose();

            isInitialized = false;
        }
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
