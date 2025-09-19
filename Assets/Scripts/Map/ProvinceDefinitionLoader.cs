using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using ProvinceSystem.Services;
using ProvinceSystem.Data;
using ParadoxDataLib.Core.Parsers.Csv.DataStructures;

namespace ProvinceSystem
{
    /// <summary>
    /// Loads and processes HOI4-style province definition files
    /// </summary>
    [System.Serializable]
    public class ProvinceDefinition
    {
        public int id;
        public Color32 color;
        public string name;
        public string category; // land, sea, lake
        
        public ProvinceDefinition(int id, byte r, byte g, byte b, string category = "land")
        {
            this.id = id;
            this.color = new Color32(r, g, b, 255);
            this.name = $"Province_{id}";
            this.category = category;
        }
    }
    
    public class ProvinceDefinitionLoader : MonoBehaviour
    {
        [Header("Definition File")]
        public TextAsset definitionFile;
        public string definitionFilePath = "Assets/Maps/definition.csv";
        
        [Header("Settings")]
        public bool loadOnStart = false;
        public bool skipFirstLine = true; // Skip header
        public char delimiter = ';';
        
        [Header("Province Filtering")]
        public bool filterSeaProvinces = true;
        public bool filterLakeProvinces = true;
        
        [Header("Debug")]
        public bool showDebugInfo = true;
        
        // Loaded data
        private Dictionary<int, ProvinceDefinition> definitionsById;
        private Dictionary<Color32, ProvinceDefinition> definitionsByColor;
        private Dictionary<Color32, int> colorToIdMap;
        
        // Statistics
        private int totalProvinces;
        private int landProvinces;
        private int seaProvinces;
        private int lakeProvinces;
        
        void Start()
        {
            if (loadOnStart)
            {
                LoadDefinitions();
            }
        }
        
        /// <summary>
        /// Load province definitions from CSV file
        /// </summary>
        [ContextMenu("Load Province Definitions")]
        public void LoadDefinitions()
        {
            definitionsById = new Dictionary<int, ProvinceDefinition>();
            definitionsByColor = new Dictionary<Color32, ProvinceDefinition>(new Color32Comparer());
            colorToIdMap = new Dictionary<Color32, int>(new Color32Comparer());
            
            string csvContent = null;
            
            // Try loading from TextAsset first
            if (definitionFile != null)
            {
                csvContent = definitionFile.text;
                Debug.Log($"Loading definitions from TextAsset: {definitionFile.name}");
            }
            // Try loading from file path
            else if (!string.IsNullOrEmpty(definitionFilePath) && File.Exists(definitionFilePath))
            {
                csvContent = File.ReadAllText(definitionFilePath);
                Debug.Log($"Loading definitions from file: {definitionFilePath}");
            }
            else
            {
                Debug.LogError("No definition file found! Please assign a TextAsset or provide a valid file path.");
                return;
            }
            
            ParseCSV(csvContent);
            
            if (showDebugInfo)
            {
                LogStatistics();
            }
        }
        
        private void ParseCSV(string csvContent)
        {
            string[] lines = csvContent.Split('\n', '\r')
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
            
            int startLine = skipFirstLine ? 1 : 0;
            totalProvinces = 0;
            landProvinces = 0;
            seaProvinces = 0;
            lakeProvinces = 0;
            
            for (int i = startLine; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                
                try
                {
                    ParseLine(line);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Failed to parse line {i + 1}: {line}\nError: {e.Message}");
                }
            }
            
            Debug.Log($"Successfully loaded {definitionsById.Count} province definitions");
        }
        
        private void ParseLine(string line)
        {
            // Remove comments (everything after #)
            int commentIndex = line.IndexOf('#');
            if (commentIndex >= 0)
            {
                line = line.Substring(0, commentIndex).Trim();
            }
            
            if (string.IsNullOrEmpty(line)) return;
            
            string[] parts = line.Split(delimiter);
            if (parts.Length < 4) return; // Need at least id, r, g, b
            
            // Parse ID
            if (!int.TryParse(parts[0].Trim(), out int id))
                return;
            
            // Parse RGB
            if (!byte.TryParse(parts[1].Trim(), out byte r))
                return;
            if (!byte.TryParse(parts[2].Trim(), out byte g))
                return;
            if (!byte.TryParse(parts[3].Trim(), out byte b))
                return;
            
            // Parse category (land/sea/lake) if present
            string category = "land"; // default
            if (parts.Length >= 5)
            {
                string categoryPart = parts[4].Trim().ToLower();
                if (categoryPart.Contains("sea"))
                    category = "sea";
                else if (categoryPart.Contains("lake"))
                    category = "lake";
            }
            
            // Track statistics
            totalProvinces++;
            switch (category)
            {
                case "sea":
                    seaProvinces++;
                    if (filterSeaProvinces) return;
                    break;
                case "lake":
                    lakeProvinces++;
                    if (filterLakeProvinces) return;
                    break;
                default:
                    landProvinces++;
                    break;
            }
            
            // Create and store definition
            var definition = new ProvinceDefinition(id, r, g, b, category);
            
            definitionsById[id] = definition;
            definitionsByColor[definition.color] = definition;
            colorToIdMap[definition.color] = id;
        }
        
        /// <summary>
        /// Enhance province data with definition information
        /// </summary>
        public void EnhanceProvinceData(ProvinceDataService dataService)
        {
            if (definitionsById == null || definitionsById.Count == 0)
            {
                Debug.LogWarning("No definitions loaded! Load definitions first.");
                return;
            }
            
            int matched = 0;
            int unmatched = 0;
            
            foreach (var province in dataService.GetAllProvinces().Values)
            {
                Color32 color32 = province.color;
                
                if (definitionsByColor.TryGetValue(color32, out ProvinceDefinition definition))
                {
                    // Update province with definition data
                    province.id = definition.id;
                    province.name = definition.name;
                    
                    // Re-register with correct ID
                    dataService.RegisterProvince(province);
                    matched++;
                }
                else
                {
                    unmatched++;
                    if (showDebugInfo)
                    {
                        Debug.LogWarning($"No definition found for province color: {color32} (R:{color32.r} G:{color32.g} B:{color32.b})");
                    }
                }
            }
            
            Debug.Log($"Enhanced province data: {matched} matched, {unmatched} unmatched");
        }
        
        /// <summary>
        /// Get province ID from color
        /// </summary>
        public int GetProvinceIdFromColor(Color32 color)
        {
            if (colorToIdMap.TryGetValue(color, out int id))
            {
                return id;
            }
            return -1;
        }
        
        /// <summary>
        /// Get province definition by ID
        /// </summary>
        public ProvinceDefinition GetDefinitionById(int id)
        {
            if (definitionsById.TryGetValue(id, out ProvinceDefinition definition))
            {
                return definition;
            }
            return null;
        }
        
        /// <summary>
        /// Get province definition by color
        /// </summary>
        public ProvinceDefinition GetDefinitionByColor(Color32 color)
        {
            if (definitionsByColor.TryGetValue(color, out ProvinceDefinition definition))
            {
                return definition;
            }
            return null;
        }
        
        /// <summary>
        /// Check if a province is sea
        /// </summary>
        public bool IsSeaProvince(int provinceId)
        {
            var definition = GetDefinitionById(provinceId);
            return definition != null && definition.category == "sea";
        }
        
        /// <summary>
        /// Check if a province is lake
        /// </summary>
        public bool IsLakeProvince(int provinceId)
        {
            var definition = GetDefinitionById(provinceId);
            return definition != null && definition.category == "lake";
        }

        /// <summary>
        /// Get all definitions by color for lookup
        /// </summary>
        public Dictionary<Color32, ProvinceDefinition> GetDefinitionsByColor()
        {
            return definitionsByColor;
        }
        
        /// <summary>
        /// Check if a province is land
        /// </summary>
        public bool IsLandProvince(int provinceId)
        {
            var definition = GetDefinitionById(provinceId);
            return definition != null && definition.category == "land";
        }
        
        /// <summary>
        /// Get all land province IDs
        /// </summary>
        public List<int> GetLandProvinceIds()
        {
            return definitionsById
                .Where(kvp => kvp.Value.category == "land")
                .Select(kvp => kvp.Key)
                .ToList();
        }
        
        /// <summary>
        /// Get all sea province IDs
        /// </summary>
        public List<int> GetSeaProvinceIds()
        {
            return definitionsById
                .Where(kvp => kvp.Value.category == "sea")
                .Select(kvp => kvp.Key)
                .ToList();
        }
        
        /// <summary>
        /// Export loaded definitions
        /// </summary>
        [ContextMenu("Export Definitions")]
        public void ExportDefinitions()
        {
            if (definitionsById == null || definitionsById.Count == 0)
            {
                Debug.LogWarning("No definitions to export!");
                return;
            }
            
            string export = "ID;R;G;B;Category;Name\n";
            
            foreach (var kvp in definitionsById.OrderBy(x => x.Key))
            {
                var def = kvp.Value;
                export += $"{def.id};{def.color.r};{def.color.g};{def.color.b};{def.category};{def.name}\n";
            }
            
            string path = "Assets/ExportedDefinitions.csv";
            File.WriteAllText(path, export);
            Debug.Log($"Exported definitions to: {path}");
            
            #if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
            #endif
        }
        
        /// <summary>
        /// Load definitions from ParadoxDataService
        /// </summary>
        [ContextMenu("Load from ParadoxDataService")]
        public void LoadFromParadoxDataService()
        {
            var paradoxDataService = FindObjectOfType<ParadoxDataService>();
            if (paradoxDataService == null)
            {
                Debug.LogError("ParadoxDataService not found! Please add it to the scene.");
                return;
            }

            if (!paradoxDataService.IsLoaded)
            {
                Debug.LogWarning("ParadoxDataService not loaded yet. Loading will start automatically.");
                StartCoroutine(WaitForParadoxDataAndLoad(paradoxDataService));
                return;
            }

            LoadFromParadoxData(paradoxDataService);
        }

        private System.Collections.IEnumerator WaitForParadoxDataAndLoad(ParadoxDataService paradoxDataService)
        {
            yield return new WaitUntil(() => paradoxDataService.IsLoaded);
            LoadFromParadoxData(paradoxDataService);
        }

        private void LoadFromParadoxData(ParadoxDataService paradoxDataService)
        {
            definitionsById = new Dictionary<int, ProvinceDefinition>();
            definitionsByColor = new Dictionary<Color32, ProvinceDefinition>(new Color32Comparer());
            colorToIdMap = new Dictionary<Color32, int>(new Color32Comparer());

            totalProvinces = 0;
            landProvinces = 0;
            seaProvinces = 0;
            lakeProvinces = 0;

            foreach (var paradoxDef in paradoxDataService.GetAllProvinceDefinitions())
            {
                // Convert ParadoxDataLib definition to our format
                var unityDef = new ProvinceDefinition(
                    paradoxDef.ProvinceId,
                    paradoxDef.Red,
                    paradoxDef.Green,
                    paradoxDef.Blue,
                    "land" // Default to land, will be updated from default.map
                );
                unityDef.name = paradoxDef.Name;

                // Determine category from default.map data if available
                var defaultMapData = paradoxDataService.GetDefaultMapData();
                if (defaultMapData != null)
                {
                    unityDef.category = DetermineProvinceCategory(paradoxDef.ProvinceId, defaultMapData);
                }

                // Filter provinces if needed
                if ((filterSeaProvinces && unityDef.category == "sea") ||
                    (filterLakeProvinces && unityDef.category == "lake"))
                {
                    continue;
                }

                // Add to dictionaries
                definitionsById[unityDef.id] = unityDef;
                definitionsByColor[unityDef.color] = unityDef;
                colorToIdMap[unityDef.color] = unityDef.id;

                // Update statistics
                totalProvinces++;
                switch (unityDef.category)
                {
                    case "land": landProvinces++; break;
                    case "sea": seaProvinces++; break;
                    case "lake": lakeProvinces++; break;
                }
            }

            Debug.Log($"Loaded {totalProvinces} provinces from ParadoxDataService");

            if (showDebugInfo)
            {
                LogStatistics();
            }
        }

        private string DetermineProvinceCategory(int provinceId, ParadoxDataLib.Core.Common.ParadoxNode defaultMapData)
        {
            // Check if province is in sea_starts list
            var seaStarts = defaultMapData.GetChild("sea_starts");
            if (seaStarts != null)
            {
                var seaProvinces = defaultMapData.GetValues<string>("sea_starts");
                foreach (var seaProvince in seaProvinces)
                {
                    if (int.TryParse(seaProvince, out int seaId) && seaId == provinceId)
                    {
                        return "sea";
                    }
                }
            }

            // Check if province is in lakes list
            var lakes = defaultMapData.GetChild("lakes");
            if (lakes != null)
            {
                var lakeProvinces = defaultMapData.GetValues<string>("lakes");
                foreach (var lakeProvince in lakeProvinces)
                {
                    if (int.TryParse(lakeProvince, out int lakeId) && lakeId == provinceId)
                    {
                        return "lake";
                    }
                }
            }

            return "land"; // Default to land
        }

        [ContextMenu("Log Statistics")]
        private void LogStatistics()
        {
            Debug.Log($"Province Definition Statistics:\n" +
                     $"- Total Provinces: {totalProvinces}\n" +
                     $"- Land Provinces: {landProvinces}\n" +
                     $"- Sea Provinces: {seaProvinces}\n" +
                     $"- Lake Provinces: {lakeProvinces}\n" +
                     $"- Loaded Definitions: {definitionsById?.Count ?? 0}");
        }
        
        public int TotalProvinces => totalProvinces;
        public int LandProvinces => landProvinces;
        public int SeaProvinces => seaProvinces;
        public int LakeProvinces => lakeProvinces;
        public Dictionary<int, ProvinceDefinition> DefinitionsById => definitionsById;
    }
    
    /// <summary>
    /// Custom comparer for Color32 to use as dictionary key
    /// </summary>
    public class Color32Comparer : IEqualityComparer<Color32>
    {
        public bool Equals(Color32 x, Color32 y)
        {
            return x.r == y.r && x.g == y.g && x.b == y.b;
        }
        
        public int GetHashCode(Color32 color)
        {
            return (color.r << 16) | (color.g << 8) | color.b;
        }
    }
}