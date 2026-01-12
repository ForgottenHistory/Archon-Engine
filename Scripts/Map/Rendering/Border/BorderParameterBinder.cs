namespace Map.Rendering.Border
{
    /// <summary>
    /// Manages border rendering mode state
    /// Extracted from BorderComputeDispatcher for single responsibility
    ///
    /// NOTE: Visual parameters (colors, widths, distance field settings) are now
    /// controlled via VisualStyleConfiguration - the single source of truth for visuals.
    /// This class only tracks the rendering mode for compute shader orchestration.
    /// </summary>
    public class BorderParameterBinder
    {
        // Border mode (which borders to show)
        public BorderMode BorderMode { get; set; } = BorderMode.Dual;

        // Rendering mode (how to render)
        public BorderRenderingMode RenderingMode { get; set; } = BorderRenderingMode.ShaderDistanceField;

        // Auto-update behavior
        public bool AutoUpdateBorders { get; set; } = true;
    }
}
