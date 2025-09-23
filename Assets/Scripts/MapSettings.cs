using UnityEngine;

[System.Serializable]
public class MapSettings
{
    [Header("Map File")]
    public string bmpFileName = "provinces.bmp";
    public float mapScale = 10f;

    [Header("3D Province Settings")]
    public bool generate3DProvinces = true;

    [Header("Adjacency Settings")]
    public bool scanAdjacencies = true;
    public bool useParallelScanning = true;

    [Header("Interaction Settings")]
    public bool enableProvinceClickDetection = true;

    public MapSettings()
    {
    }

    public MapSettings(string bmpFile, float scale = 10f)
    {
        bmpFileName = bmpFile;
        mapScale = scale;
    }
}