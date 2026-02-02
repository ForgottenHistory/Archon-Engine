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

        // Displacement compensation
        private Renderer mapRenderer;
        private Texture2D heightmapTexture;
        private float heightScale;
        private MeshFilter meshFilter;
        private Vector3 meshMin;
        private Vector3 meshMax;

        // State tracking
        private ushort currentHoveredProvince = 0;
        private ushort lastSelectedProvince = 0;
        private bool isInitialized = false;

        public void Initialize(MapTextureManager textures, Transform quadTransform)
        {
            textureManager = textures;
            mapQuadTransform = quadTransform;
            mainCamera = Camera.main;

            // Cache displacement data for tessellation compensation
            mapRenderer = quadTransform.GetComponent<Renderer>();
            heightmapTexture = textures.HeightmapTexture;
            if (mapRenderer != null && mapRenderer.sharedMaterial != null && mapRenderer.sharedMaterial.HasProperty("_HeightScale"))
            {
                heightScale = mapRenderer.sharedMaterial.GetFloat("_HeightScale");
            }

            meshFilter = quadTransform.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                Bounds meshBounds = meshFilter.sharedMesh.bounds;
                meshMin = meshBounds.center - meshBounds.extents;
                meshMax = meshBounds.center + meshBounds.extents;
            }

            isInitialized = true;

            if (logSelectionDebug)
            {
                ArchonLogger.Log($"ProvinceSelector: Initialized (heightScale={heightScale})", "map_interaction");
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
        /// Core raycast method using hit.point world position for UV calculation.
        /// Avoids hit.textureCoord which uses linear interpolation on coarse collider
        /// triangles — inaccurate at steep camera angles due to perspective distortion.
        /// </summary>
        private ushort RaycastForProvince(Ray ray)
        {
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit) || hit.transform != mapQuadTransform)
                return 0;

            Vector3 worldPoint = hit.point;

            // Compensate for tessellation displacement
            if (heightmapTexture != null && heightScale > 0.001f)
            {
                // Get UV at flat hit point
                Vector3 localPoint = mapQuadTransform.InverseTransformPoint(hit.point);
                float u = (localPoint.x - meshMin.x) / (meshMax.x - meshMin.x);
                float v = (localPoint.z - meshMin.z) / (meshMax.z - meshMin.z);

                // Sample heightmap with same flips as province ID lookup
                Color heightSample = heightmapTexture.GetPixelBilinear(1.0f - u, 1.0f - v);
                float displacedY = mapQuadTransform.position.y + (heightSample.r - 0.5f) * heightScale;

                // Re-intersect ray with plane at displaced Y
                if (Mathf.Abs(ray.direction.y) > 0.0001f)
                {
                    float t = (displacedY - ray.origin.y) / ray.direction.y;
                    if (t > 0)
                    {
                        worldPoint = ray.origin + ray.direction * t;
                    }
                }
            }

            return WorldPointToProvinceID(worldPoint);
        }

        /// <summary>
        /// Convert world-space point to province ID via local-space UV.
        /// </summary>
        private ushort WorldPointToProvinceID(Vector3 worldPoint)
        {
            if (textureManager == null)
                return 0;

            Vector3 localPoint = mapQuadTransform.InverseTransformPoint(worldPoint);
            float u = (localPoint.x - meshMin.x) / (meshMax.x - meshMin.x);
            float v = (localPoint.z - meshMin.z) / (meshMax.z - meshMin.z);

            int x = Mathf.FloorToInt(u * textureManager.MapWidth);
            int y = Mathf.FloorToInt(v * textureManager.MapHeight);

            x = Mathf.Clamp(x, 0, textureManager.MapWidth - 1);
            y = Mathf.Clamp(y, 0, textureManager.MapHeight - 1);

            if (logSelectionDebug)
            {
                ArchonLogger.Log($"ProvinceSelector: uv=({u:F4},{v:F4}), px=({x},{y}), id={textureManager.GetProvinceID(x, y)}", "map_interaction");
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
        /// For direct world-position queries (e.g. unit placement), not mouse input.
        /// </summary>
        public ushort GetProvinceAtWorldPosition(Vector3 worldPosition)
        {
            if (textureManager == null || mapQuadTransform == null)
                return 0;

            Vector3 localPoint = mapQuadTransform.InverseTransformPoint(worldPosition);

            var meshFilter = mapQuadTransform.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return 0;

            Bounds meshBounds = meshFilter.sharedMesh.bounds;
            Vector3 meshMin = meshBounds.center - meshBounds.extents;
            Vector3 meshMax = meshBounds.center + meshBounds.extents;

            float u = Mathf.Clamp01((localPoint.x - meshMin.x) / (meshMax.x - meshMin.x));
            float v = Mathf.Clamp01((localPoint.z - meshMin.z) / (meshMax.z - meshMin.z));

            int x = Mathf.Clamp(Mathf.FloorToInt(u * textureManager.MapWidth), 0, textureManager.MapWidth - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(v * textureManager.MapHeight), 0, textureManager.MapHeight - 1);

            return textureManager.GetProvinceID(x, y);
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
