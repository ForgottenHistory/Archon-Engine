using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// Geometric utility functions for border processing
    /// Extracted from BorderCurveExtractor for reusability
    ///
    /// Contains:
    /// - Line segment intersection detection
    /// - Path self-intersection checking
    /// - Convex hull computation (Graham scan)
    /// - Distance calculations
    /// - Angle calculations
    /// </summary>
    public static class BorderGeometryUtils
    {
        /// <summary>
        /// Check if two line segments intersect (not just touch at endpoints)
        /// Uses cross product method for robust intersection detection
        /// </summary>
        public static bool LineSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
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
        /// Check if path self-intersects (detects U-turns)
        /// </summary>
        public static bool PathSelfIntersects(List<Vector2> path)
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
        /// Compute convex hull of points using Graham scan algorithm
        /// Returns hull vertices in counter-clockwise order
        /// </summary>
        public static List<Vector2> ComputeConvexHull(List<Vector2> points)
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
        /// Filter border pixels to convex hull perimeter, removing interior pixels
        /// This prevents U-turns caused by peninsula indents creating interior loops
        /// </summary>
        public static List<Vector2> FilterToConvexHullPerimeter(List<Vector2> pixels)
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
        /// Calculate angle in degrees at point B formed by line A-B-C
        /// Returns 0° for straight line, 180° for complete reversal
        /// </summary>
        public static float CalculateAngle(Vector2 pointA, Vector2 pointB, Vector2 pointC)
        {
            Vector2 vectorBA = pointA - pointB;
            Vector2 vectorBC = pointC - pointB;

            float dotProduct = Vector2.Dot(vectorBA.normalized, vectorBC.normalized);
            float angleRadians = Mathf.Acos(Mathf.Clamp(dotProduct, -1f, 1f));
            float angleDegrees = angleRadians * Mathf.Rad2Deg;

            return angleDegrees;
        }

        /// <summary>
        /// Compute distance from point to line segment
        /// </summary>
        public static float DistancePointToSegment(Vector2 point, Vector2 segStart, Vector2 segEnd)
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
        /// Check if point is on or near a polyline (within tolerance distance)
        /// </summary>
        public static bool IsPointOnOrNearPolyline(Vector2 point, List<Vector2> polyline, float tolerance)
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
        /// Cross product for Graham scan (determines turn direction)
        /// Positive = counter-clockwise, Negative = clockwise, Zero = collinear
        /// </summary>
        private static float CrossProduct(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }
    }
}
