using System.Collections.Generic;
using UnityEngine;
using Map.Rendering.Border;

namespace Map.Rendering.Rivers
{
    /// <summary>
    /// ENGINE: Traces a river map (one-pixel-wide lines, a width level per pixel) into smoothed
    /// polylines through pixel centres, with a width level per point.
    ///
    /// Connectivity: 4-neighbours always connect; a diagonal neighbour only connects when neither
    /// orthogonal pixel between them is river (so L-bends are not junctions). Nodes are pixels with
    /// degree != 2 (sources, mouths, confluences); arcs run node to node, so tributaries share the
    /// confluence pixel centre exactly. Remaining pixels form loops.
    /// </summary>
    public static class RiverGraph
    {
        private sealed class MixedLongComparer : IEqualityComparer<long>
        {
            public static readonly MixedLongComparer Instance = new MixedLongComparer();
            public bool Equals(long a, long b) => a == b;
            public int GetHashCode(long x) => (int)(((ulong)x * 0x9E3779B97F4A7C15UL) >> 32);
        }

        /// <param name="levels">width level per pixel (index y * width + x, y down), 0 = no river</param>
        /// <summary>Timing breakdown of the last Build call (for the load log).</summary>
        public static string LastTimings { get; private set; } = "";

        public static List<BorderArc> Build(byte[] levels, int width, int height, BorderSmoothingSettings smoothing)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long smoothTicks = 0;
            int W = width, H = height;
            bool IsRiver(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && levels[y * W + x] != 0;

            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

            // Neighbour list per river pixel (max 8), as pixel indices
            var neighbours = new Dictionary<int, List<int>>();
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    if (levels[y * W + x] == 0) continue;
                    var list = new List<int>(2);
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + dx[k], ny = y + dy[k];
                        if (!IsRiver(nx, ny)) continue;
                        if (k >= 4 && (IsRiver(nx, y) || IsRiver(x, ny))) continue; // diagonal only if not bridged
                        list.Add(ny * W + nx);
                    }
                    neighbours[y * W + x] = list;
                }
            }

            long tScan = sw.ElapsedMilliseconds;
            // Edge keys pack two neighbouring pixel indices; long's own hash (hi ^ lo) collapses them
            // to a few values (neighbours differ by 1 or ~W), turning lookups quadratic (6 s on 88k
            // river pixels). Mix the bits instead.
            var visited = new HashSet<long>(MixedLongComparer.Instance);
            long Edge(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            var arcs = new List<BorderArc>();

            List<int> Walk(int start, int first, bool loop)
            {
                var path = new List<int> { start };
                int prev = start, cur = first;
                visited.Add(Edge(prev, cur));
                while (true)
                {
                    path.Add(cur);
                    if (loop ? cur == start : neighbours[cur].Count != 2)
                        break;
                    int next = -1;
                    foreach (int q in neighbours[cur])
                        if (q != prev && !visited.Contains(Edge(cur, q))) { next = q; break; }
                    if (next < 0)
                        break;
                    visited.Add(Edge(cur, next));
                    prev = cur;
                    cur = next;
                }
                return path;
            }

            void Emit(List<int> path, bool closed)
            {
                var pts = new List<Vector3>(path.Count);
                foreach (int p in path)
                    pts.Add(new Vector3(p % W + 0.5f, p / W + 0.5f, levels[p]));
                long s0 = sw.ElapsedTicks;
                var smoothed = Smooth(pts, smoothing);
                smoothTicks += sw.ElapsedTicks - s0;
                var arc = new BorderArc
                {
                    Closed = closed,
                    Points = new Vector2[smoothed.Count],
                    Levels = new float[smoothed.Count]
                };
                for (int i = 0; i < smoothed.Count; i++)
                {
                    arc.Points[i] = new Vector2(smoothed[i].x, smoothed[i].y);
                    arc.Levels[i] = smoothed[i].z;
                }
                arcs.Add(arc);
            }

            foreach (var kvp in neighbours)
            {
                if (kvp.Value.Count == 2) continue; // nodes only
                foreach (int n in kvp.Value)
                    if (!visited.Contains(Edge(kvp.Key, n)))
                        Emit(Walk(kvp.Key, n, false), false);
            }
            foreach (var kvp in neighbours)
            {
                foreach (int n in kvp.Value)
                    if (!visited.Contains(Edge(kvp.Key, n)))
                        Emit(Walk(kvp.Key, n, true), true);
            }
            long smoothMs = smoothTicks * 1000 / System.Diagnostics.Stopwatch.Frequency;
            LastTimings = $"scan {tScan}, walk {sw.ElapsedMilliseconds - tScan - smoothMs}, smooth {smoothMs}";
            return arcs;
        }

        // Same pipeline as borders (simplify, Chaikin, simplify; endpoints fixed), with the width
        // level carried as a third coordinate so it tapers smoothly.
        private static List<Vector3> Smooth(List<Vector3> pts, BorderSmoothingSettings s)
        {
            if (s.PreSimplifyTolerance > 0f) pts = Simplify(pts, s.PreSimplifyTolerance);
            for (int it = 0; it < s.ChaikinIterations && pts.Count >= 3; it++)
            {
                var r = new List<Vector3>(pts.Count * 2) { pts[0] };
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    Vector3 a = pts[i], b = pts[i + 1];
                    if (i > 0) r.Add(a * 0.75f + b * 0.25f);
                    if (i < pts.Count - 2) r.Add(a * 0.25f + b * 0.75f);
                }
                r.Add(pts[pts.Count - 1]);
                pts = r;
            }
            if (s.PostSimplifyTolerance > 0f) pts = Simplify(pts, s.PostSimplifyTolerance);
            return pts;
        }

        private static List<Vector3> Simplify(List<Vector3> p, float eps)
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
                Vector2 a = p[i], d = (Vector2)p[j] - a;
                float len = d.magnitude, best = -1f;
                int bestK = -1;
                for (int k = i + 1; k < j; k++)
                {
                    Vector2 v = (Vector2)p[k] - a;
                    float dist = len > 1e-6f ? Mathf.Abs(v.x * d.y - v.y * d.x) / len : v.magnitude;
                    // keep width changes too, so tapering survives simplification
                    float levelJump = Mathf.Abs(p[k].z - Mathf.Lerp(p[i].z, p[j].z, (k - i) / (float)(j - i)));
                    dist = Mathf.Max(dist, levelJump > 0.5f ? eps * 2f : 0f);
                    if (dist > best) { best = dist; bestK = k; }
                }
                if (best > eps)
                {
                    keep[bestK] = true;
                    stack.Push((i, bestK));
                    stack.Push((bestK, j));
                }
            }
            var r = new List<Vector3>();
            for (int k = 0; k < n; k++) if (keep[k]) r.Add(p[k]);
            return r;
        }
    }
}
