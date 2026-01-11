using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;
using System.Collections.Generic;
using Core;
using Core.Systems;
using Map.Core;
using Map.Interaction;
using ProvinceSystem;

namespace StarterKit
{
    /// <summary>
    /// Coordinates EngineMapInitializer + StarterKit systems.
    /// Use this as the entry point for StarterKit scenes.
    /// Owns PlayerState and EconomySystem as plain classes.
    /// </summary>
    public class Initializer : MonoBehaviour
    {
        [Header("Engine References")]
        [SerializeField] private EngineMapInitializer engineMapInitializer;

        [Header("UI Components")]
        [SerializeField] private CountrySelectionUI countrySelectionUI;
        [SerializeField] private ResourceBarUI resourceBarUI;
        [SerializeField] private TimeUI timeUI;
        [SerializeField] private ProvinceInfoUI provinceInfoUI;
        [SerializeField] private UnitInfoUI unitInfoUI;
        [SerializeField] private BuildingInfoUI buildingInfoUI;

        [Header("Visualization")]
        [SerializeField] private UnitVisualization unitVisualization;

        [Header("Configuration")]
        [SerializeField] private bool initializeOnStart = true;
        [SerializeField] private bool logProgress = true;

        // Owned systems (plain classes)
        private PlayerState playerState;
        private EconomySystem economySystem;
        private UnitSystem unitSystem;
        private BuildingSystem buildingSystem;
        private AISystem aiSystem;

        private bool isInitialized;

        public bool IsInitialized => isInitialized;
        public PlayerState PlayerState => playerState;
        public EconomySystem EconomySystem => economySystem;
        public UnitSystem UnitSystem => unitSystem;
        public BuildingSystem BuildingSystem => buildingSystem;
        public AISystem AISystem => aiSystem;

        void Start()
        {
            if (initializeOnStart)
            {
                StartCoroutine(InitializeSequence());
            }
        }

        void OnDestroy()
        {
            aiSystem?.Dispose();
            buildingSystem?.Dispose();
            unitSystem?.Dispose();
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
            if (unitInfoUI == null)
                unitInfoUI = FindFirstObjectByType<UnitInfoUI>();
            if (buildingInfoUI == null)
                buildingInfoUI = FindFirstObjectByType<BuildingInfoUI>();
            if (unitVisualization == null)
                unitVisualization = FindFirstObjectByType<UnitVisualization>();

            // Validate engine initializer
            if (engineMapInitializer == null)
            {
                ArchonLogger.LogError("Initializer: EngineMapInitializer not found!", "starter_kit");
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
                ArchonLogger.LogError("Initializer: GameState not found!", "starter_kit");
                yield break;
            }

            var timeManager = FindFirstObjectByType<TimeManager>();
            var mapInitializer = FindFirstObjectByType<MapInitializer>();

            // Scan province adjacencies (required for colonization neighbor check)
            yield return ScanProvinceAdjacencies(gameState);

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

            // Create unit system
            if (logProgress)
                ArchonLogger.Log("Creating unit system...", "starter_kit");

            unitSystem = new UnitSystem(gameState, playerState, logProgress);
            unitSystem.LoadUnitTypes("Assets/Archon-Engine/Template-Data/units");

            yield return null;

            // Create building system
            if (logProgress)
                ArchonLogger.Log("Creating building system...", "starter_kit");

            buildingSystem = new BuildingSystem(gameState, playerState, economySystem, logProgress);
            buildingSystem.LoadBuildingTypes("Assets/Archon-Engine/Template-Data/buildings");

            // Link building system to economy for bonus calculation
            economySystem.SetBuildingSystem(buildingSystem);

            yield return null;

            // Create AI system
            if (logProgress)
                ArchonLogger.Log("Creating AI system...", "starter_kit");

            aiSystem = new AISystem(gameState, playerState, buildingSystem, logProgress);

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

            // Province info UI and Unit info UI - initialize after country is selected
            if (mapInitializer != null)
            {
                var selector = mapInitializer.ProvinceSelector;
                var highlighter = mapInitializer.ProvinceHighlighter;

                // Subscribe to initialize UIs after country selection
                gameState.EventBus.Subscribe<PlayerCountrySelectedEvent>(evt =>
                {
                    if (provinceInfoUI != null)
                    {
                        if (logProgress)
                            ArchonLogger.Log("Initializing province info UI (post country selection)...", "starter_kit");

                        provinceInfoUI.Initialize(gameState, selector, highlighter, economySystem, playerState);
                    }

                    if (unitInfoUI != null && unitSystem != null)
                    {
                        if (logProgress)
                            ArchonLogger.Log("Initializing unit info UI (post country selection)...", "starter_kit");

                        unitInfoUI.Initialize(gameState, unitSystem, selector);
                    }

                    if (buildingInfoUI != null && buildingSystem != null)
                    {
                        if (logProgress)
                            ArchonLogger.Log("Initializing building info UI (post country selection)...", "starter_kit");

                        buildingInfoUI.Initialize(gameState, buildingSystem, selector);
                    }

                    if (unitVisualization != null && unitSystem != null)
                    {
                        if (logProgress)
                            ArchonLogger.Log("Initializing unit visualization (post country selection)...", "starter_kit");

                        unitVisualization.Initialize(gameState, unitSystem);
                    }
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

        /// <summary>
        /// Scan province adjacencies using FastAdjacencyScanner
        /// Populates GameState.Adjacencies with border data for colonization neighbor checks
        /// </summary>
        private IEnumerator ScanProvinceAdjacencies(GameState gameState)
        {
            if (logProgress)
                ArchonLogger.Log("Scanning province adjacencies...", "starter_kit");

            var mapSystemCoordinator = FindFirstObjectByType<MapSystemCoordinator>();
            if (mapSystemCoordinator == null || mapSystemCoordinator.ProvinceMapping == null)
            {
                ArchonLogger.LogWarning("Initializer: MapSystemCoordinator or ProvinceMapping not found - skipping adjacency scan", "starter_kit");
                yield break;
            }

            // Get the province color texture
            var provinceMapTexture = mapSystemCoordinator.TextureManager.ProvinceColorTexture;
            if (provinceMapTexture == null)
            {
                ArchonLogger.LogWarning("Initializer: ProvinceColorTexture not found - skipping adjacency scan", "starter_kit");
                yield break;
            }

            // Create FastAdjacencyScanner
            GameObject scannerObj = new GameObject("FastAdjacencyScanner_Temp");
            var scanner = scannerObj.AddComponent<FastAdjacencyScanner>();
            scanner.provinceMap = provinceMapTexture;
            scanner.ignoreDiagonals = false;
            scanner.blackThreshold = 10f;
            scanner.showDebugInfo = logProgress;

            yield return null;

            // Run scan
            var scanResult = scanner.ScanForAdjacencies();

            if (scanResult == null)
            {
                ArchonLogger.LogWarning("Initializer: Province adjacency scan failed", "starter_kit");
                Object.Destroy(scannerObj);
                yield break;
            }

            // Convert color adjacencies to ID adjacencies
            var colorToIdMap = new Dictionary<Color32, int>(new Color32Comparer());

            // Build color → ID map from ProvinceMapping
            var allProvinces = mapSystemCoordinator.ProvinceMapping.GetAllProvinces();
            foreach (var kvp in allProvinces)
            {
                ushort provinceId = kvp.Key;
                Color32 color = kvp.Value.IdentifierColor;
                colorToIdMap[color] = provinceId;
            }

            scanner.ConvertToIdAdjacencies(colorToIdMap);

            // Populate GameState.Adjacencies
            gameState.Adjacencies.SetAdjacencies(scanner.IdAdjacencies);

            if (logProgress)
                ArchonLogger.Log(gameState.Adjacencies.GetStatistics(), "starter_kit");

            // Cleanup
            Object.Destroy(scannerObj);

            yield return null;
        }
    }
}
