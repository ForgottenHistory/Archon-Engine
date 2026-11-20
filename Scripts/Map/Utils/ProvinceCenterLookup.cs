using UnityEngine;
using System.Collections.Generic;
using Map.Rendering;

namespace Map.Utils
{
    /// <summary>
    /// Utility for converting province IDs to world positions.
    ///
    /// Centers come from a one-time CPU readback of the province ID texture (one pass over the
    /// map computes every province's center of mass and pixel count). A center that falls outside
    /// its province (concave/C-shaped provinces) moves to the province's nearest pixel.
    /// ProvinceMapping no longer holds per-province pixel lists (map loading stopped filling them
    /// for speed), so the pixel-list path is only a fallback when no texture manager is given.
    /// </summary>
    public class ProvinceCenterLookup
    {
        private ProvinceMapping provinceMapping;
        private MapTextureManager textureManager;
        private Transform mapMeshTransform;
        private Bounds meshBounds;

        // Cache for performance (province centers don't change at runtime)
        private Dictionary<ushort, Vector3> cachedCenters = new Dictionary<ushort, Vector3>();

        // Per province id, from the readback: center in map pixels (row 0 = top), pixel count
        private Vector2[] pixelCenters;
        private int[] pixelCounts;
        private bool centersBuilt;

        // Map texture dimensions
        private int textureWidth;
        private int textureHeight;

        private bool isInitialized = false;

        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Initialize the lookup system with map references. Pass the texture manager so centers
        /// can be computed from the province ID texture.
        /// </summary>
        public void Initialize(ProvinceMapping mapping, Transform meshTransform, int texWidth, int texHeight,
            MapTextureManager textures = null)
        {
            provinceMapping = mapping;
            textureManager = textures;
            mapMeshTransform = meshTransform;
            textureWidth = texWidth;
            textureHeight = texHeight;
            centersBuilt = false;
            cachedCenters.Clear();

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

            isInitialized = (provinceMapping != null || textureManager != null) && mapMeshTransform != null;

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

            if (!TryGetPixelCenter(provinceID, out Vector2 centerPixels))
            {
                return false;
            }

            // Convert from pixel coordinates (row 0 = top) to UV (0-1, v up)
            float uvX = centerPixels.x / textureWidth;
            float uvY = 1.0f - centerPixels.y / textureHeight;

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
        /// Province center in map pixels (row 0 = top, pixel centers at +0.5).
        /// </summary>
        public bool TryGetPixelCenter(ushort provinceID, out Vector2 center)
        {
            center = Vector2.zero;
            if (!isInitialized)
                return false;

            EnsureCentersBuilt();
            if (pixelCenters != null)
            {
                if (provinceID >= pixelCounts.Length || pixelCounts[provinceID] == 0)
                    return false;
                center = pixelCenters[provinceID];
                return true;
            }

            // Fallback: legacy pixel lists (only filled by older loading paths)
            var pixels = provinceMapping?.GetProvincePixels(provinceID);
            if (pixels == null || pixels.Count == 0)
                return false;
            foreach (var pixel in pixels)
            {
                center.x += pixel.x + 0.5f;
                center.y += pixel.y + 0.5f;
            }
            center /= pixels.Count;
            return true;
        }

        private void EnsureCentersBuilt()
        {
            if (centersBuilt)
                return;
            centersBuilt = true;
            if (textureManager == null)
                return;

            float start = Time.realtimeSinceStartup;
            ushort[] ids = ProvinceIdReadback.Read(textureManager);
            if (ids == null || ids.Length < textureWidth * textureHeight)
            {
                ArchonLogger.LogWarning("ProvinceCenterLookup: province ID readback failed, no centers", "map_interaction");
                return;
            }

            int maxId = 0;
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] > maxId) maxId = ids[i];

            var sumX = new long[maxId + 1];
            var sumY = new long[maxId + 1];
            var counts = new int[maxId + 1];
            for (int y = 0, i = 0; y < textureHeight; y++)
            {
                for (int x = 0; x < textureWidth; x++, i++)
                {
                    int id = ids[i];
                    sumX[id] += x;
                    sumY[id] += y;
                    counts[id]++;
                }
            }

            var centers = new Vector2[maxId + 1];
            var nearestDist = new float[maxId + 1];
            bool anyOutside = false;
            for (int id = 1; id <= maxId; id++)
            {
                if (counts[id] == 0) continue;
                var c = new Vector2((float)sumX[id] / counts[id] + 0.5f, (float)sumY[id] / counts[id] + 0.5f);
                centers[id] = c;
                int px = Mathf.Clamp((int)c.x, 0, textureWidth - 1);
                int py = Mathf.Clamp((int)c.y, 0, textureHeight - 1);
                if (ids[py * textureWidth + px] != id)
                {
                    nearestDist[id] = float.MaxValue; // center of mass is outside the province
                    anyOutside = true;
                }
            }

            // Second pass only for provinces whose center of mass is outside them
            if (anyOutside)
            {
                var nearest = new Vector2[maxId + 1];
                for (int y = 0, i = 0; y < textureHeight; y++)
                {
                    for (int x = 0; x < textureWidth; x++, i++)
                    {
                        int id = ids[i];
                        if (nearestDist[id] == 0f) continue;
                        float dx = x + 0.5f - centers[id].x, dy = y + 0.5f - centers[id].y;
                        float d = dx * dx + dy * dy;
                        if (d < nearestDist[id])
                        {
                            nearestDist[id] = d;
                            nearest[id] = new Vector2(x + 0.5f, y + 0.5f);
                        }
                    }
                }
                for (int id = 1; id <= maxId; id++)
                    if (nearestDist[id] > 0f && nearestDist[id] < float.MaxValue)
                        centers[id] = nearest[id];
            }

            pixelCenters = centers;
            pixelCounts = counts;
            ArchonLogger.Log($"ProvinceCenterLookup: computed centers for provinces up to {maxId} in {(Time.realtimeSinceStartup - start) * 1000f:F0} ms", "map_interaction");
        }

        /// <summary>
        /// Clear the cache (useful if map is reloaded)
        /// </summary>
        public void ClearCache()
        {
            cachedCenters.Clear();
            centersBuilt = false;
            pixelCenters = null;
            pixelCounts = null;
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
            if (!isInitialized)
                return 0;

            EnsureCentersBuilt();
            if (pixelCounts != null)
                return provinceID < pixelCounts.Length ? pixelCounts[provinceID] : 0;

            var pixels = provinceMapping?.GetProvincePixels(provinceID);
            return pixels?.Count ?? 0;
        }
    }
}
