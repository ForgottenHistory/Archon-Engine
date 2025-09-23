using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class ProvinceData
{
    public Color color;
    public Color displayColor; // The actual color to display (from political map or province map)
    public List<Vector2Int> pixels = new List<Vector2Int>();
    public Vector2 center;
    public HashSet<Vector2Int> pixelSet; // For fast lookup
    public Bounds bounds;
    public string name;
    public int id;

    // Additional properties for ProvinceDataService compatibility
    public GameObject gameObject;
    public ProvinceComponent component;
}