using UnityEngine;
using System.Collections.Generic;

public class ProvinceMeshGenerator : MonoBehaviour
{
    [Header("Input")]
    public Texture2D provinceMap;
    public Texture2D politicalMap; // Optional: for country colors
    public GameObject mapPlane;

    [Header("Province Generation Settings")]
    public float provinceHeight = 0.1f; // Height offset above the map
    public Material provinceMaterial; // Base material for provinces
    public bool useProvinceMapColors = true; // Use colors from the province map itself
    public bool generateBorders = false; // Disabled by default for performance
    public float borderWidth = 0.01f;
    public Color borderColor = Color.black;
    public Material borderMaterial;

    [Header("Mesh Generation Method")]
    [Tooltip("Pixel Perfect: One quad per pixel (accurate but heavy)\nMerged Rectangles: Optimize into larger rectangles\nSingle Quad: One quad per province (fastest)")]
    public ProvinceMeshBuilder.MeshMethod meshMethod = ProvinceMeshBuilder.MeshMethod.MergedRectangles;

    [Header("Optimization")]
    public bool combineSmallProvinces = true;
    public int minPixelsForProvince = 10;

    [Header("Generation Control")]
    public bool generateOnStart = false;
    public bool clearExistingProvinces = true;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool limitProvinceCount = false;
    public int maxProvincesToGenerate = 1000;

    // Data structures
    private Dictionary<Color, ProvinceData> provinces = new Dictionary<Color, ProvinceData>();
    private Dictionary<Color, GameObject> provinceGameObjects = new Dictionary<Color, GameObject>();
    private GameObject provincesContainer;
    private float mapWidth;
    private float mapHeight;
    
    void Start()
    {
        if (generateOnStart && provinceMap != null)
        {
            GenerateProvinces();
        }
    }
    
    [ContextMenu("Generate Provinces")]
    public void GenerateProvinces()
    {
        if (provinceMap == null)
        {
            Debug.LogError("No province map assigned!");
            return;
        }
        
        Debug.Log($"Starting province generation with method: {meshMethod}");
        
        // Clear existing provinces if needed
        if (clearExistingProvinces)
        {
            ClearProvinces();
        }
        
        // Setup container
        if (provincesContainer == null)
        {
            provincesContainer = new GameObject("Provinces");
            provincesContainer.transform.parent = transform;
        }
        
        // Calculate map dimensions
        CalculateMapDimensions();
        
        // Find definition loader
        ProvinceDefinitionLoader definitionLoader = FindObjectOfType<ProvinceDefinitionLoader>();

        // Analyze the bitmap
        provinces = ProvinceMapAnalyzer.AnalyzeProvinceMap(
            provinceMap,
            politicalMap,
            useProvinceMapColors,
            combineSmallProvinces,
            minPixelsForProvince,
            limitProvinceCount,
            maxProvincesToGenerate,
            mapWidth,
            mapHeight,
            provinceHeight,
            definitionLoader);
        
        // Generate individual mesh for each province
        GenerateIndividualProvinceMeshes();
        
        // Generate borders if requested
        if (generateBorders)
        {
            ProvinceBorderGenerator.GenerateBorders(
                provinces,
                provincesContainer,
                provinceMap,
                mapWidth,
                mapHeight,
                provinceHeight,
                borderColor,
                borderMaterial);
        }
        
        Debug.Log($"Province generation complete! Generated {provinceGameObjects.Count} province objects.");
    }
    
    void CalculateMapDimensions()
    {
        if (mapPlane != null)
        {
            Vector3 scale = mapPlane.transform.localScale;
            mapWidth = scale.x * 10f; // Unity plane is 10x10
            mapHeight = scale.z * 10f;
        }
        else
        {
            mapWidth = 100f;
            mapHeight = 100f;
        }
    }
    
    void GenerateIndividualProvinceMeshes()
    {
        int generatedCount = 0;
        int totalProvinces = provinces.Count;
        
        foreach (var kvp in provinces)
        {
            Color color = kvp.Key;
            ProvinceData province = kvp.Value;
            
            // Create individual GameObject for each province
            GameObject provinceObj = new GameObject($"Province_{province.id}");
            provinceObj.transform.parent = provincesContainer.transform;
            
            // Add MeshFilter and MeshRenderer
            MeshFilter meshFilter = provinceObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = provinceObj.AddComponent<MeshRenderer>();
            
            // Generate mesh based on selected method
            Mesh provinceMesh = ProvinceMeshBuilder.GenerateProvinceMesh(province, meshMethod, provinceMap, mapWidth, mapHeight, provinceHeight);
            
            if (provinceMesh != null)
            {
                meshFilter.mesh = provinceMesh;
                
                // Create material instance for this province
                Material mat = provinceMaterial != null ? 
                    new Material(provinceMaterial) : 
                    new Material(Shader.Find("Standard"));
                mat.color = province.displayColor; // Use display color instead of province color
                meshRenderer.material = mat;
                
                // Add collider for interaction
                MeshCollider collider = provinceObj.AddComponent<MeshCollider>();
                collider.sharedMesh = provinceMesh;
                
                // Add province component
                ProvinceComponent comp = provinceObj.AddComponent<ProvinceComponent>();
                comp.provinceId = province.id;
                comp.provinceName = province.name;
                comp.provinceColor = province.color;
                comp.displayColor = province.displayColor;
                comp.pixelCount = province.pixels.Count;
                
                provinceGameObjects[color] = provinceObj;
                generatedCount++;
                
                if (showDebugInfo && generatedCount % 100 == 0)
                {
                    Debug.Log($"Generated {generatedCount}/{totalProvinces} provinces...");
                }
            }
        }
    }
    
    
    [ContextMenu("Clear Provinces")]
    public void ClearProvinces()
    {
        if (provincesContainer != null)
        {
            DestroyImmediate(provincesContainer);
        }
        provinces.Clear();
        provinceGameObjects.Clear();
    }
    
    [ContextMenu("Log Province Statistics")]
    public void LogProvinceStatistics()
    {
        if (provinces.Count == 0)
        {
            Debug.Log("No provinces loaded");
            return;
        }
        
        int totalPixels = 0;
        int minPixels = int.MaxValue;
        int maxPixels = 0;
        
        foreach (var province in provinces.Values)
        {
            int pixelCount = province.pixels.Count;
            totalPixels += pixelCount;
            minPixels = Mathf.Min(minPixels, pixelCount);
            maxPixels = Mathf.Max(maxPixels, pixelCount);
        }
        
        Debug.Log($"Province Statistics:");
        Debug.Log($"- Total Provinces: {provinces.Count}");
        Debug.Log($"- Total Pixels: {totalPixels}");
        Debug.Log($"- Average Pixels per Province: {totalPixels / provinces.Count}");
        Debug.Log($"- Smallest Province: {minPixels} pixels");
        Debug.Log($"- Largest Province: {maxPixels} pixels");
    }
    
    // Public API
    public GameObject GetProvinceGameObject(Color provinceColor)
    {
        return provinceGameObjects.ContainsKey(provinceColor) ? provinceGameObjects[provinceColor] : null;
    }
    
    public ProvinceData GetProvinceData(Color provinceColor)
    {
        return provinces.ContainsKey(provinceColor) ? provinces[provinceColor] : null;
    }
    
    public Dictionary<Color, ProvinceData> GetAllProvinces()
    {
        return provinces;
    }
}