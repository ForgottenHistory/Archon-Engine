using UnityEngine;
using System.Collections.Generic;
using Map.Rendering;

namespace Map.Utils
{
    /// <summary>
    /// Utility for converting province IDs to world positions.
    /// Calculates center from province pixel data and converts to world space.
    /// Caches results for performance.
    /// </summary>
    public class ProvinceCenterLookup
    {
        private ProvinceMapping provinceMapping;
        private Transform mapMeshTransform;
        private Bounds meshBounds;

        // Cache for performance (province centers don't change at runtime)
        private Dictionary<ushort, Vector3> cachedCenters = new Dictionary<ushort, Vector3>();

        // Map texture dimensions
        private int textureWidth;
        private int textureHeight;

        private bool isInitialized = false;

        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Initialize the lookup system with map references
        /// </summary>
        public void Initialize(ProvinceMapping mapping, Transform meshTransform, int texWidth, int texHeight)
        {
            provinceMapping = mapping;
            mapMeshTransform = meshTransform;
            textureWidth = texWidth;
            textureHeight = texHeight;

            // Get actual mesh bounds
            var meshFilter = meshTransform.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                meshBounds = meshFilter.sharedMesh.bounds;
            }
            else
            {
                // Fallback: assume Unity quad (1x1 local space, centered at origin)
                meshBounds = new Bounds(Vector3.zero, new Vector3(1f, 0f, 1f));
                ArchonLogger.LogWarning("ProvinceCenterLookup: Using default quad bounds (1x1)", "map_interaction");
            }

            isInitialized = provinceMapping != null && mapMeshTransform != null;

            if (isInitialized)
            {
                ArchonLogger.Log($"ProvinceCenterLookup: Initialized with texture {textureWidth}x{textureHeight}", "map_interaction");
                ArchonLogger.Log($"  Mesh bounds: min={meshBounds.min}, max={meshBounds.max}, size={meshBounds.size}", "map_interaction");
                ArchonLogger.Log($"  Mesh transform: pos={meshTransform.position}, scale={meshTransform.localScale}", "map_interaction");
            }
            else
            {
                ArchonLogger.LogWarning("ProvinceCenterLookup: Failed to initialize - missing dependencies", "map_interaction");
            }
        }

        /// <summary>
        /// Get world position for province center.
        /// Returns cached value if available, otherwise calculates and caches.
        /// </summary>
        public bool TryGetProvinceCenter(ushort provinceID, out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;

            if (!isInitialized)
            {
                return false;
            }

            // Check cache first
            if (cachedCenters.TryGetValue(provinceID, out worldPosition))
            {
                return true;
            }

            // Get province pixels
            var pixels = provinceMapping.GetProvincePixels(provinceID);
            if (pixels == null || pixels.Count == 0)
            {
                return false;
            }

            // Calculate center of mass in texture space
            Vector2 centerPixels = Vector2.zero;
            foreach (var pixel in pixels)
            {
                centerPixels.x += pixel.x;
                centerPixels.y += pixel.y;
            }
            centerPixels.x /= pixels.Count;
            centerPixels.y /= pixels.Count;

            // Convert from pixel coordinates to UV (0-1)
            float uvX = centerPixels.x / textureWidth;
            float uvY = centerPixels.y / textureHeight;

            // NOTE: Coordinate flipping depends on map mesh rotation
            // Standard setup: 180° Y rotation requires X flip
            // If both axes are reversed, the mesh may have different rotation
            // Current: no flip (works for non-rotated mesh)
            // uvX = 1.0f - uvX;  // Uncomment if map has 180° Y rotation
            uvY = 1.0f - uvY;     // Flip Y for texture-to-world mapping

            // Convert UV to local position using mesh bounds
            float localX = meshBounds.min.x + uvX * meshBounds.size.x;
            float localY = 0f;
            float localZ = meshBounds.min.z + uvY * meshBounds.size.z;

            Vector3 localPosition = new Vector3(localX, localY, localZ);

            // Transform to world space
            worldPosition = mapMeshTransform.TransformPoint(localPosition);

            // Add slight Y offset so objects don't sink into map
            worldPosition.y += 0.1f;

            // Cache the result
            cachedCenters[provinceID] = worldPosition;

            return true;
        }

        /// <summary>
        /// Clear the cache (useful if map is reloaded)
        /// </summary>
        public void ClearCache()
        {
            cachedCenters.Clear();
        }

        /// <summary>
        /// Get number of cached province centers
        /// </summary>
        public int GetCacheSize()
        {
            return cachedCenters.Count;
        }

        /// <summary>
        /// Get province area in pixels (for dynamic text sizing)
        /// </summary>
        public int GetProvincePixelCount(ushort provinceID)
        {
            if (!isInitialized || provinceMapping == null)
                return 0;

            var pixels = provinceMapping.GetProvincePixels(provinceID);
            return pixels?.Count ?? 0;
        }
    }
}
