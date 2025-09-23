using UnityEngine;
using System.Collections.Generic;
using ProvinceSystem;

public class MapController : MonoBehaviour
{
    [Header("Core References")]
    public Camera mapCamera;
    public GameObject mapPlane;
    public ParadoxStyleCameraController cameraController;

    [Header("Map Systems")]
    public ProvinceMeshGenerator provinceMeshGenerator;
    public FastAdjacencyScanner adjacencyScanner;

    [Header("Generated Components")]
    private MapLoader mapLoader;
    private MapInteractionManager interactionManager;
    private MapModes.MapModeManager mapModeManager;

    public Texture2D MapTexture { get; private set; }
    public Material MapMaterial { get; private set; }
    public Dictionary<Color, ProvinceData> AllProvinces => provinceMeshGenerator?.GetAllProvinces();
    public Dictionary<int, ProvinceComponent> ProvinceComponents { get; private set; } = new Dictionary<int, ProvinceComponent>();
    public Dictionary<Color, int> ColorToProvinceId { get; private set; } = new Dictionary<Color, int>();

    public bool IsInitialized { get; private set; }

    void Awake()
    {
        mapLoader = GetComponent<MapLoader>();
        if (mapLoader == null)
            mapLoader = gameObject.AddComponent<MapLoader>();

        interactionManager = GetComponent<MapInteractionManager>();
        if (interactionManager == null)
            interactionManager = gameObject.AddComponent<MapInteractionManager>();

        mapModeManager = GetComponent<MapModes.MapModeManager>();
        if (mapModeManager == null)
            mapModeManager = gameObject.AddComponent<MapModes.MapModeManager>();
    }

    public void Initialize(MapSettings settings)
    {
        if (IsInitialized)
        {
            Debug.LogWarning("MapController already initialized");
            return;
        }

        StartCoroutine(InitializeSequence(settings));
    }

    private System.Collections.IEnumerator InitializeSequence(MapSettings settings)
    {
        Debug.Log("Starting map initialization sequence");

        // 1. Load map texture
        mapLoader.Initialize(this, settings);
        yield return StartCoroutine(mapLoader.LoadMapTexture());

        if (mapLoader.MapTexture == null)
        {
            Debug.LogError("Failed to load map texture. Initialization aborted.");
            yield break;
        }

        MapTexture = mapLoader.MapTexture;
        MapMaterial = mapLoader.MapMaterial;

        // 2. Setup camera
        SetupCamera(settings);
        yield return null;

        // 3. Setup camera controller
        SetupCameraController();
        yield return null;

        // 4. Generate 3D provinces if enabled
        if (settings.generate3DProvinces)
        {
            Setup3DProvinces(settings);
            yield return null;
        }

        // 5. Scan adjacencies if enabled
        if (settings.scanAdjacencies)
        {
            SetupAdjacencyScanning(settings);
            yield return null;
        }

        // 6. Setup interaction if enabled
        if (settings.enableProvinceClickDetection)
        {
            SetupProvinceClickDetection();
            yield return null;
        }

        // 7. Setup map modes if enabled
        if (settings.enableMapModes)
        {
            SetupMapModes(settings);
            yield return null;
        }

        IsInitialized = true;
        Debug.Log("Map initialization sequence completed");
    }

    private void SetupCamera(MapSettings settings)
    {
        if (mapCamera == null)
        {
            mapCamera = Camera.main;
            if (mapCamera == null)
            {
                Debug.LogWarning("No camera found, creating one");
                var cameraGO = new GameObject("Map Camera");
                mapCamera = cameraGO.AddComponent<Camera>();
            }
        }

        mapCamera.transform.position = new Vector3(0, 50, 0);
        mapCamera.transform.rotation = Quaternion.Euler(90, 0, 0);

        Debug.Log("Camera positioned for map viewing");
    }

    private void SetupCameraController()
    {
        if (cameraController == null)
        {
            cameraController = mapCamera.GetComponent<ParadoxStyleCameraController>();
        }

        if (cameraController == null)
        {
            cameraController = mapCamera.gameObject.AddComponent<ParadoxStyleCameraController>();
            Debug.Log("Created ParadoxStyleCameraController component");
        }

        cameraController.mapPlane = mapPlane;
        cameraController.mapCamera = mapCamera;
        cameraController.Initialize();

        Debug.Log("ParadoxStyleCameraController configured and initialized");
    }

    private void Setup3DProvinces(MapSettings settings)
    {
        if (provinceMeshGenerator == null)
        {
            provinceMeshGenerator = GetComponent<ProvinceMeshGenerator>();
        }

        if (provinceMeshGenerator == null)
        {
            provinceMeshGenerator = gameObject.AddComponent<ProvinceMeshGenerator>();
            Debug.Log("Created ProvinceMeshGenerator component");
        }

        provinceMeshGenerator.provinceMap = MapTexture;
        provinceMeshGenerator.mapPlane = mapPlane;
        provinceMeshGenerator.useProvinceMapColors = true;

        provinceMeshGenerator.GenerateProvinces();

        Debug.Log("3D province generation completed!");
    }

    private void SetupAdjacencyScanning(MapSettings settings)
    {
        if (adjacencyScanner == null)
        {
            adjacencyScanner = GetComponent<FastAdjacencyScanner>();
        }

        if (adjacencyScanner == null)
        {
            adjacencyScanner = gameObject.AddComponent<FastAdjacencyScanner>();
            Debug.Log("Created FastAdjacencyScanner component");
        }

        adjacencyScanner.provinceMap = MapTexture;

        var result = settings.useParallelScanning ?
            adjacencyScanner.ScanForAdjacenciesParallel() :
            adjacencyScanner.ScanForAdjacencies();

        if (result != null)
        {
            Debug.Log($"Adjacency scanning completed in {result.scanTime:F3}s! " +
                     $"Found {result.provinceCount} provinces with {result.connectionCount} adjacencies");

            ConvertAdjacenciesToIds();
        }
    }

    private void SetupProvinceClickDetection()
    {
        if (provinceMeshGenerator != null && provinceMeshGenerator.GetAllProvinces() != null)
        {
            ColorToProvinceId.Clear();
            ProvinceComponents.Clear();

            foreach (var kvp in provinceMeshGenerator.GetAllProvinces())
            {
                ColorToProvinceId[kvp.Key] = kvp.Value.id;
            }

            var allProvinceObjects = FindObjectsOfType<ProvinceComponent>();
            foreach (var comp in allProvinceObjects)
            {
                ProvinceComponents[comp.provinceId] = comp;
            }

            interactionManager.Initialize(this);

            Debug.Log($"Built color-to-ID mapping for {ColorToProvinceId.Count} provinces");
            Debug.Log($"Found {ProvinceComponents.Count} province components for highlighting");
        }
    }

    private void ConvertAdjacenciesToIds()
    {
        if (adjacencyScanner == null || provinceMeshGenerator == null) return;

        var colorToIdMap = new Dictionary<UnityEngine.Color32, int>(new ProvinceSystem.Color32Comparer());
        foreach (var kvp in provinceMeshGenerator.GetAllProvinces())
        {
            Color32 color32 = new Color32(
                (byte)(kvp.Key.r * 255),
                (byte)(kvp.Key.g * 255),
                (byte)(kvp.Key.b * 255),
                255);
            colorToIdMap[color32] = kvp.Value.id;
        }

        adjacencyScanner.ConvertToIdAdjacencies(colorToIdMap);
        Debug.Log($"Converted adjacencies to ID format for {colorToIdMap.Count} provinces");
    }

    private void SetupMapModes(MapSettings settings)
    {
        if (mapModeManager != null)
        {
            mapModeManager.mapController = this;
            mapModeManager.defaultMode = settings.defaultMapMode;
            Debug.Log("Map modes initialized");
        }
    }

    public void ScanAdjacencies(bool useParallel = true)
    {
        if (adjacencyScanner != null && MapTexture != null)
        {
            var result = useParallel ?
                adjacencyScanner.ScanForAdjacenciesParallel() :
                adjacencyScanner.ScanForAdjacencies();

            if (result != null)
            {
                Debug.Log($"Manual adjacency scan completed! " +
                         $"Found {result.provinceCount} provinces with {result.connectionCount} adjacencies");
            }
        }
        else
        {
            Debug.LogWarning("Cannot scan adjacencies: missing scanner or map texture");
        }
    }

    public void ExportAdjacencies()
    {
        if (adjacencyScanner != null)
        {
            adjacencyScanner.ExportAdjacencies();
        }
        else
        {
            Debug.LogWarning("Cannot export adjacencies: no scanner available");
        }
    }

    void OnDestroy()
    {
        if (MapTexture != null)
        {
            DestroyImmediate(MapTexture);
        }
        if (MapMaterial != null)
        {
            DestroyImmediate(MapMaterial);
        }
    }
}