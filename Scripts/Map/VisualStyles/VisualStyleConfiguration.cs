using UnityEngine;

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
        /// </summary>
        [System.Serializable]
        public class BorderStyle
        {
            [Header("Country Borders")]
            public Color countryBorderColor = Color.black;
            [Range(0f, 1f)]
            public float countryBorderStrength = 1.0f;
            [Tooltip("Country border thickness in pixels (0 = thin 1px, 1-5 = progressively thicker)")]
            [Range(0, 5)]
            public int countryBorderThickness = 1;

            [Header("Province Borders")]
            public Color provinceBorderColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            [Range(0f, 1f)]
            public float provinceBorderStrength = 0.5f;
            [Tooltip("Province border thickness in pixels (0 = thin 1px, 1-5 = progressively thicker)")]
            [Range(0, 5)]
            public int provinceBorderThickness = 0;

            [Header("Border Quality")]
            [Tooltip("Enable anti-aliasing for smooth border edges (0 = off, 1-2 = smooth)")]
            [Range(0f, 2f)]
            public float borderAntiAliasing = 1.0f;

            [Header("Border Behavior")]
            public bool enableBordersOnStartup = true;
            public BorderMode defaultBorderMode = BorderMode.Dual;  // Dual is recommended (shows both country + province borders)

            public enum BorderMode
            {
                Province,
                Country,
                Thick,
                Dual,
                None
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

        /// <summary>
        /// Fog of War visual configuration
        /// </summary>
        [System.Serializable]
        public class FogOfWarSettings
        {
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
        /// Validate configuration
        /// </summary>
        void OnValidate()
        {
            // Clamp values to valid ranges
            borders.countryBorderStrength = Mathf.Clamp01(borders.countryBorderStrength);
            borders.provinceBorderStrength = Mathf.Clamp01(borders.provinceBorderStrength);
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

            // Standard borders
            style.borders.countryBorderColor = Color.black;
            style.borders.countryBorderStrength = 1.0f;
            style.borders.countryBorderThickness = 1;
            style.borders.provinceBorderColor = new Color(0.3f, 0.3f, 0.3f);
            style.borders.provinceBorderStrength = 0.5f;
            style.borders.provinceBorderThickness = 0;

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
            ArchonLogger.Log($"Country Borders: {borders.countryBorderColor} @ {borders.countryBorderStrength:P0}", "map_rendering");
            ArchonLogger.Log($"Province Borders: {borders.provinceBorderColor} @ {borders.provinceBorderStrength:P0}", "map_rendering");
            ArchonLogger.Log($"Ocean Color: {mapModes.oceanColor}", "map_rendering");
        }
    }
}
