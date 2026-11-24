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
    ///
    /// Chunked mode (BuildChunked / UpdateChunks, the map's borders): one mesh per class per square of
    /// ChunkPixels map pixels, so an ownership change rebuilds only the chunks and classes whose arcs changed
    /// class (a full rebuild of the HOI4 map is ~41k arcs, 1.3M vertices, ~240 ms). An arc's strip goes in the
    /// chunk of its middle point; a junction cap in the chunk of its corner, built from every same-class arc
    /// end at that corner (ends cached per arc), so caps stay right across chunk edges. Chunk meshes get
    /// their real bounds (plus a margin for the shader's width and height offsets) and are culled.
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

        // Chunked mode
        public const float ChunkPixels = 512f;
        private const float BoundsMarginPixels = 32f; // shader width offsets (screen-pixel floor when zoomed out)
        private int chunksX, chunksY;
        private int[] arcChunk;                        // chunk of the arc's strip, -1 = no geometry
        private int[] arcStartChunk, arcEndChunk;      // chunk of the arc's end corners, -1 = closed arc
        private (Vector3 center, Vector3 left, Vector3 right, float level)[] arcStart, arcEnd;
        private List<int>[] chunkArcs;
        private List<Vector2Int>[] chunkCorners;
        private Dictionary<Vector2Int, List<(int arc, bool start)>> cornerArcs;
        private readonly SortedDictionary<long, Mesh> chunkMeshes = new SortedDictionary<long, Mesh>(); // (class << 32 | chunk)

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

        /// <summary>Build one mesh per class id from (arc, class id) pairs. junctionCaps false: no fans where arcs
        /// meet (lines whose texture isn't a radial profile, such as railway track, overlap at their ends instead).</summary>
        public void GenerateMeshes(IEnumerable<(BorderArc arc, int classId)> classifiedArcs, IReadOnlyList<string> classNames,
                                   bool log = true, bool junctionCaps = true)
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
                AddStrip(arc, b, recordEnds: junctionCaps);
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

        private void AddStrip(BorderArc arc, Builder b, bool recordEnds = true)
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

            if (!arc.Closed && recordEnds)
            {
                // Cap directions are the actual world offsets of the strip's left and right edges
                RecordEnd(b, arc.Points[0], b.verts[baseIndex], -b.normals[baseIndex], b.normals[baseIndex], b.uvs[baseIndex].z);
                int last = baseIndex + (n - 1) * 3;
                RecordEnd(b, arc.Points[arc.Points.Length - 1], b.verts[last], -b.normals[last], b.normals[last], b.uvs[last].z);
            }
        }

        // --- Chunked mode -------------------------------------------------------------------------------------

        /// <summary>Full build: chunk index, cached arc ends, and every chunk's meshes.</summary>
        public void BuildChunked(BorderCurveCache cache)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DestroyMeshes();
            chunkMeshes.Clear();

            var arcs = cache.Arcs;
            int n = arcs.Count;
            chunksX = Mathf.Max(1, Mathf.CeilToInt(mapWidthPixels / ChunkPixels));
            chunksY = Mathf.Max(1, Mathf.CeilToInt(mapHeightPixels / ChunkPixels));
            int chunkCount = chunksX * chunksY;
            arcChunk = new int[n];
            arcStartChunk = new int[n];
            arcEndChunk = new int[n];
            arcStart = new (Vector3, Vector3, Vector3, float)[n];
            arcEnd = new (Vector3, Vector3, Vector3, float)[n];
            chunkArcs = new List<int>[chunkCount];
            chunkCorners = new List<Vector2Int>[chunkCount];
            for (int c = 0; c < chunkCount; c++) { chunkArcs[c] = new List<int>(); chunkCorners[c] = new List<Vector2Int>(); }
            cornerArcs = new Dictionary<Vector2Int, List<(int, bool)>>();

            for (int i = 0; i < n; i++)
            {
                var arc = arcs[i];
                arcStartChunk[i] = arcEndChunk[i] = -1;
                if (arc.Points.Length < 2) { arcChunk[i] = -1; continue; }
                arcChunk[i] = ChunkOf(arc.Points[arc.Points.Length / 2]);
                chunkArcs[arcChunk[i]].Add(i);
                if (arc.Closed) continue;

                // The strip's end vertices, as AddStrip makes them (geometry only, not class)
                var pts = Subdivide(arc.Points, arc.Levels, out var levels);
                int last = pts.Count - 1;
                Vector3 o0 = PixelDirToWorld(Perpendicular(pts, 0, false));
                Vector3 o1 = PixelDirToWorld(Perpendicular(pts, last, false));
                arcStart[i] = (PixelToWorld(pts[0]), -o0, o0, levels != null ? levels[0] : 0f);
                arcEnd[i] = (PixelToWorld(pts[last]), -o1, o1, levels != null ? levels[last] : 0f);
                arcStartChunk[i] = AddCorner(arc.Points[0], i, true);
                arcEndChunk[i] = AddCorner(arc.Points[arc.Points.Length - 1], i, false);
            }

            int built = 0;
            for (int c = 0; c < chunkCount; c++)
            {
                var classes = new SortedSet<int>();
                foreach (int a in chunkArcs[c]) { int k = cache.ClassOf(a); if (k != BorderCurveCache.Hidden) classes.Add(k); }
                foreach (var corner in chunkCorners[c])
                    foreach (var (a, _) in cornerArcs[corner]) { int k = cache.ClassOf(a); if (k != BorderCurveCache.Hidden) classes.Add(k); }
                foreach (int k in classes) { RebuildChunk(c, k, cache); built++; }
            }
            RefreshMeshList();
            ArchonLogger.Log($"BorderMeshGenerator: {n} arcs in {chunkMeshes.Count} chunk meshes ({chunksX}x{chunksY} chunks of " +
                             $"{ChunkPixels} px, {built} built) in {sw.ElapsedMilliseconds}ms", "map_initialization");
        }

        /// <summary>Rebuild only the chunks and classes the changed arcs (with their old class) touch.</summary>
        public void UpdateChunks(BorderCurveCache cache, List<(int arc, int oldClass)> changed)
        {
            if (arcChunk == null) { BuildChunked(cache); return; }
            var dirty = new SortedSet<long>();
            foreach (var (a, oldClass) in changed)
            {
                if (arcChunk[a] < 0) continue;
                int newClass = cache.ClassOf(a);
                foreach (int k in new[] { oldClass, newClass })
                {
                    if (k == BorderCurveCache.Hidden) continue;
                    dirty.Add(Key(arcChunk[a], k));
                    if (arcStartChunk[a] >= 0) dirty.Add(Key(arcStartChunk[a], k));
                    if (arcEndChunk[a] >= 0) dirty.Add(Key(arcEndChunk[a], k));
                }
            }
            foreach (long key in dirty)
                RebuildChunk((int)(key & 0xFFFFFFFF), (int)(key >> 32), cache);
            RefreshMeshList();
        }

        private static long Key(int chunk, int classId) => ((long)classId << 32) | (uint)chunk;

        private int ChunkOf(Vector2 pixel)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt(pixel.x / ChunkPixels), 0, chunksX - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(pixel.y / ChunkPixels), 0, chunksY - 1);
            return cy * chunksX + cx;
        }

        private static Vector2Int CornerKey(Vector2 point) =>
            new Vector2Int(Mathf.FloorToInt(point.x + 0.25f), Mathf.FloorToInt(point.y + 0.25f)); // as RecordEnd

        private int AddCorner(Vector2 point, int arc, bool start)
        {
            var key = CornerKey(point);
            int chunk = ChunkOf(new Vector2(key.x, key.y)); // by the shared corner key, the same for every arc there
            if (!cornerArcs.TryGetValue(key, out var list))
            {
                cornerArcs[key] = list = new List<(int, bool)>(3);
                chunkCorners[chunk].Add(key);
            }
            list.Add((arc, start));
            return chunk;
        }

        private void RebuildChunk(int chunk, int classId, BorderCurveCache cache)
        {
            long key = Key(chunk, classId);
            if (chunkMeshes.TryGetValue(key, out var old))
            {
                if (old != null) Object.Destroy(old);
                chunkMeshes.Remove(key);
            }

            var b = new Builder();
            var arcs = cache.Arcs;
            foreach (int a in chunkArcs[chunk])
                if (cache.ClassOf(a) == classId) AddStrip(arcs[a], b, recordEnds: false);
            var ends = new List<(Vector3 center, Vector3 left, Vector3 right, float level)>(4);
            foreach (var corner in chunkCorners[chunk])
            {
                ends.Clear();
                foreach (var (a, start) in cornerArcs[corner])
                    if (cache.ClassOf(a) == classId) ends.Add(start ? arcStart[a] : arcEnd[a]);
                if (ends.Count >= 2) AddJunctionCap(ends, b);
            }
            if (b.verts.Count == 0) return;

            var mesh = new Mesh { name = $"Lines {classId} chunk {chunk}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(b.verts);
            mesh.SetNormals(b.normals);
            mesh.SetUVs(0, b.uvs);
            mesh.SetTriangles(b.tris, 0, false);
            // Real bounds, widened for the shader's offsets (width sideways, terrain height up)
            mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            bounds.Expand(new Vector3(BoundsMarginPixels * 2f * worldPerPixel.x, 2000f, BoundsMarginPixels * 2f * worldPerPixel.y));
            mesh.bounds = bounds;
            chunkMeshes[key] = mesh;
        }

        /// <summary>Meshes / MeshClassIds from the chunk meshes, by class then chunk.</summary>
        private void RefreshMeshList()
        {
            meshes.Clear();
            meshClassIds.Clear();
            foreach (var kv in chunkMeshes)
            {
                meshes.Add(kv.Value);
                meshClassIds.Add((int)(kv.Key >> 32));
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
