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
                    if (processedBorders == 0 && borderCurves.Count == 0)
                    {
                        ArchonLogger.Log($"BorderCurveExtractor: First border pair: {provinceA} <-> {provinceB}", "map_initialization");
                    }

                    // Extract shared border pixels (unordered)
                    List<Vector2> borderPixels = ExtractSharedBorderPixels(provinceA, provinceB);

                    // Skip tiny borders (small peninsulas/artifacts from map compression)
                    // These create "wild lines" that cross through provinces
                    const int MIN_BORDER_PIXELS = 10; // Minimum pixels for a real border
                    if (borderPixels.Count > 0 && borderPixels.Count < MIN_BORDER_PIXELS)
                    {
                        // DEBUG: Log first few skipped tiny borders
                        if (processedBorders < 3)
                        {
                            ArchonLogger.Log($"BorderCurveExtractor: Skipping tiny border {provinceA}<->{provinceB} ({borderPixels.Count} pixels)", "map_initialization");
                        }
                        continue; // Skip this border - too small to be real
                    }

                    if (borderPixels.Count > 0)
                    {
                        // Chain pixels into multiple ordered polylines (handles disconnected border segments)
                        List<List<Vector2>> allChains = ChainBorderPixelsMultiple(borderPixels);

                        // CRITICAL: Merge chains into one continuous path for polyline rendering
                        // Multiple separate chains create visual gaps and clumping at junctions
                        List<Vector2> mergedPath = MergeChains(allChains);

                        // Generate polyline segments for merged path
                        List<BezierSegment> allCurveSegments = new List<BezierSegment>();
                        BorderType borderType = DetermineBorderType(provinceA, provinceB);

                        if (mergedPath.Count >= 2)
                        {
                            // Fit Bézier curves to merged pixel path (creates one continuous polyline)
                            allCurveSegments = BezierCurveFitter.FitCurve(mergedPath, borderType);
                        }

                        // Early exit if no valid curves generated
                        if (allCurveSegments.Count == 0)
                            continue;

                        // DEBUG: Validate curves for NaN or invalid values
                        foreach (var seg in allCurveSegments)
                        {
                            if (float.IsNaN(seg.P0.x) || float.IsNaN(seg.P0.y) ||
                                float.IsNaN(seg.P1.x) || float.IsNaN(seg.P1.y) ||
                                float.IsNaN(seg.P2.x) || float.IsNaN(seg.P2.y) ||
                                float.IsNaN(seg.P3.x) || float.IsNaN(seg.P3.y))
                            {
                                ArchonLogger.LogError($"BorderCurveExtractor: Border {provinceA}<->{provinceB} has NaN curve! P0={seg.P0}, P1={seg.P1}, P2={seg.P2}, P3={seg.P3}", "map_initialization");
                            }
                        }

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
                            ArchonLogger.Log($"BorderCurveExtractor: First curve - Raw pixels: {borderPixels.Count}, Chains: {allChains.Count}, Merged path: {mergedPath.Count} pixels, Bézier segments: {allCurveSegments.Count}", "map_initialization");
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

            // POST-PROCESSING: Unify junction control points to prevent overlapping render regions
            // DISABLED: Junction pixels now handled by BorderMask, no CPU post-processing needed
            // UnifyJunctionControlPoints(borderCurves);

            if (logProgress)
            {
                ArchonLogger.Log($"BorderCurveExtractor: Total time: {(Time.realtimeSinceStartup - startTime) * 1000f:F0}ms", "map_initialization");
            }

            return borderCurves;
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

            // Add junction pixels ONLY if they're already 8-connected to existing border pixels
            // This ensures borders reach junctions without adding disconnected pixels
            int addedJunctions = 0;

            foreach (var kvp in junctionPixels)
            {
                var junctionPos = kvp.Key;
                var provinces = kvp.Value;

                // If this junction involves both provinceA and provinceB, check if we should add it
                if (provinces.Contains(provinceA) && provinces.Contains(provinceB))
                {
                    // Only add junction if NOT already in border AND is 8-connected to existing border
                    if (!borderPixels.Contains(junctionPos))
                    {
                        bool isAdjacent = false;
                        int jx = (int)junctionPos.x;
                        int jy = (int)junctionPos.y;

                        // Check 8 neighbors
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                Vector2 neighbor = new Vector2(jx + dx, jy + dy);
                                if (borderPixels.Contains(neighbor))
                                {
                                    isAdjacent = true;
                                    break;
                                }
                            }
                            if (isAdjacent) break;
                        }

                        // Only add if adjacent to existing border pixels
                        if (isAdjacent)
                        {
                            borderPixels.Add(junctionPos);
                            addedJunctions++;
                        }
                    }
                }
            }

            // DEBUG: Log junction additions for first border
            if (addedJunctions > 0 && !firstResultLogged)
            {
                ArchonLogger.Log($"BorderCurveExtractor: Border {provinceA}<->{provinceB}: Added {addedJunctions} adjacent junction pixels", "map_initialization");
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

                // Merge best chain
                if (bestChainIdx >= 0)
                {
                    var chainToMerge = chains[bestChainIdx];
                    if (reverseChain)
                    {
                        chainToMerge = new List<Vector2>(chainToMerge);
                        chainToMerge.Reverse();
                    }

                    if (appendToEnd)
                        merged.AddRange(chainToMerge);
                    else
                        merged.InsertRange(0, chainToMerge);

                    usedIndices.Add(bestChainIdx);
                }
                else
                {
                    break; // No more chains to merge
                }
            }

            return merged;
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
    }
}
