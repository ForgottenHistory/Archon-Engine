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

        // Province palette texture for GAME-defined map modes
        // Layout: 256 columns x (rowsPerMode * numModes) rows
        // Each province ID maps to: x = ID % 256, y = (ID / 256) + (modeIndex * rowsPerMode)
        // Memory: 100k provinces * 16 modes * 4 bytes = ~6.4MB (vs 6.24GB for full-res textures)
        private const int MAX_CUSTOM_MAP_MODES = 16;
        private const int PALETTE_WIDTH = 256;
        private Texture2D provincePaletteTexture;
        private Dictionary<int, int> customModeToArrayIndex; // Maps custom mode ID to palette row offset
        private int nextArrayIndex = 0;
        private int maxProvinceID;
        private int rowsPerMode;

        // Shader property IDs (cached for performance)
        private static readonly int MapModePropertyID = Shader.PropertyToID("_MapMode");
        private static readonly int CustomMapModeIndexPropertyID = Shader.PropertyToID("_CustomMapModeIndex");
        private static readonly int ProvincePaletteTexturePropertyID = Shader.PropertyToID("_ProvincePaletteTexture");
        private static readonly int MapModeTextureCountPropertyID = Shader.PropertyToID("_MapModeTextureCount");
        private static readonly int MaxProvinceIDPropertyID = Shader.PropertyToID("_MaxProvinceID");

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
        /// Initialize the province palette texture for custom GAME map modes.
        /// Uses province-indexed lookup instead of full-resolution textures.
        /// Memory: 100k provinces * 16 modes * 4 bytes = ~6.4MB
        /// </summary>
        private void InitializeMapModeTextureArray(MapTextureManager textureManager)
        {
            // Get max province ID from GameState
            maxProvinceID = gameState?.Provinces?.ProvinceCount ?? 65536;
            if (maxProvinceID <= 0) maxProvinceID = 65536;

            // Calculate palette dimensions
            // Layout: 256 columns x (rowsPerMode * numModes) rows
            rowsPerMode = (maxProvinceID + PALETTE_WIDTH - 1) / PALETTE_WIDTH; // ceil division
            int paletteHeight = rowsPerMode * MAX_CUSTOM_MAP_MODES;

            // Create palette texture
            provincePaletteTexture = new Texture2D(PALETTE_WIDTH, paletteHeight, TextureFormat.RGBA32, false);
            provincePaletteTexture.filterMode = FilterMode.Point; // No interpolation - exact province colors
            provincePaletteTexture.wrapMode = TextureWrapMode.Clamp;
            provincePaletteTexture.name = "ProvincePaletteTexture";

            // Initialize with transparent black (alpha=0 means "use default/ocean")
            var clearPixels = new Color32[PALETTE_WIDTH * paletteHeight];
            for (int i = 0; i < clearPixels.Length; i++)
            {
                clearPixels[i] = new Color32(0, 0, 0, 0);
            }
            provincePaletteTexture.SetPixels32(clearPixels);
            provincePaletteTexture.Apply();

            // Initialize custom mode tracking
            customModeToArrayIndex = new Dictionary<int, int>();
            nextArrayIndex = 0;

            // Bind to material
            mapMaterial.SetTexture(ProvincePaletteTexturePropertyID, provincePaletteTexture);
            mapMaterial.SetInt(MapModeTextureCountPropertyID, 0);
            mapMaterial.SetInt(MaxProvinceIDPropertyID, maxProvinceID);

            int memoryKB = (PALETTE_WIDTH * paletteHeight * 4) / 1024;
            ArchonLogger.Log($"MapModeManager: Initialized province palette ({PALETTE_WIDTH}x{paletteHeight}, {MAX_CUSTOM_MAP_MODES} modes, {memoryKB}KB)", "map_initialization");
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

            // Also rebind the province palette
            if (provincePaletteTexture != null)
            {
                mapMaterial.SetTexture(ProvincePaletteTexturePropertyID, provincePaletteTexture);
                mapMaterial.SetInt(MaxProvinceIDPropertyID, maxProvinceID);
            }

            ArchonLogger.Log($"MapModeManager: Rebound map mode textures to material", "map_initialization");
        }

        #region Custom Map Mode Texture Array API

        /// <summary>
        /// Register a custom map mode and get its array index.
        /// Called by GAME layer when creating custom map modes.
        /// </summary>
        /// <param name="customModeId">Unique ID for this custom mode (typically ShaderModeID from handler)</param>
        /// <returns>Palette index for this mode, or -1 if full</returns>
        public int RegisterCustomMapMode(int customModeId)
        {
            if (provincePaletteTexture == null)
            {
                ArchonLogger.LogError("MapModeManager: Province palette not initialized", "map_modes");
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
                ArchonLogger.LogError($"MapModeManager: Cannot register custom mode {customModeId} - palette full ({MAX_CUSTOM_MAP_MODES} max)", "map_modes");
                return -1;
            }

            // Assign next slot
            int paletteIndex = nextArrayIndex++;
            customModeToArrayIndex[customModeId] = paletteIndex;

            mapMaterial.SetInt(MapModeTextureCountPropertyID, nextArrayIndex);

            ArchonLogger.Log($"MapModeManager: Registered custom map mode {customModeId} at palette index {paletteIndex}", "map_initialization");
            return paletteIndex;
        }

        /// <summary>
        /// Update province colors for a custom map mode.
        /// Called by GAME map mode handlers when their data changes.
        /// </summary>
        /// <param name="paletteIndex">Index returned from RegisterCustomMapMode</param>
        /// <param name="provinceColors">Dictionary mapping province ID to color</param>
        public void UpdateProvinceColors(int paletteIndex, Dictionary<int, Color32> provinceColors)
        {
            if (provincePaletteTexture == null || paletteIndex < 0 || paletteIndex >= MAX_CUSTOM_MAP_MODES)
            {
                ArchonLogger.LogError($"MapModeManager: Invalid palette index {paletteIndex}", "map_modes");
                return;
            }

            // Calculate row offset for this mode
            int rowOffset = paletteIndex * rowsPerMode;

            // Update each province color in the palette
            foreach (var kvp in provinceColors)
            {
                int provinceID = kvp.Key;
                if (provinceID <= 0 || provinceID > maxProvinceID) continue;

                int col = provinceID % PALETTE_WIDTH;
                int row = (provinceID / PALETTE_WIDTH) + rowOffset;

                provincePaletteTexture.SetPixel(col, row, kvp.Value);
            }

            provincePaletteTexture.Apply();

            if (logTextureUpdates)
            {
                ArchonLogger.Log($"MapModeManager: Updated {provinceColors.Count} province colors at palette index {paletteIndex}", "map_modes");
            }
        }

        /// <summary>
        /// Update a single province's color for a custom map mode.
        /// Use UpdateProvinceColors for batch updates.
        /// </summary>
        public void UpdateProvinceColor(int paletteIndex, int provinceID, Color32 color)
        {
            if (provincePaletteTexture == null || paletteIndex < 0 || paletteIndex >= MAX_CUSTOM_MAP_MODES)
                return;
            if (provinceID <= 0 || provinceID > maxProvinceID) return;

            int rowOffset = paletteIndex * rowsPerMode;
            int col = provinceID % PALETTE_WIDTH;
            int row = (provinceID / PALETTE_WIDTH) + rowOffset;

            provincePaletteTexture.SetPixel(col, row, color);
            // Note: Call ApplyPaletteChanges() after batch of single updates
        }

        /// <summary>
        /// Apply pending palette changes to GPU.
        /// Call after a batch of UpdateProvinceColor calls.
        /// </summary>
        public void ApplyPaletteChanges()
        {
            provincePaletteTexture?.Apply();
        }

        /// <summary>
        /// Get palette dimensions and province limit info.
        /// GAME map modes need this to know capacity.
        /// </summary>
        public (int maxProvinces, int maxModes, int rowsPerMode) GetPaletteInfo()
        {
            return (maxProvinceID, MAX_CUSTOM_MAP_MODES, rowsPerMode);
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

            // Dispose province palette texture
            if (provincePaletteTexture != null)
            {
                Object.Destroy(provincePaletteTexture);
                provincePaletteTexture = null;
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