using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ProvinceSystem.Services;

namespace ProvinceSystem
{
    /// <summary>
    /// Simplified ProvinceManager that uses pre-calculated adjacency data from FastAdjacencyScanner
    /// </summary>
    public class ProvinceManager : MonoBehaviour
    {
        [Header("References")]
        public OptimizedProvinceMeshGenerator meshGenerator;
        public FastAdjacencyScanner adjacencyScanner;
        public Camera mainCamera;
        
        [Header("Selection Settings")]
        public Material selectedMaterial;
        public Material neighborMaterial;
        public Color selectedColor = new Color(1f, 0.8f, 0f, 1f);
        public Color neighborColor = new Color(0.5f, 0.8f, 1f, 1f);
        
        [Header("Debug")]
        public bool showDebugInfo = false;
        public bool visualizeNeighbors = true;
        
        // Province data
        private ProvinceDataService dataService;
        private Dictionary<int, HashSet<int>> provinceNeighbors;
        
        // Selection state
        private int currentSelectedProvinceId = -1;
        private HashSet<int> currentHighlightedNeighbors = new HashSet<int>();
        private Dictionary<int, Material> originalMaterials = new Dictionary<int, Material>();
        
        void Start()
        {
            Initialize();
        }
        
        void Update()
        {
            HandleProvinceSelection();
        }
        
        private void Initialize()
        {
            // Get references
            if (meshGenerator == null)
                meshGenerator = GetComponent<OptimizedProvinceMeshGenerator>();
            
            if (adjacencyScanner == null)
                adjacencyScanner = GetComponent<FastAdjacencyScanner>();
            
            if (mainCamera == null)
                mainCamera = Camera.main;
            
            if (meshGenerator == null)
            {
                Debug.LogError("ProvinceManager requires OptimizedProvinceMeshGenerator!");
                enabled = false;
                return;
            }
            
            provinceNeighbors = new Dictionary<int, HashSet<int>>();
        }
        
        /// <summary>
        /// Use pre-calculated adjacency data from FastAdjacencyScanner
        /// </summary>
        public void BuildNeighborMap()
        {
            if (adjacencyScanner == null || adjacencyScanner.IdAdjacencies == null)
            {
                Debug.LogError("No adjacency data available! Run FastAdjacencyScanner first.");
                return;
            }
            
            // Simply copy the pre-calculated adjacencies
            provinceNeighbors = new Dictionary<int, HashSet<int>>();
            foreach (var kvp in adjacencyScanner.IdAdjacencies)
            {
                provinceNeighbors[kvp.Key] = new HashSet<int>(kvp.Value);
            }
            
            // Get data service reference
            dataService = GetDataService();
            
            Debug.Log($"Loaded {provinceNeighbors.Count} province adjacencies from FastAdjacencyScanner");
            
            if (showDebugInfo)
            {
                int totalConnections = provinceNeighbors.Sum(kvp => kvp.Value.Count) / 2;
                Debug.Log($"Total neighbor connections: {totalConnections}");
            }
        }
        
        private ProvinceDataService GetDataService()
        {
            if (dataService == null && meshGenerator != null)
            {
                var field = meshGenerator.GetType()
                    .GetField("dataService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                dataService = field?.GetValue(meshGenerator) as ProvinceDataService;
            }
            return dataService;
        }
        
        private void HandleProvinceSelection()
        {
            if (!Input.GetMouseButtonDown(0))
                return;
            
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                ProvinceComponent provinceComp = hit.collider.GetComponent<ProvinceComponent>();
                if (provinceComp != null)
                {
                    SelectProvince(provinceComp.provinceId);
                }
            }
            else
            {
                DeselectCurrentProvince();
            }
        }
        
        public void SelectProvince(int provinceId)
        {
            // Deselect current
            DeselectCurrentProvince();
            
            // Select new province
            currentSelectedProvinceId = provinceId;
            
            var dataService = GetDataService();
            if (dataService == null)
            {
                Debug.LogError("ProvinceDataService not available!");
                return;
            }
            
            var province = dataService.GetProvinceById(provinceId);
            if (province == null)
            {
                Debug.LogWarning($"Province {provinceId} not found!");
                return;
            }
            
            // Highlight selected province
            var provinceObj = dataService.GetProvinceGameObject(province.color);
            if (provinceObj != null)
            {
                var renderer = provinceObj.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    // Store original material
                    if (!originalMaterials.ContainsKey(provinceId))
                        originalMaterials[provinceId] = renderer.sharedMaterial;
                    
                    // Apply selection material
                    if (selectedMaterial != null)
                    {
                        renderer.material = selectedMaterial;
                    }
                    else
                    {
                        Material mat = new Material(renderer.sharedMaterial);
                        mat.color = selectedColor;
                        renderer.material = mat;
                    }
                }
            }
            
            // Highlight neighbors
            if (visualizeNeighbors)
            {
                HighlightNeighbors(provinceId);
            }
            
            if (showDebugInfo)
            {
                LogProvinceInfo(provinceId);
            }
        }
        
        private void HighlightNeighbors(int provinceId)
        {
            if (!provinceNeighbors.ContainsKey(provinceId))
            {
                Debug.LogWarning($"No neighbor data for province {provinceId}");
                return;
            }
            
            var dataService = GetDataService();
            if (dataService == null) return;
            
            currentHighlightedNeighbors.Clear();
            
            foreach (int neighborId in provinceNeighbors[provinceId])
            {
                var neighborProvince = dataService.GetProvinceById(neighborId);
                if (neighborProvince == null) continue;
                
                var neighborObj = dataService.GetProvinceGameObject(neighborProvince.color);
                if (neighborObj != null)
                {
                    var renderer = neighborObj.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        currentHighlightedNeighbors.Add(neighborId);
                        
                        // Store original material
                        if (!originalMaterials.ContainsKey(neighborId))
                            originalMaterials[neighborId] = renderer.sharedMaterial;
                        
                        // Apply neighbor material
                        if (neighborMaterial != null)
                        {
                            renderer.material = neighborMaterial;
                        }
                        else
                        {
                            Material mat = new Material(renderer.sharedMaterial);
                            mat.color = neighborColor;
                            renderer.material = mat;
                        }
                    }
                }
            }
        }
        
        private void DeselectCurrentProvince()
        {
            if (currentSelectedProvinceId < 0) return;
            
            var dataService = GetDataService();
            if (dataService == null) return;
            
            // Restore selected province material
            RestoreMaterial(currentSelectedProvinceId);
            
            // Restore neighbor materials
            foreach (int neighborId in currentHighlightedNeighbors)
            {
                RestoreMaterial(neighborId);
            }
            
            currentSelectedProvinceId = -1;
            currentHighlightedNeighbors.Clear();
        }
        
        private void RestoreMaterial(int provinceId)
        {
            if (!originalMaterials.ContainsKey(provinceId)) return;
            
            var dataService = GetDataService();
            if (dataService == null) return;
            
            var province = dataService.GetProvinceById(provinceId);
            if (province == null) return;
            
            var obj = dataService.GetProvinceGameObject(province.color);
            if (obj != null)
            {
                var renderer = obj.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.material = originalMaterials[provinceId];
                }
            }
        }
        
        private void LogProvinceInfo(int provinceId)
        {
            var dataService = GetDataService();
            if (dataService == null) return;
            
            var province = dataService.GetProvinceById(provinceId);
            if (province == null) return;
            
            string neighborList = "None";
            if (provinceNeighbors.ContainsKey(provinceId) && provinceNeighbors[provinceId].Count > 0)
            {
                neighborList = string.Join(", ", provinceNeighbors[provinceId].Select(id => $"Province_{id}"));
            }
            
            Debug.Log($"Selected Province: {province.name}\n" +
                     $"ID: {provinceId}\n" +
                     $"Color: RGB({province.color.r*255:F0},{province.color.g*255:F0},{province.color.b*255:F0})\n" +
                     $"Pixels: {province.pixels.Count}\n" +
                     $"Neighbors ({(provinceNeighbors.ContainsKey(provinceId) ? provinceNeighbors[provinceId].Count : 0)}): {neighborList}");
        }
        
        // Public API
        
        public HashSet<int> GetNeighbors(int provinceId)
        {
            if (provinceNeighbors.ContainsKey(provinceId))
                return new HashSet<int>(provinceNeighbors[provinceId]);
            return new HashSet<int>();
        }
        
        public bool AreNeighbors(int provinceId1, int provinceId2)
        {
            return provinceNeighbors.ContainsKey(provinceId1) && 
                   provinceNeighbors[provinceId1].Contains(provinceId2);
        }
        
        public HashSet<int> GetProvincesWithinDistance(int startProvinceId, int maxDistance)
        {
            var result = new HashSet<int>();
            var currentLayer = new HashSet<int> { startProvinceId };
            var visited = new HashSet<int> { startProvinceId };
            
            for (int i = 0; i < maxDistance; i++)
            {
                var nextLayer = new HashSet<int>();
                
                foreach (int provinceId in currentLayer)
                {
                    if (!provinceNeighbors.ContainsKey(provinceId)) continue;
                    
                    foreach (int neighborId in provinceNeighbors[provinceId])
                    {
                        if (!visited.Contains(neighborId))
                        {
                            visited.Add(neighborId);
                            nextLayer.Add(neighborId);
                            result.Add(neighborId);
                        }
                    }
                }
                
                currentLayer = nextLayer;
                if (currentLayer.Count == 0) break;
            }
            
            return result;
        }
        
        public List<int> FindPath(int startProvinceId, int endProvinceId)
        {
            if (startProvinceId == endProvinceId)
                return new List<int> { startProvinceId };
            
            var queue = new Queue<int>();
            var visited = new HashSet<int>();
            var parent = new Dictionary<int, int>();
            
            queue.Enqueue(startProvinceId);
            visited.Add(startProvinceId);
            
            bool found = false;
            
            while (queue.Count > 0 && !found)
            {
                int current = queue.Dequeue();
                
                if (!provinceNeighbors.ContainsKey(current)) continue;
                
                foreach (int neighbor in provinceNeighbors[current])
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        parent[neighbor] = current;
                        queue.Enqueue(neighbor);
                        
                        if (neighbor == endProvinceId)
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }
            
            if (!found)
                return null;
            
            // Reconstruct path
            var path = new List<int>();
            int node = endProvinceId;
            
            while (node != startProvinceId)
            {
                path.Add(node);
                node = parent[node];
            }
            
            path.Add(startProvinceId);
            path.Reverse();
            
            return path;
        }
        
        public int CurrentSelectedProvinceId => currentSelectedProvinceId;
        public Dictionary<int, HashSet<int>> ProvinceNeighbors => provinceNeighbors;
    }
}