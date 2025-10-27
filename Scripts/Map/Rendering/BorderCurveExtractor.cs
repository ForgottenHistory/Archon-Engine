using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Core.Systems;
using ProvinceSystemType = Core.Systems.ProvinceSystem;

namespace Map.Rendering
{
    /// <summary>
    /// Extracts smooth border curves from bitmap province boundaries
    /// Uses AdjacencySystem to process only known neighbor pairs (efficient)
    /// Pre-computes all curves at map load for zero-cost runtime updates
    ///
    /// Architecture: Static Geometry + Dynamic Appearance Pattern
    /// - Geometry (curves) computed once and cached
    /// - Appearance (colors, thickness) updated at runtime via flags
    /// </summary>
    public class BorderCurveExtractor
    {
        private readonly MapTextureManager textureManager;
        private readonly AdjacencySystem adjacencySystem;
        private readonly ProvinceSystemType provinceSystem;
        private readonly ProvinceMapping provinceMapping;

        // Configuration
        private readonly int smoothingIterations = 5;  // More iterations = smoother curves
        private readonly bool logProgress = true;

        // Debug flags
        private bool firstCallLogged = false;
        private bool firstResultLogged = false;

        // Cache province ID texture once
        private Color32[] provinceIDPixels;
        private int mapWidth;
        private int mapHeight;

        public BorderCurveExtractor(MapTextureManager textures, AdjacencySystem adjacency, ProvinceSystemType provinces, ProvinceMapping mapping)
        {
            textureManager = textures;
            adjacencySystem = adjacency;
            provinceSystem = provinces;
            provinceMapping = mapping;
        }

        /// <summary>
        /// Extract and fit Bézier curves to all province borders
        /// Returns dictionary of border curves: (provinceA, provinceB) -> list of BezierSegments
        /// </summary>
        public Dictionary<(ushort, ushort), List<BezierSegment>> ExtractAllBorders()
        {
            var borderCurves = new Dictionary<(ushort, ushort), List<BezierSegment>>();

            if (logProgress)
            {
                ArchonLogger.Log("BorderCurveExtractor: Starting border extraction...", "map_initialization");
            }

            // Cache map dimensions and province ID texture (single GPU readback for ALL border extractions)
            mapWidth = textureManager.MapWidth;
            mapHeight = textureManager.MapHeight;

            RenderTexture.active = textureManager.ProvinceIDTexture;
            Texture2D tempTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.ARGB32, false);
            tempTexture.ReadPixels(new Rect(0, 0, mapWidth, mapHeight), 0, 0);
            tempTexture.Apply();
            RenderTexture.active = null;
            provinceIDPixels = tempTexture.GetPixels32();
            Object.Destroy(tempTexture);

            // Get all provinces from ProvinceSystem
            var allProvinceIds = provinceSystem.GetAllProvinceIds(Allocator.Temp);
            int processedBorders = 0;
            float startTime = Time.realtimeSinceStartup;

            ArchonLogger.Log($"BorderCurveExtractor: Processing {allProvinceIds.Length} provinces (texture cached: {provinceIDPixels.Length} pixels)", "map_initialization");

            // Process each province's neighbors
            for (int i = 0; i < allProvinceIds.Length; i++)
            {
                ushort provinceA = allProvinceIds[i];

                // Get neighbors
                var neighbors = adjacencySystem.GetNeighbors(provinceA, Allocator.Temp);

                // DEBUG: Log first 5 provinces to see neighbor counts
                if (i < 5)
                {
                    ArchonLogger.Log($"BorderCurveExtractor: Province {provinceA} has {neighbors.Length} neighbors", "map_initialization");
                }

                int skippedCount = 0;
                int processedCount = 0;

                foreach (ushort provinceB in neighbors)
                {
                    // Only process each border once (avoid duplicates)
                    if (provinceB <= provinceA)
                    {
                        skippedCount++;
                        continue;
                    }

                    processedCount++;

                    // DEBUG: Log first border extraction call
                    if (processedBorders == 0 && borderCurves.Count == 0)
                    {
                        ArchonLogger.Log($"BorderCurveExtractor: First border pair: {provinceA} <-> {provinceB}", "map_initialization");
                    }

                    // Extract shared border pixels (unordered)
                    List<Vector2> borderPixels = ExtractSharedBorderPixels(provinceA, provinceB);

                    if (borderPixels.Count > 0)
                    {
                        // Chain pixels into multiple ordered polylines (handles disconnected border segments)
                        List<List<Vector2>> allChains = ChainBorderPixelsMultiple(borderPixels);

                        // Process each chain separately to handle disconnected borders
                        List<BezierSegment> allCurveSegments = new List<BezierSegment>();
                        BorderType borderType = DetermineBorderType(provinceA, provinceB);

                        foreach (var orderedPath in allChains)
                        {
                            if (orderedPath.Count < 3)
                                continue; // Skip tiny chains (1-2 pixels are bitmap artifacts)

                            // Smooth the pixel chain before curve fitting
                            List<Vector2> smoothedPath;
                            if (orderedPath.Count >= 5)
                            {
                                smoothedPath = SmoothCurve(orderedPath, smoothingIterations);
                            }
                            else
                            {
                                // Use raw chained path for very short borders
                                smoothedPath = orderedPath;
                            }

                            // Fit Bézier curves to smoothed path (VECTOR CURVE RENDERING!)
                            List<BezierSegment> curveSegments = BezierCurveFitter.FitCurve(smoothedPath, borderType);
                            allCurveSegments.AddRange(curveSegments);
                        }

                        // Early exit if no valid curves generated
                        if (allCurveSegments.Count == 0)
                            continue;

                        // Set province IDs for each segment
                        for (int segIdx = 0; segIdx < allCurveSegments.Count; segIdx++)
                        {
                            BezierSegment seg = allCurveSegments[segIdx];
                            seg.provinceID1 = provinceA;
                            seg.provinceID2 = provinceB;
                            allCurveSegments[segIdx] = seg;
                        }

                        // DEBUG: Log first curve stats
                        if (processedBorders == 0)
                        {
                            ArchonLogger.Log($"BorderCurveExtractor: First curve - Raw pixels: {borderPixels.Count}, Chains: {allChains.Count}, Bézier segments: {allCurveSegments.Count}", "map_initialization");
                        }

                        // Cache the curve segments
                        borderCurves[(provinceA, provinceB)] = allCurveSegments;
                        processedBorders++;
                    }
                    else if (processedBorders == 0 && borderCurves.Count == 0)
                    {
                        // DEBUG: Why did first border extraction find 0 pixels?
                        ArchonLogger.Log($"BorderCurveExtractor: First border extraction {provinceA} <-> {provinceB} found 0 pixels!", "map_initialization");
                    }
                }

                // DEBUG: Log skip stats for first province
                if (i == 0)
                {
                    ArchonLogger.Log($"BorderCurveExtractor: First province - skipped {skippedCount} (lower ID), processed {processedCount} (higher ID)", "map_initialization");
                }

                neighbors.Dispose();

                // Progress logging every 1000 provinces
                if (logProgress && (i + 1) % 1000 == 0)
                {
                    ArchonLogger.Log($"BorderCurveExtractor: Processed {i + 1}/{allProvinceIds.Length} provinces, {processedBorders} borders extracted", "map_initialization");
                }
            }

            allProvinceIds.Dispose();

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;

            if (logProgress)
            {
                ArchonLogger.Log($"BorderCurveExtractor: Completed extraction! {processedBorders} borders in {elapsed:F0}ms", "map_initialization");
            }

            // POST-PROCESSING: Snap curve endpoints at junctions to ensure borders connect
            ArchonLogger.Log("BorderCurveExtractor: Snapping curve endpoints at junctions...", "map_initialization");
            float snapStart = Time.realtimeSinceStartup;
            SnapCurveEndpointsAtJunctions(borderCurves);
            float snapElapsed = (Time.realtimeSinceStartup - snapStart) * 1000f;
            ArchonLogger.Log($"BorderCurveExtractor: Junction snapping completed in {snapElapsed:F0}ms", "map_initialization");

            if (logProgress)
            {
                ArchonLogger.Log($"BorderCurveExtractor: Total time: {(Time.realtimeSinceStartup - startTime) * 1000f:F0}ms", "map_initialization");
            }

            return borderCurves;
        }

        /// <summary>
        /// Snap curve endpoints that are within snap distance to a common point
        /// Uses spatial grid for O(n) performance instead of O(n²)
        /// </summary>
        private void SnapCurveEndpointsAtJunctions(Dictionary<(ushort, ushort), List<BezierSegment>> borderCurves)
        {
            const float SNAP_DISTANCE = 1.5f;
            const int GRID_CELL_SIZE = 3; // Grid cell size (snap distance * 2)

            // Spatial grid for fast neighbor lookup
            var grid = new Dictionary<(int, int), List<(Vector2 point, (ushort, ushort) borderKey, int segmentIndex, bool isStart)>>();

            // Add all endpoints to spatial grid
            foreach (var kvp in borderCurves)
            {
                var borderKey = kvp.Key;
                var segments = kvp.Value;

                for (int i = 0; i < segments.Count; i++)
                {
                    AddToGrid(grid, segments[i].P0, borderKey, i, true);
                    AddToGrid(grid, segments[i].P3, borderKey, i, false);
                }
            }

            // Snap endpoints within each grid cell
            int snappedCount = 0;
            var processed = new HashSet<(Vector2, (ushort, ushort), int, bool)>();

            foreach (var cell in grid.Values)
            {
                if (cell.Count < 2) continue; // No junction here

                // Find clusters within this cell
                for (int i = 0; i < cell.Count; i++)
                {
                    if (processed.Contains(cell[i])) continue;

                    var cluster = new List<(Vector2, (ushort, ushort), int, bool)> { cell[i] };

                    for (int j = i + 1; j < cell.Count; j++)
                    {
                        if (processed.Contains(cell[j])) continue;

                        float dist = Vector2.Distance(cell[i].point, cell[j].point);
                        if (dist <= SNAP_DISTANCE)
                        {
                            cluster.Add(cell[j]);
                        }
                    }

                    if (cluster.Count >= 2)
                    {
                        // Calculate average position
                        Vector2 avgPos = Vector2.zero;
                        foreach (var item in cluster)
                        {
                            avgPos += item.Item1; // Item1 is the point
                        }
                        avgPos /= cluster.Count;

                        // Snap all endpoints
                        foreach (var (point, borderKey, segmentIndex, isStart) in cluster)
                        {
                            var segments = borderCurves[borderKey];
                            var seg = segments[segmentIndex];

                            if (isStart)
                                seg.P0 = avgPos;
                            else
                                seg.P3 = avgPos;

                            segments[segmentIndex] = seg;
                            processed.Add((point, borderKey, segmentIndex, isStart));
                        }

                        snappedCount += cluster.Count;
                    }
                }
            }

            ArchonLogger.Log($"BorderCurveExtractor: Snapped {snappedCount} curve endpoints at junctions", "map_initialization");

            // Helper: Add endpoint to spatial grid
            void AddToGrid(Dictionary<(int, int), List<(Vector2, (ushort, ushort), int, bool)>> g, Vector2 point, (ushort, ushort) key, int idx, bool start)
            {
                int cellX = (int)(point.x / GRID_CELL_SIZE);
                int cellY = (int)(point.y / GRID_CELL_SIZE);
                var cellKey = (cellX, cellY);

                if (!g.ContainsKey(cellKey))
                    g[cellKey] = new List<(Vector2, (ushort, ushort), int, bool)>();

                g[cellKey].Add((point, key, idx, start));
            }
        }

        /// <summary>
        /// Extract pixels where two provinces share a border
        /// Uses province pixel lists (EFFICIENT - no texture scanning per border!)
        /// </summary>
        private List<Vector2> ExtractSharedBorderPixels(ushort provinceA, ushort provinceB)
        {
            var borderPixels = new List<Vector2>();

            // Get province pixel lists
            var pixelsA = provinceMapping.GetProvincePixels(provinceA);
            var pixelsB = provinceMapping.GetProvincePixels(provinceB);

            if (pixelsA == null || pixelsB == null)
            {
                return borderPixels; // Province not found
            }

            // DEBUG: Log first border pair details
            if (!firstCallLogged)
            {
                firstCallLogged = true;
                ArchonLogger.Log($"BorderCurveExtractor: First border {provinceA}<->{provinceB}: A has {pixelsA.Count} pixels, B has {pixelsB.Count} pixels", "map_initialization");
            }

            // Check pixels of Province A for neighbors in Province B
            foreach (var pixel in pixelsA)
            {
                int x = pixel.x;
                int y = pixel.y;

                // Check if this pixel borders Province B
                if (HasNeighborProvince(x, y, provinceB, provinceIDPixels, mapWidth, mapHeight))
                {
                    borderPixels.Add(new Vector2(x, y));
                }
            }

            // DEBUG: Log results for first pair
            if (!firstResultLogged)
            {
                firstResultLogged = true;
                ArchonLogger.Log($"BorderCurveExtractor: First border {provinceA}<->{provinceB}: Found {borderPixels.Count} border pixels", "map_initialization");
            }

            return borderPixels;
        }

        /// <summary>
        /// Chain scattered border pixels into multiple ordered polylines (handles disconnected segments)
        /// Returns a list of chains, where each chain is a list of connected pixels
        /// IMPORTANT: Only connects adjacent pixels (8-connectivity), creates new chain at gaps
        /// </summary>
        private List<List<Vector2>> ChainBorderPixelsMultiple(List<Vector2> scatteredPixels)
        {
            List<List<Vector2>> allChains = new List<List<Vector2>>();

            if (scatteredPixels.Count == 0)
                return allChains;

            HashSet<Vector2> remaining = new HashSet<Vector2>(scatteredPixels);

            // Keep creating chains until all pixels are used
            while (remaining.Count > 0)
            {
                List<Vector2> currentChain = ChainBorderPixelsSingle(remaining);
                if (currentChain.Count > 0)
                {
                    allChains.Add(currentChain);
                }
                else
                {
                    // Safety: if ChainBorderPixelsSingle returns empty, break to prevent infinite loop
                    break;
                }
            }

            return allChains;
        }

        /// <summary>
        /// Chain scattered border pixels into an ordered polyline (single chain)
        /// Uses greedy nearest-neighbor with strict adjacency constraint
        /// Modifies the 'remaining' set by removing chained pixels
        /// IMPORTANT: Only connects adjacent pixels (8-connectivity), stops at gaps
        /// </summary>
        private List<Vector2> ChainBorderPixelsSingle(HashSet<Vector2> remaining)
        {
            if (remaining.Count == 0)
                return new List<Vector2>();

            List<Vector2> orderedPath = new List<Vector2>();

            // Start with first remaining pixel
            Vector2 current = remaining.First();
            orderedPath.Add(current);
            remaining.Remove(current);

            // Greedy nearest-neighbor chaining with STRICT adjacency
            while (remaining.Count > 0)
            {
                Vector2 nearest = Vector2.zero;
                float minDistSq = float.MaxValue;
                bool foundAdjacent = false;

                // Find nearest adjacent pixel (MUST be 8-neighbor, max distance sqrt(2))
                foreach (var candidate in remaining)
                {
                    float dx = candidate.x - current.x;
                    float dy = candidate.y - current.y;
                    float distSq = dx * dx + dy * dy;

                    // ONLY accept adjacent pixels (8-connectivity: max distance sqrt(2) ≈ 1.414)
                    if (distSq <= 2.0f) // sqrt(2)^2 = 2.0
                    {
                        if (distSq < minDistSq)
                        {
                            nearest = candidate;
                            minDistSq = distSq;
                            foundAdjacent = true;
                        }
                    }
                }

                // If no adjacent pixel found, stop chaining (border has gap or branch)
                // This prevents creating artificial straight-line shortcuts across provinces
                if (!foundAdjacent)
                {
                    break; // Stop here, don't create long-distance connections
                }

                // Add to path
                orderedPath.Add(nearest);
                remaining.Remove(nearest);
                current = nearest;
            }

            return orderedPath;
        }

        /// <summary>
        /// Check if pixel at (x,y) has a neighbor with target province ID
        /// Uses 8-connectivity (includes diagonals) to match chaining algorithm
        /// </summary>
        private bool HasNeighborProvince(int x, int y, ushort targetProvince, Color32[] pixels, int mapWidth, int mapHeight)
        {
            // Check 8-neighbors (includes diagonals for complete border detection)
            int[] dx = { 1, -1, 0, 0, 1, -1, 1, -1 };
            int[] dy = { 0, 0, 1, -1, 1, 1, -1, -1 };

            for (int i = 0; i < 8; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                // Bounds check
                if (nx < 0 || nx >= mapWidth || ny < 0 || ny >= mapHeight)
                    continue;

                int neighborIndex = ny * mapWidth + nx;
                ushort neighborProvince = Province.ProvinceIDEncoder.UnpackProvinceID(pixels[neighborIndex]);
                if (neighborProvince == targetProvince)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Get all unique province IDs neighboring this pixel (including self)
        /// Used for junction detection
        /// </summary>
        private HashSet<ushort> GetNeighboringProvinces(int x, int y, Color32[] pixels, int mapWidth, int mapHeight)
        {
            var provinces = new HashSet<ushort>();

            // Add self
            int selfIndex = y * mapWidth + x;
            provinces.Add(Province.ProvinceIDEncoder.UnpackProvinceID(pixels[selfIndex]));

            // Check 8-neighbors
            int[] dx = { 1, -1, 0, 0, 1, -1, 1, -1 };
            int[] dy = { 0, 0, 1, -1, 1, 1, -1, -1 };

            for (int i = 0; i < 8; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                // Bounds check
                if (nx < 0 || nx >= mapWidth || ny < 0 || ny >= mapHeight)
                    continue;

                int neighborIndex = ny * mapWidth + nx;
                provinces.Add(Province.ProvinceIDEncoder.UnpackProvinceID(pixels[neighborIndex]));
            }

            return provinces;
        }

        /// <summary>
        /// Smooth a polyline using Chaikin's corner-cutting algorithm
        /// Iteratively rounds corners by replacing each segment with two shorter segments
        /// IMPORTANT: Endpoints are NEVER smoothed to preserve junction connections
        /// </summary>
        private List<Vector2> SmoothCurve(List<Vector2> points, int iterations)
        {
            if (points.Count < 3)
                return points; // Can't smooth lines with less than 3 points

            // Store original endpoints - these MUST NOT change to preserve junctions
            Vector2 originalFirst = points[0];
            Vector2 originalLast = points[points.Count - 1];

            List<Vector2> smoothed = new List<Vector2>(points);

            for (int iter = 0; iter < iterations; iter++)
            {
                List<Vector2> newPoints = new List<Vector2>(smoothed.Count * 2);

                // Keep EXACT first point (junction preservation)
                newPoints.Add(originalFirst);

                // Apply Chaikin smoothing to each segment EXCEPT endpoints
                for (int i = 0; i < smoothed.Count - 1; i++)
                {
                    Vector2 p0 = smoothed[i];
                    Vector2 p1 = smoothed[i + 1];

                    // Create two new points at 1/4 and 3/4 along the segment
                    Vector2 q = Vector2.Lerp(p0, p1, 0.25f);
                    Vector2 r = Vector2.Lerp(p0, p1, 0.75f);

                    newPoints.Add(q);
                    newPoints.Add(r);
                }

                // Keep EXACT last point (junction preservation)
                newPoints.Add(originalLast);

                smoothed = newPoints;
            }

            return smoothed;
        }

        /// <summary>
        /// Determine if border is between provinces of same country (province border) or different countries (country border)
        /// </summary>
        private BorderType DetermineBorderType(ushort provinceA, ushort provinceB)
        {
            // Get owners from ProvinceSystem
            ushort ownerA = provinceSystem.GetProvinceOwner(provinceA);
            ushort ownerB = provinceSystem.GetProvinceOwner(provinceB);

            // Same owner = province border, different owner = country border
            return (ownerA == ownerB) ? BorderType.Province : BorderType.Country;
        }
    }
}
