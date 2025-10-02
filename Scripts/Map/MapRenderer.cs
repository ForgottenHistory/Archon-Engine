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
        /// Generates a two-triangle quad mesh with bottom-left pivot and 0-1 UV mapping
        /// </summary>
        private void SetupMapQuad()
        {
            quadMesh = new Mesh();
            quadMesh.name = "MapQuad";

            // Vertices with bottom-left pivot (0,0 at bottom-left corner)
            Vector3[] vertices = new Vector3[4]
            {
                new Vector3(0, 0, 0),              // Bottom-left
                new Vector3(mapSize.x, 0, 0),      // Bottom-right
                new Vector3(0, mapSize.y, 0),      // Top-left
                new Vector3(mapSize.x, mapSize.y, 0) // Top-right
            };

            // UV coordinates mapping 0-1 across entire quad
            Vector2[] uvs = new Vector2[4]
            {
                new Vector2(0, 0), // Bottom-left
                new Vector2(1, 0), // Bottom-right
                new Vector2(0, 1), // Top-left
                new Vector2(1, 1)  // Top-right
            };

            // Two triangles forming the quad
            int[] triangles = new int[6]
            {
                0, 2, 1, // First triangle
                2, 3, 1  // Second triangle
            };

            // Normals pointing up (positive Y)
            Vector3[] normals = new Vector3[4]
            {
                Vector3.up, Vector3.up, Vector3.up, Vector3.up
            };

            quadMesh.vertices = vertices;
            quadMesh.uv = uvs;
            quadMesh.triangles = triangles;
            quadMesh.normals = normals;

            // Optimize mesh for performance
            quadMesh.Optimize();
            quadMesh.UploadMeshData(true); // Free CPU memory

            meshFilter.mesh = quadMesh;
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