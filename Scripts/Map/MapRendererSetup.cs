using UnityEngine;
using Map.Rendering;

namespace Map.Setup
{
    /// <summary>
    /// Utility class for setting up the MapRenderer GameObject with all required components
    /// </summary>
    public static class MapRendererSetup
    {
        /// <summary>
        /// Creates a complete MapRenderer GameObject with proper configuration
        /// </summary>
        /// <param name="mapSize">Size of the map quad in world units</param>
        /// <param name="material">Material to use for rendering (optional)</param>
        /// <param name="parent">Parent transform (optional)</param>
        /// <returns>Configured MapRenderer GameObject</returns>
        public static GameObject CreateMapRenderer(Vector2 mapSize, Material material = null, Transform parent = null)
        {
            // Create the main GameObject
            GameObject mapRendererObject = new GameObject("MapRenderer");

            if (parent != null)
            {
                mapRendererObject.transform.SetParent(parent);
            }

            // Add MapRenderer component (automatically adds MeshFilter and MeshRenderer)
            MapRenderer mapRenderer = mapRendererObject.AddComponent<MapRenderer>();

            // Configure the map
            mapRenderer.SetMapSize(mapSize);

            if (material != null)
            {
                mapRenderer.SetMaterial(material);
            }

            // Set layer to Default (can be changed later if needed)
            mapRendererObject.layer = 0;

            ArchonLogger.Log($"MapRenderer created with size {mapSize} at position {mapRendererObject.transform.position}");

            return mapRendererObject;
        }

        /// <summary>
        /// Validate that a GameObject has the correct MapRenderer setup
        /// </summary>
        /// <param name="gameObject">GameObject to validate</param>
        /// <returns>True if properly configured</returns>
        public static bool ValidateMapRenderer(GameObject gameObject)
        {
            if (gameObject == null)
            {
                ArchonLogger.LogError("MapRenderer GameObject is null");
                return false;
            }

            MapRenderer mapRenderer = gameObject.GetComponent<MapRenderer>();
            if (mapRenderer == null)
            {
                ArchonLogger.LogError("GameObject missing MapRenderer component");
                return false;
            }

            MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.mesh == null)
            {
                ArchonLogger.LogError("MapRenderer missing MeshFilter or mesh");
                return false;
            }

            MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                ArchonLogger.LogError("MapRenderer missing MeshRenderer component");
                return false;
            }

            // Validate URP settings
            if (meshRenderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
            {
                ArchonLogger.LogWarning("MapRenderer should have shadow casting disabled for performance");
            }

            if (meshRenderer.receiveShadows)
            {
                ArchonLogger.LogWarning("MapRenderer should have receive shadows disabled for performance");
            }

            ArchonLogger.Log("MapRenderer validation passed");
            return true;
        }

        /// <summary>
        /// Get recommended map size based on province texture dimensions
        /// </summary>
        /// <param name="textureWidth">Province texture width</param>
        /// <param name="textureHeight">Province texture height</param>
        /// <param name="worldScale">Scale factor for world size</param>
        /// <returns>Recommended map size</returns>
        public static Vector2 GetRecommendedMapSize(int textureWidth, int textureHeight, float worldScale = 0.01f)
        {
            return new Vector2(textureWidth * worldScale, textureHeight * worldScale);
        }
    }
}