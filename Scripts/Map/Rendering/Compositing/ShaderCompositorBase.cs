using UnityEngine;

namespace Map.Rendering.Compositing
{
    /// <summary>
    /// ENGINE: Abstract base class for shader compositors.
    /// Provides common utilities for configuring material properties.
    ///
    /// Subclasses implement:
    /// - CompositorId, DisplayName - identification
    /// - GetConfig() - return compositing configuration
    /// - ConfigureCompositor() - custom material setup
    /// </summary>
    public abstract class ShaderCompositorBase : IShaderCompositor
    {
        // Context from initialization
        protected CompositorContext context;
        protected bool isInitialized = false;

        // Cached shader property IDs
        protected static readonly int EnableBaseColorId = Shader.PropertyToID("_EnableBaseColor");
        protected static readonly int EnableLightingId = Shader.PropertyToID("_EnableLighting");
        protected static readonly int EnableBordersId = Shader.PropertyToID("_EnableBorders");
        protected static readonly int EnableHighlightsId = Shader.PropertyToID("_EnableHighlights");
        protected static readonly int EnableFogOfWarId = Shader.PropertyToID("_EnableFogOfWar");
        protected static readonly int EnableOverlayId = Shader.PropertyToID("_EnableOverlay");

        protected static readonly int BorderBlendModeId = Shader.PropertyToID("_BorderBlendMode");
        protected static readonly int HighlightBlendModeId = Shader.PropertyToID("_HighlightBlendMode");
        protected static readonly int FogBlendModeId = Shader.PropertyToID("_FogBlendMode");
        protected static readonly int OverlayBlendModeId = Shader.PropertyToID("_OverlayBlendMode");

        // Abstract properties for identification
        public abstract string CompositorId { get; }
        public abstract string DisplayName { get; }

        public void Initialize(CompositorContext ctx)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning($"{CompositorId}: Already initialized", "map_rendering");
                return;
            }

            context = ctx;
            InitializeCompositor();
            isInitialized = true;

            ArchonLogger.Log($"{CompositorId}: Initialized", "map_rendering");
        }

        /// <summary>
        /// Subclass initialization hook.
        /// </summary>
        protected virtual void InitializeCompositor() { }

        public void ConfigureMaterial(Material mapMaterial)
        {
            if (mapMaterial == null)
            {
                ArchonLogger.LogError($"{CompositorId}: Cannot configure null material", "map_rendering");
                return;
            }

            var config = GetConfig();

            // Set layer visibility
            SetMaterialFloat(mapMaterial, EnableBaseColorId, config.enableBaseColor ? 1f : 0f);
            SetMaterialFloat(mapMaterial, EnableLightingId, config.enableLighting ? 1f : 0f);
            SetMaterialFloat(mapMaterial, EnableBordersId, config.enableBorders ? 1f : 0f);
            SetMaterialFloat(mapMaterial, EnableHighlightsId, config.enableHighlights ? 1f : 0f);
            SetMaterialFloat(mapMaterial, EnableFogOfWarId, config.enableFogOfWar ? 1f : 0f);
            SetMaterialFloat(mapMaterial, EnableOverlayId, config.enableOverlay ? 1f : 0f);

            // Set blend modes
            SetMaterialInt(mapMaterial, BorderBlendModeId, (int)config.borderBlendMode);
            SetMaterialInt(mapMaterial, HighlightBlendModeId, (int)config.highlightBlendMode);
            SetMaterialInt(mapMaterial, FogBlendModeId, (int)config.fogBlendMode);
            SetMaterialInt(mapMaterial, OverlayBlendModeId, (int)config.overlayBlendMode);

            // Allow subclass to do additional configuration
            ConfigureCompositor(mapMaterial, config);

            ArchonLogger.Log($"{CompositorId}: Configured material", "map_rendering");
        }

        /// <summary>
        /// Subclass material configuration hook.
        /// </summary>
        protected virtual void ConfigureCompositor(Material mapMaterial, CompositorConfig config) { }

        public abstract CompositorConfig GetConfig();

        public virtual void OnPreRender() { }

        public virtual Shader GetCustomShader() => null;

        public void Dispose()
        {
            if (!isInitialized) return;
            DisposeCompositor();
            isInitialized = false;
            ArchonLogger.Log($"{CompositorId}: Disposed", "map_rendering");
        }

        /// <summary>
        /// Subclass disposal hook.
        /// </summary>
        protected virtual void DisposeCompositor() { }

        #region Utility Methods

        /// <summary>
        /// Safely set material float property.
        /// </summary>
        protected void SetMaterialFloat(Material material, int propertyId, float value)
        {
            if (material.HasProperty(propertyId))
            {
                material.SetFloat(propertyId, value);
            }
        }

        /// <summary>
        /// Safely set material int property.
        /// </summary>
        protected void SetMaterialInt(Material material, int propertyId, int value)
        {
            if (material.HasProperty(propertyId))
            {
                material.SetInt(propertyId, value);
            }
        }

        /// <summary>
        /// Safely set material color property.
        /// </summary>
        protected void SetMaterialColor(Material material, int propertyId, Color value)
        {
            if (material.HasProperty(propertyId))
            {
                material.SetColor(propertyId, value);
            }
        }

        /// <summary>
        /// Enable a shader keyword.
        /// </summary>
        protected void EnableKeyword(Material material, string keyword)
        {
            material.EnableKeyword(keyword);
        }

        /// <summary>
        /// Disable a shader keyword.
        /// </summary>
        protected void DisableKeyword(Material material, string keyword)
        {
            material.DisableKeyword(keyword);
        }

        #endregion
    }
}
