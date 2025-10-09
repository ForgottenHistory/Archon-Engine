using UnityEngine;
using System.Collections.Generic;
using Core;
using Core.Queries;
using Map.Rendering;
using Map.Core;

namespace Map.MapModes
{
    /// <summary>
    /// Central manager for the map mode system
    /// Handles mode switching, texture updates, and handler coordination
    /// Performance: <0.1ms mode switching, efficient texture scheduling
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
                ArchonLogger.LogWarning("MapModeManager: Already initialized, skipping");
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
                ArchonLogger.LogError("MapModeManager: GameState is null - cannot initialize");
                return;
            }

            if (mapMaterial == null)
            {
                ArchonLogger.LogError("MapModeManager: Material is null - cannot initialize");
                return;
            }

            if (provinceMapping == null)
            {
                ArchonLogger.LogError("MapModeManager: ProvinceMapping is null - cannot initialize");
                return;
            }

            // Initialize subsystems
            InitializeTextures();
            InitializeModeHandlers();
            InitializeUpdateScheduler();

            isInitialized = true;

            ArchonLogger.LogMapInit("MapModeManager initialized - ready for GAME to register handlers and set mode");
        }

        private void InitializeTextures()
        {
            // Get the existing MapTextureManager
            var textureManager = Object.FindFirstObjectByType<MapTextureManager>();
            if (textureManager == null)
            {
                ArchonLogger.LogError("MapModeManager: MapTextureManager not found");
                return;
            }

            dataTextures = new MapModeDataTextures();
            dataTextures.Initialize(textureManager);
            dataTextures.BindToMaterial(mapMaterial);
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

            ArchonLogger.LogMapInit($"MapModeManager: Registered handler for {mode} mode");
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
                ArchonLogger.LogError($"No handler for map mode: {mode}");
                return;
            }

            currentHandler?.OnDeactivate(mapMaterial);

            currentMode = mode;
            currentHandler = newHandler;

            // Set the active handler in the scheduler (architecture compliance)
            updateScheduler?.SetActiveHandler(currentHandler);

            currentHandler.OnActivate(mapMaterial, dataTextures);

            if (gameState?.ProvinceQueries != null && gameState?.CountryQueries != null && provinceMapping != null)
            {
                currentHandler.UpdateTextures(dataTextures, gameState.ProvinceQueries, gameState.CountryQueries, provinceMapping, gameProvinceSystem);

                // CRITICAL: Rebind textures to material after update to force GPU upload
                // Without this, Unity may use cached texture data from before the update
                dataTextures.BindToMaterial(mapMaterial);
            }

            // DEBUG: Verify which texture is actually bound to the material after update
            var boundPalette = mapMaterial.GetTexture(Shader.PropertyToID("_CountryColorPalette"));
            ArchonLogger.LogMapInit($"MapModeManager: After SetMapMode, material has CountryColorPalette instance {boundPalette?.GetInstanceID()} bound (dataTextures has instance {dataTextures?.CountryColorPalette?.GetInstanceID()})");
            if (boundPalette != null && dataTextures?.CountryColorPalette != null)
            {
                if (boundPalette.GetInstanceID() == dataTextures.CountryColorPalette.GetInstanceID())
                {
                    ArchonLogger.LogMapInit("MapModeManager: ✓ Material is bound to the CORRECT texture instance that we're updating");
                }
                else
                {
                    ArchonLogger.LogMapInitError($"MapModeManager: ✗ Material is bound to WRONG texture! Material has {boundPalette.GetInstanceID()}, but we're updating {dataTextures.CountryColorPalette.GetInstanceID()}");
                }
            }

            if (logModeChanges)
            {
                ArchonLogger.LogMapInit($"Switched to {currentMode} mode");
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
                ArchonLogger.LogMapInit($"MapModeManager: Forced texture update for {currentMode} mode");
            }
        }

        /// <summary>
        /// Update material reference when material is swapped
        /// Called by GAME layer (VisualStyleManager) when material changes
        /// </summary>
        public void UpdateMaterial(Material newMaterial)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogWarning("MapModeManager: Cannot update material - not initialized");
                return;
            }

            if (newMaterial == null)
            {
                ArchonLogger.LogError("MapModeManager: Cannot update to null material");
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

            ArchonLogger.LogMapInit($"MapModeManager: Updated material reference and re-applied {currentMode} mode");
        }

        /// <summary>
        /// Rebind all map mode textures to the material
        /// Call this after other systems rebind base textures to prevent losing map mode texture bindings
        /// </summary>
        public void RebindTextures()
        {
            if (!isInitialized || dataTextures == null || mapMaterial == null) return;

            dataTextures.BindToMaterial(mapMaterial);
            ArchonLogger.LogMapInit($"MapModeManager: Rebound map mode textures to material (CountryColorPalette, etc.)");
        }

        void OnDestroy()
        {
            currentHandler?.OnDeactivate(mapMaterial);
            dataTextures?.Dispose();
            updateScheduler?.Dispose();
        }
    }
}