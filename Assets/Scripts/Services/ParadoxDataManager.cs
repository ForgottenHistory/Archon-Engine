using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using ParadoxDataLib.Core.Parsers;
using ParadoxDataLib.Core.Common;
using ParadoxDataLib.Core.Parsers.Bitmap;
using ParadoxDataLib.Core.Parsers.Csv.DataStructures;
using ProvinceSystem.Utils;

namespace ProvinceSystem.Services
{
    /// <summary>
    /// Central manager for all ParadoxDataLib operations in Unity
    /// Replaces and enhances the existing ParadoxDataService with comprehensive data management
    /// </summary>
    public class ParadoxDataManager : MonoBehaviour
    {
        [Header("Data Paths")]
        [SerializeField] private string dataRootPath = "Data";
        [SerializeField] private string definitionPath = "map/definition.csv";
        [SerializeField] private string provincesMapPath = "map/provinces.bmp";
        [SerializeField] private string defaultMapPath = "map/default.map";
        [SerializeField] private string provinceHistoryPath = "history/provinces";
        [SerializeField] private string countryHistoryPath = "history/countries";
        [SerializeField] private string localizationPath = "localisation";

        [Header("Loading Settings")]
        [SerializeField] private bool loadOnStart = false;
        [SerializeField] private bool enableCaching = true;
        [SerializeField] private bool enableProgressUI = true;
        [SerializeField] private int maxConcurrentOperations = 3;

        [Header("Performance Settings")]
        [SerializeField] private int cacheExpirationMinutes = 30;
        [SerializeField] private long maxMemoryCacheSizeMB = 256;
        [SerializeField] private bool enableDiskCache = true;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private bool enablePerformanceLogging = true;

        // Singleton instance
        private static ParadoxDataManager _instance;
        public static ParadoxDataManager Instance
        {
            get
            {
                if (_instance == null)
                    _instance = FindObjectOfType<ParadoxDataManager>();
                return _instance;
            }
        }

        // Core Events
        public event System.Action<float> OnLoadProgress;
        public event System.Action<string> OnLoadStageChanged;
        public event System.Action OnLoadComplete;
        public event System.Action<ParadoxDataException> OnLoadError;
        public event System.Action OnCacheCleared;

        // Data Events
        public event System.Action<int> OnProvinceDefinitionsLoaded;
        public event System.Action<int> OnHistoryFilesLoaded;
        public event System.Action<string> OnMapDataLoaded;

        // Core Data Storage
        private ParadoxDataCache _cache;
        private Dictionary<int, ProvinceDefinition> _provinceDefinitions;
        private Dictionary<Color32, int> _colorToProvinceId;
        private ParadoxNode _defaultMapData;
        private BmpReader _provincesMapReader;
        private Dictionary<int, ParadoxNode> _provinceHistoryData;
        private Dictionary<string, ParadoxNode> _countryData;
        private Dictionary<string, Dictionary<string, string>> _localizationData;

        // Parsers
        private GenericParadoxParser _paradoxParser;
        private BmpReader _bmpReader;

        // State Management
        private bool _isInitialized = false;
        private bool _isLoading = false;
        private bool _isLoaded = false;
        private LoadingState _currentState = LoadingState.Uninitialized;
        private float _currentProgress = 0f;
        private System.Threading.CancellationTokenSource _loadingCancellation;

        // Performance Tracking
        private Dictionary<string, TimeSpan> _operationTimes;
        private long _memoryUsageBytes;

        public bool IsInitialized => _isInitialized;
        public bool IsLoading => _isLoading;
        public bool IsLoaded => _isLoaded;
        public LoadingState CurrentState => _currentState;
        public float LoadingProgress => _currentProgress;
        public long MemoryUsageBytes => _memoryUsageBytes;

        #region Unity Lifecycle

        private void Awake()
        {
            // Singleton pattern
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
                InitializeManager();
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
                return;
            }
        }

        private void Start()
        {
            if (loadOnStart && !_isLoaded)
            {
                StartCoroutine(LoadAllDataCoroutine());
            }
        }

        private void OnDestroy()
        {
            CleanupResources();
        }

        #endregion

        #region Initialization

        private void InitializeManager()
        {
            try
            {
                // Initialize logging
                if (FileLogger.Instance != null)
                {
                    DominionLogger.LogSection("ParadoxDataManager Initialization");
                }

                // Initialize collections
                _cache = new ParadoxDataCache(maxMemoryCacheSizeMB, cacheExpirationMinutes);
                _provinceDefinitions = new Dictionary<int, ProvinceDefinition>();
                _colorToProvinceId = new Dictionary<Color32, int>();
                _provinceHistoryData = new Dictionary<int, ParadoxNode>();
                _countryData = new Dictionary<string, ParadoxNode>();
                _localizationData = new Dictionary<string, Dictionary<string, string>>();
                _operationTimes = new Dictionary<string, TimeSpan>();

                // Initialize parsers
                _paradoxParser = new GenericParadoxParser();
                _bmpReader = new BmpReader();

                // Setup cache events
                if (enableCaching)
                {
                    _cache.OnCacheHit += OnCacheHit;
                    _cache.OnCacheMiss += OnCacheMiss;
                    _cache.OnCacheEviction += OnCacheEviction;
                }

                _currentState = LoadingState.Initialized;
                _isInitialized = true;

                Log("ParadoxDataManager initialized successfully");
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialize ParadoxDataManager: {ex.Message}");
                OnLoadError?.Invoke(new ParadoxDataException("Initialization failed", ex));
            }
        }

        #endregion

        #region Public API - Loading

        /// <summary>
        /// Load all Paradox data asynchronously with comprehensive progress tracking
        /// </summary>
        public async Task<bool> LoadAllDataAsync()
        {
            if (_isLoading)
            {
                LogWarning("Data loading already in progress");
                return false;
            }

            if (_isLoaded)
            {
                LogWarning("Data already loaded");
                return true;
            }

            return await ExecuteWithErrorHandling(async () =>
            {
                _isLoading = true;
                _currentState = LoadingState.Loading;
                _loadingCancellation = new System.Threading.CancellationTokenSource();

                var startTime = DateTime.Now;
                Log("Starting comprehensive data loading...");

                try
                {
                    // Phase 1: Load province definitions (20%)
                    await LoadPhase("Province Definitions", 0f, 0.2f, LoadProvinceDefinitionsAsync);

                    // Phase 2: Load default map data (15%)
                    await LoadPhase("Default Map Data", 0.2f, 0.35f, LoadDefaultMapAsync);

                    // Phase 3: Load provinces bitmap (25%)
                    await LoadPhase("Provinces Bitmap", 0.35f, 0.6f, LoadProvincesBitmapAsync);

                    // Phase 4: Load province histories (20%)
                    await LoadPhase("Province Histories", 0.6f, 0.8f, LoadProvinceHistoriesAsync);

                    // Phase 5: Load country data (10%)
                    await LoadPhase("Country Data", 0.8f, 0.9f, LoadCountryDataAsync);

                    // Phase 6: Finalization (10%)
                    await LoadPhase("Finalization", 0.9f, 1.0f, FinalizeLoadingAsync);

                    var loadTime = DateTime.Now - startTime;
                    _operationTimes["FullLoad"] = loadTime;

                    _currentState = LoadingState.Loaded;
                    _isLoaded = true;
                    _currentProgress = 1.0f;

                    OnLoadComplete?.Invoke();
                    Log($"Successfully loaded all data in {loadTime.TotalSeconds:F2} seconds");

                    return true;
                }
                catch (OperationCanceledException)
                {
                    Log("Data loading was cancelled");
                    return false;
                }
                catch (Exception ex)
                {
                    var paradoxEx = new ParadoxDataException($"Data loading failed: {ex.Message}", ex);
                    LogError(paradoxEx.Message);
                    OnLoadError?.Invoke(paradoxEx);
                    return false;
                }
                finally
                {
                    _isLoading = false;
                    _loadingCancellation?.Dispose();
                    _loadingCancellation = null;
                }
            });
        }

        /// <summary>
        /// Unity Coroutine wrapper for async loading
        /// </summary>
        public IEnumerator LoadAllDataCoroutine()
        {
            var task = LoadAllDataAsync();
            yield return new WaitUntil(() => task.IsCompleted);
        }

        /// <summary>
        /// Cancel current loading operation
        /// </summary>
        public void CancelLoading()
        {
            if (_isLoading && _loadingCancellation != null)
            {
                _loadingCancellation.Cancel();
                Log("Loading cancellation requested");
            }
        }

        #endregion

        #region Public API - Data Access

        /// <summary>
        /// Get province definition by ID with caching
        /// </summary>
        public ProvinceDefinition? GetProvinceDefinition(int provinceId)
        {
            if (_provinceDefinitions != null && _provinceDefinitions.TryGetValue(provinceId, out var definition))
            {
                return definition;
            }
            return null;
        }

        /// <summary>
        /// Get province ID from color with caching
        /// </summary>
        public int? GetProvinceIdFromColor(Color32 color)
        {
            if (_colorToProvinceId != null && _colorToProvinceId.TryGetValue(color, out var id))
            {
                return id;
            }
            return null;
        }

        /// <summary>
        /// Get province history data
        /// </summary>
        public ParadoxNode GetProvinceHistory(int provinceId)
        {
            if (_provinceHistoryData != null && _provinceHistoryData.TryGetValue(provinceId, out var data))
            {
                return data;
            }
            return null;
        }

        /// <summary>
        /// Get default map data
        /// </summary>
        public ParadoxNode GetDefaultMapData()
        {
            return _defaultMapData;
        }

        /// <summary>
        /// Get all province definitions
        /// </summary>
        public IEnumerable<ProvinceDefinition> GetAllProvinceDefinitions()
        {
            return _provinceDefinitions?.Values ?? Enumerable.Empty<ProvinceDefinition>();
        }

        /// <summary>
        /// Get country data by tag
        /// </summary>
        public ParadoxNode GetCountryData(string countryTag)
        {
            if (_countryData != null && _countryData.TryGetValue(countryTag, out var data))
            {
                return data;
            }
            return null;
        }

        /// <summary>
        /// Get localized string
        /// </summary>
        public string GetLocalizedString(string key, string language = "english")
        {
            if (_localizationData != null &&
                _localizationData.TryGetValue(language, out var langData) &&
                langData.TryGetValue(key, out var value))
            {
                return value;
            }
            return key; // Return key if not found
        }

        #endregion

        #region Private Loading Methods

        private async Task LoadPhase(string phaseName, float startProgress, float endProgress, Func<Task<bool>> loadMethod)
        {
            OnLoadStageChanged?.Invoke(phaseName);
            _currentProgress = startProgress;
            OnLoadProgress?.Invoke(_currentProgress);

            var startTime = DateTime.Now;
            bool success = await loadMethod();
            var duration = DateTime.Now - startTime;

            _operationTimes[phaseName] = duration;

            if (!success)
            {
                throw new ParadoxDataException($"Failed to load {phaseName}");
            }

            _currentProgress = endProgress;
            OnLoadProgress?.Invoke(_currentProgress);

            if (enablePerformanceLogging)
            {
                Log($"{phaseName} loaded in {duration.TotalMilliseconds:F0}ms");
            }
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

                _provinceDefinitions.Clear();
                _colorToProvinceId.Clear();

                foreach (var def in definitions)
                {
                    if (def.IsValid)
                    {
                        _provinceDefinitions[def.ProvinceId] = def;
                        var color = new Color32(def.Red, def.Green, def.Blue, 255);
                        _colorToProvinceId[color] = def.ProvinceId;
                    }
                }

                OnProvinceDefinitionsLoaded?.Invoke(_provinceDefinitions.Count);
                Log($"Loaded {_provinceDefinitions.Count} province definitions");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to parse province definitions: {ex.Message}");
                return false;
            }
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

                OnMapDataLoaded?.Invoke("default.map");
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
                // Load bitmap using ParadoxDataLib's BmpReader
                _provincesMapReader = new BmpReader();
                // Note: Actual bitmap loading will be implemented in Phase 2

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

        private async Task<bool> LoadProvinceHistoriesAsync()
        {
            var fullPath = Path.Combine(Application.dataPath, dataRootPath, provinceHistoryPath);

            if (!Directory.Exists(fullPath))
            {
                LogWarning($"Province history directory not found: {fullPath}");
                return true; // Not critical
            }

            _provinceHistoryData.Clear();

            try
            {
                var files = Directory.GetFiles(fullPath, "*.txt");
                int loadedCount = 0;

                foreach (var file in files)
                {

                    try
                    {
                        var content = await File.ReadAllTextAsync(file);
                        var data = _paradoxParser.Parse(content);

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

                OnHistoryFilesLoaded?.Invoke(loadedCount);
                Log($"Loaded {loadedCount} province history files");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to load province histories: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> LoadCountryDataAsync()
        {
            // Placeholder for country data loading
            await Task.Delay(100); // Simulate async work
            Log("Country data loading placeholder");
            return true;
        }

        private async Task<bool> FinalizeLoadingAsync()
        {
            // Update memory usage tracking
            UpdateMemoryUsage();

            // Validate loaded data
            bool isValid = ValidateLoadedData();

            await Task.Delay(50); // Simulate finalization work

            Log($"Finalization complete. Memory usage: {_memoryUsageBytes / 1024 / 1024}MB");
            return isValid;
        }

        #endregion

        #region Utility Methods

        private List<ProvinceDefinition> ParseProvinceDefinitions(string csvContent)
        {
            var definitions = new List<ProvinceDefinition>();
            var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 1; i < lines.Length; i++) // Skip header
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

                        definitions.Add(new ProvinceDefinition(id, r, g, b, name, unused));
                    }
                }
            }

            return definitions;
        }

        private bool ExtractProvinceIdFromFilename(string filename, out int provinceId)
        {
            provinceId = 0;
            var parts = filename.Split(new char[] { '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 && int.TryParse(parts[0], out provinceId);
        }

        private bool ValidateLoadedData()
        {
            bool isValid = true;

            if (_provinceDefinitions == null || _provinceDefinitions.Count == 0)
            {
                LogWarning("No province definitions loaded");
                isValid = false;
            }

            if (_colorToProvinceId == null || _colorToProvinceId.Count == 0)
            {
                LogWarning("No color mappings loaded");
                isValid = false;
            }

            return isValid;
        }

        private void UpdateMemoryUsage()
        {
            long usage = 0;

            if (_provinceDefinitions != null)
                usage += _provinceDefinitions.Count * 64; // Rough estimate

            if (_provinceHistoryData != null)
                usage += _provinceHistoryData.Count * 256; // Rough estimate

            _memoryUsageBytes = usage;
        }

        private async Task<T> ExecuteWithErrorHandling<T>(Func<Task<T>> operation)
        {
            try
            {
                return await operation();
            }
            catch (ParadoxDataException)
            {
                throw; // Re-throw our custom exceptions
            }
            catch (Exception ex)
            {
                throw new ParadoxDataException($"Unexpected error: {ex.Message}", ex);
            }
        }

        private void CleanupResources()
        {
            _loadingCancellation?.Cancel();
            _loadingCancellation?.Dispose();
            _provincesMapReader?.Dispose();
            _cache?.Dispose();
        }

        #endregion

        #region Cache Event Handlers

        private void OnCacheHit(string key)
        {
            if (enableDebugLogging)
                Log($"Cache hit: {key}");
        }

        private void OnCacheMiss(string key)
        {
            if (enableDebugLogging)
                Log($"Cache miss: {key}");
        }

        private void OnCacheEviction(string key)
        {
            if (enableDebugLogging)
                Log($"Cache eviction: {key}");
        }

        #endregion

        #region Logging

        private void Log(string message)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[ParadoxDataManager] {message}");
            }
        }

        private void LogWarning(string message)
        {
            if (enableDebugLogging)
            {
                Debug.LogWarning($"[ParadoxDataManager] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[ParadoxDataManager] {message}");
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Loading state enumeration
    /// </summary>
    public enum LoadingState
    {
        Uninitialized,
        Initialized,
        Loading,
        Loaded,
        Error
    }


    /// <summary>
    /// Simple cache implementation for ParadoxDataLib data
    /// </summary>
    public class ParadoxDataCache : IDisposable
    {
        private readonly Dictionary<string, CacheEntry> _cache;
        private readonly long _maxSizeBytes;
        private readonly TimeSpan _expirationTime;
        private long _currentSizeBytes;

        public event System.Action<string> OnCacheHit;
        public event System.Action<string> OnCacheMiss;
        public event System.Action<string> OnCacheEviction;

        public ParadoxDataCache(long maxSizeMB, int expirationMinutes)
        {
            _cache = new Dictionary<string, CacheEntry>();
            _maxSizeBytes = maxSizeMB * 1024 * 1024;
            _expirationTime = TimeSpan.FromMinutes(expirationMinutes);
        }

        public T Get<T>(string key) where T : class
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (DateTime.Now - entry.CreatedTime < _expirationTime)
                {
                    OnCacheHit?.Invoke(key);
                    return entry.Data as T;
                }
                else
                {
                    _cache.Remove(key);
                    _currentSizeBytes -= entry.SizeBytes;
                    OnCacheEviction?.Invoke(key);
                }
            }

            OnCacheMiss?.Invoke(key);
            return null;
        }

        public void Set<T>(string key, T data, long sizeBytes) where T : class
        {
            // Remove existing entry if present
            if (_cache.ContainsKey(key))
            {
                _currentSizeBytes -= _cache[key].SizeBytes;
                _cache.Remove(key);
            }

            // Check if we need to evict entries
            while (_currentSizeBytes + sizeBytes > _maxSizeBytes && _cache.Count > 0)
            {
                EvictOldestEntry();
            }

            // Add new entry
            _cache[key] = new CacheEntry
            {
                Data = data,
                CreatedTime = DateTime.Now,
                SizeBytes = sizeBytes
            };
            _currentSizeBytes += sizeBytes;
        }

        private void EvictOldestEntry()
        {
            var oldest = DateTime.MaxValue;
            string oldestKey = null;

            foreach (var kvp in _cache)
            {
                if (kvp.Value.CreatedTime < oldest)
                {
                    oldest = kvp.Value.CreatedTime;
                    oldestKey = kvp.Key;
                }
            }

            if (oldestKey != null)
            {
                _currentSizeBytes -= _cache[oldestKey].SizeBytes;
                _cache.Remove(oldestKey);
                OnCacheEviction?.Invoke(oldestKey);
            }
        }

        public void Clear()
        {
            _cache.Clear();
            _currentSizeBytes = 0;
        }

        public void Dispose()
        {
            Clear();
        }

        private class CacheEntry
        {
            public object Data;
            public DateTime CreatedTime;
            public long SizeBytes;
        }
    }

    #endregion
}