using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using ParadoxDataLib.Core.Parsers;
using ParadoxDataLib.Core.Common;
using ParadoxDataLib.Core.Parsers.Bitmap;
using ParadoxProvinceDef = ParadoxDataLib.Core.Parsers.Csv.DataStructures.ProvinceDefinition;
using ProvinceSystem.Services;

namespace ProvinceSystem.Data
{
    /// <summary>
    /// Central service for managing Paradox game data integration with Unity
    /// Provides async loading, caching, and Unity-friendly interfaces
    /// </summary>
    public class ParadoxDataService : MonoBehaviour
    {
        [Header("Data Paths")]
        [SerializeField] private string dataRootPath = "Data";
        [SerializeField] private string definitionPath = "map/definition.csv";
        [SerializeField] private string provincesMapPath = "map/provinces.bmp";
        [SerializeField] private string defaultMapPath = "map/default.map";
        [SerializeField] private string provinceHistoryPath = "history/provinces";
        [SerializeField] private string countryHistoryPath = "history/countries";

        [Header("Loading Settings")]
        [SerializeField] private bool loadOnStart = false;
        [SerializeField] private bool enableCaching = true;
        [SerializeField] private bool showProgressUI = true;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogging = true;

        // Events
        public event System.Action<float> OnLoadProgress;
        public event System.Action<string> OnLoadStageChanged;
        public event System.Action OnLoadComplete;
        public event System.Action<string> OnLoadError;

        // Cached data
        private Dictionary<int, ParadoxProvinceDef> _provinceDefinitions;
        private Dictionary<Color32, int> _colorToProvinceId;
        private ParadoxNode _defaultMapData;
        private BmpReader _provincesMapReader;
        private Dictionary<int, ParadoxNode> _provinceHistoryData;

        // Parsers
        private GenericParadoxParser _paradoxParser;

        // State
        private bool _isLoaded = false;
        private bool _isLoading = false;

        public bool IsLoaded => _isLoaded;
        public bool IsLoading => _isLoading;

        private void Awake()
        {
            InitializeParsers();
        }

        private void Start()
        {
            if (loadOnStart)
            {
                StartCoroutine(LoadAllDataCoroutine());
            }
        }

        private void InitializeParsers()
        {
            // Initialize FileLogger for logging to Assets/Logs
            if (ProvinceSystem.Utils.FileLogger.Instance != null)
            {
                ProvinceSystem.Utils.DominionLogger.LogSection("ParadoxDataService Initialization");
            }

            _paradoxParser = new GenericParadoxParser();

            Log("ParadoxDataService initialized");
        }

        /// <summary>
        /// Load all Paradox data asynchronously
        /// </summary>
        public async Task<bool> LoadAllDataAsync()
        {
            if (_isLoading || _isLoaded)
            {
                LogWarning("Data loading already in progress or completed");
                return _isLoaded;
            }

            _isLoading = true;

            try
            {
                OnLoadStageChanged?.Invoke("Loading province definitions...");
                OnLoadProgress?.Invoke(0.1f);

                if (!await LoadProvinceDefinitionsAsync())
                {
                    OnLoadError?.Invoke("Failed to load province definitions");
                    return false;
                }

                OnLoadStageChanged?.Invoke("Loading default map data...");
                OnLoadProgress?.Invoke(0.3f);

                if (!await LoadDefaultMapAsync())
                {
                    OnLoadError?.Invoke("Failed to load default map");
                    return false;
                }

                OnLoadStageChanged?.Invoke("Loading provinces bitmap...");
                OnLoadProgress?.Invoke(0.5f);

                if (!await LoadProvincesBitmapAsync())
                {
                    OnLoadError?.Invoke("Failed to load provinces bitmap");
                    return false;
                }

                OnLoadStageChanged?.Invoke("Loading province histories...");
                OnLoadProgress?.Invoke(0.7f);

                await LoadProvinceHistoriesAsync(); // Optional - don't fail if missing

                OnLoadStageChanged?.Invoke("Finalizing...");
                OnLoadProgress?.Invoke(1.0f);

                _isLoaded = true;
                OnLoadComplete?.Invoke();

                Log($"Successfully loaded {_provinceDefinitions?.Count ?? 0} province definitions");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to load Paradox data: {ex.Message}");
                OnLoadError?.Invoke(ex.Message);
                return false;
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// Unity Coroutine wrapper for async loading
        /// </summary>
        public IEnumerator LoadAllDataCoroutine()
        {
            var task = LoadAllDataAsync();
            yield return new WaitUntil(() => task.IsCompleted);
        }

        private async Task<bool> LoadProvinceDefinitionsAsync()
        {
            var fullPath = Path.Combine(Application.dataPath, dataRootPath, definitionPath);

            if (!File.Exists(fullPath))
            {
                LogError($"Definition file not found: {fullPath}");
                return false;
            }

            try
            {
                var csvContent = await File.ReadAllTextAsync(fullPath);
                var definitions = ParseProvinceDefinitions(csvContent);

                _provinceDefinitions = new Dictionary<int, ParadoxProvinceDef>();
                _colorToProvinceId = new Dictionary<Color32, int>();

                foreach (var def in definitions)
                {
                    if (def.IsValid)
                    {
                        _provinceDefinitions[def.ProvinceId] = def;
                        var color = new Color32(def.Red, def.Green, def.Blue, 255);
                        _colorToProvinceId[color] = def.ProvinceId;
                    }
                }

                Log($"Loaded {_provinceDefinitions.Count} province definitions");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to parse province definitions: {ex.Message}");
                return false;
            }
        }

        private List<ParadoxProvinceDef> ParseProvinceDefinitions(string csvContent)
        {
            var definitions = new List<ParadoxProvinceDef>();
            var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Skip header line
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var parts = line.Split(';');
                if (parts.Length >= 5)
                {
                    if (int.TryParse(parts[0], out int id) &&
                        byte.TryParse(parts[1], out byte r) &&
                        byte.TryParse(parts[2], out byte g) &&
                        byte.TryParse(parts[3], out byte b))
                    {
                        var name = parts[4].Trim('"');
                        var unused = parts.Length > 5 ? parts[5] : "x";

                        definitions.Add(new ParadoxProvinceDef(id, r, g, b, name, unused));
                    }
                }
            }

            return definitions;
        }

        private async Task<bool> LoadDefaultMapAsync()
        {
            var fullPath = Path.Combine(Application.dataPath, dataRootPath, defaultMapPath);

            if (!File.Exists(fullPath))
            {
                LogWarning($"Default map file not found: {fullPath}");
                return false;
            }

            try
            {
                var content = await File.ReadAllTextAsync(fullPath);
                _defaultMapData = _paradoxParser.Parse(content);

                Log("Loaded default map data");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to parse default map: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> LoadProvincesBitmapAsync()
        {
            var fullPath = Path.Combine(Application.dataPath, dataRootPath, provincesMapPath);

            if (!File.Exists(fullPath))
            {
                LogError($"Provinces bitmap not found: {fullPath}");
                return false;
            }

            try
            {
                // For now, we'll just verify the file exists
                // Full bitmap loading can be implemented later
                var fileInfo = new FileInfo(fullPath);
                Log($"Found provinces bitmap: {fileInfo.Length} bytes");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to load provinces bitmap: {ex.Message}");
                return false;
            }
        }

        private async Task LoadProvinceHistoriesAsync()
        {
            var fullPath = Path.Combine(Application.dataPath, dataRootPath, provinceHistoryPath);

            if (!Directory.Exists(fullPath))
            {
                LogWarning($"Province history directory not found: {fullPath}");
                return;
            }

            _provinceHistoryData = new Dictionary<int, ParadoxNode>();

            try
            {
                var files = Directory.GetFiles(fullPath, "*.txt");
                Log($"Found {files.Length} province history files");

                // Load a few sample files for testing
                int loadedCount = 0;
                foreach (var file in files)
                {
                    if (loadedCount >= 10) break; // Limit for initial testing

                    try
                    {
                        var content = await File.ReadAllTextAsync(file);
                        var data = _paradoxParser.Parse(content);

                        // Extract province ID from filename
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        if (ExtractProvinceIdFromFilename(fileName, out int provinceId))
                        {
                            _provinceHistoryData[provinceId] = data;
                            loadedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogWarning($"Failed to parse {file}: {ex.Message}");
                    }
                }

                Log($"Loaded {loadedCount} province history files");
            }
            catch (Exception ex)
            {
                LogError($"Failed to load province histories: {ex.Message}");
            }
        }

        private bool ExtractProvinceIdFromFilename(string filename, out int provinceId)
        {
            provinceId = 0;

            // Try to extract ID from filename like "1-Uppland" or "100 - Friesland"
            var parts = filename.Split(new char[] { '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && int.TryParse(parts[0], out provinceId))
            {
                return true;
            }

            return false;
        }

        // Public API methods
        public ParadoxProvinceDef? GetProvinceDefinition(int provinceId)
        {
            if (_provinceDefinitions != null && _provinceDefinitions.TryGetValue(provinceId, out var definition))
            {
                return definition;
            }
            return null;
        }

        public int? GetProvinceIdFromColor(Color32 color)
        {
            if (_colorToProvinceId != null && _colorToProvinceId.TryGetValue(color, out var id))
            {
                return id;
            }
            return null;
        }

        public ParadoxNode GetProvinceHistory(int provinceId)
        {
            if (_provinceHistoryData != null && _provinceHistoryData.TryGetValue(provinceId, out var data))
            {
                return data;
            }
            return null;
        }

        public ParadoxNode GetDefaultMapData()
        {
            return _defaultMapData;
        }

        public IEnumerable<ParadoxProvinceDef> GetAllProvinceDefinitions()
        {
            if (_provinceDefinitions == null)
                return new ParadoxProvinceDef[0];

            return _provinceDefinitions.Values;
        }

        // Utility methods
        private void Log(string message)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[ParadoxDataService] {message}");
            }
        }

        private void LogWarning(string message)
        {
            if (enableDebugLogging)
            {
                Debug.LogWarning($"[ParadoxDataService] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[ParadoxDataService] {message}");
        }

        private void OnDestroy()
        {
            _provincesMapReader?.Dispose();
        }
    }
}