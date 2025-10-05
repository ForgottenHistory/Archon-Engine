using UnityEngine;
using Map.Rendering;
using Map.MapModes;
using Map.Loading;
using Map.Interaction;
using Map.Debug;
using System.Threading.Tasks;
using Core;
using Utils;

namespace Map.Core
{
    /// <summary>
    /// Handles initialization of all map system components
    /// Extracted from MapGenerator to follow single responsibility principle
    /// Manages component creation, dependency injection, and initialization order
    /// </summary>
    public class MapInitializer : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool logInitializationProgress = true;

        [Header("Component References")]
        [SerializeField] private Camera mapCamera;
        [SerializeField] private MeshRenderer meshRenderer;

        [Header("Game Settings")]
        [SerializeField] private GameSettings gameSettings;

        // Initialized components (accessible via properties)
        private MapTextureManager textureManager;
        private BorderComputeDispatcher borderDispatcher;
        private OwnerTextureDispatcher ownerTextureDispatcher;  // GPU owner texture population
        private MapModeManager mapModeManager;
        private ProvinceMapProcessor provinceProcessor;
        private MapDataLoader dataLoader;
        private MapRenderingCoordinator renderingCoordinator;
        private ProvinceSelector provinceSelector;
        private MapTexturePopulator texturePopulator;
        private TextureUpdateBridge textureUpdateBridge;
        private ParadoxStyleCameraController cameraController;
        private MapModeDebugUI debugUI;

        // Public accessors for initialized components
        public MapTextureManager TextureManager => textureManager;
        public BorderComputeDispatcher BorderDispatcher => borderDispatcher;
        public OwnerTextureDispatcher OwnerTextureDispatcher => ownerTextureDispatcher;  // GPU owner texture dispatcher
        public MapModeManager MapModeManager => mapModeManager;
        public ProvinceMapProcessor ProvinceProcessor => provinceProcessor;
        public MapDataLoader DataLoader => dataLoader;
        public MapRenderingCoordinator RenderingCoordinator => renderingCoordinator;
        public ProvinceSelector ProvinceSelector => provinceSelector;
        public MapTexturePopulator TexturePopulator => texturePopulator;
        public TextureUpdateBridge TextureUpdateBridge => textureUpdateBridge;
        public Camera MapCamera => mapCamera;
        public MeshRenderer MeshRenderer => meshRenderer;
        public ParadoxStyleCameraController CameraController => cameraController;
        public MapModeDebugUI DebugUI => debugUI;

        // Initialization state
        private bool isInitialized = false;
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Set initialization state (called by MapSystemCoordinator when map generation completes)
        /// </summary>
        public void SetInitialized(bool success)
        {
            isInitialized = success;
            if (logInitializationProgress)
            {
                DominionLogger.LogMapInit($"MapInitializer: Initialization {(success ? "completed successfully" : "failed")}");
            }
        }

        /// <summary>
        /// Subscribe to simulation events on startup
        /// </summary>
        void Start()
        {
            // Subscribe to simulation ready event
            if (!TrySubscribeToEvents())
            {
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: GameState not ready yet, will retry subscription...");
                }
                StartCoroutine(WaitForGameStateAndSubscribe());
            }
        }

        /// <summary>
        /// Try to subscribe to simulation events
        /// </summary>
        private bool TrySubscribeToEvents()
        {
            var gameState = FindFirstObjectByType<GameState>();
            if (gameState?.EventBus != null)
            {
                gameState.EventBus.Subscribe<SimulationDataReadyEvent>(OnSimulationDataReady);
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Subscribed to SimulationDataReadyEvent");
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Wait for GameState to be ready and subscribe to events
        /// </summary>
        private System.Collections.IEnumerator WaitForGameStateAndSubscribe()
        {
            while (!TrySubscribeToEvents())
            {
                yield return new WaitForSeconds(0.1f);
            }
        }

        /// <summary>
        /// Handle simulation data ready - store the event data, wait for GAME layer to trigger initialization
        /// </summary>
        private void OnSimulationDataReady(SimulationDataReadyEvent simulationData)
        {
            if (logInitializationProgress)
            {
                DominionLogger.LogMapInit($"MapInitializer: Received simulation data with {simulationData.ProvinceCount} provinces - waiting for GAME layer to trigger initialization");
            }

            // Store simulation data for later initialization
            cachedSimulationData = simulationData;
            hasSimulationData = true;
        }

        private SimulationDataReadyEvent cachedSimulationData;
        private bool hasSimulationData = false;

        /// <summary>
        /// Manually trigger map initialization - called by GAME layer (HegemonInitializer) after visual style is applied
        /// </summary>
        public void StartMapInitialization()
        {
            if (!hasSimulationData)
            {
                DominionLogger.LogError("MapInitializer: StartMapInitialization called but no simulation data cached!");
                return;
            }

            if (logInitializationProgress)
            {
                DominionLogger.LogMapInit($"MapInitializer: Starting map initialization with {cachedSimulationData.ProvinceCount} provinces");
            }

            // ONLY initialize components
            InitializeAllComponents();

            // Get or create the coordinator
            var coordinator = GetComponent<MapSystemCoordinator>();
            if (coordinator == null)
            {
                coordinator = gameObject.AddComponent<MapSystemCoordinator>();
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Created MapSystemCoordinator");
                }
            }

            // Debug: Verify our references before passing to coordinator
            if (logInitializationProgress)
            {
                DominionLogger.LogMapInit($"MapInitializer: Camera reference: {(mapCamera != null ? mapCamera.name : "null")}");
                DominionLogger.LogMapInit($"MapInitializer: MeshRenderer reference: {(meshRenderer != null ? meshRenderer.name : "null")}");
            }

            // Tell the coordinator to handle map generation using GameSettings
            if (gameSettings != null)
            {
                var provinceBitmapPath = System.IO.Path.Combine(gameSettings.DataDirectory, "map", "provinces.bmp");
                var provinceDefinitionsPath = System.IO.Path.Combine(gameSettings.DataDirectory, "map", "definition.csv");
                bool useDefinition = System.IO.File.Exists(provinceDefinitionsPath);
                coordinator.HandleSimulationReady(cachedSimulationData, provinceBitmapPath, provinceDefinitionsPath, useDefinition);
            }
            else
            {
                DominionLogger.LogError("MapInitializer: GameSettings not assigned - cannot proceed with map generation");
            }
        }

        /// <summary>
        /// Initialize all map system components in the correct order
        /// </summary>
        public void InitializeAllComponents()
        {
            if (logInitializationProgress)
            {
                DominionLogger.LogMapInit("MapInitializer: Starting map system component initialization...");
            }

            // Phase 1: Core texture and computation components
            InitializeTextureManager();
            InitializeBorderDispatcher();
            InitializeOwnerTextureDispatcher();  // GPU owner texture population
            InitializeMapModeManager();

            // Phase 2: Processing components
            InitializeProvinceProcessor();

            // Phase 3: High-level components
            InitializeDataLoader();
            InitializeRenderingCoordinator();
            InitializeProvinceSelector();
            InitializeTexturePopulator();
            InitializeTextureUpdateBridge();

            // Phase 4: Camera setup
            InitializeCamera();
            InitializeCameraController();

            // Phase 5: Debug components (only in development builds)
            InitializeDebugUI();

            if (logInitializationProgress)
            {
                DominionLogger.LogMapInit("MapInitializer: All map system components initialized successfully");
            }
        }

        private void InitializeTextureManager()
        {
            textureManager = GetComponent<MapTextureManager>();
            if (textureManager == null)
            {
                textureManager = gameObject.AddComponent<MapTextureManager>();
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Created MapTextureManager component");
                }
            }
        }

        private void InitializeBorderDispatcher()
        {
            borderDispatcher = GetComponent<BorderComputeDispatcher>();
            if (borderDispatcher == null)
            {
                borderDispatcher = gameObject.AddComponent<BorderComputeDispatcher>();
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Created BorderComputeDispatcher component");
                }
            }

            // Set texture manager reference
            if (borderDispatcher != null && textureManager != null)
            {
                borderDispatcher.SetTextureManager(textureManager);
            }
        }

        private void InitializeOwnerTextureDispatcher()
        {
            ownerTextureDispatcher = GetComponent<OwnerTextureDispatcher>();
            if (ownerTextureDispatcher == null)
            {
                ownerTextureDispatcher = gameObject.AddComponent<OwnerTextureDispatcher>();
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Created OwnerTextureDispatcher component");
                }
            }

            // Set texture manager reference
            if (ownerTextureDispatcher != null && textureManager != null)
            {
                ownerTextureDispatcher.SetTextureManager(textureManager);
            }
        }

        private void InitializeMapModeManager()
        {
            mapModeManager = GetComponent<MapModeManager>();
            if (mapModeManager == null)
            {
                mapModeManager = gameObject.AddComponent<MapModeManager>();
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Created MapModeManager component");
                }
            }
        }

        private void InitializeProvinceProcessor()
        {
            provinceProcessor = new ProvinceMapProcessor();
            // Note: Progress callback should be set by the calling component

            if (logInitializationProgress)
            {
                DominionLogger.LogMapInit("MapInitializer: Created ProvinceMapProcessor for high-performance province map processing");
            }
        }

        private void InitializeDataLoader()
        {
            dataLoader = GetComponent<MapDataLoader>();
            if (dataLoader == null)
            {
                dataLoader = gameObject.AddComponent<MapDataLoader>();
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Created MapDataLoader component");
                }
            }

            // Initialize MapDataLoader with dependencies
            if (provinceProcessor != null && borderDispatcher != null && textureManager != null)
            {
                dataLoader.Initialize(provinceProcessor, borderDispatcher, textureManager);
            }
        }

        private void InitializeRenderingCoordinator()
        {
            renderingCoordinator = GetComponent<MapRenderingCoordinator>();
            if (renderingCoordinator == null)
            {
                renderingCoordinator = gameObject.AddComponent<MapRenderingCoordinator>();
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Created MapRenderingCoordinator component");
                }
            }

            // Initialize MapRenderingCoordinator with dependencies
            if (textureManager != null && mapModeManager != null && meshRenderer != null && mapCamera != null)
            {
                renderingCoordinator.Initialize(textureManager, mapModeManager, meshRenderer, mapCamera);
            }
        }

        private void InitializeProvinceSelector()
        {
            provinceSelector = GetComponent<ProvinceSelector>();
            if (provinceSelector == null)
            {
                provinceSelector = gameObject.AddComponent<ProvinceSelector>();
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Created ProvinceSelector component");
                }
            }
        }

        private void InitializeTexturePopulator()
        {
            texturePopulator = GetComponent<MapTexturePopulator>();
            if (texturePopulator == null)
            {
                texturePopulator = gameObject.AddComponent<MapTexturePopulator>();
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Created MapTexturePopulator component");
                }
            }
        }

        private void InitializeCamera()
        {
            // Find or create camera
            if (mapCamera == null)
            {
                mapCamera = Camera.main;
                if (mapCamera == null)
                {
                    mapCamera = FindFirstObjectByType<Camera>();
                }
                if (mapCamera == null)
                {
                    DominionLogger.LogError("MapInitializer: No camera found for map rendering");
                }
                else if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Found camera for map rendering");
                }
            }
        }

        private void InitializeCameraController()
        {
            // First check on this GameObject
            cameraController = GetComponent<ParadoxStyleCameraController>();

            // If not found, search the scene
            if (cameraController == null)
            {
                cameraController = FindFirstObjectByType<ParadoxStyleCameraController>();
            }

            if (cameraController != null)
            {
                // Set up camera controller references
                cameraController.mapCamera = mapCamera;
                cameraController.mapPlane = meshRenderer?.gameObject;

                // Initialize the camera controller
                cameraController.Initialize();

                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit($"MapInitializer: Initialized ParadoxStyleCameraController on {cameraController.gameObject.name}");
                }
            }
            else if (logInitializationProgress)
            {
                DominionLogger.LogMapInit("MapInitializer: No ParadoxStyleCameraController found in scene - camera control disabled");
            }
        }

        private void InitializeTextureUpdateBridge()
        {
            textureUpdateBridge = GetComponent<TextureUpdateBridge>();
            if (textureUpdateBridge == null)
            {
                textureUpdateBridge = gameObject.AddComponent<TextureUpdateBridge>();
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Created TextureUpdateBridge component");
                }
            }
        }

        private void InitializeDebugUI()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debugUI = GetComponent<MapModeDebugUI>();
            if (debugUI == null)
            {
                debugUI = gameObject.AddComponent<MapModeDebugUI>();
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Created MapModeDebugUI component");
                }
            }

            // Set the MapModeManager reference after both components are initialized
            if (debugUI != null && mapModeManager != null)
            {
                debugUI.SetMapModeManager(mapModeManager);
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Connected MapModeDebugUI to MapModeManager");
                }
            }
#endif
        }

        /// <summary>
        /// Set progress callback for ProvinceMapProcessor
        /// </summary>
        public void SetProvinceProcessingCallback(System.Action<ProvinceMapProcessor.ProcessingProgress> callback)
        {
            if (provinceProcessor != null)
            {
                provinceProcessor.OnProgressUpdate += callback;
            }
        }

        /// <summary>
        /// Initialize ProvinceSelector after rendering setup is complete
        /// </summary>
        public void InitializeProvinceSelectorWithMesh()
        {
            if (provinceSelector != null && textureManager != null && meshRenderer != null)
            {
                provinceSelector.Initialize(textureManager, meshRenderer.transform);
                if (logInitializationProgress)
                {
                    DominionLogger.LogMapInit("MapInitializer: Initialized ProvinceSelector for province interaction");
                }
            }
        }

    }
}