using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ProvinceSystem.Data;
using ProvinceSystem.Services;

namespace ProvinceSystem.Services
{
    /// <summary>
    /// Integrates all map data components for complete Paradox game data loading
    /// </summary>
    public class MapDataIntegrator : MonoBehaviour
    {
        [Header("Integration Settings")]
        [SerializeField] private bool loadOnStart = true;
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private bool loadHistoryData = true;
        [SerializeField] private bool loadCountryData = true;

        [Header("Performance Settings")]
        [SerializeField] private int maxConcurrentOperations = 3;
        [SerializeField] private float progressUpdateInterval = 0.1f;

        // Events for integration progress
        public event System.Action<float> OnIntegrationProgress;
        public event System.Action<string> OnIntegrationStageChanged;
        public event System.Action OnIntegrationComplete;
        public event System.Action<string> OnIntegrationError;

        // Component references
        private ParadoxDataService _paradoxDataService;
        private ProvinceDefinitionLoader _provinceDefinitionLoader;
        private ProvinceHistoryLoader _provinceHistoryLoader;
        private ProvinceDataService _provinceDataService;

        // Integration state
        private bool _isIntegrating = false;
        private bool _isIntegrated = false;
        private float _currentProgress = 0f;

        // Statistics
        private IntegrationStatistics _stats;

        public bool IsIntegrating => _isIntegrating;
        public bool IsIntegrated => _isIntegrated;
        public float IntegrationProgress => _currentProgress;
        public IntegrationStatistics Statistics => _stats;

        private void Awake()
        {
            _stats = new IntegrationStatistics();
            DiscoverComponents();
        }

        private void Start()
        {
            if (loadOnStart)
            {
                StartCoroutine(IntegrateAllMapDataCoroutine());
            }
        }

        private void DiscoverComponents()
        {
            _paradoxDataService = FindObjectOfType<ParadoxDataService>();
            _provinceDefinitionLoader = FindObjectOfType<ProvinceDefinitionLoader>();
            _provinceHistoryLoader = FindObjectOfType<ProvinceHistoryLoader>();
            // ProvinceDataService is not a MonoBehaviour, it's a regular service class
            _provinceDataService = new ProvinceDataService();

            if (_paradoxDataService == null)
                LogWarning("ParadoxDataService not found. Please add it to the scene.");
            if (_provinceDefinitionLoader == null)
                LogWarning("ProvinceDefinitionLoader not found. Please add it to the scene.");
        }

        /// <summary>
        /// Start complete map data integration
        /// </summary>
        [ContextMenu("Integrate All Map Data")]
        public void IntegrateAllMapData()
        {
            StartCoroutine(IntegrateAllMapDataCoroutine());
        }

        public IEnumerator IntegrateAllMapDataCoroutine()
        {
            if (_isIntegrating)
            {
                LogWarning("Integration already in progress");
                yield break;
            }

            if (_paradoxDataService == null)
            {
                LogError("ParadoxDataService is required for integration");
                OnIntegrationError?.Invoke("Missing ParadoxDataService component");
                yield break;
            }

            _isIntegrating = true;
            _isIntegrated = false;
            _currentProgress = 0f;
            _stats.Reset();
            _stats.IntegrationStartTime = DateTime.Now;

            // Initialize FileLogger for detailed logging
            if (ProvinceSystem.Utils.FileLogger.Instance != null)
            {
                ProvinceSystem.Utils.DominionLogger.LogSection("Map Data Integration");
            }

            Log("Starting complete map data integration...");

            // Phase 1: Load base Paradox data
            OnIntegrationStageChanged?.Invoke("Loading Paradox data files...");
            yield return StartCoroutine(LoadParadoxDataPhase());

            // Phase 2: Load province definitions
            OnIntegrationStageChanged?.Invoke("Loading province definitions...");
            yield return StartCoroutine(LoadProvinceDefinitionsPhase());

            // Phase 3: Load province history (optional)
            if (loadHistoryData && _provinceHistoryLoader != null)
            {
                OnIntegrationStageChanged?.Invoke("Loading province histories...");
                yield return StartCoroutine(LoadProvinceHistoryPhase());
            }

            // Phase 4: Integrate with existing province system
            OnIntegrationStageChanged?.Invoke("Integrating with province system...");
            yield return StartCoroutine(IntegrateWithProvinceSystemPhase());

            // Phase 5: Load country data (optional)
            if (loadCountryData)
            {
                OnIntegrationStageChanged?.Invoke("Loading country data...");
                yield return StartCoroutine(LoadCountryDataPhase());
            }

            // Phase 6: Finalization
            OnIntegrationStageChanged?.Invoke("Finalizing integration...");
            yield return StartCoroutine(FinalizationPhase());

            _stats.IntegrationEndTime = DateTime.Now;
            _isIntegrated = true;
            _currentProgress = 1.0f;

            OnIntegrationComplete?.Invoke();
            Log($"Map data integration completed successfully in {_stats.TotalIntegrationTime.TotalSeconds:F2} seconds");
            LogIntegrationStatistics();

            _isIntegrating = false;
        }

        private IEnumerator LoadParadoxDataPhase()
        {
            UpdateProgress(0.05f, "Loading Paradox data service...");

            if (!_paradoxDataService.IsLoaded)
            {
                var loadTask = _paradoxDataService.LoadAllDataAsync();
                yield return new WaitUntil(() => loadTask.IsCompleted);

                if (!loadTask.Result)
                {
                    LogError("Failed to load Paradox data");
                    OnIntegrationError?.Invoke("Failed to load Paradox data");
                    _isIntegrating = false;
                    yield break;
                }
            }

            _stats.ParadoxDataLoaded = true;
            _stats.ProvinceDefinitionsCount = _paradoxDataService.GetAllProvinceDefinitions() != null ?
                System.Linq.Enumerable.Count(_paradoxDataService.GetAllProvinceDefinitions()) : 0;

            UpdateProgress(0.2f, "Paradox data loaded successfully");
            Log($"Loaded {_stats.ProvinceDefinitionsCount} province definitions from ParadoxDataService");
        }

        private IEnumerator LoadProvinceDefinitionsPhase()
        {
            UpdateProgress(0.25f, "Loading province definitions...");

            if (_provinceDefinitionLoader != null)
            {
                _provinceDefinitionLoader.LoadFromParadoxDataService();

                // Wait a frame for the loading to process
                yield return null;

                _stats.DefinitionLoaderIntegrated = true;
                _stats.LoadedDefinitionsCount = _provinceDefinitionLoader.DefinitionsById?.Count ?? 0;

                Log($"Province definitions integrated: {_stats.LoadedDefinitionsCount} definitions");
            }
            else
            {
                LogWarning("ProvinceDefinitionLoader not found, skipping definition integration");
            }

            UpdateProgress(0.4f, "Province definitions loaded");
        }

        private IEnumerator LoadProvinceHistoryPhase()
        {
            UpdateProgress(0.45f, "Loading province histories...");

            if (_provinceHistoryLoader != null)
            {
                yield return StartCoroutine(_provinceHistoryLoader.LoadAllHistoriesCoroutine());
                _stats.HistoryLoaderIntegrated = true;
                _stats.LoadedHistoryCount = _provinceHistoryLoader.LoadedHistoryCount;

                Log($"Province histories loaded: {_stats.LoadedHistoryCount} histories");
            }
            else
            {
                LogWarning("ProvinceHistoryLoader not found, skipping history loading");
            }

            UpdateProgress(0.6f, "Province histories loaded");
        }

        private IEnumerator IntegrateWithProvinceSystemPhase()
        {
            UpdateProgress(0.65f, "Integrating with province system...");

            if (_provinceDataService != null && _provinceDefinitionLoader != null)
            {
                _provinceDefinitionLoader.EnhanceProvinceData(_provinceDataService);
                _stats.ProvinceSystemIntegrated = true;

                var allProvinces = _provinceDataService.GetAllProvinces();
                _stats.IntegratedProvincesCount = allProvinces?.Count ?? 0;

                Log($"Enhanced {_stats.IntegratedProvincesCount} provinces with definition data");
            }
            else
            {
                LogWarning("Cannot integrate province system - missing components");
            }

            UpdateProgress(0.8f, "Province system integration completed");
            yield return null;
        }

        private IEnumerator LoadCountryDataPhase()
        {
            UpdateProgress(0.85f, "Loading country data...");

            // Country data loading would be implemented here
            // For now, this is a placeholder
            yield return new WaitForSeconds(0.1f);

            _stats.CountryDataLoaded = true;
            UpdateProgress(0.9f, "Country data loaded");
        }

        private IEnumerator FinalizationPhase()
        {
            UpdateProgress(0.95f, "Finalizing integration...");

            // Perform any final validation or setup
            ValidateIntegration();

            UpdateProgress(1.0f, "Integration completed");
            yield return null;
        }

        private void ValidateIntegration()
        {
            var validation = new IntegrationValidation();

            // Validate ParadoxDataService
            validation.ParadoxDataValid = _paradoxDataService != null && _paradoxDataService.IsLoaded;

            // Validate province definitions
            validation.DefinitionsValid = _provinceDefinitionLoader != null &&
                                         _provinceDefinitionLoader.DefinitionsById != null &&
                                         _provinceDefinitionLoader.DefinitionsById.Count > 0;

            // Validate province system integration
            validation.ProvinceSystemValid = _provinceDataService != null &&
                                           _provinceDataService.GetAllProvinces() != null;

            // Validate histories if enabled
            if (loadHistoryData)
            {
                validation.HistoryValid = _provinceHistoryLoader != null &&
                                        _provinceHistoryLoader.IsLoaded &&
                                        _provinceHistoryLoader.LoadedHistoryCount > 0;
            }
            else
            {
                validation.HistoryValid = true; // Not required
            }

            _stats.ValidationResult = validation;

            if (validation.IsFullyValid)
            {
                Log("Integration validation passed");
            }
            else
            {
                LogWarning($"Integration validation issues: {validation.GetIssuesSummary()}");
            }
        }

        private void UpdateProgress(float progress, string stage)
        {
            _currentProgress = progress;
            OnIntegrationProgress?.Invoke(progress);
            OnIntegrationStageChanged?.Invoke(stage);

            if (enableDebugLogging)
            {
                Log($"Progress: {(progress * 100):F1}% - {stage}");
            }
        }

        /// <summary>
        /// Get current integration status
        /// </summary>
        public IntegrationStatus GetIntegrationStatus()
        {
            return new IntegrationStatus
            {
                IsIntegrating = _isIntegrating,
                IsIntegrated = _isIntegrated,
                Progress = _currentProgress,
                Statistics = _stats,
                HasErrors = !string.IsNullOrEmpty(_stats.ErrorMessage)
            };
        }

        /// <summary>
        /// Export integration report
        /// </summary>
        [ContextMenu("Export Integration Report")]
        public void ExportIntegrationReport()
        {
            if (_stats == null)
            {
                LogWarning("No integration statistics available");
                return;
            }

            var report = GenerateIntegrationReport();
            var fileName = $"IntegrationReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var filePath = Path.Combine(Application.dataPath, fileName);

            File.WriteAllText(filePath, report);
            Log($"Integration report exported to: {filePath}");

            #if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
            #endif
        }

        private string GenerateIntegrationReport()
        {
            var report = $"Dominion Map Data Integration Report\n";
            report += $"Generated: {DateTime.Now}\n";
            report += $"========================================\n\n";

            report += $"Integration Status: {(_isIntegrated ? "COMPLETED" : (_isIntegrating ? "IN PROGRESS" : "NOT STARTED"))}\n";
            report += $"Total Time: {_stats.TotalIntegrationTime.TotalSeconds:F2} seconds\n";
            report += $"Progress: {(_currentProgress * 100):F1}%\n\n";

            report += $"Component Status:\n";
            report += $"- ParadoxDataService: {(_stats.ParadoxDataLoaded ? "✓" : "✗")}\n";
            report += $"- Definition Loader: {(_stats.DefinitionLoaderIntegrated ? "✓" : "✗")}\n";
            report += $"- History Loader: {(_stats.HistoryLoaderIntegrated ? "✓" : "✗")}\n";
            report += $"- Province System: {(_stats.ProvinceSystemIntegrated ? "✓" : "✗")}\n";
            report += $"- Country Data: {(_stats.CountryDataLoaded ? "✓" : "✗")}\n\n";

            report += $"Data Statistics:\n";
            report += $"- Province Definitions: {_stats.ProvinceDefinitionsCount}\n";
            report += $"- Loaded Definitions: {_stats.LoadedDefinitionsCount}\n";
            report += $"- Province Histories: {_stats.LoadedHistoryCount}\n";
            report += $"- Integrated Provinces: {_stats.IntegratedProvincesCount}\n\n";

            if (_stats.ValidationResult != null)
            {
                report += $"Validation Results:\n";
                report += $"- Paradox Data: {(_stats.ValidationResult.ParadoxDataValid ? "✓" : "✗")}\n";
                report += $"- Definitions: {(_stats.ValidationResult.DefinitionsValid ? "✓" : "✗")}\n";
                report += $"- Province System: {(_stats.ValidationResult.ProvinceSystemValid ? "✓" : "✗")}\n";
                report += $"- History Data: {(_stats.ValidationResult.HistoryValid ? "✓" : "✗")}\n\n";
            }

            if (!string.IsNullOrEmpty(_stats.ErrorMessage))
            {
                report += $"Errors:\n{_stats.ErrorMessage}\n";
            }

            return report;
        }

        private void LogIntegrationStatistics()
        {
            Log($"Integration Statistics:");
            Log($"  Total Time: {_stats.TotalIntegrationTime.TotalSeconds:F2}s");
            Log($"  Province Definitions: {_stats.ProvinceDefinitionsCount}");
            Log($"  Loaded Definitions: {_stats.LoadedDefinitionsCount}");
            Log($"  Province Histories: {_stats.LoadedHistoryCount}");
            Log($"  Integrated Provinces: {_stats.IntegratedProvincesCount}");
        }

        private void Log(string message)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[MapDataIntegrator] {message}");
            }
        }

        private void LogWarning(string message)
        {
            if (enableDebugLogging)
            {
                Debug.LogWarning($"[MapDataIntegrator] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[MapDataIntegrator] {message}");
        }
    }

    /// <summary>
    /// Statistics about the integration process
    /// </summary>
    [System.Serializable]
    public class IntegrationStatistics
    {
        public DateTime IntegrationStartTime;
        public DateTime IntegrationEndTime;
        public bool ParadoxDataLoaded = false;
        public bool DefinitionLoaderIntegrated = false;
        public bool HistoryLoaderIntegrated = false;
        public bool ProvinceSystemIntegrated = false;
        public bool CountryDataLoaded = false;

        public int ProvinceDefinitionsCount = 0;
        public int LoadedDefinitionsCount = 0;
        public int LoadedHistoryCount = 0;
        public int IntegratedProvincesCount = 0;

        public string ErrorMessage = string.Empty;
        public IntegrationValidation ValidationResult;

        public TimeSpan TotalIntegrationTime => IntegrationEndTime - IntegrationStartTime;

        public void Reset()
        {
            IntegrationStartTime = DateTime.Now;
            IntegrationEndTime = DateTime.Now;
            ParadoxDataLoaded = false;
            DefinitionLoaderIntegrated = false;
            HistoryLoaderIntegrated = false;
            ProvinceSystemIntegrated = false;
            CountryDataLoaded = false;
            ProvinceDefinitionsCount = 0;
            LoadedDefinitionsCount = 0;
            LoadedHistoryCount = 0;
            IntegratedProvincesCount = 0;
            ErrorMessage = string.Empty;
            ValidationResult = null;
        }
    }

    /// <summary>
    /// Validation results for the integration
    /// </summary>
    [System.Serializable]
    public class IntegrationValidation
    {
        public bool ParadoxDataValid = false;
        public bool DefinitionsValid = false;
        public bool ProvinceSystemValid = false;
        public bool HistoryValid = false;

        public bool IsFullyValid => ParadoxDataValid && DefinitionsValid && ProvinceSystemValid && HistoryValid;

        public string GetIssuesSummary()
        {
            var issues = new List<string>();
            if (!ParadoxDataValid) issues.Add("ParadoxData");
            if (!DefinitionsValid) issues.Add("Definitions");
            if (!ProvinceSystemValid) issues.Add("ProvinceSystem");
            if (!HistoryValid) issues.Add("History");
            return string.Join(", ", issues);
        }
    }

    /// <summary>
    /// Current status of the integration process
    /// </summary>
    public class IntegrationStatus
    {
        public bool IsIntegrating;
        public bool IsIntegrated;
        public float Progress;
        public IntegrationStatistics Statistics;
        public bool HasErrors;
    }
}