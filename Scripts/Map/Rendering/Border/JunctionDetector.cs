using System.Collections.Generic;
using UnityEngine;
using Utils;

namespace Map.Rendering.Border
{
    /// <summary>
    /// Detects junction pixels (where 3+ provinces meet) and snaps polyline endpoints to them
    /// Extracted from BorderCurveExtractor for single responsibility
    ///
    /// Contains logic for:
    /// - Junction pixel detection (3+ provinces meeting at a point)
    /// - Endpoint snapping with spatial grid optimization (O(n) performance)
    /// - Degenerate loop prevention (same polyline endpoints)
    /// </summary>
    public class JunctionDetector
    {
        private readonly int mapWidth;
        private readonly int mapHeight;
        private readonly Color32[] provinceIDPixels;

        public JunctionDetector(int width, int height, Color32[] idPixels)
        {
            mapWidth = width;
            mapHeight = height;
            provinceIDPixels = idPixels;
        }

        /// <summary>
        /// Detect junction pixels where 3+ provinces meet
        /// These are critical connection points for border polylines
        /// </summary>
        public Dictionary<Vector2, HashSet<ushort>> DetectJunctionPixels()
        {
            var junctions = new Dictionary<Vector2, HashSet<ushort>>();

            for (int y = 0; y < mapHeight; y++)
            {
                for (int x = 0; x < mapWidth; x++)
                {
                    HashSet<ushort> neighboringProvinces = GetNeighboringProvinces(x, y);

                    // Junction = 3+ provinces meet at this pixel
                    if (neighboringProvinces.Count >= 3)
                    {
                        Vector2 pos = new Vector2(x, y);
                        junctions[pos] = neighboringProvinces;
                    }
                }
            }

            ArchonLogger.Log($"Detected {junctions.Count} junction pixels (3+ provinces meeting)", "map_initialization");
            return junctions;
        }

        /// <summary>
        /// Snap polyline endpoints to nearby junctions using spatial grid optimization
        /// Prevents small gaps where borders should meet at junction points
        /// </summary>
        /// <param name="allPolylines">All border polylines (will be modified in-place)</param>
        /// <param name="junctionPixels">Junction pixels from DetectJunctionPixels()</param>
        public void SnapPolylineEndpointsAtJunctions(
            Dictionary<(ushort, ushort), List<Vector2>> allPolylines,
            Dictionary<Vector2, HashSet<ushort>> junctionPixels)
        {
            const float SNAP_DISTANCE = 2.0f;
            const float GRID_CELL_SIZE = 4.0f; // Spatial grid for O(n) performance

            int gridWidth = Mathf.CeilToInt(mapWidth / GRID_CELL_SIZE);
            int gridHeight = Mathf.CeilToInt(mapHeight / GRID_CELL_SIZE);

            // Build spatial grid: map cell → list of (polyline key, isStart, endpoint position)
            var spatialGrid = new Dictionary<(int, int), List<(PolylineKey, bool, Vector2)>>();

            // Populate spatial grid with all polyline endpoints
            foreach (var kvp in allPolylines)
            {
                var key = new PolylineKey(kvp.Key.Item1, kvp.Key.Item2);
                var polyline = kvp.Value;

                if (polyline.Count == 0)
                    continue;

                Vector2 start = polyline[0];
                Vector2 end = polyline[polyline.Count - 1];

                // Add start endpoint to grid
                int startCellX = Mathf.FloorToInt(start.x / GRID_CELL_SIZE);
                int startCellY = Mathf.FloorToInt(start.y / GRID_CELL_SIZE);
                var startCell = (startCellX, startCellY);

                if (!spatialGrid.ContainsKey(startCell))
                    spatialGrid[startCell] = new List<(PolylineKey, bool, Vector2)>();

                spatialGrid[startCell].Add((key, true, start));

                // Add end endpoint to grid
                int endCellX = Mathf.FloorToInt(end.x / GRID_CELL_SIZE);
                int endCellY = Mathf.FloorToInt(end.y / GRID_CELL_SIZE);
                var endCell = (endCellX, endCellY);

                if (!spatialGrid.ContainsKey(endCell))
                    spatialGrid[endCell] = new List<(PolylineKey, bool, Vector2)>();

                spatialGrid[endCell].Add((key, false, end));
            }

            // For each junction, find nearby endpoints and snap them
            int snappedCount = 0;

            foreach (var junctionKvp in junctionPixels)
            {
                Vector2 junctionPos = junctionKvp.Key;

                // Find grid cell for this junction
                int jx = Mathf.FloorToInt(junctionPos.x / GRID_CELL_SIZE);
                int jy = Mathf.FloorToInt(junctionPos.y / GRID_CELL_SIZE);

                // Collect all nearby endpoints (5x5 grid search = 25 cells)
                List<(PolylineKey, bool, Vector2)> nearbyEndpoints = new List<(PolylineKey, bool, Vector2)>();

                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        var cell = (jx + dx, jy + dy);
                        if (spatialGrid.ContainsKey(cell))
                        {
                            nearbyEndpoints.AddRange(spatialGrid[cell]);
                        }
                    }
                }

                // Filter to endpoints within SNAP_DISTANCE
                List<(PolylineKey, bool)> toSnap = new List<(PolylineKey, bool)>();

                foreach (var (key, isStart, endpointPos) in nearbyEndpoints)
                {
                    float dist = Vector2.Distance(junctionPos, endpointPos);
                    if (dist <= SNAP_DISTANCE)
                    {
                        toSnap.Add((key, isStart));
                    }
                }

                // If multiple endpoints near this junction, snap them all to average position
                if (toSnap.Count >= 2)
                {
                    // Calculate average position of all endpoints in this cluster
                    Vector2 avgPos = Vector2.zero;
                    foreach (var (key, isStart) in toSnap)
                    {
                        var tupleKey = (key.provinceA, key.provinceB);
                        if (allPolylines.ContainsKey(tupleKey))
                        {
                            var polyline = allPolylines[tupleKey];
                            Vector2 endpoint = isStart ? polyline[0] : polyline[polyline.Count - 1];
                            avgPos += endpoint;
                        }
                    }
                    avgPos /= toSnap.Count;

                    // Snap all endpoints to average position
                    foreach (var (key, isStart) in toSnap)
                    {
                        var tupleKey = (key.provinceA, key.provinceB);
                        if (allPolylines.ContainsKey(tupleKey))
                        {
                            var polyline = allPolylines[tupleKey];

                            // CRITICAL: Don't snap start and end of SAME polyline (would create degenerate loop)
                            bool isSamePolyline = toSnap.Count == 2 &&
                                                  toSnap[0].Item1.Equals(toSnap[1].Item1) &&
                                                  toSnap[0].Item2 != toSnap[1].Item2;

                            if (!isSamePolyline)
                            {
                                if (isStart)
                                    polyline[0] = avgPos;
                                else
                                    polyline[polyline.Count - 1] = avgPos;

                                snappedCount++;
                            }
                        }
                    }
                }
            }

            ArchonLogger.Log($"Snapped {snappedCount} polyline endpoints to junctions (within {SNAP_DISTANCE}px)", "map_initialization");
        }

        /// <summary>
        /// Get all provinces neighboring this pixel (including self + 8 neighbors)
        /// </summary>
        private HashSet<ushort> GetNeighboringProvinces(int x, int y)
        {
            var provinces = new HashSet<ushort>();

            int[] dx = { 0, 1, -1, 0, 0, 1, -1, 1, -1 }; // Self + 8 neighbors
            int[] dy = { 0, 0, 0, 1, -1, 1, 1, -1, -1 };

            for (int i = 0; i < 9; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                // Bounds check
                if (nx < 0 || nx >= mapWidth || ny < 0 || ny >= mapHeight)
                    continue;

                int index = ny * mapWidth + nx;
                ushort provinceID = Province.ProvinceIDEncoder.UnpackProvinceID(provinceIDPixels[index]);

                if (provinceID != 0) // Skip ocean
                    provinces.Add(provinceID);
            }

            return provinces;
        }

        /// <summary>
        /// Helper struct for polyline keys in spatial grid
        /// Implements Equals and GetHashCode for dictionary lookups
        /// </summary>
        private struct PolylineKey
        {
            public ushort provinceA;
            public ushort provinceB;

            public PolylineKey(ushort a, ushort b)
            {
                provinceA = a;
                provinceB = b;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is PolylineKey))
                    return false;

                var other = (PolylineKey)obj;
                return provinceA == other.provinceA && provinceB == other.provinceB;
            }

            public override int GetHashCode()
            {
                return (provinceA << 16) | provinceB;
            }
        }
    }
}
