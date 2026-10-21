using UnityEngine;
using Map.Rendering;
using Map.Rendering.Highlight;

namespace Archon.Engine.Map
{
    /// <summary>
    /// ENGINE: VisualStyleConfiguration section types (fog, tessellation, highlights, terrain blend, colorization).
    /// Split from VisualStyleConfiguration.cs to keep files focused.
    /// </summary>
    public partial class VisualStyleConfiguration
    {
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
            [Tooltip("Color for provinces with no data (negative value).")]
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
    }
}
