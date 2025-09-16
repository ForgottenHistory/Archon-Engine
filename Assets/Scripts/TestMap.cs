using UnityEngine;
using System.Collections.Generic;
using ProvinceSystem;
using ProvinceSystem.Services;

public class TestMap : MonoBehaviour
{
    [Header("Map Settings")]
    public GameObject mapPlane;
    public Camera mapCamera;
    public Texture2D provinceTexture;
    public ProvinceDefinitionLoader provinceDefinitionLoader;

    [Header("Camera Controller")]
    public ParadoxStyleCameraController cameraController;
    
    [Header("Adjacency Scanner")]
    public FastAdjacencyScanner adjacencyScanner; // NEW
    
    [Header("Visual Settings")]
    public float mapScale = 10f; // Base scale for the map

    [Header("Debug")]
    public GameObject buttonStart;

    private Texture2D provinceMap;
    private Material mapMaterial;
    private Dictionary<Color, int> provinceColors = new Dictionary<Color, int>();
    private Dictionary<int, ProvinceData> provinces = new Dictionary<int, ProvinceData>();
    
    // For highlighting provinces
    private int selectedProvinceId = -1;
    private int hoveredProvinceId = -1;

    // Debug flags
    private bool generateOnStart = false;

    // Province data structure
    public class ProvinceData
    {
        public int id;
        public Color color;
        public Vector2 center;
        public string name;
        public List<Vector2> pixels = new List<Vector2>();
    }

    void Start()
    {
        if (generateOnStart)
            Initialize();
    }

    // Run through button
    // Let Unity compile, then we can load our stuff. Easy fix
    public void Initialize()
    {
        buttonStart.SetActive(false);

        LoadProvinceMap();
        SetupMapPlane();
        SetupCameraController();
        
        // Setup adjacency scanner BEFORE generating provinces
        SetupAdjacencyScanner();

        // Generate 3D provinces with event-driven sequencing
        var generator = GetComponent<OptimizedProvinceMeshGenerator>();
        if (generator != null)
        {
            // Subscribe to generation completion event
            generator.OnGenerationCompleted.RemoveAllListeners();
            generator.OnGenerationCompleted.AddListener(OnProvinceGenerationCompleted);
            generator.OnGenerationFailed.RemoveAllListeners();
            generator.OnGenerationFailed.AddListener(OnProvinceGenerationFailed);

            generator.provinceMap = provinceMap;
            generator.mapPlane = mapPlane;
            generator.GenerateProvinces();
        }
        else
        {
            Debug.LogError("OptimizedProvinceMeshGenerator component not found!");
        }
    }
    
    private void SetupAdjacencyScanner()
    {
        // Get or create FastAdjacencyScanner
        if (adjacencyScanner == null)
        {
            adjacencyScanner = GetComponent<FastAdjacencyScanner>();
        }
        
        if (adjacencyScanner == null)
        {
            adjacencyScanner = gameObject.AddComponent<FastAdjacencyScanner>();
            Debug.Log("Created FastAdjacencyScanner component");
        }
        
        // Configure the scanner
        adjacencyScanner.provinceMap = provinceMap;
        adjacencyScanner.ignoreDiagonals = false; // Include diagonal neighbors
        adjacencyScanner.blackThreshold = 10f; // Threshold for ocean/borders
        
        // Run the fast adjacency scan
        Debug.Log("Starting fast adjacency scan...");
        var scanResult = adjacencyScanner.ScanForAdjacenciesParallel(); // Use parallel version for speed
        
        if (scanResult != null)
        {
            Debug.Log($"Fast adjacency scan completed in {scanResult.scanTime:F3} seconds");
            Debug.Log($"Found {scanResult.provinceCount} provinces with {scanResult.connectionCount} connections");
        }
        else
        {
            Debug.LogError("Adjacency scan failed!");
        }
    }

    private void OnProvinceGenerationCompleted()
    {
        Debug.Log("Province generation completed, integrating neighbor data...");

        // Get the ProvinceDataService from the mesh generator
        var generator = GetComponent<OptimizedProvinceMeshGenerator>();
        var dataServiceField = generator.GetType()
            .GetField("dataService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dataService = dataServiceField?.GetValue(generator) as ProvinceDataService;
        
        if (dataService != null && adjacencyScanner != null)
        {
            // Convert color adjacencies to ID adjacencies using the data service
            adjacencyScanner.BuildColorToIdMapFromDataService(dataService);
            
            // Now update ProvinceManager with pre-calculated adjacency data
            var provinceManager = GetComponent<ProvinceManager>();
            if (provinceManager != null)
            {
                // Instead of building neighbor map from scratch, inject the pre-calculated data
                InjectAdjacencyDataIntoProvinceManager(provinceManager, adjacencyScanner);
                Debug.Log("Initialization sequence completed successfully with fast adjacency data!");
            }
            else
            {
                Debug.LogError("ProvinceManager component not found!");
            }
        }
        else
        {
            Debug.LogError("Could not access ProvinceDataService or AdjacencyScanner!");
        }
    }
    
    private void InjectAdjacencyDataIntoProvinceManager(ProvinceManager provinceManager, FastAdjacencyScanner scanner)
    {
        // Use reflection to inject the pre-calculated adjacency data
        var neighborField = provinceManager.GetType()
            .GetField("provinceNeighbors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (neighborField != null && scanner.IdAdjacencies != null)
        {
            // Convert to the format ProvinceManager expects
            var convertedNeighbors = new Dictionary<int, HashSet<int>>();
            foreach (var kvp in scanner.IdAdjacencies)
            {
                convertedNeighbors[kvp.Key] = new HashSet<int>(kvp.Value);
            }
            
            neighborField.SetValue(provinceManager, convertedNeighbors);
            Debug.Log($"Injected {convertedNeighbors.Count} province adjacencies into ProvinceManager");
            
            // Also update the last calculation time
            var timeField = provinceManager.GetType()
                .GetField("lastNeighborCalculationTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (timeField != null)
            {
                timeField.SetValue(provinceManager, scanner.LastScanTime);
            }
        }
        else
        {
            Debug.LogWarning("Could not inject adjacency data, falling back to traditional neighbor detection");
            provinceManager.BuildNeighborMap();
        }
    }

    private void OnProvinceGenerationFailed()
    {
        Debug.LogError("Province generation failed! Initialization sequence aborted.");
    }   

    void Update()
    {
        HandleInput();
    }

    void LoadProvinceMap()
    {
        if (provinceTexture != null)
        {
            provinceMap = provinceTexture;
            Debug.Log($"Using assigned province texture: {provinceMap.width}x{provinceMap.height}");
        }
        else
        {
            // Try loading BMP file directly using custom BMP loader
            string filePath = Application.dataPath + "/Map/provinces.bmp";

            if (System.IO.File.Exists(filePath))
            {
                Debug.Log($"Loading BMP file from: {filePath}");
                provinceMap = BMPLoader.LoadBMP(filePath);

                if (provinceMap == null)
                {
                    Debug.LogError("Failed to load BMP file with custom loader");
                    return;
                }

                Debug.Log($"Successfully loaded BMP: {provinceMap.width}x{provinceMap.height}");
            }
            else
            {
                Debug.LogError($"Province map file not found at: {filePath}");
                Debug.LogError("Please ensure provinces.bmp exists in Assets/Map/ or assign a texture to provinceTexture field");
                return;
            }
        }

        provinceMap.filterMode = FilterMode.Point;
        provinceMap.wrapMode = TextureWrapMode.Clamp;

        AnalyzeProvinces();
    }

    void AnalyzeProvinces()
    {
        Color[] pixels = provinceMap.GetPixels();
        int provinceId = 1;

        // First pass: identify unique colors and assign IDs
        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];
            if (!provinceColors.ContainsKey(pixel))
            {
                provinceColors[pixel] = provinceId;
                provinces[provinceId] = new ProvinceData 
                { 
                    id = provinceId, 
                    color = pixel,
                    name = $"Province {provinceId}"
                };
                provinceId++;
            }
        }

        // Second pass: calculate province centers
        int width = provinceMap.width;
        int height = provinceMap.height;
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color pixel = provinceMap.GetPixel(x, y);
                if (provinceColors.ContainsKey(pixel))
                {
                    int id = provinceColors[pixel];
                    provinces[id].pixels.Add(new Vector2(x, y));
                }
            }
        }

        // Calculate centers
        foreach (var province in provinces.Values)
        {
            if (province.pixels.Count > 0)
            {
                Vector2 sum = Vector2.zero;
                foreach (var pixel in province.pixels)
                {
                    sum += pixel;
                }
                province.center = sum / province.pixels.Count;
            }
        }

        Debug.Log($"Found {provinceColors.Count} unique provinces");
    }

    void SetupMapPlane()
    {
        if (mapPlane == null)
        {
            mapPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            mapPlane.name = "ProvinceMap";
        }

        Renderer renderer = mapPlane.GetComponent<Renderer>();
        if (renderer.material.shader != Shader.Find("Unlit/Texture"))
        {
            mapMaterial = new Material(Shader.Find("Unlit/Texture"));
            renderer.material = mapMaterial;
        }
        else
        {
            mapMaterial = renderer.material;
        }

        mapMaterial.mainTexture = provinceMap;

        // Calculate aspect ratio and scale plane accordingly
        float aspectRatio = (float)provinceMap.width / (float)provinceMap.height;

        Vector3 scale;
        if (aspectRatio > 1) // Wider than tall
        {
            scale = new Vector3(mapScale * aspectRatio, 1, mapScale);
        }
        else // Taller than wide
        {
            scale = new Vector3(mapScale, 1, mapScale / aspectRatio);
        }

        mapPlane.transform.rotation = Quaternion.Euler(0, 0, 0);
        mapPlane.transform.localScale = scale;

        Debug.Log($"Map aspect ratio: {aspectRatio:F2}, plane scale: {scale}");

        if (mapCamera == null)
        {
            mapCamera = Camera.main;
        }

        // Don't override camera position if controller exists
        if (mapCamera != null && cameraController == null)
        {
            // Only set default camera position if no camera controller exists yet
            ParadoxStyleCameraController existingController = mapCamera.GetComponent<ParadoxStyleCameraController>();
            if (existingController == null)
            {
                mapCamera.transform.position = new Vector3(0, 85, 0);
                mapCamera.transform.rotation = Quaternion.Euler(90, 0, 180);
            }
        }
    }

    void SetupCameraController()
    {
        // First, check if a camera controller already exists
        if (cameraController == null)
        {
            cameraController = mapCamera.GetComponent<ParadoxStyleCameraController>();
        }
        
        // If still null, try to find it in the scene
        if (cameraController == null)
        {
            cameraController = FindFirstObjectByType<ParadoxStyleCameraController>();
        }
        
        // If still not found, create it
        if (cameraController == null)
        {
            Debug.Log("No ParadoxStyleCameraController found, creating new one");
            cameraController = mapCamera.gameObject.AddComponent<ParadoxStyleCameraController>();
            
            // Only set camera position if we're creating a new controller
            mapCamera.transform.position = new Vector3(0, 85, 0);
            mapCamera.transform.rotation = Quaternion.Euler(90, 0, 180);
        }
        else
        {
            Debug.Log("Found existing ParadoxStyleCameraController");
        }

        // Configure the camera controller
        cameraController.mapPlane = mapPlane;
        cameraController.mapCamera = mapCamera;
        
        // Initialize the controller (it will handle its own checks for duplicate initialization)
        cameraController.Initialize();
        
        // Only set camera transform if it's not already in a good position
        if (mapCamera.transform.position.y < 10f || mapCamera.transform.position.y > 200f)
        {
            mapCamera.transform.position = new Vector3(mapCamera.transform.position.x, 85, mapCamera.transform.position.z);
            mapCamera.transform.rotation = Quaternion.Euler(90, 0, 180);
        }
        
        Debug.Log("Camera controller configured for Paradox-style navigation");
    }

    void HandleInput()
    {
        // Left click to select province
        if (Input.GetMouseButtonDown(0) && !Input.GetKey(KeyCode.LeftShift))
        {
            DetectProvinceAtMousePosition(true);
        }
        // Hover detection (optional - can be performance intensive)
        else if (Time.frameCount % 5 == 0) // Check every 5 frames for performance
        {
            DetectProvinceAtMousePosition(false);
        }

        // Debug: Press Space to center on selected province
        if (Input.GetKeyDown(KeyCode.Space) && selectedProvinceId > 0)
        {
            CenterCameraOnProvince(selectedProvinceId);
        }
        
        // Debug: Press N to show neighbors of selected province
        if (Input.GetKeyDown(KeyCode.N) && selectedProvinceId > 0)
        {
            ShowNeighborsOfProvince(selectedProvinceId);
        }
    }
    
    void ShowNeighborsOfProvince(int provinceId)
    {
        if (adjacencyScanner != null && adjacencyScanner.IdAdjacencies != null)
        {
            var neighbors = adjacencyScanner.GetNeighborsForId(provinceId);
            if (neighbors != null && neighbors.Count > 0)
            {
                string neighborList = string.Join(", ", neighbors);
                Debug.Log($"Province {provinceId} has {neighbors.Count} neighbors: {neighborList}");
            }
            else
            {
                Debug.Log($"Province {provinceId} has no neighbors (island or isolated)");
            }
        }
    }

    void DetectProvinceAtMousePosition(bool isClick)
    {
        Ray ray = mapCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            // Check if we hit any of the map planes (main or ghosts)
            GameObject hitObject = hit.collider.gameObject;
            if (hitObject.name.Contains("ProvinceMap") || hitObject.name.Contains("GhostMap"))
            {
                Vector2 textureCoord = hit.textureCoord;

                // Wrap texture coordinates for seamless selection
                textureCoord.x = Mathf.Repeat(textureCoord.x, 1f);
                textureCoord.y = Mathf.Clamp01(textureCoord.y);

                int pixelX = Mathf.FloorToInt(textureCoord.x * provinceMap.width);
                int pixelY = Mathf.FloorToInt(textureCoord.y * provinceMap.height);

                // Clamp to valid range
                pixelX = Mathf.Clamp(pixelX, 0, provinceMap.width - 1);
                pixelY = Mathf.Clamp(pixelY, 0, provinceMap.height - 1);

                Color pixelColor = provinceMap.GetPixel(pixelX, pixelY);

                if (provinceColors.ContainsKey(pixelColor))
                {
                    int provinceId = provinceColors[pixelColor];
                    
                    if (isClick)
                    {
                        selectedProvinceId = provinceId;
                        ProvinceData province = provinces[provinceId];
                        Debug.Log($"Selected Province: {province.name} (ID: {provinceId})");
                        Debug.Log($"World Position: {hit.point}, UV: ({textureCoord.x:F3}, {textureCoord.y:F3})");
                        
                        // Optional: Highlight the province
                        HighlightProvince(provinceId);
                    }
                    else
                    {
                        hoveredProvinceId = provinceId;
                    }
                }
            }
        }
        else if (!isClick)
        {
            hoveredProvinceId = -1;
        }
    }

    void CenterCameraOnProvince(int provinceId)
    {
        if (provinces.ContainsKey(provinceId) && cameraController != null)
        {
            ProvinceData province = provinces[provinceId];
            
            // Convert province center from texture space to world space
            float worldX = (province.center.x / provinceMap.width - 0.5f) * mapPlane.transform.localScale.x * 10f;
            float worldZ = (province.center.y / provinceMap.height - 0.5f) * mapPlane.transform.localScale.z * 10f;
            
            Vector3 worldPos = new Vector3(worldX, 0, worldZ);
            cameraController.CenterCameraOn(worldPos);
            
            Debug.Log($"Centered camera on {province.name}");
        }
    }

    void HighlightProvince(int provinceId)
    {
        // This is a placeholder for province highlighting
        // The ProvinceManager should handle the actual visual highlighting
    }

    // Public API for other systems to interact with provinces
    public ProvinceData GetProvinceData(int provinceId)
    {
        return provinces.ContainsKey(provinceId) ? provinces[provinceId] : null;
    }

    public int GetSelectedProvinceId()
    {
        return selectedProvinceId;
    }

    public int GetHoveredProvinceId()
    {
        return hoveredProvinceId;
    }
}