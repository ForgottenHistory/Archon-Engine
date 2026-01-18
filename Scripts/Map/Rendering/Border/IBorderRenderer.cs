using UnityEngine;
using CoreSystems = Core.Systems;
using Core.Queries;

namespace Map.Rendering.Border
{
    /// <summary>
    /// ENGINE: Interface for pluggable border rendering implementations.
    /// ENGINE provides defaults (DistanceField, PixelPerfect, MeshGeometry).
    /// GAME can register custom implementations via MapRendererRegistry.
    /// </summary>
    public interface IBorderRenderer
    {
        /// <summary>
        /// Unique identifier for this renderer (e.g., "DistanceField", "PixelPerfect", "MyCustom")
        /// </summary>
        string RendererId { get; }

        /// <summary>
        /// Display name for UI/debugging
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Whether this renderer requires per-frame updates (e.g., MeshGeometry needs OnRenderFrame)
        /// </summary>
        bool RequiresPerFrameUpdate { get; }

        /// <summary>
        /// Initialize the renderer with required resources.
        /// Called once when renderer is first activated.
        /// </summary>
        void Initialize(MapTextureManager textureManager, BorderRendererContext context);

        /// <summary>
        /// Generate/update borders. Called when borders are dirty or ownership changes.
        /// </summary>
        void GenerateBorders(BorderGenerationParams parameters);

        /// <summary>
        /// Apply visual parameters to the material (colors, widths, etc.)
        /// Called when visual style parameters change.
        /// </summary>
        void ApplyToMaterial(Material material, BorderStyleParams styleParams);

        /// <summary>
        /// Called per-frame for renderers that need continuous updates.
        /// Only called if RequiresPerFrameUpdate is true.
        /// </summary>
        void OnRenderFrame();

        /// <summary>
        /// Cleanup resources when renderer is deactivated or destroyed.
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// Context provided to border renderers during initialization.
    /// Contains references to systems needed for border generation.
    /// </summary>
    public struct BorderRendererContext
    {
        public CoreSystems.AdjacencySystem AdjacencySystem;
        public CoreSystems.ProvinceSystem ProvinceSystem;
        public CoreSystems.CountrySystem CountrySystem;
        public ProvinceMapping ProvinceMapping;
        public Transform MapPlaneTransform;
        public ComputeShader BorderDetectionCompute;
        public ComputeShader BorderSDFCompute;
    }

    /// <summary>
    /// Parameters for border generation.
    /// </summary>
    public struct BorderGenerationParams
    {
        /// <summary>Which borders to show (Province, Country, Dual, None)</summary>
        public BorderMode Mode;

        /// <summary>Force full regeneration vs incremental update</summary>
        public bool ForceRegenerate;

        /// <summary>Province queries for owner data (needed for country vs province border detection)</summary>
        public ProvinceQueries ProvinceQueries;
    }

    /// <summary>
    /// Style parameters from VisualStyleConfiguration.
    /// Passed to ApplyToMaterial for visual customization.
    /// </summary>
    public struct BorderStyleParams
    {
        /// <summary>Color of borders between countries.</summary>
        public Color CountryBorderColor;

        /// <summary>Color of borders between provinces within the same country.</summary>
        public Color ProvinceBorderColor;

        /// <summary>Visibility strength of country borders (0 = hidden, 1 = full).</summary>
        public float CountryBorderStrength;

        /// <summary>Visibility strength of province borders (0 = hidden, 1 = full).</summary>
        public float ProvinceBorderStrength;

        // Pixel-perfect mode parameters

        /// <summary>Country border thickness in pixels (PixelPerfect mode only).</summary>
        public int PixelPerfectCountryThickness;

        /// <summary>Province border thickness in pixels (PixelPerfect mode only).</summary>
        public int PixelPerfectProvinceThickness;

        /// <summary>Anti-aliasing gradient width (PixelPerfect mode only).</summary>
        public float PixelPerfectAntiAliasing;

        // Distance field mode parameters

        /// <summary>Sharp edge width in pixels (DistanceField mode only).</summary>
        public float EdgeWidth;

        /// <summary>Soft gradient falloff distance (DistanceField mode only).</summary>
        public float GradientWidth;

        /// <summary>Anti-aliasing smoothness factor (DistanceField mode only).</summary>
        public float EdgeSmoothness;
    }
}
