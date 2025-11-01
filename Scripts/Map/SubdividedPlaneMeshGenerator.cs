using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Map.Rendering
{
    /// <summary>
    /// Utility to generate subdivided plane meshes for tessellation
    /// Use via menu: Tools/Archon/Generate Subdivided Plane Mesh
    /// </summary>
    public static class SubdividedPlaneMeshGenerator
    {
#if UNITY_EDITOR
        [MenuItem("Tools/Archon/Generate Subdivided Plane Mesh")]
        public static void GenerateSubdividedPlaneMesh()
        {
            int subdivisions = EditorUtility.DisplayDialogComplex(
                "Subdivided Plane Generator",
                "Choose subdivision level:\n\n" +
                "Low (50x50) = 5,000 triangles\n" +
                "Medium (100x100) = 20,000 triangles\n" +
                "High (200x200) = 80,000 triangles",
                "Low (50)", "Medium (100)", "High (200)");

            int subdivisionCount = subdivisions switch
            {
                0 => 50,
                1 => 100,
                2 => 200,
                _ => 100
            };

            Mesh mesh = GenerateMesh(subdivisionCount);

            // Save mesh as asset
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Subdivided Plane Mesh",
                $"SubdividedPlane_{subdivisionCount}x{subdivisionCount}.asset",
                "asset",
                "Save mesh asset");

            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.CreateAsset(mesh, path);
                AssetDatabase.SaveAssets();

                Debug.Log($"Generated subdivided plane mesh: {subdivisionCount}x{subdivisionCount} " +
                         $"({mesh.vertexCount:N0} vertices, {mesh.triangles.Length / 3:N0} triangles)\n" +
                         $"Saved to: {path}");

                // Select the created asset
                Selection.activeObject = mesh;
                EditorGUIUtility.PingObject(mesh);
            }
        }

        public static Mesh GenerateMesh(int subdivisions)
        {
            Mesh mesh = new Mesh();
            mesh.name = $"SubdividedPlane_{subdivisions}x{subdivisions}";

            // Use 32-bit index buffer for high subdivision counts
            if (subdivisions > 255)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            // Calculate grid dimensions
            int vertexCountX = subdivisions + 1;
            int vertexCountY = subdivisions + 1;
            int totalVertices = vertexCountX * vertexCountY;

            // Pre-allocate arrays
            Vector3[] vertices = new Vector3[totalVertices];
            Vector2[] uvs = new Vector2[totalVertices];
            Vector3[] normals = new Vector3[totalVertices];

            // Generate vertices (10x10 size to match Unity's Plane)
            float size = 10f;
            for (int y = 0; y < vertexCountY; y++)
            {
                for (int x = 0; x < vertexCountX; x++)
                {
                    int index = y * vertexCountX + x;

                    // Normalized position (0-1)
                    float u = (float)x / subdivisions;
                    float v = (float)y / subdivisions;

                    // World position centered at origin (like Unity's Plane)
                    vertices[index] = new Vector3(
                        (u - 0.5f) * size,
                        0,
                        (v - 0.5f) * size
                    );

                    uvs[index] = new Vector2(u, v);
                    normals[index] = Vector3.up;
                }
            }

            // Generate triangles (2 triangles per quad)
            int quadCount = subdivisions * subdivisions;
            int[] triangles = new int[quadCount * 6];
            int triangleIndex = 0;

            for (int y = 0; y < subdivisions; y++)
            {
                for (int x = 0; x < subdivisions; x++)
                {
                    int vertexIndex = y * vertexCountX + x;

                    // First triangle (bottom-left)
                    triangles[triangleIndex++] = vertexIndex;
                    triangles[triangleIndex++] = vertexIndex + vertexCountX;
                    triangles[triangleIndex++] = vertexIndex + 1;

                    // Second triangle (top-right)
                    triangles[triangleIndex++] = vertexIndex + 1;
                    triangles[triangleIndex++] = vertexIndex + vertexCountX;
                    triangles[triangleIndex++] = vertexIndex + vertexCountX + 1;
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.normals = normals;

            // Optimize mesh for performance
            mesh.Optimize();

            return mesh;
        }
#endif
    }
}
