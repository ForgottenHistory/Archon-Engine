using UnityEngine;
using Map.Rendering;
using Map.MapModes;
using Map.Loading;
using Map.Interaction;
using Map.Debug;
using Map.Simulation;
using Core;
using Utils;
using static Map.Loading.ProvinceMapProcessor;

namespace Map.Core
{
    /// <summary>
    /// Central coordinator for the entire map system
    /// Manages all map components internally without exposing them to MapGenerator
    /// Follows facade pattern to simplify MapGenerator's responsibilities
    /// </summary>
    public class MapSystemCoordinator : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool logSystemProgress = true;

        // Internal components - not exposed to MapGenerator
        private MeshRenderer meshRenderer;
        private Camera mapCamera;
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

        // Only expose what MapGenerator actually needs
        public ProvinceMapping ProvinceMapping { get; private set; }
        public MapTextureManager TextureManager => textureManager;

        /// <summary>
        /// Initialize the entire map system with proper references
        /// </summary>
        public void InitializeSystem(Camera camera, MeshRenderer renderer)
        {
            // Set references FIRST
            mapCamera = camera;
            meshRenderer = renderer;

            if (logSystemProgress)
            {
                DominionLogger.LogMapInit("MapSystemCoordinator: Initializing complete map system...");
            }

            InitializeAllComponents();
            SetupDependencies();

            if (logSystemProgress)
            {
                DominionLogger.LogMapInit("MapSystemCoordinator: Map system initialization complete");
            }
        }

        /// <summary>
        /// Generate map from files (legacy method)
        /// </summary>
        public async System.Threading.Tasks.Task<bool> GenerateMapFromFiles(string bitmapPath, string csvPath, bool useDefinition)
        {
            try
            {
                // Load province data
                var provinceResult = await dataLoader.LoadFromFilesAsync(bitmapPath, csvPath, useDefinition);
                if (!provinceResult.HasValue)
                {
                    DominionLogger.LogError("MapSystemCoordinator: Failed to load province data from files");
                    return false;
                }

                // Convert and populate
                ProvinceMapping = ConvertProvinceResultToMapping(provinceResult.Value, textureManager);
                texturePopulator.PopulateFromProvinceResult(provinceResult.Value, textureManager, ProvinceMapping);
                provinceResult.Value.Dispose();

                // Setup rendering
                renderingCoordinator.SetupMapRendering();
                provinceSelector.Initialize(textureManager, meshRenderer.transform);

                // Note: MapModeManager initialization is controlled by GAME layer
                // ENGINE provides mechanism, GAME controls initialization flow

                return true;
            }
            catch (System.Exception e)
            {
                DominionLogger.LogError($"MapSystemCoordinator: Exception during file-based generation: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Generate map from simulation data (preferred method)
        /// </summary>
        public async System.Threading.Tasks.Task<bool> GenerateMapFromSimulation(SimulationDataReadyEvent simulationData, string bitmapPath, string csvPath, bool useDefinition)
        {
            try
            {
                // Load province data with simulation integration
                var provinceResult = await dataLoader.LoadFromSimulationAsync(simulationData, bitmapPath, csvPath, useDefinition);
                if (!provinceResult.HasValue)
                {
                    DominionLogger.LogError("MapSystemCoordinator: Failed to load province data from simulation");
                    return false;
                }

                // Convert and populate with simulation data
                var gameState = FindFirstObjectByType<GameState>();
                ProvinceMapping = ConvertProvinceResultWithSimulationData(provinceResult.Value, textureManager, gameState);
                texturePopulator.PopulateWithSimulationData(provinceResult.Value, textureManager, ProvinceMapping, gameState);
                provinceResult.Value.Dispose();

                // Setup rendering
                renderingCoordinator.SetupMapRendering();
                provinceSelector.Initialize(textureManager, meshRenderer.transform);

                // Note: MapModeManager initialization is controlled by GAME layer
                // ENGINE provides mechanism, GAME controls initialization flow

                // Initialize TextureUpdateBridge for runtime texture updates
                if (textureUpdateBridge != null && gameState != null)
                {
                    textureUpdateBridge.Initialize(gameState, textureManager, texturePopulator, ProvinceMapping);

                    if (logSystemProgress)
                    {
                        DominionLogger.LogMapInit("MapSystemCoordinator: Initialized TextureUpdateBridge for runtime updates");
                    }
                }
                else if (logSystemProgress)
                {
                    DominionLogger.LogWarning("MapSystemCoordinator: TextureUpdateBridge not available - runtime texture updates disabled");
                }

                return true;
            }
            catch (System.Exception e)
            {
                DominionLogger.LogError($"MapSystemCoordinator: Exception during simulation-based generation: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get province ID at world position
        /// </summary>
        public ushort GetProvinceAtWorldPosition(Vector3 worldPosition)
        {
            return provinceSelector?.GetProvinceAtWorldPosition(worldPosition) ?? 0;
        }

        /// <summary>
        /// Set map mode
        /// </summary>
        public void SetMapMode(int modeId)
        {
            mapModeManager?.SetMapMode((Map.MapModes.MapMode)modeId);
        }

        /// <summary>
        /// Handle simulation ready event (called by MapInitializer after components are initialized)
        /// </summary>
        public async void HandleSimulationReady(SimulationDataReadyEvent simulationData, string bitmapPath, string csvPath, bool useDefinition)
        {
            if (logSystemProgress)
            {
                DominionLogger.LogMapInit($"MapSystemCoordinator: Handling simulation ready with {simulationData.ProvinceCount} provinces");
            }

            // Get components from MapInitializer (which already initialized them)
            var initializer = GetComponent<MapInitializer>();
            if (initializer == null)
            {
                DominionLogger.LogError("MapSystemCoordinator: MapInitializer not found - cannot proceed");
                return;
            }

            // Get initialized components from MapInitializer
            textureManager = initializer.TextureManager;
            borderDispatcher = initializer.BorderDispatcher;
            ownerTextureDispatcher = initializer.OwnerTextureDispatcher;  // GPU owner texture dispatcher
            mapModeManager = initializer.MapModeManager;
            provinceProcessor = initializer.ProvinceProcessor;
            dataLoader = initializer.DataLoader;
            renderingCoordinator = initializer.RenderingCoordinator;
            provinceSelector = initializer.ProvinceSelector;
            texturePopulator = initializer.TexturePopulator;
            textureUpdateBridge = initializer.TextureUpdateBridge;
            mapCamera = initializer.MapCamera;
            meshRenderer = initializer.MeshRenderer;

            if (logSystemProgress)
            {
                DominionLogger.LogMapInit($"MapSystemCoordinator: Received camera reference: {(mapCamera != null ? mapCamera.name : "null")}");
                DominionLogger.LogMapInit($"MapSystemCoordinator: Received meshRenderer reference: {(meshRenderer != null ? meshRenderer.name : "null")}");
            }

            if (meshRenderer == null)
            {
                DominionLogger.LogError("MapSystemCoordinator: MeshRenderer reference not found from MapInitializer");
                return;
            }

            // Generate map from simulation data using provided paths
            bool success = await GenerateMapFromSimulation(simulationData, bitmapPath, csvPath, useDefinition);

            if (success && logSystemProgress)
            {
                DominionLogger.LogMapInit($"MapSystemCoordinator: Map generation complete. Rendering {simulationData.ProvinceCount} provinces.");
            }
        }

        private void InitializeAllComponents()
        {
            // Core components
            textureManager = GetOrCreateComponent<MapTextureManager>();
            borderDispatcher = GetOrCreateComponent<BorderComputeDispatcher>();
            ownerTextureDispatcher = GetOrCreateComponent<OwnerTextureDispatcher>();  // GPU owner texture population
            mapModeManager = GetOrCreateComponent<MapModeManager>();
            dataLoader = GetOrCreateComponent<MapDataLoader>();
            renderingCoordinator = GetOrCreateComponent<MapRenderingCoordinator>();
            provinceSelector = GetOrCreateComponent<ProvinceSelector>();
            texturePopulator = GetOrCreateComponent<MapTexturePopulator>();

            // Non-component objects
            provinceProcessor = new ProvinceMapProcessor();
        }

        private void SetupDependencies()
        {
            // Setup component dependencies
            borderDispatcher?.SetTextureManager(textureManager);
            ownerTextureDispatcher?.SetTextureManager(textureManager);  // GPU owner texture dispatcher
            dataLoader?.Initialize(provinceProcessor, borderDispatcher, textureManager);
            renderingCoordinator?.Initialize(textureManager, mapModeManager, meshRenderer, mapCamera);
        }

        private T GetOrCreateComponent<T>() where T : Component
        {
            var component = GetComponent<T>();
            if (component == null)
            {
                component = gameObject.AddComponent<T>();
                if (logSystemProgress)
                {
                    DominionLogger.LogMapInit($"MapSystemCoordinator: Created {typeof(T).Name} component");
                }
            }
            return component;
        }

        // Simplified conversion methods (moved from MapGenerator)
        private ProvinceMapping ConvertProvinceResultToMapping(ProvinceMapResult provinceResult, MapTextureManager textureManager)
        {
            if (!provinceResult.Success)
                return null;

            var mapping = new ProvinceMapping();

            // Build color mappings
            var colorMappingEnumerator = provinceResult.ProvinceMappings.ColorToProvinceID.GetEnumerator();
            while (colorMappingEnumerator.MoveNext())
            {
                int rgb = colorMappingEnumerator.Current.Key;
                int provinceID = colorMappingEnumerator.Current.Value;

                byte r = (byte)((rgb >> 16) & 0xFF);
                byte g = (byte)((rgb >> 8) & 0xFF);
                byte b = (byte)(rgb & 0xFF);
                var color = new Color32(r, g, b, 255);

                mapping.AddProvince((ushort)provinceID, color);
            }

            return mapping;
        }

        private ProvinceMapping ConvertProvinceResultWithSimulationData(ProvinceMapResult provinceResult, MapTextureManager textureManager, GameState gameState)
        {
            if (!provinceResult.Success || gameState == null)
                return null;

            var mapping = new ProvinceMapping();
            var provinceQueries = gameState.ProvinceQueries;

            // Build mappings with simulation validation
            var colorMappingEnumerator = provinceResult.ProvinceMappings.ColorToProvinceID.GetEnumerator();
            while (colorMappingEnumerator.MoveNext())
            {
                int rgb = colorMappingEnumerator.Current.Key;
                int bitmapProvinceID = colorMappingEnumerator.Current.Value;

                byte r = (byte)((rgb >> 16) & 0xFF);
                byte g = (byte)((rgb >> 8) & 0xFF);
                byte b = (byte)(rgb & 0xFF);
                var color = new Color32(r, g, b, 255);

                // DEBUG: Log Spanish Cuenca
                if (bitmapProvinceID == 2751 || bitmapProvinceID == 817)
                {
                    bool exists = provinceQueries.Exists((ushort)bitmapProvinceID);
                    DominionLogger.LogMapInit($"MapSystemCoordinator: Province {bitmapProvinceID} in bitmap → Exists in simulation: {exists}");
                }

                if (provinceQueries.Exists((ushort)bitmapProvinceID))
                {
                    mapping.AddProvince((ushort)bitmapProvinceID, color);
                }
            }

            return mapping;
        }
    }
}