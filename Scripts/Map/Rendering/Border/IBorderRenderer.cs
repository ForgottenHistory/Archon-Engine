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

    /// <summary>ENGINE: Optional capability of a border renderer: exposes its traced border arcs.</summary>
    public interface IBorderArcSource
    {
        /// <summary>Every traced province border (polylines in map pixels), for games that draw their own lines along borders.</summary>
        System.Collections.Generic.IReadOnlyList<BorderArc> Arcs { get; }
    }

    /// <summary>
    /// ENGINE: Optional capability of a border renderer: outline the highlighted province or
    /// country with its own (smoothed) border geometry, so the selection edge matches the borders
    /// instead of the pixel staircase of the highlight texture. ProvinceHighlighter calls this on
    /// the active border renderer when it implements it. Styled by the "highlight" border class.
    /// </summary>
    public interface IBorderHighlightOutline
    {
        /// <summary>Outline one province. Color: rgb tints the outline, alpha is ignored.</summary>
        void SetHighlightOutlineProvince(ushort provinceID, Color color);

        /// <summary>Outline the outer border of every province owned by the country.</summary>
        void SetHighlightOutlineCountry(ushort countryID, Color color);

        void ClearHighlightOutline();

        /// <summary>
        /// Second, independent outline around any set of provinces (e.g. the state of the selected
        /// province). Drawn just under the primary outline; color tints it like the primary.
        /// </summary>
        void SetSecondaryOutline(System.Func<ushort, bool> inSet, Color color);

        void ClearSecondaryOutline();
    }
}
