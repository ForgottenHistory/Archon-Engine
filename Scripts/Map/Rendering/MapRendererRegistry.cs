using UnityEngine;
using System.Collections.Generic;
using Map.Rendering.Border;
using Map.Rendering.Highlight;
using Map.Rendering.FogOfWar;
using Map.Rendering.Terrain;
using Map.MapModes.Colorization;

namespace Map.Rendering
{
    /// <summary>
    /// ENGINE: Central registry for pluggable map rendering implementations.
    ///
    /// ENGINE provides default renderers (DistanceField, PixelPerfect, MeshGeometry).
    /// GAME can register custom implementations during initialization.
    ///
    /// Usage:
    /// 1. ENGINE registers defaults automatically on initialization
    /// 2. GAME registers custom renderers before map initialization:
    ///    MapRendererRegistry.Instance.RegisterBorderRenderer(new MyCustomBorderRenderer());
    /// 3. VisualStyleConfiguration references renderers by string ID
    ///
    /// Pattern follows IMapModeHandler registration approach.
    /// </summary>
    public class MapRendererRegistry : MonoBehaviour
    {
        public static MapRendererRegistry Instance { get; private set; }

        // Border Renderers
        private Dictionary<string, IBorderRenderer> borderRenderers = new Dictionary<string, IBorderRenderer>();
        private string defaultBorderRendererId = "DistanceField";

        // Highlight Renderers
        private Dictionary<string, IHighlightRenderer> highlightRenderers = new Dictionary<string, IHighlightRenderer>();
        private string defaultHighlightRendererId = "Default";

        // Fog of War Renderers
        private Dictionary<string, IFogOfWarRenderer> fogOfWarRenderers = new Dictionary<string, IFogOfWarRenderer>();
        private string defaultFogOfWarRendererId = "Default";

        // Terrain Renderers
        private Dictionary<string, ITerrainRenderer> terrainRenderers = new Dictionary<string, ITerrainRenderer>();
        private string defaultTerrainRendererId = "Default";

        // Map Mode Colorizers
        private Dictionary<string, IMapModeColorizer> mapModeColorizers = new Dictionary<string, IMapModeColorizer>();
        private string defaultMapModeColorizerId = "Gradient";

        public bool IsInitialized { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                ArchonLogger.LogWarning("Multiple MapRendererRegistry instances detected. Destroying duplicate.", "map_rendering");
                Destroy(this);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// Initialize the registry. Called by MapInitializer after ENGINE default renderers are created.
        /// </summary>
        public void Initialize()
        {
            if (IsInitialized)
            {
                ArchonLogger.LogWarning("MapRendererRegistry already initialized", "map_rendering");
                return;
            }

            IsInitialized = true;
            ArchonLogger.Log($"MapRendererRegistry initialized with {borderRenderers.Count} border, {highlightRenderers.Count} highlight, {fogOfWarRenderers.Count} fog, {terrainRenderers.Count} terrain, {mapModeColorizers.Count} colorizer(s)", "map_rendering");
        }

        #region Border Renderers

        /// <summary>
        /// Register a border renderer implementation.
        /// ENGINE registers defaults; GAME can register customs.
        /// </summary>
        public void RegisterBorderRenderer(IBorderRenderer renderer)
        {
            if (renderer == null)
            {
                ArchonLogger.LogWarning("Attempted to register null border renderer", "map_rendering");
                return;
            }

            string id = renderer.RendererId;
            if (borderRenderers.ContainsKey(id))
            {
                ArchonLogger.LogWarning($"Border renderer '{id}' already registered. Replacing.", "map_rendering");
            }

            borderRenderers[id] = renderer;
            ArchonLogger.Log($"Registered border renderer: {id} ({renderer.DisplayName})", "map_rendering");
        }

        /// <summary>
        /// Unregister a border renderer.
        /// </summary>
        public bool UnregisterBorderRenderer(string rendererId)
        {
            if (borderRenderers.TryGetValue(rendererId, out var renderer))
            {
                renderer.Dispose();
                borderRenderers.Remove(rendererId);
                ArchonLogger.Log($"Unregistered border renderer: {rendererId}", "map_rendering");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get a border renderer by ID.
        /// Returns null if not found.
        /// </summary>
        public IBorderRenderer GetBorderRenderer(string rendererId)
        {
            if (string.IsNullOrEmpty(rendererId))
            {
                rendererId = defaultBorderRendererId;
            }

            if (borderRenderers.TryGetValue(rendererId, out var renderer))
            {
                return renderer;
            }

            ArchonLogger.LogWarning($"Border renderer '{rendererId}' not found. Available: {string.Join(", ", borderRenderers.Keys)}", "map_rendering");
            return null;
        }

        /// <summary>
        /// Get the default border renderer.
        /// </summary>
        public IBorderRenderer GetDefaultBorderRenderer()
        {
            return GetBorderRenderer(defaultBorderRendererId);
        }

        /// <summary>
        /// Set which renderer ID is the default.
        /// </summary>
        public void SetDefaultBorderRenderer(string rendererId)
        {
            if (borderRenderers.ContainsKey(rendererId))
            {
                defaultBorderRendererId = rendererId;
            }
            else
            {
                ArchonLogger.LogWarning($"Cannot set default to unknown renderer: {rendererId}", "map_rendering");
            }
        }

        /// <summary>
        /// Get all available border renderer IDs.
        /// </summary>
        public IEnumerable<string> GetAvailableBorderRenderers()
        {
            return borderRenderers.Keys;
        }

        /// <summary>
        /// Check if a border renderer is registered.
        /// </summary>
        public bool HasBorderRenderer(string rendererId)
        {
            return borderRenderers.ContainsKey(rendererId);
        }

        /// <summary>
        /// Map legacy BorderRenderingMode enum to renderer ID.
        /// Provides backwards compatibility with existing configurations.
        /// </summary>
        public static string MapBorderModeToRendererId(BorderRenderingMode mode)
        {
            return mode switch
            {
                BorderRenderingMode.None => "None",
                BorderRenderingMode.ShaderDistanceField => "DistanceField",
                BorderRenderingMode.ShaderPixelPerfect => "PixelPerfect",
                BorderRenderingMode.MeshGeometry => "MeshGeometry",
                _ => "DistanceField"
            };
        }

        #endregion

        #region Highlight Renderers

        /// <summary>
        /// Register a highlight renderer implementation.
        /// ENGINE registers defaults; GAME can register customs.
        /// </summary>
        public void RegisterHighlightRenderer(IHighlightRenderer renderer)
        {
            if (renderer == null)
            {
                ArchonLogger.LogWarning("Attempted to register null highlight renderer", "map_rendering");
                return;
            }

            string id = renderer.RendererId;
            if (highlightRenderers.ContainsKey(id))
            {
                ArchonLogger.LogWarning($"Highlight renderer '{id}' already registered. Replacing.", "map_rendering");
            }

            highlightRenderers[id] = renderer;
            ArchonLogger.Log($"Registered highlight renderer: {id} ({renderer.DisplayName})", "map_rendering");
        }

        /// <summary>
        /// Unregister a highlight renderer.
        /// </summary>
        public bool UnregisterHighlightRenderer(string rendererId)
        {
            if (highlightRenderers.TryGetValue(rendererId, out var renderer))
            {
                renderer.Dispose();
                highlightRenderers.Remove(rendererId);
                ArchonLogger.Log($"Unregistered highlight renderer: {rendererId}", "map_rendering");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get a highlight renderer by ID.
        /// Returns null if not found.
        /// </summary>
        public IHighlightRenderer GetHighlightRenderer(string rendererId)
        {
            if (string.IsNullOrEmpty(rendererId))
            {
                rendererId = defaultHighlightRendererId;
            }

            if (highlightRenderers.TryGetValue(rendererId, out var renderer))
            {
                return renderer;
            }

            ArchonLogger.LogWarning($"Highlight renderer '{rendererId}' not found. Available: {string.Join(", ", highlightRenderers.Keys)}", "map_rendering");
            return null;
        }

        /// <summary>
        /// Get the default highlight renderer.
        /// </summary>
        public IHighlightRenderer GetDefaultHighlightRenderer()
        {
            return GetHighlightRenderer(defaultHighlightRendererId);
        }

        /// <summary>
        /// Set which highlight renderer ID is the default.
        /// </summary>
        public void SetDefaultHighlightRenderer(string rendererId)
        {
            if (highlightRenderers.ContainsKey(rendererId))
            {
                defaultHighlightRendererId = rendererId;
            }
            else
            {
                ArchonLogger.LogWarning($"Cannot set default highlight to unknown renderer: {rendererId}", "map_rendering");
            }
        }

        /// <summary>
        /// Get all available highlight renderer IDs.
        /// </summary>
        public IEnumerable<string> GetAvailableHighlightRenderers()
        {
            return highlightRenderers.Keys;
        }

        /// <summary>
        /// Check if a highlight renderer is registered.
        /// </summary>
        public bool HasHighlightRenderer(string rendererId)
        {
            return highlightRenderers.ContainsKey(rendererId);
        }

        #endregion

        #region Fog of War Renderers

        /// <summary>
        /// Register a fog of war renderer implementation.
        /// ENGINE registers defaults; GAME can register customs.
        /// </summary>
        public void RegisterFogOfWarRenderer(IFogOfWarRenderer renderer)
        {
            if (renderer == null)
            {
                ArchonLogger.LogWarning("Attempted to register null fog of war renderer", "map_rendering");
                return;
            }

            string id = renderer.RendererId;
            if (fogOfWarRenderers.ContainsKey(id))
            {
                ArchonLogger.LogWarning($"Fog of war renderer '{id}' already registered. Replacing.", "map_rendering");
            }

            fogOfWarRenderers[id] = renderer;
            ArchonLogger.Log($"Registered fog of war renderer: {id} ({renderer.DisplayName})", "map_rendering");
        }

        /// <summary>
        /// Unregister a fog of war renderer.
        /// </summary>
        public bool UnregisterFogOfWarRenderer(string rendererId)
        {
            if (fogOfWarRenderers.TryGetValue(rendererId, out var renderer))
            {
                renderer.Dispose();
                fogOfWarRenderers.Remove(rendererId);
                ArchonLogger.Log($"Unregistered fog of war renderer: {rendererId}", "map_rendering");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get a fog of war renderer by ID.
        /// Returns null if not found.
        /// </summary>
        public IFogOfWarRenderer GetFogOfWarRenderer(string rendererId)
        {
            if (string.IsNullOrEmpty(rendererId))
            {
                rendererId = defaultFogOfWarRendererId;
            }

            if (fogOfWarRenderers.TryGetValue(rendererId, out var renderer))
            {
                return renderer;
            }

            ArchonLogger.LogWarning($"Fog of war renderer '{rendererId}' not found. Available: {string.Join(", ", fogOfWarRenderers.Keys)}", "map_rendering");
            return null;
        }

        /// <summary>
        /// Get the default fog of war renderer.
        /// </summary>
        public IFogOfWarRenderer GetDefaultFogOfWarRenderer()
        {
            return GetFogOfWarRenderer(defaultFogOfWarRendererId);
        }

        /// <summary>
        /// Set which fog of war renderer ID is the default.
        /// </summary>
        public void SetDefaultFogOfWarRenderer(string rendererId)
        {
            if (fogOfWarRenderers.ContainsKey(rendererId))
            {
                defaultFogOfWarRendererId = rendererId;
            }
            else
            {
                ArchonLogger.LogWarning($"Cannot set default fog to unknown renderer: {rendererId}", "map_rendering");
            }
        }

        /// <summary>
        /// Get all available fog of war renderer IDs.
        /// </summary>
        public IEnumerable<string> GetAvailableFogOfWarRenderers()
        {
            return fogOfWarRenderers.Keys;
        }

        /// <summary>
        /// Check if a fog of war renderer is registered.
        /// </summary>
        public bool HasFogOfWarRenderer(string rendererId)
        {
            return fogOfWarRenderers.ContainsKey(rendererId);
        }

        #endregion

        #region Terrain Renderers

        /// <summary>
        /// Register a terrain renderer implementation.
        /// ENGINE registers defaults; GAME can register customs.
        /// </summary>
        public void RegisterTerrainRenderer(ITerrainRenderer renderer)
        {
            if (renderer == null)
            {
                ArchonLogger.LogWarning("Attempted to register null terrain renderer", "map_rendering");
                return;
            }

            string id = renderer.RendererId;
            if (terrainRenderers.ContainsKey(id))
            {
                ArchonLogger.LogWarning($"Terrain renderer '{id}' already registered. Replacing.", "map_rendering");
            }

            terrainRenderers[id] = renderer;
            ArchonLogger.Log($"Registered terrain renderer: {id} ({renderer.DisplayName})", "map_rendering");
        }

        /// <summary>
        /// Unregister a terrain renderer.
        /// </summary>
        public bool UnregisterTerrainRenderer(string rendererId)
        {
            if (terrainRenderers.TryGetValue(rendererId, out var renderer))
            {
                renderer.Dispose();
                terrainRenderers.Remove(rendererId);
                ArchonLogger.Log($"Unregistered terrain renderer: {rendererId}", "map_rendering");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get a terrain renderer by ID.
        /// Returns null if not found.
        /// </summary>
        public ITerrainRenderer GetTerrainRenderer(string rendererId)
        {
            if (string.IsNullOrEmpty(rendererId))
            {
                rendererId = defaultTerrainRendererId;
            }

            if (terrainRenderers.TryGetValue(rendererId, out var renderer))
            {
                return renderer;
            }

            ArchonLogger.LogWarning($"Terrain renderer '{rendererId}' not found. Available: {string.Join(", ", terrainRenderers.Keys)}", "map_rendering");
            return null;
        }

        /// <summary>
        /// Get the default terrain renderer.
        /// </summary>
        public ITerrainRenderer GetDefaultTerrainRenderer()
        {
            return GetTerrainRenderer(defaultTerrainRendererId);
        }

        /// <summary>
        /// Set which terrain renderer ID is the default.
        /// </summary>
        public void SetDefaultTerrainRenderer(string rendererId)
        {
            if (terrainRenderers.ContainsKey(rendererId))
            {
                defaultTerrainRendererId = rendererId;
            }
            else
            {
                ArchonLogger.LogWarning($"Cannot set default terrain to unknown renderer: {rendererId}", "map_rendering");
            }
        }

        /// <summary>
        /// Get all available terrain renderer IDs.
        /// </summary>
        public IEnumerable<string> GetAvailableTerrainRenderers()
        {
            return terrainRenderers.Keys;
        }

        /// <summary>
        /// Check if a terrain renderer is registered.
        /// </summary>
        public bool HasTerrainRenderer(string rendererId)
        {
            return terrainRenderers.ContainsKey(rendererId);
        }

        #endregion

        #region Map Mode Colorizers

        /// <summary>
        /// Register a map mode colorizer implementation.
        /// ENGINE registers defaults; GAME can register customs.
        /// </summary>
        public void RegisterMapModeColorizer(IMapModeColorizer colorizer)
        {
            if (colorizer == null)
            {
                ArchonLogger.LogWarning("Attempted to register null map mode colorizer", "map_modes");
                return;
            }

            string id = colorizer.ColorizerId;
            if (mapModeColorizers.ContainsKey(id))
            {
                ArchonLogger.LogWarning($"Map mode colorizer '{id}' already registered. Replacing.", "map_modes");
            }

            mapModeColorizers[id] = colorizer;
            ArchonLogger.Log($"Registered map mode colorizer: {id} ({colorizer.DisplayName})", "map_modes");
        }

        /// <summary>
        /// Unregister a map mode colorizer.
        /// </summary>
        public bool UnregisterMapModeColorizer(string colorizerId)
        {
            if (mapModeColorizers.TryGetValue(colorizerId, out var colorizer))
            {
                colorizer.Dispose();
                mapModeColorizers.Remove(colorizerId);
                ArchonLogger.Log($"Unregistered map mode colorizer: {colorizerId}", "map_modes");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get a map mode colorizer by ID.
        /// Returns null if not found.
        /// </summary>
        public IMapModeColorizer GetMapModeColorizer(string colorizerId)
        {
            if (string.IsNullOrEmpty(colorizerId))
            {
                colorizerId = defaultMapModeColorizerId;
            }

            if (mapModeColorizers.TryGetValue(colorizerId, out var colorizer))
            {
                return colorizer;
            }

            ArchonLogger.LogWarning($"Map mode colorizer '{colorizerId}' not found. Available: {string.Join(", ", mapModeColorizers.Keys)}", "map_modes");
            return null;
        }

        /// <summary>
        /// Get the default map mode colorizer.
        /// </summary>
        public IMapModeColorizer GetDefaultMapModeColorizer()
        {
            return GetMapModeColorizer(defaultMapModeColorizerId);
        }

        /// <summary>
        /// Set which colorizer ID is the default.
        /// </summary>
        public void SetDefaultMapModeColorizer(string colorizerId)
        {
            if (mapModeColorizers.ContainsKey(colorizerId))
            {
                defaultMapModeColorizerId = colorizerId;
            }
            else
            {
                ArchonLogger.LogWarning($"Cannot set default colorizer to unknown: {colorizerId}", "map_modes");
            }
        }

        /// <summary>
        /// Get all available map mode colorizer IDs.
        /// </summary>
        public IEnumerable<string> GetAvailableMapModeColorizers()
        {
            return mapModeColorizers.Keys;
        }

        /// <summary>
        /// Check if a map mode colorizer is registered.
        /// </summary>
        public bool HasMapModeColorizer(string colorizerId)
        {
            return mapModeColorizers.ContainsKey(colorizerId);
        }

        #endregion

        void OnDestroy()
        {
            // Dispose all registered border renderers
            foreach (var renderer in borderRenderers.Values)
            {
                renderer?.Dispose();
            }
            borderRenderers.Clear();

            // Dispose all registered highlight renderers
            foreach (var renderer in highlightRenderers.Values)
            {
                renderer?.Dispose();
            }
            highlightRenderers.Clear();

            // Dispose all registered fog of war renderers
            foreach (var renderer in fogOfWarRenderers.Values)
            {
                renderer?.Dispose();
            }
            fogOfWarRenderers.Clear();

            // Dispose all registered terrain renderers
            foreach (var renderer in terrainRenderers.Values)
            {
                renderer?.Dispose();
            }
            terrainRenderers.Clear();

            // Dispose all registered map mode colorizers
            foreach (var colorizer in mapModeColorizers.Values)
            {
                colorizer?.Dispose();
            }
            mapModeColorizers.Clear();

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
