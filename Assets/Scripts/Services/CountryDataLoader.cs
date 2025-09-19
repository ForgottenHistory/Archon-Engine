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
    /// Loads and processes country data from Paradox game files
    /// </summary>
    public class CountryDataLoader : MonoBehaviour
    {
        [Header("Country Data Settings")]
        [SerializeField] private bool loadOnStart = false;
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private int maxCountriesToLoad = 200; // Limit for testing

        [Header("File Paths")]
        [SerializeField] private string countryTagsPath = "common/country_tags";
        [SerializeField] private string countryHistoryPath = "history/countries";
        [SerializeField] private string commonCountriesPath = "common/countries";

        [Header("File Patterns")]
        [SerializeField] private string tagsFilePattern = "*.txt";
        [SerializeField] private string historyFilePattern = "*.txt";
        [SerializeField] private string commonFilePattern = "*.txt";

        // Events
        public event System.Action<float> OnLoadProgress;
        public event System.Action<string> OnLoadStageChanged;
        public event System.Action OnLoadComplete;
        public event System.Action<string> OnLoadError;

        // Data storage
        private Dictionary<string, CountryData> _countries;
        private Dictionary<string, string> _countryTagToFile;
        private GenericParadoxParser _parser;
        private ParadoxDataService _paradoxDataService;

        // State
        private bool _isLoaded = false;
        private bool _isLoading = false;

        public bool IsLoaded => _isLoaded;
        public bool IsLoading => _isLoading;
        public int LoadedCountryCount => _countries?.Count ?? 0;

        private void Awake()
        {
            _parser = new GenericParadoxParser();
            _countries = new Dictionary<string, CountryData>();
            _countryTagToFile = new Dictionary<string, string>();
            _paradoxDataService = FindObjectOfType<ParadoxDataService>();
        }

        private void Start()
        {
            if (loadOnStart)
            {
                StartCoroutine(LoadAllCountryDataCoroutine());
            }
        }

        /// <summary>
        /// Load all country data
        /// </summary>
        [ContextMenu("Load Country Data")]
        public void LoadAllCountryData()
        {
            StartCoroutine(LoadAllCountryDataCoroutine());
        }

        public IEnumerator LoadAllCountryDataCoroutine()
        {
            if (_isLoading || _isLoaded)
            {
                LogWarning("Country data loading already in progress or completed");
                yield break;
            }

            _isLoading = true;
            _countries.Clear();
            _countryTagToFile.Clear();

            // Phase 1: Load country tags
            OnLoadStageChanged?.Invoke("Loading country tags...");
            OnLoadProgress?.Invoke(0.1f);
            yield return StartCoroutine(LoadCountryTagsPhase());

            // Phase 2: Load country definitions from common/countries
            OnLoadStageChanged?.Invoke("Loading country definitions...");
            OnLoadProgress?.Invoke(0.3f);
            yield return StartCoroutine(LoadCountryDefinitionsPhase());

            // Phase 3: Load country history
            OnLoadStageChanged?.Invoke("Loading country histories...");
            OnLoadProgress?.Invoke(0.6f);
            yield return StartCoroutine(LoadCountryHistoriesPhase());

            // Phase 4: Finalization
            OnLoadStageChanged?.Invoke("Finalizing country data...");
            OnLoadProgress?.Invoke(0.9f);
            yield return StartCoroutine(FinalizationPhase());

            OnLoadProgress?.Invoke(1.0f);
            _isLoaded = true;
            OnLoadComplete?.Invoke();

            Log($"Country data loading completed: {_countries.Count} countries loaded");
            LogCountryStatistics();

            _isLoading = false;
        }

        private IEnumerator LoadCountryTagsPhase()
        {
            var tagsDir = Path.Combine(Application.dataPath, "Data", countryTagsPath);

            if (!Directory.Exists(tagsDir))
            {
                LogWarning($"Country tags directory not found: {tagsDir}");
                yield break;
            }

            var tagFiles = Directory.GetFiles(tagsDir, tagsFilePattern, SearchOption.AllDirectories);
            Log($"Found {tagFiles.Length} country tag files");

            foreach (var file in tagFiles)
            {
                yield return StartCoroutine(LoadCountryTagsFromFile(file));
            }

            Log($"Loaded {_countryTagToFile.Count} country tag mappings");
        }

        private IEnumerator LoadCountryTagsFromFile(string filePath)
        {
            var content = File.ReadAllText(filePath);
            var data = _parser.Parse(content);

            // Parse country tag assignments
            // Format: TAG = "countries/countryfile.txt"
            if (data.Children != null)
            {
                foreach (var kvp in data.Children)
                {
                    var tag = kvp.Key.Trim();
                    var valueNode = kvp.Value;
                    var countryFile = valueNode.Value?.ToString()?.Trim(' ', '"') ?? "";

                    if (tag.Length == 3 && !string.IsNullOrEmpty(countryFile))
                    {
                        _countryTagToFile[tag] = countryFile;

                        // Create initial country data
                        if (!_countries.ContainsKey(tag))
                        {
                            _countries[tag] = new CountryData
                            {
                                Tag = tag,
                                CountryFile = countryFile
                            };
                        }
                    }
                }
            }

            yield return null; // Yield every file to prevent frame drops
        }

        private IEnumerator LoadCountryDefinitionsPhase()
        {
            var definitionsDir = Path.Combine(Application.dataPath, "Data", commonCountriesPath);

            if (!Directory.Exists(definitionsDir))
            {
                LogWarning($"Country definitions directory not found: {definitionsDir}");
                yield break;
            }

            int loadedCount = 0;
            foreach (var kvp in _countryTagToFile.Take(maxCountriesToLoad))
            {
                var tag = kvp.Key;
                var countryFile = kvp.Value;

                var fullPath = Path.Combine(definitionsDir, countryFile);
                if (File.Exists(fullPath))
                {
                    yield return StartCoroutine(LoadCountryDefinition(tag, fullPath));
                    loadedCount++;
                }
                else
                {
                    LogWarning($"Country definition file not found: {fullPath}");
                }

                // Yield every 10 countries to prevent frame drops
                if (loadedCount % 10 == 0)
                {
                    yield return null;
                }
            }

            Log($"Loaded definitions for {loadedCount} countries");
        }

        private IEnumerator LoadCountryDefinition(string tag, string filePath)
        {
            var content = File.ReadAllText(filePath);
            var data = _parser.Parse(content);

            if (_countries.TryGetValue(tag, out var country))
            {
                // Extract country definition data
                ParseCountryDefinition(country, data);
            }

            yield return null;
        }

        private void ParseCountryDefinition(CountryData country, ParadoxNode data)
        {
            // Parse basic country properties
            country.GraphicalCulture = data.GetValue<string>("graphical_culture", "");
            country.Color = ParseColor(data.GetChild("color"));

            // Parse monarch names
            var monarchNames = data.GetChild("monarch_names");
            if (monarchNames != null && monarchNames.Children != null)
            {
                country.MonarchNames = new List<string>();
                foreach (var name in monarchNames.Children.Keys)
                {
                    country.MonarchNames.Add(name);
                }
            }

            // Parse ship names
            var shipNames = data.GetChild("ship_names");
            if (shipNames != null)
            {
                country.ShipNames = new List<string>();
                var names = data.GetValues<string>("ship_names");
                foreach (var name in names)
                {
                    country.ShipNames.Add(name);
                }
            }

            // Parse army names
            var armyNames = data.GetChild("army_names");
            if (armyNames != null)
            {
                country.ArmyNames = new List<string>();
                var names = data.GetValues<string>("army_names");
                foreach (var name in names)
                {
                    country.ArmyNames.Add(name);
                }
            }

            // Parse fleet names
            var fleetNames = data.GetChild("fleet_names");
            if (fleetNames != null)
            {
                country.FleetNames = new List<string>();
                var names = data.GetValues<string>("fleet_names");
                foreach (var name in names)
                {
                    country.FleetNames.Add(name);
                }
            }
        }

        private Color32 ParseColor(ParadoxNode colorNode)
        {
            if (colorNode == null) return new Color32(128, 128, 128, 255);

            if (colorNode.Items != null && colorNode.Items.Count >= 3)
            {
                var values = colorNode.Items.Select(item => item.Value?.ToString()).ToArray();
                if (byte.TryParse(values[0], out byte r) &&
                    byte.TryParse(values[1], out byte g) &&
                    byte.TryParse(values[2], out byte b))
                {
                    return new Color32(r, g, b, 255);
                }
            }

            return new Color32(128, 128, 128, 255);
        }

        private IEnumerator LoadCountryHistoriesPhase()
        {
            var historyDir = Path.Combine(Application.dataPath, "Data", countryHistoryPath);

            if (!Directory.Exists(historyDir))
            {
                LogWarning($"Country history directory not found: {historyDir}");
                yield break;
            }

            var historyFiles = Directory.GetFiles(historyDir, historyFilePattern, SearchOption.AllDirectories);
            Log($"Found {historyFiles.Length} country history files");

            int loadedCount = 0;
            foreach (var file in historyFiles.Take(maxCountriesToLoad))
            {
                var tag = ExtractCountryTagFromHistoryFile(file);
                if (!string.IsNullOrEmpty(tag) && _countries.ContainsKey(tag))
                {
                    yield return StartCoroutine(LoadCountryHistory(tag, file));
                    loadedCount++;
                }

                // Yield every 10 files to prevent frame drops
                if (loadedCount % 10 == 0)
                {
                    yield return null;
                }
            }

            Log($"Loaded history for {loadedCount} countries");
        }

        private string ExtractCountryTagFromHistoryFile(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // Try different patterns:
            // "TAG - CountryName.txt", "TAG.txt", "CountryName - TAG.txt"
            var parts = fileName.Split(new char[] { '-', ' ', '_' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var cleanPart = part.Trim();
                if (cleanPart.Length == 3 && cleanPart.All(char.IsUpper))
                {
                    return cleanPart;
                }
            }

            // If no 3-letter tag found, try the first 3 characters
            if (fileName.Length >= 3)
            {
                var possibleTag = fileName.Substring(0, 3).ToUpper();
                if (_countries.ContainsKey(possibleTag))
                {
                    return possibleTag;
                }
            }

            return null;
        }

        private IEnumerator LoadCountryHistory(string tag, string filePath)
        {
            var content = File.ReadAllText(filePath);
            var data = _parser.Parse(content);

            if (_countries.TryGetValue(tag, out var country))
            {
                country.HistoryData = ParseCountryHistory(data);
            }

            yield return null;
        }

        private CountryHistoryData ParseCountryHistory(ParadoxNode data)
        {
            var history = new CountryHistoryData
            {
                HistoricalEntries = new List<CountryHistoricalEntry>()
            };

            // Parse initial state (non-dated properties)
            history.InitialCapital = ParseInt(data.GetValue<string>("capital", "0"));
            history.InitialGovernment = data.GetValue<string>("government", "");
            history.InitialPrimaryCulture = data.GetValue<string>("primary_culture", "");
            history.InitialReligion = data.GetValue<string>("religion", "");
            history.InitialTechnologyGroup = data.GetValue<string>("technology_group", "");
            history.InitialUnitType = data.GetValue<string>("unit_type", "");

            // Parse accepted cultures
            var acceptedCultures = data.GetValues<string>("add_accepted_culture");
            if (acceptedCultures != null && acceptedCultures.Any())
            {
                history.InitialAcceptedCultures = new List<string>(acceptedCultures);
            }

            // Parse dated entries
            if (data.Children != null)
            {
                foreach (var child in data.Children)
                {
                    if (TryParseDate(child.Key, out DateTime date))
                    {
                        var entry = new CountryHistoricalEntry
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

                        history.HistoricalEntries.Add(entry);
                    }
                }
            }

            // Sort entries by date
            history.HistoricalEntries = history.HistoricalEntries.OrderBy(e => e.Date).ToList();

            return history;
        }

        private int ParseInt(string value)
        {
            if (int.TryParse(value, out int result))
                return result;
            return 0;
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

        private IEnumerator FinalizationPhase()
        {
            // Validate loaded data
            int validCountries = 0;
            foreach (var country in _countries.Values)
            {
                if (!string.IsNullOrEmpty(country.Tag))
                {
                    validCountries++;
                }
            }

            Log($"Finalization: {validCountries} valid countries out of {_countries.Count} total");
            yield return null;
        }

        /// <summary>
        /// Get country data by tag
        /// </summary>
        public CountryData GetCountryData(string tag)
        {
            if (_countries.TryGetValue(tag, out var country))
            {
                return country;
            }
            return null;
        }

        /// <summary>
        /// Get all loaded country data
        /// </summary>
        public Dictionary<string, CountryData> GetAllCountryData()
        {
            return _countries;
        }

        /// <summary>
        /// Get country state at a specific date
        /// </summary>
        public CountryStateAtDate GetCountryStateAtDate(string tag, DateTime date)
        {
            var country = GetCountryData(tag);
            if (country?.HistoryData == null)
                return null;

            var state = new CountryStateAtDate
            {
                Date = date,
                Tag = tag,
                Capital = country.HistoryData.InitialCapital,
                Government = country.HistoryData.InitialGovernment,
                PrimaryCulture = country.HistoryData.InitialPrimaryCulture,
                Religion = country.HistoryData.InitialReligion,
                TechnologyGroup = country.HistoryData.InitialTechnologyGroup,
                AcceptedCultures = new List<string>(country.HistoryData.InitialAcceptedCultures ?? new List<string>())
            };

            // Apply historical changes up to the specified date
            foreach (var entry in country.HistoryData.HistoricalEntries.Where(e => e.Date <= date))
            {
                ApplyHistoricalChanges(state, entry);
            }

            return state;
        }

        private void ApplyHistoricalChanges(CountryStateAtDate state, CountryHistoricalEntry entry)
        {
            foreach (var change in entry.Changes)
            {
                switch (change.Key.ToLower())
                {
                    case "capital":
                        if (int.TryParse(change.Value.ToString(), out int capital))
                            state.Capital = capital;
                        break;
                    case "government":
                        state.Government = change.Value.ToString();
                        break;
                    case "primary_culture":
                        state.PrimaryCulture = change.Value.ToString();
                        break;
                    case "religion":
                        state.Religion = change.Value.ToString();
                        break;
                    case "technology_group":
                        state.TechnologyGroup = change.Value.ToString();
                        break;
                    case "add_accepted_culture":
                        if (!state.AcceptedCultures.Contains(change.Value.ToString()))
                            state.AcceptedCultures.Add(change.Value.ToString());
                        break;
                    case "remove_accepted_culture":
                        state.AcceptedCultures.Remove(change.Value.ToString());
                        break;
                }
            }
        }

        /// <summary>
        /// Export country data summary
        /// </summary>
        [ContextMenu("Export Country Summary")]
        public void ExportCountrySummary()
        {
            if (_countries.Count == 0)
            {
                LogWarning("No country data loaded to export");
                return;
            }

            var summary = "Tag;GraphicalCulture;MonarchNames;ShipNames;HistoryLoaded\n";

            foreach (var kvp in _countries.OrderBy(x => x.Key))
            {
                var country = kvp.Value;
                var monarchCount = country.MonarchNames?.Count ?? 0;
                var shipCount = country.ShipNames?.Count ?? 0;
                var hasHistory = country.HistoryData != null;

                summary += $"{country.Tag};{country.GraphicalCulture};{monarchCount};{shipCount};{hasHistory}\n";
            }

            var path = Path.Combine(Application.dataPath, "CountryDataSummary.csv");
            File.WriteAllText(path, summary);
            Log($"Exported country data summary to: {path}");

            #if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
            #endif
        }

        private void LogCountryStatistics()
        {
            var countriesWithHistory = _countries.Values.Count(c => c.HistoryData != null);
            var totalMonarchNames = _countries.Values.Sum(c => c.MonarchNames?.Count ?? 0);

            Log($"Country Statistics:");
            Log($"  Total Countries: {_countries.Count}");
            Log($"  Countries with History: {countriesWithHistory}");
            Log($"  Total Monarch Names: {totalMonarchNames}");
        }

        private void Log(string message)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[CountryDataLoader] {message}");
            }
        }

        private void LogWarning(string message)
        {
            if (enableDebugLogging)
            {
                Debug.LogWarning($"[CountryDataLoader] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[CountryDataLoader] {message}");
        }
    }

    /// <summary>
    /// Complete country data including definition and history
    /// </summary>
    [System.Serializable]
    public class CountryData
    {
        public string Tag;
        public string CountryFile;

        // Definition data (from common/countries)
        public string GraphicalCulture;
        public Color32 Color;
        public List<string> MonarchNames;
        public List<string> ShipNames;
        public List<string> ArmyNames;
        public List<string> FleetNames;

        // History data
        public CountryHistoryData HistoryData;

        public string GetSummary()
        {
            var monarchCount = MonarchNames?.Count ?? 0;
            var hasHistory = HistoryData != null;
            return $"Tag: {Tag}, Culture: {GraphicalCulture}, {monarchCount} monarch names, History: {hasHistory}";
        }
    }

    /// <summary>
    /// Historical data for a country
    /// </summary>
    [System.Serializable]
    public class CountryHistoryData
    {
        // Initial state
        public int InitialCapital;
        public string InitialGovernment;
        public string InitialPrimaryCulture;
        public string InitialReligion;
        public string InitialTechnologyGroup;
        public string InitialUnitType;
        public List<string> InitialAcceptedCultures;

        // Historical changes over time
        public List<CountryHistoricalEntry> HistoricalEntries;
    }

    /// <summary>
    /// Changes that occurred on a specific date for a country
    /// </summary>
    [System.Serializable]
    public class CountryHistoricalEntry
    {
        public DateTime Date;
        public Dictionary<string, object> Changes;

        public string GetChangesSummary()
        {
            return string.Join(", ", Changes.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }
    }

    /// <summary>
    /// Country state at a specific point in time
    /// </summary>
    [System.Serializable]
    public class CountryStateAtDate
    {
        public DateTime Date;
        public string Tag;
        public int Capital;
        public string Government;
        public string PrimaryCulture;
        public string Religion;
        public string TechnologyGroup;
        public List<string> AcceptedCultures;
    }
}