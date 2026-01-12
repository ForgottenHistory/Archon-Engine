using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// ENGINE: No-op border renderer for when borders are disabled.
    /// Provides clean shutdown of border rendering.
    /// </summary>
    public class NoneBorderRenderer : BorderRendererBase
    {
        public override string RendererId => "None";
        public override string DisplayName => "None (Disabled)";
        public override bool RequiresPerFrameUpdate => false;

        protected override void OnInitialize()
        {
            // Nothing to initialize
        }

        public override void GenerateBorders(BorderGenerationParams parameters)
        {
            // No borders to generate
        }

        public override void ApplyToMaterial(Material material, BorderStyleParams styleParams)
        {
            if (material == null) return;

            // Set shader mode to 0 (no borders)
            material.SetInt("_BorderRenderingMode", 0);
        }

        public override void Dispose()
        {
            // Nothing to dispose
        }
    }
}
