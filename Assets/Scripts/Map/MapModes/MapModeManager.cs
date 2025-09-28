using System.Collections.Generic;
using UnityEngine;
using Map.Rendering;
using Utils;

namespace Map.MapModes
{
    /// <summary>
    /// Manages all map display modes and handles switching between them
    /// Integrates with the texture-based map system following dual-layer architecture
    /// </summary>
    public class MapModeManager : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool logMapModeChanges = true;
        [SerializeField] private int defaultMapModeID = 0; // Political by default

        // Core components
        private MapTextureManager textureManager;
        private Material mapMaterial;

        // Mapmode management
        private Dictionary<int, MapMode> registeredMapModes = new Dictionary<int, MapMode>();
        private MapMode currentMapMode;
        private int currentMapModeID = -1;

        // Performance tracking
        private bool needsTextureUpdate = false;

        /// <summary>
        /// Current active mapmode
        /// </summary>
        public MapMode CurrentMapMode => currentMapMode;

        /// <summary>
        /// Current mapmode ID
        /// </summary>
        public int CurrentMapModeID => currentMapModeID;

        /// <summary>
        /// All registered mapmodes
        /// </summary>
        public IReadOnlyDictionary<int, MapMode> RegisteredMapModes => registeredMapModes;

        public void Initialize(MapTextureManager textureManager, Material mapMaterial)
        {
            this.textureManager = textureManager;
            this.mapMaterial = mapMaterial;

            // Register built-in mapmodes
            RegisterBuiltInMapModes();

            // Set default mapmode
            SetMapMode(defaultMapModeID);

            if (logMapModeChanges)
            {
                DominionLogger.Log($"MapModeManager: Initialized with {registeredMapModes.Count} mapmodes");
            }
        }

        /// <summary>
        /// Register a new mapmode
        /// </summary>
        public void RegisterMapMode(int modeID, MapMode mapMode)
        {
            if (mapMode == null)
            {
                DominionLogger.LogError($"MapModeManager: Cannot register null mapmode for ID {modeID}");
                return;
            }

            if (registeredMapModes.ContainsKey(modeID))
            {
                DominionLogger.LogWarning($"MapModeManager: Overriding existing mapmode {modeID}");
            }

            registeredMapModes[modeID] = mapMode;

            if (logMapModeChanges)
            {
                DominionLogger.Log($"MapModeManager: Registered {mapMode.Name} (ID: {modeID})");
            }
        }

        /// <summary>
        /// Switch to a different mapmode
        /// </summary>
        public bool SetMapMode(int modeID)
        {
            if (!registeredMapModes.TryGetValue(modeID, out var newMapMode))
            {
                DominionLogger.LogError($"MapModeManager: Unknown mapmode ID {modeID}");
                return false;
            }

            // No change needed
            if (currentMapModeID == modeID && currentMapMode == newMapMode)
            {
                return true;
            }

            // Deactivate current mapmode
            if (currentMapMode != null)
            {
                currentMapMode.OnDeactivate();
            }

            // Switch to new mapmode
            var previousMode = currentMapMode;
            currentMapMode = newMapMode;
            currentMapModeID = modeID;

            // Activate new mapmode
            currentMapMode.OnActivate();

            // Update textures and shader
            RefreshCurrentMapMode();

            if (logMapModeChanges)
            {
                var previousName = previousMode?.Name ?? "None";
                DominionLogger.Log($"MapModeManager: Switched from {previousName} to {currentMapMode.Name}");
            }

            return true;
        }

        /// <summary>
        /// Refresh the current mapmode (update textures and shader)
        /// Call this when simulation data changes
        /// </summary>
        public void RefreshCurrentMapMode()
        {
            if (currentMapMode == null || textureManager == null || mapMaterial == null)
            {
                return;
            }

            // Update GPU textures from simulation data
            currentMapMode.UpdateGPUTextures(textureManager);

            // Apply shader settings
            currentMapMode.ApplyShaderSettings(mapMaterial);

            needsTextureUpdate = false;
        }

        /// <summary>
        /// Mark that textures need updating (call when simulation changes)
        /// </summary>
        public void MarkForUpdate()
        {
            needsTextureUpdate = true;
        }

        void Update()
        {
            // Update dynamic mapmodes that require frequent refreshes
            if (currentMapMode != null && currentMapMode.RequiresFrequentUpdates)
            {
                needsTextureUpdate = true;
            }

            // Refresh if needed
            if (needsTextureUpdate)
            {
                RefreshCurrentMapMode();
            }
        }

        /// <summary>
        /// Register built-in mapmodes
        /// </summary>
        private void RegisterBuiltInMapModes()
        {
            // Political mapmode (ID 0) - shows political borders
            RegisterMapMode(0, new PoliticalMapMode());

            // Terrain mapmode (ID 1) - shows terrain types
            RegisterMapMode(1, new TerrainMapMode());

            // Development mapmode (ID 2) - shows province development
            RegisterMapMode(2, new DevelopmentMapMode());

            // Culture mapmode (ID 3) - shows culture groups
            RegisterMapMode(3, new CultureMapMode());

            // Country mapmode (ID 4) - shows country colors
            RegisterMapMode(4, new CountryMapMode());

            // Debug mapmodes (high IDs to avoid conflicts)
            RegisterMapMode(10, new BorderDebugMapMode());
            RegisterMapMode(99, new DebugMapMode());

            if (logMapModeChanges)
            {
                DominionLogger.Log($"MapModeManager: Registered {registeredMapModes.Count} built-in mapmodes");
            }
        }

        /// <summary>
        /// Get available mapmode IDs and names for UI
        /// </summary>
        public Dictionary<int, string> GetAvailableMapModes()
        {
            var result = new Dictionary<int, string>();
            foreach (var kvp in registeredMapModes)
            {
                result[kvp.Key] = kvp.Value.Name;
            }
            return result;
        }

        void OnDestroy()
        {
            // Clean up current mapmode
            if (currentMapMode != null)
            {
                currentMapMode.OnDeactivate();
            }
        }
    }
}