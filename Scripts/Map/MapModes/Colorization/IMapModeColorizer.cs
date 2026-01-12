using UnityEngine;
using Map.Rendering;

namespace Map.MapModes.Colorization
{
    /// <summary>
    /// ENGINE: Interface for pluggable map mode colorization strategies.
    ///
    /// Pattern 20: Pluggable Implementation Pattern
    /// - ENGINE provides interface + default implementation (gradient-based)
    /// - GAME registers custom implementations via MapRendererRegistry
    /// - VisualStyleConfiguration references by string ID
    ///
    /// Default: GradientMapModeColorizer (3-color linear interpolation)
    /// Custom examples: DiscreteColorBands, MultiGradient, PatternOverlay, AnimatedEffects
    /// </summary>
    public interface IMapModeColorizer
    {
        /// <summary>
        /// Unique identifier for this colorizer (e.g., "Gradient", "DiscreteBands", "MultiColor")
        /// </summary>
        string ColorizerId { get; }

        /// <summary>
        /// Human-readable name for UI/debugging
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Whether this colorizer needs per-frame updates (e.g., animated effects)
        /// </summary>
        bool RequiresPerFrameUpdate { get; }

        /// <summary>
        /// Initialize the colorizer with required resources.
        /// Called once when colorizer is first activated.
        /// </summary>
        void Initialize(MapModeColorizerContext context);

        /// <summary>
        /// Apply colorization to generate output texture.
        /// Core operation: province values + style params → colored texture
        /// </summary>
        /// <param name="provinceIDTexture">Input: Province ID texture (RG16 encoded)</param>
        /// <param name="outputTexture">Output: Colorized RGBA texture</param>
        /// <param name="provinceValues">Per-province normalized values (0-1), negative = skip</param>
        /// <param name="styleParams">Colorization style parameters</param>
        void Colorize(
            RenderTexture provinceIDTexture,
            RenderTexture outputTexture,
            float[] provinceValues,
            ColorizationStyleParams styleParams);

        /// <summary>
        /// Called each frame if RequiresPerFrameUpdate is true.
        /// Use for animated effects, pulsing, etc.
        /// </summary>
        void OnRenderFrame();

        /// <summary>
        /// Release GPU resources.
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// Context provided during colorizer initialization.
    /// Contains shared resources needed by colorizers.
    /// </summary>
    public struct MapModeColorizerContext
    {
        /// <summary>
        /// Reference to texture manager for accessing map textures
        /// </summary>
        public MapTextureManager TextureManager;

        /// <summary>
        /// Map dimensions
        /// </summary>
        public int MapWidth;
        public int MapHeight;

        /// <summary>
        /// Maximum province count for buffer sizing
        /// </summary>
        public int MaxProvinces;
    }

    /// <summary>
    /// Style parameters for colorization.
    /// Passed to Colorize() to control visual output.
    /// </summary>
    public struct ColorizationStyleParams
    {
        /// <summary>
        /// Primary gradient for value mapping (required)
        /// </summary>
        public ColorGradient Gradient;

        /// <summary>
        /// Color for ocean/water provinces
        /// </summary>
        public Color OceanColor;

        /// <summary>
        /// Color for provinces with no data (value < 0)
        /// </summary>
        public Color NoDataColor;

        /// <summary>
        /// Number of discrete bands (for band-based colorizers, 0 = continuous)
        /// </summary>
        public int DiscreteBands;

        /// <summary>
        /// Whether to show value labels on provinces
        /// </summary>
        public bool ShowValueLabels;

        /// <summary>
        /// Animation time (for animated colorizers)
        /// </summary>
        public float AnimationTime;

        /// <summary>
        /// Create default style parameters
        /// </summary>
        public static ColorizationStyleParams Default => new ColorizationStyleParams
        {
            Gradient = ColorGradient.RedToYellow(),
            OceanColor = new Color(0.098f, 0.098f, 0.439f, 1f),
            NoDataColor = new Color(0.25f, 0.25f, 0.25f, 1f),
            DiscreteBands = 0,
            ShowValueLabels = false,
            AnimationTime = 0f
        };
    }
}
