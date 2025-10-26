using System.Collections.Generic;
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
                        // Chain pixels into ordered polyline (CRITICAL for smooth curves!)
                        List<Vector2> orderedPath = ChainBorderPixels(borderPixels);

                        // Smooth the pixel chain before curve fitting
                        // Smoothing helps Bézier fitting by reducing noise in pixel positions
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
                        // This creates parametric curves that can be evaluated at any resolution
                        BorderType borderType = DetermineBorderType(provinceA, provinceB);
                        List<BezierSegment> curveSegments = BezierCurveFitter.FitCurve(smoothedPath, borderType);

                        // Set province IDs for each segment
                        for (int segIdx = 0; segIdx < curveSegments.Count; segIdx++)
                        {
                            BezierSegment seg = curveSegments[segIdx];
                            seg.provinceID1 = provinceA;
                            seg.provinceID2 = provinceB;
                            curveSegments[segIdx] = seg;
                        }

                        // DEBUG: Log first curve stats
                        if (processedBorders == 0)
                        {
                            string rawSample = "";
                            for (int j = 0; j < Mathf.Min(5, borderPixels.Count); j++)
                            {
                                rawSample += $"({borderPixels[j].x:F0},{borderPixels[j].y:F0}) ";
                            }
                            string chainedSample = "";
                            for (int k = 0; k < Mathf.Min(5, orderedPath.Count); k++)
                            {
                                chainedSample += $"({orderedPath[k].x:F0},{orderedPath[k].y:F0}) ";
                            }
                            string smoothedSample = "";
                            for (int m = 0; m < Mathf.Min(5, smoothedPath.Count); m++)
                            {
                                smoothedSample += $"({smoothedPath[m].x:F2},{smoothedPath[m].y:F2}) ";
                            }
                            ArchonLogger.Log($"BorderCurveExtractor: First curve - Raw pixels: {borderPixels.Count}, After chaining: {orderedPath.Count}, After smoothing: {smoothedPath.Count}, Bézier segments: {curveSegments.Count}", "map_initialization");
                            ArchonLogger.Log($"  Raw sample: {rawSample}", "map_initialization");
                            ArchonLogger.Log($"  Chained sample: {chainedSample}", "map_initialization");
                            ArchonLogger.Log($"  Smoothed sample: {smoothedSample}", "map_initialization");
                            if (curveSegments.Count > 0)
                            {
                                BezierSegment firstSeg = curveSegments[0];
                                ArchonLogger.Log($"  First Bézier segment: P0=({firstSeg.P0.x:F2},{firstSeg.P0.y:F2}) P1=({firstSeg.P1.x:F2},{firstSeg.P1.y:F2}) P2=({firstSeg.P2.x:F2},{firstSeg.P2.y:F2}) P3=({firstSeg.P3.x:F2},{firstSeg.P3.y:F2})", "map_initialization");
                            }
                        }

                        // Cache the curve segments
                        borderCurves[(provinceA, provinceB)] = curveSegments;
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
                ArchonLogger.Log($"BorderCurveExtractor: Completed! Extracted {processedBorders} border curves in {elapsed:F0}ms ({elapsed / processedBorders:F2}ms per border)", "map_initialization");
            }

            return borderCurves;
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
            // This is O(pixelsA) instead of O(width×height)!
            foreach (var pixel in pixelsA)
            {
                int x = pixel.x;
                int y = pixel.y;

                // Check 4-neighbors for Province B
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
        /// Chain scattered border pixels into an ordered polyline
        /// Uses greedy nearest-neighbor to create connected path
        /// </summary>
        private List<Vector2> ChainBorderPixels(List<Vector2> scatteredPixels)
        {
            if (scatteredPixels.Count < 2)
                return scatteredPixels;

            List<Vector2> orderedPath = new List<Vector2>(scatteredPixels.Count);
            HashSet<Vector2> remaining = new HashSet<Vector2>(scatteredPixels);

            // Start with first pixel
            Vector2 current = scatteredPixels[0];
            orderedPath.Add(current);
            remaining.Remove(current);

            // Greedy nearest-neighbor chaining
            while (remaining.Count > 0)
            {
                Vector2 nearest = Vector2.zero;
                float minDistSq = float.MaxValue;

                // Find nearest remaining pixel (8-connectivity)
                foreach (var candidate in remaining)
                {
                    float dx = candidate.x - current.x;
                    float dy = candidate.y - current.y;
                    float distSq = dx * dx + dy * dy;

                    // Prioritize adjacent pixels (8-neighbors)
                    if (distSq <= 2.0f) // sqrt(2) for diagonal neighbors
                    {
                        nearest = candidate;
                        minDistSq = distSq;
                        break; // Found adjacent pixel, use it immediately
                    }
                    else if (distSq < minDistSq)
                    {
                        nearest = candidate;
                        minDistSq = distSq;
                    }
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
        /// </summary>
        private bool HasNeighborProvince(int x, int y, ushort targetProvince, Color32[] pixels, int mapWidth, int mapHeight)
        {
            // Check 4-neighbors
            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };

            for (int i = 0; i < 4; i++)
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
        /// Smooth a polyline using Chaikin's corner-cutting algorithm
        /// Iteratively rounds corners by replacing each segment with two shorter segments
        /// </summary>
        private List<Vector2> SmoothCurve(List<Vector2> points, int iterations)
        {
            if (points.Count < 3)
                return points; // Can't smooth lines with less than 3 points

            List<Vector2> smoothed = new List<Vector2>(points);

            for (int iter = 0; iter < iterations; iter++)
            {
                List<Vector2> newPoints = new List<Vector2>(smoothed.Count * 2);

                // Keep first point
                newPoints.Add(smoothed[0]);

                // Apply Chaikin smoothing to each segment
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

                // Keep last point
                newPoints.Add(smoothed[smoothed.Count - 1]);

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
