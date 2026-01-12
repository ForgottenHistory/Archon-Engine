using UnityEngine;
using System.Collections;
using Core;
using Archon.Engine.Map;
using Map.MapModes;
using Map.Rendering;
using Map.CameraControllers;

namespace Map.Core
{
    /// <summary>
    /// ENGINE LAYER: Simple coordinator for standalone map showcase scenes
    /// Wires EngineInitializer (Core) → MapInitializer (Map) without GAME layer dependencies
    /// </summary>
    public class EngineMapInitializer : MonoBehaviour
    {
        [Header("Engine References")]
        [SerializeField] private EngineInitializer engineInitializer;
        [SerializeField] private MapInitializer mapInitializer;
        [SerializeField] private VisualStyleManager visualStyleManager;

        [Header("Camera")]
        [SerializeField] private bool initializeCamera = true;

        [Header("Configuration")]
        [SerializeField] private bool initializeOnStart = true;
        [SerializeField] private bool logProgress = true;

        // State
        private bool isInitialized = false;
        public bool IsInitialized => isInitialized;

        void Start()
        {
            if (initializeOnStart)
            {
                StartCoroutine(InitializeSequence());
            }
        }

        private IEnumerator InitializeSequence()
        {
            if (logProgress)
                ArchonLogger.Log("=== Starting ENGINE map initialization ===", "map_initialization");

            // Find references if not assigned
            if (engineInitializer == null)
                engineInitializer = FindFirstObjectByType<EngineInitializer>();
            if (mapInitializer == null)
                mapInitializer = FindFirstObjectByType<MapInitializer>();
            if (visualStyleManager == null)
                visualStyleManager = FindFirstObjectByType<VisualStyleManager>();

            // Validate
            if (engineInitializer == null)
            {
                ArchonLogger.LogError("EngineMapInitializer: EngineInitializer not found!", "map_initialization");
                yield break;
            }
            if (mapInitializer == null)
            {
                ArchonLogger.LogError("EngineMapInitializer: MapInitializer not found!", "map_initialization");
                yield break;
            }

            // Start and wait for EngineInitializer to complete
            if (logProgress)
                ArchonLogger.Log("[1/6] Starting engine initialization...", "map_initialization");

            engineInitializer.StartInitialization();

            while (!engineInitializer.IsComplete)
            {
                if (engineInitializer.CurrentPhase == EngineInitializer.LoadingPhase.Error)
                {
                    ArchonLogger.LogError("EngineMapInitializer: Engine initialization failed!", "map_initialization");
                    yield break;
                }
                yield return null;
            }

            if (logProgress)
                ArchonLogger.Log("[1/6] ✓ Engine initialization complete", "map_initialization");

            // Apply visual style before map initialization
            if (logProgress)
                ArchonLogger.Log("[2/6] Applying visual style...", "map_initialization");

            if (visualStyleManager != null)
            {
                var activeStyle = visualStyleManager.GetActiveStyle();
                if (activeStyle != null)
                {
                    visualStyleManager.ApplyStyle(activeStyle);
                    if (logProgress)
                        ArchonLogger.Log($"[2/6] ✓ Visual style '{activeStyle.styleName}' applied", "map_initialization");
                }
                else
                {
                    ArchonLogger.LogWarning("EngineMapInitializer: No active visual style configured", "map_initialization");
                }
            }

            yield return null;

            // Trigger map initialization
            if (logProgress)
                ArchonLogger.Log("[3/6] Starting map initialization...", "map_initialization");

            mapInitializer.StartMapInitialization();

            // Wait for map to complete
            while (!mapInitializer.IsInitialized)
            {
                yield return null;
            }

            // Initialize province selector (needs mesh to be ready)
            mapInitializer.InitializeProvinceSelectorWithMesh();

            // Initialize map modes (ENGINE-only political mode)
            if (logProgress)
                ArchonLogger.Log("[4/6] Initializing map modes...", "map_initialization");

            yield return InitializeMapModes();

            // Initialize border system (must happen after map init when BorderDispatcher exists)
            if (logProgress)
                ArchonLogger.Log("[5/6] Initializing border system...", "map_initialization");

            yield return InitializeBorderSystem();

            // Apply border configuration
            if (visualStyleManager != null)
            {
                var activeStyle = visualStyleManager.GetActiveStyle();
                if (activeStyle != null)
                {
                    visualStyleManager.ApplyBorderConfiguration(activeStyle);
                    if (logProgress)
                        ArchonLogger.Log("[5/6] ✓ Border system initialized and configured", "map_initialization");
                }
            }

            // Initialize camera
            if (initializeCamera)
            {
                if (logProgress)
                    ArchonLogger.Log("[6/6] Initializing camera...", "map_initialization");

                InitializeCameraController();
            }

            isInitialized = true;

            if (logProgress)
                ArchonLogger.Log("=== ✓ ENGINE map initialization complete ===", "map_initialization");
        }

        /// <summary>
        /// Initialize border system (distance field generator)
        /// Mirrors what HegemonMapPhaseHandler.ScanProvinceAdjacencies does in GAME layer
        /// </summary>
        private IEnumerator InitializeBorderSystem()
        {
            var mapSystemCoordinator = FindFirstObjectByType<MapSystemCoordinator>();
            var gameState = FindFirstObjectByType<GameState>();

            if (mapSystemCoordinator == null || gameState == null)
            {
                ArchonLogger.LogWarning("EngineMapInitializer: Missing dependencies for border system", "map_initialization");
                yield break;
            }

            var borderDispatcher = mapSystemCoordinator.GetComponent<BorderComputeDispatcher>();
            if (borderDispatcher == null)
            {
                ArchonLogger.LogWarning("EngineMapInitializer: BorderComputeDispatcher not found", "map_initialization");
                yield break;
            }

            // Border rendering mode is set via inspector on BorderComputeDispatcher component
            // Available modes: ShaderDistanceField, ShaderPixelPerfect, MeshGeometry, None

            // Initialize smooth borders with distance field generator
            borderDispatcher.InitializeSmoothBorders(
                gameState.Adjacencies,
                gameState.Provinces,
                gameState.Countries,
                mapSystemCoordinator.ProvinceMapping,
                null // mapPlaneTransform not needed for shader mode
            );

            if (logProgress)
                ArchonLogger.Log("EngineMapInitializer: Initialized border distance field system", "map_initialization");

            yield return null;
        }

        /// <summary>
        /// Initialize camera controller if present
        /// </summary>
        private void InitializeCameraController()
        {
            var cameraController = FindFirstObjectByType<BaseCameraController>();
            if (cameraController != null)
            {
                cameraController.Initialize();
                if (logProgress)
                    ArchonLogger.Log($"[5/5] ✓ Camera initialized ({cameraController.GetType().Name})", "map_initialization");
            }
            else
            {
                if (logProgress)
                    ArchonLogger.Log("[5/5] No camera controller found (optional)", "map_initialization");
            }
        }

        /// <summary>
        /// Initialize ENGINE-only map modes (no GAME layer dependencies)
        /// </summary>
        private IEnumerator InitializeMapModes()
        {
            var mapModeManager = FindFirstObjectByType<MapModeManager>();
            var gameState = FindFirstObjectByType<GameState>();
            var mapSystemCoordinator = FindFirstObjectByType<MapSystemCoordinator>();
            var mapRenderingCoordinator = FindFirstObjectByType<MapRenderingCoordinator>();

            if (mapModeManager == null || gameState == null || mapSystemCoordinator == null || mapRenderingCoordinator == null)
            {
                ArchonLogger.LogError("EngineMapInitializer: Missing dependencies for map mode initialization", "map_initialization");
                yield break;
            }

            var material = mapRenderingCoordinator.MapMaterial;
            if (material == null)
            {
                ArchonLogger.LogError("EngineMapInitializer: Map material not ready", "map_initialization");
                yield break;
            }

            // Initialize MapModeManager (no game system needed for ENGINE-only modes)
            mapModeManager.Initialize(gameState, material, mapSystemCoordinator.ProvinceMapping, null);

            if (!mapModeManager.IsInitialized)
            {
                ArchonLogger.LogError("EngineMapInitializer: MapModeManager initialization failed", "map_initialization");
                yield break;
            }

            // Register ENGINE political map mode
            mapModeManager.RegisterHandler(MapMode.Political, new EnginePoliticalMapMode());

            // Activate political mode
            mapModeManager.SetMapMode(MapMode.Political, forceUpdate: true);

            if (logProgress)
                ArchonLogger.Log("[4/6] ✓ Map modes initialized (Political mode active)", "map_initialization");

            yield return null;
        }
    }
}
