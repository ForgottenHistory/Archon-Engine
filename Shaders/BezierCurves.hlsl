// BezierCurves.hlsl
// Bézier curve evaluation and distance calculation functions
// Used for resolution-independent vector border rendering

#ifndef BEZIER_CURVES_INCLUDED
#define BEZIER_CURVES_INCLUDED

// Bézier segment structure matching C# BezierSegment
struct BezierSegment
{
    float2 P0;  // Start point
    float2 P1;  // First control point
    float2 P2;  // Second control point
    float2 P3;  // End point

    int borderType;      // BorderType enum: 0=None, 1=Province, 2=Country
    uint provinceID1;    // First province (stored as 2 ushorts packed)
    uint provinceID2;    // Second province
};

/// <summary>
/// Evaluate cubic Bézier curve at parameter t (0-1)
/// B(t) = (1-t)³·P0 + 3(1-t)²t·P1 + 3(1-t)t²·P2 + t³·P3
/// </summary>
float2 EvaluateBezier(BezierSegment seg, float t)
{
    float u = 1.0 - t;
    float tt = t * t;
    float uu = u * u;
    float uuu = uu * u;
    float ttt = tt * t;

    // Cubic Bézier formula
    float2 pt = uuu * seg.P0;
    pt += 3.0 * uu * t * seg.P1;
    pt += 3.0 * u * tt * seg.P2;
    pt += ttt * seg.P3;

    return pt;
}

/// <summary>
/// Evaluate first derivative of Bézier curve at parameter t
/// B'(t) = 3(1-t)²(P1-P0) + 6(1-t)t(P2-P1) + 3t²(P3-P2)
/// Used for finding closest point
/// </summary>
float2 EvaluateBezierDerivative(BezierSegment seg, float t)
{
    float u = 1.0 - t;
    float uu = u * u;
    float tt = t * t;

    // First derivative of cubic Bézier
    float2 derivative = 3.0 * uu * (seg.P1 - seg.P0);
    derivative += 6.0 * u * t * (seg.P2 - seg.P1);
    derivative += 3.0 * tt * (seg.P3 - seg.P2);

    return derivative;
}

/// <summary>
/// Evaluate second derivative of Bézier curve at parameter t
/// B''(t) = 6(1-t)(P2-2P1+P0) + 6t(P3-2P2+P1)
/// Used for Newton-Raphson refinement
/// </summary>
float2 EvaluateBezierSecondDerivative(BezierSegment seg, float t)
{
    float u = 1.0 - t;

    float2 secondDerivative = 6.0 * u * (seg.P2 - 2.0 * seg.P1 + seg.P0);
    secondDerivative += 6.0 * t * (seg.P3 - 2.0 * seg.P2 + seg.P1);

    return secondDerivative;
}

/// <summary>
/// Find parameter t (0-1) of closest point on Bézier curve to given position
/// Uses Newton-Raphson iteration for accuracy
/// </summary>
float FindClosestT(float2 pos, BezierSegment seg)
{
    // Initial guess: sample at 5 points and pick closest
    float bestT = 0.0;
    float minDistSq = 1e10;

    for (int i = 0; i <= 4; i++)
    {
        float t = i / 4.0;
        float2 curvePoint = EvaluateBezier(seg, t);
        float distSq = dot(curvePoint - pos, curvePoint - pos);

        if (distSq < minDistSq)
        {
            minDistSq = distSq;
            bestT = t;
        }
    }

    // Refine with Newton-Raphson (5 iterations for accuracy)
    for (int iter = 0; iter < 5; iter++)
    {
        float2 curvePoint = EvaluateBezier(seg, bestT);
        float2 derivative = EvaluateBezierDerivative(seg, bestT);

        // Vector from curve to query point
        float2 delta = pos - curvePoint;

        // Newton step: t_new = t - f(t)/f'(t)
        // f(t) = (B(t) - pos) · B'(t) = 0 at closest point
        float numerator = dot(delta, derivative);
        float denominator = dot(derivative, derivative) - dot(delta, EvaluateBezierSecondDerivative(seg, bestT));

        if (abs(denominator) > 1e-6)
        {
            bestT += numerator / denominator;
            bestT = clamp(bestT, 0.0, 1.0);
        }
        else
        {
            break; // Converged or degenerate
        }
    }

    return bestT;
}

/// <summary>
/// Calculate minimum distance from point to Bézier curve segment
/// Returns distance in pixels
/// </summary>
float DistanceToBezier(float2 pos, BezierSegment seg)
{
    // Find parameter of closest point
    float t = FindClosestT(pos, seg);

    // Evaluate curve at that parameter
    float2 closestPt = EvaluateBezier(seg, t);

    // Return Euclidean distance
    return length(pos - closestPt);
}

/// <summary>
/// Fast approximate distance to Bézier curve using control polygon
/// Much faster than accurate distance, useful for early-out tests
/// </summary>
float ApproximateDistanceToBezier(float2 pos, BezierSegment seg)
{
    // Distance to control polygon (4 line segments)
    float minDist = 1e10;

    // Check distance to each control polygon edge
    float2 edges[4];
    edges[0] = seg.P0;
    edges[1] = seg.P1;
    edges[2] = seg.P2;
    edges[3] = seg.P3;

    for (int i = 0; i < 3; i++)
    {
        float2 a = edges[i];
        float2 b = edges[i + 1];

        // Point-to-line-segment distance
        float2 ab = b - a;
        float2 ap = pos - a;
        float t = clamp(dot(ap, ab) / dot(ab, ab), 0.0, 1.0);
        float2 closestPt = a + t * ab;
        float dist = length(pos - closestPt);

        minDist = min(minDist, dist);
    }

    return minDist;
}

/// <summary>
/// Check if point is near Bézier curve (within threshold)
/// Uses fast approximation first, then accurate calculation if needed
/// </summary>
bool IsNearBezier(float2 pos, BezierSegment seg, float threshold)
{
    // Early out with fast approximation
    float approxDist = ApproximateDistanceToBezier(pos, seg);
    if (approxDist > threshold * 1.5) // 1.5x safety margin
        return false;

    // Accurate distance calculation
    float exactDist = DistanceToBezier(pos, seg);
    return exactDist <= threshold;
}

#endif // BEZIER_CURVES_INCLUDED
