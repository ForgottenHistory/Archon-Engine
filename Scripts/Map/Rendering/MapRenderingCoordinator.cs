using UnityEngine;
using Map.MapModes;
using Utils;

namespace Map.Rendering
{
    /// <summary>
    /// Coordinates map rendering setup including material configuration and camera positioning
    /// Extracted from MapGenerator to follow single responsibility principle
    /// Works with existing MapRenderer component for mesh creation
    /// </summary>
    public class MapRenderingCoordinator : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool logRenderingProgress = true;

        // Dependencies
        private MapTextureManager textureManager;
        private MapModeManager mapModeManager;
        private Material mapMaterial;
        private Material runtimeMaterial;  // Actual material instance used by renderer
        private Camera mapCamera;
        private MeshRenderer meshRenderer;

        public Material MapMaterial => runtimeMaterial ?? mapMaterial;  // Return runtime instance if available

        public void Initialize(MapTextureManager textures, MapModeManager modes, MeshRenderer renderer, Camera camera)
        {
            textureManager = textures;
            mapModeManager = modes;
            meshRenderer = renderer;
            mapCamera = camera;
        }

        /// <summary>
        /// Set up complete map rendering system
        /// </summary>
        public void SetupMapRendering()
        {
            if (logRenderingProgress)
            {
                DominionLogger.LogMapInit("MapRenderingCoordinator: Setting up map rendering system...");
            }

            // Set up material
            SetupMaterial();

            // Configure camera
            SetupCamera();

            if (logRenderingProgress)
            {
                DominionLogger.LogMapInit("MapRenderingCoordinator: Map rendering setup complete");
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
                    mapMaterial.name = "MapRenderingCoordinator_Material";
                    if (logRenderingProgress)
                    {
                        DominionLogger.LogMapInit("MapRenderingCoordinator: Created material with MapCore shader");
                    }
                }
                else
                {
                    DominionLogger.LogError("MapRenderingCoordinator: MapCore shader not found. Make sure the shader is in the project.");
                    return;
                }
            }

            // Set material to renderer FIRST (this creates a material instance)
            meshRenderer.material = mapMaterial;

            // Now bind textures to the ACTUAL runtime material instance
            runtimeMaterial = meshRenderer.material;  // Store the runtime instance
            textureManager.BindTexturesToMaterial(runtimeMaterial);

            // Note: MapModeManager initialization is controlled by GAME layer
            // ENGINE provides mechanism, GAME controls initialization flow and initial mode
            if (mapModeManager != null && logRenderingProgress)
            {
                DominionLogger.LogMapInit("MapRenderingCoordinator: MapModeManager ready for GAME initialization");
            }

            // Set general material properties not handled by mapmodes
            mapMaterial.SetFloat("_HighlightStrength", 1.0f);

            if (logRenderingProgress)
            {
                DominionLogger.LogMapInit("MapRenderingCoordinator: Material setup complete with all map textures bound");
            }
        }

        /// <summary>
        /// Position and configure the camera to view the map
        /// </summary>
        private void SetupCamera()
        {
            if (mapCamera == null) return;

            // Calculate map dimensions
            float aspectRatio = (float)textureManager.MapWidth / textureManager.MapHeight;

            // Set camera to orthographic for top-down map view
            mapCamera.orthographic = true;

            // Position camera above the map center
            mapCamera.transform.position = new Vector3(0, 20, 0);
            mapCamera.transform.rotation = Quaternion.Euler(90, 0, 0);

            // Set orthographic size to show the entire map with some padding
            float mapHeight = 10.0f; // Same as quad height
            mapCamera.orthographicSize = mapHeight * 0.6f; // Show with padding

            // Set appropriate clipping planes
            mapCamera.nearClipPlane = 0.1f;
            mapCamera.farClipPlane = 100f;

            if (logRenderingProgress)
            {
                DominionLogger.LogMapInit($"MapRenderingCoordinator: Camera positioned for {textureManager.MapWidth}x{textureManager.MapHeight} map");
            }
        }

        void OnDestroy()
        {
            // Clean up created material
            if (mapMaterial != null && mapMaterial.name == "MapRenderingCoordinator_Material")
            {
                DestroyImmediate(mapMaterial);
            }
        }
    }
}