using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using ProvinceSystem.Services;

namespace ProvinceSystem
{
    /// <summary>
    /// Optimized province mesh generator using Unity Job System and refactored services
    /// </summary>
    public class OptimizedProvinceMeshGenerator : MonoBehaviour
    {
        [Header("Input")]
        public Texture2D provinceMap;
        public Texture2D politicalMap;
        public GameObject mapPlane;
        
        [Header("Province Generation Settings")]
        public float provinceHeight = 0.1f;
        public Material provinceMaterial;
        public bool useProvinceMapColors = true;
        
        [Header("Border Generation")]
        public bool generateBorders = false;
        public float borderWidth = 0.01f;
        public Color borderColor = Color.black;
        public Material borderMaterial;
        
        [Header("Mesh Generation Method")]
        public ProvinceMeshService.MeshMethod meshMethod = ProvinceMeshService.MeshMethod.MergedRectangles;
        
        [Header("Optimization")]
        public bool combineSmallProvinces = true;
        public int minPixelsForProvince = 10;
        public bool limitProvinceCount = false;
        public int maxProvincesToGenerate = 1000;
        
        [Header("Performance")]
        public bool useJobSystem = true;
        public bool generateAsync = true;
        public int provincesPerFrame = 50; // For async generation
        
        [Header("Generation Control")]
        public bool generateOnStart = false;
        public bool clearExistingProvinces = true;
        
        [Header("Debug")]
        public bool showDebugInfo = true;
        public bool showGenerationProgress = true;

        [Header("Events")]
        public UnityEvent OnGenerationStarted = new UnityEvent();
        public UnityEvent OnGenerationCompleted = new UnityEvent();
        public UnityEvent OnGenerationFailed = new UnityEvent();

        // Services
        private ProvinceDataService dataService;
        private ProvinceAnalysisService analysisService;
        private ProvinceMeshService meshService;
        private ProvinceBorderService borderService;
        
        // Generation state
        private GameObject provincesContainer;
        private float mapWidth;
        private float mapHeight;
        private bool isGenerating = false;
        private float generationStartTime;
        
        void Awake()
        {
            InitializeServices();
        }
        
        void Start()
        {
            if (generateOnStart && provinceMap != null)
            {
                GenerateProvinces();
            }
        }
        
        private void InitializeServices()
        {
            dataService = new ProvinceDataService();
            analysisService = new ProvinceAnalysisService();
            meshService = new ProvinceMeshService();
            borderService = new ProvinceBorderService();
        }
        
        [ContextMenu("Generate Provinces")]
        public void GenerateProvinces()
        {
            if (provinceMap == null)
            {
                Debug.LogError("No province map assigned!");
                OnGenerationFailed?.Invoke();
                return;
            }

            if (isGenerating)
            {
                Debug.LogWarning("Generation already in progress!");
                return;
            }

            if (generateAsync)
            {
                StartCoroutine(GenerateProvincesAsync());
            }
            else
            {
                GenerateProvincesSync();
            }
        }
        
        private void GenerateProvincesSync()
        {
            generationStartTime = Time.realtimeSinceStartup;
            Debug.Log($"Starting province generation (Sync, Job System: {useJobSystem})");

            OnGenerationStarted?.Invoke();

            // Clear existing provinces
            if (clearExistingProvinces)
            {
                ClearProvinces();
            }
            
            // Setup
            SetupContainer();
            CalculateMapDimensions();
            
            // Analyze provinces
            var analysisResult = AnalyzeProvinces();
            
            // Generate meshes
            GenerateAllProvinceMeshes(analysisResult);
            
            // Generate borders
            if (generateBorders)
            {
                GenerateBorders();
            }

            float generationTime = Time.realtimeSinceStartup - generationStartTime;
            Debug.Log($"Province generation complete! Generated {dataService.GetProvinceCount()} provinces in {generationTime:F2} seconds");

            if (showDebugInfo)
            {
                LogStatistics();
            }

            OnGenerationCompleted?.Invoke();
        }
        
        private IEnumerator GenerateProvincesAsync()
        {
            isGenerating = true;
            generationStartTime = Time.realtimeSinceStartup;
            Debug.Log($"Starting province generation (Async, Job System: {useJobSystem})");

            OnGenerationStarted?.Invoke();

            // Clear existing provinces
            if (clearExistingProvinces)
            {
                ClearProvinces();
            }
            
            // Setup
            SetupContainer();
            CalculateMapDimensions();
            
            // Analyze provinces
            if (showGenerationProgress)
                Debug.Log("Phase 1: Analyzing province map...");
            
            var analysisResult = AnalyzeProvinces();
            yield return null; // Give frame back
            
            // Generate meshes in batches
            if (showGenerationProgress)
                Debug.Log($"Phase 2: Generating {analysisResult.provinces.Count} province meshes...");
            
            yield return GenerateProvinceMeshesAsync(analysisResult);
            
            // Generate borders
            if (generateBorders)
            {
                if (showGenerationProgress)
                    Debug.Log("Phase 3: Generating borders...");
                
                GenerateBorders();
                yield return null;
            }

            float generationTime = Time.realtimeSinceStartup - generationStartTime;
            Debug.Log($"Province generation complete! Generated {dataService.GetProvinceCount()} provinces in {generationTime:F2} seconds");

            if (showDebugInfo)
            {
                LogStatistics();
            }

            isGenerating = false;
            OnGenerationCompleted?.Invoke();
        }
        
        private void SetupContainer()
        {
            if (provincesContainer == null)
            {
                provincesContainer = new GameObject("Provinces");
                provincesContainer.transform.parent = transform;
            }
        }
        
        private void CalculateMapDimensions()
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
            
            meshService.Initialize(mapWidth, mapHeight, provinceMap.width, provinceMap.height);
        }
        
        private ProvinceAnalysisService.AnalysisResult AnalyzeProvinces()
        {
            ProvinceAnalysisService.AnalysisResult result;
            
            if (useJobSystem)
            {
                // Use parallel job-based analysis
                result = analysisService.AnalyzeProvinceMapParallel(
                    provinceMap, 
                    politicalMap,
                    combineSmallProvinces ? minPixelsForProvince : 0,
                    maxProvincesToGenerate,
                    limitProvinceCount
                );
            }
            else
            {
                // Fallback to traditional single-threaded analysis
                result = AnalyzeProvincesTraditional();
            }
            
            // Register provinces with data service
            int provinceId = 1;
            foreach (var kvp in result.provinces)
            {
                var provinceData = new ProvinceDataService.ProvinceData
                {
                    id = provinceId++,
                    color = kvp.Key,
                    displayColor = DetermineDisplayColor(kvp.Key, kvp.Value.center),
                    name = $"Province_{provinceId}",
                    center = kvp.Value.center,
                    bounds = kvp.Value.bounds,
                    pixels = kvp.Value.pixels,
                    pixelSet = new HashSet<Vector2Int>(kvp.Value.pixels)
                };
                
                dataService.RegisterProvince(provinceData);
            }
            
            return result;
        }
        
        private ProvinceAnalysisService.AnalysisResult AnalyzeProvincesTraditional()
        {
            // This is a simplified version of the traditional analysis
            // You can implement the full traditional method if needed
            var result = new ProvinceAnalysisService.AnalysisResult
            {
                provinces = new Dictionary<Color, ProvinceAnalysisService.ProvinceInfo>()
            };
            
            Debug.LogWarning("Traditional analysis not fully implemented. Using Job System.");
            return analysisService.AnalyzeProvinceMapParallel(
                provinceMap, 
                politicalMap,
                combineSmallProvinces ? minPixelsForProvince : 0,
                maxProvincesToGenerate,
                limitProvinceCount
            );
        }
        
        private Color DetermineDisplayColor(Color provinceColor, Vector2 center)
        {
            if (politicalMap != null && 
                politicalMap.width == provinceMap.width && 
                politicalMap.height == provinceMap.height)
            {
                int x = Mathf.Clamp(Mathf.RoundToInt(center.x), 0, politicalMap.width - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(center.y), 0, politicalMap.height - 1);
                return politicalMap.GetPixel(x, y);
            }
            
            return useProvinceMapColors ? provinceColor : Color.white;
        }
        
        private void GenerateAllProvinceMeshes(ProvinceAnalysisService.AnalysisResult analysisResult)
        {
            int generatedCount = 0;
            foreach (var province in dataService.GetAllProvinces().Values)
            {
                GenerateSingleProvinceMesh(province);
                generatedCount++;
                
                if (showGenerationProgress && generatedCount % 100 == 0)
                {
                    Debug.Log($"Generated {generatedCount}/{analysisResult.provinces.Count} provinces...");
                }
            }
        }
        
        private IEnumerator GenerateProvinceMeshesAsync(ProvinceAnalysisService.AnalysisResult analysisResult)
        {
            int generatedCount = 0;
            int batchCount = 0;
            
            foreach (var province in dataService.GetAllProvinces().Values)
            {
                GenerateSingleProvinceMesh(province);
                generatedCount++;
                batchCount++;
                
                if (batchCount >= provincesPerFrame)
                {
                    if (showGenerationProgress)
                    {
                        float progress = (float)generatedCount / analysisResult.provinces.Count * 100f;
                        Debug.Log($"Generating provinces... {progress:F1}% ({generatedCount}/{analysisResult.provinces.Count})");
                    }
                    
                    batchCount = 0;
                    yield return null; // Give frame back
                }
            }
        }
        
        private void GenerateSingleProvinceMesh(ProvinceDataService.ProvinceData province)
        {
            // Create GameObject
            GameObject provinceObj = new GameObject($"Province_{province.id}");
            provinceObj.transform.parent = provincesContainer.transform;
            
            // Add components
            MeshFilter meshFilter = provinceObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = provinceObj.AddComponent<MeshRenderer>();
            
            // Generate mesh
            Mesh mesh = meshService.GenerateProvinceMeshOptimized(province, provinceHeight, meshMethod);
            meshFilter.mesh = mesh;
            
            // Setup material
            Material mat = provinceMaterial != null ? 
                new Material(provinceMaterial) : 
                new Material(Shader.Find("Standard"));
            mat.color = province.displayColor;
            meshRenderer.material = mat;
            
            // Add collider
            MeshCollider collider = provinceObj.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            
            // Add province component
            ProvinceComponent comp = provinceObj.AddComponent<ProvinceComponent>();
            comp.provinceId = province.id;
            comp.provinceName = province.name;
            comp.provinceColor = province.color;
            comp.displayColor = province.displayColor;
            comp.pixelCount = province.pixels.Count;
            
            // Register with data service
            dataService.RegisterProvinceGameObject(province.color, provinceObj);
        }
        
        private void GenerateBorders()
        {
            if (useJobSystem)
            {
                var borderPixels = analysisService.FindBorderPixelsParallel(provinceMap);
                borderService.GenerateBorderMeshes(borderPixels, provinceHeight, borderWidth, 
                    borderColor, borderMaterial, provincesContainer, mapWidth, mapHeight, 
                    provinceMap.width, provinceMap.height);
            }
            else
            {
                Debug.LogWarning("Border generation without Job System not implemented");
            }
        }
        
        [ContextMenu("Clear Provinces")]
        public void ClearProvinces()
        {
            if (provincesContainer != null)
            {
                DestroyImmediate(provincesContainer);
            }
            dataService?.Clear();
        }
        
        [ContextMenu("Log Province Statistics")]
        public void LogStatistics()
        {
            var stats = dataService.GetStatistics();
            Debug.Log($"Province Statistics:");
            Debug.Log($"- Total Provinces: {stats.totalProvinces}");
            Debug.Log($"- Total Pixels: {stats.totalPixels}");
            Debug.Log($"- Average Pixels per Province: {stats.averagePixels}");
            Debug.Log($"- Smallest Province: {stats.minPixels} pixels");
            Debug.Log($"- Largest Province: {stats.maxPixels} pixels");
        }
        
        // Public API
        public ProvinceDataService.ProvinceData GetProvinceByColor(Color color)
        {
            return dataService?.GetProvinceByColor(color);
        }
        
        public ProvinceDataService.ProvinceData GetProvinceById(int id)
        {
            return dataService?.GetProvinceById(id);
        }
        
        public GameObject GetProvinceGameObject(Color color)
        {
            return dataService?.GetProvinceGameObject(color);
        }
        
        public bool IsGenerating => isGenerating;
    }
    
    /// <summary>
    /// Service for generating border meshes
    /// </summary>
    public class ProvinceBorderService
    {
        public void GenerateBorderMeshes(List<Vector2Int> borderPixels, float height, float width,
            Color color, Material material, GameObject container, float mapWidth, float mapHeight,
            int textureWidth, int textureHeight)
        {
            GameObject bordersContainer = new GameObject("Borders");
            bordersContainer.transform.parent = container.transform;
            
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            
            float pixelToWorldX = mapWidth / textureWidth;
            float pixelToWorldZ = mapHeight / textureHeight;
            
            foreach (var pixel in borderPixels)
            {
                Vector3 worldPos = PixelToWorldPosition(pixel, pixelToWorldX, pixelToWorldZ, 
                    textureWidth, textureHeight, mapWidth, mapHeight);
                worldPos.y = height + 0.01f; // Slightly above provinces
                
                float halfWidth = width * 0.5f;
                int baseIndex = vertices.Count;
                
                // Add vertices for border quad
                vertices.Add(worldPos + new Vector3(-halfWidth, 0, -halfWidth));
                vertices.Add(worldPos + new Vector3(halfWidth, 0, -halfWidth));
                vertices.Add(worldPos + new Vector3(halfWidth, 0, halfWidth));
                vertices.Add(worldPos + new Vector3(-halfWidth, 0, halfWidth));
                
                // Add triangles
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
                
                // Create new mesh if getting too large
                if (vertices.Count > 60000)
                {
                    CreateBorderMeshObject(bordersContainer, vertices, triangles, 
                        bordersContainer.transform.childCount, material, color);
                    vertices.Clear();
                    triangles.Clear();
                }
            }
            
            // Create final mesh
            if (vertices.Count > 0)
            {
                CreateBorderMeshObject(bordersContainer, vertices, triangles, 
                    bordersContainer.transform.childCount, material, color);
            }
        }
        
        private void CreateBorderMeshObject(GameObject parent, List<Vector3> vertices, 
            List<int> triangles, int index, Material material, Color color)
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
            
            Material mat = material != null ? 
                new Material(material) : 
                new Material(Shader.Find("Unlit/Color"));
            mat.color = color;
            meshRenderer.material = mat;
            meshRenderer.receiveShadows = false;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        
        private Vector3 PixelToWorldPosition(Vector2Int pixel, float pixelToWorldX, float pixelToWorldZ,
            int textureWidth, int textureHeight, float mapWidth, float mapHeight)
        {
            float x = (pixel.x / (float)textureWidth - 0.5f) * mapWidth;
            float z = (pixel.y / (float)textureHeight - 0.5f) * mapHeight;
            return new Vector3(x, 0, z);
        }
    }
}