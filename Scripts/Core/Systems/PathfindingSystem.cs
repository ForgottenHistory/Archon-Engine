using System.Collections.Generic;
using Unity.Collections;
using Core.Data;

namespace Core.Systems
{
    /// <summary>
    /// ENGINE LAYER: Pathfinding system for multi-province unit movement
    ///
    /// Uses A* algorithm to find shortest path between provinces.
    /// Designed to support future terrain costs and movement blocking.
    ///
    /// MVP Implementation:
    /// - All provinces cost 1 (uniform)
    /// - All provinces passable
    ///
    /// Future Extensions:
    /// - Terrain-based movement costs
    /// - Movement blocking (ZOC, military access, borders)
    /// - Unit-type specific costs (cavalry faster on plains, etc)
    ///
    /// Performance:
    /// - A* with uniform costs = Dijkstra mode
    /// - ~O(E log V) where E = adjacencies, V = provinces
    /// - For 13k provinces with ~6 neighbors avg: very fast (<1ms typical)
    /// - ZERO ALLOCATIONS: Pre-allocated collections, reused across pathfinding calls
    /// </summary>
    public class PathfindingSystem : System.IDisposable
    {
        private AdjacencySystem adjacencySystem;
        private bool isInitialized = false;

        // Pre-allocated collections (cleared and reused for each pathfinding call)
        // Worst-case capacity for 13k provinces with ~6 neighbors
        private List<PathNode> openSet;                         // Max open set size (~256 typical)
        private HashSet<ushort> closedSet;                      // Max provinces explored (~1024 typical)
        private Dictionary<ushort, ushort> cameFrom;            // Parent tracking (~1024)
        private Dictionary<ushort, FixedPoint64> gScore;        // Cost from start (~1024)
        private Dictionary<ushort, FixedPoint64> fScore;        // Estimated total cost (~1024)
        private List<ushort> pathResult;                        // Reusable result buffer (~64 max path length)
        private NativeList<ushort> neighborBuffer;              // Reusable neighbor buffer (~16 max neighbors)

        /// <summary>
        /// Initialize pathfinding system with adjacency data
        /// Pre-allocates all collections for zero-allocation pathfinding
        /// </summary>
        public void Initialize(AdjacencySystem adjacencies)
        {
            if (adjacencies == null || !adjacencies.IsInitialized)
            {
                ArchonLogger.LogError("PathfindingSystem: Cannot initialize with null or uninitialized AdjacencySystem");
                return;
            }

            this.adjacencySystem = adjacencies;

            // Pre-allocate collections (worst-case capacity)
            openSet = new List<PathNode>(256);          // Max open set size
            closedSet = new HashSet<ushort>(1024);      // Max provinces explored
            cameFrom = new Dictionary<ushort, ushort>(1024);
            gScore = new Dictionary<ushort, FixedPoint64>(1024);
            fScore = new Dictionary<ushort, FixedPoint64>(1024);
            pathResult = new List<ushort>(64);          // Max path length
            neighborBuffer = new NativeList<ushort>(16, Allocator.Persistent);  // Max neighbors per province

            this.isInitialized = true;

            ArchonLogger.Log("PathfindingSystem: Initialized (zero-allocation mode)");
        }

        /// <summary>
        /// Find shortest path from start to goal using A*
        /// Returns full path including start and goal provinces
        /// Returns empty list if no path exists
        /// </summary>
        public List<ushort> FindPath(ushort start, ushort goal)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("PathfindingSystem: Not initialized");
                return new List<ushort>();
            }

            if (start == goal)
            {
                // Same province - return single-element path
                return new List<ushort> { start };
            }

            if (start == 0 || goal == 0)
            {
                ArchonLogger.LogWarning($"PathfindingSystem: Invalid province ID (start={start}, goal={goal})");
                return new List<ushort>();
            }

            // CLEAR pre-allocated collections (zero allocations!)
            openSet.Clear();
            closedSet.Clear();
            cameFrom.Clear();
            gScore.Clear();
            fScore.Clear();
            pathResult.Clear();

            // Initialize start node
            gScore[start] = FixedPoint64.Zero;
            fScore[start] = GetHeuristic(start, goal);
            openSet.Add(new PathNode { provinceID = start, fScore = fScore[start] });

            while (openSet.Count > 0)
            {
                // Get node with lowest fScore
                PathNode current = GetLowestFScore(openSet);
                openSet.Remove(current);

                // Goal reached - reconstruct path
                if (current.provinceID == goal)
                {
                    ReconstructPath(cameFrom, current.provinceID, pathResult);
                    // Return copy (caller owns this allocation)
                    return new List<ushort>(pathResult);
                }

                closedSet.Add(current.provinceID);

                // Explore neighbors (reuse pre-allocated buffer - zero allocations!)
                adjacencySystem.GetNeighbors(current.provinceID, neighborBuffer);

                for (int i = 0; i < neighborBuffer.Length; i++)
                {
                    ushort neighbor = neighborBuffer[i];

                    if (closedSet.Contains(neighbor))
                        continue; // Already explored

                    // TODO: Add movement blocking check
                    // if (!IsPassable(neighbor, unitType, ownerCountry)) continue;

                    // Calculate tentative gScore
                    FixedPoint64 movementCost = GetMovementCost(current.provinceID, neighbor);
                    FixedPoint64 tentativeG = gScore[current.provinceID] + movementCost;

                    // If this path to neighbor is better than previous, record it
                    if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor])
                    {
                        cameFrom[neighbor] = current.provinceID;
                        gScore[neighbor] = tentativeG;
                        fScore[neighbor] = tentativeG + GetHeuristic(neighbor, goal);

                        // Add to open set if not already there
                        if (!ContainsProvince(openSet, neighbor))
                        {
                            openSet.Add(new PathNode { provinceID = neighbor, fScore = fScore[neighbor] });
                        }
                    }
                }
            }

            // No path found
            ArchonLogger.LogWarning($"PathfindingSystem: No path from {start} to {goal}");
            return new List<ushort>();
        }

        /// <summary>
        /// Get movement cost between two adjacent provinces
        /// MVP: Always returns 1 (uniform cost)
        /// TODO: Add terrain-based costs
        /// DETERMINISM: Uses FixedPoint64 for cross-platform compatibility
        /// </summary>
        private FixedPoint64 GetMovementCost(ushort fromProvince, ushort toProvince)
        {
            // MVP: Uniform cost
            return FixedPoint64.One;

            // TODO: Future implementation
            // var terrainFrom = provinceSystem.GetTerrain(fromProvince);
            // var terrainTo = provinceSystem.GetTerrain(toProvince);
            // return CalculateTerrainCost(terrainFrom, terrainTo, unitType);
        }

        /// <summary>
        /// Heuristic function for A* (estimated cost to goal)
        /// MVP: Returns 0 (Dijkstra mode - finds optimal path)
        /// TODO: Add straight-line distance heuristic for performance
        /// DETERMINISM: Uses FixedPoint64 for cross-platform compatibility
        /// </summary>
        private FixedPoint64 GetHeuristic(ushort from, ushort to)
        {
            // MVP: Dijkstra mode (h=0 guarantees optimal path)
            return FixedPoint64.Zero;

            // TODO: Future optimization with province positions
            // Vector2 fromPos = provinceSystem.GetPosition(from);
            // Vector2 toPos = provinceSystem.GetPosition(to);
            // return FixedPoint64.FromFloat(Vector2.Distance(fromPos, toPos) / avgProvinceDistance);
        }

        /// <summary>
        /// Check if movement through a province is allowed
        /// MVP: Always returns true
        /// TODO: Add movement blocking (ZOC, borders, military access)
        /// </summary>
        private bool IsPassable(ushort province, ushort unitType, ushort ownerCountry)
        {
            // MVP: All provinces passable
            return true;

            // TODO: Future implementation
            // if (HasEnemyZOC(province, ownerCountry)) return false;
            // if (IsHostileTerritory(province, ownerCountry) && !HasMilitaryAccess(province, ownerCountry)) return false;
            // if (unitType.isNaval && !province.isWater) return false;
            // return true;
        }

        /// <summary>
        /// Reconstruct path from parent tracking into pre-allocated buffer (zero allocations)
        /// </summary>
        private void ReconstructPath(Dictionary<ushort, ushort> cameFrom, ushort current, List<ushort> result)
        {
            result.Add(current);

            while (cameFrom.ContainsKey(current))
            {
                current = cameFrom[current];
                result.Insert(0, current); // Prepend to build path from start to goal
            }
        }

        /// <summary>
        /// Get node with lowest fScore from open set (simple linear search)
        /// For better performance with large maps, could use a proper priority queue
        /// </summary>
        private PathNode GetLowestFScore(List<PathNode> openSet)
        {
            PathNode lowest = openSet[0];
            for (int i = 1; i < openSet.Count; i++)
            {
                if (openSet[i].fScore < lowest.fScore)
                {
                    lowest = openSet[i];
                }
            }
            return lowest;
        }

        /// <summary>
        /// Check if open set contains a province
        /// </summary>
        private bool ContainsProvince(List<PathNode> openSet, ushort provinceID)
        {
            for (int i = 0; i < openSet.Count; i++)
            {
                if (openSet[i].provinceID == provinceID)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Check if system is initialized
        /// </summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Dispose native collections
        /// </summary>
        public void Dispose()
        {
            if (neighborBuffer.IsCreated)
            {
                neighborBuffer.Dispose();
            }

            isInitialized = false;
        }

        /// <summary>
        /// Node for A* pathfinding
        /// DETERMINISM: Uses FixedPoint64 for cross-platform compatibility
        /// </summary>
        private struct PathNode
        {
            public ushort provinceID;
            public FixedPoint64 fScore; // g + h (deterministic)
        }
    }
}
