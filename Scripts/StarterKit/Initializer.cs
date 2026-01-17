using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Core;
using Core.Modifiers;
using Core.SaveLoad;
using Core.Systems;
using Map.Core;
using Map.Interaction;
using Map.MapModes;
using ProvinceSystem;
using StarterKit.MapModes;

namespace StarterKit
{
    /// <summary>
    /// Coordinates EngineMapInitializer + StarterKit systems.
    /// Use this as the entry point for StarterKit scenes.
    /// Owns PlayerState and EconomySystem as plain classes.
    ///
    /// Access via Initializer.Instance for commands and other systems.
    /// </summary>
    public class Initializer : MonoBehaviour
    {
        #region Static Instance

        /// <summary>
        /// Static instance for easy access from commands.
        /// Set on Awake, cleared on OnDestroy.
        /// </summary>
        public static Initializer Instance { get; private set; }

        #endregion

        #region Serialized Fields

        [Header("Engine References")]
        [SerializeField] private EngineMapInitializer engineMapInitializer;

        [Header("UI Components")]
        [SerializeField] private CountrySelectionUI countrySelectionUI;
        [SerializeField] private ResourceBarUI resourceBarUI;
        [SerializeField] private TimeUI timeUI;
        [SerializeField] private ProvinceInfoUI provinceInfoUI;
        [SerializeField] private UnitInfoUI unitInfoUI;
        [SerializeField] private BuildingInfoUI buildingInfoUI;
        [SerializeField] private LedgerUI ledgerUI;
        [SerializeField] private ToolbarUI toolbarUI;

        [Header("Visualization")]
        [SerializeField] private UnitVisualization unitVisualization;

        [Header("Configuration")]
        [SerializeField] private bool initializeOnStart = true;
        [SerializeField] private bool logProgress = true;

        #endregion

        #region Private Fields & Public Properties

        // Owned systems (plain classes)
        private PlayerState playerState;
        private ModifierSystem modifierSystem;
        private EconomySystem economySystem;
        private UnitSystem unitSystem;
        private BuildingSystem buildingSystem;
        private AISystem aiSystem;
        private ProvinceHistorySystem provinceHistorySystem;

        // Map modes (GAME layer extends ENGINE map modes)
        private FarmDensityMapMode farmDensityMapMode;
        private TerrainCostMapMode terrainCostMapMode;
        private MapModeManager mapModeManager;

        private bool isInitialized;

        public bool IsInitialized => isInitialized;
        public PlayerState PlayerState => playerState;
        public EconomySystem EconomySystem => economySystem;
        public UnitSystem UnitSystem => unitSystem;
        public BuildingSystem BuildingSystem => buildingSystem;
        public AISystem AISystem => aiSystem;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            Instance = this;
        }

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
            modifierSystem?.Dispose();
            provinceHistorySystem?.Dispose();

            if (Instance == this)
                Instance = null;
        }

        public void StartInitialization()
        {
            if (!isInitialized)
            {
                StartCoroutine(InitializeSequence());
            }
        }

        #endregion

        #region Initialization Sequence

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
            if (ledgerUI == null)
                ledgerUI = FindFirstObjectByType<LedgerUI>();
            if (toolbarUI == null)
                toolbarUI = FindFirstObjectByType<ToolbarUI>();
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

            // Create modifier system (for building bonuses)
            if (logProgress)
                ArchonLogger.Log("Creating modifier system...", "starter_kit");

            modifierSystem = new ModifierSystem();

            yield return null;

            // Create economy system
            if (logProgress)
                ArchonLogger.Log("Creating economy system...", "starter_kit");

            economySystem = new EconomySystem(gameState, playerState, modifierSystem, logProgress);

            yield return null;

            // Create unit system
            if (logProgress)
                ArchonLogger.Log("Creating unit system...", "starter_kit");

            unitSystem = new UnitSystem(gameState, playerState, logProgress);
            var unitsPath = System.IO.Path.Combine(GameSettings.Instance.TemplateDataDirectory, "units");
            unitSystem.LoadUnitTypes(unitsPath);

            yield return null;

            // Create building system
            if (logProgress)
                ArchonLogger.Log("Creating building system...", "starter_kit");

            buildingSystem = new BuildingSystem(gameState, playerState, economySystem, modifierSystem, logProgress);
            var buildingsPath = System.IO.Path.Combine(GameSettings.Instance.TemplateDataDirectory, "common", "buildings");
            buildingSystem.LoadBuildingTypes(buildingsPath);

            yield return null;

            // Create AI system
            if (logProgress)
                ArchonLogger.Log("Creating AI system...", "starter_kit");

            aiSystem = new AISystem(gameState, playerState, buildingSystem, economySystem, logProgress);

            yield return null;

            // Create province history system (Pattern 4: Hot/Cold Data Separation demo)
            // This tracks ownership changes as COLD DATA - only accessed when viewing province details
            if (logProgress)
                ArchonLogger.Log("Creating province history system (cold data storage)...", "starter_kit");

            provinceHistorySystem = new ProvinceHistorySystem(gameState, logProgress);

            yield return null;

            // Register custom map modes (GAME layer extends ENGINE map modes)
            yield return RegisterMapModes(mapInitializer, gameState);

            // Hook up SaveManager for StarterKit systems
            SetupSaveManager(gameState);

            yield return null;

            // Initialize toolbar UI
            if (logProgress)
                ArchonLogger.Log("Initializing toolbar UI...", "starter_kit");

            if (toolbarUI != null)
            {
                var saveManager = FindFirstObjectByType<SaveManager>();
                toolbarUI.Initialize(gameState, ledgerUI, saveManager);
            }

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
                timeUI.Initialize(gameState, timeManager);
                gameState.EventBus.Subscribe<PlayerCountrySelectedEvent>(evt => timeUI.Show());
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

                        provinceInfoUI.Initialize(gameState, selector, highlighter, economySystem, playerState, provinceHistorySystem);
                    }

                    if (unitInfoUI != null && unitSystem != null)
                    {
                        if (logProgress)
                            ArchonLogger.Log("Initializing unit info UI (post country selection)...", "starter_kit");

                        unitInfoUI.Initialize(gameState, unitSystem, selector, economySystem);
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

                    if (ledgerUI != null)
                    {
                        if (logProgress)
                            ArchonLogger.Log("Initializing ledger UI (post country selection)...", "starter_kit");

                        ledgerUI.Initialize(gameState, economySystem, unitSystem, playerState);
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

        #endregion

        #region Save/Load Setup

        /// <summary>
        /// Hook up SaveManager callbacks for StarterKit systems
        /// </summary>
        private void SetupSaveManager(GameState gameState)
        {
            var saveManager = FindFirstObjectByType<SaveManager>();
            if (saveManager == null)
            {
                if (logProgress)
                    ArchonLogger.LogWarning("Initializer: SaveManager not found - save/load disabled", "starter_kit");
                return;
            }

            if (logProgress)
                ArchonLogger.Log("Setting up SaveManager hooks...", "starter_kit");

            // Hook up PlayerState serialization
            saveManager.OnSerializePlayerState = () =>
            {
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    // PlayerState
                    byte[] playerData = playerState?.Serialize();
                    writer.Write(playerData?.Length ?? 0);
                    if (playerData != null) writer.Write(playerData);

                    // EconomySystem
                    byte[] economyData = economySystem?.Serialize();
                    writer.Write(economyData?.Length ?? 0);
                    if (economyData != null) writer.Write(economyData);

                    // BuildingSystem
                    byte[] buildingData = buildingSystem?.Serialize();
                    writer.Write(buildingData?.Length ?? 0);
                    if (buildingData != null) writer.Write(buildingData);

                    return ms.ToArray();
                }
            };

            saveManager.OnDeserializePlayerState = (data) =>
            {
                if (data == null || data.Length == 0) return;

                using (var ms = new MemoryStream(data))
                using (var reader = new BinaryReader(ms))
                {
                    // PlayerState
                    int playerLen = reader.ReadInt32();
                    if (playerLen > 0)
                    {
                        byte[] playerData = reader.ReadBytes(playerLen);
                        playerState?.Deserialize(playerData);
                    }

                    // EconomySystem
                    int economyLen = reader.ReadInt32();
                    if (economyLen > 0)
                    {
                        byte[] economyData = reader.ReadBytes(economyLen);
                        economySystem?.Deserialize(economyData);
                    }

                    // BuildingSystem
                    int buildingLen = reader.ReadInt32();
                    if (buildingLen > 0)
                    {
                        byte[] buildingData = reader.ReadBytes(buildingLen);
                        buildingSystem?.Deserialize(buildingData);
                    }
                }
            };

            // Hook up post-load finalization (refresh UI, etc.)
            saveManager.OnPostLoadFinalize = () =>
            {
                if (logProgress)
                    ArchonLogger.Log("Initializer: Post-load finalization...", "starter_kit");

                // CRITICAL: Refresh all map visuals from loaded state
                var mapCoordinator = FindFirstObjectByType<MapSystemCoordinator>();
                mapCoordinator?.RefreshAllVisuals();

                // Refresh resource bar UI
                resourceBarUI?.RefreshDisplay();

                // Refresh ledger if visible
                if (ledgerUI != null && ledgerUI.IsVisible)
                    ledgerUI.Show(); // This refreshes the data via OnShow()

                if (logProgress)
                    ArchonLogger.Log("Initializer: Post-load finalization complete", "starter_kit");
            };

            if (logProgress)
                ArchonLogger.Log("SaveManager hooks configured (F6=Save, F7=Load)", "starter_kit");
        }

        #endregion

        #region Map Mode Registration

        /// <summary>
        /// Register custom map modes with the ENGINE's MapModeManager.
        /// Demonstrates: GAME layer (StarterKit) extends ENGINE (Map.MapModes) with custom visualization.
        /// </summary>
        private IEnumerator RegisterMapModes(MapInitializer mapInitializerRef, GameState gameState)
        {
            if (mapInitializerRef == null)
            {
                if (logProgress)
                    ArchonLogger.LogWarning("Initializer: MapInitializer not available - skipping map mode registration", "starter_kit");
                yield break;
            }

            mapModeManager = mapInitializerRef.MapModeManager;
            if (mapModeManager == null)
            {
                if (logProgress)
                    ArchonLogger.LogWarning("Initializer: MapModeManager not available - skipping map mode registration", "starter_kit");
                yield break;
            }

            // Check if MapModeManager is initialized
            if (!mapModeManager.IsInitialized)
            {
                // Initialize MapModeManager if not already done
                // ENGINE provides mechanism, GAME controls when to initialize
                var mapSystemCoordinator = FindFirstObjectByType<MapSystemCoordinator>();
                if (mapSystemCoordinator != null && mapInitializerRef.MeshRenderer != null)
                {
                    var material = mapInitializerRef.MeshRenderer.sharedMaterial;
                    mapModeManager.Initialize(gameState, material, mapSystemCoordinator.ProvinceMapping, buildingSystem);

                    if (logProgress)
                        ArchonLogger.Log("Initializer: MapModeManager initialized", "starter_kit");
                }
                else
                {
                    ArchonLogger.LogWarning("Initializer: Cannot initialize MapModeManager - missing dependencies", "starter_kit");
                    yield break;
                }
            }

            yield return null;

            // Create and register Farm Density map mode
            if (buildingSystem != null && mapModeManager != null)
            {
                farmDensityMapMode = new FarmDensityMapMode(buildingSystem, mapModeManager);
                mapModeManager.RegisterHandler(MapMode.Economic, farmDensityMapMode);

                // Subscribe to building construction to mark map mode dirty (via EventBus)
                gameState.EventBus.Subscribe<BuildingConstructedEvent>(evt =>
                {
                    farmDensityMapMode?.MarkDirty();
                });

                // Subscribe to province ownership changes to update map mode
                gameState.EventBus.Subscribe<Core.Systems.ProvinceOwnershipChangedEvent>(evt =>
                {
                    farmDensityMapMode?.MarkDirty();
                });

                if (logProgress)
                    ArchonLogger.Log("Initializer: FarmDensityMapMode registered", "starter_kit");
            }

            // Create and register Terrain Movement Cost map mode
            // Demonstrates: ENGINE terrain costs visualized by GAME layer
            if (gameState.Registries?.Terrains != null && mapModeManager != null)
            {
                terrainCostMapMode = new TerrainCostMapMode(gameState, mapModeManager);
                mapModeManager.RegisterHandler(MapMode.Terrain, terrainCostMapMode);

                if (logProgress)
                    ArchonLogger.Log("Initializer: TerrainCostMapMode registered", "starter_kit");
            }

            yield return null;
        }

        /// <summary>
        /// Set the current map mode. Called by ToolbarUI.
        /// </summary>
        public void SetMapMode(MapMode mode)
        {
            mapModeManager?.SetMapMode(mode);
        }

        /// <summary>
        /// Get the current map mode.
        /// </summary>
        public MapMode GetCurrentMapMode()
        {
            return mapModeManager?.CurrentMode ?? MapMode.Political;
        }

        #endregion

        #region Province Adjacency Scanning

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

            // Initialize pathfinding system now that adjacencies are ready
            if (gameState.Pathfinding != null && gameState.Adjacencies.IsInitialized)
            {
                gameState.Pathfinding.Initialize(gameState.Adjacencies);

                // Set up terrain-based movement costs (Pattern 1: Engine-Game Separation)
                // ENGINE: TerrainMovementCostCalculator provides terrain cost lookups
                // GAME: LandUnitCostCalculator adds policy (land units can't cross water)
                if (gameState.Registries?.Terrains != null && gameState.Registries.Terrains.Count > 0)
                {
                    var terrainCalculator = new TerrainMovementCostCalculator(
                        gameState.Provinces,
                        gameState.Registries.Terrains);
                    var landUnitCalculator = new LandUnitCostCalculator(terrainCalculator);
                    gameState.Pathfinding.SetDefaultCostCalculator(landUnitCalculator);

                    if (logProgress)
                        ArchonLogger.Log("Initializer: Terrain-based pathfinding enabled", "starter_kit");
                }

                if (logProgress)
                    ArchonLogger.Log("Initializer: PathfindingSystem initialized", "starter_kit");
            }

            // Cleanup
            Object.Destroy(scannerObj);

            yield return null;
        }

        #endregion
    }
}
