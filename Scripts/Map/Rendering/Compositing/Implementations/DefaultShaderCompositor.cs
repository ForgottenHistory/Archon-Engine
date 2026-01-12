using UnityEngine;

namespace Map.Rendering.Compositing
{
    /// <summary>
    /// ENGINE: Default shader compositor with standard layer compositing.
    ///
    /// Compositing Order:
    /// 1. Base Color (map mode) - always rendered
    /// 2. Normal Map Lighting - lerp blend
    /// 3. Borders - lerp blend
    /// 4. Highlights - lerp blend
    /// 5. Fog of War - lerp blend
    /// 6. Overlay Texture - multiply blend
    ///
    /// All layers enabled, standard blend modes.
    /// This is the ENGINE default - GAME can register alternatives.
    /// </summary>
    public class DefaultShaderCompositor : ShaderCompositorBase
    {
        public override string CompositorId => "Default";
        public override string DisplayName => "Default Compositor";

        private CompositorConfig config;

        public DefaultShaderCompositor()
        {
            config = new CompositorConfig
            {
                // All layers enabled
                enableBaseColor = true,
                enableLighting = true,
                enableBorders = true,
                enableHighlights = true,
                enableFogOfWar = true,
                enableOverlay = true,

                // Standard blend modes
                borderBlendMode = BlendMode.Normal,
                highlightBlendMode = BlendMode.Normal,
                fogBlendMode = BlendMode.Normal,
                overlayBlendMode = BlendMode.Multiply,

                // Standard layer order
                baseColorOrder = 0,
                lightingOrder = 1,
                borderOrder = 2,
                highlightOrder = 3,
                fogOrder = 4,
                overlayOrder = 5
            };
        }

        public override CompositorConfig GetConfig() => config;

        protected override void ConfigureCompositor(Material mapMaterial, CompositorConfig cfg)
        {
            // Default compositor uses standard shader - no custom keywords needed
            // All configuration is done via material properties in base class
        }
    }

    /// <summary>
    /// Minimal compositor - borders and highlights only, no fog/overlay.
    /// Useful for performance or clean visual style.
    /// </summary>
    public class MinimalShaderCompositor : ShaderCompositorBase
    {
        public override string CompositorId => "Minimal";
        public override string DisplayName => "Minimal (Performance)";

        private CompositorConfig config;

        public MinimalShaderCompositor()
        {
            config = new CompositorConfig
            {
                enableBaseColor = true,
                enableLighting = false,    // Skip lighting
                enableBorders = true,
                enableHighlights = true,
                enableFogOfWar = false,    // Skip fog
                enableOverlay = false,     // Skip overlay

                borderBlendMode = BlendMode.Normal,
                highlightBlendMode = BlendMode.Normal
            };
        }

        public override CompositorConfig GetConfig() => config;
    }

    /// <summary>
    /// Stylized compositor - uses multiply blending for borders for EU4-like look.
    /// Borders darken the base color instead of overlaying.
    /// </summary>
    public class StylizedShaderCompositor : ShaderCompositorBase
    {
        public override string CompositorId => "Stylized";
        public override string DisplayName => "Stylized (EU4-like)";

        private CompositorConfig config;

        public StylizedShaderCompositor()
        {
            config = new CompositorConfig
            {
                enableBaseColor = true,
                enableLighting = true,
                enableBorders = true,
                enableHighlights = true,
                enableFogOfWar = true,
                enableOverlay = true,

                // Stylized blend modes
                borderBlendMode = BlendMode.Multiply,      // Borders darken
                highlightBlendMode = BlendMode.Additive,   // Highlights glow
                fogBlendMode = BlendMode.Normal,
                overlayBlendMode = BlendMode.SoftLight     // Subtle paper effect
            };
        }

        public override CompositorConfig GetConfig() => config;
    }

    /// <summary>
    /// Cinematic compositor - enhanced contrast and effects.
    /// Suitable for screenshots and trailers.
    /// </summary>
    public class CinematicShaderCompositor : ShaderCompositorBase
    {
        public override string CompositorId => "Cinematic";
        public override string DisplayName => "Cinematic (High Contrast)";

        private CompositorConfig config;

        // Additional cinematic parameters
        private static readonly int ContrastId = Shader.PropertyToID("_Contrast");
        private static readonly int SaturationBoostId = Shader.PropertyToID("_SaturationBoost");
        private static readonly int VignetteStrengthId = Shader.PropertyToID("_VignetteStrength");

        public CinematicShaderCompositor()
        {
            config = new CompositorConfig
            {
                enableBaseColor = true,
                enableLighting = true,
                enableBorders = true,
                enableHighlights = true,
                enableFogOfWar = true,
                enableOverlay = true,

                // Cinematic blend modes
                borderBlendMode = BlendMode.Overlay,       // High contrast borders
                highlightBlendMode = BlendMode.Screen,     // Bright highlights
                fogBlendMode = BlendMode.Multiply,         // Deep fog
                overlayBlendMode = BlendMode.Overlay       // Strong paper texture
            };
        }

        public override CompositorConfig GetConfig() => config;

        protected override void ConfigureCompositor(Material mapMaterial, CompositorConfig cfg)
        {
            // Set cinematic-specific parameters if shader supports them
            SetMaterialFloat(mapMaterial, ContrastId, 1.2f);
            SetMaterialFloat(mapMaterial, SaturationBoostId, 1.1f);
            SetMaterialFloat(mapMaterial, VignetteStrengthId, 0.3f);
        }
    }
}
