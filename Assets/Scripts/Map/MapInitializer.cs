using UnityEngine;

public class MapInitializer : MonoBehaviour
{
    [Header("Map Configuration")]
    public MapSettings mapSettings = new MapSettings();

    [Header("Auto Start")]
    public bool initializeOnStart = true;

    private MapController mapController;

    void Start()
    {
        if (initializeOnStart)
        {
            InitializeMap();
        }
    }

    public void InitializeMap()
    {
        InitializeMap(mapSettings);
    }

    public void InitializeMap(MapSettings settings)
    {
        mapController = GetComponent<MapController>();
        if (mapController == null)
        {
            mapController = gameObject.AddComponent<MapController>();
            Debug.Log("Created MapController component");
        }

        mapController.Initialize(settings);
    }

    [ContextMenu("Initialize Map")]
    public void InitializeMapManual()
    {
        InitializeMap();
    }

    [ContextMenu("Scan Adjacencies")]
    public void ScanAdjacencies()
    {
        if (mapController != null)
        {
            mapController.ScanAdjacencies(mapSettings.useParallelScanning);
        }
        else
        {
            Debug.LogWarning("MapController not initialized");
        }
    }

    [ContextMenu("Export Adjacencies")]
    public void ExportAdjacencies()
    {
        if (mapController != null)
        {
            mapController.ExportAdjacencies();
        }
        else
        {
            Debug.LogWarning("MapController not initialized");
        }
    }

    public MapController GetMapController()
    {
        return mapController;
    }
}