using UnityEngine;
using System.Collections.Generic;
using System.Linq;

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
    public MeshMethod meshMethod = MeshMethod.MergedRectangles;
    
    public enum MeshMethod
    {
        PixelPerfect,      // One quad per pixel - most accurate
        MergedRectangles,  // Merge adjacent pixels into rectangles
        SingleQuad         // Just one quad per province (bounding box)
    }
    
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
    private float pixelToWorldX;
    private float pixelToWorldZ;
    
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
    }
    
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
        
        // Analyze the bitmap
        AnalyzeProvinceMap();
        
        // Generate individual mesh for each province
        GenerateIndividualProvinceMeshes();
        
        // Generate borders if requested
        if (generateBorders)
        {
            GenerateBorders();
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
        
        // Calculate pixel to world conversion factors
        pixelToWorldX = mapWidth / provinceMap.width;
        pixelToWorldZ = mapHeight / provinceMap.height;
    }
    
    void AnalyzeProvinceMap()
    {
        provinces.Clear();
        int width = provinceMap.width;
        int height = provinceMap.height;
        int provinceId = 1;
        
        // Check if we have a political map for colors
        bool hasPoliticalMap = (politicalMap != null && 
                                politicalMap.width == width && 
                                politicalMap.height == height);
        
        if (!hasPoliticalMap && !useProvinceMapColors)
        {
            Debug.LogWarning("No political map provided and useProvinceMapColors is false. Using province map colors.");
            useProvinceMapColors = true;
        }
        
        // First pass: collect all pixels for each province
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color pixelColor = provinceMap.GetPixel(x, y);
                
                // Skip near-black pixels (usually ocean or borders)
                if (pixelColor.r < 0.01f && pixelColor.g < 0.01f && pixelColor.b < 0.01f)
                    continue;
                
                // Round colors to avoid floating point issues
                pixelColor = new Color(
                    Mathf.Round(pixelColor.r * 255f) / 255f,
                    Mathf.Round(pixelColor.g * 255f) / 255f,
                    Mathf.Round(pixelColor.b * 255f) / 255f,
                    1f
                );
                
                if (!provinces.ContainsKey(pixelColor))
                {
                    // Determine display color
                    Color displayColor = pixelColor;
                    if (hasPoliticalMap)
                    {
                        // Use the color from the political map at this position
                        displayColor = politicalMap.GetPixel(x, y);
                    }
                    else if (useProvinceMapColors)
                    {
                        displayColor = pixelColor;
                    }
                    
                    provinces[pixelColor] = new ProvinceData
                    {
                        color = pixelColor,
                        displayColor = displayColor,
                        id = provinceId++,
                        name = $"Province_{provinceId}",
                        pixelSet = new HashSet<Vector2Int>()
                    };
                }
                
                provinces[pixelColor].pixels.Add(new Vector2Int(x, y));
                provinces[pixelColor].pixelSet.Add(new Vector2Int(x, y));
                
                // Update display color if using political map (in case province spans multiple countries)
                // This will use the color at the province center later
                if (hasPoliticalMap && provinces[pixelColor].pixels.Count == 1)
                {
                    provinces[pixelColor].displayColor = politicalMap.GetPixel(x, y);
                }
            }
        }
        
        // Remove provinces that are too small
        if (combineSmallProvinces)
        {
            var smallProvinces = provinces.Where(p => p.Value.pixels.Count < minPixelsForProvince).ToList();
            foreach (var kvp in smallProvinces)
            {
                provinces.Remove(kvp.Key);
            }
        }
        
        // Limit province count if requested
        if (limitProvinceCount && provinces.Count > maxProvincesToGenerate)
        {
            var largestProvinces = provinces.OrderByDescending(p => p.Value.pixels.Count)
                                          .Take(maxProvincesToGenerate)
                                          .ToDictionary(p => p.Key, p => p.Value);
            provinces = largestProvinces;
            Debug.Log($"Limited to {maxProvincesToGenerate} largest provinces");
        }
        
        // Calculate centers and bounds, and update display colors from political map
        foreach (var province in provinces.Values)
        {
            CalculateProvinceCenter(province);
            CalculateProvinceBounds(province);
            
            // If we have a political map, use the color at the province center
            if (hasPoliticalMap)
            {
                int centerX = Mathf.RoundToInt(province.center.x);
                int centerY = Mathf.RoundToInt(province.center.y);
                centerX = Mathf.Clamp(centerX, 0, width - 1);
                centerY = Mathf.Clamp(centerY, 0, height - 1);
                province.displayColor = politicalMap.GetPixel(centerX, centerY);
            }
        }
        
        Debug.Log($"Found {provinces.Count} provinces in bitmap");
        
        if (hasPoliticalMap)
        {
            // Count unique countries (unique display colors)
            HashSet<Color> uniqueCountries = new HashSet<Color>();
            foreach (var province in provinces.Values)
            {
                Color roundedColor = new Color(
                    Mathf.Round(province.displayColor.r * 255f) / 255f,
                    Mathf.Round(province.displayColor.g * 255f) / 255f,
                    Mathf.Round(province.displayColor.b * 255f) / 255f,
                    1f
                );
                uniqueCountries.Add(roundedColor);
            }
            Debug.Log($"Found {uniqueCountries.Count} unique countries/colors in political map");
        }
    }
    
    void CalculateProvinceCenter(ProvinceData province)
    {
        Vector2 sum = Vector2.zero;
        foreach (var pixel in province.pixels)
        {
            sum += new Vector2(pixel.x, pixel.y);
        }
        province.center = sum / province.pixels.Count;
    }
    
    void CalculateProvinceBounds(ProvinceData province)
    {
        if (province.pixels.Count == 0) return;
        
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        
        foreach (var pixel in province.pixels)
        {
            minX = Mathf.Min(minX, pixel.x);
            maxX = Mathf.Max(maxX, pixel.x);
            minY = Mathf.Min(minY, pixel.y);
            maxY = Mathf.Max(maxY, pixel.y);
        }
        
        Vector3 min = PixelToWorldPosition(new Vector2Int(minX, minY));
        Vector3 max = PixelToWorldPosition(new Vector2Int(maxX, maxY));
        
        province.bounds = new Bounds();
        province.bounds.SetMinMax(min, max);
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
            Mesh provinceMesh = GenerateProvinceMesh(province);
            
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
    
    Mesh GenerateProvinceMesh(ProvinceData province)
    {
        switch (meshMethod)
        {
            case MeshMethod.PixelPerfect:
                return GeneratePixelPerfectMesh(province);
            case MeshMethod.MergedRectangles:
                return GenerateMergedRectangleMesh(province);
            case MeshMethod.SingleQuad:
                return GenerateSingleQuadMesh(province);
            default:
                return GenerateMergedRectangleMesh(province);
        }
    }
    
    Mesh GeneratePixelPerfectMesh(ProvinceData province)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();
        
        foreach (var pixel in province.pixels)
        {
            Vector3 worldPos = PixelToWorldPosition(pixel);
            
            // Create a quad for this pixel
            int baseIndex = vertices.Count;
            
            float halfPixelX = pixelToWorldX * 0.5f;
            float halfPixelZ = pixelToWorldZ * 0.5f;
            
            vertices.Add(worldPos + new Vector3(-halfPixelX, 0, -halfPixelZ));
            vertices.Add(worldPos + new Vector3(halfPixelX, 0, -halfPixelZ));
            vertices.Add(worldPos + new Vector3(halfPixelX, 0, halfPixelZ));
            vertices.Add(worldPos + new Vector3(-halfPixelX, 0, halfPixelZ));
            
            // UVs
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(1, 1));
            uvs.Add(new Vector2(0, 1));
            
            // Create triangles (counter-clockwise winding because Y is flipped)
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 3);
            triangles.Add(baseIndex + 2);
        }

        Mesh mesh = new Mesh();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }
    
    Mesh GenerateMergedRectangleMesh(ProvinceData province)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();
        
        // Create rectangles by scanning horizontally
        HashSet<Vector2Int> processed = new HashSet<Vector2Int>();
        
        foreach (var pixel in province.pixels)
        {
            if (processed.Contains(pixel)) continue;
            
            // Find the extent of this horizontal run
            int runLength = 1;
            while (province.pixelSet.Contains(new Vector2Int(pixel.x + runLength, pixel.y)) &&
                   !processed.Contains(new Vector2Int(pixel.x + runLength, pixel.y)))
            {
                runLength++;
            }
            
            // Now try to extend this rectangle vertically
            int runHeight = 1;
            bool canExtend = true;
            
            while (canExtend && runHeight < 10) // Limit height for performance
            {
                for (int x = pixel.x; x < pixel.x + runLength; x++)
                {
                    Vector2Int testPixel = new Vector2Int(x, pixel.y + runHeight);
                    if (!province.pixelSet.Contains(testPixel) || processed.Contains(testPixel))
                    {
                        canExtend = false;
                        break;
                    }
                }
                if (canExtend) runHeight++;
            }
            
            // Mark all pixels in this rectangle as processed
            for (int y = pixel.y; y < pixel.y + runHeight; y++)
            {
                for (int x = pixel.x; x < pixel.x + runLength; x++)
                {
                    processed.Add(new Vector2Int(x, y));
                }
            }
            
            // Create a quad for this rectangle
            Vector3 bottomLeft = PixelToWorldPosition(pixel);
            Vector3 topRight = PixelToWorldPosition(new Vector2Int(pixel.x + runLength, pixel.y + runHeight));
            
            int baseIndex = vertices.Count;
            
            vertices.Add(new Vector3(bottomLeft.x - pixelToWorldX * 0.5f, provinceHeight, bottomLeft.z - pixelToWorldZ * 0.5f));
            vertices.Add(new Vector3(topRight.x - pixelToWorldX * 0.5f, provinceHeight, bottomLeft.z - pixelToWorldZ * 0.5f));
            vertices.Add(new Vector3(topRight.x - pixelToWorldX * 0.5f, provinceHeight, topRight.z - pixelToWorldZ * 0.5f));
            vertices.Add(new Vector3(bottomLeft.x - pixelToWorldX * 0.5f, provinceHeight, topRight.z - pixelToWorldZ * 0.5f));
            
            // UVs
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(1, 1));
            uvs.Add(new Vector2(0, 1));
            
            // Create triangles (counter-clockwise winding because Y is flipped)
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 3);
            triangles.Add(baseIndex + 2);
        }

        Mesh mesh = new Mesh();

        // Use 32-bit index buffer if needed (for provinces with many vertices)
        if (vertices.Count > 65535)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }
    
    Mesh GenerateSingleQuadMesh(ProvinceData province)
    {
        // Simple single quad based on bounds
        Mesh mesh = new Mesh();
        
        Vector3 center = province.bounds.center;
        Vector3 size = province.bounds.size;
        
        // Ensure the quad is at the correct height
        center.y = provinceHeight;
        
        // Create vertices for a quad
        Vector3[] vertices = new Vector3[4];
        vertices[0] = center + new Vector3(-size.x/2, 0, -size.z/2);
        vertices[1] = center + new Vector3(size.x/2, 0, -size.z/2);
        vertices[2] = center + new Vector3(size.x/2, 0, size.z/2);
        vertices[3] = center + new Vector3(-size.x/2, 0, size.z/2);
        
        // Create triangles (counter-clockwise winding because Y is flipped)
        int[] triangles = new int[] { 0, 2, 1, 0, 3, 2 };
        
        // Create UVs
        Vector2[] uvs = new Vector2[4];
        uvs[0] = new Vector2(0, 0);
        uvs[1] = new Vector2(1, 0);
        uvs[2] = new Vector2(1, 1);
        uvs[3] = new Vector2(0, 1);
        
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }
    
    Vector3 PixelToWorldPosition(Vector2Int pixel)
    {
        // Apply same coordinate corrections as SimpleBMPMapViewer
        // Flip X coordinate to match the texture correction
        float correctedX = provinceMap.width - 1 - pixel.x;
        // Flip Y coordinate to match the texture orientation (upside down fix)
        float correctedY = provinceMap.height - 1 - pixel.y;

        float x = (correctedX / (float)provinceMap.width - 0.5f) * mapWidth;
        float z = (correctedY / (float)provinceMap.height - 0.5f) * mapHeight;
        return new Vector3(x, provinceHeight, z);
    }
    
    void GenerateBorders()
    {
        GameObject bordersContainer = new GameObject("Borders");
        bordersContainer.transform.parent = provincesContainer.transform;
        
        // We'll create border lines between provinces
        List<Vector3> borderVertices = new List<Vector3>();
        List<int> borderTriangles = new List<int>();
        
        int meshCount = 0;
        int vertexCount = 0;
        int maxVerticesPerMesh = 60000;
        
        Debug.Log("Generating province borders...");
        
        // For each province, find its border pixels
        foreach (var province in provinces.Values)
        {
            HashSet<Vector2Int> borderPixels = FindProvinceBorderPixels(province);
            
            foreach (var borderPixel in borderPixels)
            {
                // Check if we need to create a new mesh
                if (vertexCount + 4 > maxVerticesPerMesh)
                {
                    if (borderVertices.Count > 0)
                    {
                        CreateBorderMeshObject(bordersContainer, borderVertices, borderTriangles, meshCount++);
                        borderVertices.Clear();
                        borderTriangles.Clear();
                        vertexCount = 0;
                    }
                }
                
                // Create a quad for this border pixel
                // For borders, we want to cover the full pixel area like merged rectangles do
                Vector3 pixelBottomLeft = PixelToWorldPosition(borderPixel);
                Vector3 pixelTopRight = PixelToWorldPosition(new Vector2Int(borderPixel.x + 1, borderPixel.y + 1));

                float yPos = provinceHeight + 0.01f; // Slightly above provinces to avoid z-fighting
                int baseIndex = borderVertices.Count;

                // Add vertices covering the full pixel area
                borderVertices.Add(new Vector3(pixelBottomLeft.x - pixelToWorldX * 0.5f, yPos, pixelBottomLeft.z - pixelToWorldZ * 0.5f));
                borderVertices.Add(new Vector3(pixelTopRight.x - pixelToWorldX * 0.5f, yPos, pixelBottomLeft.z - pixelToWorldZ * 0.5f));
                borderVertices.Add(new Vector3(pixelTopRight.x - pixelToWorldX * 0.5f, yPos, pixelTopRight.z - pixelToWorldZ * 0.5f));
                borderVertices.Add(new Vector3(pixelBottomLeft.x - pixelToWorldX * 0.5f, yPos, pixelTopRight.z - pixelToWorldZ * 0.5f));
                
                // Add triangles
                borderTriangles.Add(baseIndex);
                borderTriangles.Add(baseIndex + 2);
                borderTriangles.Add(baseIndex + 1);
                borderTriangles.Add(baseIndex);
                borderTriangles.Add(baseIndex + 3);
                borderTriangles.Add(baseIndex + 2);
                
                vertexCount += 4;
            }
        }
        
        // Create final mesh if there are remaining vertices
        if (borderVertices.Count > 0)
        {
            CreateBorderMeshObject(bordersContainer, borderVertices, borderTriangles, meshCount);
        }
        
        Debug.Log($"Created {meshCount + 1} border mesh objects");
    }
    
    HashSet<Vector2Int> FindProvinceBorderPixels(ProvinceData province)
    {
        HashSet<Vector2Int> borderPixels = new HashSet<Vector2Int>();
        
        foreach (var pixel in province.pixels)
        {
            // Check 4 adjacent pixels
            Vector2Int[] neighbors = {
                new Vector2Int(pixel.x + 1, pixel.y),
                new Vector2Int(pixel.x - 1, pixel.y),
                new Vector2Int(pixel.x, pixel.y + 1),
                new Vector2Int(pixel.x, pixel.y - 1)
            };
            
            foreach (var neighbor in neighbors)
            {
                // Check if neighbor is outside the province
                if (!province.pixelSet.Contains(neighbor))
                {
                    // This pixel is on the border
                    borderPixels.Add(pixel);
                    break;
                }
            }
        }
        
        return borderPixels;
    }
    
    void CreateBorderMeshObject(GameObject parent, List<Vector3> vertices, List<int> triangles, int index)
    {
        GameObject borderObj = new GameObject($"BorderMesh_{index}");
        borderObj.transform.parent = parent.transform;
        
        MeshFilter meshFilter = borderObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = borderObj.AddComponent<MeshRenderer>();
        
        Mesh borderMesh = new Mesh();
        
        if (vertices.Count > 65535)
        {
            borderMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        
        borderMesh.vertices = vertices.ToArray();
        borderMesh.triangles = triangles.ToArray();
        borderMesh.RecalculateNormals();
        borderMesh.RecalculateBounds();
        
        meshFilter.mesh = borderMesh;
        
        // Set up the border material
        Material mat = borderMaterial != null ? 
            new Material(borderMaterial) : 
            new Material(Shader.Find("Unlit/Color"));
        mat.color = borderColor;
        meshRenderer.material = mat;
        meshRenderer.receiveShadows = false;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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

// Component to attach to each province for interaction
public class ProvinceComponent : MonoBehaviour
{
    public int provinceId;
    public string provinceName;
    public Color provinceColor; // The unique province identifier color
    public Color displayColor;  // The actual displayed color (country/political)
    public int pixelCount;
    
    private static ProvinceComponent lastHovered;
    private MeshRenderer meshRenderer;
    private Color originalColor;
    
    void Start()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null && meshRenderer.material != null)
        {
            originalColor = meshRenderer.material.color;
        }
    }
    
    void OnMouseEnter()
    {
        if (lastHovered != this)
        {
            lastHovered = this;
            //Debug.Log($"Mouse entered province: {provinceName} (ID: {provinceId}, Pixels: {pixelCount})");
            
            // Highlight on hover
            if (meshRenderer != null && meshRenderer.material != null)
            {
                meshRenderer.material.color = originalColor * 1.2f; // Brighten
            }
        }
    }
    
    void OnMouseExit()
    {
        if (lastHovered == this)
        {
            lastHovered = null;
            
            // Restore original color
            if (meshRenderer != null && meshRenderer.material != null)
            {
                meshRenderer.material.color = originalColor;
            }
        }
    }
    
    void OnMouseDown()
    {
        Debug.Log($"Clicked province: {provinceName} (ID: {provinceId}, Province Color: {provinceColor}, Display Color: {displayColor})");
    }
}