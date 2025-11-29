using UnityEngine;

namespace Map.CameraControllers
{
    /// <summary>
    /// ENGINE LAYER: Base class for grand strategy camera controllers
    /// Provides shared functionality: pan, drag, edge scroll, wrapping, smooth movement
    /// Derived classes implement specific projection modes (orthographic vs perspective)
    /// </summary>
    public abstract class BaseCameraController : MonoBehaviour
    {
        [Header("Map References")]
        public GameObject mapPlane;
        public float mapWorldWidth = 100f;
        public float mapWorldHeight = 100f;

        [Header("Camera Settings")]
        public UnityEngine.Camera mapCamera;
        public float minZoom = 0.1f;
        public float maxZoom = 5f;
        public float zoomSpeed = 1f;
        public float currentZoom = 4f;

        [Header("Scrolling Settings")]
        public float dragSpeed = 1f;
        public float edgeScrollSpeed = 30f;
        public float edgeScrollBorder = 20f;
        public float arrowKeySpeed = 40f;
        public bool enableEdgeScroll = true;
        public bool enableDragScroll = true;
        public bool enableArrowKeys = true;

        [Header("Smooth Movement")]
        [Tooltip("Smoothing for camera position (0 = instant, higher = smoother)")]
        [Range(0f, 0.5f)]
        public float positionSmoothTime = 0.15f;

        [Tooltip("Smoothing for zoom transitions (0 = instant, higher = smoother)")]
        [Range(0f, 0.5f)]
        public float zoomSmoothTime = 0.1f;

        [Tooltip("Enable zoom-to-cursor (zoom toward mouse position instead of screen center)")]
        public bool zoomToCursor = true;

        [Tooltip("Scale movement speed with zoom level (faster when zoomed out)")]
        public bool scaleSpeedWithZoom = true;

        [Header("Wrapping")]
        public bool enableHorizontalWrapping = true;
        public bool clampVerticalMovement = true;

        [Header("Unit Visibility")]
        [Tooltip("Hide unit visuals when zoomed out above this level")]
        public float hideUnitsAboveZoom = 3.5f;

        [Header("Fog of War")]
        [Tooltip("Disable fog of war when zoomed out above this level (0 = always enabled)")]
        public float disableFogOfWarAboveZoom = 4.0f;
        [Tooltip("Fade transition time when enabling/disabling fog of war (0 = instant)")]
        [Range(0f, 2f)]
        public float fogOfWarFadeTime = 0.5f;

        [Header("Map Labels")]
        [Tooltip("Threshold for switching between province/country labels")]
        public float labelSwitchZoomThreshold = 2.5f;

        // Protected state accessible to derived classes
        protected Material mapMaterial;
        protected float currentFogOfWarDisable = 0f;
        protected float targetFogOfWarDisable = 0f;

        // Private state
        private GameObject ghostMapLeft;
        private GameObject ghostMapRight;
        private Vector3 lastMousePosition;
        private bool isDragging = false;
        protected float actualMapWidth;
        protected float actualMapHeight;
        protected bool isInitialized = false;

        // Smooth movement
        protected Vector3 targetPosition;
        protected Vector3 positionVelocity;
        protected float targetZoom;
        protected float zoomVelocity;

        void Start()
        {
            // Don't initialize yet - wait for explicit call
        }

        public virtual void Initialize()
        {
            if (isInitialized) return;

            SetupCamera();
            CalculateMapDimensions();

            if (enableHorizontalWrapping)
            {
                CreateGhostMaps();
            }

            // Apply initial zoom
            targetZoom = currentZoom;
            targetPosition = mapCamera.transform.position;
            ApplyZoom(currentZoom);

            // Find map material
            if (mapPlane != null)
            {
                var renderer = mapPlane.GetComponent<Renderer>();
                if (renderer != null)
                {
                    mapMaterial = renderer.material;
                }
            }

            UpdateFogOfWarZoomLevel();

            isInitialized = true;
            ArchonLogger.Log($"{GetType().Name} initialized successfully", "map_rendering");
        }

        /// <summary>
        /// Setup camera - derived classes override to set projection mode
        /// </summary>
        protected virtual void SetupCamera()
        {
            if (mapCamera == null)
            {
                mapCamera = GetComponent<UnityEngine.Camera>();
                if (mapCamera == null)
                {
                    mapCamera = UnityEngine.Camera.main;
                }
            }
        }

        void CalculateMapDimensions()
        {
            if (mapPlane != null)
            {
                Vector3 planeScale = mapPlane.transform.localScale;
                actualMapWidth = planeScale.x * 10f;
                actualMapHeight = planeScale.z * 10f;
                ArchonLogger.Log($"Map dimensions: {actualMapWidth} x {actualMapHeight} world units", "map_rendering");
            }
        }

        void CreateGhostMaps()
        {
            if (mapPlane == null) return;

            if (ghostMapLeft != null) DestroyImmediate(ghostMapLeft);
            if (ghostMapRight != null) DestroyImmediate(ghostMapRight);

            ghostMapLeft = Instantiate(mapPlane);
            ghostMapLeft.name = "GhostMap_Left";
            ghostMapLeft.transform.position = mapPlane.transform.position + new Vector3(-actualMapWidth, 0, 0);
            ghostMapLeft.transform.rotation = mapPlane.transform.rotation;
            ghostMapLeft.transform.localScale = mapPlane.transform.localScale;

            ghostMapRight = Instantiate(mapPlane);
            ghostMapRight.name = "GhostMap_Right";
            ghostMapRight.transform.position = mapPlane.transform.position + new Vector3(actualMapWidth, 0, 0);
            ghostMapRight.transform.rotation = mapPlane.transform.rotation;
            ghostMapRight.transform.localScale = mapPlane.transform.localScale;

            ArchonLogger.Log("Ghost maps created for seamless wrapping", "map_rendering");
        }

        void Update()
        {
            if (!isInitialized) return;

            HandleZoom();
            HandleDragScroll();
            HandleEdgeScroll();
            HandleArrowKeys();

            ApplySmoothMovement();

            if (enableHorizontalWrapping)
            {
                HandleWrapping();
                UpdateGhostMapVisibility();
            }

            if (clampVerticalMovement)
            {
                ClampVerticalPosition();
            }
        }

        void HandleZoom()
        {
            float scrollDelta = Input.GetAxis("Mouse ScrollWheel");

            if (scrollDelta != 0 && mapCamera != null)
            {
                float oldZoom = targetZoom;
                targetZoom -= scrollDelta * zoomSpeed;  // Scroll up = zoom out (lower zoom value = further distance)
                targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);

                // Zoom to cursor
                if (zoomToCursor && oldZoom != targetZoom)
                {
                    Vector3 mousePos = Input.mousePosition;
                    Vector3 worldPosBefore = GetWorldPositionAtMouse(mousePos);

                    // Derived classes handle zoom application differently
                    float zoomRatio = GetZoomRatio(targetZoom, currentZoom);

                    Vector3 offset = (worldPosBefore - GetCameraFocusPoint()) * (1f - zoomRatio);
                    targetPosition += new Vector3(offset.x, 0, offset.z);
                }
            }
        }

        void HandleDragScroll()
        {
            if (!enableDragScroll) return;

            bool dragButton = Input.GetMouseButton(2) || (Input.GetMouseButton(1) && Input.GetKey(KeyCode.LeftShift));

            if (dragButton && !isDragging)
            {
                isDragging = true;
                lastMousePosition = Input.mousePosition;
            }
            else if (!dragButton && isDragging)
            {
                isDragging = false;
            }

            if (isDragging)
            {
                Vector3 mouseDelta = Input.mousePosition - lastMousePosition;
                Vector3 worldDelta = ScreenToWorldDelta(mouseDelta);

                worldDelta *= dragSpeed;

                targetPosition += worldDelta;
                mapCamera.transform.position += worldDelta;
                lastMousePosition = Input.mousePosition;
            }
        }

        void HandleEdgeScroll()
        {
            if (!enableEdgeScroll || isDragging) return;

            Vector3 mousePos = Input.mousePosition;
            Vector3 moveDirection = Vector3.zero;

            if (mousePos.x >= 0 && mousePos.x <= edgeScrollBorder)
            {
                moveDirection.x = -1;
            }
            else if (mousePos.x <= Screen.width && mousePos.x >= Screen.width - edgeScrollBorder)
            {
                moveDirection.x = 1;
            }

            if (mousePos.y >= 0 && mousePos.y <= edgeScrollBorder)
            {
                moveDirection.z = -1;
            }
            else if (mousePos.y <= Screen.height && mousePos.y >= Screen.height - edgeScrollBorder)
            {
                moveDirection.z = 1;
            }

            if (moveDirection != Vector3.zero)
            {
                float speedMultiplier = GetZoomSpeedMultiplier();
                targetPosition += moveDirection.normalized * edgeScrollSpeed * speedMultiplier * Time.deltaTime;
            }
        }

        void HandleArrowKeys()
        {
            if (!enableArrowKeys) return;

            Vector3 moveDirection = Vector3.zero;

            if (Input.GetKey(KeyCode.LeftArrow))
            {
                moveDirection.x = -1;
            }
            else if (Input.GetKey(KeyCode.RightArrow))
            {
                moveDirection.x = 1;
            }

            if (Input.GetKey(KeyCode.DownArrow))
            {
                moveDirection.z = -1;
            }
            else if (Input.GetKey(KeyCode.UpArrow))
            {
                moveDirection.z = 1;
            }

            if (moveDirection != Vector3.zero)
            {
                float speedMultiplier = GetZoomSpeedMultiplier();
                targetPosition += moveDirection.normalized * arrowKeySpeed * speedMultiplier * Time.deltaTime;
            }
        }

        /// <summary>
        /// Apply smooth damping to camera position and zoom
        /// Derived classes override to handle zoom application differently
        /// </summary>
        protected virtual void ApplySmoothMovement()
        {
            if (mapCamera == null) return;

            // Smooth zoom transition
            if (Mathf.Abs(currentZoom - targetZoom) > 0.001f)
            {
                if (zoomSmoothTime > 0f)
                {
                    currentZoom = Mathf.SmoothDamp(currentZoom, targetZoom, ref zoomVelocity, zoomSmoothTime);
                }
                else
                {
                    currentZoom = targetZoom;
                }
                ApplyZoom(currentZoom);
            }

            UpdateFogOfWarZoomLevel();

            // Smooth position transition
            if (Vector3.Distance(mapCamera.transform.position, targetPosition) > 0.001f)
            {
                if (positionSmoothTime > 0f)
                {
                    Vector3 smoothedPosition = Vector3.SmoothDamp(mapCamera.transform.position, targetPosition, ref positionVelocity, positionSmoothTime);
                    mapCamera.transform.position = smoothedPosition;
                }
                else
                {
                    mapCamera.transform.position = targetPosition;
                }
            }
        }

        void UpdateFogOfWarZoomLevel()
        {
            if (mapMaterial == null || disableFogOfWarAboveZoom <= 0f) return;

            bool shouldDisableFog = currentZoom >= disableFogOfWarAboveZoom;
            targetFogOfWarDisable = shouldDisableFog ? 1.0f : 0.0f;

            if (Mathf.Abs(currentFogOfWarDisable - targetFogOfWarDisable) > 0.001f)
            {
                if (fogOfWarFadeTime > 0f)
                {
                    float fadeSpeed = 1f / fogOfWarFadeTime;
                    currentFogOfWarDisable = Mathf.MoveTowards(currentFogOfWarDisable, targetFogOfWarDisable, fadeSpeed * Time.deltaTime);
                }
                else
                {
                    currentFogOfWarDisable = targetFogOfWarDisable;
                }
            }

            mapMaterial.SetFloat("_FogOfWarZoomDisable", currentFogOfWarDisable);
        }

        float GetZoomSpeedMultiplier()
        {
            if (!scaleSpeedWithZoom) return 1f;

            float zoomNormalized = (currentZoom - minZoom) / (maxZoom - minZoom);
            return Mathf.Lerp(0.5f, 2.0f, zoomNormalized);
        }

        void HandleWrapping()
        {
            Vector3 camPos = mapCamera.transform.position;
            float halfWidth = actualMapWidth / 2f;

            if (camPos.x > halfWidth)
            {
                camPos.x -= actualMapWidth;
                mapCamera.transform.position = camPos;
                targetPosition.x -= actualMapWidth;
            }
            else if (camPos.x < -halfWidth)
            {
                camPos.x += actualMapWidth;
                mapCamera.transform.position = camPos;
                targetPosition.x += actualMapWidth;
            }
        }

        protected virtual void ClampVerticalPosition()
        {
            // Derived classes override - different for ortho vs perspective
        }

        void UpdateGhostMapVisibility()
        {
            if (ghostMapLeft == null || ghostMapRight == null) return;

            float camHalfWidth = GetCameraViewWidth() / 2f;
            float camX = mapCamera.transform.position.x;
            float mapEdge = actualMapWidth / 2f;

            bool seeingLeftEdge = camX - camHalfWidth < -mapEdge + 5f;
            bool seeingRightEdge = camX + camHalfWidth > mapEdge - 5f;

            ghostMapLeft.SetActive(seeingLeftEdge);
            ghostMapRight.SetActive(seeingRightEdge);
        }

        // Public API
        public void CenterCameraOn(Vector3 worldPosition)
        {
            Vector3 newPos = new Vector3(worldPosition.x, worldPosition.y, mapCamera.transform.position.z);
            targetPosition = newPos;
        }

        public Bounds GetCameraBounds()
        {
            float height = GetCameraViewHeight();
            float width = GetCameraViewWidth();
            return new Bounds(GetCameraFocusPoint(), new Vector3(width, 0, height));
        }

        // Abstract methods - derived classes must implement
        /// <summary>
        /// Apply zoom value to camera (orthographic size vs perspective distance/angle)
        /// </summary>
        protected abstract void ApplyZoom(float zoom);

        /// <summary>
        /// Get camera view height in world units
        /// </summary>
        protected abstract float GetCameraViewHeight();

        /// <summary>
        /// Get camera view width in world units
        /// </summary>
        protected abstract float GetCameraViewWidth();

        /// <summary>
        /// Get world position at mouse cursor
        /// </summary>
        protected abstract Vector3 GetWorldPositionAtMouse(Vector3 mouseScreenPos);

        /// <summary>
        /// Get camera focus point (orthographic: camera position, perspective: raycast to ground)
        /// </summary>
        protected abstract Vector3 GetCameraFocusPoint();

        /// <summary>
        /// Get zoom ratio between two zoom levels (for zoom-to-cursor)
        /// </summary>
        protected abstract float GetZoomRatio(float newZoom, float oldZoom);

        /// <summary>
        /// Convert screen space delta to world space delta (for dragging)
        /// </summary>
        protected abstract Vector3 ScreenToWorldDelta(Vector3 screenDelta);
    }
}
