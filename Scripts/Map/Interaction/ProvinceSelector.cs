using UnityEngine;
using Map.Rendering;
using Utils;

namespace Map.Interaction
{
    /// <summary>
    /// Handles province selection and world position to province ID conversion
    /// Extracted from MapGenerator to follow single responsibility principle
    /// Provides fast mouse-to-province lookup for interaction systems
    /// </summary>
    public class ProvinceSelector : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool logSelectionDebug = false;

        // Dependencies
        private MapTextureManager textureManager;
        private Transform mapQuadTransform;

        public void Initialize(MapTextureManager textures, Transform quadTransform)
        {
            textureManager = textures;
            mapQuadTransform = quadTransform;
        }

        /// <summary>
        /// Get province ID at world position (for mouse interaction)
        /// Uses texture-based lookup for optimal performance (<1ms)
        /// </summary>
        /// <param name="worldPosition">World position to query</param>
        /// <returns>Province ID at position, or 0 if invalid</returns>
        public ushort GetProvinceAtWorldPosition(Vector3 worldPosition)
        {
            if (textureManager == null || mapQuadTransform == null)
            {
                if (logSelectionDebug)
                {
                    ArchonLogger.LogWarning("ProvinceSelector: Cannot get province - missing dependencies");
                }
                return 0;
            }

            // Convert world position to local quad space
            Vector3 localPos = mapQuadTransform.InverseTransformPoint(worldPosition);

            // Convert to UV coordinates (assuming quad is centered and scaled properly)
            float aspectRatio = (float)textureManager.MapWidth / textureManager.MapHeight;
            float quadHalfWidth = 5f * aspectRatio;
            float quadHalfHeight = 5f;

            float u = (localPos.x + quadHalfWidth) / (2f * quadHalfWidth);
            float v = (localPos.y + quadHalfHeight) / (2f * quadHalfHeight);

            // Clamp UV to valid range
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            // Convert to pixel coordinates
            int x = Mathf.FloorToInt(u * textureManager.MapWidth);
            int y = Mathf.FloorToInt(v * textureManager.MapHeight);

            // Clamp to texture bounds
            x = Mathf.Clamp(x, 0, textureManager.MapWidth - 1);
            y = Mathf.Clamp(y, 0, textureManager.MapHeight - 1);

            ushort provinceID = textureManager.GetProvinceID(x, y);

            if (logSelectionDebug)
            {
                ArchonLogger.Log($"ProvinceSelector: World {worldPosition} → Local {localPos} → UV ({u:F3},{v:F3}) → Pixel ({x},{y}) → Province {provinceID}");
            }

            return provinceID;
        }

        /// <summary>
        /// Get province ID at screen position (converts screen to world first)
        /// </summary>
        /// <param name="screenPosition">Screen position (e.g., mouse position)</param>
        /// <param name="camera">Camera to use for conversion</param>
        /// <returns>Province ID at position, or 0 if invalid</returns>
        public ushort GetProvinceAtScreenPosition(Vector2 screenPosition, Camera camera)
        {
            if (camera == null)
            {
                if (logSelectionDebug)
                {
                    ArchonLogger.LogWarning("ProvinceSelector: Cannot convert screen position - no camera provided");
                }
                return 0;
            }

            // Convert screen to world position
            Vector3 worldPos = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, camera.nearClipPlane + 1f));

            return GetProvinceAtWorldPosition(worldPos);
        }

        /// <summary>
        /// Get province ID at mouse position (convenience method)
        /// </summary>
        /// <param name="camera">Camera to use for conversion</param>
        /// <returns>Province ID at mouse position, or 0 if invalid</returns>
        public ushort GetProvinceAtMousePosition(Camera camera)
        {
            return GetProvinceAtScreenPosition(Input.mousePosition, camera);
        }
    }
}