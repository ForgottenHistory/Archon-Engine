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
        /// Creates multiple meshes if vertex count exceeds 65k limit
        /// </summary>
        public void GenerateBorderMeshes(BorderCurveCache cache)
        {
            float startTime = Time.realtimeSinceStartup;

            // Current mesh data (will create new mesh when approaching 65k vertices)
            var currentProvinceVerts = new List<Vector3>();
            var currentProvinceTris = new List<int>();
            var currentProvinceColors = new List<Color>();

            var currentCountryVerts = new List<Vector3>();
            var currentCountryTris = new List<int>();
            var currentCountryColors = new List<Color>();

            int totalSegments = 0;
            int provinceVertCount = 0;
            int countryVertCount = 0;

            int skippedNullCount = 0;
            int skippedTooShortCount = 0;
            int skippedInvisibleCount = 0;
            int smallBordersRendered = 0;

            foreach (var (borderKey, style) in cache.GetAllBorderStyles())
            {
                if (!style.visible)
                {
                    skippedInvisibleCount++;
                    continue;
                }

                var (provinceA, provinceB) = borderKey;
                var polyline = cache.GetPolyline(provinceA, provinceB);

                if (polyline == null)
                {
                    skippedNullCount++;
                    continue;
                }

                if (polyline.Count < 2)
                {
                    skippedTooShortCount++;
                    if (skippedTooShortCount <= 10)
                        ArchonLogger.LogWarning($"BorderMeshGenerator: Skipping border {provinceA}-{provinceB} - only {polyline.Count} vertices", "map_initialization");
                    continue;
                }

                // Track small borders being rendered
                if (polyline.Count < 50 && smallBordersRendered < 10)
                {
                    ArchonLogger.Log($"BorderMeshGenerator: Rendering small border {provinceA}-{provinceB} with {polyline.Count} vertices", "map_initialization");
                    smallBordersRendered++;
                }

                // Estimate vertices this border will add (2 per point in triangle strip)
                int estimatedVerts = polyline.Count * 2;

                // Choose target lists based on border type
                bool isCountryBorder = (style.type == BorderType.Country);
                var targetVertices = isCountryBorder ? currentCountryVerts : currentProvinceVerts;
                var targetTriangles = isCountryBorder ? currentCountryTris : currentProvinceTris;
                var targetColors = isCountryBorder ? currentCountryColors : currentProvinceColors;

                // Check if adding this border would exceed vertex limit
                if (targetVertices.Count + estimatedVerts > MAX_VERTICES_PER_MESH)
                {
                    // Finalize current mesh before starting new one
                    if (isCountryBorder && currentCountryVerts.Count > 0)
                    {
                        var mesh = CreateSingleMesh(currentCountryVerts, currentCountryTris, currentCountryColors, $"Country Borders {countryBorderMeshes.Count}");
                        countryBorderMeshes.Add(mesh);
                        countryVertCount += currentCountryVerts.Count;
                        currentCountryVerts.Clear();
                        currentCountryTris.Clear();
                        currentCountryColors.Clear();
                    }
                    else if (!isCountryBorder && currentProvinceVerts.Count > 0)
                    {
                        var mesh = CreateSingleMesh(currentProvinceVerts, currentProvinceTris, currentProvinceColors, $"Province Borders {provinceBorderMeshes.Count}");
                        provinceBorderMeshes.Add(mesh);
                        provinceVertCount += currentProvinceVerts.Count;
                        currentProvinceVerts.Clear();
                        currentProvinceTris.Clear();
                        currentProvinceColors.Clear();
                    }
                }

                // Generate triangle strip for this border
                GenerateQuadsForPolyline(polyline, targetVertices, targetTriangles, targetColors, style);
                totalSegments += polyline.Count - 1;
            }

            // Finalize any remaining meshes
            if (currentProvinceVerts.Count > 0)
            {
                var mesh = CreateSingleMesh(currentProvinceVerts, currentProvinceTris, currentProvinceColors, $"Province Borders {provinceBorderMeshes.Count}");
                provinceBorderMeshes.Add(mesh);
                provinceVertCount += currentProvinceVerts.Count;
            }
            if (currentCountryVerts.Count > 0)
            {
                var mesh = CreateSingleMesh(currentCountryVerts, currentCountryTris, currentCountryColors, $"Country Borders {countryBorderMeshes.Count}");
                countryBorderMeshes.Add(mesh);
                countryVertCount += currentCountryVerts.Count;
            }

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderMeshGenerator: Generated meshes in {elapsed:F1}ms", "map_initialization");
            ArchonLogger.Log($"  Province borders: {provinceVertCount} vertices in {provinceBorderMeshes.Count} meshes", "map_initialization");
            ArchonLogger.Log($"  Country borders: {countryVertCount} vertices in {countryBorderMeshes.Count} meshes", "map_initialization");
            ArchonLogger.Log($"  Total segments: {totalSegments}", "map_initialization");
            ArchonLogger.Log($"  Skipped: {skippedInvisibleCount} invisible, {skippedNullCount} null, {skippedTooShortCount} too short", "map_initialization");
        }

        /// <summary>
        /// Create a single mesh from vertex data (helper for GenerateBorderMeshes)
        /// </summary>
        private Mesh CreateSingleMesh(List<Vector3> verts, List<int> tris, List<Color> cols, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetColors(cols);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Generate triangle strip geometry for a polyline (SEAMLESS - Paradox approach)
        /// Creates alternating left/right vertices that form connected triangles
        /// Border width: 0.0002 world units (Paradox value = sub-pixel thin)
        /// </summary>
        private void GenerateQuadsForPolyline(List<Vector2> polyline, List<Vector3> verts, List<int> tris, List<Color> cols, BorderStyle style)
        {
            if (polyline.Count < 2)
                return;

            // TEMPORARY: Use moderately thick borders for debugging visibility
            // Normal: 0.0002 world units = sub-pixel thin
            // Debug: 0.002 world units = 10x thicker
            float halfWidth = 0.001f; // Half of 0.002 world units (TEMPORARY FOR DEBUGGING)

            Color borderColor = style.color;
            int baseIndex = verts.Count;

            // TRIANGLE STRIP APPROACH (Paradox method):
            // Generate vertices alternating left/right along polyline
            // GPU automatically connects them into seamless triangles
            //
            // Example for 4 points:
            // Polyline: A -> B -> C -> D
            // Vertices: [A_left, A_right, B_left, B_right, C_left, C_right, D_left, D_right]
            // Triangles: (0,1,2), (1,2,3), (2,3,4), (3,4,5), ... (automatic via index pattern)

            // First pass: convert all polyline points to world space
            List<Vector3> worldPoints = new List<Vector3>();
            for (int i = 0; i < polyline.Count; i++)
            {
                Vector2 p = polyline[i];
                // Unity's default plane is 10x10 units, centered at origin (-5 to +5)
                // Convert pixel coordinates to world space
                // NOTE: Flip X axis to match map texture orientation
                float x = 5f - (p.x / mapWidth) * 10f;  // Flipped
                float z = (p.y / mapHeight) * 10f - 5f;
                worldPoints.Add(new Vector3(x, 0, z));
            }

            // Second pass: generate vertices with perpendiculars calculated in world space
            for (int i = 0; i < worldPoints.Count; i++)
            {
                Vector3 worldPos = worldPoints[i];

                // Calculate perpendicular in world space
                Vector3 perpendicular = CalculatePerpendicularWorldSpace(worldPoints, i);

                // Apply width offset
                Vector3 offset = perpendicular * halfWidth;

                // NOTE: Map plane is at Y=0, borders rendered at Y=0.01 to be slightly above
                float borderHeight = 0.01f;

                // Add left and right vertices
                verts.Add(new Vector3(worldPos.x - offset.x, borderHeight, worldPos.z - offset.z)); // Left edge
                verts.Add(new Vector3(worldPos.x + offset.x, borderHeight, worldPos.z + offset.z)); // Right edge

                cols.Add(borderColor);
                cols.Add(borderColor);
            }

            // Generate triangle indices for strip
            // Pattern: (0,1,2), (1,3,2), (2,3,4), (3,5,4), ...
            // This creates zigzag triangles connecting left/right edges
            for (int i = 0; i < polyline.Count - 1; i++)
            {
                int idx = baseIndex + i * 2;

                // Triangle 1: left[i], right[i], left[i+1]
                tris.Add(idx + 0);
                tris.Add(idx + 1);
                tris.Add(idx + 2);

                // Triangle 2: right[i], right[i+1], left[i+1]
                tris.Add(idx + 1);
                tris.Add(idx + 3);
                tris.Add(idx + 2);
            }
        }

        /// <summary>
        /// Evaluate cubic Bézier curve at parameter t (0 to 1)
        /// </summary>
        private Vector2 EvaluateBezier(BezierSegment seg, float t)
        {
            // Cubic Bézier formula: B(t) = (1-t)³P0 + 3(1-t)²tP1 + 3(1-t)t²P2 + t³P3
            float u = 1f - t;
            float u2 = u * u;
            float u3 = u2 * u;
            float t2 = t * t;
            float t3 = t2 * t;

            Vector2 result = u3 * seg.P0 +
                            3f * u2 * t * seg.P1 +
                            3f * u * t2 * seg.P2 +
                            t3 * seg.P3;

            return result;
        }

        /// <summary>
        /// Calculate perpendicular direction in world space
        /// Averages directions to previous and next points for smooth corners
        /// </summary>
        private Vector3 CalculatePerpendicularWorldSpace(List<Vector3> worldPoints, int index)
        {
            Vector3 perp = Vector3.zero;

            // For interior points, average direction to prev and next
            if (index > 0 && index < worldPoints.Count - 1)
            {
                Vector3 dirToPrev = (worldPoints[index] - worldPoints[index - 1]).normalized;
                Vector3 dirToNext = (worldPoints[index + 1] - worldPoints[index]).normalized;

                // Average direction (tangent)
                Vector3 avgDir = (dirToPrev + dirToNext).normalized;

                // Perpendicular in XZ plane (rotate 90° around Y axis)
                perp = new Vector3(-avgDir.z, 0, avgDir.x);
            }
            // For start point, use direction to next
            else if (index == 0)
            {
                Vector3 dir = (worldPoints[1] - worldPoints[0]).normalized;
                perp = new Vector3(-dir.z, 0, dir.x);
            }
            // For end point, use direction from previous
            else
            {
                Vector3 dir = (worldPoints[index] - worldPoints[index - 1]).normalized;
                perp = new Vector3(-dir.z, 0, dir.x);
            }

            return perp.normalized;
        }

        /// <summary>
        /// Calculate perpendicular direction at a point along polyline (OLD - PIXEL SPACE)
        /// Averages directions to previous and next points for smooth corners
        /// </summary>
        private Vector2 CalculatePerpendicular(List<Vector2> polyline, int index)
        {
            Vector2 perp = Vector2.zero;

            // For interior points, average direction to prev and next
            if (index > 0 && index < polyline.Count - 1)
            {
                Vector2 dirToPrev = (polyline[index] - polyline[index - 1]).normalized;
                Vector2 dirToNext = (polyline[index + 1] - polyline[index]).normalized;

                // Average direction
                Vector2 avgDir = (dirToPrev + dirToNext).normalized;

                // Perpendicular to average direction
                perp = new Vector2(-avgDir.y, avgDir.x);
            }
            // For start point, use direction to next
            else if (index == 0)
            {
                Vector2 dir = (polyline[1] - polyline[0]).normalized;
                perp = new Vector2(-dir.y, dir.x);
            }
            // For end point, use direction from previous
            else
            {
                Vector2 dir = (polyline[index] - polyline[index - 1]).normalized;
                perp = new Vector2(-dir.y, dir.x);
            }

            return perp;
        }

        /// <summary>
        /// Create Unity Meshes from vertex data, splitting if necessary due to 65k vertex limit
        /// IMPORTANT: Cannot split mid-border - must split at border boundaries
        /// </summary>
        private List<Mesh> CreateMeshes(List<Vector3> verts, List<int> tris, List<Color> cols, string baseName)
        {
            var meshes = new List<Mesh>();

            if (verts.Count == 0)
                return meshes;

            // If under vertex limit, create single mesh
            if (verts.Count <= MAX_VERTICES_PER_MESH)
            {
                var mesh = new Mesh
                {
                    name = baseName
                };
                mesh.SetVertices(verts);
                mesh.SetTriangles(tris, 0);
                mesh.SetColors(cols);
                mesh.RecalculateBounds();
                meshes.Add(mesh);
                return meshes;
            }

            // Over 65k vertices - need to split
            // This means too many borders for one mesh
            // Solution: Don't try to split triangle strips mid-border
            // Instead: Generate borders into separate meshes from the start
            ArchonLogger.LogError($"BorderMeshGenerator: {baseName} has {verts.Count} vertices (exceeds 65k limit). Mesh splitting not yet implemented for triangle strips.", "map_initialization");
            ArchonLogger.LogError("BorderMeshGenerator: Consider splitting borders by type or region at generation time.", "map_initialization");

            // Return empty - better to fail cleanly than render incorrectly
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
