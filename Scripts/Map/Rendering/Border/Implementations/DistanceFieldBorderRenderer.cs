using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// ENGINE: Distance field border renderer implementation.
    /// Wraps BorderDistanceFieldGenerator for pluggable interface.
    ///
    /// Uses Jump Flooding Algorithm (JFA) for GPU-based smooth anti-aliased borders.
    /// Results in silky smooth borders at any zoom level (CK3/Stellaris quality).
    /// </summary>
    public class DistanceFieldBorderRenderer : BorderRendererBase
    {
        public override string RendererId => "DistanceField";
        public override string DisplayName => "Distance Field (Smooth)";
        public override bool RequiresPerFrameUpdate => false;

        private BorderDistanceFieldGenerator distanceFieldGenerator;
        private ComputeShader distanceFieldCompute;

        public DistanceFieldBorderRenderer(BorderDistanceFieldGenerator generator = null)
        {
            this.distanceFieldGenerator = generator;
        }

        protected override void OnInitialize()
        {
            // If no generator provided, try to get from context
            if (distanceFieldGenerator == null)
            {
                distanceFieldCompute = context.BorderSDFCompute;
                ArchonLogger.Log("DistanceFieldBorderRenderer: Using compute shader from context", "map_rendering");
            }
            else
            {
                // Generator already set - make sure it has texture manager reference
                distanceFieldGenerator.SetTextureManager(textureManager);
                ArchonLogger.Log("DistanceFieldBorderRenderer: Using existing BorderDistanceFieldGenerator", "map_rendering");
            }
        }

        /// <summary>
        /// Set the distance field generator reference.
        /// Called by BorderComputeDispatcher when migrating to pluggable architecture.
        /// </summary>
        public void SetDistanceFieldGenerator(BorderDistanceFieldGenerator generator)
        {
            distanceFieldGenerator = generator;
            if (isInitialized && distanceFieldGenerator != null)
            {
                distanceFieldGenerator.SetTextureManager(textureManager);
            }
        }

        public override void GenerateBorders(BorderGenerationParams parameters)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogWarning("DistanceFieldBorderRenderer: Not initialized", "map_rendering");
                return;
            }

            if (parameters.Mode == BorderMode.None)
            {
                return;
            }

            if (distanceFieldGenerator != null)
            {
                // Use the existing generator's method
                distanceFieldGenerator.GenerateDistanceField();
            }
            else
            {
                ArchonLogger.LogWarning("DistanceFieldBorderRenderer: No generator available", "map_rendering");
            }
        }

        public override void ApplyToMaterial(Material material, BorderStyleParams styleParams)
        {
            if (material == null) return;

            // Set shader mode for distance field
            material.SetInt("_BorderRenderingMode", GetShaderModeValue(RendererId));

            // Apply common style parameters (colors, strengths)
            ApplyCommonStyleParams(material, styleParams);

            // Apply distance field specific parameters
            material.SetFloat("_BorderEdgeWidth", styleParams.EdgeWidth);
            material.SetFloat("_BorderGradientWidth", styleParams.GradientWidth);
            material.SetFloat("_BorderSmoothness", styleParams.EdgeSmoothness);

            // Bind the distance field texture
            if (textureManager?.DistanceFieldBorderTexture != null)
            {
                material.SetTexture("_DistanceFieldBorderTexture", textureManager.DistanceFieldBorderTexture);
            }
        }

        public override void Dispose()
        {
            // BorderDistanceFieldGenerator is a MonoBehaviour owned by the GameObject,
            // so we don't destroy it - just clear our reference
            distanceFieldGenerator = null;
            distanceFieldCompute = null;
        }
    }
}
