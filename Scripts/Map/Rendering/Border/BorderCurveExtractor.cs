using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Core.Systems;
using Map.Rendering.Border;
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
    ///
    /// REFACTORED: Now uses specialized helper classes for single responsibility
    /// - BorderPolylineSimplifier: RDP simplification, Chaikin smoothing, tessellation
    /// - BorderGeometryUtils: Geometric utilities (intersection, angles, distances)
    /// - BorderChainMerger: Chain merging with U-turn detection
    /// - JunctionDetector: Junction detection and endpoint snapping
    /// - MedianFilterProcessor: Median filtering and pixel chaining
    /// </summary>
    public class BorderCurveExtractor
    {
        private readonly MapTextureManager textureManager;
        private readonly AdjacencySystem adjacencySystem;
        private readonly ProvinceSystemType provinceSystem;
        private readonly ProvinceMapping provinceMapping;

        // Configuration
        private readonly int smoothingIterations = 2;  // More iterations = smoother curves (reduced for sharper corners)
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

        // Helper classes (created once, reused for all borders)
        private MedianFilterProcessor medianFilterProcessor;
        private JunctionDetector junctionDetector;
        private BorderChainMerger chainMerger;

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

            // Initialize junction detector BEFORE median filtering (needs original pixel data)
            junctionPixels = new Dictionary<Vector2, HashSet<ushort>>();

            // Initialize helper classes
            medianFilterProcessor = new MedianFilterProcessor(mapWidth, mapHeight, junctionPixels);

            // CRITICAL: Apply median filter to smooth province IDs (removes peninsula indents at source)
            // This eliminates U-turn artifacts by smoothing the province boundaries before extraction
            provinceIDPixels = medianFilterProcessor.ApplyMedianFilter(provinceIDPixels);

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

            // Initialize junction detector and detect junctions
            junctionDetector = new JunctionDetector(mapWidth, mapHeight, provinceIDPixels);
            junctionPixels = junctionDetector.DetectJunctionPixels();

            // Re-initialize median filter processor with detected junctions
            medianFilterProcessor = new MedianFilterProcessor(mapWidth, mapHeight, junctionPixels);

            // Initialize chain merger with junction data
            chainMerger = new BorderChainMerger(mapWidth, mapHeight, provinceIDPixels, junctionPixels);

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

                        // Chain border pixels into polylines using MedianFilterProcessor
                        List<List<Vector2>> allChains = medianFilterProcessor.ChainBorderPixelsMultiple(borderPixels);
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

                        // Merge significant chains with self-intersection detection using BorderChainMerger
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
                            // Multiple significant chains - merge using BorderChainMerger
                            mergedPath = chainMerger.MergeChainsSimple(significantChains, provinceA, provinceB);
                        }

                        // DEBUG: Log pre-smoothing data (ONLY FIRST 3 BORDERS to avoid spam)
                        bool enableDebugLog = (processedBorders < 3);

                        if (enableDebugLog)
                        {
                            ArchonLogger.Log($"SMOOTHING DEBUG - Border {provinceA}-{provinceB} (#{processedBorders + 1}):", "map_initialization");
                            ArchonLogger.Log($"  Merged: {mergedPath.Count} points", "map_initialization");
                        }

                        // CRITICAL: Simplify the pixel-perfect path using BorderPolylineSimplifier
                        List<Vector2> simplifiedPath = BorderPolylineSimplifier.SimplifyPolyline(mergedPath, epsilon: 1.5f);

                        if (enableDebugLog)
                        {
                            ArchonLogger.Log($"  Simplified: {simplifiedPath.Count} points", "map_initialization");
                            if (simplifiedPath.Count >= 3)
                            {
                                ArchonLogger.Log($"  First 3 points: ({simplifiedPath[0].x:F2},{simplifiedPath[0].y:F2}), ({simplifiedPath[1].x:F2},{simplifiedPath[1].y:F2}), ({simplifiedPath[2].x:F2},{simplifiedPath[2].y:F2})", "map_initialization");
                            }
                        }

                        // CRITICAL: Apply Chaikin smoothing using BorderPolylineSimplifier
                        List<Vector2> smoothedPath = BorderPolylineSimplifier.SmoothCurve(simplifiedPath, smoothingIterations, enableDebugLog);

                        // DEBUG: Log post-smoothing data
                        if (enableDebugLog)
                        {
                            ArchonLogger.Log($"  Post-smooth: {smoothedPath.Count} points (iterations: {smoothingIterations})", "map_initialization");
                            if (smoothedPath.Count >= 3)
                            {
                                ArchonLogger.Log($"  First 3 points: ({smoothedPath[0].x:F2},{smoothedPath[0].y:F2}), ({smoothedPath[1].x:F2},{smoothedPath[1].y:F2}), ({smoothedPath[2].x:F2},{smoothedPath[2].y:F2})", "map_initialization");
                            }
                        }

                        // CRITICAL: Tessellate smoothed path using BorderPolylineSimplifier
                        smoothedPath = BorderPolylineSimplifier.TessellatePolyline(smoothedPath, maxSegmentLength: 0.5f);

                        if (enableDebugLog)
                        {
                            ArchonLogger.Log($"  Post-tessellation: {smoothedPath.Count} points", "map_initialization");
                        }

                        // Check if final smoothed path self-intersects using BorderGeometryUtils
                        if (BorderGeometryUtils.PathSelfIntersects(smoothedPath))
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

            // POST-PROCESSING: Snap polyline endpoints at junctions using JunctionDetector
            junctionDetector.SnapPolylineEndpointsAtJunctions(borderPolylines, junctionPixels);

            if (logProgress)
            {
                ArchonLogger.Log($"BorderCurveExtractor: Total time: {(Time.realtimeSinceStartup - startTime) * 1000f:F0}ms", "map_initialization");
            }

            return borderPolylines;
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
                if (HasNeighborProvince(x, y, provinceB))
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
        /// Check if pixel at (x,y) has a neighbor with target province ID
        /// Uses 8-connectivity (includes diagonals) to match chaining algorithm
        /// </summary>
        private bool HasNeighborProvince(int x, int y, ushort targetProvince)
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
                ushort neighborProvince = Province.ProvinceIDEncoder.UnpackProvinceID(provinceIDPixels[neighborIndex]);
                if (neighborProvince == targetProvince)
                    return true;
            }

            return false;
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
