using UnityEngine;

namespace Map.Rendering
{
    /// <summary>
    /// Creates and manages the single quad mesh for texture-based map rendering
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MapRenderer : MonoBehaviour
    {
        [Header("Map Dimensions")]
        [SerializeField] private Vector2 mapSize = new Vector2(10f, 10f);

        [Header("Tessellation Support")]
        [SerializeField] private int subdivisions = 100; // Grid resolution (100x100 = 20,000 triangles)

        [Header("URP Settings")]
        [SerializeField] private Material mapMaterial;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh quadMesh;

        void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();

            SetupMapQuad();
            ConfigureRenderer();
        }

        /// <summary>
        /// Generates a subdivided grid mesh for tessellation support
        /// Bottom-left pivot with 0-1 UV mapping
        /// </summary>
        private void SetupMapQuad()
        {
            quadMesh = new Mesh();
            quadMesh.name = "MapGrid";

            // Use 32-bit index buffer for meshes with >65k vertices
            if (subdivisions > 255)
            {
                quadMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            // Calculate grid dimensions
            int vertexCountX = subdivisions + 1;
            int vertexCountY = subdivisions + 1;
            int totalVertices = vertexCountX * vertexCountY;

            // Pre-allocate arrays
            Vector3[] vertices = new Vector3[totalVertices];
            Vector2[] uvs = new Vector2[totalVertices];
            Vector3[] normals = new Vector3[totalVertices];

            // Generate vertices
            for (int y = 0; y < vertexCountY; y++)
            {
                for (int x = 0; x < vertexCountX; x++)
                {
                    int index = y * vertexCountX + x;

                    // Normalized position (0-1)
                    float u = (float)x / subdivisions;
                    float v = (float)y / subdivisions;

                    // World position with bottom-left pivot
                    vertices[index] = new Vector3(u * mapSize.x, 0, v * mapSize.y);
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

            quadMesh.vertices = vertices;
            quadMesh.uv = uvs;
            quadMesh.triangles = triangles;
            quadMesh.normals = normals;

            // Optimize mesh for performance
            quadMesh.Optimize();
            quadMesh.UploadMeshData(false); // Keep CPU copy for potential modifications

            meshFilter.mesh = quadMesh;

            ArchonLogger.Log($"MapRenderer: Generated {totalVertices:N0} vertices, {quadCount * 2:N0} triangles (subdivisions: {subdivisions}x{subdivisions})", "map_rendering");
        }

        /// <summary>
        /// Configure MeshRenderer for URP with SRP Batcher compatibility
        /// </summary>
        private void ConfigureRenderer()
        {
            // Disable shadows for performance
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            // Enable SRP Batcher compatibility
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // Set material if provided
            if (mapMaterial != null)
            {
                meshRenderer.material = mapMaterial;
            }
        }

        /// <summary>
        /// Update map dimensions and regenerate quad
        /// </summary>
        public void SetMapSize(Vector2 newSize)
        {
            mapSize = newSize;
            SetupMapQuad();
        }

        /// <summary>
        /// Set the material for map rendering
        /// </summary>
        public void SetMaterial(Material material)
        {
            mapMaterial = material;
            meshRenderer.material = material;
        }

        /// <summary>
        /// Get the current map material
        /// </summary>
        public Material GetMaterial()
        {
            return meshRenderer.material;
        }

        void OnDestroy()
        {
            if (quadMesh != null)
            {
                DestroyImmediate(quadMesh);
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Update quad when values change in editor
            if (Application.isPlaying && meshFilter != null)
            {
                SetupMapQuad();
            }
        }
#endif
    }
}