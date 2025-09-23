using UnityEngine;

public class SimpleMapViewer : MonoBehaviour
{
    [Header("Map Configuration")]
    public MapSettings mapSettings = new MapSettings();

    private MapInitializer mapInitializer;

    void Start()
    {
        mapInitializer = GetComponent<MapInitializer>();
        if (mapInitializer == null)
        {
            mapInitializer = gameObject.AddComponent<MapInitializer>();
        }

        mapInitializer.mapSettings = mapSettings;
        mapInitializer.InitializeMap();
    }

    [ContextMenu("Reload Map")]
    public void ReloadMap()
    {
        mapInitializer?.InitializeMap(mapSettings);
    }

    public MapController GetMapController()
    {
        return mapInitializer?.GetMapController();
    }
}