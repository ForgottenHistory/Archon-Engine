using System.Collections.Generic;
using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// Generates quad meshes from smoothed polyline borders
    /// Converts Chaikin-smoothed curves into renderable triangle geometry
    /// Each line segment becomes a thin quad (2 triangles) with flat square caps
    /// </summary>
    public class BorderMeshGenerator
    {
        private readonly float borderWidth;
        private readonly float mapWidth;
        private readonly float mapHeight;

        // Mesh data for all borders
        private List<Vector3> vertices = new List<Vector3>();
        private List<int> triangles = new List<int>();
        private List<Color> colors = new List<Color>();

        // Separate meshes for different border types (may be split into multiple meshes due to 65k vertex limit)
        private List<Mesh> provinceBorderMeshes = new List<Mesh>();
        private List<Mesh> countryBorderMeshes = new List<Mesh>();

        private const int MAX_VERTICES_PER_MESH = 65000; // Unity's 65535 limit with safety margin

        public BorderMeshGenerator(float width, float mapWidthPixels, float mapHeightPixels)
        {
            borderWidth = width;
            mapWidth = mapWidthPixels;
            mapHeight = mapHeightPixels;
        }

        /// <summary>
        /// Generate meshes from border curve data
        /// </summary>
        public void GenerateBorderMeshes(BorderCurveCache cache)
        {
            float startTime = Time.realtimeSinceStartup;

            // Separate vertices/triangles by border type
            var provinceVertices = new List<Vector3>();
            var provinceTriangles = new List<int>();
            var provinceColors = new List<Color>();

            var countryVertices = new List<Vector3>();
            var countryTriangles = new List<int>();
            var countryColors = new List<Color>();

            int totalSegments = 0;

            foreach (var (borderKey, style) in cache.GetAllBorderStyles())
            {
                if (!style.visible)
                    continue;

                var (provinceA, provinceB) = borderKey;
                var segments = cache.GetCurve(provinceA, provinceB);
                if (segments == null || segments.Count == 0)
                    continue;

                // Reconstruct polyline from Bézier segments
                // Each segment P0->P3 is a line segment (P1, P2 are control points, but we treat as linear)
                List<Vector2> polyline = new List<Vector2>();
                foreach (var seg in segments)
                {
                    if (polyline.Count == 0)
                        polyline.Add(seg.P0);
                    polyline.Add(seg.P3);
                }

                if (polyline.Count < 2)
                    continue;

                // Choose target lists based on border type
                var targetVertices = style.type == BorderType.Country ? countryVertices : provinceVertices;
                var targetTriangles = style.type == BorderType.Country ? countryTriangles : provinceTriangles;
                var targetColors = style.type == BorderType.Country ? countryColors : provinceColors;

                // Generate quads for this border
                GenerateQuadsForPolyline(polyline, targetVertices, targetTriangles, targetColors, style);
                totalSegments += polyline.Count - 1;
            }

            // Create Unity meshes (split into multiple if needed due to 65k vertex limit)
            provinceBorderMeshes = CreateMeshes(provinceVertices, provinceTriangles, provinceColors, "Province Borders");
            countryBorderMeshes = CreateMeshes(countryVertices, countryTriangles, countryColors, "Country Borders");

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderMeshGenerator: Generated meshes in {elapsed:F1}ms", "map_initialization");
            ArchonLogger.Log($"  Province borders: {provinceVertices.Count} vertices, {provinceTriangles.Count / 3} triangles in {provinceBorderMeshes.Count} meshes", "map_initialization");
            ArchonLogger.Log($"  Country borders: {countryVertices.Count} vertices, {countryTriangles.Count / 3} triangles in {countryBorderMeshes.Count} meshes", "map_initialization");
            ArchonLogger.Log($"  Total segments: {totalSegments}", "map_initialization");
        }

        /// <summary>
        /// Generate quad geometry for a polyline
        /// Each segment P0->P1 becomes a quad (4 vertices, 2 triangles)
        /// </summary>
        private void GenerateQuadsForPolyline(List<Vector2> polyline, List<Vector3> verts, List<int> tris, List<Color> cols, BorderStyle style)
        {
            float halfWidth = borderWidth * 0.5f;
            Color borderColor = style.color;

            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector2 p0 = polyline[i];
                Vector2 p1 = polyline[i + 1];

                // Skip degenerate segments
                Vector2 dir = p1 - p0;
                float length = dir.magnitude;
                if (length < 0.01f)
                    continue;

                // Calculate perpendicular direction
                dir /= length; // Normalize
                Vector2 perp = new Vector2(-dir.y, dir.x) * halfWidth;

                // Generate 4 vertices for the quad
                int baseIndex = verts.Count;

                // Unity's default plane is 10x10 units, centered at origin (-5 to +5)
                // Convert pixel coordinates to match this: pixels to -5 to +5 range
                // NOTE: Flip X axis to match map texture orientation
                float x0 = 5f - (p0.x / mapWidth) * 10f;  // Flipped
                float z0 = (p0.y / mapHeight) * 10f - 5f;
                float x1 = 5f - (p1.x / mapWidth) * 10f;  // Flipped
                float z1 = (p1.y / mapHeight) * 10f - 5f;

                // Perpendicular offset scaled to match 10-unit plane
                float perpX = (perp.x / mapWidth) * 10f;
                float perpZ = (perp.y / mapHeight) * 10f;

                // NOTE: Map plane is at Y=0, borders rendered at Y=0.01 to be slightly above
                float borderHeight = 0.01f;
                verts.Add(new Vector3(x0 - perpX, borderHeight, z0 - perpZ)); // Bottom-left
                verts.Add(new Vector3(x0 + perpX, borderHeight, z0 + perpZ)); // Top-left
                verts.Add(new Vector3(x1 + perpX, borderHeight, z1 + perpZ)); // Top-right
                verts.Add(new Vector3(x1 - perpX, borderHeight, z1 - perpZ)); // Bottom-right

                // Add vertex colors
                for (int v = 0; v < 4; v++)
                    cols.Add(borderColor);

                // Generate 2 triangles (quad)
                // Triangle 1: 0, 1, 2
                tris.Add(baseIndex + 0);
                tris.Add(baseIndex + 1);
                tris.Add(baseIndex + 2);

                // Triangle 2: 0, 2, 3
                tris.Add(baseIndex + 0);
                tris.Add(baseIndex + 2);
                tris.Add(baseIndex + 3);
            }
        }

        /// <summary>
        /// Create Unity Meshes from vertex data, splitting if necessary due to 65k vertex limit
        /// </summary>
        private List<Mesh> CreateMeshes(List<Vector3> verts, List<int> tris, List<Color> cols, string baseName)
        {
            var meshes = new List<Mesh>();

            if (verts.Count == 0)
                return meshes;

            // Split into chunks of MAX_VERTICES_PER_MESH
            int offset = 0;
            int meshIndex = 0;

            while (offset < verts.Count)
            {
                int chunkSize = Mathf.Min(MAX_VERTICES_PER_MESH, verts.Count - offset);

                // Find triangle count for this vertex range
                int triCount = 0;
                for (int i = 0; i < tris.Count; i += 3)
                {
                    if (tris[i] >= offset && tris[i] < offset + chunkSize)
                        triCount += 3;
                }

                // Create chunk lists
                var chunkVerts = verts.GetRange(offset, chunkSize);
                var chunkCols = cols.GetRange(offset, chunkSize);
                var chunkTris = new List<int>(triCount);

                // Copy triangles and remap indices
                for (int i = 0; i < tris.Count; i += 3)
                {
                    if (tris[i] >= offset && tris[i] < offset + chunkSize)
                    {
                        chunkTris.Add(tris[i] - offset);
                        chunkTris.Add(tris[i + 1] - offset);
                        chunkTris.Add(tris[i + 2] - offset);
                    }
                }

                // Create mesh for this chunk
                var mesh = new Mesh
                {
                    name = $"{baseName}_{meshIndex}"
                };

                mesh.SetVertices(chunkVerts);
                mesh.SetTriangles(chunkTris, 0);
                mesh.SetColors(chunkCols);
                mesh.RecalculateBounds();

                meshes.Add(mesh);

                offset += chunkSize;
                meshIndex++;
            }

            return meshes;
        }

        /// <summary>
        /// Get generated meshes for province borders
        /// </summary>
        public List<Mesh> GetProvinceBorderMeshes() => provinceBorderMeshes;

        /// <summary>
        /// Get generated meshes for country borders
        /// </summary>
        public List<Mesh> GetCountryBorderMeshes() => countryBorderMeshes;

        /// <summary>
        /// Clean up meshes
        /// </summary>
        public void Dispose()
        {
            foreach (var mesh in provinceBorderMeshes)
                Object.Destroy(mesh);
            foreach (var mesh in countryBorderMeshes)
                Object.Destroy(mesh);

            provinceBorderMeshes.Clear();
            countryBorderMeshes.Clear();
        }
    }
}
