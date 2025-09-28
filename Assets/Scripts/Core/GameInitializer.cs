using UnityEngine;
using System.Threading.Tasks;
using System.Collections;
using Core.Systems;
using Core.Loaders;
using ParadoxParser.Jobs;
using Core.Loaders;
using Core.Data;

namespace Core
{
    /// <summary>
    /// Master coordinator for game initialization and data loading
    /// Orchestrates the complete loading pipeline from files to ready-to-play state
    /// Performance: Target <5 seconds for 10k provinces, <100MB memory during loading
    /// </summary>
    public class GameInitializer : MonoBehaviour
    {
        [Header("Game Settings")]
        [SerializeField] private GameSettings gameSettings;
        [SerializeField] private bool initializeOnStart = true;
        [SerializeField] private bool enableDetailedLogging = true;

        [Header("Loading Progress")]
        [SerializeField] private bool showLoadingUI = true;
        [SerializeField] private Canvas loadingCanvas;

        // Loading phases
        public enum LoadingPhase
        {
            NotStarted,
            InitializingCore,
            LoadingProvinces,
            LoadingCountries,
            LoadingScenario,
            InitializingSystems,
            WarmingCaches,
            Complete,
            Error
        }

        // Events
        public System.Action<LoadingPhase, float, string> OnLoadingProgress;
        public System.Action<bool, string> OnLoadingComplete;

        // State
        private LoadingPhase currentPhase = LoadingPhase.NotStarted;
        private float currentProgress = 0f;
        private string currentStatus = "";
        private bool isLoading = false;

        // Core systems
        private GameState gameState;
        private ProvinceMapProcessor provinceProcessor;
        private JobifiedCountryLoader countryLoader;

        // Properties
        public LoadingPhase CurrentPhase => currentPhase;
        public float Progress => currentProgress;
        public string Status => currentStatus;
        public bool IsLoading => isLoading;
        public bool IsComplete => currentPhase == LoadingPhase.Complete;

        void Start()
        {
            if (initializeOnStart)
            {
                StartInitialization();
            }
        }

        /// <summary>
        /// Start the complete game initialization process
        /// </summary>
        public void StartInitialization()
        {
            if (isLoading)
            {
                DominionLogger.LogWarning("Game initialization already in progress!");
                return;
            }

            if (gameSettings == null)
            {
                DominionLogger.LogError("GameSettings not assigned! Cannot initialize game.");
                ReportError("Missing GameSettings configuration");
                return;
            }

            DominionLogger.Log("Starting game initialization...");
            StartCoroutine(InitializeGameCoroutine());
        }

        /// <summary>
        /// Main initialization coroutine that orchestrates all loading phases
        /// </summary>
        private IEnumerator InitializeGameCoroutine()
        {
            isLoading = true;
            var startTime = Time.realtimeSinceStartup;

            // Phase 1: Initialize Core Systems (0-10%)
            yield return StartCoroutine(SafeExecutePhase(() => InitializeCoreSystemsPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 2: Load Province Data (10-40%)
            yield return StartCoroutine(SafeExecutePhase(() => LoadProvinceDataPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 3: Load Country Data (40-60%)
            yield return StartCoroutine(SafeExecutePhase(() => LoadCountryDataPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 4: Load Scenario Data (60-75%)
            yield return StartCoroutine(SafeExecutePhase(() => LoadScenarioDataPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 5: Initialize Derived Systems (75-90%)
            yield return StartCoroutine(SafeExecutePhase(() => InitializeSystemsPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 6: Warm Caches (90-100%)
            yield return StartCoroutine(SafeExecutePhase(() => WarmCachesPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Complete
            var totalTime = Time.realtimeSinceStartup - startTime;
            CompleteInitialization(totalTime);

            isLoading = false;
        }

        /// <summary>
        /// Safely execute a phase with error handling
        /// </summary>
        private IEnumerator SafeExecutePhase(System.Func<IEnumerator> phaseFunction)
        {
            IEnumerator phaseCoroutine = null;

            try
            {
                phaseCoroutine = phaseFunction();
            }
            catch (System.Exception e)
            {
                DominionLogger.LogError($"Critical error creating initialization phase: {e.Message}");
                Debug.LogException(e);
                ReportError($"Phase creation failed: {e.Message}");
                yield break;
            }

            yield return StartCoroutine(ExecutePhaseWithErrorHandling(phaseCoroutine));
        }

        /// <summary>
        /// Execute phase coroutine with error monitoring
        /// </summary>
        private IEnumerator ExecutePhaseWithErrorHandling(IEnumerator phaseCoroutine)
        {
            bool hasError = false;
            string errorMessage = "";

            // Execute the phase
            yield return StartCoroutine(MonitoredCoroutine(phaseCoroutine,
                (error) => { hasError = true; errorMessage = error; }));

            // Handle any errors that occurred
            if (hasError)
            {
                DominionLogger.LogError($"Error during initialization phase: {errorMessage}");
                ReportError($"Phase failed: {errorMessage}");
            }
        }

        /// <summary>
        /// Monitor a coroutine for exceptions
        /// </summary>
        private IEnumerator MonitoredCoroutine(IEnumerator coroutine, System.Action<string> onError)
        {
            while (true)
            {
                try
                {
                    if (!coroutine.MoveNext())
                        break;
                }
                catch (System.Exception e)
                {
                    onError?.Invoke(e.Message);
                    yield break;
                }
                yield return coroutine.Current;
            }
        }

        /// <summary>
        /// Phase 1: Initialize core infrastructure
        /// </summary>
        private IEnumerator InitializeCoreSystemsPhase()
        {
            SetPhase(LoadingPhase.InitializingCore, 0f, "Initializing core systems...");

            // Find or create GameState
            gameState = FindFirstObjectByType<GameState>();
            if (gameState == null)
            {
                var gameStateGO = new GameObject("GameState");
                gameState = gameStateGO.AddComponent<GameState>();
            }

            UpdateProgress(2f, "Creating event system...");
            yield return null;

            // Initialize GameState (this creates EventBus, TimeManager, etc.)
            gameState.InitializeSystems();

            UpdateProgress(5f, "Creating data loaders...");
            yield return null;

            // Create loaders
            provinceProcessor = new ProvinceMapProcessor();
            countryLoader = new JobifiedCountryLoader();

            UpdateProgress(10f, "Core systems ready");

            LogPhaseComplete("Core systems initialized successfully");
        }

        /// <summary>
        /// Phase 2: Load province data from BMP and definitions
        /// </summary>
        private IEnumerator LoadProvinceDataPhase()
        {
            SetPhase(LoadingPhase.LoadingProvinces, 10f, "Loading province map...");

            // Start province loading
            var provinceTask = LoadProvinceDataAsync();

            // Wait for completion while updating progress
            while (!provinceTask.IsCompleted)
            {
                yield return null;
            }

            if (provinceTask.Exception != null)
            {
                ReportError($"Province loading failed: {provinceTask.Exception.GetBaseException().Message}");
                yield break;
            }

            var mapResult = provinceTask.Result;
            if (!mapResult.Success)
            {
                ReportError($"Province loading failed: {mapResult.ErrorMessage}");
                yield break;
            }

            UpdateProgress(32f, "Initializing province system...");
            yield return null;

            // Initialize ProvinceSystem with loaded data
            gameState.Provinces.InitializeFromMapData(mapResult);

            UpdateProgress(35f, "Loading province initial states...");
            yield return null;

            // Load province initial states using Burst jobs
            gameState.Provinces.LoadProvinceInitialStates(gameSettings.DataDirectory);

            UpdateProgress(40f, "Province data loaded");
            LogPhaseComplete($"Loaded {gameState.Provinces.ProvinceCount} provinces with history data");

            // Emit province data ready event
            gameState.EventBus.Emit(new ProvinceDataReadyEvent
            {
                ProvinceCount = gameState.Provinces.ProvinceCount,
                HasDefinitions = mapResult.HasDefinitions,
                HasInitialStates = true, // We loaded initial states
                TimeStamp = Time.time
            });

            // Clean up
            mapResult.Dispose();
        }

        /// <summary>
        /// Phase 3: Load country data
        /// </summary>
        private IEnumerator LoadCountryDataPhase()
        {
            SetPhase(LoadingPhase.LoadingCountries, 40f, "Loading countries...");

            // Load country data
            var countryResult = countryLoader.LoadAllCountriesJob(gameSettings.CountriesDirectory);

            UpdateProgress(55f, "Initializing country system...");
            yield return null;

            if (!countryResult.Success)
            {
                ReportError($"Country loading failed: {countryResult.ErrorMessage}");
                yield break;
            }

            // Initialize CountrySystem with loaded data
            gameState.Countries.InitializeFromCountryData(countryResult);

            UpdateProgress(60f, "Country data loaded");
            LogPhaseComplete($"Loaded {gameState.Countries.CountryCount} countries");

            // Emit country data ready event
            gameState.EventBus.Emit(new CountryDataReadyEvent
            {
                CountryCount = gameState.Countries.CountryCount,
                HasScenarioData = false, // Will be set to true after scenario loading
                TimeStamp = Time.time
            });

            // Clean up
            countryResult.Dispose();
        }

        /// <summary>
        /// Phase 4: Load scenario data
        /// </summary>
        private IEnumerator LoadScenarioDataPhase()
        {
            SetPhase(LoadingPhase.LoadingScenario, 60f, "Loading scenario...");

            ScenarioLoader.ScenarioLoadResult scenarioResult;

            // Try to load scenario file if specified
            if (!string.IsNullOrEmpty(gameSettings.ScenariosDirectory))
            {
                var scenarioPath = System.IO.Path.Combine(gameSettings.ScenariosDirectory, "default_1444.json");

                UpdateProgress(62f, "Loading scenario file...");
                yield return null;

                if (System.IO.File.Exists(scenarioPath))
                {
                    scenarioResult = ScenarioLoader.LoadFromFile(scenarioPath);
                }
                else
                {
                    DominionLogger.LogWarning($"Scenario file not found: {scenarioPath}, using default");
                    scenarioResult = ScenarioLoader.CreateDefaultScenario();
                }
            }
            else
            {
                DominionLogger.Log("No scenario directory specified, using default scenario");
                scenarioResult = ScenarioLoader.CreateDefaultScenario();
            }

            UpdateProgress(65f, "Validating scenario...");
            yield return null;

            if (!scenarioResult.Success)
            {
                DominionLogger.LogWarning($"Scenario loading failed: {scenarioResult.ErrorMessage}, using default");
                scenarioResult = ScenarioLoader.CreateDefaultScenario();
            }

            // Validate scenario against loaded data
            var validationIssues = ScenarioLoader.ValidateScenario(scenarioResult.Data, gameState);
            if (validationIssues.Count > 0)
            {
                DominionLogger.LogWarning($"Scenario validation found {validationIssues.Count} issues");
                foreach (var issue in validationIssues)
                {
                    DominionLogger.LogWarning($"  - {issue}");
                }
            }

            UpdateProgress(70f, "Applying scenario...");
            yield return null;

            // Apply scenario to game state
            bool applySuccess = ScenarioLoader.ApplyScenario(scenarioResult.Data, gameState);
            if (!applySuccess)
            {
                DominionLogger.LogError("Failed to apply scenario");
                ReportError("Scenario application failed");
                yield break;
            }

            UpdateProgress(75f, "Scenario applied");
            LogPhaseComplete($"Applied scenario: {scenarioResult.Data.Name}");
        }

        /// <summary>
        /// Phase 5: Initialize derived systems
        /// </summary>
        private IEnumerator InitializeSystemsPhase()
        {
            SetPhase(LoadingPhase.InitializingSystems, 75f, "Initializing game systems...");

            // TODO: Initialize AI, Economy, Military systems
            UpdateProgress(80f, "Initializing AI...");
            yield return null;

            UpdateProgress(85f, "Initializing economy...");
            yield return null;

            UpdateProgress(90f, "Systems initialized");
            LogPhaseComplete("Game systems ready");
        }

        /// <summary>
        /// Phase 6: Warm up caches
        /// </summary>
        private IEnumerator WarmCachesPhase()
        {
            SetPhase(LoadingPhase.WarmingCaches, 90f, "Preparing game...");

            // Warm up query caches
            WarmUpCaches();

            UpdateProgress(95f, "Validating data...");
            yield return null;

            // Validate loaded data
            ValidateLoadedData();

            UpdateProgress(100f, "Game ready!");
            LogPhaseComplete("All systems ready");
        }

        /// <summary>
        /// Async province loading
        /// </summary>
        private async Task<ProvinceMapResult> LoadProvinceDataAsync()
        {
            // Hook up progress reporting
            provinceProcessor.OnProgressUpdate += (progress) =>
            {
                var adjustedProgress = 10f + (progress.ProgressPercentage * 25f);
                UpdateProgress(adjustedProgress, progress.CurrentOperation);
            };

            return await provinceProcessor.LoadProvinceMapAsync(
                gameSettings.ProvinceBitmapPath,
                gameSettings.ProvinceDefinitionsPath
            );
        }


        /// <summary>
        /// Warm up frequently accessed caches
        /// </summary>
        private void WarmUpCaches()
        {
            // Warm up some basic queries
            var provinceCount = gameState.ProvinceQueries.GetTotalProvinceCount();
            var countryCount = gameState.CountryQueries.GetTotalCountryCount();

            DominionLogger.Log($"Cache warm-up complete: {provinceCount} provinces, {countryCount} countries");
        }

        /// <summary>
        /// Validate that loaded data is consistent
        /// </summary>
        private void ValidateLoadedData()
        {
            var issues = 0;

            // Validate province system
            if (!gameState.Provinces.IsInitialized)
            {
                DominionLogger.LogError("ProvinceSystem not properly initialized!");
                issues++;
            }

            // Validate country system
            if (!gameState.Countries.IsInitialized)
            {
                DominionLogger.LogError("CountrySystem not properly initialized!");
                issues++;
            }

            if (issues > 0)
            {
                DominionLogger.LogWarning($"Data validation found {issues} issues");
            }
            else
            {
                DominionLogger.Log("Data validation passed");
            }
        }

        /// <summary>
        /// Complete the initialization process
        /// </summary>
        private void CompleteInitialization(float totalTime)
        {
            currentPhase = LoadingPhase.Complete;
            currentProgress = 100f;
            currentStatus = "Game ready!";

            DominionLogger.Log($"Game initialization complete in {totalTime:F2} seconds");

            // Emit the main simulation data ready event for presentation layer
            var simulationEvent = new SimulationDataReadyEvent
            {
                ProvinceCount = gameState.Provinces.ProvinceCount,
                CountryCount = gameState.Countries.CountryCount,
                LoadingTimeSeconds = totalTime,
                TimeStamp = Time.time
            };

            DominionLogger.Log($"Emitting SimulationDataReadyEvent: {simulationEvent.ProvinceCount} provinces, {simulationEvent.CountryCount} countries");
            gameState.EventBus.Emit(simulationEvent);

            // Process events immediately to ensure MapGenerator receives the event
            gameState.EventBus.ProcessEvents();

            // Hide loading UI
            if (loadingCanvas != null)
            {
                loadingCanvas.gameObject.SetActive(false);
            }

            // Emit completion event
            OnLoadingComplete?.Invoke(true, "Initialization successful");
        }

        /// <summary>
        /// Report an error and stop initialization
        /// </summary>
        private void ReportError(string error)
        {
            currentPhase = LoadingPhase.Error;
            currentStatus = $"Error: {error}";

            DominionLogger.LogError($"Game initialization failed: {error}");

            // Emit error event
            OnLoadingComplete?.Invoke(false, error);
        }

        /// <summary>
        /// Update current phase
        /// </summary>
        private void SetPhase(LoadingPhase phase, float progress, string status)
        {
            currentPhase = phase;
            currentProgress = progress;
            currentStatus = status;

            if (enableDetailedLogging)
            {
                DominionLogger.Log($"[{phase}] {status} ({progress:F1}%)");
            }

            OnLoadingProgress?.Invoke(phase, progress, status);
        }

        /// <summary>
        /// Update progress within current phase
        /// </summary>
        private void UpdateProgress(float progress, string status)
        {
            currentProgress = progress;
            currentStatus = status;

            OnLoadingProgress?.Invoke(currentPhase, progress, status);
        }

        /// <summary>
        /// Log phase completion
        /// </summary>
        private void LogPhaseComplete(string message)
        {
            if (enableDetailedLogging)
            {
                DominionLogger.Log($"[{currentPhase}] Complete: {message}");
            }
        }

        void OnDestroy()
        {
            // Clean up loaders
            provinceProcessor = null;
            countryLoader = null;
        }
    }

    // ===========================
    // SIMULATION EVENTS
    // Events that decouple simulation from presentation layer
    // ===========================

    /// <summary>
    /// Emitted when all simulation data is loaded and ready for presentation
    /// Allows presentation layer (MapGenerator, UI) to initialize without creating dependencies
    /// </summary>
    public struct SimulationDataReadyEvent : IGameEvent
    {
        public int ProvinceCount;
        public int CountryCount;
        public float LoadingTimeSeconds;
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// Emitted when province data is fully loaded and ready
    /// Includes province map, definitions, and initial state data
    /// </summary>
    public struct ProvinceDataReadyEvent : IGameEvent
    {
        public int ProvinceCount;
        public bool HasDefinitions;
        public bool HasInitialStates;
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// Emitted when country data is fully loaded and ready
    /// Includes all country files and country system initialization
    /// </summary>
    public struct CountryDataReadyEvent : IGameEvent
    {
        public int CountryCount;
        public bool HasScenarioData;
        public float TimeStamp { get; set; }
    }
}