using UnityEngine;
using Map.Rendering;
using Map.Rendering.Highlight;

namespace Archon.Engine.Map
{
    /// <summary>
    /// ENGINE: Visual style configuration for modular graphics
    /// Defines colors, borders, and visual parameters for different art styles
    /// Games customize by creating ScriptableObject assets with different parameter values
    /// </summary>
    [CreateAssetMenu(fileName = "VisualStyle", menuName = "Archon/Visual Styles/Style Configuration", order = 1)]
    public class VisualStyleConfiguration : ScriptableObject
    {
        [Header("Style Identity")]
        [Tooltip("Display name for this visual style")]
        public string styleName = "Default";

        [Tooltip("Short description of this style")]
        [TextArea(2, 4)]
        public string description = "Default visual style";

        [Header("Border Styling")]
        public BorderStyle borders = new BorderStyle();

        [Header("Map Mode Colors")]
        public MapModeColors mapModes = new MapModeColors();

        [Header("Material (Required)")]
        [Tooltip("Map material with complete shader (REQUIRED - defines entire visual style)")]
        public Material mapMaterial;

        /// <summary>
        /// Border visual configuration
        /// Single source of truth for all border rendering settings
        /// </summary>
        [System.Serializable]
        public class BorderStyle
        {
            [Header("Rendering Mode")]
            [Tooltip("How borders are rendered:\n• PixelPerfect: Sharp 1px borders (retro aesthetic)\n• DistanceField: Smooth anti-aliased borders (modern look)\n• MeshGeometry: 3D mesh borders (resolution-independent)")]
            public BorderRenderingMode renderingMode = BorderRenderingMode.ShaderDistanceField;

            [Header("Custom Renderer (Advanced)")]
            [Tooltip("Override with custom renderer ID from MapRendererRegistry.\nLeave empty to use renderingMode above.\nGAME layer can register custom renderers via MapRendererRegistry.")]
            public string customRendererId = "";

            [Header("Country Borders")]
            public Color countryBorderColor = Color.black;
            [Range(0f, 1f)]
            [Tooltip("Visibility strength (0 = hidden, 1 = fully visible)")]
            public float countryBorderStrength = 1.0f;

            [Header("Province Borders")]
            public Color provinceBorderColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            [Range(0f, 1f)]
            [Tooltip("Visibility strength (0 = hidden, 1 = fully visible)")]
            public float provinceBorderStrength = 0.5f;

            [Header("Border Behavior")]
            public bool enableBordersOnStartup = true;
            [Tooltip("Which borders to display:\n• Dual: Both country and province (recommended)\n• Country: Only borders between nations\n• Province: All province boundaries")]
            public BorderModeType borderMode = BorderModeType.Dual;

            [Header("Pixel Perfect Settings (for PixelPerfect mode only)")]
            [Tooltip("Thickness of country borders in pixels (0 = 1px thin border)")]
            [Range(0, 5)]
            public int pixelPerfectCountryThickness = 1;

            [Tooltip("Thickness of province borders in pixels (0 = 1px thin border)")]
            [Range(0, 5)]
            public int pixelPerfectProvinceThickness = 0;

            [Tooltip("Anti-aliasing gradient width (0 = sharp edges, 1-2 = smooth)")]
            [Range(0f, 2f)]
            public float pixelPerfectAntiAliasing = 0f;

            [Header("Distance Field Settings (for DistanceField mode only)")]
            [Tooltip("Width of the sharp border edge in pixels")]
            [Range(0.1f, 3f)]
            public float edgeWidth = 0.5f;

            [Tooltip("Soft gradient falloff distance in pixels (outer glow)")]
            [Range(0f, 5f)]
            public float gradientWidth = 2.0f;

            [Tooltip("Anti-aliasing smoothness (lower = crisper, higher = softer)")]
            [Range(0.1f, 1f)]
            public float edgeSmoothness = 0.2f;

            [Tooltip("Edge color darkening (0 = black, 1 = border color)")]
            [Range(0f, 1f)]
            public float edgeColorMultiplier = 0.7f;

            [Tooltip("Gradient color darkening (0 = black, 1 = border color)")]
            [Range(0f, 1f)]
            public float gradientColorMultiplier = 0.85f;

            [Tooltip("Edge opacity")]
            [Range(0f, 1f)]
            public float edgeAlpha = 1.0f;

            [Tooltip("Gradient opacity inside border")]
            [Range(0f, 1f)]
            public float gradientAlphaInside = 0.5f;

            [Tooltip("Gradient opacity outside border")]
            [Range(0f, 1f)]
            public float gradientAlphaOutside = 0.3f;

            /// <summary>
            /// Border mode - what borders to show (mirrors ENGINE enum for serialization)
            /// </summary>
            public enum BorderModeType
            {
                Province,
                Country,
                Thick,
                Dual,
                None
            }

            /// <summary>
            /// Get the effective renderer ID (custom or mapped from enum).
            /// Custom ID takes priority if set.
            /// </summary>
            public string GetEffectiveRendererId()
            {
                if (!string.IsNullOrEmpty(customRendererId))
                    return customRendererId;

                return MapRenderingModeToId(renderingMode);
            }

            /// <summary>
            /// Map BorderRenderingMode enum to renderer ID string.
            /// </summary>
            private static string MapRenderingModeToId(BorderRenderingMode mode)
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
        }

        /// <summary>
        /// Map mode color configuration
        /// </summary>
        [System.Serializable]
        public class MapModeColors
        {
            [Header("Common Colors")]
            [Tooltip("Color for ocean/water provinces")]
            public Color oceanColor = new Color(0.098f, 0.157f, 0.439f, 1f); // Dark blue (25, 40, 112)

            [Tooltip("Color for unowned land provinces")]
            public Color unownedLandColor = new Color(0.8f, 0.7f, 0.5f, 1f); // Beige

            [Header("Political Mode")]
            [Tooltip("Show terrain for unowned provinces instead of gray")]
            public bool showTerrainForUnowned = true;

            [Header("Terrain Mode")]
            [Tooltip("Brightness adjustment for terrain colors")]
            [Range(0.5f, 2.0f)]
            public float terrainBrightness = 1.0f;

            [Tooltip("Saturation adjustment for terrain colors")]
            [Range(0f, 2.0f)]
            public float terrainSaturation = 1.0f;

            [Header("Terrain Detail Mapping (Scale-Independent)")]
            [Tooltip("World-space tiling for detail textures (higher = more repetitions, typical: 0.01-0.1 for large scale, 1-10 for small scale)")]
            [Range(0.001f, 10f)]
            public float detailTiling = 0.05f;

            [Tooltip("Strength of detail texture blend (0 = macro only, 1 = full detail)")]
            [Range(0f, 1.0f)]
            public float detailStrength = 0.5f;

            [Header("Normal Map Lighting")]
            [Tooltip("Overall strength of the normal map effect")]
            [Range(0f, 2.0f)]
            public float normalMapStrength = 1.0f;

            [Tooltip("Shadow darkness (ambient light level)")]
            [Range(0f, 1.0f)]
            public float normalMapAmbient = 0.4f;

            [Tooltip("Highlight brightness (lit areas)")]
            [Range(1.0f, 2.0f)]
            public float normalMapHighlight = 1.4f;
        }

        /// <summary>
        /// Optional advanced visual effects
        /// These can be used by any visual style (EU3, Imperator, custom mods, etc.)
        /// </summary>
        [System.Serializable]
        public class AdvancedEffects
        {
            [Header("Texture Overlay")]
            [Tooltip("Optional overlay texture (parchment, paper, canvas, etc.)")]
            public Texture2D overlayTexture = null;

            [Tooltip("Strength of overlay texture (0 = none, 1 = full)")]
            [Range(0f, 1.0f)]
            public float overlayStrength = 0.0f;

            [Header("Color Adjustments")]
            [Tooltip("Country color saturation (0 = grayscale, 1 = full color)")]
            [Range(0f, 1.0f)]
            public float countryColorSaturation = 1.0f;
        }

        [Header("Advanced Effects")]
        [Tooltip("Optional effects like texture overlays and color adjustments")]
        public AdvancedEffects advancedEffects = new AdvancedEffects();

        [Header("Fog of War")]
        [Tooltip("Fog of war visibility settings")]
        public FogOfWarSettings fogOfWar = new FogOfWarSettings();

        [Header("Tessellation (3D Terrain)")]
        [Tooltip("3D terrain height displacement settings")]
        public TessellationSettings tessellation = new TessellationSettings();

        [Header("Highlight/Selection")]
        [Tooltip("Province selection and hover highlight settings")]
        public HighlightStyle highlights = new HighlightStyle();

        [Header("Terrain Blending")]
        [Tooltip("Terrain blend map generation settings")]
        public TerrainBlendStyle terrainBlend = new TerrainBlendStyle();

        [Header("Map Mode Colorization")]
        [Tooltip("Map mode colorization settings (gradient/discrete bands/custom)")]
        public MapModeColorizerStyle mapModeColorizer = new MapModeColorizerStyle();

        /// <summary>
        /// Fog of War visual configuration
        /// </summary>
        [System.Serializable]
        public class FogOfWarSettings
        {
            [Header("Custom Renderer (Advanced)")]
            [Tooltip("Override with custom renderer ID from MapRendererRegistry.\nLeave empty to use Default renderer.\nGAME layer can register custom renderers (stylized fog, animated clouds, etc.).")]
            public string customRendererId = "";

            [Header("Fog of War")]
            [Tooltip("Enable fog of war effect")]
            public bool enabled = true;

            [Header("Unexplored Areas")]
            [Tooltip("Color for unexplored provinces (never seen)")]
            public Color unexploredColor = new Color(0.05f, 0.05f, 0.05f, 1f); // Almost black

            [Header("Explored Areas")]
            [Tooltip("Color tint for explored but not visible provinces")]
            public Color exploredTint = new Color(0.5f, 0.5f, 0.5f, 1f); // Gray tint

            [Tooltip("Desaturation amount for explored areas (0 = full color, 1 = grayscale)")]
            [Range(0f, 1f)]
            public float exploredDesaturation = 0.7f;

            [Header("Fog Noise")]
            [Tooltip("Color of the animated fog clouds")]
            public Color noiseColor = new Color(0.3f, 0.3f, 0.4f, 1f); // Bluish-gray fog

            [Tooltip("Scale of fog noise pattern (higher = smaller noise features)")]
            [Range(0.1f, 20f)]
            public float noiseScale = 5.0f;

            [Tooltip("Strength of noise effect (0 = no noise, 1 = full noise)")]
            [Range(0f, 1f)]
            public float noiseStrength = 0.3f;

            [Tooltip("Animation speed of drifting fog (0 = static, higher = faster drift)")]
            [Range(0f, 2f)]
            public float noiseSpeed = 0.1f;

            /// <summary>
            /// Get the effective renderer ID (custom or default).
            /// </summary>
            public string GetEffectiveRendererId()
            {
                if (!string.IsNullOrEmpty(customRendererId))
                    return customRendererId;
                return "Default";
            }
        }

        /// <summary>
        /// Tessellation (3D terrain) configuration
        /// </summary>
        [System.Serializable]
        public class TessellationSettings
        {
            [Header("Tessellation (Optional 3D Feature)")]
            [Tooltip("Enable tessellation for 3D terrain (requires tessellated shader)")]
            public bool enabled = false;

            [Tooltip("Height scale for terrain displacement (0-100, typical: 10-50)")]
            [Range(0f, 100f)]
            public float heightScale = 10.0f;

            [Tooltip("Maximum tessellation factor (triangle density, 1-64, typical: 16-32)")]
            [Range(1f, 64f)]
            public float tessellationFactor = 16.0f;

            [Tooltip("Distance where tessellation starts to reduce (typical: 50-100)")]
            [Range(10f, 500f)]
            public float tessellationMinDistance = 50.0f;

            [Tooltip("Distance where tessellation becomes minimal (typical: 300-500)")]
            [Range(50f, 1000f)]
            public float tessellationMaxDistance = 500.0f;
        }

        /// <summary>
        /// Highlight/Selection visual configuration
        /// </summary>
        [System.Serializable]
        public class HighlightStyle
        {
            [Header("Custom Renderer (Advanced)")]
            [Tooltip("Override with custom renderer ID from MapRendererRegistry.\nLeave empty to use Default renderer.\nGAME layer can register custom renderers (glow, pulse, etc.).")]
            public string customRendererId = "";

            [Header("Selection Colors")]
            [Tooltip("Color for selected province")]
            public Color selectionColor = new Color(1f, 0.84f, 0f, 0.4f); // Gold with transparency

            [Tooltip("Color for hovered province")]
            public Color hoverColor = new Color(1f, 1f, 1f, 0.3f); // White with transparency

            [Header("Highlight Mode")]
            [Tooltip("How highlights are rendered:\n• Fill: Fill entire province\n• BorderOnly: Only highlight borders")]
            public HighlightMode defaultMode = HighlightMode.Fill;

            [Header("Settings")]
            [Tooltip("Border thickness for BorderOnly mode (1-5 pixels)")]
            [Range(1f, 5f)]
            public float borderThickness = 2.0f;

            [Tooltip("Overall highlight opacity multiplier")]
            [Range(0f, 1f)]
            public float opacityMultiplier = 1.0f;

            /// <summary>
            /// Get the effective renderer ID (custom or default).
            /// </summary>
            public string GetEffectiveRendererId()
            {
                if (!string.IsNullOrEmpty(customRendererId))
                    return customRendererId;
                return "Default";
            }
        }

        /// <summary>
        /// Terrain blend map generation configuration
        /// </summary>
        [System.Serializable]
        public class TerrainBlendStyle
        {
            [Header("Custom Renderer (Advanced)")]
            [Tooltip("Override with custom renderer ID from MapRendererRegistry.\nLeave empty to use Default renderer.\nGAME layer can register custom renderers (8-channel blending, stylized, etc.).")]
            public string customRendererId = "";

            [Header("Blend Map Generation")]
            [Tooltip("Sample radius for terrain blending (2 = 5x5, 5 = 11x11). Higher = smoother transitions.")]
            [Range(1, 10)]
            public int sampleRadius = 2;

            [Tooltip("Blend sharpness (1.0 = linear, >1 = sharper transitions, <1 = softer).")]
            [Range(0.1f, 5f)]
            public float blendSharpness = 1.0f;

            /// <summary>
            /// Get the effective renderer ID (custom or default).
            /// </summary>
            public string GetEffectiveRendererId()
            {
                if (!string.IsNullOrEmpty(customRendererId))
                    return customRendererId;
                return "Default";
            }
        }

        /// <summary>
        /// Map mode colorization configuration
        /// </summary>
        [System.Serializable]
        public class MapModeColorizerStyle
        {
            [Header("Custom Colorizer (Advanced)")]
            [Tooltip("Override with custom colorizer ID from MapRendererRegistry.\nLeave empty to use Gradient (default).\nGAME layer can register custom colorizers (discrete bands, multi-gradient, patterns, etc.).")]
            public string customColorizerId = "";

            [Header("Colorization Settings")]
            [Tooltip("Number of discrete color bands (0 = continuous gradient, 3-10 = discrete steps).\nOnly used by colorizers that support discrete modes.")]
            [Range(0, 10)]
            public int discreteBands = 0;

            [Tooltip("Show value labels on provinces (for data-heavy map modes).")]
            public bool showValueLabels = false;

            [Header("Special Colors")]
            [Tooltip("Color for provinces with no data (value < 0).")]
            public Color noDataColor = new Color(0.25f, 0.25f, 0.25f, 1f);

            /// <summary>
            /// Get the effective colorizer ID (custom or default gradient).
            /// </summary>
            public string GetEffectiveColorizerId()
            {
                if (!string.IsNullOrEmpty(customColorizerId))
                    return customColorizerId;
                return "Gradient";
            }
        }

        /// <summary>
        /// Validate configuration
        /// </summary>
        void OnValidate()
        {
            // Clamp border values
            borders.countryBorderStrength = Mathf.Clamp01(borders.countryBorderStrength);
            borders.provinceBorderStrength = Mathf.Clamp01(borders.provinceBorderStrength);
            borders.edgeWidth = Mathf.Clamp(borders.edgeWidth, 0.1f, 3f);
            borders.gradientWidth = Mathf.Clamp(borders.gradientWidth, 0f, 5f);
            borders.edgeSmoothness = Mathf.Clamp(borders.edgeSmoothness, 0.1f, 1f);

            // Clamp map mode values
            mapModes.terrainBrightness = Mathf.Clamp(mapModes.terrainBrightness, 0.5f, 2.0f);
            mapModes.terrainSaturation = Mathf.Clamp(mapModes.terrainSaturation, 0f, 2.0f);

            // Warn if material is missing
            if (mapMaterial == null)
            {
                ArchonLogger.LogWarning($"VisualStyleConfiguration '{styleName}': mapMaterial is not assigned! Style will not render correctly.", "map_rendering");
            }
        }

        /// <summary>
        /// Create default style with sensible parameters
        /// </summary>
        public static VisualStyleConfiguration CreateDefault()
        {
            var style = CreateInstance<VisualStyleConfiguration>();
            style.styleName = "Default";
            style.description = "ENGINE default style - basic rendering with configurable parameters";

            // Border rendering mode
            style.borders.renderingMode = BorderRenderingMode.ShaderDistanceField;
            style.borders.borderMode = BorderStyle.BorderModeType.Dual;

            // Border colors and strengths
            style.borders.countryBorderColor = Color.black;
            style.borders.countryBorderStrength = 1.0f;
            style.borders.provinceBorderColor = new Color(0.3f, 0.3f, 0.3f);
            style.borders.provinceBorderStrength = 0.5f;

            // Distance field defaults (smooth modern borders)
            style.borders.edgeWidth = 0.5f;
            style.borders.gradientWidth = 2.0f;
            style.borders.edgeSmoothness = 0.2f;
            style.borders.edgeColorMultiplier = 0.7f;
            style.borders.gradientColorMultiplier = 0.85f;
            style.borders.edgeAlpha = 1.0f;
            style.borders.gradientAlphaInside = 0.5f;
            style.borders.gradientAlphaOutside = 0.3f;

            // Standard colors
            style.mapModes.oceanColor = new Color(0.098f, 0.157f, 0.439f); // Dark blue
            style.mapModes.unownedLandColor = new Color(0.8f, 0.7f, 0.5f); // Beige

            return style;
        }

        /// <summary>
        /// Log configuration details (debug utility)
        /// </summary>
        public void LogConfiguration()
        {
            ArchonLogger.Log($"=== Visual Style: {styleName} ===", "map_rendering");
            ArchonLogger.Log($"Description: {description}", "map_rendering");
            ArchonLogger.Log($"Material: {(mapMaterial != null ? mapMaterial.name : "MISSING!")}", "map_rendering");
            ArchonLogger.Log($"Shader: {(mapMaterial != null ? mapMaterial.shader.name : "N/A")}", "map_rendering");
            ArchonLogger.Log($"Border Rendering: {borders.renderingMode}, Mode: {borders.borderMode}", "map_rendering");
            ArchonLogger.Log($"Country Borders: {borders.countryBorderColor} @ {borders.countryBorderStrength:P0}", "map_rendering");
            ArchonLogger.Log($"Province Borders: {borders.provinceBorderColor} @ {borders.provinceBorderStrength:P0}", "map_rendering");
            ArchonLogger.Log($"Ocean Color: {mapModes.oceanColor}", "map_rendering");
        }
    }
}
