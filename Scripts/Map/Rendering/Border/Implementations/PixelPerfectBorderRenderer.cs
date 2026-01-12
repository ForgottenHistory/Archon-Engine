using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// ENGINE: Pixel-perfect border renderer implementation.
    /// Uses compute shader for crisp, aliased 1-pixel border detection.
    ///
    /// Best for retro/pixel art aesthetics where sharp borders are desired.
    /// Supports configurable thickness and optional anti-aliasing.
    /// </summary>
    public class PixelPerfectBorderRenderer : BorderRendererBase
    {
        public override string RendererId => "PixelPerfect";
        public override string DisplayName => "Pixel Perfect (Crisp)";
        public override bool RequiresPerFrameUpdate => false;

        private ComputeShader borderDetectionCompute;
        private int detectDualBordersKernel = -1;

        // Configurable parameters
        private int countryBorderThickness = 1;
        private int provinceBorderThickness = 0;
        private float antiAliasing = 0f;

        public PixelPerfectBorderRenderer(ComputeShader computeShader = null)
        {
            this.borderDetectionCompute = computeShader;
        }

        protected override void OnInitialize()
        {
            // Get compute shader from context if not provided
            if (borderDetectionCompute == null)
            {
                borderDetectionCompute = context.BorderDetectionCompute;
            }

            if (borderDetectionCompute == null)
            {
                ArchonLogger.LogWarning("PixelPerfectBorderRenderer: No compute shader available", "map_rendering");
                return;
            }

            // Find the kernel
            if (borderDetectionCompute.HasKernel("DetectDualBorders"))
            {
                detectDualBordersKernel = borderDetectionCompute.FindKernel("DetectDualBorders");
                ArchonLogger.Log($"PixelPerfectBorderRenderer: Found DetectDualBorders kernel ({detectDualBordersKernel})", "map_rendering");
            }
            else
            {
                ArchonLogger.LogWarning("PixelPerfectBorderRenderer: Compute shader missing DetectDualBorders kernel", "map_rendering");
            }
        }

        /// <summary>
        /// Set pixel-perfect rendering parameters.
        /// </summary>
        /// <param name="countryThickness">Country border thickness in pixels</param>
        /// <param name="provinceThickness">Province border thickness in pixels</param>
        /// <param name="aa">Anti-aliasing gradient width (0 = sharp)</param>
        public void SetParameters(int countryThickness, int provinceThickness, float aa)
        {
            countryBorderThickness = Mathf.Clamp(countryThickness, 0, 5);
            provinceBorderThickness = Mathf.Clamp(provinceThickness, 0, 5);
            antiAliasing = Mathf.Clamp(aa, 0f, 2f);
        }

        public override void GenerateBorders(BorderGenerationParams parameters)
        {
            if (!isInitialized || detectDualBordersKernel < 0)
            {
                ArchonLogger.LogWarning("PixelPerfectBorderRenderer: Not properly initialized", "map_rendering");
                return;
            }

            if (parameters.Mode == BorderMode.None)
            {
                return;
            }

            if (textureManager == null || textureManager.PixelPerfectBorderTexture == null)
            {
                ArchonLogger.LogWarning("PixelPerfectBorderRenderer: Texture manager or output texture not available", "map_rendering");
                return;
            }

            var shader = borderDetectionCompute;

            // Set map dimensions
            shader.SetInt("MapWidth", textureManager.MapWidth);
            shader.SetInt("MapHeight", textureManager.MapHeight);

            // Set border thickness parameters
            shader.SetInt("CountryBorderThickness", countryBorderThickness);
            shader.SetInt("ProvinceBorderThickness", provinceBorderThickness);
            shader.SetFloat("BorderAntiAliasing", antiAliasing);

            // Bind textures
            shader.SetTexture(detectDualBordersKernel, "ProvinceIDTexture", textureManager.ProvinceIDTexture);
            shader.SetTexture(detectDualBordersKernel, "ProvinceOwnerTexture", textureManager.ProvinceOwnerTexture);
            shader.SetTexture(detectDualBordersKernel, "DualBorderTexture", textureManager.PixelPerfectBorderTexture);

            // Dispatch compute shader
            var (threadGroupsX, threadGroupsY) = CalculateThreadGroups(textureManager.MapWidth, textureManager.MapHeight);
            shader.Dispatch(detectDualBordersKernel, threadGroupsX, threadGroupsY, 1);

            ArchonLogger.Log($"PixelPerfectBorderRenderer: Generated borders (thickness: country={countryBorderThickness}, province={provinceBorderThickness})", "map_rendering");
        }

        public override void ApplyToMaterial(Material material, BorderStyleParams styleParams)
        {
            if (material == null) return;

            // Set shader mode for pixel-perfect
            material.SetInt("_BorderRenderingMode", GetShaderModeValue(RendererId));

            // Apply common style parameters
            ApplyCommonStyleParams(material, styleParams);

            // Update internal parameters from style params
            countryBorderThickness = styleParams.PixelPerfectCountryThickness;
            provinceBorderThickness = styleParams.PixelPerfectProvinceThickness;
            antiAliasing = styleParams.PixelPerfectAntiAliasing;

            // Bind the pixel-perfect border texture
            if (textureManager?.PixelPerfectBorderTexture != null)
            {
                material.SetTexture("_PixelPerfectBorderTexture", textureManager.PixelPerfectBorderTexture);
            }
        }

        public override void Dispose()
        {
            borderDetectionCompute = null;
            detectDualBordersKernel = -1;
        }
    }
}
