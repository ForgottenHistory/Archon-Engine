using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// One border between two provinces: a polyline that runs junction to junction,
    /// or a closed loop (islands, enclaves, lakes). A province pair can own several arcs.
    /// Points are in map pixel space (x right, y down, matching the province ID readback).
    /// </summary>
    public sealed class BorderArc
    {
        public ushort ProvinceA;   // smaller ID
        public ushort ProvinceB;   // larger ID
        public bool Closed;        // closed loop: last point equals first
        public Vector2[] Points;   // smoothed polyline
    }

    /// <summary>
    /// Smoothing settings for BorderGraph.Build.
    /// Defaults were tuned in Tools/prototypes/borders on the HOI4 map.
    /// </summary>
    public struct BorderSmoothingSettings
    {
        /// <summary>RDP tolerance (px) applied before smoothing. Turns pixel steps into straight runs.</summary>
        public float PreSimplifyTolerance;
        /// <summary>Chaikin corner-cutting iterations. Endpoints (junctions) never move.</summary>
        public int ChaikinIterations;
        /// <summary>RDP tolerance (px) applied after smoothing, to drop redundant vertices.</summary>
        public float PostSimplifyTolerance;

        public static BorderSmoothingSettings Default => new BorderSmoothingSettings
        {
            PreSimplifyTolerance = 0.75f,
            ChaikinIterations = 3,
            PostSimplifyTolerance = 0.15f
        };
    }

    /// <summary>
    /// ENGINE: Extracts province borders as a planar graph by tracing the pixel-corner lattice.
    ///
    /// Every boundary edge lies between two pixels with different province IDs; pixel corners are
    /// the graph vertices. A corner touching 3+ boundary edges is a junction. Each arc runs from
    /// junction to junction and separates exactly one province pair, so neighbouring arcs share
    /// their endpoints exactly. Edges never reached from a junction form closed loops.
    ///
    /// Replaces the old pixel-chaining pipeline (median filter, chaining, chain merging, endpoint
    /// snapping): those existed to patch up topology that this construction gets right by itself.
    /// </summary>
    public static class BorderGraph
    {
        // Per-corner flags. Corner (cx, cy) for cx in [0, W], cy in [0, H].
        // Right edge: corner (cx,cy) -> (cx+1,cy), between pixels (cx,cy-1) and (cx,cy).
        // Down edge:  corner (cx,cy) -> (cx,cy+1), between pixels (cx-1,cy) and (cx,cy).
        private const byte RIGHT = 1, DOWN = 2, RIGHT_VISITED = 4, DOWN_VISITED = 8;

        /// <summary>
        /// Build the border graph from a province ID grid (index y * width + x).
        /// ID 0 is treated like any other ID; callers decide whether to draw borders touching it.
        /// </summary>
        public static List<BorderArc> Build(ushort[] ids, int width, int height, BorderSmoothingSettings smoothing)
        {
            var raw = Trace(ids, width, height);
            var arcs = new List<BorderArc>(raw.Count);
            foreach (var r in raw)
            {
                arcs.Add(new BorderArc
                {
                    ProvinceA = r.a,
                    ProvinceB = r.b,
                    Closed = r.closed,
                    Points = Smooth(r.points, r.closed, smoothing)
                });
            }
            return arcs;
        }

        private struct RawArc
        {
            public ushort a, b;
            public bool closed;
            public List<Vector2> points;
        }

        private static List<RawArc> Trace(ushort[] ids, int W, int H)
        {
            int CW = W + 1;
            var flags = new byte[CW * (H + 1)];

            for (int cy = 1; cy < H; cy++)
            {
                int row = cy * W, above = (cy - 1) * W, crow = cy * CW;
                for (int cx = 0; cx < W; cx++)
                    if (ids[above + cx] != ids[row + cx]) flags[crow + cx] |= RIGHT;
            }
            for (int cy = 0; cy < H; cy++)
            {
                int row = cy * W, crow = cy * CW;
                for (int cx = 1; cx < W; cx++)
                    if (ids[row + cx - 1] != ids[row + cx]) flags[crow + cx] |= DOWN;
            }

            int Degree(int cx, int cy)
            {
                int c = cy * CW + cx, d = 0;
                if ((flags[c] & RIGHT) != 0) d++;
                if ((flags[c] & DOWN) != 0) d++;
                if (cx > 0 && (flags[c - 1] & RIGHT) != 0) d++;
                if (cy > 0 && (flags[c - CW] & DOWN) != 0) d++;
                return d;
            }

            bool IsNode(int cx, int cy)
            {
                int d = Degree(cx, cy);
                return d >= 3 || d == 1;
            }

            // Directions: 0 right, 1 down, 2 left, 3 up. Returns the edge's flag owner corner and bits.
            bool HasUnvisited(int cx, int cy, int dir)
            {
                switch (dir)
                {
                    case 0: { int c = cy * CW + cx; return (flags[c] & (RIGHT | RIGHT_VISITED)) == RIGHT; }
                    case 1: { int c = cy * CW + cx; return (flags[c] & (DOWN | DOWN_VISITED)) == DOWN; }
                    case 2: { if (cx == 0) return false; int c = cy * CW + cx - 1; return (flags[c] & (RIGHT | RIGHT_VISITED)) == RIGHT; }
                    default: { if (cy == 0) return false; int c = (cy - 1) * CW + cx; return (flags[c] & (DOWN | DOWN_VISITED)) == DOWN; }
                }
            }

            // Marks the edge visited and returns the two pixel IDs it separates.
            (ushort, ushort) Mark(int cx, int cy, int dir)
            {
                switch (dir)
                {
                    case 0: flags[cy * CW + cx] |= RIGHT_VISITED; return (ids[(cy - 1) * W + cx], ids[cy * W + cx]);
                    case 1: flags[cy * CW + cx] |= DOWN_VISITED; return (ids[cy * W + cx - 1], ids[cy * W + cx]);
                    case 2: flags[cy * CW + cx - 1] |= RIGHT_VISITED; return (ids[(cy - 1) * W + cx - 1], ids[cy * W + cx - 1]);
                    default: flags[(cy - 1) * CW + cx] |= DOWN_VISITED; return (ids[(cy - 1) * W + cx - 1], ids[(cy - 1) * W + cx]);
                }
            }

            int[] DX = { 1, 0, -1, 0 };
            int[] DY = { 0, 1, 0, -1 };

            var arcs = new List<RawArc>();

            RawArc Walk(int cx, int cy, int dir, bool closedLoop)
            {
                int sx = cx, sy = cy;
                var (p, q) = Mark(cx, cy, dir);
                var pts = new List<Vector2> { new Vector2(cx, cy) };
                cx += DX[dir]; cy += DY[dir];
                while (true)
                {
                    pts.Add(new Vector2(cx, cy));
                    if (closedLoop ? (cx == sx && cy == sy) : IsNode(cx, cy))
                        break;
                    int next = -1;
                    for (int d = 0; d < 4; d++)
                        if (HasUnvisited(cx, cy, d)) { next = d; break; }
                    if (next < 0)
                        break;
                    Mark(cx, cy, next);
                    cx += DX[next]; cy += DY[next];
                }
                return new RawArc
                {
                    a = p < q ? p : q,
                    b = p < q ? q : p,
                    closed = closedLoop,
                    points = pts
                };
            }

            // Arcs starting at junctions (and dangling ends)
            for (int cy = 0; cy <= H; cy++)
            {
                for (int cx = 0; cx <= W; cx++)
                {
                    if ((flags[cy * CW + cx] & (RIGHT | DOWN)) == 0 &&
                        !(cx > 0 && (flags[cy * CW + cx - 1] & RIGHT) != 0) &&
                        !(cy > 0 && (flags[(cy - 1) * CW + cx] & DOWN) != 0))
                        continue;
                    if (!IsNode(cx, cy))
                        continue;
                    for (int d = 0; d < 4; d++)
                        if (HasUnvisited(cx, cy, d))
                            arcs.Add(Walk(cx, cy, d, false));
                }
            }

            // Remaining edges only pass through degree-2 corners: closed loops
            for (int cy = 0; cy <= H; cy++)
            {
                for (int cx = 0; cx <= W; cx++)
                {
                    if (HasUnvisited(cx, cy, 0)) arcs.Add(Walk(cx, cy, 0, true));
                    if (HasUnvisited(cx, cy, 1)) arcs.Add(Walk(cx, cy, 1, true));
                }
            }

            return arcs;
        }

        // ------------------------------------------------------------------ smoothing

        private static Vector2[] Smooth(List<Vector2> corners, bool closed, BorderSmoothingSettings s)
        {
            // Edge midpoints remove the staircase; open arcs keep their junction endpoints
            var pts = new List<Vector2>(corners.Count + 1);
            if (!closed) pts.Add(corners[0]);
            for (int i = 0; i < corners.Count - 1; i++)
                pts.Add((corners[i] + corners[i + 1]) * 0.5f);
            if (!closed) pts.Add(corners[corners.Count - 1]);

            if (s.PreSimplifyTolerance > 0f)
                pts = closed ? SimplifyClosed(pts, s.PreSimplifyTolerance) : Simplify(pts, s.PreSimplifyTolerance);

            for (int it = 0; it < s.ChaikinIterations && pts.Count >= 3; it++)
                pts = closed ? ChaikinClosed(pts) : ChaikinOpen(pts);

            if (closed)
                pts.Add(pts[0]);
            if (s.PostSimplifyTolerance > 0f)
                pts = Simplify(pts, s.PostSimplifyTolerance);
            return pts.ToArray();
        }

        private static List<Vector2> ChaikinOpen(List<Vector2> p)
        {
            var r = new List<Vector2>(p.Count * 2) { p[0] };
            for (int i = 0; i < p.Count - 1; i++)
            {
                Vector2 a = p[i], b = p[i + 1];
                if (i > 0) r.Add(a * 0.75f + b * 0.25f);
                if (i < p.Count - 2) r.Add(a * 0.25f + b * 0.75f);
            }
            r.Add(p[p.Count - 1]);
            return r;
        }

        private static List<Vector2> ChaikinClosed(List<Vector2> p)
        {
            var r = new List<Vector2>(p.Count * 2);
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Count];
                r.Add(a * 0.75f + b * 0.25f);
                r.Add(a * 0.25f + b * 0.75f);
            }
            return r;
        }

        private static List<Vector2> SimplifyClosed(List<Vector2> p, float eps)
        {
            p.Add(p[0]);
            var r = Simplify(p, eps);
            r.RemoveAt(r.Count - 1);
            return r.Count >= 3 ? r : p.GetRange(0, p.Count - 1);
        }

        /// <summary>Ramer-Douglas-Peucker; first and last points are always kept.</summary>
        private static List<Vector2> Simplify(List<Vector2> p, float eps)
        {
            int n = p.Count;
            if (n < 3) return p;
            var keep = new bool[n];
            keep[0] = keep[n - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, n - 1));
            while (stack.Count > 0)
            {
                var (i, j) = stack.Pop();
                if (j <= i + 1) continue;
                Vector2 a = p[i], d = p[j] - a;
                float len = d.magnitude;
                float best = -1f; int bestK = -1;
                for (int k = i + 1; k < j; k++)
                {
                    Vector2 v = p[k] - a;
                    float dist = len > 1e-6f ? Mathf.Abs(v.x * d.y - v.y * d.x) / len : v.magnitude;
                    if (dist > best) { best = dist; bestK = k; }
                }
                if (best > eps)
                {
                    keep[bestK] = true;
                    stack.Push((i, bestK));
                    stack.Push((bestK, j));
                }
            }
            var r = new List<Vector2>();
            for (int k = 0; k < n; k++) if (keep[k]) r.Add(p[k]);
            return r;
        }
    }
}
