using UnityEngine;
using System.Collections;
using Core;
using Core.Systems;
using Map.Core;
using Map.Interaction;
using Map.MapModes;
using Map.Rendering;
using Map.Rendering.Border;
using Map.CameraControllers;
using Core.SaveLoad;
using Archon.Engine.Map;
using Map.Province;

namespace Engine
{
    /// <summary>
    /// Main entry point for Archon Engine.
    ///
    /// This facade provides a single, clean API for GAME layer code to access
    /// all ENGINE functionality. Instead of using FindFirstObjectByType for
    /// various components, access everything through ArchonEngine.Instance.
    ///
    /// Setup:
    /// 1. Add ArchonEngine prefab to your scene
    /// 2. Assign GameSettings asset
    /// 3. Assign map mesh renderer
    /// 4. Access via ArchonEngine.Instance
    ///
    /// Example:
    ///     // Wait for engine to initialize
    ///     yield return new WaitUntil(() => ArchonEngine.Instance.IsInitialized);
    ///
    ///     // Access game state
    ///     var provinces = ArchonEngine.Instance.GameState.Provinces;
    ///
    ///     // Subscribe to province clicks
    ///     ArchonEngine.Instance.ProvinceSelector.OnProvinceClicked += OnClick;
    /// </summary>
    public class ArchonEngine : MonoBehaviour
    {
        #region Singleton

        private static ArchonEngine instance;

        /// <summary>
        /// Singleton instance. Available after Awake.
        /// </summary>
        public static ArchonEngine Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<ArchonEngine>();
                }
                return instance;
            }
        }

        #endregion

        #region User Configuration (Inspector)

        [Header("Required - Configuration Asset")]
        [Tooltip("Game settings asset containing all configuration (data paths, logging, rendering options).")]
        [SerializeField] private GameSettings gameSettings;

        [Header("Required - Scene References")]
        [Tooltip("MapGenerator object with BorderComputeDispatcher, OwnerTextureDispatcher, and other map components.")]
        [SerializeField] private GameObject mapGenerator;

        [Tooltip("The mesh renderer for the map quad.")]
        [SerializeField] private MeshRenderer mapMeshRenderer;

        [Tooltip("Camera for map rendering. If not set, uses Camera.main.")]
        [SerializeField] private Camera mapCamera;

        [Header("Optional")]
        [Tooltip("Visual style configuration. If not set, uses default material.")]
        [SerializeField] private VisualStyleConfiguration visualStyle;

        [Tooltip("Start initialization automatically on Start().")]
        [SerializeField] private bool initializeOnStart = true;

        #endregion

        #region Logging Helper

        private bool LogProgress => gameSettings?.ShouldLog(LogLevel.Info) ?? false;

        #endregion

        #region Public API - Systems

        /// <summary>
        /// Central game state hub. Access provinces, countries, events, commands.
        /// </summary>
        public GameState GameState { get; private set; }

        /// <summary>
        /// Time progression control. Pause, speed, tick events.
        /// </summary>
        public TimeManager TimeManager { get; private set; }

        /// <summary>
        /// Province click and hover detection.
        /// Subscribe to OnProvinceClicked, OnProvinceHovered.
        /// </summary>
        public ProvinceSelector ProvinceSelector { get; private set; }

        /// <summary>
        /// Province visual highlighting (selection, hover effects).
        /// </summary>
        public ProvinceHighlighter ProvinceHighlighter { get; private set; }

        /// <summary>
        /// Map visualization modes (Political, Terrain, Economic, etc.).
        /// </summary>
        public MapModeManager MapModeManager { get; private set; }

        /// <summary>
        /// Save and load game state.
        /// </summary>
        public SaveManager SaveManager { get; private set; }

        /// <summary>
        /// Visual style manager for borders, colors, etc.
        /// </summary>
        public VisualStyleManager VisualStyleManager { get; private set; }

        /// <summary>
        /// Game settings configuration asset.
        /// </summary>
        public GameSettings GameSettings => gameSettings;

        /// <summary>
        /// Province color-to-ID mapping. Used for adjacency scanning and map mode initialization.
        /// </summary>
        public ProvinceMapping ProvinceMapping => mapSystemCoordinator?.ProvinceMapping;

        /// <summary>
        /// Map texture manager for province textures. Used for adjacency scanning.
        /// </summary>
        public MapTextureManager TextureManager => textureManager;

        /// <summary>
        /// Map mesh renderer material. Used for map mode initialization.
        /// </summary>
        public Material MapMaterial => mapMeshRenderer?.sharedMaterial;

        /// <summary>
        /// Map mesh renderer. Used for map plane transform.
        /// </summary>
        public MeshRenderer MapMeshRenderer => mapMeshRenderer;

        /// <summary>
        /// Border compute dispatcher for border rendering.
        /// </summary>
        public BorderComputeDispatcher BorderDispatcher => borderDispatcher;

        /// <summary>
        /// Map camera used for rendering.
        /// </summary>
        public Camera MapCamera => mapCamera;

        /// <summary>
        /// Camera controller for map navigation (pan, zoom, etc).
        /// May be null if no camera controller is in the scene.
        /// </summary>
        public BaseCameraController CameraController { get; private set; }

        #endregion

        #region Public API - State

        /// <summary>
        /// True when engine is fully initialized and ready for use.
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Current initialization progress (0-100).
        /// </summary>
        public float InitializationProgress { get; private set; }

        /// <summary>
        /// Current initialization status message.
        /// </summary>
        public string InitializationStatus { get; private set; }

        /// <summary>
        /// Map width in pixels.
        /// </summary>
        public int MapWidth => textureManager?.MapWidth ?? 0;

        /// <summary>
        /// Map height in pixels.
        /// </summary>
        public int MapHeight => textureManager?.MapHeight ?? 0;

        #endregion

        #region Public API - Events

        /// <summary>
        /// Called during initialization with progress (0-100) and status message.
        /// </summary>
        public event System.Action<float, string> OnInitializationProgress;

        /// <summary>
        /// Called when initialization completes. Bool indicates success.
        /// </summary>
        public event System.Action<bool, string> OnInitializationComplete;

        #endregion

        #region Internal Components (Hidden from GAME layer)

        // These are created and managed internally
        private EngineInitializer engineInitializer;
        private MapTextureManager textureManager;
        private MapSystemCoordinator mapSystemCoordinator;
        private BorderComputeDispatcher borderDispatcher;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            if (instance != null && instance != this)
            {
                ArchonLogger.LogError("Multiple ArchonEngine instances detected! Destroying duplicate.", "core_simulation");
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            if (initializeOnStart)
            {
                StartCoroutine(InitializeEngine());
            }
        }

        void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Manually start initialization (if initializeOnStart is false).
        /// </summary>
        public void StartInitialization()
        {
            if (!IsInitialized)
            {
                StartCoroutine(InitializeEngine());
            }
        }

        /// <summary>
        /// Refresh all map visuals from current simulation state.
        /// Call after loading a save game to update textures.
        /// </summary>
        public void RefreshAllVisuals()
        {
            mapSystemCoordinator?.RefreshAllVisuals();
        }

        #endregion

        #region Initialization

        private IEnumerator InitializeEngine()
        {
            if (LogProgress)
                ArchonLogger.Log("=== ArchonEngine: Starting initialization ===", "core_simulation");

            // Validate required configuration
            if (gameSettings == null)
            {
                ReportError("GameSettings not assigned!");
                yield break;
            }

            if (mapMeshRenderer == null)
            {
                ReportError("Map mesh renderer not assigned!");
                yield break;
            }

            // Find or default camera
            if (mapCamera == null)
            {
                mapCamera = Camera.main;
                if (mapCamera == null)
                {
                    ReportError("No camera found for map rendering!");
                    yield break;
                }
            }

            // Phase 1: Create core components (0-5%)
            ReportProgress(0f, "Creating core systems...");
            yield return CreateCoreComponents();

            // Phase 2: Initialize simulation (5-50%)
            ReportProgress(5f, "Loading simulation data...");
            yield return InitializeSimulation();

            // Phase 3: Initialize map rendering (50-80%)
            ReportProgress(50f, "Initializing map...");
            yield return InitializeMap();

            // Phase 4: Scan province adjacencies (80-90%)
            ReportProgress(80f, "Scanning adjacencies...");
            yield return ScanProvinceAdjacencies();

            // Phase 5: Initialize interaction (90-100%)
            ReportProgress(90f, "Setting up interaction...");
            yield return InitializeInteraction();

            // Complete
            IsInitialized = true;
            ReportProgress(100f, "Ready!");

            if (LogProgress)
                ArchonLogger.Log("=== ArchonEngine: Initialization complete ===", "core_simulation");

            OnInitializationComplete?.Invoke(true, "Engine initialized successfully");
        }

        private IEnumerator CreateCoreComponents()
        {
            // GameState (creates EventBus, ProvinceSystem, etc.)
            GameState = GetComponentInChildren<GameState>();
            if (GameState == null)
            {
                var go = new GameObject("GameState");
                go.transform.SetParent(transform);
                GameState = go.AddComponent<GameState>();
            }

            // TimeManager
            TimeManager = GetComponentInChildren<TimeManager>();
            if (TimeManager == null)
            {
                TimeManager = GameState.gameObject.AddComponent<TimeManager>();
            }

            // EngineInitializer (handles data loading)
            engineInitializer = GetComponentInChildren<EngineInitializer>();
            if (engineInitializer == null)
            {
                var go = new GameObject("EngineInitializer");
                go.transform.SetParent(transform);
                engineInitializer = go.AddComponent<EngineInitializer>();
            }
            // Disable auto-start - we'll start it manually after configuring
            SetPrivateField(engineInitializer, "initializeOnStart", false);
            SetPrivateField(engineInitializer, "gameSettings", gameSettings);

            // SaveManager
            SaveManager = GetComponentInChildren<SaveManager>();
            if (SaveManager == null)
            {
                var go = new GameObject("SaveManager");
                go.transform.SetParent(transform);
                SaveManager = go.AddComponent<SaveManager>();
            }

            // VisualStyleManager
            VisualStyleManager = GetComponentInChildren<VisualStyleManager>();
            if (VisualStyleManager == null)
            {
                var go = new GameObject("VisualStyleManager");
                go.transform.SetParent(transform);
                VisualStyleManager = go.AddComponent<VisualStyleManager>();
            }

            yield return null;
        }

        private IEnumerator InitializeSimulation()
        {
            // Subscribe to progress
            engineInitializer.OnLoadingProgress += (phase, progress, status) =>
            {
                // Map engine progress (0-100) to our range (5-50)
                float mappedProgress = 5f + (progress * 0.45f);
                ReportProgress(mappedProgress, status);
            };

            // Start and wait
            engineInitializer.StartInitialization();

            while (!engineInitializer.IsComplete)
            {
                if (engineInitializer.CurrentPhase == EngineInitializer.LoadingPhase.Error)
                {
                    ReportError("Simulation initialization failed");
                    yield break;
                }
                yield return null;
            }

            if (LogProgress)
                ArchonLogger.Log("ArchonEngine: Simulation initialized", "core_simulation");
        }

        private IEnumerator InitializeMap()
        {
            // Use assigned MapGenerator (has components with shader references)
            if (mapGenerator == null)
            {
                ReportError("MapGenerator not assigned! Assign the MapGenerator object with BorderComputeDispatcher and other map components.");
                yield break;
            }

            // ============================================================
            // PHASE 1: Get and initialize all map components
            // ArchonEngine is the SINGLE OWNER of component initialization
            // ============================================================

            // BorderComputeDispatcher (required, has compute shader)
            borderDispatcher = mapGenerator.GetComponent<BorderComputeDispatcher>();
            if (borderDispatcher == null)
            {
                ReportError("BorderComputeDispatcher not found on MapGenerator!");
                yield break;
            }
            borderDispatcher.Initialize();

            // OwnerTextureDispatcher (required, has compute shader)
            var ownerTextureDispatcher = mapGenerator.GetComponent<OwnerTextureDispatcher>();
            if (ownerTextureDispatcher == null)
            {
                ReportError("OwnerTextureDispatcher not found on MapGenerator!");
                yield break;
            }
            ownerTextureDispatcher.Initialize();

            // MapTextureManager (required)
            textureManager = mapGenerator.GetComponent<MapTextureManager>();
            if (textureManager == null)
                textureManager = mapGenerator.AddComponent<MapTextureManager>();
            textureManager.Initialize();

            // Wire up texture manager dependencies
            borderDispatcher.SetTextureManager(textureManager);
            ownerTextureDispatcher.SetTextureManager(textureManager);

            // BorderDistanceFieldGenerator (optional, has compute shader)
            var distanceFieldGenerator = mapGenerator.GetComponent<BorderDistanceFieldGenerator>();
            if (distanceFieldGenerator != null)
            {
                distanceFieldGenerator.Initialize();
                distanceFieldGenerator.SetTextureManager(textureManager);
            }

            // ProvinceTerrainAnalyzer (optional, has compute shader)
            var terrainAnalyzer = mapGenerator.GetComponent<ProvinceTerrainAnalyzer>();
            if (terrainAnalyzer != null)
                terrainAnalyzer.Initialize(gameSettings.DataDirectory);

            // TerrainBlendMapGenerator (optional, has compute shader)
            var blendMapGenerator = mapGenerator.GetComponent<Map.Rendering.Terrain.TerrainBlendMapGenerator>();
            if (blendMapGenerator != null)
                blendMapGenerator.InitializeKernels();

            // MapModeManager (optional)
            MapModeManager = mapGenerator.GetComponent<MapModeManager>();
            if (MapModeManager == null)
                MapModeManager = mapGenerator.AddComponent<MapModeManager>();

            // MapSystemCoordinator (required for map loading)
            mapSystemCoordinator = mapGenerator.GetComponent<MapSystemCoordinator>();
            if (mapSystemCoordinator == null)
                mapSystemCoordinator = mapGenerator.AddComponent<MapSystemCoordinator>();

            // ============================================================
            // PHASE 2: Configure MapSystemCoordinator with initialized components
            // MapSystemCoordinator does NOT initialize - it only coordinates
            // ============================================================

            mapSystemCoordinator.Configure(
                mapCamera,
                mapMeshRenderer,
                gameSettings,
                textureManager,
                borderDispatcher,
                ownerTextureDispatcher,
                terrainAnalyzer,
                blendMapGenerator
            );

            // Subscribe to progress
            mapSystemCoordinator.OnProgress += (progress, status) =>
            {
                // Map progress (0-100) to our range (50-90)
                float mappedProgress = 50f + (progress * 0.4f);
                ReportProgress(mappedProgress, status);
            };

            // ============================================================
            // PHASE 3: Configure visual style
            // ============================================================

            if (VisualStyleManager != null)
            {
                VisualStyleManager.Configure(mapMeshRenderer, visualStyle);
                VisualStyleManager.SetComponents(textureManager, borderDispatcher);

                if (visualStyle != null)
                {
                    VisualStyleManager.ApplyStyle(visualStyle);
                }
            }

            // ============================================================
            // PHASE 4: Start map loading (async)
            // ============================================================

            var simulationData = new SimulationDataReadyEvent
            {
                ProvinceCount = GameState.Provinces.ProvinceCount,
                CountryCount = GameState.Countries.CountryCount
            };
            mapSystemCoordinator.Initialize(simulationData);

            // Wait for completion
            while (!mapSystemCoordinator.IsInitialized)
            {
                yield return null;
            }

            if (LogProgress)
                ArchonLogger.Log("ArchonEngine: Map initialized", "map_initialization");
        }

        private IEnumerator ScanProvinceAdjacencies()
        {
            if (LogProgress)
                ArchonLogger.Log("ArchonEngine: Scanning province adjacencies...", "map_initialization");

            if (ProvinceMapping == null)
            {
                ArchonLogger.LogWarning("ArchonEngine: ProvinceMapping not found - skipping adjacency scan", "map_initialization");
                yield break;
            }

            // Get the province color texture
            var provinceMapTexture = textureManager?.ProvinceColorTexture;
            if (provinceMapTexture == null)
            {
                ArchonLogger.LogWarning("ArchonEngine: ProvinceColorTexture not found - skipping adjacency scan", "map_initialization");
                yield break;
            }

            // Create FastAdjacencyScanner
            GameObject scannerObj = new GameObject("FastAdjacencyScanner_Temp");
            var scanner = scannerObj.AddComponent<FastAdjacencyScanner>();
            scanner.provinceMap = provinceMapTexture;
            scanner.ignoreDiagonals = false;
            scanner.blackThreshold = 10f;
            scanner.showDebugInfo = LogProgress;

            yield return null;

            // Run scan
            var scanResult = scanner.ScanForAdjacencies();

            if (scanResult == null)
            {
                ArchonLogger.LogWarning("ArchonEngine: Province adjacency scan failed", "map_initialization");
                Object.Destroy(scannerObj);
                yield break;
            }

            // Convert color adjacencies to ID adjacencies
            var colorToIdMap = new System.Collections.Generic.Dictionary<Color32, int>(new Color32Comparer());

            // Build color → ID map from ProvinceMapping
            var allProvinces = ProvinceMapping.GetAllProvinces();
            foreach (var kvp in allProvinces)
            {
                ushort provinceId = kvp.Key;
                Color32 color = kvp.Value.IdentifierColor;
                colorToIdMap[color] = provinceId;
            }

            scanner.ConvertToIdAdjacencies(colorToIdMap);

            // Populate GameState.Adjacencies
            GameState.Adjacencies.SetAdjacencies(scanner.IdAdjacencies);

            if (LogProgress)
                ArchonLogger.Log(GameState.Adjacencies.GetStatistics(), "map_initialization");

            // Cleanup
            Object.Destroy(scannerObj);

            yield return null;
        }

        private IEnumerator InitializeInteraction()
        {
            // ProvinceSelector
            ProvinceSelector = GetComponentInChildren<ProvinceSelector>();
            if (ProvinceSelector == null)
            {
                ProvinceSelector = mapSystemCoordinator.gameObject.AddComponent<ProvinceSelector>();
            }
            ProvinceSelector.Initialize(textureManager, mapMeshRenderer.transform);

            // ProvinceHighlighter
            ProvinceHighlighter = GetComponentInChildren<ProvinceHighlighter>();
            if (ProvinceHighlighter == null)
            {
                ProvinceHighlighter = mapSystemCoordinator.gameObject.AddComponent<ProvinceHighlighter>();
            }
            ProvinceHighlighter.Initialize(textureManager);

            // Initialize MapModeManager
            if (mapSystemCoordinator != null)
            {
                var material = mapMeshRenderer.sharedMaterial;
                MapModeManager.Initialize(GameState, material, mapSystemCoordinator.ProvinceMapping, null);

                // Register default political map mode
                MapModeManager.RegisterHandler(MapMode.Political, new EnginePoliticalMapMode());
                MapModeManager.SetMapMode(MapMode.Political, forceUpdate: true);
            }

            // Initialize border system
            if (borderDispatcher == null)
            {
                ArchonLogger.LogWarning("ArchonEngine: borderDispatcher is null, cannot initialize borders", "map_initialization");
            }
            else if (!GameState.Adjacencies.IsInitialized)
            {
                ArchonLogger.LogWarning("ArchonEngine: Adjacencies not initialized yet, cannot initialize borders", "map_initialization");
            }
            else
            {
                ArchonLogger.Log("ArchonEngine: Initializing smooth borders...", "map_initialization");
                borderDispatcher.InitializeSmoothBorders(
                    GameState.Adjacencies,
                    GameState.Provinces,
                    GameState.Countries,
                    mapSystemCoordinator?.ProvinceMapping,
                    null
                );

                // Apply border style
                if (visualStyle != null && VisualStyleManager != null)
                {
                    VisualStyleManager.ApplyBorderConfiguration(visualStyle);
                }
            }

            // Initialize camera controller if present
            InitializeCameraController();

            yield return null;

            if (LogProgress)
                ArchonLogger.Log("ArchonEngine: Interaction ready", "map_interaction");
        }

        /// <summary>
        /// Initialize camera controller if present in the scene.
        /// </summary>
        private void InitializeCameraController()
        {
            CameraController = FindFirstObjectByType<BaseCameraController>();
            if (CameraController != null)
            {
                CameraController.Initialize();
                if (LogProgress)
                    ArchonLogger.Log($"ArchonEngine: Camera controller initialized ({CameraController.GetType().Name})", "map_initialization");
            }
            else
            {
                if (LogProgress)
                    ArchonLogger.Log("ArchonEngine: No camera controller found (optional)", "map_initialization");
            }
        }

        #endregion

        #region Helpers

        private void ReportProgress(float progress, string status)
        {
            InitializationProgress = progress;
            InitializationStatus = status;

            if (LogProgress)
                ArchonLogger.Log($"ArchonEngine: [{progress:F0}%] {status}", "core_simulation");

            OnInitializationProgress?.Invoke(progress, status);
        }

        private void ReportError(string error)
        {
            ArchonLogger.LogError($"ArchonEngine: {error}", "core_simulation");
            InitializationStatus = $"Error: {error}";
            OnInitializationComplete?.Invoke(false, error);
        }

        private void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(target, value);
        }

        #endregion
    }
}
