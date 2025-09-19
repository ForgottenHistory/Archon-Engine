using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using ProvinceSystem.Data;
using ParadoxDataLib.Core.Parsers;
using ParadoxDataLib.Core.Common;

namespace ProvinceSystem.Services
{
    /// <summary>
    /// Loads and processes province history files from Paradox game data
    /// </summary>
    public class ProvinceHistoryLoader : MonoBehaviour
    {
        [Header("History Settings")]
        [SerializeField] private bool loadOnStart = false;
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private int maxFilesToLoad = 100; // Limit for testing

        [Header("File Patterns")]
        [SerializeField] private string historyPath = "history/provinces";
        [SerializeField] private string filePattern = "*.txt";

        // Events
        public event System.Action<float> OnLoadProgress;
        public event System.Action<string> OnLoadStageChanged;
        public event System.Action OnLoadComplete;
        public event System.Action<string> OnLoadError;

        // Data storage
        private Dictionary<int, ProvinceHistoryData> _provinceHistories;
        private GenericParadoxParser _parser;
        private ParadoxDataService _paradoxDataService;

        // State
        private bool _isLoaded = false;
        private bool _isLoading = false;

        public bool IsLoaded => _isLoaded;
        public bool IsLoading => _isLoading;
        public int LoadedHistoryCount => _provinceHistories?.Count ?? 0;

        private void Awake()
        {
            _parser = new GenericParadoxParser();
            _provinceHistories = new Dictionary<int, ProvinceHistoryData>();
            _paradoxDataService = FindObjectOfType<ParadoxDataService>();
        }

        private void Start()
        {
            if (loadOnStart)
            {
                StartCoroutine(LoadAllHistoriesCoroutine());
            }
        }

        /// <summary>
        /// Load all province history files
        /// </summary>
        [ContextMenu("Load Province Histories")]
        public void LoadAllHistories()
        {
            StartCoroutine(LoadAllHistoriesCoroutine());
        }

        public IEnumerator LoadAllHistoriesCoroutine()
        {
            if (_isLoading || _isLoaded)
            {
                LogWarning("History loading already in progress or completed");
                yield break;
            }

            _isLoading = true;
            _provinceHistories.Clear();

            OnLoadStageChanged?.Invoke("Scanning province history files...");
            OnLoadProgress?.Invoke(0.1f);

            var historyDir = Path.Combine(Application.dataPath, "Data", historyPath);

            if (!Directory.Exists(historyDir))
            {
                LogError($"Province history directory not found: {historyDir}");
                OnLoadError?.Invoke($"History directory not found: {historyDir}");
                _isLoading = false;
                yield break;
            }

            var files = Directory.GetFiles(historyDir, filePattern, SearchOption.AllDirectories);
            Log($"Found {files.Length} province history files");

            // Limit files for testing
            if (files.Length > maxFilesToLoad)
            {
                Log($"Limiting to first {maxFilesToLoad} files for testing");
                files = files.Take(maxFilesToLoad).ToArray();
            }

            OnLoadStageChanged?.Invoke($"Loading {files.Length} province history files...");

            int loadedCount = 0;
            int failedCount = 0;

            for (int i = 0; i < files.Length; i++)
            {
                var file = files[i];
                OnLoadProgress?.Invoke(0.1f + (i / (float)files.Length) * 0.8f);

                if (LoadSingleHistoryFile(file))
                {
                    loadedCount++;
                }
                else
                {
                    failedCount++;
                }

                // Yield every 10 files to prevent frame drops
                if (i % 10 == 0)
                {
                    yield return null;
                }
            }

            OnLoadStageChanged?.Invoke("Finalizing province histories...");
            OnLoadProgress?.Invoke(1.0f);

            _isLoaded = true;
            OnLoadComplete?.Invoke();

            Log($"Province history loading completed: {loadedCount} loaded, {failedCount} failed");
            _isLoading = false;
        }

        private bool LoadSingleHistoryFile(string filePath)
        {
            try
            {
                // Extract province ID from filename
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                if (!ExtractProvinceIdFromFilename(fileName, out int provinceId))
                {
                    LogWarning($"Could not extract province ID from filename: {fileName}");
                    return false;
                }

                // Read and parse file content
                var content = File.ReadAllText(filePath);
                var paradoxData = _parser.Parse(content);

                // Convert to our history format
                var historyData = ConvertToHistoryData(provinceId, paradoxData, filePath);

                if (historyData != null)
                {
                    _provinceHistories[provinceId] = historyData;

                    if (enableDebugLogging && provinceId <= 10) // Log first few for debugging
                    {
                        Log($"Loaded history for province {provinceId}: {historyData.GetSummary()}");
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to parse {filePath}: {ex.Message}");
            }

            return false;
        }

        private bool ExtractProvinceIdFromFilename(string filename, out int provinceId)
        {
            provinceId = 0;

            // Try different filename patterns:
            // "1-Uppland", "100 - Friesland", "1234.txt", etc.
            var parts = filename.Split(new char[] { '-', ' ', '_' }, StringSplitOptions.RemoveEmptyEntries);

            // Try first part as number
            if (parts.Length > 0 && int.TryParse(parts[0], out provinceId))
            {
                return true;
            }

            // Try the whole filename as number
            if (int.TryParse(filename, out provinceId))
            {
                return true;
            }

            return false;
        }

        private ProvinceHistoryData ConvertToHistoryData(int provinceId, ParadoxNode paradoxData, string filePath)
        {
            if (paradoxData == null)
                return null;

            var historyData = new ProvinceHistoryData
            {
                ProvinceId = provinceId,
                FilePath = filePath,
                HistoricalEntries = new List<ProvinceHistoricalEntry>()
            };

            // Extract direct properties (not dated)
            ExtractDirectProperties(historyData, paradoxData);

            // Extract dated entries
            ExtractDatedEntries(historyData, paradoxData);

            return historyData;
        }

        private void ExtractDirectProperties(ProvinceHistoryData historyData, ParadoxNode paradoxData)
        {
            // Get initial/base values (properties without dates)
            var owner = paradoxData.GetValue<string>("owner", "");
            if (!string.IsNullOrEmpty(owner))
                historyData.InitialOwner = owner;

            var controller = paradoxData.GetValue<string>("controller", "");
            if (!string.IsNullOrEmpty(controller))
                historyData.InitialController = controller;

            var culture = paradoxData.GetValue<string>("culture", "");
            if (!string.IsNullOrEmpty(culture))
                historyData.InitialCulture = culture;

            var religion = paradoxData.GetValue<string>("religion", "");
            if (!string.IsNullOrEmpty(religion))
                historyData.InitialReligion = religion;

            var tradeGood = paradoxData.GetValue<string>("trade_goods", "");
            if (!string.IsNullOrEmpty(tradeGood))
                historyData.InitialTradeGood = tradeGood;

            // Parse numeric values
            if (int.TryParse(paradoxData.GetValue<string>("base_tax", "0"), out int baseTax))
                historyData.InitialBaseTax = baseTax;

            if (int.TryParse(paradoxData.GetValue<string>("base_production", "0"), out int baseProd))
                historyData.InitialBaseProduction = baseProd;

            if (int.TryParse(paradoxData.GetValue<string>("base_manpower", "0"), out int baseMan))
                historyData.InitialBaseManpower = baseMan;

            // Extract cores
            var cores = paradoxData.GetValues<string>("add_core");
            if (cores != null && cores.Any())
            {
                historyData.InitialCores = new List<string>(cores);
            }
        }

        private void ExtractDatedEntries(ProvinceHistoryData historyData, ParadoxNode paradoxData)
        {
            // Look for dated entries (children with date names)
            if (paradoxData.Children != null)
            {
                foreach (var child in paradoxData.Children)
                {
                    if (TryParseDate(child.Key, out DateTime date))
                    {
                        var entry = new ProvinceHistoricalEntry
                        {
                            Date = date,
                            Changes = new Dictionary<string, object>()
                        };

                        // Extract all changes for this date
                        if (child.Value.Children != null)
                        {
                            foreach (var property in child.Value.Children)
                            {
                                entry.Changes[property.Key] = property.Value.Value;
                            }
                        }

                        historyData.HistoricalEntries.Add(entry);
                    }
                }
            }

            // Sort entries by date
            historyData.HistoricalEntries = historyData.HistoricalEntries.OrderBy(e => e.Date).ToList();
        }

        private bool TryParseDate(string dateString, out DateTime date)
        {
            date = default;

            // Try Paradox date format "1444.11.11"
            if (DateTime.TryParseExact(dateString, "yyyy.M.d", null, System.Globalization.DateTimeStyles.None, out date))
                return true;

            // Try with leading zeros
            if (DateTime.TryParseExact(dateString, "yyyy.MM.dd", null, System.Globalization.DateTimeStyles.None, out date))
                return true;

            // Try other common formats
            if (DateTime.TryParse(dateString, out date))
                return true;

            return false;
        }

        /// <summary>
        /// Get province history data by ID
        /// </summary>
        public ProvinceHistoryData GetProvinceHistory(int provinceId)
        {
            if (_provinceHistories.TryGetValue(provinceId, out var history))
            {
                return history;
            }
            return null;
        }

        /// <summary>
        /// Get all loaded province histories
        /// </summary>
        public Dictionary<int, ProvinceHistoryData> GetAllProvinceHistories()
        {
            return _provinceHistories;
        }

        /// <summary>
        /// Get province state at a specific date
        /// </summary>
        public ProvinceStateAtDate GetProvinceStateAtDate(int provinceId, DateTime date)
        {
            var history = GetProvinceHistory(provinceId);
            if (history == null)
                return null;

            var state = new ProvinceStateAtDate
            {
                Date = date,
                ProvinceId = provinceId,
                Owner = history.InitialOwner,
                Controller = history.InitialController,
                Culture = history.InitialCulture,
                Religion = history.InitialReligion,
                TradeGood = history.InitialTradeGood,
                BaseTax = history.InitialBaseTax,
                BaseProduction = history.InitialBaseProduction,
                BaseManpower = history.InitialBaseManpower,
                Cores = new List<string>(history.InitialCores ?? new List<string>())
            };

            // Apply historical changes up to the specified date
            foreach (var entry in history.HistoricalEntries.Where(e => e.Date <= date))
            {
                ApplyHistoricalChanges(state, entry);
            }

            return state;
        }

        private void ApplyHistoricalChanges(ProvinceStateAtDate state, ProvinceHistoricalEntry entry)
        {
            foreach (var change in entry.Changes)
            {
                switch (change.Key.ToLower())
                {
                    case "owner":
                        state.Owner = change.Value.ToString();
                        break;
                    case "controller":
                        state.Controller = change.Value.ToString();
                        break;
                    case "culture":
                        state.Culture = change.Value.ToString();
                        break;
                    case "religion":
                        state.Religion = change.Value.ToString();
                        break;
                    case "trade_goods":
                        state.TradeGood = change.Value.ToString();
                        break;
                    case "base_tax":
                        if (int.TryParse(change.Value.ToString(), out int tax))
                            state.BaseTax = tax;
                        break;
                    case "base_production":
                        if (int.TryParse(change.Value.ToString(), out int prod))
                            state.BaseProduction = prod;
                        break;
                    case "base_manpower":
                        if (int.TryParse(change.Value.ToString(), out int man))
                            state.BaseManpower = man;
                        break;
                    case "add_core":
                        if (!state.Cores.Contains(change.Value.ToString()))
                            state.Cores.Add(change.Value.ToString());
                        break;
                    case "remove_core":
                        state.Cores.Remove(change.Value.ToString());
                        break;
                }
            }
        }

        /// <summary>
        /// Export province history summary
        /// </summary>
        [ContextMenu("Export History Summary")]
        public void ExportHistorySummary()
        {
            if (_provinceHistories.Count == 0)
            {
                LogWarning("No province histories loaded to export");
                return;
            }

            var summary = "Province;Owner;Culture;Religion;BaseTax;BaseProduction;BaseManpower;HistoricalEntries\n";

            foreach (var kvp in _provinceHistories.OrderBy(x => x.Key))
            {
                var history = kvp.Value;
                summary += $"{history.ProvinceId};{history.InitialOwner};{history.InitialCulture};" +
                          $"{history.InitialReligion};{history.InitialBaseTax};{history.InitialBaseProduction};" +
                          $"{history.InitialBaseManpower};{history.HistoricalEntries.Count}\n";
            }

            var path = Path.Combine(Application.dataPath, "ProvinceHistorySummary.csv");
            File.WriteAllText(path, summary);
            Log($"Exported province history summary to: {path}");

            #if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
            #endif
        }

        private void Log(string message)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[ProvinceHistoryLoader] {message}");
            }
        }

        private void LogWarning(string message)
        {
            if (enableDebugLogging)
            {
                Debug.LogWarning($"[ProvinceHistoryLoader] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[ProvinceHistoryLoader] {message}");
        }
    }

    /// <summary>
    /// Complete province history data for a single province
    /// </summary>
    [System.Serializable]
    public class ProvinceHistoryData
    {
        public int ProvinceId;
        public string FilePath;

        // Initial state (from start of file)
        public string InitialOwner;
        public string InitialController;
        public string InitialCulture;
        public string InitialReligion;
        public string InitialTradeGood;
        public int InitialBaseTax;
        public int InitialBaseProduction;
        public int InitialBaseManpower;
        public List<string> InitialCores;

        // Historical changes over time
        public List<ProvinceHistoricalEntry> HistoricalEntries;

        public string GetSummary()
        {
            var coreCount = InitialCores?.Count ?? 0;
            var entryCount = HistoricalEntries?.Count ?? 0;
            return $"Owner: {InitialOwner}, Culture: {InitialCulture}, {coreCount} cores, {entryCount} historical entries";
        }
    }

    /// <summary>
    /// Represents changes that occurred on a specific date
    /// </summary>
    [System.Serializable]
    public class ProvinceHistoricalEntry
    {
        public DateTime Date;
        public Dictionary<string, object> Changes;

        public string GetChangesSummary()
        {
            return string.Join(", ", Changes.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }
    }

    /// <summary>
    /// Province state at a specific point in time
    /// </summary>
    [System.Serializable]
    public class ProvinceStateAtDate
    {
        public DateTime Date;
        public int ProvinceId;
        public string Owner;
        public string Controller;
        public string Culture;
        public string Religion;
        public string TradeGood;
        public int BaseTax;
        public int BaseProduction;
        public int BaseManpower;
        public List<string> Cores;
    }
}