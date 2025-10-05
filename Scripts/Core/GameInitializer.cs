using UnityEngine;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System;
using Core.Systems;
using Core.Loaders;
using ParadoxParser.Jobs;
using Core.Data;
using Core.Registries;
using Core.Linking;

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
        [SerializeField] private Canvas loadingCanvas;

        // Loading phases
        public enum LoadingPhase
        {
            NotStarted,
            InitializingCore,
            LoadingStaticData,    // NEW: Load religions, cultures, trade goods
            LoadingProvinces,
            LoadingCountries,
            LinkingReferences,    // NEW: Resolve string references to IDs
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
        // TODO: Reimplement for JSON5
        // private ProvinceMapProcessor provinceProcessor;
        // private JobifiedCountryLoader countryLoader;

        // Data linking systems
        private GameRegistries gameRegistries;
        private ProvinceInitialStateLoadResult provinceInitialStates;
        private System.Collections.Generic.List<DefinitionLoader.DefinitionEntry> provinceDefinitions;
        private ReferenceResolver referenceResolver;
        private CrossReferenceBuilder crossReferenceBuilder;
        private DataValidator dataValidator;

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

            // Phase 1: Initialize Core Systems (0-5%)
            yield return StartCoroutine(SafeExecutePhase(() => InitializeCoreSystemsPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 2: Load Static Data (5-15%) - NEW: Religions, cultures, trade goods
            yield return StartCoroutine(SafeExecutePhase(() => LoadStaticDataPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 3: Load Province Data (15-35%)
            yield return StartCoroutine(SafeExecutePhase(() => LoadProvinceDataPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 4: Load Country Data (35-50%)
            yield return StartCoroutine(SafeExecutePhase(() => LoadCountryDataPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 5: Link References (50-65%) - NEW: Convert strings to IDs
            yield return StartCoroutine(SafeExecutePhase(() => LinkingReferencesPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 6: Load Scenario Data (65-75%)
            yield return StartCoroutine(SafeExecutePhase(() => LoadScenarioDataPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 7: Initialize Derived Systems (75-85%)
            yield return StartCoroutine(SafeExecutePhase(() => InitializeSystemsPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 8: Warm Caches (85-100%)
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

            UpdateProgress(1f, "Creating event system...");
            yield return null;

            // Initialize GameState (this creates EventBus, TimeManager, etc.)
            gameState.InitializeSystems();

            UpdateProgress(2f, "Initializing data linking systems...");
            yield return null;

            // Initialize data linking systems
            gameRegistries = new GameRegistries();
            referenceResolver = new ReferenceResolver(gameRegistries);
            crossReferenceBuilder = new CrossReferenceBuilder(gameRegistries);
            dataValidator = new DataValidator(gameRegistries);

            // Provide registries to GameState for central access
            gameState.SetRegistries(gameRegistries);

            UpdateProgress(3f, "Creating data loaders...");
            yield return null;

            // TODO: Reimplement loaders for JSON5
            // Create loaders
            // provinceProcessor = new ProvinceMapProcessor();
            // countryLoader = new JobifiedCountryLoader();

            UpdateProgress(10f, "Core systems ready");

            LogPhaseComplete("Core systems initialized successfully");
        }

        /// <summary>
        /// Phase 2: Load static data (religions, cultures, trade goods, terrains)
        /// </summary>
        private IEnumerator LoadStaticDataPhase()
        {
            SetPhase(LoadingPhase.LoadingStaticData, 5f, "Loading static data...");

            UpdateProgress(6f, "Loading religions...");
            yield return null;

            // Load religions
            ReligionLoader.LoadReligions(gameRegistries.Religions, gameSettings.DataDirectory);

            UpdateProgress(8f, "Loading cultures...");
            yield return null;

            // Load cultures
            CultureLoader.LoadCultures(gameRegistries.Cultures, gameSettings.DataDirectory);

            UpdateProgress(10f, "Loading trade goods...");
            yield return null;

            // Load trade goods
            TradeGoodLoader.LoadTradeGoods(gameRegistries.TradeGoods, gameSettings.DataDirectory);

            UpdateProgress(12f, "Loading terrain types...");
            yield return null;

            // Load terrain types
            TerrainLoader.LoadTerrains(gameRegistries.Terrains, gameSettings.DataDirectory);

            UpdateProgress(13f, "Loading water province definitions...");
            yield return null;

            // Load water province definitions from default.json5 and terrain.json5
            WaterProvinceLoader.LoadWaterProvinceData(gameSettings.DataDirectory);

            UpdateProgress(14f, "Validating static data...");
            yield return null;

            // Validate that static data loaded successfully
            if (!gameRegistries.ValidateRegistries())
            {
                ReportError("Static data validation failed - some required data could not be loaded");
                yield break;
            }

            UpdateProgress(15f, "Static data ready");

            // Emit static data ready event
            gameState.EventBus.Emit(new StaticDataReadyEvent
            {
                ReligionCount = gameRegistries.Religions.Count,
                CultureCount = gameRegistries.Cultures.Count,
                TradeGoodCount = gameRegistries.TradeGoods.Count,
                TerrainCount = gameRegistries.Terrains.Count,
                TimeStamp = Time.time
            });
            gameState.EventBus.ProcessEvents();

            LogPhaseComplete($"Static data loaded: {gameRegistries.Religions.Count} religions, {gameRegistries.Cultures.Count} cultures, {gameRegistries.TradeGoods.Count} trade goods");
        }

        /// <summary>
        /// Phase 3: Load province data using definition.csv + JSON5 + Burst architecture
        /// </summary>
        private IEnumerator LoadProvinceDataPhase()
        {
            SetPhase(LoadingPhase.LoadingProvinces, 15f, "Loading province data...");

            // Step 1: Load definition.csv to get ALL provinces (including uncolonized ones)
            UpdateProgress(15f, "Loading definition.csv...");
            yield return null;

            provinceDefinitions = DefinitionLoader.LoadDefinitions(gameSettings.DataDirectory);
            if (provinceDefinitions.Count == 0)
            {
                ReportError("Failed to load province definitions from definition.csv");
                yield break;
            }

            DominionLogger.Log($"Loaded {provinceDefinitions.Count} province definitions from definition.csv");

            // Step 2: Load province initial states from JSON5 files (history data)
            UpdateProgress(20f, "Loading province JSON5 files...");
            yield return null;

            provinceInitialStates = BurstProvinceHistoryLoader.LoadProvinceInitialStates(gameSettings.DataDirectory);

            if (!provinceInitialStates.Success)
            {
                ReportError($"Province loading failed: {provinceInitialStates.ErrorMessage}");
                yield break;
            }

            DominionLogger.Log($"Province loading complete: {provinceInitialStates.LoadedCount} provinces loaded, {provinceInitialStates.FailedCount} failed");

            UpdateProgress(32f, "Initializing province system...");
            yield return null;

            // Initialize ProvinceSystem with loaded data
            gameState.Provinces.InitializeFromProvinceStates(provinceInitialStates);

            UpdateProgress(40f, "Province data loaded");
            LogPhaseComplete($"Loaded {provinceInitialStates.LoadedCount} provinces from JSON5 (ready for reference linking)");
        }

        /// <summary>
        /// Phase 3: Load country data
        /// </summary>
        private IEnumerator LoadCountryDataPhase()
        {
            SetPhase(LoadingPhase.LoadingCountries, 35f, "Loading countries...");

            // Load country tags FIRST to get correct tag→filename mapping
            var countryTagResult = CountryTagLoader.LoadCountryTags(gameSettings.DataDirectory);
            Dictionary<string, string> tagMapping = null;

            if (countryTagResult.Success)
            {
                tagMapping = countryTagResult.CountryTags;
                DominionLogger.Log($"Loaded {tagMapping.Count} country tag mappings");
            }
            else
            {
                DominionLogger.LogWarning($"Failed to load country tags: {countryTagResult.ErrorMessage}. Tags will be extracted from filenames.");
            }

            UpdateProgress(50f, "Loading country JSON5 files...");
            yield return null;

            // Load country data using JSON5 + Burst architecture
            var countriesPath = System.IO.Path.Combine(gameSettings.DataDirectory, "common", "countries");
            var countryDataResult = BurstCountryLoader.LoadAllCountries(countriesPath, tagMapping);

            if (!countryDataResult.Success)
            {
                ReportError($"Country loading failed: {countryDataResult.ErrorMessage}");
                yield break;
            }

            DominionLogger.Log($"Country loading complete: {countryDataResult.Statistics.FilesProcessed} countries loaded, {countryDataResult.Statistics.FilesSkipped} failed");

            UpdateProgress(55f, "Initializing country system...");
            yield return null;

            // Initialize CountrySystem with loaded data
            gameState.Countries.InitializeFromCountryData(countryDataResult);

            UpdateProgress(60f, "Country phase complete");
            LogPhaseComplete($"Loaded {countryDataResult.Statistics.FilesProcessed} countries from JSON5 (ready for reference linking)");
        }

        /// <summary>
        /// Phase 5: Link all string references to numeric IDs
        /// </summary>
        private IEnumerator LinkingReferencesPhase()
        {
            SetPhase(LoadingPhase.LinkingReferences, 50f, "Linking data references...");

            UpdateProgress(52f, "Registering countries...");
            yield return null;

            // Load real country tags using ManifestLoader pattern
            var countryTagResult = CountryTagLoader.LoadCountryTags(gameSettings.DataDirectory);

            if (!countryTagResult.Success)
            {
                DominionLogger.LogError($"Failed to load country tags: {countryTagResult.ErrorMessage}");
                // Continue with limited functionality
            }

            // Register countries using actual tags from CountrySystem
            var countryIds = gameState.Countries.GetAllCountryIds();
            DominionLogger.Log($"Country registration: Found {countryIds.Length} countries to register");

            var tagToIdMapping = new Dictionary<string, ushort>();
            var registeredCount = 0;

            // Use actual tags from CountrySystem instead of assigning by position
            for (int i = 0; i < countryIds.Length; i++)
            {
                var countryId = countryIds[i];

                // Get the ACTUAL tag from CountrySystem
                var tag = gameState.Countries.GetCountryTag(countryId);
                if (string.IsNullOrEmpty(tag) || tag == "---")
                    continue;

                var countryData = new Core.Registries.CountryData
                {
                    Id = countryId,
                    Tag = tag
                };

                try
                {
                    gameRegistries.Countries.Register(tag, countryData);
                    tagToIdMapping[tag] = countryId;
                    registeredCount++;

                    if (registeredCount <= 10) // Log first 10 for debugging
                    {
                        DominionLogger.LogDataLinking($"Registered country '{tag}' with ID {countryId}");
                    }
                }
                catch (System.Exception e)
                {
                    DominionLogger.LogDataLinkingError($"Failed to register country {tag} (ID: {countryId}): {e.Message}");
                }
            }

            DominionLogger.Log($"Country registration complete: {gameRegistries.Countries.Count} countries registered with real tags");

            UpdateProgress(53f, "Registering provinces with JSON5 history data...");
            yield return null;

            // STEP 1: Register provinces that have JSON5 history files (full data)
            DominionLogger.Log($"Province processing: Found {provinceInitialStates.LoadedCount} provinces with JSON5 history files");

            for (int i = 0; i < provinceInitialStates.InitialStates.Length; i++)
            {
                var initialState = provinceInitialStates.InitialStates[i];

                // Skip provinces with invalid IDs (0 or negative)
                if (initialState.ProvinceID <= 0)
                    continue;

                // Create ProvinceData with real loaded data
                var provinceData = new Core.Registries.ProvinceData
                {
                    RuntimeId = (ushort)initialState.ProvinceID,
                    DefinitionId = initialState.ProvinceID,
                    Name = $"Province {initialState.ProvinceID}",
                    Development = initialState.Development,
                    // Fix terrain: if province has development, it's land (terrain = 1), otherwise ocean (terrain = 0)
                    Terrain = (byte)(initialState.Development > 0 ? 1 : 0),
                    Flags = initialState.Flags,
                    BaseTax = initialState.BaseTax,
                    BaseProduction = initialState.BaseProduction,
                    BaseManpower = initialState.BaseManpower,
                    CenterOfTrade = initialState.CenterOfTrade
                };

                try
                {
                    gameRegistries.Provinces.Register(initialState.ProvinceID, provinceData);
                }
                catch (System.Exception e)
                {
                    DominionLogger.LogWarning($"Failed to register province {initialState.ProvinceID}: {e.Message}");
                }
            }

            DominionLogger.Log($"Province registration (JSON5): {gameRegistries.Provinces.Count} provinces registered with historical data");

            UpdateProgress(55f, "Filling in missing provinces from definition.csv...");
            yield return null;

            // STEP 2: Register remaining provinces from definition.csv (uncolonized/water provinces without JSON5)
            // RegisterDefinitions() automatically skips provinces already registered in step 1
            DefinitionLoader.RegisterDefinitions(provinceDefinitions, gameRegistries.Provinces);

            DominionLogger.Log($"Province registration complete: {gameRegistries.Provinces.Count} total provinces (JSON5 + definition.csv)");

            UpdateProgress(58f, "Resolving province references...");
            yield return null;

            // Resolve string references in province data
            for (int i = 0; i < provinceInitialStates.InitialStates.Length; i++)
            {
                var initialState = provinceInitialStates.InitialStates[i];

                // Skip provinces with invalid IDs
                if (initialState.ProvinceID <= 0)
                    continue;

                var provinceData = gameRegistries.Provinces.GetByDefinition(initialState.ProvinceID);
                if (provinceData != null)
                {
                    referenceResolver.ResolveProvinceReferences(ref initialState, provinceData);

                    // CRITICAL: Save the updated initialState back to the array
                    provinceInitialStates.InitialStates[i] = initialState;
                }
            }

            UpdateProgress(60f, "Resolving country references...");
            yield return null;

            // Resolve references for all countries
            // TODO: This will be implemented when country data contains string references

            UpdateProgress(60f, "Applying resolved province data...");
            yield return null;

            // Apply the resolved province data to the hot ProvinceSystem
            gameState.Provinces.ApplyResolvedInitialStates(provinceInitialStates.InitialStates);

            UpdateProgress(62f, "Building cross-references...");
            yield return null;

            // Build bidirectional references
            crossReferenceBuilder.BuildAllCrossReferences();

            UpdateProgress(64f, "Validating data integrity...");
            yield return null;

            // Validate all references
            if (!dataValidator.ValidateGameData())
            {
                ReportError("Data validation failed after linking references");
                yield break;
            }

            UpdateProgress(65f, "Reference linking complete");

            // Emit references linked event
            gameState.EventBus.Emit(new ReferencesLinkedEvent
            {
                CountriesLinked = gameRegistries.Countries.Count,
                ProvincesLinked = gameRegistries.Provinces.Count,
                ValidationErrors = dataValidator.GetErrors().Count,
                ValidationWarnings = dataValidator.GetWarnings().Count,
                TimeStamp = Time.time
            });
            gameState.EventBus.ProcessEvents();

            LogPhaseComplete($"Linked references: {gameRegistries.Countries.Count} countries, {gameRegistries.Provinces.Count} provinces");

            // Clean up
            provinceInitialStates.Dispose();
        }

        /// <summary>
        /// Phase 6: Load scenario data
        /// </summary>
        private IEnumerator LoadScenarioDataPhase()
        {
            SetPhase(LoadingPhase.LoadingScenario, 65f, "Loading scenario...");

            ScenarioLoader.ScenarioLoadResult scenarioResult;

            // Try to load scenario file if specified
            var scenariosDirectory = System.IO.Path.Combine(gameSettings.DataDirectory, "history", "countries");
            if (System.IO.Directory.Exists(scenariosDirectory))
            {
                var scenarioPath = System.IO.Path.Combine(scenariosDirectory, "default_1444.json");

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

    /// <summary>
    /// Emitted when static data (religions, cultures, trade goods) is loaded
    /// </summary>
    public struct StaticDataReadyEvent : IGameEvent
    {
        public int ReligionCount;
        public int CultureCount;
        public int TradeGoodCount;
        public int TerrainCount;
        public float TimeStamp { get; set; }
    }

    /// <summary>
    /// Emitted when all string references have been resolved to numeric IDs
    /// Indicates the data linking phase is complete
    /// </summary>
    public struct ReferencesLinkedEvent : IGameEvent
    {
        public int CountriesLinked;
        public int ProvincesLinked;
        public int ValidationErrors;
        public int ValidationWarnings;
        public float TimeStamp { get; set; }
    }
}