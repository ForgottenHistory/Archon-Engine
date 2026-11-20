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
        [Tooltip("Fire left clicks on mouse release, and not at all when the mouse was dragged further than " +
                 "dragThresholdPixels, so a left-drag (box selection) doesn't also click a province")]
        [SerializeField] private bool leftClickOnRelease = false;
        [SerializeField] private float dragThresholdPixels = 6f;

        // Left button press tracking for leftClickOnRelease
        private bool leftPressed;
        private Vector2 leftPressPosition;
        private bool leftDragged;

        /// <summary>
        /// Fire left clicks on release instead of press, and drop them when the press turned into a
        /// drag (see IsLeftDragging). For games with left-drag box selection.
        /// </summary>
        public bool LeftClickOnRelease
        {
            get => leftClickOnRelease;
            set => leftClickOnRelease = value;
        }

        /// <summary>Screen pixels the mouse must move while pressed before a press counts as a drag.</summary>
        public float DragThresholdPixels
        {
            get => dragThresholdPixels;
            set => dragThresholdPixels = Mathf.Max(0f, value);
        }

        /// <summary>
        /// With LeftClickOnRelease: true while the left button is held and has moved past the drag
        /// threshold since it was pressed over the map (not over UI).
        /// </summary>
        public bool IsLeftDragging => leftClickOnRelease && leftPressed && leftDragged;

        /// <summary>Screen position where the current left press started (valid while pressed).</summary>
        public Vector2 LeftPressPosition => leftPressPosition;

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

        // Click interceptors: (mouse button, screen position) -> true if the click was consumed.
        // Asked in registration order before any province click handling, so game-layer map
        // objects (unit counters, markers) can take clicks that would otherwise select a province.
        private readonly System.Collections.Generic.List<Func<int, Vector2, bool>> clickInterceptors =
            new System.Collections.Generic.List<Func<int, Vector2, bool>>();

        /// <summary>
        /// Register a click interceptor: called with (mouse button 0/1, screen position) before
        /// province click handling; return true to consume the click (no province events fire).
        /// </summary>
        public void AddClickInterceptor(Func<int, Vector2, bool> interceptor)
        {
            if (interceptor != null && !clickInterceptors.Contains(interceptor))
                clickInterceptors.Add(interceptor);
        }

        public void RemoveClickInterceptor(Func<int, Vector2, bool> interceptor) => clickInterceptors.Remove(interceptor);

        // Pointer blockers: screen position -> true when a game-layer map object is under the
        // pointer, so no province counts as hovered there (no hover highlight under unit counters).
        private readonly System.Collections.Generic.List<Func<Vector2, bool>> pointerBlockers =
            new System.Collections.Generic.List<Func<Vector2, bool>>();

        /// <summary>
        /// Register a pointer blocker: while it returns true for the pointer position, no province
        /// is hovered (OnProvinceHovered(0) fires when the pointer moves onto it).
        /// </summary>
        public void AddPointerBlocker(Func<Vector2, bool> blocker)
        {
            if (blocker != null && !pointerBlockers.Contains(blocker))
                pointerBlockers.Add(blocker);
        }

        public void RemovePointerBlocker(Func<Vector2, bool> blocker) => pointerBlockers.Remove(blocker);

        private bool IsPointerBlocked()
        {
            Vector2 position = Input.mousePosition;
            foreach (var blocker in pointerBlockers)
                if (blocker(position))
                    return true;
            return false;
        }

        /// <summary>
        /// Clear the province selection as if the player clicked outside the provinces
        /// (fires OnSelectionCleared), e.g. when the game selects something else on the map.
        /// </summary>
        public void ClearSelection()
        {
            lastSelectedProvince = 0;
            OnSelectionCleared?.Invoke();
        }

        private bool TakeLeftClick()
        {
            if (!leftClickOnRelease)
                return Input.GetMouseButtonDown(0) && !IsPointerOverUI();

            if (Input.GetMouseButtonDown(0))
            {
                leftPressed = !IsPointerOverUI();
                leftDragged = false;
                leftPressPosition = Input.mousePosition;
            }
            if (leftPressed && !leftDragged &&
                ((Vector2)Input.mousePosition - leftPressPosition).sqrMagnitude > dragThresholdPixels * dragThresholdPixels)
            {
                leftDragged = true;
            }
            if (leftPressed && Input.GetMouseButtonUp(0))
            {
                leftPressed = false;
                return !leftDragged;
            }
            if (leftPressed && !Input.GetMouseButton(0))
                leftPressed = false; // release missed (focus loss)
            return false;
        }

        private bool InterceptClick(int button)
        {
            Vector2 position = Input.mousePosition;
            foreach (var interceptor in clickInterceptors)
                if (interceptor(button, position))
                    return true;
            return false;
        }

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

            // Handle hover detection (nothing is hovered while the pointer is over a blocking map object)
            ushort hoveredProvince = IsPointerBlocked() ? (ushort)0 : GetProvinceAtMousePosition(mainCamera);
            if (hoveredProvince != currentHoveredProvince)
            {
                ushort previous = currentHoveredProvince;
                currentHoveredProvince = hoveredProvince;

                if (hoveredProvince != 0 || previous != 0)
                {
                    // 0 = the pointer left the provinces (off the map or onto a blocking object)
                    OnProvinceHovered?.Invoke(hoveredProvince);
                }
            }

            // Handle left-click detection: on press, or on release when not dragged (leftClickOnRelease)
            if (TakeLeftClick() && !InterceptClick(0))
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
            if (Input.GetMouseButtonDown(1) && !IsPointerOverUI() && !InterceptClick(1))
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

            // Read live: the visual style can change _HeightScale after Initialize (and on hot reload)
            if (mapRenderer != null && mapRenderer.sharedMaterial != null && mapRenderer.sharedMaterial.HasProperty("_HeightScale"))
                heightScale = mapRenderer.sharedMaterial.GetFloat("_HeightScale");

            // Compensate for tessellation displacement: the collider is flat, the rendered
            // surface is displaced by the heightmap. Intersect the ray with the heightfield.
            if (heightmapTexture != null && heightScale > 0.001f &&
                TryIntersectHeightfield(ray, out Vector3 displacedPoint))
            {
                worldPoint = displacedPoint;
            }

            return WorldPointToProvinceID(worldPoint);
        }

        private const int HeightfieldMarchSteps = 64;
        private const int HeightfieldRefineSteps = 10;

        /// <summary>
        /// Surface height in mesh-local space at local (x, z). Must match the displacement in
        /// MapPassTerrain.hlsl: heightUV = (uv.x, 1 - uv.y), y += (h - 0.5) * _HeightScale.
        /// </summary>
        private float LocalSurfaceHeight(float localX, float localZ)
        {
            float u = (localX - meshMin.x) / (meshMax.x - meshMin.x);
            float v = (localZ - meshMin.z) / (meshMax.z - meshMin.z);
            float h = heightmapTexture.GetPixelBilinear(u, 1.0f - v).r;
            return (h - 0.5f) * heightScale;
        }

        /// <summary>
        /// Ray-march the heightfield in mesh-local space (same space the shader displaces in,
        /// so transform scale is handled), then bisect to the surface crossing.
        /// </summary>
        private bool TryIntersectHeightfield(Ray worldRay, out Vector3 worldPoint)
        {
            worldPoint = default;

            Vector3 origin = mapQuadTransform.InverseTransformPoint(worldRay.origin);
            // Vector (not Direction) form so the transform's scale applies, matching the point
            Vector3 dir = mapQuadTransform.InverseTransformVector(worldRay.direction);
            if (dir.y > -1e-6f)
                return false; // ray not pointing down onto the map

            // Displacement range is [-0.5, 0.5] * heightScale; march between those planes
            float top = 0.5f * heightScale;
            float bottom = -0.5f * heightScale;
            float tStart = Mathf.Max(0f, (top - origin.y) / dir.y);
            float tEnd = (bottom - origin.y) / dir.y;
            if (tEnd <= tStart)
                return false;

            float step = (tEnd - tStart) / HeightfieldMarchSteps;
            float tPrev = tStart;
            for (int i = 1; i <= HeightfieldMarchSteps; i++)
            {
                float t = tStart + step * i;
                Vector3 p = origin + dir * t;
                if (p.y <= LocalSurfaceHeight(p.x, p.z))
                {
                    // Crossed the surface between tPrev and t: bisect
                    float lo = tPrev, hi = t;
                    for (int j = 0; j < HeightfieldRefineSteps; j++)
                    {
                        float mid = 0.5f * (lo + hi);
                        Vector3 m = origin + dir * mid;
                        if (m.y <= LocalSurfaceHeight(m.x, m.z)) hi = mid;
                        else lo = mid;
                    }
                    worldPoint = mapQuadTransform.TransformPoint(origin + dir * hi);
                    return true;
                }
                tPrev = t;
            }

            return false;
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

            ushort id = textureManager.GetProvinceID(x, y);

            return id;
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
