using UnityEngine;
using Map.Rendering;
using Map.MapModes;
using Map.Loading;
using Map.Interaction;
using Core;
using Utils;
using static Map.Loading.ProvinceMapProcessor;

namespace Map.Core
{
    /// <summary>
    /// Coordinator for map system runtime operations.
    ///
    /// IMPORTANT: This class does NOT initialize components.
    /// All components must be initialized by ArchonEngine BEFORE calling Configure().
    /// This class only coordinates runtime operations (map loading, rendering, interaction).
    /// </summary>
    public class MapSystemCoordinator : MonoBehaviour
    {
        #region Configuration (Injected via Configure method)

        private Camera mapCamera;
        private MeshRenderer meshRenderer;
        private GameSettings gameSettings;

        private bool LogProgress => gameSettings?.ShouldLog(LogLevel.Info) ?? false;

        #endregion

        #region Component References (All injected, NOT initialized here)

        // Components injected by ArchonEngine (already initialized)
        private MapTextureManager textureManager;
        private BorderComputeDispatcher borderDispatcher;
        private OwnerTextureDispatcher ownerTextureDispatcher;
        private ProvinceTerrainAnalyzer terrainAnalyzer;
        private Map.Rendering.Terrain.TerrainBlendMapGenerator blendMapGenerator;

        // Components that may be on the GameObject (optional)
        private MapModeManager mapModeManager;
        private ProvinceSelector provinceSelector;
        private ProvinceHighlighter provinceHighlighter;
        private TextureUpdateBridge textureUpdateBridge;
        private FogOfWarSystem fogOfWarSystem;

        // Plain C# classes (created here, not MonoBehaviours)
        private ProvinceMapProcessor provinceProcessor;
        private MapDataLoader dataLoader;
        private MapTexturePopulator texturePopulator;

        #endregion

        #region Public API

        /// <summary>Province color-to-ID mapping for texture lookups.</summary>
        public ProvinceMapping ProvinceMapping { get; private set; }

        /// <summary>Map texture manager for province textures.</summary>
        public MapTextureManager TextureManager => textureManager;

        /// <summary>Province selector for mouse interaction.</summary>
        public ProvinceSelector ProvinceSelector => provinceSelector;

        /// <summary>Province highlighter for selection visuals.</summary>
        public ProvinceHighlighter ProvinceHighlighter => provinceHighlighter;

        /// <summary>Map mode manager for visualization modes.</summary>
        public MapModeManager MapModeManager => mapModeManager;

        /// <summary>Border compute dispatcher for border rendering.</summary>
        public BorderComputeDispatcher BorderDispatcher => borderDispatcher;

        /// <summary>Camera used for map rendering.</summary>
        public Camera MapCamera => mapCamera;

        /// <summary>Mesh renderer for the map quad.</summary>
        public MeshRenderer MeshRenderer => meshRenderer;

        /// <summary>Data directory from GameSettings.</summary>
        public string DataDirectory => gameSettings?.DataDirectory;

        /// <summary>True when map system is fully initialized.</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>Progress callback for loading screen (0-100, status message).</summary>
        public System.Action<float, string> OnProgress;

        /// <summary>Completion callback (success, message).</summary>
        public System.Action<bool, string> OnComplete;

        #endregion

        #region Initialization

        /// <summary>
        /// Configure the coordinator with required references and already-initialized components.
        /// All components must be initialized by ArchonEngine BEFORE calling this method.
        /// </summary>
        public void Configure(
            Camera camera,
            MeshRenderer mesh,
            GameSettings settings,
            MapTextureManager textures,
            BorderComputeDispatcher borders,
            OwnerTextureDispatcher ownerDispatcher,
            ProvinceTerrainAnalyzer terrain,
            Map.Rendering.Terrain.TerrainBlendMapGenerator blendGen)
        {
            mapCamera = camera;
            meshRenderer = mesh;
            gameSettings = settings;

            // Store already-initialized components (DO NOT call Initialize on these)
            textureManager = textures;
            borderDispatcher = borders;
            ownerTextureDispatcher = ownerDispatcher;
            terrainAnalyzer = terrain;
            blendMapGenerator = blendGen;

            // Get optional components from GameObject
            mapModeManager = GetComponent<MapModeManager>();
            provinceSelector = GetComponent<ProvinceSelector>();
            provinceHighlighter = GetComponent<ProvinceHighlighter>();
            textureUpdateBridge = GetComponent<TextureUpdateBridge>();
            fogOfWarSystem = GetComponent<FogOfWarSystem>();
        }

        /// <summary>
        /// Initialize the map system with simulation data.
        /// Call this after Configure() and after simulation is ready.
        /// </summary>
        public async void Initialize(SimulationDataReadyEvent simulationData)
        {
            if (LogProgress)
                ArchonLogger.Log($"MapSystemCoordinator: Starting map loading with {simulationData.ProvinceCount} provinces", "map_initialization");

            // Validate that Configure() was called with required components
            if (!ValidateConfiguration())
                return;

            // Create plain C# helper classes (these are NOT MonoBehaviours)
            ReportProgress(0f, "Creating data loaders...");
            provinceProcessor = new ProvinceMapProcessor();
            dataLoader = new MapDataLoader(LogProgress);
            dataLoader.Initialize(provinceProcessor, borderDispatcher, textureManager, terrainAnalyzer, blendMapGenerator, gameSettings.DataDirectory);
            texturePopulator = new MapTexturePopulator(ownerTextureDispatcher, LogProgress);

            // Initialize FogOfWarSystem if available
            var gameState = GameState.Instance;
            if (fogOfWarSystem != null && gameState != null)
            {
                fogOfWarSystem.Initialize(gameState.ProvinceQueries, gameState.Provinces.Capacity);
            }

            // Load and process map data (20-80%)
            ReportProgress(20f, "Loading province data...");
            bool success = await GenerateMap(simulationData);

            if (!success)
            {
                ReportError("Map generation failed");
                return;
            }

            // Setup rendering (80-90%)
            ReportProgress(80f, "Setting up rendering...");
            SetupRendering();

            // Setup interaction (90-95%)
            ReportProgress(90f, "Setting up interaction...");
            SetupInteraction();

            // Terrain analysis (95-100%)
            ReportProgress(95f, "Analyzing terrain...");
            AnalyzeTerrain();

            // Complete
            IsInitialized = true;
            ReportProgress(100f, "Map ready");

            if (LogProgress)
                ArchonLogger.Log("MapSystemCoordinator: Map loading complete", "map_initialization");

            OnComplete?.Invoke(true, "Map initialized successfully");
        }

        private bool ValidateConfiguration()
        {
            if (textureManager == null)
            {
                ReportError("MapTextureManager not configured - call Configure() first");
                return false;
            }

            if (meshRenderer == null)
            {
                ReportError("MeshRenderer not configured - call Configure() first");
                return false;
            }

            if (gameSettings == null)
            {
                ReportError("GameSettings not configured - call Configure() first");
                return false;
            }

            if (mapCamera == null)
            {
                mapCamera = Camera.main;
                if (mapCamera == null)
                {
                    ReportError("No camera found for map rendering");
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Map Generation

        private async System.Threading.Tasks.Task<bool> GenerateMap(SimulationDataReadyEvent simulationData)
        {
            try
            {
                // Build paths from GameSettings
                string bitmapPath = System.IO.Path.Combine(gameSettings.DataDirectory, "map", "provinces.bmp");
                if (!System.IO.File.Exists(bitmapPath))
                {
                    string pngPath = System.IO.Path.Combine(gameSettings.DataDirectory, "map", "provinces.png");
                    if (System.IO.File.Exists(pngPath))
                        bitmapPath = pngPath;
                }

                string csvPath = System.IO.Path.Combine(gameSettings.DataDirectory, "map", "definition.csv");
                bool useDefinition = System.IO.File.Exists(csvPath);

                // Load province data
                var provinceResult = await dataLoader.LoadFromSimulationAsync(simulationData, bitmapPath, csvPath, useDefinition);
                if (!provinceResult.HasValue)
                {
                    ArchonLogger.LogError("MapSystemCoordinator: Failed to load province data", "map_initialization");
                    return false;
                }

                ReportProgress(50f, "Province data loaded");

                // Convert to mapping with simulation validation
                var gameState = GameState.Instance;
                ProvinceMapping = ConvertProvinceResult(provinceResult.Value, gameState);

                if (ProvinceMapping == null)
                {
                    provinceResult.Value.Dispose();
                    return false;
                }

                ReportProgress(60f, "Populating textures...");

                // Populate textures
                texturePopulator.PopulateWithSimulationData(provinceResult.Value, textureManager, ProvinceMapping, gameState);
                provinceResult.Value.Dispose();

                ReportProgress(80f, "Textures populated");
                return true;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"MapSystemCoordinator: Exception during map generation: {e.Message}\n{e.StackTrace}", "map_initialization");
                return false;
            }
        }

        private ProvinceMapping ConvertProvinceResult(ProvinceMapResult provinceResult, GameState gameState)
        {
            if (!provinceResult.IsSuccess || gameState == null)
                return null;

            var mapping = new ProvinceMapping();
            var provinceRegistry = gameState.Registries?.Provinces;

            if (provinceRegistry == null)
            {
                ArchonLogger.LogError("MapSystemCoordinator: GameState.Registries not set!", "map_initialization");
                return null;
            }

            var colorMappingEnumerator = provinceResult.ProvinceMappings.ColorToProvinceID.GetEnumerator();
            int total = 0, validated = 0, skipped = 0;

            while (colorMappingEnumerator.MoveNext())
            {
                int rgb = colorMappingEnumerator.Current.Key;
                int bitmapProvinceID = colorMappingEnumerator.Current.Value;
                total++;

                byte r = (byte)((rgb >> 16) & 0xFF);
                byte g = (byte)((rgb >> 8) & 0xFF);
                byte b = (byte)(rgb & 0xFF);
                var color = new Color32(r, g, b, 255);

                if (provinceRegistry.ExistsByDefinition(bitmapProvinceID))
                {
                    mapping.AddProvince((ushort)bitmapProvinceID, color);
                    validated++;
                }
                else
                {
                    skipped++;
                }
            }

            if (LogProgress)
                ArchonLogger.Log($"MapSystemCoordinator: Mapping complete - {total} total, {validated} validated, {skipped} skipped", "map_initialization");

            return mapping;
        }

        #endregion

        #region Rendering Setup

        private void SetupRendering()
        {
            // Setup material with textures
            if (meshRenderer != null && textureManager != null)
            {
                var material = meshRenderer.sharedMaterial;
                if (material != null)
                {
                    textureManager.BindTexturesToMaterial(material);

                    if (LogProgress)
                        ArchonLogger.Log("MapSystemCoordinator: Textures bound to material", "map_initialization");
                }
            }

            // Initialize texture update bridge for runtime updates
            var gameState = GameState.Instance;
            if (textureUpdateBridge != null && gameState != null)
            {
                textureUpdateBridge.Initialize(gameState, textureManager, texturePopulator, ProvinceMapping, ownerTextureDispatcher, borderDispatcher);
            }
        }

        private void SetupInteraction()
        {
            if (provinceSelector != null && textureManager != null && meshRenderer != null)
            {
                provinceSelector.Initialize(textureManager, meshRenderer.transform);

                if (LogProgress)
                    ArchonLogger.Log("MapSystemCoordinator: Province selector initialized", "map_initialization");
            }
        }

        private void AnalyzeTerrain()
        {
            var gameState = GameState.Instance;
            if (gameState != null && dataLoader != null)
            {
                dataLoader.AnalyzeProvinceTerrainAfterMapInit(gameState);
            }
        }

        #endregion

        #region Runtime API

        /// <summary>
        /// Refresh all map visuals from current simulation state.
        /// Call after loading a save game or any bulk state change.
        /// </summary>
        public void RefreshAllVisuals()
        {
            var gameState = GameState.Instance;
            if (gameState == null)
            {
                ArchonLogger.LogWarning("MapSystemCoordinator: Cannot refresh visuals - GameState not available", "map_rendering");
                return;
            }

            if (LogProgress)
                ArchonLogger.Log("MapSystemCoordinator: Refreshing all map visuals...", "map_rendering");

            // Refresh owner texture
            if (ownerTextureDispatcher != null)
            {
                ownerTextureDispatcher.PopulateOwnerTexture(gameState.ProvinceQueries);
            }

            // Refresh borders
            if (borderDispatcher != null)
            {
                borderDispatcher.DetectBorders();
            }

            // Force texture update bridge to process pending updates
            if (textureUpdateBridge != null)
            {
                textureUpdateBridge.ForceUpdate();
            }

            if (LogProgress)
                ArchonLogger.Log("MapSystemCoordinator: Visuals refreshed", "map_rendering");
        }

        /// <summary>
        /// Get province ID at world position.
        /// </summary>
        public ushort GetProvinceAtWorldPosition(Vector3 worldPosition)
        {
            return provinceSelector?.GetProvinceAtWorldPosition(worldPosition) ?? 0;
        }

        /// <summary>
        /// Get the map plane transform for border mesh scaling.
        /// </summary>
        public Transform GetMapPlaneTransform() => meshRenderer?.transform;

        #endregion

        #region Helpers

        private void ReportProgress(float progress, string status)
        {
            if (LogProgress)
                ArchonLogger.Log($"MapSystemCoordinator: [{progress:F0}%] {status}", "map_initialization");

            OnProgress?.Invoke(progress, status);
        }

        private void ReportError(string error)
        {
            ArchonLogger.LogError($"MapSystemCoordinator: {error}", "map_initialization");
            OnComplete?.Invoke(false, error);
        }

        #endregion

        #region Lifecycle

        private void OnDestroy()
        {
            // Dispose resources that have native allocations
            dataLoader?.Dispose();
        }

        #endregion
    }
}
