using UnityEngine;
using System.Collections.Generic;
using Map.Rendering.Border;
using Map.Rendering.Highlight;

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

        // Future: FogOfWar, Terrain renderers
        // private Dictionary<string, IFogOfWarRenderer> fogOfWarRenderers;
        // private Dictionary<string, ITerrainRenderer> terrainRenderers;

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
            ArchonLogger.Log($"MapRendererRegistry initialized with {borderRenderers.Count} border renderer(s), {highlightRenderers.Count} highlight renderer(s)", "map_rendering");
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

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
