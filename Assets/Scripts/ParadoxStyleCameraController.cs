using UnityEngine;
using System.Collections.Generic;

public class ParadoxStyleCameraController : MonoBehaviour
{
    [Header("Map References")]
    public GameObject mapPlane;
    public float mapWorldWidth = 100f;  // This will be calculated based on your plane scale
    public float mapWorldHeight = 100f; // This will be calculated based on your plane scale
    
    [Header("Camera Settings")]
    public Camera mapCamera;
    public float minZoom = 10f;
    public float maxZoom = 100f;
    public float zoomSpeed = 10f;
    public float currentZoom = 50f;
    
    [Header("Scrolling Settings")]
    public float dragSpeed = 1f;
    public float edgeScrollSpeed = 30f;
    public float edgeScrollBorder = 20f; // Pixels from edge
    public float arrowKeySpeed = 40f;
    public bool enableEdgeScroll = true;
    public bool enableDragScroll = true;
    public bool enableArrowKeys = true;
    
    [Header("Wrapping")]
    public bool enableHorizontalWrapping = true;
    public bool clampVerticalMovement = true;
    
    // Private variables
    private GameObject ghostMapLeft;
    private GameObject ghostMapRight;
    private Vector3 lastMousePosition;
    private bool isDragging = false;
    private float actualMapWidth;
    private float actualMapHeight;
    private bool isInitialized = false;
    
    void Start()
    {
        // Don't initialize yet - wait for TestMap to set us up
        // This prevents race conditions
    }
    
    public void Initialize()
    {
        if (isInitialized) return;
        
        SetupCamera();
        CalculateMapDimensions();
        
        if (enableHorizontalWrapping)
        {
            CreateGhostMaps();
        }
        
        isInitialized = true;
        Debug.Log("ParadoxStyleCameraController initialized successfully");
    }
    
    void SetupCamera()
    {
        if (mapCamera == null)
        {
            mapCamera = GetComponent<Camera>();
            if (mapCamera == null)
            {
                mapCamera = Camera.main;
            }
        }
        
        if (mapCamera != null)
        {
            mapCamera.orthographic = true;
            mapCamera.orthographicSize = currentZoom;
        }
    }
    
    void CalculateMapDimensions()
    {
        if (mapPlane != null)
        {
            // Get the actual world size of your plane
            Vector3 planeScale = mapPlane.transform.localScale;
            
            // Unity's default plane is 10x10 units
            actualMapWidth = planeScale.x * 10f;
            actualMapHeight = planeScale.z * 10f;
            
            Debug.Log($"Map dimensions: {actualMapWidth} x {actualMapHeight} world units");
        }
    }
    
    void CreateGhostMaps()
    {
        if (mapPlane == null) return;
        
        // Clean up any existing ghost maps first
        if (ghostMapLeft != null) DestroyImmediate(ghostMapLeft);
        if (ghostMapRight != null) DestroyImmediate(ghostMapRight);
        
        // Create duplicate maps on left and right for seamless wrapping
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
        
        Debug.Log("Ghost maps created for seamless wrapping");
    }
    
    void Update()
    {
        if (!isInitialized) return;

        HandleZoom();
        HandleDragScroll();
        HandleEdgeScroll();
        HandleArrowKeys();

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
        
        if (scrollDelta != 0)
        {
            currentZoom -= scrollDelta * zoomSpeed;
            currentZoom = Mathf.Clamp(currentZoom, minZoom, maxZoom);
            
            if (mapCamera != null)
            {
                mapCamera.orthographicSize = currentZoom;
            }
        }
    }
    
    void HandleDragScroll()
    {
        if (!enableDragScroll) return;
        
        // Middle mouse button or right mouse button drag
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
            
            // Convert screen space movement to world space
            float orthoSize = mapCamera.orthographicSize;
            float screenToWorldRatio = (orthoSize * 2f) / Screen.height;
            
            Vector3 worldDelta = new Vector3(
                -mouseDelta.x * screenToWorldRatio * mapCamera.aspect,
                0,
                -mouseDelta.y * screenToWorldRatio
            );
            
            worldDelta *= dragSpeed;
            
            mapCamera.transform.position += worldDelta;
            lastMousePosition = Input.mousePosition;
        }
    }
    
    void HandleEdgeScroll()
    {
        if (!enableEdgeScroll || isDragging) return;
        
        Vector3 mousePos = Input.mousePosition;
        Vector3 moveDirection = Vector3.zero;
        
        // Check if mouse is near screen edges
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
        
        // Apply movement
        if (moveDirection != Vector3.zero)
        {
            float speedMultiplier = currentZoom / maxZoom; // Slower when zoomed in
            mapCamera.transform.position += moveDirection.normalized * edgeScrollSpeed * speedMultiplier * Time.deltaTime;
        }
    }

    void HandleArrowKeys()
    {
        if (!enableArrowKeys) return;

        Vector3 moveDirection = Vector3.zero;

        // Check arrow key inputs
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

        // Apply movement
        if (moveDirection != Vector3.zero)
        {
            float speedMultiplier = currentZoom / maxZoom; // Slower when zoomed in
            mapCamera.transform.position += moveDirection.normalized * arrowKeySpeed * speedMultiplier * Time.deltaTime;
        }
    }

    void HandleWrapping()
    {
        Vector3 camPos = mapCamera.transform.position;
        float halfWidth = actualMapWidth / 2f;
        
        // Simple wrapping without complex checks
        if (camPos.x > halfWidth)
        {
            camPos.x -= actualMapWidth;
            mapCamera.transform.position = camPos;
        }
        else if (camPos.x < -halfWidth)
        {
            camPos.x += actualMapWidth;
            mapCamera.transform.position = camPos;
        }
    }
    
    void ClampVerticalPosition()
    {
        Vector3 camPos = mapCamera.transform.position;
        
        // Calculate how far the camera can see
        float orthoHeight = mapCamera.orthographicSize;
        float orthoWidth = orthoHeight * mapCamera.aspect;
        
        // Calculate boundaries (with some padding to prevent seeing beyond the map)
        float maxZ = (actualMapHeight / 2f) - orthoHeight + 5f;
        float minZ = -(actualMapHeight / 2f) + orthoHeight - 5f;
        
        // Clamp the position
        camPos.z = Mathf.Clamp(camPos.z, minZ, maxZ);
        mapCamera.transform.position = camPos;
    }
    
    void UpdateGhostMapVisibility()
    {
        if (ghostMapLeft == null || ghostMapRight == null) return;
        
        // Calculate camera view bounds
        float camHalfWidth = mapCamera.orthographicSize * mapCamera.aspect;
        float camX = mapCamera.transform.position.x;
        
        // Check if camera view overlaps with map edges
        float mapEdge = actualMapWidth / 2f;
        
        bool seeingLeftEdge = camX - camHalfWidth < -mapEdge + 5f;
        bool seeingRightEdge = camX + camHalfWidth > mapEdge - 5f;
        
        // Enable ghost maps when needed
        ghostMapLeft.SetActive(seeingLeftEdge);
        ghostMapRight.SetActive(seeingRightEdge);
    }
    
    // Public method to center camera on a world position
    public void CenterCameraOn(Vector3 worldPosition)
    {
        Vector3 newPos = new Vector3(worldPosition.x, mapCamera.transform.position.y, worldPosition.z);
        mapCamera.transform.position = newPos;
    }
    
    // Public method to get the current camera bounds
    public Bounds GetCameraBounds()
    {
        float height = mapCamera.orthographicSize * 2f;
        float width = height * mapCamera.aspect;
        return new Bounds(mapCamera.transform.position, new Vector3(width, 0, height));
    }
}