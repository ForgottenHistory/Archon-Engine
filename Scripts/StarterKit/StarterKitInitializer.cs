using UnityEngine;
using System.Collections;
using Core;
using Core.Systems;
using Map.Core;
using Map.Interaction;

namespace StarterKit
{
    /// <summary>
    /// Coordinates EngineMapInitializer + StarterKit systems.
    /// Use this as the entry point for StarterKit scenes.
    /// Owns PlayerState and EconomySystem as plain classes.
    /// </summary>
    public class StarterKitInitializer : MonoBehaviour
    {
        [Header("Engine References")]
        [SerializeField] private EngineMapInitializer engineMapInitializer;

        [Header("UI Components")]
        [SerializeField] private CountrySelectionUI countrySelectionUI;
        [SerializeField] private ResourceBarUI resourceBarUI;
        [SerializeField] private TimeUI timeUI;
        [SerializeField] private ProvinceInfoUI provinceInfoUI;

        [Header("Configuration")]
        [SerializeField] private bool initializeOnStart = true;
        [SerializeField] private bool logProgress = true;

        // Owned systems (plain classes)
        private PlayerState playerState;
        private EconomySystem economySystem;

        private bool isInitialized;

        public bool IsInitialized => isInitialized;
        public PlayerState PlayerState => playerState;
        public EconomySystem EconomySystem => economySystem;

        void Start()
        {
            if (initializeOnStart)
            {
                StartCoroutine(InitializeSequence());
            }
        }

        void OnDestroy()
        {
            economySystem?.Dispose();
        }

        public void StartInitialization()
        {
            if (!isInitialized)
            {
                StartCoroutine(InitializeSequence());
            }
        }

        private IEnumerator InitializeSequence()
        {
            if (logProgress)
                ArchonLogger.Log("=== Starting StarterKit initialization ===", "starter_kit");

            // Find references if not assigned
            if (engineMapInitializer == null)
                engineMapInitializer = FindFirstObjectByType<EngineMapInitializer>();
            if (countrySelectionUI == null)
                countrySelectionUI = FindFirstObjectByType<CountrySelectionUI>();
            if (resourceBarUI == null)
                resourceBarUI = FindFirstObjectByType<ResourceBarUI>();
            if (timeUI == null)
                timeUI = FindFirstObjectByType<TimeUI>();
            if (provinceInfoUI == null)
                provinceInfoUI = FindFirstObjectByType<ProvinceInfoUI>();

            // Validate engine initializer
            if (engineMapInitializer == null)
            {
                ArchonLogger.LogError("StarterKitInitializer: EngineMapInitializer not found!", "starter_kit");
                yield break;
            }

            // Wait for EngineMapInitializer to complete
            if (logProgress)
                ArchonLogger.Log("Waiting for engine + map initialization...", "starter_kit");

            while (!engineMapInitializer.IsInitialized)
            {
                yield return null;
            }

            if (logProgress)
                ArchonLogger.Log("Engine + map initialization complete", "starter_kit");

            // Get GameState and TimeManager
            var gameState = GameState.Instance;
            if (gameState == null)
            {
                ArchonLogger.LogError("StarterKitInitializer: GameState not found!", "starter_kit");
                yield break;
            }

            var timeManager = FindFirstObjectByType<TimeManager>();
            var mapInitializer = FindFirstObjectByType<MapInitializer>();

            // Create player state
            if (logProgress)
                ArchonLogger.Log("Creating player state...", "starter_kit");

            playerState = new PlayerState(gameState, logProgress);

            yield return null;

            // Create economy system
            if (logProgress)
                ArchonLogger.Log("Creating economy system...", "starter_kit");

            economySystem = new EconomySystem(gameState, playerState, logProgress);

            yield return null;

            // Initialize resource bar UI
            if (logProgress)
                ArchonLogger.Log("Initializing resource bar UI...", "starter_kit");

            if (resourceBarUI != null)
                resourceBarUI.Initialize(economySystem, playerState, gameState);

            yield return null;

            // Initialize time UI
            if (logProgress)
                ArchonLogger.Log("Initializing time UI...", "starter_kit");

            if (timeUI != null && timeManager != null)
            {
                timeUI.Initialize(timeManager);
                gameState.EventBus.Subscribe<PlayerCountrySelectedEvent>(evt => timeUI.ShowUI());
            }

            yield return null;

            // Province info UI - initialize after country is selected
            if (provinceInfoUI != null && mapInitializer != null)
            {
                var selector = mapInitializer.ProvinceSelector;
                var highlighter = mapInitializer.ProvinceHighlighter;

                // Subscribe to initialize ProvinceInfoUI after country selection
                gameState.EventBus.Subscribe<PlayerCountrySelectedEvent>(evt =>
                {
                    if (logProgress)
                        ArchonLogger.Log("Initializing province info UI (post country selection)...", "starter_kit");

                    provinceInfoUI.Initialize(gameState, selector, highlighter);
                });
            }

            yield return null;

            // Initialize country selection UI
            if (logProgress)
                ArchonLogger.Log("Initializing country selection UI...", "starter_kit");

            if (countrySelectionUI != null)
                countrySelectionUI.Initialize(gameState, playerState);

            isInitialized = true;

            if (logProgress)
                ArchonLogger.Log("=== StarterKit initialization complete ===", "starter_kit");
        }
    }
}
