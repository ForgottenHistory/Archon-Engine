using UnityEngine;
using UnityEngine.Rendering;

namespace Map.Rendering.Compositing
{
    /// <summary>
    /// ENGINE: Interface for pluggable shader compositing strategies.
    ///
    /// Pattern 20: Pluggable Implementation Pattern
    /// - ENGINE provides interface + default implementation
    /// - GAME registers custom implementations via MapRendererRegistry
    /// - Controls HOW render layers are combined in the final output
    ///
    /// Compositing Layers (each from pluggable IRenderer):
    /// 1. Base Color - Map mode (political/terrain/development)
    /// 2. Lighting - Normal map lighting
    /// 3. Borders - From IBorderRenderer
    /// 4. Highlights - From IHighlightRenderer
    /// 5. Fog of War - From IFogOfWarRenderer
    /// 6. Overlay - Paper/parchment effects
    ///
    /// Default: Standard layer-by-layer compositing with lerp blending
    /// Custom examples: Stylized (multiply borders), Minimal (skip fog), Cinematic (custom post)
    /// </summary>
    public interface IShaderCompositor
    {
        /// <summary>
        /// Unique identifier for this compositor (e.g., "Default", "Stylized", "Minimal")
        /// </summary>
        string CompositorId { get; }

        /// <summary>
        /// Human-readable name for UI/debugging
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Initialize the compositor with the map material.
        /// Called once when compositor is activated.
        /// </summary>
        void Initialize(CompositorContext context);

        /// <summary>
        /// Configure the material for this compositing strategy.
        /// Sets shader keywords, blend modes, layer visibility, etc.
        /// Called when compositor is activated or settings change.
        /// </summary>
        void ConfigureMaterial(Material mapMaterial);

        /// <summary>
        /// Get the compositing configuration for this compositor.
        /// Used by shader to determine layer order and blend modes.
        /// </summary>
        CompositorConfig GetConfig();

        /// <summary>
        /// Called before rendering each frame.
        /// Use for animated effects or dynamic adjustments.
        /// </summary>
        void OnPreRender();

        /// <summary>
        /// Optional: Provide a custom shader for compositing.
        /// Return null to use the default map shader.
        /// </summary>
        Shader GetCustomShader();

        /// <summary>
        /// Release resources.
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// Context provided during compositor initialization.
    /// </summary>
    public struct CompositorContext
    {
        /// <summary>
        /// Reference to MapTextureManager for accessing render textures
        /// </summary>
        public MapTextureManager TextureManager;

        /// <summary>
        /// Reference to MapRendererRegistry for accessing other renderers
        /// </summary>
        public MapRendererRegistry Registry;

        /// <summary>
        /// Map dimensions
        /// </summary>
        public int MapWidth;
        public int MapHeight;
    }

    /// <summary>
    /// Configuration for shader compositing.
    /// Passed to shader via material properties.
    /// </summary>
    [System.Serializable]
    public class CompositorConfig
    {
        [Header("Layer Visibility")]
        [Tooltip("Enable base map mode coloring")]
        public bool enableBaseColor = true;

        [Tooltip("Enable normal map lighting")]
        public bool enableLighting = true;

        [Tooltip("Enable border rendering")]
        public bool enableBorders = true;

        [Tooltip("Enable highlight rendering")]
        public bool enableHighlights = true;

        [Tooltip("Enable fog of war")]
        public bool enableFogOfWar = true;

        [Tooltip("Enable overlay texture")]
        public bool enableOverlay = true;

        [Header("Blend Modes")]
        [Tooltip("How borders blend with base color")]
        public BlendMode borderBlendMode = BlendMode.Normal;

        [Tooltip("How highlights blend with base color")]
        public BlendMode highlightBlendMode = BlendMode.Normal;

        [Tooltip("How fog of war blends with base color")]
        public BlendMode fogBlendMode = BlendMode.Normal;

        [Tooltip("How overlay blends with base color")]
        public BlendMode overlayBlendMode = BlendMode.Multiply;

        [Header("Layer Order (lower = rendered first)")]
        [Tooltip("Base color layer order (usually 0)")]
        public int baseColorOrder = 0;

        [Tooltip("Lighting layer order")]
        public int lightingOrder = 1;

        [Tooltip("Border layer order")]
        public int borderOrder = 2;

        [Tooltip("Highlight layer order")]
        public int highlightOrder = 3;

        [Tooltip("Fog of war layer order")]
        public int fogOrder = 4;

        [Tooltip("Overlay layer order")]
        public int overlayOrder = 5;

        /// <summary>
        /// Create default configuration
        /// </summary>
        public static CompositorConfig Default => new CompositorConfig();
    }

    /// <summary>
    /// Blend modes for layer compositing.
    /// Matches shader implementation.
    /// </summary>
    public enum BlendMode
    {
        /// <summary>Normal alpha blending (lerp)</summary>
        Normal = 0,

        /// <summary>Multiply blend (darkens)</summary>
        Multiply = 1,

        /// <summary>Screen blend (lightens)</summary>
        Screen = 2,

        /// <summary>Overlay blend (contrast)</summary>
        Overlay = 3,

        /// <summary>Additive blend</summary>
        Additive = 4,

        /// <summary>Soft light blend</summary>
        SoftLight = 5
    }
}
