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
using Core.Initialization;
using Core.Initialization.Phases;

namespace Core
{
    /// <summary>
    /// ENGINE LAYER: Master coordinator for engine initialization and data loading
    /// Orchestrates the complete loading pipeline from files to ready-to-play state
    /// Performance: Target <5 seconds for 10k provinces, <100MB memory during loading
    /// </summary>
    public class EngineInitializer : MonoBehaviour
    {
        [Header("Game Settings")]
        [SerializeField] private GameSettings gameSettings;
        [SerializeField] private bool initializeOnStart = true;
        [SerializeField] private bool enableDetailedLogging = true;

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

        // NEW: Phase-based initialization
        private InitializationContext initContext;
        private readonly List<IInitializationPhase> phases = new List<IInitializationPhase>();

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
                ArchonLogger.LogWarning("Game initialization already in progress!");
                return;
            }

            if (gameSettings == null)
            {
                ArchonLogger.LogError("GameSettings not assigned! Cannot initialize game.");
                ReportError("Missing GameSettings configuration");
                return;
            }

            ArchonLogger.Log("Starting game initialization...");
            StartCoroutine(InitializeGameCoroutine());
        }

        /// <summary>
        /// Main initialization coroutine that orchestrates all loading phases
        /// </summary>
        private IEnumerator InitializeGameCoroutine()
        {
            isLoading = true;
            var startTime = Time.realtimeSinceStartup;

            // NEW: Setup initialization context for phase-based system
            initContext = new InitializationContext
            {
                Settings = gameSettings,
                EnableDetailedLogging = enableDetailedLogging,
                OnProgress = (progress, status) => UpdateProgress(progress, status)
            };

            // Phase 1: Initialize Core Systems (0-5%) - NEW: Using phase-based architecture
            yield return StartCoroutine(ExecuteNewPhase(new CoreSystemsInitializationPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Sync old variables with new context
            gameState = initContext.GameState;
            gameRegistries = initContext.Registries;
            referenceResolver = initContext.ReferenceResolver;
            crossReferenceBuilder = initContext.CrossReferenceBuilder;
            dataValidator = initContext.DataValidator;

            // Phase 2: Load Static Data (5-15%) - NEW: Using phase-based architecture
            SetPhase(LoadingPhase.LoadingStaticData, 5f, "Loading static data...");
            yield return StartCoroutine(ExecuteNewPhase(new StaticDataLoadingPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 3: Load Province Data (15-40%) - NEW: Using phase-based architecture
            SetPhase(LoadingPhase.LoadingProvinces, 15f, "Loading province data...");
            yield return StartCoroutine(ExecuteNewPhase(new ProvinceDataLoadingPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Sync loaded province data back to old variables
            provinceInitialStates = initContext.ProvinceInitialStates;
            provinceDefinitions = initContext.ProvinceDefinitions;

            // NOTE: Do NOT dispose provinceInitialStates here - ReferenceLinkingPhase still needs it!
            // It will be disposed at the end of ReferenceLinkingPhase (line 203)

            // Phase 4: Load Country Data (40-60%) - NEW: Using phase-based architecture
            SetPhase(LoadingPhase.LoadingCountries, 40f, "Loading countries...");
            yield return StartCoroutine(ExecuteNewPhase(new CountryDataLoadingPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 5: Link References (60-65%) - NEW: Using phase-based architecture
            SetPhase(LoadingPhase.LinkingReferences, 60f, "Linking data references...");
            yield return StartCoroutine(ExecuteNewPhase(new ReferenceLinkingPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 6: Load Scenario Data (65-75%) - NEW: Using phase-based architecture
            SetPhase(LoadingPhase.LoadingScenario, 65f, "Loading scenario...");
            yield return StartCoroutine(ExecuteNewPhase(new ScenarioLoadingPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Phase 7-8: Systems Warmup (75-100%) - NEW: Using phase-based architecture
            SetPhase(LoadingPhase.InitializingSystems, 75f, "Initializing systems...");
            yield return StartCoroutine(ExecuteNewPhase(new SystemsWarmupPhase()));
            if (currentPhase == LoadingPhase.Error) yield break;

            // Complete
            var totalTime = Time.realtimeSinceStartup - startTime;
            CompleteInitialization(totalTime);

            isLoading = false;
        }

        /// <summary>
        /// NEW: Execute a phase using the new phase-based architecture
        /// </summary>
        private IEnumerator ExecuteNewPhase(IInitializationPhase phase)
        {
            if (enableDetailedLogging)
            {
                ArchonLogger.Log($"Starting phase: {phase.PhaseName}");
            }

            yield return StartCoroutine(phase.ExecuteAsync(initContext));

            // Check for errors
            if (initContext.HasError)
            {
                ReportError(initContext.ErrorMessage);
                yield break;
            }
        }

        /// <summary>
        /// Safely execute a phase with error handling (OLD SYSTEM - for legacy phases)
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
                ArchonLogger.LogError($"Critical error creating initialization phase: {e.Message}");
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
                ArchonLogger.LogError($"Error during initialization phase: {errorMessage}");
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

        // OLD PHASE METHODS REMOVED - Now using phase-based architecture:
        // - InitializeCoreSystemsPhase() → CoreSystemsInitializationPhase class
        // - LoadStaticDataPhase() → StaticDataLoadingPhase class
        // - LoadProvinceDataPhase() → ProvinceDataLoadingPhase class
        // - LoadCountryDataPhase() → CountryDataLoadingPhase class
        // - LinkingReferencesPhase() → ReferenceLinkingPhase class
        // - LoadScenarioDataPhase() → ScenarioLoadingPhase class
        // - InitializeSystemsPhase() + WarmCachesPhase() → SystemsWarmupPhase class

        /// <summary>
        /// Complete the initialization process
        /// </summary>
        private void CompleteInitialization(float totalTime)
        {
            currentPhase = LoadingPhase.Complete;
            currentProgress = 100f;
            currentStatus = "Game ready!";

            ArchonLogger.Log($"Game initialization complete in {totalTime:F2} seconds");

            // Emit the main simulation data ready event for presentation layer
            var simulationEvent = new SimulationDataReadyEvent
            {
                ProvinceCount = gameState.Provinces.ProvinceCount,
                CountryCount = gameState.Countries.CountryCount,
                LoadingTimeSeconds = totalTime,
                TimeStamp = Time.time
            };

            ArchonLogger.Log($"Emitting SimulationDataReadyEvent: {simulationEvent.ProvinceCount} provinces, {simulationEvent.CountryCount} countries");
            gameState.EventBus.Emit(simulationEvent);

            // Process events immediately to ensure MapGenerator receives the event
            gameState.EventBus.ProcessEvents();

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

            ArchonLogger.LogError($"Game initialization failed: {error}");

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
                ArchonLogger.Log($"[{phase}] {status} ({progress:F1}%)");
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
                ArchonLogger.Log($"[{currentPhase}] Complete: {message}");
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
    /// </summary>
    // ProvinceDataReadyEvent moved to Core.Initialization.Phases.ProvinceDataLoadingPhase

    /// <summary>
    /// Emitted when country data is fully loaded and ready
    /// </summary>
    // CountryDataReadyEvent moved to Core.Initialization.Phases.CountryDataLoadingPhase

    /// <summary>
    /// Emitted when static data (religions, cultures, trade goods) is loaded
    /// </summary>
    // StaticDataReadyEvent moved to Core.Initialization.Phases.StaticDataLoadingPhase

    /// <summary>
    /// Emitted when all string references have been resolved to numeric IDs
    /// </summary>
    // ReferencesLinkedEvent moved to Core.Initialization.Phases.ReferenceLinkingPhase
}