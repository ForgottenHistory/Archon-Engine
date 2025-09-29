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
            // Don't auto-initialize - wait for explicit initialization
            // This prevents timing issues with material availability
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
        /// Initialize the map mode system - call after MapRenderingCoordinator is set up
        /// </summary>
        public void Initialize()
        {
            InitializeSystem();
        }

        private void InitializeSystem()
        {
            if (isInitialized) return;

            gameState = Object.FindFirstObjectByType<GameState>();
            if (gameState == null || !gameState.IsInitialized)
            {
                Invoke(nameof(InitializeSystem), 0.5f);
                return;
            }

            // Get material from MapRenderingCoordinator if not assigned
            if (mapMaterial == null)
            {
                var renderingCoordinator = Object.FindFirstObjectByType<MapRenderingCoordinator>();
                if (renderingCoordinator != null)
                {
                    mapMaterial = renderingCoordinator.MapMaterial;
                }
            }

            if (mapMaterial == null)
            {
                DominionLogger.LogError("MapModeManager: Map material not found - ensure MapRenderingCoordinator is initialized first");
                Invoke(nameof(InitializeSystem), 0.5f);
                return;
            }

            // Get ProvinceMapping from MapSystemCoordinator
            var mapSystemCoordinator = Object.FindFirstObjectByType<MapSystemCoordinator>();
            if (mapSystemCoordinator?.ProvinceMapping == null)
            {
                DominionLogger.LogError("MapModeManager: ProvinceMapping not found - ensure MapSystemCoordinator is initialized first");
                Invoke(nameof(InitializeSystem), 0.5f);
                return;
            }
            provinceMapping = mapSystemCoordinator.ProvinceMapping;

            InitializeTextures();
            InitializeModeHandlers();
            InitializeUpdateScheduler();
            SetMapMode(currentMode, forceUpdate: true);

            isInitialized = true;
            DominionLogger.Log("MapModeManager initialized");
        }

        private void InitializeTextures()
        {
            // Get the existing MapTextureManager
            var textureManager = Object.FindFirstObjectByType<MapTextureManager>();
            if (textureManager == null)
            {
                DominionLogger.LogError("MapModeManager: MapTextureManager not found");
                return;
            }

            dataTextures = new MapModeDataTextures();
            dataTextures.Initialize(textureManager);
            dataTextures.BindToMaterial(mapMaterial);
        }

        private void InitializeModeHandlers()
        {
            modeHandlers = new Dictionary<MapMode, IMapModeHandler>
            {
                { MapMode.Political, new PoliticalMapMode() },
                { MapMode.Terrain, new TerrainMapMode() },
                { MapMode.Development, new DevelopmentMapMode() }
                // TODO: Add other handlers as we implement them
            };
        }

        private void InitializeUpdateScheduler()
        {
            updateScheduler = new TextureUpdateScheduler();

            foreach (var handler in modeHandlers.Values)
            {
                updateScheduler.RegisterHandler(handler, handler.GetUpdateFrequency(), (h) =>
                {
                    if (gameState?.ProvinceQueries != null && gameState?.CountryQueries != null && provinceMapping != null)
                    {
                        h.UpdateTextures(dataTextures, gameState.ProvinceQueries, gameState.CountryQueries, provinceMapping);
                    }
                });
            }
        }

        public void SetMapMode(MapMode mode, bool forceUpdate = false)
        {
            if (!isInitialized || (currentMode == mode && !forceUpdate)) return;

            if (!modeHandlers.TryGetValue(mode, out var newHandler))
            {
                DominionLogger.LogError($"No handler for map mode: {mode}");
                return;
            }

            currentHandler?.OnDeactivate(mapMaterial);

            currentMode = mode;
            currentHandler = newHandler;

            currentHandler.OnActivate(mapMaterial, dataTextures);

            if (gameState?.ProvinceQueries != null && gameState?.CountryQueries != null && provinceMapping != null)
            {
                currentHandler.UpdateTextures(dataTextures, gameState.ProvinceQueries, gameState.CountryQueries, provinceMapping);
            }

            if (logModeChanges)
            {
                DominionLogger.Log($"Switched to {currentMode} mode");
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
                currentHandler.UpdateTextures(dataTextures, gameState.ProvinceQueries, gameState.CountryQueries, provinceMapping);
                DominionLogger.Log($"MapModeManager: Forced texture update for {currentMode} mode");
            }
        }

        void OnDestroy()
        {
            currentHandler?.OnDeactivate(mapMaterial);
            dataTextures?.Dispose();
            updateScheduler?.Dispose();
        }
    }
}