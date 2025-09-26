using UnityEngine;
using Map.Rendering;

namespace Map
{
    /// <summary>
    /// MapGenerator orchestrates the loading and rendering of the texture-based map system.
    /// Creates a simple map display by loading provinces.bmp and rendering it with the MapCore shader.
    /// Follows the dual-layer architecture: loads simulation data and creates GPU presentation layer.
    /// </summary>
    public class MapGenerator : MonoBehaviour
    {
        [Header("Map Settings")]
        [SerializeField] private string provinceBitmapPath = "Assets/Data/map/provinces.bmp";
        [SerializeField] private string resourcesProvinceBitmapPath = "Data/map/provinces"; // For Resources.Load (without extension)
        [SerializeField] private Material mapMaterial;
        [SerializeField] private bool autoLoadOnStart = true;
        [SerializeField] private bool logLoadingProgress = true;

        [Header("Rendering")]
        [SerializeField] private Camera mapCamera;
        [SerializeField] private MeshRenderer mapRenderer;

        // Core components
        private MapTextureManager textureManager;
        private ProvinceMapping provinceMapping;

        // Map geometry
        private GameObject mapQuad;
        private Mesh mapMesh;

        public ProvinceMapping ProvinceMapping => provinceMapping;
        public MapTextureManager TextureManager => textureManager;

        void Start()
        {
            if (autoLoadOnStart)
            {
                GenerateMap();
            }
        }

        /// <summary>
        /// Generate the complete map by loading province bitmap and setting up rendering
        /// </summary>
        [ContextMenu("Generate Map")]
        public void GenerateMap()
        {
            if (logLoadingProgress)
            {
                Debug.Log("MapGenerator: Starting map generation...");
            }

            // Initialize components
            InitializeComponents();

            // Load province bitmap data
            LoadProvinceData();

            // Set up map rendering
            SetupMapRendering();

            // Configure camera
            SetupCamera();

            if (logLoadingProgress)
            {
                Debug.Log($"MapGenerator: Map generation complete. Loaded {provinceMapping?.ProvinceCount ?? 0} provinces.");
            }
        }

        /// <summary>
        /// Initialize core map components
        /// </summary>
        private void InitializeComponents()
        {
            // Get or create MapTextureManager
            textureManager = GetComponent<MapTextureManager>();
            if (textureManager == null)
            {
                textureManager = gameObject.AddComponent<MapTextureManager>();
                if (logLoadingProgress)
                {
                    Debug.Log("MapGenerator: Created MapTextureManager component");
                }
            }

            // Find or create camera
            if (mapCamera == null)
            {
                mapCamera = Camera.main;
                if (mapCamera == null)
                {
                    mapCamera = FindObjectOfType<Camera>();
                }
                if (mapCamera == null)
                {
                    Debug.LogError("MapGenerator: No camera found for map rendering");
                }
            }
        }

        /// <summary>
        /// Load province bitmap and populate texture data
        /// </summary>
        private void LoadProvinceData()
        {
            if (string.IsNullOrEmpty(provinceBitmapPath))
            {
                Debug.LogError("MapGenerator: Province bitmap path not set");
                return;
            }

            // Use absolute path for loading
            string fullPath = System.IO.Path.GetFullPath(provinceBitmapPath);

            if (logLoadingProgress)
            {
                Debug.Log($"MapGenerator: Loading province bitmap from: {fullPath}");
            }

            // Load province bitmap using existing ProvinceTextureLoader
            provinceMapping = ProvinceTextureLoader.LoadProvinceBitmap(textureManager, fullPath);

            if (provinceMapping == null)
            {
                Debug.LogError("MapGenerator: Failed to load province bitmap");
                return;
            }

            if (logLoadingProgress)
            {
                Debug.Log($"MapGenerator: Successfully loaded {provinceMapping.ProvinceCount} provinces");
            }
        }

        /// <summary>
        /// Set up map quad mesh and rendering components
        /// </summary>
        private void SetupMapRendering()
        {
            // Create map quad if it doesn't exist
            if (mapQuad == null)
            {
                mapQuad = new GameObject("MapQuad");
                mapQuad.transform.SetParent(transform);
            }

            // Get or create mesh renderer
            mapRenderer = mapQuad.GetComponent<MeshRenderer>();
            if (mapRenderer == null)
            {
                mapRenderer = mapQuad.AddComponent<MeshRenderer>();
            }

            // Get or create mesh filter
            MeshFilter meshFilter = mapQuad.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = mapQuad.AddComponent<MeshFilter>();
            }

            // Create map mesh
            CreateMapMesh();
            meshFilter.mesh = mapMesh;

            // Set up material
            SetupMaterial();
        }

        /// <summary>
        /// Create a simple quad mesh for the map
        /// </summary>
        private void CreateMapMesh()
        {
            mapMesh = new Mesh();
            mapMesh.name = "MapQuad";

            // Calculate aspect ratio from texture dimensions
            float aspectRatio = (float)textureManager.MapWidth / textureManager.MapHeight;
            float quadHeight = 10f; // Base height
            float quadWidth = quadHeight * aspectRatio;

            // Quad vertices (centered on origin)
            Vector3[] vertices = new Vector3[]
            {
                new Vector3(-quadWidth/2, -quadHeight/2, 0), // Bottom left
                new Vector3(quadWidth/2, -quadHeight/2, 0),  // Bottom right
                new Vector3(quadWidth/2, quadHeight/2, 0),   // Top right
                new Vector3(-quadWidth/2, quadHeight/2, 0)   // Top left
            };

            // UV coordinates
            Vector2[] uvs = new Vector2[]
            {
                new Vector2(0, 0), // Bottom left
                new Vector2(1, 0), // Bottom right
                new Vector2(1, 1), // Top right
                new Vector2(0, 1)  // Top left
            };

            // Triangle indices
            int[] triangles = new int[]
            {
                0, 1, 2, // First triangle
                0, 2, 3  // Second triangle
            };

            // Assign to mesh
            mapMesh.vertices = vertices;
            mapMesh.uv = uvs;
            mapMesh.triangles = triangles;
            mapMesh.RecalculateNormals();
            mapMesh.RecalculateBounds();

            if (logLoadingProgress)
            {
                Debug.Log($"MapGenerator: Created map quad {quadWidth:F1} x {quadHeight:F1} units");
            }
        }

        /// <summary>
        /// Set up material with map textures
        /// </summary>
        private void SetupMaterial()
        {
            // Create material if not assigned
            if (mapMaterial == null)
            {
                // Try to find the MapCore shader
                Shader mapShader = Shader.Find("Dominion/MapCore");
                if (mapShader != null)
                {
                    mapMaterial = new Material(mapShader);
                    mapMaterial.name = "MapGenerator_Material";
                    if (logLoadingProgress)
                    {
                        Debug.Log("MapGenerator: Created material with MapCore shader");
                    }
                }
                else
                {
                    Debug.LogError("MapGenerator: MapCore shader not found. Make sure the shader is in the project.");
                    return;
                }
            }

            // Bind textures to material
            textureManager.BindTexturesToMaterial(mapMaterial);

            // Set material to renderer
            mapRenderer.material = mapMaterial;

            // Debug: Verify textures are bound
            if (logLoadingProgress)
            {
                Debug.Log($"Texture Debug Info:");
                Debug.Log($"  ProvinceIDTexture: {(textureManager.ProvinceIDTexture != null ? "Valid" : "NULL")}");
                Debug.Log($"  ProvinceColorTexture: {(textureManager.ProvinceColorTexture != null ? "Valid" : "NULL")}");
                Debug.Log($"  ProvinceOwnerTexture: {(textureManager.ProvinceOwnerTexture != null ? "Valid" : "NULL")}");
                Debug.Log($"  Material has textures: {(mapMaterial.GetTexture("_ProvinceColorTexture") != null ? "Yes" : "No")}");

                // Sample a pixel from the color texture to verify it has data
                var testColor = textureManager.ProvinceColorTexture.GetPixel(100, 100);
                Debug.Log($"  Sample color at (100,100): RGB({testColor.r * 255}, {testColor.g * 255}, {testColor.b * 255})");
            }

            // Set initial map mode to show raw province colors directly
            // We'll use terrain mode (1) temporarily to show the ProvinceColorTexture directly
            mapMaterial.SetInt("_MapMode", 1);
            mapMaterial.EnableKeyword("MAP_MODE_TERRAIN");
            mapMaterial.SetFloat("_BorderStrength", 0.0f);  // No borders initially for clearer view
            mapMaterial.SetFloat("_HighlightStrength", 1.0f);

            if (logLoadingProgress)
            {
                Debug.Log("MapGenerator: Material setup complete with all map textures bound");
            }
        }

        /// <summary>
        /// Position and configure the camera to view the map
        /// </summary>
        private void SetupCamera()
        {
            if (mapCamera == null) return;

            // Position camera to view the entire map
            float aspectRatio = (float)textureManager.MapWidth / textureManager.MapHeight;
            float mapHeight = 10f;
            float mapWidth = mapHeight * aspectRatio;

            // Position camera above the map center
            mapCamera.transform.position = new Vector3(0, 0, -15f);
            mapCamera.transform.rotation = Quaternion.identity;

            // Set orthographic projection for top-down map view
            mapCamera.orthographic = true;
            mapCamera.orthographicSize = mapHeight * 0.6f; // Slight padding around the map

            // Clear settings for clean map display
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.backgroundColor = new Color(0.1f, 0.1f, 0.15f); // Dark blue background

            if (logLoadingProgress)
            {
                Debug.Log($"MapGenerator: Camera configured for {mapWidth:F1} x {mapHeight:F1} map view");
            }
        }

        /// <summary>
        /// Test debug mode - shows province IDs as colors
        /// </summary>
        [ContextMenu("Set Debug Mode")]
        public void SetDebugMode()
        {
            if (mapMaterial != null)
            {
                // Disable all other keywords
                mapMaterial.DisableKeyword("MAP_MODE_POLITICAL");
                mapMaterial.DisableKeyword("MAP_MODE_TERRAIN");
                mapMaterial.DisableKeyword("MAP_MODE_DEVELOPMENT");
                mapMaterial.DisableKeyword("MAP_MODE_CULTURE");

                // Enable debug mode
                mapMaterial.EnableKeyword("MAP_MODE_DEBUG");
                mapMaterial.SetInt("_MapMode", 99); // Use a special value for debug

                Debug.Log("MapGenerator: Set to DEBUG mode - showing province IDs as colors");
            }
        }

        /// <summary>
        /// Test terrain mode - shows raw province colors
        /// </summary>
        [ContextMenu("Set Terrain Mode")]
        public void SetTerrainMode()
        {
            SetMapMode(1);
            Debug.Log("MapGenerator: Set to TERRAIN mode - showing raw province colors");
        }

        /// <summary>
        /// Change the map mode (political, terrain, etc.)
        /// </summary>
        /// <param name="mode">Map mode: 0=Political, 1=Terrain, 2=Development, 3=Culture</param>
        public void SetMapMode(int mode)
        {
            if (mapMaterial != null)
            {
                mapMaterial.SetInt("_MapMode", mode);

                // Set shader keywords for map modes
                mapMaterial.DisableKeyword("MAP_MODE_POLITICAL");
                mapMaterial.DisableKeyword("MAP_MODE_TERRAIN");
                mapMaterial.DisableKeyword("MAP_MODE_DEVELOPMENT");
                mapMaterial.DisableKeyword("MAP_MODE_CULTURE");

                switch (mode)
                {
                    case 0: mapMaterial.EnableKeyword("MAP_MODE_POLITICAL"); break;
                    case 1: mapMaterial.EnableKeyword("MAP_MODE_TERRAIN"); break;
                    case 2: mapMaterial.EnableKeyword("MAP_MODE_DEVELOPMENT"); break;
                    case 3: mapMaterial.EnableKeyword("MAP_MODE_CULTURE"); break;
                }

                if (logLoadingProgress)
                {
                    Debug.Log($"MapGenerator: Changed to map mode {mode}");
                }
            }
        }

        /// <summary>
        /// Get province ID at world position (for future interaction)
        /// </summary>
        /// <param name="worldPosition">World position to query</param>
        /// <returns>Province ID at position, or 0 if invalid</returns>
        public ushort GetProvinceAtWorldPosition(Vector3 worldPosition)
        {
            if (textureManager == null || mapQuad == null) return 0;

            // Convert world position to local quad space
            Vector3 localPos = mapQuad.transform.InverseTransformPoint(worldPosition);

            // Convert to UV coordinates (assuming quad is centered and scaled properly)
            float u = (localPos.x + 5f * ((float)textureManager.MapWidth / textureManager.MapHeight)) / (10f * ((float)textureManager.MapWidth / textureManager.MapHeight));
            float v = (localPos.y + 5f) / 10f;

            // Clamp UV to valid range
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            // Convert to pixel coordinates
            int x = Mathf.FloorToInt(u * textureManager.MapWidth);
            int y = Mathf.FloorToInt(v * textureManager.MapHeight);

            return textureManager.GetProvinceID(x, y);
        }

        void OnDestroy()
        {
            // Clean up created mesh
            if (mapMesh != null)
            {
                DestroyImmediate(mapMesh);
            }

            // Clean up created material
            if (mapMaterial != null && mapMaterial.name == "MapGenerator_Material")
            {
                DestroyImmediate(mapMaterial);
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Log Map Info")]
        private void LogMapInfo()
        {
            if (provinceMapping != null)
            {
                Debug.Log($"Map Info: {provinceMapping.ProvinceCount} provinces loaded");
                Debug.Log($"Texture Size: {textureManager.MapWidth} x {textureManager.MapHeight}");
                Debug.Log($"Province Bitmap Path: {provinceBitmapPath}");
            }
            else
            {
                Debug.Log("Map Info: No map data loaded");
            }
        }
#endif
    }
}