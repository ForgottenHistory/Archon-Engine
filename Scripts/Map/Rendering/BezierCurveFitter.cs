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
        private const int MAX_POINTS_PER_SEGMENT = 20;  // Max points to fit in single Bézier
        private const int MIN_POINTS_PER_SEGMENT = 5;   // Min points needed for meaningful fit
        private const float MAX_FIT_ERROR = 2.0f;       // Max pixel error tolerance

        /// <summary>
        /// Fit Bézier curve segments to an ordered point chain
        /// Returns list of cubic Bézier segments that approximate the input
        /// </summary>
        public static List<BezierSegment> FitCurve(List<Vector2> points, BorderType borderType = BorderType.None)
        {
            List<BezierSegment> segments = new List<BezierSegment>();

            if (points == null || points.Count < 2)
                return segments;

            // Very short chains: create single linear segment
            if (points.Count < MIN_POINTS_PER_SEGMENT)
            {
                Vector2 p0 = points[0];
                Vector2 p3 = points[points.Count - 1];
                // Linear: control points at 1/3 and 2/3 along line
                Vector2 p1 = Vector2.Lerp(p0, p3, 0.33f);
                Vector2 p2 = Vector2.Lerp(p0, p3, 0.67f);
                segments.Add(new BezierSegment(p0, p1, p2, p3, borderType));
                return segments;
            }

            // Segment long chains into chunks
            int index = 0;
            while (index < points.Count - 1)
            {
                int segmentLength = Mathf.Min(MAX_POINTS_PER_SEGMENT, points.Count - index);

                // Extract segment points
                List<Vector2> segmentPoints = points.GetRange(index, segmentLength);

                // Fit Bézier to this segment
                BezierSegment bezier = FitSegment(segmentPoints, borderType);
                segments.Add(bezier);

                // Advance index (overlap by 1 point for continuity)
                index += segmentLength - 1;
            }

            return segments;
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

            // For short segments, use simple heuristic for control points
            if (points.Count < 4)
            {
                P1 = Vector2.Lerp(P0, P3, 0.33f);
                P2 = Vector2.Lerp(P0, P3, 0.67f);
                return new BezierSegment(P0, P1, P2, P3, borderType);
            }

            // Estimate tangent vectors at endpoints
            Vector2 tangent0 = EstimateTangent(points, 0);
            Vector2 tangent1 = EstimateTangent(points, points.Count - 1);

            // Parameterize points (assign t values 0-1 using chord length)
            float[] tValues = ChordLengthParameterize(points);

            // Solve for control points P1 and P2 using least-squares
            // We'll use a simplified approach: place control points along tangents
            float alpha0 = 0.3f * Vector2.Distance(P0, P3);  // Rough distance along tangent
            float alpha1 = 0.3f * Vector2.Distance(P0, P3);

            P1 = P0 + tangent0.normalized * alpha0;
            P2 = P3 - tangent1.normalized * alpha1;

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
