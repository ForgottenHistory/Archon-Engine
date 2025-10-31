using System.Collections.Generic;
using UnityEngine;
using Utils;

namespace Map.Rendering.Border
{
    /// <summary>
    /// Utilities for simplifying and smoothing polylines
    /// Extracted from BorderCurveExtractor for single responsibility
    ///
    /// Methods:
    /// - SimplifyPolyline: Ramer-Douglas-Peucker algorithm to reduce vertex count
    /// - SmoothCurve: Chaikin subdivision smoothing algorithm
    /// - TessellatePolyline: Subdivide long segments for dense vertex coverage
    /// </summary>
    public static class BorderPolylineSimplifier
    {
        /// <summary>
        /// Simplify polyline using Ramer-Douglas-Peucker algorithm
        /// Reduces pixel-perfect staircase to longer line segments that Chaikin can smooth
        /// </summary>
        /// <param name="points">Input polyline points</param>
        /// <param name="epsilon">Maximum distance from line for point to be removed</param>
        /// <returns>Simplified polyline with fewer points</returns>
        public static List<Vector2> SimplifyPolyline(List<Vector2> points, float epsilon)
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
        /// Smooth polyline using Chaikin subdivision algorithm
        /// Preserves original endpoints to maintain junction connections
        /// </summary>
        /// <param name="points">Input polyline points</param>
        /// <param name="iterations">Number of smoothing iterations (more = smoother)</param>
        /// <param name="enableDebugLog">Log smoothing progress</param>
        /// <returns>Smoothed polyline with more points</returns>
        public static List<Vector2> SmoothCurve(List<Vector2> points, int iterations, bool enableDebugLog = false)
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
        /// <param name="points">Input polyline points</param>
        /// <param name="maxSegmentLength">Maximum allowed segment length before subdivision</param>
        /// <returns>Tessellated polyline with denser vertex coverage</returns>
        public static List<Vector2> TessellatePolyline(List<Vector2> points, float maxSegmentLength)
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
        /// Calculate perpendicular distance from point to line segment
        /// Used by SimplifyPolyline for RDP algorithm
        /// </summary>
        private static float PerpendicularDistance(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
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
    }
}
