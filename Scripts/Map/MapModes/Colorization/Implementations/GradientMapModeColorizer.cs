using UnityEngine;
using Core.Modding;

namespace Map.MapModes.Colorization
{
    /// <summary>
    /// ENGINE: Default map mode colorizer using 3-color gradient interpolation.
    ///
    /// Algorithm:
    /// - Sample gradient at 3 points (low=0, mid=0.5, high=1.0)
    /// - GPU compute shader interpolates based on province values
    /// - Provinces with negative values use ocean color
    ///
    /// This is the ENGINE default colorizer - wraps existing GradientMapMode.compute logic.
    /// GAME layer can register alternatives (DiscreteColorBands, MultiGradient, etc.)
    /// </summary>
    public class GradientMapModeColorizer : MapModeColorizerBase
    {
        public override string ColorizerId => "Gradient";
        public override string DisplayName => "3-Color Gradient (Default)";

        // Compute shader
        private ComputeShader gradientCompute;
        private int colorizeKernel;

        // Compute buffers
        private ComputeBuffer provinceValueBuffer;
        private ComputeBuffer gradientColorsBuffer;

        public GradientMapModeColorizer() { }

        /// <summary>
        /// Constructor with pre-loaded compute shader (for testing or direct use).
        /// </summary>
        public GradientMapModeColorizer(ComputeShader computeShader)
        {
            gradientCompute = computeShader;
        }

        protected override void InitializeColorizer()
        {
            // Load compute shader - check mods first, then fall back to Resources
            if (gradientCompute == null)
            {
                gradientCompute = ModLoader.LoadAssetWithFallback<ComputeShader>(
                    "GradientMapMode",
                    "Shaders/GradientMapMode"
                );
            }

            if (gradientCompute == null)
            {
                ArchonLogger.LogError("GradientMapModeColorizer: GradientMapMode compute shader not found!", "map_modes");
                return;
            }

            colorizeKernel = gradientCompute.FindKernel("ColorizeGradient");
        }

        protected override void DoColorize(
            RenderTexture provinceIDTexture,
            RenderTexture outputTexture,
            float[] provinceValues,
            ColorizationStyleParams styleParams)
        {
            if (gradientCompute == null)
            {
                ArchonLogger.LogError("GradientMapModeColorizer: No compute shader!", "map_modes");
                return;
            }

            float startTime = Time.realtimeSinceStartup;

            // Ensure buffers are sized correctly
            provinceValueBuffer = EnsureProvinceValueBuffer(provinceValueBuffer, provinceValues.Length);
            provinceValueBuffer.SetData(provinceValues);

            // Gradient buffer - 3 colors (low, mid, high)
            gradientColorsBuffer = EnsureGradientBuffer(gradientColorsBuffer, 3);
            UploadGradientColors(gradientColorsBuffer, styleParams.Gradient, 3);

            // Set textures
            gradientCompute.SetTexture(colorizeKernel, "ProvinceIDTexture", provinceIDTexture);
            gradientCompute.SetTexture(colorizeKernel, "OutputTexture", outputTexture);

            // Set buffers
            gradientCompute.SetBuffer(colorizeKernel, "ProvinceValueBuffer", provinceValueBuffer);
            gradientCompute.SetBuffer(colorizeKernel, "GradientColors", gradientColorsBuffer);

            // Set parameters
            gradientCompute.SetInt("MapWidth", outputTexture.width);
            gradientCompute.SetInt("MapHeight", outputTexture.height);
            gradientCompute.SetVector("OceanColor", ColorToVector4(styleParams.OceanColor));

            // Dispatch
            var (groupsX, groupsY) = CalculateThreadGroups(outputTexture.width, outputTexture.height);
            gradientCompute.Dispatch(colorizeKernel, groupsX, groupsY, 1);

            float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"GradientMapModeColorizer: Colorized in {elapsed:F2}ms", "map_modes");
        }

        protected override void DisposeResources()
        {
            provinceValueBuffer?.Release();
            provinceValueBuffer = null;

            gradientColorsBuffer?.Release();
            gradientColorsBuffer = null;
        }
    }
}
