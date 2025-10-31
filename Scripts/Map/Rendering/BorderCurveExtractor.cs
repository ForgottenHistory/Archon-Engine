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
        private readonly int smoothingIterations = 7;  // More iterations = smoother curves (7 = good balance between smoothness and performance)
        private readonly bool logProgress = true;

        // Debug flags
        private bool firstCallLogged = false;
        private bool firstResultLogged = false;

        // Cache province ID texture once
        private Color32[] provinceIDPixels;
        private int mapWidth;
        private int mapHeight;

        // Junction detection: pixels where 3+ provinces meet
        private Dictionary<Vector2, HashSet<ushort>> junctionPixels;

        public BorderCurveExtractor(MapTextureManager textures, AdjacencySystem adjacency, ProvinceSystemType provinces, ProvinceMapping mapping)
        {
            textureManager = textures;
            adjacencySystem = adjacency;
            provinceSystem = provinces;
            provinceMapping = mapping;
        }

        /// <summary>
        /// Extract and smooth all province borders using Chaikin algorithm
        /// Returns dictionary of border polylines: (provinceA, provinceB) -> smooth polyline points
        /// </summary>
        public Dictionary<(ushort, ushort), List<Vector2>> ExtractAllBorders()
        {
            var borderPolylines = new Dictionary<(ushort, ushort), List<Vector2>>();

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

            // CRITICAL: Apply median filter to smooth province IDs (removes peninsula indents at source)
            // This eliminates U-turn artifacts by smoothing the province boundaries before extraction
            provinceIDPixels = ApplyMedianFilterToProvinceIDs(provinceIDPixels, mapWidth, mapHeight);

            if (logProgress)
            {
                ArchonLogger.Log("BorderCurveExtractor: Applied median filter to smooth province boundaries", "map_initialization");
            }

            // CRITICAL: Rebuild provinceMapping to match filtered data
            // The median filter changed which pixels belong to which provinces
            RebuildProvinceMappingFromFilteredPixels(provinceIDPixels, mapWidth, mapHeight);

            if (logProgress)
            {
                ArchonLogger.Log("BorderCurveExtractor: Rebuilt province pixel mapping from filtered data", "map_initialization");
            }

            // Get all provinces from ProvinceSystem
            var allProvinceIds = provinceSystem.GetAllProvinceIds(Allocator.Temp);
            int processedBorders = 0;
            float startTime = Time.realtimeSinceStartup;

            ArchonLogger.Log($"BorderCurveExtractor: Processing {allProvinceIds.Length} provinces (texture cached: {provinceIDPixels.Length} pixels)", "map_initialization");

            // STEP 1: Detect all junction pixels (where 3+ provinces meet)
            DetectJunctionPixels();

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
                    if (processedBorders == 0 && borderPolylines.Count == 0)
                    {
                        ArchonLogger.Log($"BorderCurveExtractor: First border pair: {provinceA} <-> {provinceB}", "map_initialization");
                    }

                    // Extract shared border pixels (unordered)
                    List<Vector2> borderPixels = ExtractSharedBorderPixels(provinceA, provinceB);

                    // Skip truly degenerate borders (1-2 pixels = compression artifacts)
                    // But allow small borders (3-9 pixels) - these are real borders that need to render
                    const int MIN_BORDER_PIXELS = 3; // Minimum pixels for a real border (lowered from 10)
                    if (borderPixels.Count > 0 && borderPixels.Count < MIN_BORDER_PIXELS)
                    {
                        // DEBUG: Log skipped degenerate borders
                        if (processedBorders < 10)
                        {
                            ArchonLogger.Log($"BorderCurveExtractor: Skipping degenerate border {provinceA}<->{provinceB} ({borderPixels.Count} pixels)", "map_initialization");
                        }
                        continue; // Skip this border - too small to be real
                    }

                    if (borderPixels.Count > 0)
                    {
                        // DEBUG: Check if this is a debug border (for detailed logging)
                        bool isDebugBorder = (provinceA == 1160 || provinceB == 1160 ||
                                             provinceA == 1765 || provinceB == 1765 ||
                                             provinceA == 4703 || provinceB == 4703);

                        // Median filter already smoothed the province IDs at source - no need for convex hull filtering
                        // Chain border pixels into polylines
                        List<List<Vector2>> allChains = ChainBorderPixelsMultiple(borderPixels);
                        if ((allChains.Count > 1 && processedBorders < 20) || isDebugBorder)
                        {
                            string chainSizes = string.Join(", ", allChains.Select(c => c.Count));
                            ArchonLogger.Log($"MULTI-CHAIN BORDER {provinceA}-{provinceB}: {allChains.Count} chains [{chainSizes}], {borderPixels.Count} total pixels", "map_initialization");
                        }

                        // CRITICAL: Two-tier approach for handling multiple chains
                        // 1. Discard tiny chains (≤3 pixels) - these are noise/artifacts
                        // 2. Merge remaining chains without U-turn detection - they're real border segments
                        List<List<Vector2>> significantChains = allChains.Where(chain => chain.Count > 3).ToList();

                        if (significantChains.Count < allChains.Count && (isDebugBorder || processedBorders < 10))
                        {
                            int discarded = allChains.Count - significantChains.Count;
                            ArchonLogger.Log($"FILTERED TINY CHAINS {provinceA}-{provinceB}: Discarded {discarded} chains (≤3 pixels)", "map_initialization");
                        }

                        // Merge significant chains with self-intersection detection
                        List<Vector2> mergedPath;
                        if (significantChains.Count == 0)
                        {
                            // All chains were tiny - fallback to longest original chain
                            mergedPath = allChains.OrderByDescending(c => c.Count).First();
                        }
                        else if (significantChains.Count == 1)
                        {
                            // Single chain - just use it directly
                            mergedPath = significantChains[0];
                        }
                        else
                        {
                            // Multiple significant chains - merge with self-intersection detection
                            mergedPath = MergeChainsSimple(significantChains, provinceA, provinceB);
                        }

                        // DEBUG: Log pre-smoothing data (ONLY FIRST 3 BORDERS to avoid spam)
                        bool enableDebugLog = (processedBorders < 3);

                        if (enableDebugLog)
                        {
                            ArchonLogger.Log($"SMOOTHING DEBUG - Border {provinceA}-{provinceB} (#{processedBorders + 1}):", "map_initialization");
                            ArchonLogger.Log($"  Merged: {mergedPath.Count} points", "map_initialization");
                        }

                        // CRITICAL: Simplify the pixel-perfect path to create longer line segments
                        // This gives Chaikin something to actually smooth (staircase corners become angled lines)
                        List<Vector2> simplifiedPath = SimplifyPolyline(mergedPath, epsilon: 1.5f);

                        if (enableDebugLog)
                        {
                            ArchonLogger.Log($"  Simplified: {simplifiedPath.Count} points", "map_initialization");
                            if (simplifiedPath.Count >= 3)
                            {
                                ArchonLogger.Log($"  First 3 points: ({simplifiedPath[0].x:F2},{simplifiedPath[0].y:F2}), ({simplifiedPath[1].x:F2},{simplifiedPath[1].y:F2}), ({simplifiedPath[2].x:F2},{simplifiedPath[2].y:F2})", "map_initialization");
                            }
                        }

                        // CRITICAL: Apply Chaikin smoothing to create smooth sub-pixel curves
                        // This is the ONLY smoothing step - no Bézier fitting needed!
                        List<Vector2> smoothedPath = SmoothCurve(simplifiedPath, smoothingIterations, enableDebugLog);

                        // DEBUG: Log post-smoothing data
                        if (enableDebugLog)
                        {
                            ArchonLogger.Log($"  Post-smooth: {smoothedPath.Count} points (iterations: {smoothingIterations})", "map_initialization");
                            if (smoothedPath.Count >= 3)
                            {
                                ArchonLogger.Log($"  First 3 points: ({smoothedPath[0].x:F2},{smoothedPath[0].y:F2}), ({smoothedPath[1].x:F2},{smoothedPath[1].y:F2}), ({smoothedPath[2].x:F2},{smoothedPath[2].y:F2})", "map_initialization");
                            }
                        }

                        // CRITICAL: Tessellate smoothed path for dense vertex coverage (Paradox approach)
                        // Target: ~0.5 pixel spacing between vertices for smooth rendering at all zoom levels
                        // This dramatically improves visual quality for small borders
                        smoothedPath = TessellatePolyline(smoothedPath, maxSegmentLength: 0.5f);

                        if (enableDebugLog)
                        {
                            ArchonLogger.Log($"  Post-tessellation: {smoothedPath.Count} points", "map_initialization");
                        }

                        // Check if final smoothed path self-intersects (U-turn created by smoothing)
                        if (PathSelfIntersects(smoothedPath))
                        {
                            if (processedBorders < 10 || isDebugBorder)
                            {
                                ArchonLogger.LogWarning($"POST-SMOOTHING SELF-INTERSECTION {provinceA}-{provinceB}: Final path has U-turn after smoothing (chains: {allChains.Count})", "map_initialization");
                            }
                        }

                        // Early exit if path too short or degenerate
                        if (smoothedPath.Count < 2)
                            continue;

                        // Skip degenerate borders where start/end are same point
                        if (smoothedPath.Count == 2)
                        {
                            float length = Vector2.Distance(smoothedPath[0], smoothedPath[1]);
                            if (length < 0.5f) // Less than half a pixel
                            {
                                if (processedBorders < 10) // Log first few
                                    ArchonLogger.LogWarning($"BorderCurveExtractor: Skipping degenerate border {provinceA}<->{provinceB} (length: {length:F2}px)", "map_initialization");
                                continue;
                            }
                        }

                        // DEBUG: Log first curve stats
                        if (processedBorders == 0)
                        {
                            ArchonLogger.Log($"BorderCurveExtractor: First border - Raw pixels: {borderPixels.Count}, Chains: {allChains.Count}, Merged: {mergedPath.Count} points, Smoothed: {smoothedPath.Count} points", "map_initialization");
                        }

                        // Cache the smoothed polyline (Chaikin output)
                        // Note: Junction snapping happens AFTER all borders are extracted
                        borderPolylines[(provinceA, provinceB)] = smoothedPath;

                        // DEBUG: Log small borders to track if they reach mesh generation
                        if (mergedPath.Count <= 20 && processedBorders < 20)
                        {
                            ArchonLogger.Log($"CACHED SMALL BORDER {provinceA}-{provinceB}: original={mergedPath.Count}px, final={smoothedPath.Count} vertices", "map_initialization");
                        }

                        processedBorders++;
                    }
                    else if (processedBorders == 0 && borderPolylines.Count == 0)
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

            // POST-PROCESSING: Snap polyline endpoints at junctions to ensure perfect connectivity
            // This fixes staircase artifacts where multiple borders meet
            SnapPolylineEndpointsAtJunctions(borderPolylines);

            if (logProgress)
            {
                ArchonLogger.Log($"BorderCurveExtractor: Total time: {(Time.realtimeSinceStartup - startTime) * 1000f:F0}ms", "map_initialization");
            }

            return borderPolylines;
        }

        /// <summary>
        /// Detect all junction pixels where 3 or more provinces meet
        /// These pixels should be exact endpoints for all borders meeting at that junction
        /// </summary>
        private void DetectJunctionPixels()
        {
            junctionPixels = new Dictionary<Vector2, HashSet<ushort>>();
            int junctionCount = 0;
            float startTime = Time.realtimeSinceStartup;

            // Scan all border pixels to find junctions
            for (int y = 0; y < mapHeight; y++)
            {
                for (int x = 0; x < mapWidth; x++)
                {
                    // Get unique neighboring provinces at this pixel (includes self)
                    HashSet<ushort> neighboringProvinces = GetNeighboringProvinces(x, y, provinceIDPixels, mapWidth, mapHeight);

                    // If 3+ provinces meet here, it's a junction
                    if (neighboringProvinces.Count >= 3)
                    {
                        Vector2 junctionPos = new Vector2(x, y);
                        junctionPixels[junctionPos] = neighboringProvinces;
                        junctionCount++;
                    }
                }
            }

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderCurveExtractor: Detected {junctionCount} junction pixels (3+ provinces) in {elapsed:F0}ms", "map_initialization");
        }

        /// <summary>
        /// Snap curve endpoints that are within snap distance to a common point
        /// Uses spatial grid for O(n) performance instead of O(n²)
        /// Checks neighboring cells to catch cross-boundary junctions
        /// </summary>
        private void SnapCurveEndpointsAtJunctions(Dictionary<(ushort, ushort), List<BezierSegment>> borderCurves)
        {
            const float SNAP_DISTANCE = 1.5f; // Conservative snapping for polyline approach (short segments)
            const int GRID_CELL_SIZE = 4; // Grid cell size (must be >= snap distance)

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

            // Snap endpoints checking neighboring cells (3x3 around each cell)
            int snappedCount = 0;
            var processed = new HashSet<(Vector2, (ushort, ushort), int, bool)>();

            foreach (var kvp in grid)
            {
                var cellKey = kvp.Key;
                var cellEndpoints = kvp.Value;

                // For each endpoint in this cell
                for (int i = 0; i < cellEndpoints.Count; i++)
                {
                    if (processed.Contains(cellEndpoints[i])) continue;

                    var cluster = new List<(Vector2, (ushort, ushort), int, bool)> { cellEndpoints[i] };

                    // Check current cell + 24 neighboring cells (5x5 grid)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            var neighborKey = (cellKey.Item1 + dx, cellKey.Item2 + dy);
                            if (!grid.ContainsKey(neighborKey)) continue;

                            var neighborEndpoints = grid[neighborKey];
                            foreach (var candidate in neighborEndpoints)
                            {
                                if (processed.Contains(candidate)) continue;
                                if (candidate.Equals(cellEndpoints[i])) continue;

                                // CRITICAL: Don't snap P0 and P3 of the SAME segment together
                                // This creates degenerate zero-length segments (tumors)
                                bool sameSegment = (candidate.Item2 == cellEndpoints[i].Item2) &&
                                                   (candidate.Item3 == cellEndpoints[i].Item3);
                                if (sameSegment) continue;

                                float dist = Vector2.Distance(cellEndpoints[i].point, candidate.point);
                                if (dist <= SNAP_DISTANCE)
                                {
                                    cluster.Add(candidate);
                                }
                            }
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

                        // Snap all endpoints in cluster
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

            // DEBUG: Check for degenerate segments after snapping
            int degenerateCount = 0;
            foreach (var kvp in borderCurves)
            {
                var segments = kvp.Value;
                for (int i = 0; i < segments.Count; i++)
                {
                    float segLength = Vector2.Distance(segments[i].P0, segments[i].P3);
                    if (segLength < 0.1f)
                    {
                        degenerateCount++;
                        if (degenerateCount <= 5) // Log first 5
                        {
                            ArchonLogger.LogWarning($"BorderCurveExtractor: Degenerate segment in border {kvp.Key.Item1}<->{kvp.Key.Item2}, seg {i}: P0={segments[i].P0}, P3={segments[i].P3}, length={segLength:F2}", "map_initialization");
                        }
                    }
                }
            }
            if (degenerateCount > 0)
            {
                ArchonLogger.LogWarning($"BorderCurveExtractor: Found {degenerateCount} degenerate segments (length < 0.1px) after snapping!", "map_initialization");
            }

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
        /// Snap polyline endpoints that are within snap distance to a common point
        /// This ensures all borders meeting at a junction share the exact same endpoint coordinate
        /// Uses spatial grid for O(n) performance instead of O(n²)
        /// </summary>
        private void SnapPolylineEndpointsAtJunctions(Dictionary<(ushort, ushort), List<Vector2>> borderPolylines)
        {
            const float SNAP_DISTANCE = 2.0f; // Snap endpoints within 2 pixels (accounts for smoothing displacement)
            const int GRID_CELL_SIZE = 4; // Grid cell size (must be >= snap distance)

            // Spatial grid for fast neighbor lookup
            var grid = new Dictionary<(int, int), List<(Vector2 point, (ushort, ushort) borderKey, bool isStart)>>();

            // Add all polyline endpoints to spatial grid
            foreach (var kvp in borderPolylines)
            {
                var borderKey = kvp.Key;
                var polyline = kvp.Value;

                if (polyline.Count < 2)
                    continue;

                AddToGrid(grid, polyline[0], borderKey, true);  // First point
                AddToGrid(grid, polyline[polyline.Count - 1], borderKey, false);  // Last point
            }

            // Snap endpoints checking neighboring cells (5x5 grid around each cell)
            int snappedCount = 0;
            int junctionCount = 0;
            var processed = new HashSet<(Vector2, (ushort, ushort), bool)>();

            foreach (var kvp in grid)
            {
                var cellKey = kvp.Key;
                var cellEndpoints = kvp.Value;

                // For each endpoint in this cell
                for (int i = 0; i < cellEndpoints.Count; i++)
                {
                    if (processed.Contains(cellEndpoints[i])) continue;

                    var cluster = new List<(Vector2, (ushort, ushort), bool)> { cellEndpoints[i] };

                    // Check current cell + 24 neighboring cells (5x5 grid)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            var neighborKey = (cellKey.Item1 + dx, cellKey.Item2 + dy);
                            if (!grid.ContainsKey(neighborKey)) continue;

                            var neighborEndpoints = grid[neighborKey];
                            foreach (var candidate in neighborEndpoints)
                            {
                                if (processed.Contains(candidate)) continue;
                                if (candidate.Equals(cellEndpoints[i])) continue;

                                // CRITICAL: Don't snap start and end of the SAME polyline together
                                // This would create a degenerate loop
                                bool samePolyline = (candidate.Item2 == cellEndpoints[i].Item2);
                                if (samePolyline) continue;

                                float dist = Vector2.Distance(cellEndpoints[i].point, candidate.point);
                                if (dist <= SNAP_DISTANCE)
                                {
                                    cluster.Add(candidate);
                                }
                            }
                        }
                    }

                    // If 2+ endpoints are close, snap them to their average position
                    if (cluster.Count >= 2)
                    {
                        // Calculate average position
                        Vector2 avgPos = Vector2.zero;
                        foreach (var item in cluster)
                        {
                            avgPos += item.Item1; // Item1 is the point
                        }
                        avgPos /= cluster.Count;

                        // Snap all endpoints in cluster
                        foreach (var (point, borderKey, isStart) in cluster)
                        {
                            var polyline = borderPolylines[borderKey];

                            if (isStart)
                                polyline[0] = avgPos;
                            else
                                polyline[polyline.Count - 1] = avgPos;

                            processed.Add((point, borderKey, isStart));
                        }

                        snappedCount += cluster.Count;
                        junctionCount++;
                    }
                }
            }

            ArchonLogger.Log($"BorderCurveExtractor: Snapped {snappedCount} polyline endpoints at {junctionCount} junctions", "map_initialization");

            // Helper: Add endpoint to spatial grid
            void AddToGrid(Dictionary<(int, int), List<(Vector2, (ushort, ushort), bool)>> g, Vector2 point, (ushort, ushort) key, bool start)
            {
                int cellX = (int)(point.x / GRID_CELL_SIZE);
                int cellY = (int)(point.y / GRID_CELL_SIZE);
                var cellKey = (cellX, cellY);

                if (!g.ContainsKey(cellKey))
                    g[cellKey] = new List<(Vector2, (ushort, ushort), bool)>();

                g[cellKey].Add((point, key, start));
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

            // NOTE: We do NOT remove junction pixels here - curves render fully to junctions
            // Two-pass approach:
            // 1. Render all curves to texture (overlaps allowed)
            // 2. GPU cleanup pass fixes overlaps and junctions

            // DEBUG: Log results for first pair
            if (!firstResultLogged)
            {
                firstResultLogged = true;
                ArchonLogger.Log($"BorderCurveExtractor: First border {provinceA}<->{provinceB}: Found {borderPixels.Count} border pixels", "map_initialization");
            }

            return borderPixels;
        }

        /// <summary>
        /// Bridge gap between existing border pixels and a junction using BFS
        /// Finds shortest 8-connected path of valid border pixels to reach the junction
        /// Returns number of bridge pixels added
        /// </summary>
        private int BridgeToJunction(Vector2 junctionPos, ushort provinceA, ushort provinceB, List<Vector2> borderPixels)
        {
            int jx = (int)junctionPos.x;
            int jy = (int)junctionPos.y;

            // Check if junction already has an 8-connected neighbor in borderPixels
            bool hasConnection = false;
            int[] dx = { 1, -1, 0, 0, 1, -1, 1, -1 };
            int[] dy = { 0, 0, 1, -1, 1, 1, -1, -1 };

            for (int i = 0; i < 8; i++)
            {
                Vector2 neighbor = new Vector2(jx + dx[i], jy + dy[i]);
                if (borderPixels.Contains(neighbor))
                {
                    hasConnection = true;
                    break;
                }
            }

            // If already connected, no bridge needed
            if (hasConnection)
                return 0;

            // BFS to find shortest path from any existing border pixel to junction
            var queue = new Queue<Vector2>();
            var visited = new HashSet<Vector2>();
            var parent = new Dictionary<Vector2, Vector2>();

            // Start BFS from junction
            queue.Enqueue(junctionPos);
            visited.Add(junctionPos);

            Vector2 connectionPoint = Vector2.zero;
            bool foundPath = false;

            // BFS search (max distance: 10 pixels to avoid runaway searches)
            int maxDistance = 10;
            int currentDistance = 0;

            while (queue.Count > 0 && currentDistance < maxDistance)
            {
                int levelSize = queue.Count;
                currentDistance++;

                for (int level = 0; level < levelSize; level++)
                {
                    Vector2 current = queue.Dequeue();
                    int cx = (int)current.x;
                    int cy = (int)current.y;

                    // Check 8-neighbors
                    for (int i = 0; i < 8; i++)
                    {
                        int nx = cx + dx[i];
                        int ny = cy + dy[i];

                        // Bounds check
                        if (nx < 0 || nx >= mapWidth || ny < 0 || ny >= mapHeight)
                            continue;

                        Vector2 neighborPos = new Vector2(nx, ny);

                        // Skip if already visited
                        if (visited.Contains(neighborPos))
                            continue;

                        // If this pixel is already in borderPixels, we found a connection!
                        if (borderPixels.Contains(neighborPos))
                        {
                            connectionPoint = neighborPos;
                            parent[neighborPos] = current;
                            foundPath = true;
                            break;
                        }

                        // Check if this pixel is a valid border pixel (borders both A and B or is part of junction)
                        int index = ny * mapWidth + nx;
                        ushort pixelProvince = Province.ProvinceIDEncoder.UnpackProvinceID(provinceIDPixels[index]);

                        bool isValidBridge = false;

                        // Valid if it's part of A or B and borders the other
                        if (pixelProvince == provinceA && HasNeighborProvince(nx, ny, provinceB, provinceIDPixels, mapWidth, mapHeight))
                        {
                            isValidBridge = true;
                        }
                        else if (pixelProvince == provinceB && HasNeighborProvince(nx, ny, provinceA, provinceIDPixels, mapWidth, mapHeight))
                        {
                            isValidBridge = true;
                        }
                        // Also valid if it's part of a junction (touches A and B)
                        else if (junctionPixels.ContainsKey(neighborPos))
                        {
                            var junctionProvinces = junctionPixels[neighborPos];
                            if (junctionProvinces.Contains(provinceA) && junctionProvinces.Contains(provinceB))
                            {
                                isValidBridge = true;
                            }
                        }

                        if (isValidBridge)
                        {
                            visited.Add(neighborPos);
                            parent[neighborPos] = current;
                            queue.Enqueue(neighborPos);
                        }
                    }

                    if (foundPath)
                        break;
                }

                if (foundPath)
                    break;
            }

            // If no path found, return 0
            if (!foundPath)
                return 0;

            // Trace back path and add bridge pixels
            int bridgeCount = 0;
            Vector2 tracePos = connectionPoint;

            while (parent.ContainsKey(tracePos) && tracePos != junctionPos)
            {
                Vector2 nextPos = parent[tracePos];
                if (!borderPixels.Contains(nextPos) && nextPos != junctionPos)
                {
                    borderPixels.Add(nextPos);
                    bridgeCount++;
                }
                tracePos = nextPos;
            }

            return bridgeCount;
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
        /// Merge multiple chains with self-intersection detection
        /// Rejects merges that would create U-turns (self-crossing paths)
        /// </summary>
        private List<Vector2> MergeChainsSimple(List<List<Vector2>> chains, ushort provinceA = 0, ushort provinceB = 0)
        {
            if (chains.Count == 0)
                return new List<Vector2>();
            if (chains.Count == 1)
                return chains[0];

            // Start with longest chain
            int longestIdx = 0;
            int longestCount = chains[0].Count;
            for (int i = 1; i < chains.Count; i++)
            {
                if (chains[i].Count > longestCount)
                {
                    longestCount = chains[i].Count;
                    longestIdx = i;
                }
            }

            List<Vector2> merged = new List<Vector2>(chains[longestIdx]);
            HashSet<int> usedIndices = new HashSet<int> { longestIdx };

            // Merge remaining chains by closest endpoint (no U-turn detection)
            while (usedIndices.Count < chains.Count)
            {
                float bestDist = float.MaxValue;
                int bestChainIdx = -1;
                bool appendToEnd = true;
                bool reverseChain = false;

                for (int i = 0; i < chains.Count; i++)
                {
                    if (usedIndices.Contains(i)) continue;

                    var chain = chains[i];
                    Vector2 mergedStart = merged[0];
                    Vector2 mergedEnd = merged[merged.Count - 1];
                    Vector2 chainStart = chain[0];
                    Vector2 chainEnd = chain[chain.Count - 1];

                    float d1 = Vector2.Distance(mergedEnd, chainStart);
                    float d2 = Vector2.Distance(mergedEnd, chainEnd);
                    float d3 = Vector2.Distance(mergedStart, chainEnd);
                    float d4 = Vector2.Distance(mergedStart, chainStart);

                    float minDist = Mathf.Min(d1, Mathf.Min(d2, Mathf.Min(d3, d4)));

                    if (minDist < bestDist)
                    {
                        bestDist = minDist;
                        bestChainIdx = i;

                        if (minDist == d1) { appendToEnd = true; reverseChain = false; }
                        else if (minDist == d2) { appendToEnd = true; reverseChain = true; }
                        else if (minDist == d3) { appendToEnd = false; reverseChain = false; }
                        else { appendToEnd = false; reverseChain = true; }
                    }
                }

                // Merge if within reasonable distance
                const float MAX_MERGE_DISTANCE = 5.0f;
                if (bestChainIdx >= 0 && bestDist <= MAX_MERGE_DISTANCE)
                {
                    var chainToMerge = chains[bestChainIdx];
                    if (reverseChain)
                    {
                        chainToMerge = new List<Vector2>(chainToMerge);
                        chainToMerge.Reverse();
                    }

                    // CRITICAL: Test merge and check if it creates self-intersection (U-turn)
                    // U-turns always self-intersect, legitimate borders don't
                    List<Vector2> testMerge = new List<Vector2>(merged);
                    if (appendToEnd)
                        testMerge.AddRange(chainToMerge);
                    else
                        testMerge.InsertRange(0, chainToMerge);

                    bool wouldSelfIntersect = PathSelfIntersects(testMerge);

                    if (!wouldSelfIntersect)
                    {
                        if (appendToEnd)
                            merged.AddRange(chainToMerge);
                        else
                            merged.InsertRange(0, chainToMerge);

                        usedIndices.Add(bestChainIdx);
                    }
                    else
                    {
                        // Log rejection (for first 10 cases or debug borders)
                        if (usedIndices.Count < 10 || provinceA == 1160 || provinceB == 1160)
                        {
                            ArchonLogger.Log($"SELF-INTERSECTION DETECTED {provinceA}-{provinceB}: Rejected chain {bestChainIdx} ({chainToMerge.Count}px) - would create U-turn", "map_initialization");
                        }
                        break; // Skip this chain - would create U-turn
                    }
                }
                else
                {
                    break; // Too far apart
                }
            }

            return merged;
        }

        /// <summary>
        /// Check if a polyline path self-intersects (crosses itself)
        /// U-turns create self-intersections, legitimate borders don't
        /// </summary>
        private bool PathSelfIntersects(List<Vector2> path)
        {
            if (path.Count < 4)
                return false; // Need at least 4 points to self-intersect

            // Check every line segment against every other non-adjacent segment
            for (int i = 0; i < path.Count - 1; i++)
            {
                for (int j = i + 2; j < path.Count - 1; j++)
                {
                    // Skip adjacent segments (they share an endpoint, so they "touch" but don't cross)
                    if (j == i + 1)
                        continue;

                    if (LineSegmentsIntersect(path[i], path[i + 1], path[j], path[j + 1]))
                    {
                        return true; // Self-intersection found = U-turn
                    }
                }
            }

            return false; // No self-intersection = valid path
        }

        /// <summary>
        /// Check if two line segments intersect (not just touch at endpoints)
        /// Uses cross product method for robust intersection detection
        /// </summary>
        private bool LineSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            // Line segment 1: p1 -> p2
            // Line segment 2: p3 -> p4

            Vector2 d1 = p2 - p1;
            Vector2 d2 = p4 - p3;

            // Cross product to determine orientation
            float cross = d1.x * d2.y - d1.y * d2.x;

            // Parallel lines (or degenerate segments)
            if (Mathf.Abs(cross) < 1e-6f)
                return false;

            Vector2 d3 = p3 - p1;

            // Calculate parameters for intersection point
            float t1 = (d3.x * d2.y - d3.y * d2.x) / cross;
            float t2 = (d3.x * d1.y - d3.y * d1.x) / cross;

            // Segments intersect if both t parameters are in range (0, 1)
            // Exclude endpoints (== 0 or == 1) to avoid false positives at shared vertices
            const float epsilon = 0.01f; // Small tolerance to exclude near-endpoint intersections
            return (t1 > epsilon && t1 < 1.0f - epsilon &&
                    t2 > epsilon && t2 < 1.0f - epsilon);
        }

        /// <summary>
        /// Filter border pixels to convex hull perimeter, removing interior pixels
        /// This prevents U-turns caused by peninsula indents creating interior loops
        /// </summary>
        private List<Vector2> FilterToConvexHullPerimeter(List<Vector2> pixels)
        {
            if (pixels.Count < 3)
                return pixels;

            // Compute convex hull using Graham scan
            List<Vector2> hull = ComputeConvexHull(pixels);

            // Keep pixels that are ON or NEAR the hull perimeter (within 2 pixels)
            const float PERIMETER_TOLERANCE = 2.0f;
            List<Vector2> filtered = new List<Vector2>();

            foreach (var pixel in pixels)
            {
                if (IsPointOnOrNearPolyline(pixel, hull, PERIMETER_TOLERANCE))
                {
                    filtered.Add(pixel);
                }
            }

            return filtered.Count > 0 ? filtered : pixels; // Fallback to original if filtering failed
        }

        /// <summary>
        /// Compute convex hull of points using Graham scan algorithm
        /// Returns hull vertices in counter-clockwise order
        /// </summary>
        private List<Vector2> ComputeConvexHull(List<Vector2> points)
        {
            if (points.Count < 3)
                return new List<Vector2>(points);

            // Find bottom-most point (or left-most if tied)
            Vector2 pivot = points[0];
            foreach (var p in points)
            {
                if (p.y < pivot.y || (p.y == pivot.y && p.x < pivot.x))
                    pivot = p;
            }

            // Sort points by polar angle with respect to pivot
            List<Vector2> sorted = new List<Vector2>(points);
            sorted.Sort((a, b) =>
            {
                if (a == pivot) return -1;
                if (b == pivot) return 1;

                float angleA = Mathf.Atan2(a.y - pivot.y, a.x - pivot.x);
                float angleB = Mathf.Atan2(b.y - pivot.y, b.x - pivot.x);

                if (Mathf.Abs(angleA - angleB) < 1e-6f)
                {
                    // Same angle - closer point first
                    float distA = Vector2.Distance(pivot, a);
                    float distB = Vector2.Distance(pivot, b);
                    return distA.CompareTo(distB);
                }

                return angleA.CompareTo(angleB);
            });

            // Graham scan
            List<Vector2> hull = new List<Vector2>();
            foreach (var point in sorted)
            {
                // Remove points that make right turn
                while (hull.Count >= 2 && CrossProduct(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(point);
            }

            return hull;
        }

        /// <summary>
        /// Cross product for Graham scan (determines turn direction)
        /// Positive = counter-clockwise, Negative = clockwise, Zero = collinear
        /// </summary>
        private float CrossProduct(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }

        /// <summary>
        /// Check if point is on or near a polyline (within tolerance distance)
        /// </summary>
        private bool IsPointOnOrNearPolyline(Vector2 point, List<Vector2> polyline, float tolerance)
        {
            if (polyline.Count < 2)
                return false;

            // Check distance to each line segment
            for (int i = 0; i < polyline.Count; i++)
            {
                Vector2 p1 = polyline[i];
                Vector2 p2 = polyline[(i + 1) % polyline.Count]; // Wrap around for closed hull

                float dist = DistancePointToSegment(point, p1, p2);
                if (dist <= tolerance)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Compute distance from point to line segment
        /// </summary>
        private float DistancePointToSegment(Vector2 point, Vector2 segStart, Vector2 segEnd)
        {
            Vector2 segVec = segEnd - segStart;
            Vector2 pointVec = point - segStart;

            float segLengthSq = segVec.sqrMagnitude;
            if (segLengthSq < 1e-6f)
                return Vector2.Distance(point, segStart);

            // Project point onto segment
            float t = Mathf.Clamp01(Vector2.Dot(pointVec, segVec) / segLengthSq);
            Vector2 projection = segStart + t * segVec;

            return Vector2.Distance(point, projection);
        }

        /// <summary>
        /// Rebuild provinceMapping from filtered province ID pixels
        /// Clears existing pixel lists and re-scans the filtered texture
        /// </summary>
        private void RebuildProvinceMappingFromFilteredPixels(Color32[] pixels, int width, int height)
        {
            // Clear all pixel lists (but keep province IDs and colors)
            var allProvinces = provinceMapping.GetAllProvinces();
            foreach (var kvp in allProvinces)
            {
                kvp.Value.Pixels.Clear();
            }

            // Re-scan filtered texture and rebuild pixel lists
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    ushort provinceID = Province.ProvinceIDEncoder.UnpackProvinceID(pixels[idx]);

                    if (provinceID > 0)
                    {
                        provinceMapping.AddPixelToProvince(provinceID, x, y);
                    }
                }
            }
        }

        /// <summary>
        /// Apply median filter to province ID texture to smooth boundaries
        /// Removes small peninsulas and indents that cause U-turn artifacts
        /// Uses 3x3 window - replaces center pixel with most common province ID in neighborhood
        /// </summary>
        private Color32[] ApplyMedianFilterToProvinceIDs(Color32[] pixels, int width, int height)
        {
            Color32[] filtered = new Color32[pixels.Length];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int centerIdx = y * width + x;

                    // Collect province IDs in 3x3 window
                    var neighborIDs = new System.Collections.Generic.Dictionary<ushort, int>(); // provinceID -> count

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;

                            // Skip out of bounds
                            if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                                continue;

                            int neighborIdx = ny * width + nx;
                            ushort provinceID = Province.ProvinceIDEncoder.UnpackProvinceID(pixels[neighborIdx]);

                            if (provinceID > 0) // Skip empty pixels
                            {
                                if (!neighborIDs.ContainsKey(provinceID))
                                    neighborIDs[provinceID] = 0;
                                neighborIDs[provinceID]++;
                            }
                        }
                    }

                    // Find most common province ID (median/mode)
                    ushort mostCommonID = 0;
                    int maxCount = 0;
                    foreach (var kvp in neighborIDs)
                    {
                        if (kvp.Value > maxCount)
                        {
                            maxCount = kvp.Value;
                            mostCommonID = kvp.Key;
                        }
                    }

                    // Use most common ID, or keep original if no neighbors found
                    if (mostCommonID > 0)
                        filtered[centerIdx] = Province.ProvinceIDEncoder.PackProvinceID(mostCommonID);
                    else
                        filtered[centerIdx] = pixels[centerIdx];
                }
            }

            return filtered;
        }

        /// <summary>
        /// Merge multiple chains into one continuous path by connecting nearby endpoints
        /// Prioritizes junction pixels as connection points to ensure borders meet at junctions
        /// </summary>
        private List<Vector2> MergeChains(List<List<Vector2>> chains)
        {
            if (chains.Count == 0)
                return new List<Vector2>();

            if (chains.Count == 1)
                return chains[0];

            // Start with longest chain
            int longestIdx = 0;
            int longestCount = chains[0].Count;
            for (int i = 1; i < chains.Count; i++)
            {
                if (chains[i].Count > longestCount)
                {
                    longestCount = chains[i].Count;
                    longestIdx = i;
                }
            }

            List<Vector2> merged = new List<Vector2>(chains[longestIdx]);
            HashSet<int> usedIndices = new HashSet<int> { longestIdx };

            // Keep merging closest chains until all are connected
            while (usedIndices.Count < chains.Count)
            {
                float bestDist = float.MaxValue;
                int bestChainIdx = -1;
                bool appendToEnd = true;
                bool reverseChain = false;

                // Find closest unused chain
                for (int i = 0; i < chains.Count; i++)
                {
                    if (usedIndices.Contains(i)) continue;

                    var chain = chains[i];
                    Vector2 mergedStart = merged[0];
                    Vector2 mergedEnd = merged[merged.Count - 1];
                    Vector2 chainStart = chain[0];
                    Vector2 chainEnd = chain[chain.Count - 1];

                    // Check all 4 connection possibilities
                    float d1 = Vector2.Distance(mergedEnd, chainStart); // Append chain normally
                    float d2 = Vector2.Distance(mergedEnd, chainEnd);   // Append chain reversed
                    float d3 = Vector2.Distance(mergedStart, chainEnd); // Prepend chain normally
                    float d4 = Vector2.Distance(mergedStart, chainStart); // Prepend chain reversed

                    float minDist = Mathf.Min(d1, Mathf.Min(d2, Mathf.Min(d3, d4)));

                    if (minDist < bestDist)
                    {
                        bestDist = minDist;
                        bestChainIdx = i;

                        if (minDist == d1) { appendToEnd = true; reverseChain = false; }
                        else if (minDist == d2) { appendToEnd = true; reverseChain = true; }
                        else if (minDist == d3) { appendToEnd = false; reverseChain = false; }
                        else { appendToEnd = false; reverseChain = true; }
                    }
                }

                // CRITICAL: Only merge if chains are close (within 5 pixels)
                // Prevents creating U-turn artifacts from connecting distant chains
                const float MAX_MERGE_DISTANCE = 5.0f;

                if (bestChainIdx >= 0 && bestDist <= MAX_MERGE_DISTANCE)
                {
                    var chainToMerge = chains[bestChainIdx];
                    if (reverseChain)
                    {
                        chainToMerge = new List<Vector2>(chainToMerge);
                        chainToMerge.Reverse();
                    }

                    // CRITICAL: Check if merge would create backtracking (U-turn)
                    // Use BOTH angle at connection point AND overall direction comparison
                    bool wouldCreateUturn = false;

                    // Check 1: Angle at connection point (sharp turn check)
                    if (merged.Count >= 2 && chainToMerge.Count >= 2)
                    {
                        if (appendToEnd)
                        {
                            Vector2 beforeConnection = merged[merged.Count - 2];
                            Vector2 connectionPoint = merged[merged.Count - 1];
                            Vector2 afterConnection = chainToMerge[1];

                            float angle = CalculateAngle(beforeConnection, connectionPoint, afterConnection);
                            if (angle > 90f) // Sharp turn at connection
                            {
                                wouldCreateUturn = true;
                            }
                        }
                        else
                        {
                            Vector2 beforeConnection = chainToMerge[chainToMerge.Count - 2];
                            Vector2 connectionPoint = chainToMerge[chainToMerge.Count - 1];
                            Vector2 afterConnection = merged[1];

                            float angle = CalculateAngle(beforeConnection, connectionPoint, afterConnection);
                            if (angle > 90f) // Sharp turn at connection
                            {
                                wouldCreateUturn = true;
                            }
                        }
                    }

                    // Check 2: Overall direction comparison (gradual U-turn check)
                    // DISABLED: This was too aggressive and blocked legitimate connections
                    // The sharp angle check (Check 1) should be sufficient

                    if (!wouldCreateUturn)
                    {
                        if (appendToEnd)
                            merged.AddRange(chainToMerge);
                        else
                            merged.InsertRange(0, chainToMerge);

                        usedIndices.Add(bestChainIdx);
                    }
                    else
                    {
                        // Reject merge - would create U-turn
                        // DEBUG: Log rejected merges for debugging
                        if (usedIndices.Count >= 1 && chains.Count > 1) // Log when we have unmerged chains left
                        {
                            int remainingChains = chains.Count - usedIndices.Count;
                            ArchonLogger.Log($"REJECTED MERGE: {remainingChains} chains left unmerged, rejected connection at distance {bestDist:F2}px", "map_initialization");
                        }
                        break;
                    }
                }
                else
                {
                    // Can't merge - chains too far apart or no more chains
                    break;
                }
            }

            // CRITICAL: If we couldn't merge all chains, discard tiny leftover chains (<5 pixels)
            // These are usually artifacts (noise pixels) that would create bad geometry
            if (usedIndices.Count < chains.Count)
            {
                int discardedCount = 0;
                for (int i = 0; i < chains.Count; i++)
                {
                    if (!usedIndices.Contains(i) && chains[i].Count < 5)
                    {
                        discardedCount++;
                    }
                }

                if (discardedCount > 0)
                {
                    // Note: We already returned 'merged' which doesn't include these tiny chains
                    // This is intentional - we're discarding them as artifacts
                }
            }

            return merged;
        }

        /// <summary>
        /// Calculate angle in degrees at point B formed by line A-B-C
        /// Returns 0° for straight line, 180° for complete reversal
        /// </summary>
        private float CalculateAngle(Vector2 pointA, Vector2 pointB, Vector2 pointC)
        {
            Vector2 vectorBA = pointA - pointB;
            Vector2 vectorBC = pointC - pointB;

            float dotProduct = Vector2.Dot(vectorBA.normalized, vectorBC.normalized);
            float angleRadians = Mathf.Acos(Mathf.Clamp(dotProduct, -1f, 1f));
            float angleDegrees = angleRadians * Mathf.Rad2Deg;

            return angleDegrees;
        }

        /// <summary>
        /// Chain scattered border pixels into an ordered polyline (single chain)
        /// Uses greedy nearest-neighbor with strict adjacency constraint
        /// Modifies the 'remaining' set by removing chained pixels
        /// IMPORTANT: Starts from junction pixels and always chains to junction endpoints
        /// </summary>
        private List<Vector2> ChainBorderPixelsSingle(HashSet<Vector2> remaining)
        {
            if (remaining.Count == 0)
                return new List<Vector2>();

            List<Vector2> orderedPath = new List<Vector2>();

            // STRATEGY: Start from a junction pixel if one exists
            // This ensures chains naturally end at junctions
            Vector2 current = Vector2.zero;
            bool foundJunctionStart = false;

            foreach (var pixel in remaining)
            {
                if (junctionPixels.ContainsKey(pixel))
                {
                    current = pixel;
                    foundJunctionStart = true;
                    break;
                }
            }

            // If no junction found, start with any pixel
            if (!foundJunctionStart)
            {
                current = remaining.First();
            }

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

                // Stop conditions:
                // 1. No adjacent pixel found (gap)
                // 2. Current pixel is a junction (endpoint reached)
                if (!foundAdjacent)
                {
                    break; // Gap detected, stop chain
                }

                // Add to path
                orderedPath.Add(nearest);
                remaining.Remove(nearest);
                current = nearest;

                // DON'T stop at junctions - continue chaining through them
                // This ensures chains connect all the way through junction points
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
        private List<Vector2> SmoothCurve(List<Vector2> points, int iterations, bool enableDebugLog = false)
        {
            if (points.Count < 3)
            {
                if (enableDebugLog)
                    ArchonLogger.Log($"  SmoothCurve: SKIPPED (only {points.Count} points, need 3+)", "map_initialization");
                return points; // Can't smooth lines with less than 3 points
            }

            if (enableDebugLog)
                ArchonLogger.Log($"  SmoothCurve: Starting with {points.Count} points, {iterations} iterations", "map_initialization");

            // Store original endpoints - these MUST NOT change to preserve junctions
            Vector2 originalFirst = points[0];
            Vector2 originalLast = points[points.Count - 1];

            List<Vector2> smoothed = new List<Vector2>(points);

            for (int iter = 0; iter < iterations; iter++)
            {
                int beforeCount = smoothed.Count;
                List<Vector2> newPoints = new List<Vector2>(smoothed.Count * 2);

                // Chaikin smoothing: For each segment, create 2 points at 1/4 and 3/4
                // But preserve the ORIGINAL endpoints (don't modify first/last)
                for (int i = 0; i < smoothed.Count - 1; i++)
                {
                    Vector2 p0 = smoothed[i];
                    Vector2 p1 = smoothed[i + 1];

                    // Create two new points at 1/4 and 3/4 along the segment
                    Vector2 q = Vector2.Lerp(p0, p1, 0.25f);
                    Vector2 r = Vector2.Lerp(p0, p1, 0.75f);

                    // For first segment, use original first point instead of q
                    if (i == 0)
                    {
                        newPoints.Add(originalFirst);
                        newPoints.Add(r);
                    }
                    // For last segment, use original last point instead of r
                    else if (i == smoothed.Count - 2)
                    {
                        newPoints.Add(q);
                        newPoints.Add(originalLast);
                    }
                    // For interior segments, add both q and r
                    else
                    {
                        newPoints.Add(q);
                        newPoints.Add(r);
                    }
                }

                smoothed = newPoints;

                if (enableDebugLog)
                    ArchonLogger.Log($"    Iteration {iter + 1}: {beforeCount} → {smoothed.Count} points", "map_initialization");
            }

            if (enableDebugLog)
                ArchonLogger.Log($"  SmoothCurve: COMPLETE - Final count: {smoothed.Count} points", "map_initialization");
            return smoothed;
        }

        /// <summary>
        /// Tessellate polyline to ensure dense vertex coverage (Paradox approach)
        /// Subdivides any segment longer than maxSegmentLength to create smooth rendering
        /// Target: 0.5 pixel spacing = ~2 vertices per pixel for ultra-smooth borders
        /// </summary>
        private List<Vector2> TessellatePolyline(List<Vector2> points, float maxSegmentLength)
        {
            if (points.Count < 2)
                return points;

            List<Vector2> tessellated = new List<Vector2>();
            tessellated.Add(points[0]);

            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 p0 = points[i];
                Vector2 p1 = points[i + 1];
                float segmentLength = Vector2.Distance(p0, p1);

                // If segment is longer than max, subdivide it
                if (segmentLength > maxSegmentLength)
                {
                    int subdivisions = Mathf.CeilToInt(segmentLength / maxSegmentLength);
                    for (int j = 1; j <= subdivisions; j++)
                    {
                        float t = j / (float)subdivisions;
                        Vector2 interpolated = Vector2.Lerp(p0, p1, t);
                        tessellated.Add(interpolated);
                    }
                }
                else
                {
                    // Segment is short enough - keep it
                    tessellated.Add(p1);
                }
            }

            return tessellated;
        }

        /// <summary>
        /// Simplify polyline using Ramer-Douglas-Peucker algorithm
        /// Reduces pixel-perfect staircase to longer line segments that Chaikin can smooth
        /// </summary>
        private List<Vector2> SimplifyPolyline(List<Vector2> points, float epsilon)
        {
            if (points.Count < 3)
                return new List<Vector2>(points);

            // Find the point with maximum distance from line segment
            float maxDistance = 0;
            int maxIndex = 0;

            Vector2 start = points[0];
            Vector2 end = points[points.Count - 1];

            for (int i = 1; i < points.Count - 1; i++)
            {
                float distance = PerpendicularDistance(points[i], start, end);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    maxIndex = i;
                }
            }

            // If max distance is greater than epsilon, recursively simplify
            if (maxDistance > epsilon)
            {
                // Recursive call on both halves
                List<Vector2> left = SimplifyPolyline(points.GetRange(0, maxIndex + 1), epsilon);
                List<Vector2> right = SimplifyPolyline(points.GetRange(maxIndex, points.Count - maxIndex), epsilon);

                // Combine results (remove duplicate middle point)
                List<Vector2> result = new List<Vector2>(left.Count + right.Count - 1);
                result.AddRange(left);
                result.AddRange(right.GetRange(1, right.Count - 1)); // Skip first point (duplicate)
                return result;
            }
            else
            {
                // Points are close enough to line - just return endpoints
                return new List<Vector2> { start, end };
            }
        }

        /// <summary>
        /// Calculate perpendicular distance from point to line segment
        /// </summary>
        private float PerpendicularDistance(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float lineLengthSquared = line.sqrMagnitude;

            if (lineLengthSquared == 0)
                return Vector2.Distance(point, lineStart);

            // Project point onto line
            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / lineLengthSquared);
            Vector2 projection = lineStart + t * line;

            return Vector2.Distance(point, projection);
        }

        /// <summary>
        /// Unify control points for curves meeting at junctions
        /// Prevents overlapping render regions by forcing curves to approach junctions from consistent angles
        /// Uses spatial indexing for O(n) performance instead of O(n²)
        /// </summary>
        private void UnifyJunctionControlPoints(Dictionary<(ushort, ushort), List<BezierSegment>> borderCurves)
        {
            float startTime = Time.realtimeSinceStartup;
            int segmentsModified = 0;

            // STEP 1: Build spatial index - map junction positions to segments (O(n))
            var junctionIndex = new Dictionary<Vector2, List<((ushort, ushort) border, int segmentIdx, bool isStart)>>();

            foreach (var borderKvp in borderCurves)
            {
                var borderKey = borderKvp.Key;
                var segments = borderKvp.Value;

                for (int i = 0; i < segments.Count; i++)
                {
                    var seg = segments[i];

                    // Quantize endpoints to match junction pixel positions (integer coordinates)
                    Vector2 p0Quantized = new Vector2(Mathf.Round(seg.P0.x), Mathf.Round(seg.P0.y));
                    Vector2 p3Quantized = new Vector2(Mathf.Round(seg.P3.x), Mathf.Round(seg.P3.y));

                    // Index by start point
                    if (!junctionIndex.ContainsKey(p0Quantized))
                        junctionIndex[p0Quantized] = new List<((ushort, ushort), int, bool)>();
                    junctionIndex[p0Quantized].Add((borderKey, i, true));

                    // Index by end point
                    if (!junctionIndex.ContainsKey(p3Quantized))
                        junctionIndex[p3Quantized] = new List<((ushort, ushort), int, bool)>();
                    junctionIndex[p3Quantized].Add((borderKey, i, false));
                }
            }

            // STEP 2: Process junctions where 3+ segments meet (O(junctions))
            int junctionsProcessed = 0;

            foreach (var kvp in junctionIndex)
            {
                var segmentsAtJunction = kvp.Value;

                // Only process if 3+ segments meet here (actual junction)
                if (segmentsAtJunction.Count >= 3)
                {
                    junctionsProcessed++;

                    // Force all segments to use straight-line control points at junction
                    foreach (var (borderKey, segmentIdx, isStart) in segmentsAtJunction)
                    {
                        var segments = borderCurves[borderKey];
                        var seg = segments[segmentIdx];

                        if (isStart)
                        {
                            // Segment starts at junction - force P1 = P0 (straight line)
                            seg.P1 = seg.P0;
                        }
                        else
                        {
                            // Segment ends at junction - force P2 = P3 (straight line)
                            seg.P2 = seg.P3;
                        }

                        segments[segmentIdx] = seg;
                        segmentsModified++;
                    }
                }
            }

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderCurveExtractor: Unified {junctionsProcessed} junctions ({segmentsModified} segments modified) in {elapsed:F0}ms", "map_initialization");
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

        /// <summary>
        /// Build connectivity information for all segment endpoints
        /// Sets connectivity flags for geometry-aware rendering (avoids round caps)
        /// </summary>
        private void BuildSegmentConnectivity(Dictionary<(ushort, ushort), List<BezierSegment>> borderCurves)
        {
            float startTime = Time.realtimeSinceStartup;
            const float ENDPOINT_THRESHOLD = 2.0f; // Endpoints within 2px are considered connected

            // Build spatial index of all endpoints
            var endpointIndex = new Dictionary<(int, int), List<(Vector2 pos, (ushort, ushort) borderKey, int segmentIndex, bool isP0)>>();

            foreach (var kvp in borderCurves)
            {
                var borderKey = kvp.Key;
                var segments = kvp.Value;

                for (int i = 0; i < segments.Count; i++)
                {
                    // Add both endpoints to spatial grid
                    AddToEndpointGrid(endpointIndex, segments[i].P0, borderKey, i, true);
                    AddToEndpointGrid(endpointIndex, segments[i].P3, borderKey, i, false);
                }
            }

            // Analyze connectivity at each grid cell
            int connectedCount = 0;
            int junctionCount = 0;

            foreach (var cellEndpoints in endpointIndex.Values)
            {
                if (cellEndpoints.Count < 2) continue; // No connectivity possible

                // Check all pairs of endpoints in this cell
                for (int i = 0; i < cellEndpoints.Count; i++)
                {
                    var (pos1, key1, idx1, isP0_1) = cellEndpoints[i];

                    // Count how many OTHER endpoints are close to this one
                    int nearbyCount = 0;
                    for (int j = 0; j < cellEndpoints.Count; j++)
                    {
                        if (i == j) continue;
                        var (pos2, key2, idx2, isP0_2) = cellEndpoints[j];

                        float dist = Vector2.Distance(pos1, pos2);
                        if (dist < ENDPOINT_THRESHOLD)
                        {
                            nearbyCount++;
                        }
                    }

                    // Set connectivity flags
                    // IMPORTANT: Get list reference, modify segment, write back to list
                    var segments = borderCurves[key1];
                    var seg = segments[idx1];
                    uint flags = seg.connectivityFlags;

                    if (nearbyCount >= 1)
                    {
                        // Has at least one connected segment
                        if (isP0_1)
                            flags |= 0x1; // Bit 0: P0 has connection
                        else
                            flags |= 0x2; // Bit 1: P3 has connection

                        connectedCount++;
                    }

                    if (nearbyCount >= 2)
                    {
                        // Junction (3+ segments meet at this point)
                        if (isP0_1)
                            flags |= 0x4; // Bit 2: P0 is junction
                        else
                            flags |= 0x8; // Bit 3: P3 is junction

                        junctionCount++;
                    }

                    // Update the flags
                    seg.connectivityFlags = flags;

                    // CRITICAL: Write back to the list (structs are value types!)
                    segments[idx1] = seg;

                    // Update the dictionary entry (in case the list is a copy)
                    borderCurves[key1] = segments;
                }
            }

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderCurveExtractor: Built connectivity - {connectedCount} connected endpoints, {junctionCount} junction endpoints in {elapsed:F0}ms", "map_initialization");

            // DEBUG: Log first segment's connectivity flags
            foreach (var kvp in borderCurves)
            {
                if (kvp.Value.Count > 0)
                {
                    var seg = kvp.Value[0];
                    ArchonLogger.Log($"BorderCurveExtractor: First segment connectivity flags = 0x{seg.connectivityFlags:X} (P0 connected={(seg.connectivityFlags & 0x1) != 0}, P3 connected={(seg.connectivityFlags & 0x2) != 0})", "map_initialization");
                    break;
                }
            }
        }

        /// <summary>
        /// Helper: Add endpoint to spatial grid (10px cells for fast lookup)
        /// </summary>
        private void AddToEndpointGrid(
            Dictionary<(int, int), List<(Vector2, (ushort, ushort), int, bool)>> grid,
            Vector2 point,
            (ushort, ushort) borderKey,
            int segmentIndex,
            bool isP0)
        {
            const int CELL_SIZE = 10;
            int cellX = (int)(point.x / CELL_SIZE);
            int cellY = (int)(point.y / CELL_SIZE);
            var cellKey = (cellX, cellY);

            if (!grid.ContainsKey(cellKey))
                grid[cellKey] = new List<(Vector2, (ushort, ushort), int, bool)>();

            grid[cellKey].Add((point, borderKey, segmentIndex, isP0));
        }
    }
}
