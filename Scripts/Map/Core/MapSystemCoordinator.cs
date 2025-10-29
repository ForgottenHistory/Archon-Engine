using UnityEngine;
using Map.Rendering;
using Map.MapModes;
using Map.Loading;
using Map.Interaction;
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

        // Progress callback for loading screen
        public System.Action<float, string> OnGenerationProgress;

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
        /// Get the map plane transform for border mesh scaling
        /// </summary>
        public Transform GetMapPlaneTransform() => meshRenderer?.transform;

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
                ArchonLogger.Log("MapSystemCoordinator: Initializing complete map system...", "map_initialization");
            }

            InitializeAllComponents();
            SetupDependencies();

            if (logSystemProgress)
            {
                ArchonLogger.Log("MapSystemCoordinator: Map system initialization complete", "map_initialization");
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
                    ArchonLogger.LogError("MapSystemCoordinator: Failed to load province data from files", "core_simulation");
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
                ArchonLogger.LogError($"MapSystemCoordinator: Exception during file-based generation: {e.Message}", "core_simulation");
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
                // Load province data with simulation integration (0-40% of generation)
                OnGenerationProgress?.Invoke(0f, "Loading province bitmap...");
                var provinceResult = await dataLoader.LoadFromSimulationAsync(simulationData, bitmapPath, csvPath, useDefinition);
                if (!provinceResult.HasValue)
                {
                    ArchonLogger.LogError("MapSystemCoordinator: Failed to load province data from simulation", "core_simulation");
                    return false;
                }

                OnGenerationProgress?.Invoke(40f, "Province bitmap loaded");

                // Convert and populate with simulation data (40-80% of generation)
                OnGenerationProgress?.Invoke(40f, "Validating provinces...");
                var gameState = FindFirstObjectByType<GameState>();
                ProvinceMapping = ConvertProvinceResultWithSimulationData(provinceResult.Value, textureManager, gameState);

                OnGenerationProgress?.Invoke(50f, "Populating textures...");
                texturePopulator.PopulateWithSimulationData(provinceResult.Value, textureManager, ProvinceMapping, gameState);
                provinceResult.Value.Dispose();

                OnGenerationProgress?.Invoke(80f, "Textures populated");

                // Setup rendering (80-95% of generation)
                OnGenerationProgress?.Invoke(80f, "Setting up rendering...");
                renderingCoordinator.SetupMapRendering();
                provinceSelector.Initialize(textureManager, meshRenderer.transform);

                OnGenerationProgress?.Invoke(90f, "Rendering configured");

                // Note: MapModeManager initialization is controlled by GAME layer
                // ENGINE provides mechanism, GAME controls initialization flow

                // Initialize TextureUpdateBridge for runtime texture updates (95-97%)
                OnGenerationProgress?.Invoke(95f, "Initializing texture updates...");
                if (textureUpdateBridge != null && gameState != null)
                {
                    textureUpdateBridge.Initialize(gameState, textureManager, texturePopulator, ProvinceMapping);

                    if (logSystemProgress)
                    {
                        ArchonLogger.Log("MapSystemCoordinator: Initialized TextureUpdateBridge for runtime updates", "map_initialization");
                    }
                }
                else if (logSystemProgress)
                {
                    ArchonLogger.LogWarning("MapSystemCoordinator: TextureUpdateBridge not available - runtime texture updates disabled", "core_simulation");
                }

                // NOTE: Smooth border initialization moved to HegemonMapPhaseHandler
                // Must be AFTER AdjacencySystem.SetAdjacencies() is called
                // (AdjacencySystem instance exists but is empty at this point)

                OnGenerationProgress?.Invoke(100f, "Map generation complete");

                return true;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"MapSystemCoordinator: Exception during simulation-based generation: {e.Message}\n{e.StackTrace}", "core_simulation");
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
                ArchonLogger.Log($"MapSystemCoordinator: Handling simulation ready with {simulationData.ProvinceCount} provinces", "map_initialization");
            }

            // Get components from MapInitializer (which already initialized them)
            var initializer = GetComponent<MapInitializer>();
            if (initializer == null)
            {
                ArchonLogger.LogError("MapSystemCoordinator: MapInitializer not found - cannot proceed", "core_simulation");
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
                ArchonLogger.Log($"MapSystemCoordinator: Received camera reference: {(mapCamera != null ? mapCamera.name : "null")}", "map_initialization");
                ArchonLogger.Log($"MapSystemCoordinator: Received meshRenderer reference: {(meshRenderer != null ? meshRenderer.name : "null")}", "map_initialization");
            }

            if (meshRenderer == null)
            {
                ArchonLogger.LogError("MapSystemCoordinator: MeshRenderer reference not found from MapInitializer", "core_simulation");
                return;
            }

            // Generate map from simulation data using provided paths
            bool success = await GenerateMapFromSimulation(simulationData, bitmapPath, csvPath, useDefinition);

            if (success && logSystemProgress)
            {
                ArchonLogger.Log($"MapSystemCoordinator: Map generation complete. Rendering {simulationData.ProvinceCount} provinces.", "map_initialization");
            }

            // Notify MapInitializer that initialization is complete
            var mapInitializer = GetComponent<MapInitializer>();
            if (mapInitializer != null)
            {
                mapInitializer.SetInitialized(success);
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
                    ArchonLogger.Log($"MapSystemCoordinator: Created {typeof(T).Name} component", "map_initialization");
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

            // BUG FIX: bitmapProvinceID is a definition ID (from definition.csv), not a runtime ID
            // Must use ProvinceRegistry.ExistsByDefinition() instead of ProvinceQueries.Exists()
            // ProvinceRegistry maps: definition ID (sparse: 1, 10, 1299, etc.) → runtime ID (dense: 1, 2, 334, etc.)
            var provinceRegistry = gameState.Registries?.Provinces;
            if (provinceRegistry == null)
            {
                ArchonLogger.LogError("MapSystemCoordinator: GameState.Registries not set! Cannot validate provinces.", "map_initialization");
                return null;
            }

            // Build mappings with simulation validation
            var colorMappingEnumerator = provinceResult.ProvinceMappings.ColorToProvinceID.GetEnumerator();
            int totalBitmapProvinces = 0;
            int validatedProvinces = 0;
            int skippedProvinces = 0;

            while (colorMappingEnumerator.MoveNext())
            {
                int rgb = colorMappingEnumerator.Current.Key;
                int bitmapProvinceID = colorMappingEnumerator.Current.Value;
                totalBitmapProvinces++;

                byte r = (byte)((rgb >> 16) & 0xFF);
                byte g = (byte)((rgb >> 8) & 0xFF);
                byte b = (byte)(rgb & 0xFF);
                var color = new Color32(r, g, b, 255);

                // Check if province exists by definition ID (from definition.csv)
                if (provinceRegistry.ExistsByDefinition(bitmapProvinceID))
                {
                    mapping.AddProvince((ushort)bitmapProvinceID, color);
                    validatedProvinces++;
                }
                else
                {
                    skippedProvinces++;
                    // Log first few skipped provinces for debugging
                    if (skippedProvinces <= 10)
                    {
                        ArchonLogger.Log($"MapSystemCoordinator: Skipping province {bitmapProvinceID} RGB({r},{g},{b}) - not in registry", "map_initialization");
                    }
                }
            }

            ArchonLogger.Log($"MapSystemCoordinator: Province mapping complete - {totalBitmapProvinces} in bitmap, {validatedProvinces} validated, {skippedProvinces} skipped", "map_initialization");
            return mapping;
        }
    }
}