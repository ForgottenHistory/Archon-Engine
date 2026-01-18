using UnityEngine;
using System.Collections.Generic;
using Core;
using Core.Queries;
using Map.Rendering;
using Map.Core;
using Map.MapModes.Colorization;

namespace Map.MapModes
{
    /// <summary>
    /// Central manager for the map mode system
    /// Handles mode switching, texture updates, and handler coordination
    /// Performance: &lt;0.1ms mode switching, efficient texture scheduling
    /// </summary>
    public class MapModeManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Material mapMaterial;

        [Header("Settings")]
        [SerializeField] private MapMode currentMode = MapMode.Political;
        [SerializeField] private bool autoUpdateTextures = true;
        [SerializeField] private float updateInterval = 0.1f;

        [Header("Debug")]
        [SerializeField] private bool logModeChanges = true;
        [SerializeField] private bool logTextureUpdates = false;

        // Core systems
        private MapModeDataTextures dataTextures;
        private TextureUpdateScheduler updateScheduler;
        private GameState gameState;
        private ProvinceMapping provinceMapping;
        private object gameProvinceSystem; // Optional game-specific system (engine doesn't know type)

        // Handler management
        private Dictionary<MapMode, IMapModeHandler> modeHandlers;
        private IMapModeHandler currentHandler;

        // Custom map mode texture array (for GAME-defined map modes)
        // Each custom map mode gets a slot in this array for instant switching
        private const int MAX_CUSTOM_MAP_MODES = 16;
        private Texture2DArray mapModeTextureArray;
        private Dictionary<int, int> customModeToArrayIndex; // Maps custom mode ID to array index
        private int nextArrayIndex = 0;
        private int mapWidth;
        private int mapHeight;

        // Shader property IDs (cached for performance)
        private static readonly int MapModePropertyID = Shader.PropertyToID("_MapMode");
        private static readonly int CustomMapModeIndexPropertyID = Shader.PropertyToID("_CustomMapModeIndex");
        private static readonly int MapModeTextureArrayPropertyID = Shader.PropertyToID("_MapModeTextureArray");
        private static readonly int MapModeTextureCountPropertyID = Shader.PropertyToID("_MapModeTextureCount");

        // State tracking
        private bool isInitialized = false;
        private float lastUpdateTime;

        // Properties
        public MapMode CurrentMode => currentMode;
        public bool IsInitialized => isInitialized;

        void Start()
        {
            // ENGINE does not auto-initialize
            // GAME layer controls initialization via Initialize() call
        }

        void Update()
        {
            if (!isInitialized) return;

            if (autoUpdateTextures && Time.time - lastUpdateTime > updateInterval)
            {
                updateScheduler?.Update();
                lastUpdateTime = Time.time;
            }
        }

        /// <summary>
        /// Initialize the map mode system
        /// ENGINE provides MECHANISM, GAME controls WHEN to initialize
        /// Called by GAME layer after handlers are registered
        /// </summary>
        /// <param name="gameSystem">Optional game-specific province system - engine passes through without knowing type</param>
        public void Initialize(GameState gameStateRef, Material material, ProvinceMapping mapping, object gameSystem = null)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning("MapModeManager: Already initialized, skipping", "map_modes");
                return;
            }

            // Store references provided by GAME
            gameState = gameStateRef;
            mapMaterial = material;
            provinceMapping = mapping;
            gameProvinceSystem = gameSystem;

            // Validate required dependencies
            if (gameState == null)
            {
                ArchonLogger.LogError("MapModeManager: GameState is null - cannot initialize", "map_modes");
                return;
            }

            if (mapMaterial == null)
            {
                ArchonLogger.LogError("MapModeManager: Material is null - cannot initialize", "map_modes");
                return;
            }

            if (provinceMapping == null)
            {
                ArchonLogger.LogError("MapModeManager: ProvinceMapping is null - cannot initialize", "map_modes");
                return;
            }

            // Initialize subsystems
            InitializeTextures();
            InitializeModeHandlers();
            InitializeUpdateScheduler();

            // Register default colorizer with MapRendererRegistry (Pattern 20)
            RegisterDefaultColorizer();

            isInitialized = true;

            ArchonLogger.Log("MapModeManager initialized - ready for GAME to register handlers and set mode", "map_initialization");
        }

        /// <summary>
        /// Register ENGINE's default map mode colorizer with MapRendererRegistry.
        /// GAME layer can register additional custom colorizers via MapRendererRegistry.Instance.RegisterMapModeColorizer().
        /// </summary>
        private void RegisterDefaultColorizer()
        {
            var registry = MapRendererRegistry.Instance;
            if (registry == null)
            {
                ArchonLogger.LogWarning("MapModeManager: MapRendererRegistry not found, cannot register default colorizer", "map_modes");
                return;
            }

            // Only register if not already registered
            if (!registry.HasMapModeColorizer("Gradient"))
            {
                var defaultColorizer = new GradientMapModeColorizer();

                // Initialize colorizer with context
                var context = new MapModeColorizerContext
                {
                    TextureManager = Object.FindFirstObjectByType<MapTextureManager>(),
                    MapWidth = dataTextures?.ProvinceDevelopmentTexture?.width ?? 5632,
                    MapHeight = dataTextures?.ProvinceDevelopmentTexture?.height ?? 2048,
                    MaxProvinces = 65536
                };
                defaultColorizer.Initialize(context);

                registry.RegisterMapModeColorizer(defaultColorizer);
                ArchonLogger.Log("MapModeManager: Registered default gradient colorizer", "map_initialization");
            }
        }

        private void InitializeTextures()
        {
            // Get the existing MapTextureManager
            var textureManager = Object.FindFirstObjectByType<MapTextureManager>();
            if (textureManager == null)
            {
                ArchonLogger.LogError("MapModeManager: MapTextureManager not found", "map_modes");
                return;
            }

            dataTextures = new MapModeDataTextures();
            dataTextures.Initialize(textureManager);
            dataTextures.BindToMaterial(mapMaterial);

            // Initialize custom map mode texture array
            InitializeMapModeTextureArray(textureManager);
        }

        /// <summary>
        /// Initialize the texture array for custom GAME map modes.
        /// Pre-allocates slots for instant switching between modes.
        /// </summary>
        private void InitializeMapModeTextureArray(MapTextureManager textureManager)
        {
            // Get map dimensions from province ID texture
            var provinceIDTexture = textureManager.ProvinceIDTexture;
            if (provinceIDTexture == null)
            {
                ArchonLogger.LogWarning("MapModeManager: ProvinceIDTexture not available, using default dimensions", "map_modes");
                mapWidth = 5632;
                mapHeight = 2048;
            }
            else
            {
                mapWidth = provinceIDTexture.width;
                mapHeight = provinceIDTexture.height;
            }

            // Create texture array for custom map modes
            // ARGB32 format for full color support
            mapModeTextureArray = new Texture2DArray(mapWidth, mapHeight, MAX_CUSTOM_MAP_MODES, TextureFormat.ARGB32, false);
            mapModeTextureArray.filterMode = FilterMode.Point; // No interpolation for map data
            mapModeTextureArray.wrapMode = TextureWrapMode.Clamp;
            mapModeTextureArray.name = "MapModeTextureArray";

            // Initialize with transparent black (alpha=0 means "use default")
            var clearPixels = new Color32[mapWidth * mapHeight];
            for (int i = 0; i < clearPixels.Length; i++)
            {
                clearPixels[i] = new Color32(0, 0, 0, 0);
            }
            for (int slice = 0; slice < MAX_CUSTOM_MAP_MODES; slice++)
            {
                mapModeTextureArray.SetPixels32(clearPixels, slice);
            }
            mapModeTextureArray.Apply();

            // Initialize custom mode tracking
            customModeToArrayIndex = new Dictionary<int, int>();
            nextArrayIndex = 0;

            // Bind to material
            mapMaterial.SetTexture(MapModeTextureArrayPropertyID, mapModeTextureArray);
            mapMaterial.SetInt(MapModeTextureCountPropertyID, 0);

            ArchonLogger.Log($"MapModeManager: Initialized texture array ({mapWidth}x{mapHeight}, {MAX_CUSTOM_MAP_MODES} slots)", "map_initialization");
        }

        private void InitializeModeHandlers()
        {
            // ENGINE provides infrastructure, GAME registers handlers via RegisterHandler()
            modeHandlers = new Dictionary<MapMode, IMapModeHandler>();
        }

        /// <summary>
        /// Register a map mode handler - called by GAME layer during initialization
        /// Enables dependency injection: ENGINE provides mechanism, GAME provides policy
        /// </summary>
        public void RegisterHandler(MapMode mode, IMapModeHandler handler)
        {
            if (modeHandlers == null)
            {
                modeHandlers = new Dictionary<MapMode, IMapModeHandler>();
            }

            // Dispose old handler if replacing to prevent ComputeBuffer leaks
            if (modeHandlers.TryGetValue(mode, out var oldHandler))
            {
                if (oldHandler is System.IDisposable disposable)
                {
                    disposable.Dispose();
                    ArchonLogger.Log($"MapModeManager: Disposed old handler for {mode} mode", "map_modes");
                }
            }

            modeHandlers[mode] = handler;

            // If scheduler exists, register the handler for updates
            if (updateScheduler != null && gameState?.ProvinceQueries != null && provinceMapping != null)
            {
                updateScheduler.RegisterHandler(handler, handler.GetUpdateFrequency(), (h) =>
                {
                    if (gameState?.ProvinceQueries != null && gameState?.CountryQueries != null && provinceMapping != null)
                    {
                        h.UpdateTextures(dataTextures, gameState.ProvinceQueries, gameState.CountryQueries, provinceMapping, gameProvinceSystem);
                    }
                });
            }

            ArchonLogger.Log($"MapModeManager: Registered handler for {mode} mode", "map_initialization");
        }

        private void InitializeUpdateScheduler()
        {
            // Create scheduler - handlers will be registered via RegisterHandler()
            updateScheduler = new TextureUpdateScheduler();
        }

        public void SetMapMode(MapMode mode, bool forceUpdate = false)
        {
            if (!isInitialized || (currentMode == mode && !forceUpdate)) return;

            if (!modeHandlers.TryGetValue(mode, out var newHandler))
            {
                ArchonLogger.LogError($"No handler for map mode: {mode}", "map_modes");
                return;
            }

            currentHandler?.OnDeactivate(mapMaterial);

            currentMode = mode;
            currentHandler = newHandler;

            // Set the active handler in the scheduler (architecture compliance)
            updateScheduler?.SetActiveHandler(currentHandler);

            // Set shader properties for mode switching
            int shaderModeId = currentHandler.ShaderModeID;
            mapMaterial.SetInt(MapModePropertyID, shaderModeId);

            // For custom modes (ShaderModeID >= 2), set the array index
            if (shaderModeId >= 2)
            {
                int arrayIndex = GetCustomModeArrayIndex(shaderModeId);
                if (arrayIndex >= 0)
                {
                    mapMaterial.SetInt(CustomMapModeIndexPropertyID, arrayIndex);
                }
            }

            currentHandler.OnActivate(mapMaterial, dataTextures);

            if (gameState?.ProvinceQueries != null && gameState?.CountryQueries != null && provinceMapping != null)
            {
                currentHandler.UpdateTextures(dataTextures, gameState.ProvinceQueries, gameState.CountryQueries, provinceMapping, gameProvinceSystem);

                // CRITICAL: Rebind textures to material after update to force GPU upload
                // Without this, Unity may use cached texture data from before the update
                dataTextures.BindToMaterial(mapMaterial);
            }

            if (logModeChanges)
            {
                ArchonLogger.Log($"Switched to {currentMode} mode (shader mode {shaderModeId})", "map_initialization");
            }
        }

        public string GetProvinceTooltip(ushort provinceId)
        {
            if (!isInitialized || currentHandler == null) return "";

            if (gameState?.ProvinceQueries != null && gameState?.CountryQueries != null)
            {
                return currentHandler.GetProvinceTooltip(provinceId, gameState.ProvinceQueries, gameState.CountryQueries);
            }

            return $"Province {provinceId}";
        }

        /// <summary>
        /// Force an immediate texture update for the current map mode
        /// </summary>
        public void ForceTextureUpdate()
        {
            if (!isInitialized || currentHandler == null) return;

            if (gameState?.ProvinceQueries != null && gameState?.CountryQueries != null && provinceMapping != null)
            {
                currentHandler.UpdateTextures(dataTextures, gameState.ProvinceQueries, gameState.CountryQueries, provinceMapping, gameProvinceSystem);
                ArchonLogger.Log($"MapModeManager: Forced texture update for {currentMode} mode", "map_initialization");
            }
        }

        /// <summary>
        /// Force texture update only if the given handler is currently active.
        /// Called by GradientMapMode.MarkDirty() to trigger event-driven updates.
        /// </summary>
        public void ForceUpdateIfActive(IMapModeHandler handler)
        {
            if (!isInitialized || currentHandler == null || handler == null) return;

            // Only update if this handler is the currently active one
            if (currentHandler == handler)
            {
                if (gameState?.ProvinceQueries != null && gameState?.CountryQueries != null && provinceMapping != null)
                {
                    handler.UpdateTextures(dataTextures, gameState.ProvinceQueries, gameState.CountryQueries, provinceMapping, gameProvinceSystem);
                }
            }
        }

        // Deferred update tracking - batches multiple dirty events into single update per frame
        private bool hasPendingDeferredUpdate = false;
        private IMapModeHandler pendingDeferredHandler = null;

        /// <summary>
        /// Request a deferred texture update for next frame.
        /// Multiple requests within same frame are batched into single update.
        /// Called by GradientMapMode.MarkDirty() to prevent multiple rebuilds per tick.
        /// </summary>
        public void RequestDeferredUpdate(IMapModeHandler handler)
        {
            if (!isInitialized || handler == null) return;

            // Only process if this is the active handler
            if (currentHandler != handler) return;

            // Mark pending - will be processed in LateUpdate
            if (!hasPendingDeferredUpdate)
            {
                hasPendingDeferredUpdate = true;
                pendingDeferredHandler = handler;
            }
        }

        void LateUpdate()
        {
            // Process deferred update at end of frame (after all events have fired)
            if (hasPendingDeferredUpdate && pendingDeferredHandler != null)
            {
                if (gameState?.ProvinceQueries != null && gameState?.CountryQueries != null && provinceMapping != null)
                {
                    pendingDeferredHandler.UpdateTextures(dataTextures, gameState.ProvinceQueries, gameState.CountryQueries, provinceMapping, gameProvinceSystem);
                }

                hasPendingDeferredUpdate = false;
                pendingDeferredHandler = null;
            }
        }

        /// <summary>
        /// Get a specific map mode handler (for GAME layer to mark dirty)
        /// Returns null if handler not registered
        /// </summary>
        public IMapModeHandler GetHandler(MapMode mode)
        {
            if (modeHandlers == null) return null;
            return modeHandlers.TryGetValue(mode, out var handler) ? handler : null;
        }

        /// <summary>
        /// Update material reference when material is swapped
        /// Called by GAME layer (VisualStyleManager) when material changes
        /// </summary>
        public void UpdateMaterial(Material newMaterial)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogWarning("MapModeManager: Cannot update material - not initialized", "map_modes");
                return;
            }

            if (newMaterial == null)
            {
                ArchonLogger.LogError("MapModeManager: Cannot update to null material", "map_modes");
                return;
            }

            mapMaterial = newMaterial;

            // Rebind all textures to new material
            dataTextures.BindToMaterial(mapMaterial);

            // Re-apply current map mode to new material
            if (currentHandler != null)
            {
                currentHandler.OnActivate(mapMaterial, dataTextures);

                if (gameState?.ProvinceQueries != null && gameState?.CountryQueries != null && provinceMapping != null)
                {
                    currentHandler.UpdateTextures(dataTextures, gameState.ProvinceQueries, gameState.CountryQueries, provinceMapping, gameProvinceSystem);
                    dataTextures.BindToMaterial(mapMaterial);
                }
            }

            ArchonLogger.Log($"MapModeManager: Updated material reference and re-applied {currentMode} mode", "map_initialization");
        }

        /// <summary>
        /// Rebind all map mode textures to the material
        /// Call this after other systems rebind base textures to prevent losing map mode texture bindings
        /// </summary>
        public void RebindTextures()
        {
            if (!isInitialized || dataTextures == null || mapMaterial == null) return;

            dataTextures.BindToMaterial(mapMaterial);

            // Also rebind the texture array
            if (mapModeTextureArray != null)
            {
                mapMaterial.SetTexture(MapModeTextureArrayPropertyID, mapModeTextureArray);
            }

            ArchonLogger.Log($"MapModeManager: Rebound map mode textures to material (CountryColorPalette, etc.)", "map_initialization");
        }

        #region Custom Map Mode Texture Array API

        /// <summary>
        /// Register a custom map mode and get its array index.
        /// Called by GAME layer when creating custom map modes.
        /// </summary>
        /// <param name="customModeId">Unique ID for this custom mode (typically ShaderModeID from handler)</param>
        /// <returns>Array index for this mode's texture slice, or -1 if full</returns>
        public int RegisterCustomMapMode(int customModeId)
        {
            if (mapModeTextureArray == null)
            {
                ArchonLogger.LogError("MapModeManager: Texture array not initialized", "map_modes");
                return -1;
            }

            // Check if already registered
            if (customModeToArrayIndex.TryGetValue(customModeId, out int existingIndex))
            {
                return existingIndex;
            }

            // Check capacity
            if (nextArrayIndex >= MAX_CUSTOM_MAP_MODES)
            {
                ArchonLogger.LogError($"MapModeManager: Cannot register custom mode {customModeId} - texture array full ({MAX_CUSTOM_MAP_MODES} max)", "map_modes");
                return -1;
            }

            // Assign next slot
            int arrayIndex = nextArrayIndex++;
            customModeToArrayIndex[customModeId] = arrayIndex;

            mapMaterial.SetInt(MapModeTextureCountPropertyID, nextArrayIndex);

            ArchonLogger.Log($"MapModeManager: Registered custom map mode {customModeId} at array index {arrayIndex}", "map_initialization");
            return arrayIndex;
        }

        /// <summary>
        /// Update a custom map mode's texture data (CPU pixel array version).
        /// Called by GAME map mode handlers when their data changes (dirty).
        /// NOTE: This is SLOW due to GPU→CPU→GPU roundtrip. Prefer BindCustomMapModeRenderTexture().
        /// </summary>
        /// <param name="arrayIndex">Index returned from RegisterCustomMapMode</param>
        /// <param name="pixels">Color data for the entire map</param>
        public void UpdateCustomMapModeTexture(int arrayIndex, Color32[] pixels)
        {
            if (mapModeTextureArray == null || arrayIndex < 0 || arrayIndex >= MAX_CUSTOM_MAP_MODES)
            {
                ArchonLogger.LogError($"MapModeManager: Invalid array index {arrayIndex}", "map_modes");
                return;
            }

            if (pixels.Length != mapWidth * mapHeight)
            {
                ArchonLogger.LogError($"MapModeManager: Pixel array size mismatch. Expected {mapWidth * mapHeight}, got {pixels.Length}", "map_modes");
                return;
            }

            mapModeTextureArray.SetPixels32(pixels, arrayIndex);
            mapModeTextureArray.Apply();

            if (logTextureUpdates)
            {
                ArchonLogger.Log($"MapModeManager: Updated custom map mode texture at index {arrayIndex}", "map_modes");
            }
        }

        /// <summary>
        /// Copy a RenderTexture to the texture array using GPU-to-GPU copy.
        /// This is the FAST path - no CPU roundtrip, stays entirely on GPU.
        /// Called by GradientMapMode after compute shader dispatch.
        /// </summary>
        public void CopyRenderTextureToArray(int arrayIndex, RenderTexture sourceTexture)
        {
            if (mapModeTextureArray == null || sourceTexture == null) return;
            if (arrayIndex < 0 || arrayIndex >= MAX_CUSTOM_MAP_MODES) return;

            // GPU-to-GPU copy - no CPU sync needed
            // Graphics.CopyTexture copies directly on GPU, no ReadPixels/SetPixels
            Graphics.CopyTexture(sourceTexture, 0, 0, mapModeTextureArray, arrayIndex, 0);

            if (logTextureUpdates)
            {
                ArchonLogger.Log($"MapModeManager: GPU copied RenderTexture to array index {arrayIndex}", "map_modes");
            }
        }

        /// <summary>
        /// Get the dimensions of the map mode texture array.
        /// GAME map modes need this to create correctly sized pixel arrays.
        /// </summary>
        public (int width, int height) GetMapDimensions()
        {
            return (mapWidth, mapHeight);
        }

        /// <summary>
        /// Get the array index for a registered custom mode.
        /// Returns -1 if not registered.
        /// </summary>
        public int GetCustomModeArrayIndex(int customModeId)
        {
            return customModeToArrayIndex.TryGetValue(customModeId, out int index) ? index : -1;
        }

        #endregion

        void OnDestroy()
        {
            currentHandler?.OnDeactivate(mapMaterial);
            dataTextures?.Dispose();
            updateScheduler?.Dispose();

            // Dispose texture array
            if (mapModeTextureArray != null)
            {
                Object.Destroy(mapModeTextureArray);
                mapModeTextureArray = null;
            }

            // Dispose all map mode handlers to prevent ComputeBuffer leaks
            if (modeHandlers != null)
            {
                foreach (var handler in modeHandlers.Values)
                {
                    if (handler is System.IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                modeHandlers.Clear();
            }

            customModeToArrayIndex?.Clear();
        }
    }
}