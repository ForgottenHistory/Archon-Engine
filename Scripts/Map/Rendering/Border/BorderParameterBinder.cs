using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// Manages border rendering parameters and binds them to shaders/materials
    /// Extracted from BorderComputeDispatcher for single responsibility
    ///
    /// Responsibilities:
    /// - Store all border rendering parameters (thickness, colors, alphas, etc.)
    /// - Bind parameters to materials for shader rendering
    /// - Validate and clamp parameter values
    /// </summary>
    public class BorderParameterBinder
    {
        // Border mode and basic settings
        public BorderMode BorderMode { get; set; } = BorderMode.Province;
        public int CountryBorderThickness { get; set; } = 1;
        public int ProvinceBorderThickness { get; set; } = 0;
        public float BorderAntiAliasing { get; set; } = 1.0f;
        public bool AutoUpdateBorders { get; set; } = true;

        // Rendering mode and widths
        public BorderRenderingMode RenderingMode { get; set; } = BorderRenderingMode.ShaderDistanceField;
        public float CountryBorderWidth { get; set; } = 0.5f;
        public float ProvinceBorderWidth { get; set; } = 0.5f;

        // AAA Distance Field Border Settings
        public float EdgeWidth { get; set; } = 0.5f;              // Sharp border thickness
        public float GradientWidth { get; set; } = 2.0f;          // Soft gradient falloff
        public float EdgeSmoothness { get; set; } = 0.2f;         // Anti-aliasing smoothness
        public float EdgeColorMul { get; set; } = 0.7f;           // Edge color darkening
        public float GradientColorMul { get; set; } = 0.85f;      // Gradient color darkening
        public float EdgeAlpha { get; set; } = 1.0f;              // Edge opacity
        public float GradientAlphaInside { get; set; } = 0.5f;    // Gradient opacity inside
        public float GradientAlphaOutside { get; set; } = 0.3f;   // Gradient opacity outside

        /// <summary>
        /// Bind distance field border parameters to material
        /// </summary>
        public void BindDistanceFieldBorderParams(MapTextureManager textureManager, Material material)
        {
            if (material == null)
            {
                ArchonLogger.LogWarning("BorderParameterBinder: Cannot bind distance field params - material is null", "map_rendering");
                return;
            }

            if (textureManager == null)
            {
                ArchonLogger.LogWarning("BorderParameterBinder: Cannot bind distance field params - textureManager is null", "map_rendering");
                return;
            }

            // Bind parameters via DynamicTextureSet
            textureManager.DynamicTextures?.SetDistanceFieldBorderParams(
                material,
                EdgeWidth,
                GradientWidth,
                EdgeSmoothness,
                EdgeColorMul,
                GradientColorMul,
                EdgeAlpha,
                GradientAlphaInside,
                GradientAlphaOutside
            );

            ArchonLogger.Log("BorderParameterBinder: Bound distance field border parameters to material", "map_rendering");
        }

        /// <summary>
        /// Set multiple border parameters at once with validation
        /// </summary>
        public void SetBorderParameters(BorderMode mode, int countryThickness, int provinceThickness, float antiAliasing)
        {
            BorderMode = mode;
            CountryBorderThickness = Mathf.Clamp(countryThickness, 0, 5);
            ProvinceBorderThickness = Mathf.Clamp(provinceThickness, 0, 5);
            BorderAntiAliasing = Mathf.Clamp(antiAliasing, 0f, 2f);
        }

        /// <summary>
        /// Set distance field parameters with validation
        /// </summary>
        public void SetDistanceFieldParameters(
            float edgeWidth,
            float gradientWidth,
            float edgeSmoothness,
            float edgeColorMul,
            float gradientColorMul,
            float edgeAlpha,
            float gradientAlphaInside,
            float gradientAlphaOutside)
        {
            EdgeWidth = Mathf.Max(0.1f, edgeWidth);
            GradientWidth = Mathf.Max(0f, gradientWidth);
            EdgeSmoothness = Mathf.Clamp(edgeSmoothness, 0f, 1f);
            EdgeColorMul = Mathf.Clamp01(edgeColorMul);
            GradientColorMul = Mathf.Clamp01(gradientColorMul);
            EdgeAlpha = Mathf.Clamp01(edgeAlpha);
            GradientAlphaInside = Mathf.Clamp01(gradientAlphaInside);
            GradientAlphaOutside = Mathf.Clamp01(gradientAlphaOutside);
        }
    }
}
