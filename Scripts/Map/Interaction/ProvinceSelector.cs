using UnityEngine;
using UnityEngine.EventSystems;
using Map.Rendering;
using Utils;
using System;

namespace Map.Interaction
{
    /// <summary>
    /// Handles province selection and world position to province ID conversion
    /// Extracted from MapGenerator to follow single responsibility principle
    /// Provides fast mouse-to-province lookup for interaction systems
    ///
    /// Uses hit.textureCoord from MeshCollider for accurate UV-to-province mapping
    /// </summary>
    public class ProvinceSelector : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool logSelectionDebug = false;
        [SerializeField] private bool enableSelection = true;

        /// <summary>
        /// Enable or disable province selection.
        /// </summary>
        public bool SelectionEnabled
        {
            get => enableSelection;
            set => enableSelection = value;
        }

        // Events for Game layer to subscribe to
        public event Action<ushort> OnProvinceClicked;
        public event Action<ushort> OnProvinceRightClicked;
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
                ArchonLogger.Log("ProvinceSelector: Initialized with texture manager and map quad", "map_interaction");
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

            // Handle left-click detection
            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
            {
                ushort clickedProvince = RaycastForProvince(mainCamera.ScreenPointToRay(Input.mousePosition));

                if (clickedProvince != 0)
                {
                    lastSelectedProvince = clickedProvince;
                    OnProvinceClicked?.Invoke(clickedProvince);
                }
                else
                {
                    OnSelectionCleared?.Invoke();

                    if (logSelectionDebug)
                    {
                        ArchonLogger.Log("ProvinceSelector: Selection cleared (clicked outside provinces)", "map_interaction");
                    }
                }
            }

            // Handle right-click detection (for unit movement)
            if (Input.GetMouseButtonDown(1) && !IsPointerOverUI())
            {
                ushort clickedProvince = RaycastForProvince(mainCamera.ScreenPointToRay(Input.mousePosition));

                if (clickedProvince != 0)
                {
                    OnProvinceRightClicked?.Invoke(clickedProvince);
                }
            }
        }

        /// <summary>
        /// Core raycast method: ray -> hit -> textureCoord -> pixel -> province ID.
        /// Uses hit.textureCoord from MeshCollider for accurate UV mapping
        /// regardless of camera projection (orthographic or perspective).
        /// </summary>
        private ushort RaycastForProvince(Ray ray)
        {
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit) || hit.transform != mapQuadTransform)
                return 0;

            return WorldHitToProvinceID(hit.point);
        }

        /// <summary>
        /// Convert a world-space hit point on the map to a province ID.
        /// Computes UV from hit.point since hit.textureCoord is unreliable
        /// with runtime-generated meshes.
        /// MapRenderer vertices go from (0,0,0) to (mapSize.x, 0, mapSize.y)
        /// with UVs linearly mapped 0-1, so UV = localPos / meshMax.
        /// </summary>
        private ushort WorldHitToProvinceID(Vector3 worldPoint)
        {
            if (textureManager == null || mapQuadTransform == null)
                return 0;

            // Convert world point to local space of the map quad
            Vector3 localPoint = mapQuadTransform.InverseTransformPoint(worldPoint);

            var meshFilter = mapQuadTransform.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return 0;

            // Mesh goes from (0,0,0) to (max.x, 0, max.z) — use bounds.max directly
            Bounds meshBounds = meshFilter.sharedMesh.bounds;
            Vector3 meshMax = meshBounds.center + meshBounds.extents;

            // UV = local position / mesh size (vertices start at origin)
            float u = Mathf.Clamp01(localPoint.x / meshMax.x);
            float v = Mathf.Clamp01(localPoint.z / meshMax.z);

            // Flip both axes to match province texture convention
            int x = Mathf.FloorToInt((1.0f - u) * textureManager.MapWidth);
            int y = Mathf.FloorToInt((1.0f - v) * textureManager.MapHeight);

            x = Mathf.Clamp(x, 0, textureManager.MapWidth - 1);
            y = Mathf.Clamp(y, 0, textureManager.MapHeight - 1);

            if (logSelectionDebug)
            {
                ArchonLogger.Log($"ProvinceSelector: world={worldPoint}, local={localPoint}, meshMax={meshMax}, u={u:F4}, v={v:F4}, px=({x},{y}), id={textureManager.GetProvinceID(x, y)}", "map_interaction");
            }

            return textureManager.GetProvinceID(x, y);
        }

        /// <summary>
        /// Check if the mouse pointer is over any UI element (works for both uGUI and UI Toolkit)
        /// </summary>
        private bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
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
        /// Get province ID at world position using local-space UV lookup.
        /// Converts world position to mesh-local UV without raycasting.
        /// </summary>
        public ushort GetProvinceAtWorldPosition(Vector3 worldPosition)
        {
            return WorldHitToProvinceID(worldPosition);
        }

        /// <summary>
        /// Get province ID at screen position using raycast
        /// Works with both orthographic and perspective cameras
        /// </summary>
        public ushort GetProvinceAtScreenPosition(Vector2 screenPosition, Camera camera)
        {
            if (camera == null)
                return 0;

            return RaycastForProvince(camera.ScreenPointToRay(new Vector3(screenPosition.x, screenPosition.y, 0f)));
        }

        /// <summary>
        /// Get province ID at mouse position (convenience method)
        /// </summary>
        public ushort GetProvinceAtMousePosition(Camera camera)
        {
            return GetProvinceAtScreenPosition(Input.mousePosition, camera);
        }
    }
}
