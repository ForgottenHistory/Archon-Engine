using UnityEngine;

namespace Map.Rendering.Highlight
{
    /// <summary>
    /// ENGINE: Interface for pluggable highlight/selection rendering implementations.
    /// ENGINE provides default (GPU compute-based fill/border).
    /// GAME can register custom implementations via MapRendererRegistry.
    ///
    /// Examples of custom implementations:
    /// - Animated pulse effect
    /// - Glow/bloom effect
    /// - Custom outline styles
    /// - Shader-based selection
    /// </summary>
    public interface IHighlightRenderer
    {
        /// <summary>
        /// Unique identifier for this renderer (e.g., "Default", "Glow", "Pulse")
        /// </summary>
        string RendererId { get; }

        /// <summary>
        /// Display name for UI/debugging
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Whether this renderer requires per-frame updates (e.g., animated effects)
        /// </summary>
        bool RequiresPerFrameUpdate { get; }

        /// <summary>
        /// Initialize the renderer with required resources.
        /// Called once when renderer is first activated.
        /// </summary>
        void Initialize(MapTextureManager textureManager, HighlightRendererContext context);

        /// <summary>
        /// Highlight a single province.
        /// </summary>
        /// <param name="provinceID">Province to highlight (0 = clear)</param>
        /// <param name="color">Highlight color with alpha</param>
        /// <param name="mode">Fill or border-only mode</param>
        void HighlightProvince(ushort provinceID, Color color, HighlightMode mode);

        /// <summary>
        /// Highlight all provinces owned by a country.
        /// </summary>
        /// <param name="countryID">Country ID to highlight</param>
        /// <param name="color">Highlight color with alpha</param>
        void HighlightCountry(ushort countryID, Color color);

        /// <summary>
        /// Clear all highlights.
        /// </summary>
        void ClearHighlight();

        /// <summary>
        /// Apply visual parameters to the material (colors, effects, etc.)
        /// Called when visual style parameters change.
        /// </summary>
        void ApplyToMaterial(Material material, HighlightStyleParams styleParams);

        /// <summary>
        /// Called per-frame for renderers that need continuous updates (animations).
        /// Only called if RequiresPerFrameUpdate is true.
        /// </summary>
        void OnRenderFrame();

        /// <summary>
        /// Get the currently highlighted province ID.
        /// </summary>
        ushort GetHighlightedProvince();

        /// <summary>
        /// Cleanup resources when renderer is deactivated or destroyed.
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// Highlight mode - how to visualize the highlight
    /// </summary>
    public enum HighlightMode
    {
        Fill,        // Fill entire province with color
        BorderOnly   // Only highlight province borders
    }

    /// <summary>
    /// Context provided to highlight renderers during initialization.
    /// </summary>
    public struct HighlightRendererContext
    {
        public ComputeShader HighlightCompute;
        public ProvinceMapping ProvinceMapping;
    }

    /// <summary>
    /// Style parameters for highlight rendering.
    /// Passed to ApplyToMaterial for visual customization.
    /// </summary>
    public struct HighlightStyleParams
    {
        /// <summary>Default color for province selection</summary>
        public Color SelectionColor;

        /// <summary>Default color for province hover</summary>
        public Color HoverColor;

        /// <summary>Default highlight mode</summary>
        public HighlightMode DefaultMode;

        /// <summary>Border thickness for BorderOnly mode (1-5 pixels)</summary>
        public float BorderThickness;

        /// <summary>Highlight opacity multiplier</summary>
        public float OpacityMultiplier;
    }
}
