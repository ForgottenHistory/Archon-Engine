using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Map.Rendering.Border;

namespace Map.Rendering
{
    /// <summary>
    /// Builds one mesh per border class from the classified border arcs.
    ///
    /// Each arc becomes a strip with two vertices per polyline point. Both vertices sit on the
    /// center line; the normal holds the world-space offset of one map pixel towards that side, so
    /// the vertex shader can expand the strip to any width (map pixels, with a screen-pixel floor).
    /// UV: x = distance along the arc in map pixels, y = 0 (left) .. 1 (right).
    ///
    /// Junction caps: where 2+ arcs of the same class meet, a fan connects their strip ends
    /// (center v = 0.5, outer v = 0/1), so the texture profile continues radially and fills the
    /// wedge between flat strip ends.
    /// </summary>
    public class BorderMeshGenerator
    {
        private const float MAX_SEGMENT_PIXELS = 3f; // subdivision so strips follow the heightmap

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
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> tris = new List<int>();
            // Junction endpoints: corner -> (center, offset dir left, offset dir right)
            public readonly Dictionary<Vector2Int, List<(Vector3 center, Vector3 left, Vector3 right)>> ends =
                new Dictionary<Vector2Int, List<(Vector3, Vector3, Vector3)>>();
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
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DestroyMeshes();

            var builders = new Dictionary<int, Builder>();
            int arcCount = 0;

            foreach (var (arc, classId) in cache.GetVisibleArcs())
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

                var mesh = new Mesh { name = $"Borders {cache.ClassNames[kvp.Key]}", indexFormat = IndexFormat.UInt32 };
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

            ArchonLogger.Log($"BorderMeshGenerator: {arcCount} arcs, {caps} junction caps, {verts} vertices " +
                             $"in {meshes.Count} class meshes ({sw.ElapsedMilliseconds}ms)", "map_initialization");
        }

        private void AddStrip(BorderArc arc, Builder b)
        {
            var pts = Subdivide(arc.Points);
            int n = pts.Count;
            int baseIndex = b.verts.Count;
            float along = 0f;

            for (int i = 0; i < n; i++)
            {
                if (i > 0) along += Vector2.Distance(pts[i], pts[i - 1]);

                Vector3 center = PixelToWorld(pts[i]);
                Vector3 offset = PixelDirToWorld(Perpendicular(pts, i, arc.Closed));

                b.verts.Add(center); b.normals.Add(-offset); b.uvs.Add(new Vector2(along, 0f));
                b.verts.Add(center); b.normals.Add(offset); b.uvs.Add(new Vector2(along, 1f));
            }

            for (int i = 0; i < n - 1; i++)
            {
                int idx = baseIndex + i * 2;
                b.tris.Add(idx); b.tris.Add(idx + 1); b.tris.Add(idx + 2);
                b.tris.Add(idx + 1); b.tris.Add(idx + 3); b.tris.Add(idx + 2);
            }

            if (!arc.Closed)
            {
                RecordEnd(b, arc.Points[0], b.verts[baseIndex], b.normals[baseIndex], b.normals[baseIndex + 1]);
                int last = baseIndex + (n - 1) * 2;
                RecordEnd(b, arc.Points[arc.Points.Length - 1], b.verts[last], b.normals[last], b.normals[last + 1]);
            }
        }

        private static void RecordEnd(Builder b, Vector2 corner, Vector3 center, Vector3 left, Vector3 right)
        {
            // Arc endpoints are exact pixel corners shared by every arc meeting there
            var key = new Vector2Int(Mathf.RoundToInt(corner.x), Mathf.RoundToInt(corner.y));
            if (!b.ends.TryGetValue(key, out var list))
                b.ends[key] = list = new List<(Vector3, Vector3, Vector3)>();
            list.Add((center, left, right));
        }

        private static void AddJunctionCap(List<(Vector3 center, Vector3 left, Vector3 right)> ends, Builder b)
        {
            Vector3 center = ends[0].center;
            var outer = new List<Vector3>(ends.Count * 2);
            foreach (var e in ends) { outer.Add(e.left); outer.Add(e.right); }
            outer.Sort((p, q) => Mathf.Atan2(p.z, p.x).CompareTo(Mathf.Atan2(q.z, q.x)));

            int c = b.verts.Count;
            b.verts.Add(center); b.normals.Add(Vector3.zero); b.uvs.Add(new Vector2(0f, 0.5f));
            foreach (var dir in outer)
            {
                b.verts.Add(center); b.normals.Add(dir); b.uvs.Add(new Vector2(0f, 0f));
            }
            for (int i = 0; i < outer.Count; i++)
            {
                b.tris.Add(c);
                b.tris.Add(c + 1 + i);
                b.tris.Add(c + 1 + (i + 1) % outer.Count);
            }
        }

        private static List<Vector2> Subdivide(Vector2[] polyline)
        {
            var result = new List<Vector2>(polyline.Length * 2) { polyline[0] };
            for (int i = 1; i < polyline.Length; i++)
            {
                Vector2 from = polyline[i - 1], to = polyline[i];
                float dist = Vector2.Distance(from, to);
                if (dist > MAX_SEGMENT_PIXELS)
                {
                    int steps = Mathf.CeilToInt(dist / MAX_SEGMENT_PIXELS);
                    for (int s = 1; s < steps; s++)
                        result.Add(Vector2.Lerp(from, to, s / (float)steps));
                }
                result.Add(to);
            }
            return result;
        }

        /// <summary>Unit perpendicular in pixel space; joins average the neighbouring segment directions.</summary>
        private static Vector2 Perpendicular(List<Vector2> p, int i, bool closed)
        {
            int last = p.Count - 1;
            Vector2 dir;
            if (closed && last >= 2 && (i == 0 || i == last))
                dir = (p[last] - p[last - 1]).normalized + (p[1] - p[0]).normalized;
            else if (i > 0 && i < last)
                dir = (p[i] - p[i - 1]).normalized + (p[i + 1] - p[i]).normalized;
            else if (i == 0)
                dir = p[1] - p[0];
            else
                dir = p[i] - p[i - 1];
            dir = dir.sqrMagnitude > 1e-12f ? dir.normalized : Vector2.right;
            return new Vector2(-dir.y, dir.x);
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
