using UnityEngine;
using Map.Rendering;
using Utils;
using System;

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
        [SerializeField] private bool enableSelection = true;

        // Events for Game layer to subscribe to
        public event Action<ushort> OnProvinceClicked;
        public event Action<ushort> OnProvinceHovered;
        public event Action OnSelectionCleared;

        // Dependencies
        private MapTextureManager textureManager;
        private Transform mapQuadTransform;
        private Camera mainCamera;

        // State tracking
        private ushort currentHoveredProvince = 0;
        private ushort lastSelectedProvince = 0;
        private bool isInitialized = false;

        public void Initialize(MapTextureManager textures, Transform quadTransform)
        {
            textureManager = textures;
            mapQuadTransform = quadTransform;
            mainCamera = Camera.main;
            isInitialized = true;

            if (logSelectionDebug)
            {
                ArchonLogger.Log("ProvinceSelector: Initialized with texture manager and map quad");
            }
        }

        void Update()
        {
            if (!isInitialized || !enableSelection || mainCamera == null)
            {
                return;
            }

            // Handle hover detection
            ushort hoveredProvince = GetProvinceAtMousePosition(mainCamera);
            if (hoveredProvince != currentHoveredProvince)
            {
                currentHoveredProvince = hoveredProvince;

                if (hoveredProvince != 0)
                {
                    OnProvinceHovered?.Invoke(hoveredProvince);
                }
            }

            // Handle click detection
            if (Input.GetMouseButtonDown(0))
            {
                // Get detailed info for click debugging
                Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit) && hit.transform == mapQuadTransform)
                {
                    Vector2 uv = hit.textureCoord;
                    int x = Mathf.FloorToInt(uv.x * textureManager.MapWidth);
                    int y = Mathf.FloorToInt(uv.y * textureManager.MapHeight);
                    x = Mathf.Clamp(x, 0, textureManager.MapWidth - 1);
                    y = Mathf.Clamp(y, 0, textureManager.MapHeight - 1);
                    ushort clickedProvince = textureManager.GetProvinceID(x, y);

                    if (clickedProvince != 0)
                    {
                        lastSelectedProvince = clickedProvince;
                        OnProvinceClicked?.Invoke(clickedProvince);

                        // Test: Read the texture in a small area around the click
                        ushort testNearby = textureManager.GetProvinceID(x + 10, y);
                        ArchonLogger.Log($"CLICK: UV ({uv.x:F3},{uv.y:F3}) → Pixel ({x},{y}) → Province {clickedProvince} | Nearby pixel ({x+10},{y}) → Province {testNearby}");
                    }
                }
                else
                {
                    // Clicked on non-province area (e.g., ocean, UI)
                    OnSelectionCleared?.Invoke();

                    if (logSelectionDebug)
                    {
                        ArchonLogger.Log("ProvinceSelector: Selection cleared (clicked outside provinces)");
                    }
                }
            }
        }

        /// <summary>
        /// Get the currently selected province ID
        /// </summary>
        public ushort GetSelectedProvince() => lastSelectedProvince;

        /// <summary>
        /// Get the currently hovered province ID
        /// </summary>
        public ushort GetHoveredProvince() => currentHoveredProvince;

        /// <summary>
        /// Get province ID at world position using raycast + texture lookup
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

            // Raycast from camera to world position to get UV coordinates
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                // Check if we hit the map quad
                if (hit.transform == mapQuadTransform)
                {
                    // Get UV coordinates from raycast hit
                    Vector2 uv = hit.textureCoord;

                    // Convert UV to pixel coordinates
                    int x = Mathf.FloorToInt(uv.x * textureManager.MapWidth);
                    int y = Mathf.FloorToInt(uv.y * textureManager.MapHeight);

                    // Clamp to texture bounds
                    x = Mathf.Clamp(x, 0, textureManager.MapWidth - 1);
                    y = Mathf.Clamp(y, 0, textureManager.MapHeight - 1);

                    // Get province ID from texture
                    ushort provinceID = textureManager.GetProvinceID(x, y);

                    return provinceID;
                }
            }

            // No hit or hit wrong object
            return 0;
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