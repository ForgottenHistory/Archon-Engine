using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// Bézier curve segment with 4 control points (cubic Bézier)
    /// Used for resolution-independent vector border rendering
    ///
    /// CRITICAL: Memory layout MUST match HLSL BezierSegment struct exactly!
    /// HLSL expects: float2 P0-P3 (32 bytes), int borderType (4), uint provinceID1 (4), uint provinceID2 (4)
    /// Total: 44 bytes
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct BezierSegment
    {
        public Vector2 P0;  // Start point (8 bytes, offset 0)
        public Vector2 P1;  // First control point (8 bytes, offset 8)
        public Vector2 P2;  // Second control point (8 bytes, offset 16)
        public Vector2 P3;  // End point (8 bytes, offset 24)

        // Metadata for border classification
        public BorderType borderType;  // 4 bytes (offset 32) - enum stored as int
        public uint provinceID1;       // 4 bytes (offset 36) - MUST be uint to match HLSL
        public uint provinceID2;       // 4 bytes (offset 40) - MUST be uint to match HLSL

        public BezierSegment(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, BorderType type = BorderType.None)
        {
            P0 = p0;
            P1 = p1;
            P2 = p2;
            P3 = p3;
            borderType = type;
            provinceID1 = 0;
            provinceID2 = 0;
        }

        /// <summary>
        /// Evaluate Bézier curve at parameter t (0-1)
        /// </summary>
        public Vector2 Evaluate(float t)
        {
            float u = 1f - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            // B(t) = (1-t)³·P0 + 3(1-t)²t·P1 + 3(1-t)t²·P2 + t³·P3
            Vector2 point = uuu * P0;
            point += 3f * uu * t * P1;
            point += 3f * u * tt * P2;
            point += ttt * P3;

            return point;
        }

        /// <summary>
        /// Approximate arc length of curve (for error metrics)
        /// </summary>
        public float ApproximateLength()
        {
            // Use control polygon length as upper bound approximation
            float chord = Vector2.Distance(P0, P3);
            float controlNet = Vector2.Distance(P0, P1) + Vector2.Distance(P1, P2) + Vector2.Distance(P2, P3);
            return (chord + controlNet) / 2f;
        }
    }

    /// <summary>
    /// Fits cubic Bézier curves to ordered point chains using least-squares approximation
    ///
    /// Algorithm: Segments long chains into chunks, fits each chunk to minimize error
    /// Result: Smooth parametric curves that can be rendered at any resolution
    ///
    /// Reference: "An Algorithm for Automatically Fitting Digitized Curves"
    ///            by Philip J. Schneider (Graphics Gems, 1990)
    /// </summary>
    public static class BezierCurveFitter
    {
        private const int MAX_POINTS_PER_SEGMENT = 20;  // Shorter segments for accuracy
        private const int MIN_POINTS_PER_SEGMENT = 4;   // Lower minimum to handle short borders
        private const float MAX_FIT_ERROR = 1.5f;       // Tight error tolerance for accurate borders

        private const float ENDPOINT_QUANTIZATION = 0.5f; // Snap endpoints to 0.5px grid for junction alignment

        /// <summary>
        /// Fit Bézier curves to pixel chain using adaptive segmentation
        /// Creates smooth curves that approximate the pixel path
        /// Uses recursive splitting to ensure accuracy within MAX_FIT_ERROR
        /// Quantizes endpoints to ensure perfect junction connections
        /// </summary>
        public static List<BezierSegment> FitCurve(List<Vector2> points, BorderType borderType = BorderType.None)
        {
            List<BezierSegment> segments = new List<BezierSegment>();

            if (points == null || points.Count < 2)
                return segments;

            // Quantize first and last points to grid for junction alignment
            points[0] = QuantizePoint(points[0], ENDPOINT_QUANTIZATION);
            points[points.Count - 1] = QuantizePoint(points[points.Count - 1], ENDPOINT_QUANTIZATION);

            if (points.Count < MIN_POINTS_PER_SEGMENT)
            {
                // Too few points - create single segment
                segments.Add(FitSegment(points, borderType));
                return segments;
            }

            // Use adaptive recursive fitting to ensure accuracy
            FitCurveAdaptive(points, 0, points.Count - 1, segments, borderType);

            return segments;
        }

        /// <summary>
        /// Quantize point to grid for endpoint alignment
        /// </summary>
        private static Vector2 QuantizePoint(Vector2 point, float gridSize)
        {
            return new Vector2(
                Mathf.Round(point.x / gridSize) * gridSize,
                Mathf.Round(point.y / gridSize) * gridSize
            );
        }

        /// <summary>
        /// Recursively fit curve segments with adaptive splitting based on error
        /// </summary>
        private static void FitCurveAdaptive(List<Vector2> points, int first, int last, List<BezierSegment> segments, BorderType borderType)
        {
            // Extract segment points
            List<Vector2> segmentPoints = new List<Vector2>();
            for (int i = first; i <= last; i++)
            {
                segmentPoints.Add(points[i]);
            }

            // Fit a curve to this segment
            BezierSegment segment = FitSegment(segmentPoints, borderType);

            // Check error
            float maxError = CalculateMaxError(segment, segmentPoints);

            // If error acceptable OR segment too short to split, accept it
            if (maxError <= MAX_FIT_ERROR || (last - first) < MIN_POINTS_PER_SEGMENT)
            {
                segments.Add(segment);

                // Log if error is high but we had to accept it
                if (maxError > MAX_FIT_ERROR)
                {
                    ArchonLogger.LogWarning($"BezierCurveFitter: Accepted segment with high error {maxError:F2}px (too short to split, {last - first + 1} points)", "map_rendering");
                }

                return;
            }

            // Error too high - split in half and recurse
            int mid = first + (last - first) / 2;
            FitCurveAdaptive(points, first, mid, segments, borderType);
            FitCurveAdaptive(points, mid, last, segments, borderType);
        }

        /// <summary>
        /// Fit a single cubic Bézier curve to a point segment using least-squares
        /// </summary>
        private static BezierSegment FitSegment(List<Vector2> points, BorderType borderType)
        {
            if (points.Count < 2)
            {
                // Degenerate case
                Vector2 p = points[0];
                return new BezierSegment(p, p, p, p, borderType);
            }

            // P0 and P3 are fixed (endpoints)
            Vector2 P0 = points[0];
            Vector2 P3 = points[points.Count - 1];

            // Declare control points
            Vector2 P1;
            Vector2 P2;

            // For short segments OR polyline-style rendering, use minimal curvature
            // Control points very close to endpoints = almost straight line with slight smoothing
            if (points.Count < 4)
            {
                // Straight line with tiny control point offset (5% from endpoints)
                P1 = Vector2.Lerp(P0, P3, 0.05f);
                P2 = Vector2.Lerp(P0, P3, 0.95f);
                return new BezierSegment(P0, P1, P2, P3, borderType);
            }

            // Use OVERALL segment direction for tangents (prevents overshoot at junctions)
            // Local pixel-to-pixel tangents can be misaligned with overall segment direction
            Vector2 overallDirection = (P3 - P0).normalized;

            // Use MINIMAL tangent influence for gentle curves (not aggressive Bézier curves)
            // 0.15x segment length = subtle corner rounding, mostly straight
            float segmentLength = Vector2.Distance(P0, P3);
            float alpha0 = 0.15f * segmentLength; // Reduced from 0.4x
            float alpha1 = 0.15f * segmentLength;

            // Control points aligned with overall segment direction (no perpendicular offset)
            P1 = P0 + overallDirection * alpha0;
            P2 = P3 - overallDirection * alpha1;

            return new BezierSegment(P0, P1, P2, P3, borderType);
        }

        /// <summary>
        /// Estimate tangent vector at a point in the chain
        /// </summary>
        private static Vector2 EstimateTangent(List<Vector2> points, int index)
        {
            if (index == 0)
            {
                // Start: use forward difference
                return (points[1] - points[0]).normalized;
            }
            else if (index == points.Count - 1)
            {
                // End: use backward difference
                return (points[index] - points[index - 1]).normalized;
            }
            else
            {
                // Middle: use central difference
                return (points[index + 1] - points[index - 1]).normalized;
            }
        }

        /// <summary>
        /// Assign parameter values (0-1) to points using chord length
        /// </summary>
        private static float[] ChordLengthParameterize(List<Vector2> points)
        {
            float[] tValues = new float[points.Count];
            tValues[0] = 0f;

            // Accumulate chord lengths
            float totalLength = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                totalLength += Vector2.Distance(points[i - 1], points[i]);
                tValues[i] = totalLength;
            }

            // Normalize to [0, 1]
            if (totalLength > 0f)
            {
                for (int i = 1; i < points.Count; i++)
                {
                    tValues[i] /= totalLength;
                }
            }

            return tValues;
        }

        /// <summary>
        /// Calculate maximum error between fitted curve and original points
        /// Used for adaptive refinement (future enhancement)
        /// </summary>
        public static float CalculateMaxError(BezierSegment segment, List<Vector2> points)
        {
            if (points.Count == 0)
                return 0f;

            float maxError = 0f;
            float[] tValues = ChordLengthParameterize(points);

            for (int i = 0; i < points.Count; i++)
            {
                Vector2 curvePoint = segment.Evaluate(tValues[i]);
                float error = Vector2.Distance(curvePoint, points[i]);
                maxError = Mathf.Max(maxError, error);
            }

            return maxError;
        }
    }
}
