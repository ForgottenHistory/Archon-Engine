using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Map.Rendering.Border;

namespace Map.Rendering
{
    /// <summary>
    /// Builds one mesh per border class from the classified border arcs.
    ///
    /// Each arc becomes a strip with three vertices per polyline point: left edge, center, right
    /// edge. All sit on the center line; the normal holds the world-space offset of one map pixel
    /// towards the right, and the vertex shader moves each vertex by (2v - 1) of the half width, so
    /// it can expand the strip to any width (map pixels, with a screen-pixel floor).
    /// UV: x = distance along the arc in map pixels, y = 0 (left) / 0.5 (center) / 1 (right),
    /// z = width level (BorderArc.Levels, 0 for borders; the shader adds WidthPerLevel per level).
    ///
    /// Why a center vertex: on the inside of a corner tighter than the half width, the strip edge
    /// folds back over itself. With only edge vertices, v = 0.5 (the visible line of a texture
    /// profile, the peak of a soft glow) then ran diagonally through the folded quads, out of the
    /// corner. With the center vertex, v = 0.5 is the center line exactly; folds only affect one
    /// half. Prototype: Tools/prototypes/highlight/outline_strip.py (MODE=old/new).
    ///
    /// Junction caps: where 2+ arcs of the same class meet, a fan connects their strip ends
    /// (center v = 0.5, outer v = 0), so the texture profile continues radially and fills the
    /// wedge between flat strip ends.
    /// </summary>
    public class BorderMeshGenerator
    {
        private const float MAX_SEGMENT_PIXELS = 3f; // subdivision so strips follow the heightmap
        private const float NORMAL_LOOKAHEAD_PIXELS = 1.5f; // strip normals ignore sub-pixel jogs (see Perpendicular)

        private readonly float mapWidthPixels;
        private readonly float mapHeightPixels;
        private readonly Vector3 worldMin;
        private readonly Vector3 worldSize;
        private readonly Vector2 worldPerPixel; // world units per map pixel in x and z

        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<int> meshClassIds = new List<int>();

        private class Builder
        {
            public readonly List<Vector3> verts = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector3> uvs = new List<Vector3>();
            public readonly List<int> tris = new List<int>();
            // Junction endpoints: corner -> (center, offset dir left, offset dir right)
            public readonly Dictionary<Vector2Int, List<(Vector3 center, Vector3 left, Vector3 right, float level)>> ends =
                new Dictionary<Vector2Int, List<(Vector3, Vector3, Vector3, float)>>();
        }

        public BorderMeshGenerator(float mapWidthPx, float mapHeightPx, Transform mapPlaneTransform)
        {
            mapWidthPixels = mapWidthPx;
            mapHeightPixels = mapHeightPx;

            var mr = mapPlaneTransform != null ? mapPlaneTransform.GetComponent<MeshRenderer>() : null;
            if (mr != null)
            {
                worldMin = mr.bounds.min;
                worldSize = mr.bounds.size;
            }
            else
            {
                worldMin = new Vector3(-5f, 0f, -5f);
                worldSize = new Vector3(10f, 0f, 10f);
            }
            worldPerPixel = new Vector2(worldSize.x / mapWidthPixels, worldSize.z / mapHeightPixels);
        }

        /// <summary>World units per map pixel (x, z).</summary>
        public Vector2 WorldPerPixel => worldPerPixel;

        public IReadOnlyList<Mesh> Meshes => meshes;
        public IReadOnlyList<int> MeshClassIds => meshClassIds;

        public void GenerateBorderMeshes(BorderCurveCache cache)
        {
            GenerateMeshes(cache.GetVisibleArcs(), cache.ClassNames);
        }

        /// <summary>Build one mesh per class id from (arc, class id) pairs.</summary>
        public void GenerateMeshes(IEnumerable<(BorderArc arc, int classId)> classifiedArcs, IReadOnlyList<string> classNames,
                                   bool log = true)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DestroyMeshes();

            var builders = new Dictionary<int, Builder>();
            int arcCount = 0;

            foreach (var (arc, classId) in classifiedArcs)
            {
                if (arc.Points.Length < 2) continue;
                if (!builders.TryGetValue(classId, out var b))
                    builders[classId] = b = new Builder();
                AddStrip(arc, b);
                arcCount++;
            }

            int caps = 0, verts = 0;
            foreach (var kvp in builders)
            {
                var b = kvp.Value;
                foreach (var end in b.ends)
                {
                    if (end.Value.Count < 2) continue;
                    AddJunctionCap(end.Value, b);
                    caps++;
                }

                var mesh = new Mesh { name = $"Lines {classNames[kvp.Key]}", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(b.verts);
                mesh.SetNormals(b.normals);
                mesh.SetUVs(0, b.uvs);
                mesh.SetTriangles(b.tris, 0, false);
                // Vertices move in the shader (width, height); generous bounds avoid wrong culling
                var bounds = new Bounds(worldMin + worldSize * 0.5f, new Vector3(worldSize.x, 1000f, worldSize.z));
                mesh.bounds = bounds;
                meshes.Add(mesh);
                meshClassIds.Add(kvp.Key);
                verts += b.verts.Count;
            }

            if (log)
                ArchonLogger.Log($"BorderMeshGenerator: {arcCount} arcs, {caps} junction caps, {verts} vertices " +
                             $"in {meshes.Count} class meshes ({sw.ElapsedMilliseconds}ms)", "map_initialization");
        }

        private void AddStrip(BorderArc arc, Builder b)
        {
            var pts = Subdivide(arc.Points, arc.Levels, out var levels);
            int n = pts.Count;
            int baseIndex = b.verts.Count;
            float along = 0f;

            for (int i = 0; i < n; i++)
            {
                if (i > 0) along += Vector2.Distance(pts[i], pts[i - 1]);

                Vector3 center = PixelToWorld(pts[i]);
                Vector3 offset = PixelDirToWorld(Perpendicular(pts, i, arc.Closed));

                float level = levels != null ? levels[i] : 0f;
                b.verts.Add(center); b.normals.Add(offset); b.uvs.Add(new Vector3(along, 0f, level));   // left
                b.verts.Add(center); b.normals.Add(offset); b.uvs.Add(new Vector3(along, 0.5f, level)); // center
                b.verts.Add(center); b.normals.Add(offset); b.uvs.Add(new Vector3(along, 1f, level));   // right
            }

            for (int i = 0; i < n - 1; i++)
            {
                int l0 = baseIndex + i * 3, c0 = l0 + 1, r0 = l0 + 2;
                int l1 = l0 + 3, c1 = l1 + 1, r1 = l1 + 2;
                // Left half
                b.tris.Add(l0); b.tris.Add(c0); b.tris.Add(l1);
                b.tris.Add(c0); b.tris.Add(c1); b.tris.Add(l1);
                // Right half
                b.tris.Add(c0); b.tris.Add(r0); b.tris.Add(c1);
                b.tris.Add(r0); b.tris.Add(r1); b.tris.Add(c1);
            }

            if (!arc.Closed)
            {
                // Cap directions are the actual world offsets of the strip's left and right edges
                RecordEnd(b, arc.Points[0], b.verts[baseIndex], -b.normals[baseIndex], b.normals[baseIndex], b.uvs[baseIndex].z);
                int last = baseIndex + (n - 1) * 3;
                RecordEnd(b, arc.Points[arc.Points.Length - 1], b.verts[last], -b.normals[last], b.normals[last], b.uvs[last].z);
            }
        }

        private static void RecordEnd(Builder b, Vector2 point, Vector3 center, Vector3 left, Vector3 right, float level)
        {
            // Arc endpoints are shared exactly by every arc meeting there: pixel corners (borders,
            // integer coordinates) or pixel centres (rivers, +0.5). Floor(+0.25) maps both to the pixel.
            var key = new Vector2Int(Mathf.FloorToInt(point.x + 0.25f), Mathf.FloorToInt(point.y + 0.25f));
            if (!b.ends.TryGetValue(key, out var list))
                b.ends[key] = list = new List<(Vector3, Vector3, Vector3, float)>();
            list.Add((center, left, right, level));
        }

        private static void AddJunctionCap(List<(Vector3 center, Vector3 left, Vector3 right, float level)> ends, Builder b)
        {
            Vector3 center = ends[0].center;
            float level = 0f;
            var outer = new List<Vector3>(ends.Count * 2);
            foreach (var e in ends) { outer.Add(e.left); outer.Add(e.right); level = Mathf.Max(level, e.level); }
            outer.Sort((p, q) => Mathf.Atan2(p.z, p.x).CompareTo(Mathf.Atan2(q.z, q.x)));

            int c = b.verts.Count;
            // Center: v = 0.5 does not move; a real normal keeps the shader's width floor and fade right
            b.verts.Add(center); b.normals.Add(outer[0]); b.uvs.Add(new Vector3(0f, 0.5f, level));
            foreach (var dir in outer)
            {
                // Outer: v = 0 moves by -1 x normal, so store -dir to land on dir
                b.verts.Add(center); b.normals.Add(-dir); b.uvs.Add(new Vector3(0f, 0f, level));
            }
            for (int i = 0; i < outer.Count; i++)
            {
                b.tris.Add(c);
                b.tris.Add(c + 1 + i);
                b.tris.Add(c + 1 + (i + 1) % outer.Count);
            }
        }

        private static List<Vector2> Subdivide(Vector2[] polyline, float[] levelsIn, out List<float> levelsOut)
        {
            var result = new List<Vector2>(polyline.Length * 2) { polyline[0] };
            levelsOut = levelsIn != null ? new List<float>(polyline.Length * 2) { levelsIn[0] } : null;
            for (int i = 1; i < polyline.Length; i++)
            {
                Vector2 from = polyline[i - 1], to = polyline[i];
                float dist = Vector2.Distance(from, to);
                if (dist > MAX_SEGMENT_PIXELS)
                {
                    int steps = Mathf.CeilToInt(dist / MAX_SEGMENT_PIXELS);
                    for (int s = 1; s < steps; s++)
                    {
                        float t = s / (float)steps;
                        result.Add(Vector2.Lerp(from, to, t));
                        levelsOut?.Add(Mathf.Lerp(levelsIn[i - 1], levelsIn[i], t));
                    }
                }
                result.Add(to);
                levelsOut?.Add(levelsIn != null ? levelsIn[i] : 0f);
            }
            return result;
        }

        /// <summary>Unit perpendicular in pixel space; joins average the neighbouring segment directions.</summary>
        /// <remarks>
        /// Neighbours are the nearest points at least NORMAL_LOOKAHEAD_PIXELS away, not the adjacent
        /// ones. Arc ends keep their first pixel step (a sub-pixel stub along the pixel grid after
        /// smoothing); with adjacent neighbours that stub turned the normal by up to 90 degrees within a
        /// fraction of a pixel, and the quad between two such vertices became a wedge as long as the
        /// half width (spokes at joints, visible on wide strips like the highlight glow). A full
        /// reversal (back == -forward) uses the forward direction.
        /// </remarks>
        private static Vector2 Perpendicular(List<Vector2> p, int i, bool closed)
        {
            Vector2 back = DistinctDirection(p, i, -1, closed);
            Vector2 fwd = DistinctDirection(p, i, +1, closed);
            Vector2 dir = back + fwd;
            if (dir.sqrMagnitude < 1e-6f)
                dir = fwd.sqrMagnitude > 0f ? fwd : back;
            dir = dir.sqrMagnitude > 1e-12f ? dir.normalized : Vector2.right;
            return new Vector2(-dir.y, dir.x);
        }

        /// <summary>
        /// Unit direction along the line between p[i] and the nearest point distinct from it,
        /// stepping backwards (-1) or forwards (+1); zero if there is none (open arc end).
        /// Closed arcs wrap (their last point repeats the first).
        /// </summary>
        private static Vector2 DistinctDirection(List<Vector2> p, int i, int step, bool closed)
        {
            const float minDist = NORMAL_LOOKAHEAD_PIXELS;
            int count = closed ? p.Count - 1 : p.Count; // closed: unique points only
            if (count < 2) return Vector2.zero;
            if (closed && i == p.Count - 1) i = 0;

            // Short arcs may have nothing that far: fall back to the farthest distinct point
            Vector2 fallback = Vector2.zero;
            float fallbackLen = 1e-4f;
            for (int k = 1; k < count; k++)
            {
                int j = i + step * k;
                if (closed) j = ((j % count) + count) % count;
                else if (j < 0 || j >= count) break;

                Vector2 d = step > 0 ? p[j] - p[i] : p[i] - p[j];
                float len = d.magnitude;
                if (len >= minDist) return d / len;
                if (len > fallbackLen) { fallback = d / len; fallbackLen = len; }
            }
            return fallback;
        }

        /// <summary>Pixel (x right, y down) to world on the map plane; height is applied in the shader.</summary>
        private Vector3 PixelToWorld(Vector2 pixel)
        {
            return new Vector3(
                worldMin.x + pixel.x * worldPerPixel.x,
                0f,
                worldMin.z + (mapHeightPixels - pixel.y) * worldPerPixel.y);
        }

        /// <summary>A pixel-space vector as a world-space offset (z flips because pixel y points down).</summary>
        private Vector3 PixelDirToWorld(Vector2 d)
        {
            return new Vector3(d.x * worldPerPixel.x, 0f, -d.y * worldPerPixel.y);
        }

        private void DestroyMeshes()
        {
            foreach (var m in meshes)
                if (m != null) Object.Destroy(m);
            meshes.Clear();
            meshClassIds.Clear();
        }

        public void Dispose() => DestroyMeshes();
    }
}
