using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// Generates quad meshes from smoothed polyline borders
    /// Converts Chaikin-smoothed curves into renderable triangle geometry
    /// Each line segment becomes a thin quad (2 triangles) with flat square caps
    /// Junction caps fill holes where 3+ borders meet
    /// </summary>
    public class BorderMeshGenerator
    {
        private readonly float borderWidth;
        private readonly float mapWidthPixels;
        private readonly float mapHeightPixels;

        // World-space bounds from MapPlane transform
        private readonly Vector3 worldMin;   // Bottom-left corner in world space
        private readonly Vector3 worldSize;  // Width/height in world space

        // Mesh data for all borders
        private List<Vector3> vertices = new List<Vector3>();
        private List<int> triangles = new List<int>();
        private List<Color> colors = new List<Color>();
        private List<Vector2> uvs = new List<Vector2>();

        // Separate meshes for different border types (may be split into multiple meshes due to 65k vertex limit)
        private List<Mesh> provinceBorderMeshes = new List<Mesh>();
        private List<Mesh> countryBorderMeshes = new List<Mesh>();

        private const int MAX_VERTICES_PER_MESH = 65000; // Unity's 65535 limit with safety margin

        public BorderMeshGenerator(float width, float mapWidthPx, float mapHeightPx, Transform mapPlaneTransform)
        {
            borderWidth = width;
            mapWidthPixels = mapWidthPx;
            mapHeightPixels = mapHeightPx;

            // Get actual world-space bounds from the map plane
            // Unity default plane is -5 to +5 in local space, scaled by transform
            if (mapPlaneTransform != null)
            {
                var mr = mapPlaneTransform.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    worldMin = mr.bounds.min;
                    worldSize = mr.bounds.size;
                }
                else
                {
                    // Fallback: use transform scale * default plane size
                    Vector3 scale = mapPlaneTransform.localScale;
                    worldSize = new Vector3(scale.x * 10f, 0f, scale.z * 10f);
                    worldMin = mapPlaneTransform.position - worldSize * 0.5f;
                }
            }
            else
            {
                worldMin = new Vector3(-5f, 0f, -5f);
                worldSize = new Vector3(10f, 0f, 10f);
            }

            ArchonLogger.Log($"BorderMeshGenerator: World bounds min={worldMin}, size={worldSize}", "map_initialization");
        }

        /// <summary>
        /// Generate meshes from border curve data
        /// Creates multiple meshes if vertex count exceeds 65k limit
        /// </summary>
        public void GenerateBorderMeshes(BorderCurveCache cache)
        {
            float startTime = Time.realtimeSinceStartup;

            // Clear previous meshes before regenerating
            foreach (var mesh in provinceBorderMeshes)
                Object.Destroy(mesh);
            foreach (var mesh in countryBorderMeshes)
                Object.Destroy(mesh);
            provinceBorderMeshes.Clear();
            countryBorderMeshes.Clear();

            // Current mesh data (will create new mesh when approaching 65k vertices)
            var currentProvinceVerts = new List<Vector3>();
            var currentProvinceTris = new List<int>();
            var currentProvinceColors = new List<Color>();
            var currentProvinceUVs = new List<Vector2>();

            var currentCountryVerts = new List<Vector3>();
            var currentCountryTris = new List<int>();
            var currentCountryColors = new List<Color>();
            var currentCountryUVs = new List<Vector2>();

            int totalSegments = 0;
            int provinceVertCount = 0;
            int countryVertCount = 0;

            int skippedNullCount = 0;
            int skippedTooShortCount = 0;
            int skippedInvisibleCount = 0;
            int smallBordersRendered = 0;

            // Track polyline endpoint vertex positions for junction cap generation
            // Key: snapped pixel position (rounded to int for grouping), Value: list of (leftPos, rightPos)
            var junctionEndpoints = new Dictionary<Vector2Int, List<(Vector3 left, Vector3 right)>>();

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
                var targetUVs = isCountryBorder ? currentCountryUVs : currentProvinceUVs;

                // Check if adding this border would exceed vertex limit
                if (targetVertices.Count + estimatedVerts > MAX_VERTICES_PER_MESH)
                {
                    // Finalize current mesh before starting new one
                    if (isCountryBorder && currentCountryVerts.Count > 0)
                    {
                        var mesh = CreateSingleMesh(currentCountryVerts, currentCountryTris, currentCountryColors, currentCountryUVs, $"Country Borders {countryBorderMeshes.Count}");
                        countryBorderMeshes.Add(mesh);
                        countryVertCount += currentCountryVerts.Count;
                        currentCountryVerts.Clear();
                        currentCountryTris.Clear();
                        currentCountryColors.Clear();
                        currentCountryUVs.Clear();
                    }
                    else if (!isCountryBorder && currentProvinceVerts.Count > 0)
                    {
                        var mesh = CreateSingleMesh(currentProvinceVerts, currentProvinceTris, currentProvinceColors, currentProvinceUVs, $"Province Borders {provinceBorderMeshes.Count}");
                        provinceBorderMeshes.Add(mesh);
                        provinceVertCount += currentProvinceVerts.Count;
                        currentProvinceVerts.Clear();
                        currentProvinceTris.Clear();
                        currentProvinceColors.Clear();
                        currentProvinceUVs.Clear();
                    }
                }

                // Generate triangle strip for this border
                int vertsBefore = targetVertices.Count;
                GenerateQuadsForPolyline(polyline, targetVertices, targetTriangles, targetColors, targetUVs, style);
                int vertsAfter = targetVertices.Count;
                totalSegments += polyline.Count - 1;

                // Record endpoint vertex positions for junction caps
                if (vertsAfter > vertsBefore && polyline.Count >= 2)
                {
                    // Start endpoint: first two vertices (left, right)
                    Vector2 startPixel = polyline[0];
                    var startKey = new Vector2Int(Mathf.RoundToInt(startPixel.x), Mathf.RoundToInt(startPixel.y));
                    if (!junctionEndpoints.ContainsKey(startKey))
                        junctionEndpoints[startKey] = new List<(Vector3, Vector3)>();
                    junctionEndpoints[startKey].Add((targetVertices[vertsBefore], targetVertices[vertsBefore + 1]));

                    // End endpoint: last two vertices (left, right)
                    Vector2 endPixel = polyline[polyline.Count - 1];
                    var endKey = new Vector2Int(Mathf.RoundToInt(endPixel.x), Mathf.RoundToInt(endPixel.y));
                    if (!junctionEndpoints.ContainsKey(endKey))
                        junctionEndpoints[endKey] = new List<(Vector3, Vector3)>();
                    junctionEndpoints[endKey].Add((targetVertices[vertsAfter - 2], targetVertices[vertsAfter - 1]));
                }
            }

            // Generate junction caps to fill holes where 3+ borders meet
            int junctionCapCount = 0;
            foreach (var kvp in junctionEndpoints)
            {
                var endpoints = kvp.Value;
                if (endpoints.Count < 3)
                    continue;

                // All province borders go to province mesh lists for now
                // (junction caps use same color as borders)
                GenerateJunctionCap(endpoints, currentProvinceVerts, currentProvinceTris, currentProvinceColors, currentProvinceUVs);
                junctionCapCount++;

                // Check vertex limit
                if (currentProvinceVerts.Count > MAX_VERTICES_PER_MESH)
                {
                    var mesh = CreateSingleMesh(currentProvinceVerts, currentProvinceTris, currentProvinceColors, currentProvinceUVs, $"Province Borders {provinceBorderMeshes.Count}");
                    provinceBorderMeshes.Add(mesh);
                    provinceVertCount += currentProvinceVerts.Count;
                    currentProvinceVerts.Clear();
                    currentProvinceTris.Clear();
                    currentProvinceColors.Clear();
                    currentProvinceUVs.Clear();
                }
            }

            // Finalize any remaining meshes
            if (currentProvinceVerts.Count > 0)
            {
                var mesh = CreateSingleMesh(currentProvinceVerts, currentProvinceTris, currentProvinceColors, currentProvinceUVs, $"Province Borders {provinceBorderMeshes.Count}");
                provinceBorderMeshes.Add(mesh);
                provinceVertCount += currentProvinceVerts.Count;
            }
            if (currentCountryVerts.Count > 0)
            {
                var mesh = CreateSingleMesh(currentCountryVerts, currentCountryTris, currentCountryColors, currentCountryUVs, $"Country Borders {countryBorderMeshes.Count}");
                countryBorderMeshes.Add(mesh);
                countryVertCount += currentCountryVerts.Count;
            }

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"BorderMeshGenerator: Generated meshes in {elapsed:F1}ms", "map_initialization");
            ArchonLogger.Log($"  Province borders: {provinceVertCount} vertices in {provinceBorderMeshes.Count} meshes", "map_initialization");
            ArchonLogger.Log($"  Country borders: {countryVertCount} vertices in {countryBorderMeshes.Count} meshes", "map_initialization");
            ArchonLogger.Log($"  Total segments: {totalSegments}", "map_initialization");
            ArchonLogger.Log($"  Junction caps: {junctionCapCount}", "map_initialization");
            ArchonLogger.Log($"  Skipped: {skippedInvisibleCount} invisible, {skippedNullCount} null, {skippedTooShortCount} too short", "map_initialization");
        }

        /// <summary>
        /// Create a single mesh from vertex data (helper for GenerateBorderMeshes)
        /// </summary>
        private Mesh CreateSingleMesh(List<Vector3> verts, List<int> tris, List<Color> cols, List<Vector2> uvs, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetColors(cols);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Generate triangle strip geometry for a polyline (SEAMLESS - Paradox approach)
        /// Creates alternating left/right vertices that form connected triangles
        /// Border width: 0.0002 world units (Paradox value = sub-pixel thin)
        /// UVs: U = along border length, V = 0 (left edge) to 1 (right edge)
        /// </summary>
        private void GenerateQuadsForPolyline(List<Vector2> polyline, List<Vector3> verts, List<int> tris, List<Color> cols, List<Vector2> uvs, BorderStyle style)
        {
            if (polyline.Count < 2)
                return;

            // Subdivide long segments so border mesh follows terrain heightmap closely.
            // Max segment length in pixels — shorter = more vertices = better terrain tracking.
            const float MAX_SEGMENT_PIXELS = 3f;
            var subdividedPolyline = SubdividePolyline(polyline, MAX_SEGMENT_PIXELS);

            // Border width in PIXELS — all geometry computed in pixel space for uniformity.
            // borderWidth is in local mesh units (10 units = mapWidth pixels),
            // so convert to pixel-space half-width.
            float halfWidthPixels = (borderWidth / 10f) * mapWidthPixels * 0.5f;

            Color borderColor = style.color;
            int baseIndex = verts.Count;

            // Compute perpendiculars in pixel space (uniform, no aspect distortion)
            // Then convert final vertex positions to local mesh space.
            float totalLength = 0f;
            List<float> accumulatedDistances = new List<float>(subdividedPolyline.Count);
            accumulatedDistances.Add(0f);
            for (int i = 1; i < subdividedPolyline.Count; i++)
            {
                totalLength += Vector2.Distance(subdividedPolyline[i], subdividedPolyline[i - 1]);
                accumulatedDistances.Add(totalLength);
            }

            for (int i = 0; i < subdividedPolyline.Count; i++)
            {
                Vector2 pixelPos = subdividedPolyline[i];

                // Calculate perpendicular in pixel space (uniform coordinates)
                Vector2 perp = CalculatePerpendicularPixelSpace(subdividedPolyline, i);

                // Offset in pixel space
                Vector2 leftPixel = pixelPos - perp * halfWidthPixels;
                Vector2 rightPixel = pixelPos + perp * halfWidthPixels;

                // Convert pixel positions to world space
                Vector3 leftWorld = PixelToWorld(leftPixel);
                Vector3 rightWorld = PixelToWorld(rightPixel);

                verts.Add(leftWorld);
                verts.Add(rightWorld);

                cols.Add(borderColor);
                cols.Add(borderColor);

                float textureRepeatScale = 10f;
                float u = accumulatedDistances[i] * textureRepeatScale;
                uvs.Add(new Vector2(u, 0f));
                uvs.Add(new Vector2(u, 1f));
            }

            // Generate triangle indices for strip
            for (int i = 0; i < subdividedPolyline.Count - 1; i++)
            {
                int idx = baseIndex + i * 2;
                tris.Add(idx + 0);
                tris.Add(idx + 1);
                tris.Add(idx + 2);
                tris.Add(idx + 1);
                tris.Add(idx + 3);
                tris.Add(idx + 2);
            }
        }

        /// <summary>
        /// Subdivide a polyline so no segment exceeds maxSegmentLength pixels.
        /// Ensures enough vertices for the vertex shader heightmap to track terrain.
        /// </summary>
        private List<Vector2> SubdividePolyline(List<Vector2> polyline, float maxSegmentLength)
        {
            var result = new List<Vector2>(polyline.Count * 2);
            result.Add(polyline[0]);

            for (int i = 1; i < polyline.Count; i++)
            {
                Vector2 from = polyline[i - 1];
                Vector2 to = polyline[i];
                float dist = Vector2.Distance(from, to);

                if (dist > maxSegmentLength)
                {
                    int subdivisions = Mathf.CeilToInt(dist / maxSegmentLength);
                    for (int s = 1; s < subdivisions; s++)
                    {
                        float t = s / (float)subdivisions;
                        result.Add(Vector2.Lerp(from, to, t));
                    }
                }

                result.Add(to);
            }

            return result;
        }

        /// <summary>
        /// Convert pixel coordinates to world space using MapPlane bounds.
        /// </summary>
        private Vector3 PixelToWorld(Vector2 pixel)
        {
            float uvX = pixel.x / mapWidthPixels;
            float uvY = 1f - (pixel.y / mapHeightPixels);
            return new Vector3(
                worldMin.x + uvX * worldSize.x,
                0f,
                worldMin.z + uvY * worldSize.z
            );
        }

        /// <summary>
        /// Calculate perpendicular direction in pixel space (uniform coordinates).
        /// </summary>
        private Vector2 CalculatePerpendicularPixelSpace(List<Vector2> points, int index)
        {
            Vector2 dir;

            if (index > 0 && index < points.Count - 1)
            {
                Vector2 dirToPrev = (points[index] - points[index - 1]).normalized;
                Vector2 dirToNext = (points[index + 1] - points[index]).normalized;
                dir = (dirToPrev + dirToNext).normalized;
            }
            else if (index == 0)
            {
                dir = (points[1] - points[0]).normalized;
            }
            else
            {
                dir = (points[index] - points[index - 1]).normalized;
            }

            return new Vector2(-dir.y, dir.x);
        }


        /// <summary>
        /// Generate a triangle fan to fill the hole at a junction where 3+ borders meet.
        /// Collects the left/right endpoint vertices from each border strip, sorts by angle
        /// around the center, and creates triangles connecting them.
        /// </summary>
        private void GenerateJunctionCap(List<(Vector3 left, Vector3 right)> endpoints,
            List<Vector3> verts, List<int> tris, List<Color> cols, List<Vector2> uvs)
        {
            // Collect all outer vertices from the endpoint pairs
            var outerVertices = new List<Vector3>();
            foreach (var (left, right) in endpoints)
            {
                outerVertices.Add(left);
                outerVertices.Add(right);
            }

            if (outerVertices.Count < 3)
                return;

            // Compute center point (average of all outer vertices)
            Vector3 center = Vector3.zero;
            foreach (var v in outerVertices)
                center += v;
            center /= outerVertices.Count;

            // Sort outer vertices by angle around center in XZ plane
            outerVertices.Sort((a, b) =>
            {
                float angleA = Mathf.Atan2(a.z - center.z, a.x - center.x);
                float angleB = Mathf.Atan2(b.z - center.z, b.x - center.x);
                return angleA.CompareTo(angleB);
            });

            // Generate triangle fan from center to sorted outer vertices
            int centerIdx = verts.Count;
            verts.Add(center);
            cols.Add(Color.black);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i < outerVertices.Count; i++)
            {
                verts.Add(outerVertices[i]);
                cols.Add(Color.black);
                uvs.Add(new Vector2(0.5f, 0.5f));
            }

            // Create triangles: center → vertex[i] → vertex[i+1] (wrapping around)
            for (int i = 0; i < outerVertices.Count; i++)
            {
                int next = (i + 1) % outerVertices.Count;
                tris.Add(centerIdx);
                tris.Add(centerIdx + 1 + i);
                tris.Add(centerIdx + 1 + next);
            }
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
