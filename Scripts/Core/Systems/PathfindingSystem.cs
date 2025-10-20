using System.Collections.Generic;
using Unity.Collections;

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
    /// </summary>
    public class PathfindingSystem
    {
        private AdjacencySystem adjacencySystem;
        private bool isInitialized = false;

        /// <summary>
        /// Initialize pathfinding system with adjacency data
        /// </summary>
        public void Initialize(AdjacencySystem adjacencies)
        {
            if (adjacencies == null || !adjacencies.IsInitialized)
            {
                ArchonLogger.LogError("PathfindingSystem: Cannot initialize with null or uninitialized AdjacencySystem");
                return;
            }

            this.adjacencySystem = adjacencies;
            this.isInitialized = true;

            ArchonLogger.Log("PathfindingSystem: Initialized");
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

            // A* pathfinding
            var openSet = new List<PathNode>(); // Provinces to explore (priority queue)
            var closedSet = new HashSet<ushort>(); // Already explored
            var cameFrom = new Dictionary<ushort, ushort>(); // Parent tracking for path reconstruction
            var gScore = new Dictionary<ushort, float>(); // Cost from start
            var fScore = new Dictionary<ushort, float>(); // Estimated total cost (g + h)

            // Initialize start node
            gScore[start] = 0f;
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
                    return ReconstructPath(cameFrom, current.provinceID);
                }

                closedSet.Add(current.provinceID);

                // Explore neighbors
                var neighbors = adjacencySystem.GetNeighbors(current.provinceID, Allocator.Temp);

                for (int i = 0; i < neighbors.Length; i++)
                {
                    ushort neighbor = neighbors[i];

                    if (closedSet.Contains(neighbor))
                        continue; // Already explored

                    // TODO: Add movement blocking check
                    // if (!IsPassable(neighbor, unitType, ownerCountry)) continue;

                    // Calculate tentative gScore
                    float movementCost = GetMovementCost(current.provinceID, neighbor);
                    float tentativeG = gScore[current.provinceID] + movementCost;

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

                neighbors.Dispose();
            }

            // No path found
            ArchonLogger.LogWarning($"PathfindingSystem: No path from {start} to {goal}");
            return new List<ushort>();
        }

        /// <summary>
        /// Get movement cost between two adjacent provinces
        /// MVP: Always returns 1 (uniform cost)
        /// TODO: Add terrain-based costs
        /// </summary>
        private float GetMovementCost(ushort fromProvince, ushort toProvince)
        {
            // MVP: Uniform cost
            return 1f;

            // TODO: Future implementation
            // var terrainFrom = provinceSystem.GetTerrain(fromProvince);
            // var terrainTo = provinceSystem.GetTerrain(toProvince);
            // return CalculateTerrainCost(terrainFrom, terrainTo, unitType);
        }

        /// <summary>
        /// Heuristic function for A* (estimated cost to goal)
        /// MVP: Returns 0 (Dijkstra mode - finds optimal path)
        /// TODO: Add straight-line distance heuristic for performance
        /// </summary>
        private float GetHeuristic(ushort from, ushort to)
        {
            // MVP: Dijkstra mode (h=0 guarantees optimal path)
            return 0f;

            // TODO: Future optimization with province positions
            // Vector2 fromPos = provinceSystem.GetPosition(from);
            // Vector2 toPos = provinceSystem.GetPosition(to);
            // return Vector2.Distance(fromPos, toPos) / avgProvinceDistance;
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
        /// Reconstruct path from parent tracking
        /// </summary>
        private List<ushort> ReconstructPath(Dictionary<ushort, ushort> cameFrom, ushort current)
        {
            var path = new List<ushort> { current };

            while (cameFrom.ContainsKey(current))
            {
                current = cameFrom[current];
                path.Insert(0, current); // Prepend to build path from start to goal
            }

            return path;
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
        /// Node for A* pathfinding
        /// </summary>
        private struct PathNode
        {
            public ushort provinceID;
            public float fScore; // g + h
        }
    }
}
