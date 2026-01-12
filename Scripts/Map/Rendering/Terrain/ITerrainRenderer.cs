using UnityEngine;

namespace Map.Rendering.Terrain
{
    /// <summary>
    /// ENGINE: Interface for pluggable terrain rendering implementations.
    ///
    /// ENGINE provides default Imperator Rome-style 4-channel blend map generation.
    /// GAME can register custom implementations for different terrain styles:
    /// - Different blending algorithms (8-channel, height-based, etc.)
    /// - Stylized terrain rendering
    /// - Performance-optimized variants
    ///
    /// Pattern 20: Pluggable Implementation (Interface + Registry)
    /// </summary>
    public interface ITerrainRenderer
    {
        /// <summary>
        /// Unique identifier for this renderer (e.g., "Default", "Stylized", "HighDetail")
        /// </summary>
        string RendererId { get; }

        /// <summary>
        /// Human-readable name for UI/debugging
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Whether this renderer needs per-frame updates
        /// </summary>
        bool RequiresPerFrameUpdate { get; }

        /// <summary>
        /// Initialize the renderer with required dependencies
        /// </summary>
        void Initialize(MapTextureManager textureManager, TerrainRendererContext context);

        /// <summary>
        /// Generate terrain blend maps (DetailIndexTexture + DetailMaskTexture)
        /// Called after ProvinceTerrainAnalyzer completes
        /// </summary>
        /// <param name="provinceIDTexture">Province ID texture</param>
        /// <param name="provinceTerrainBuffer">Province terrain buffer (terrain type per province)</param>
        /// <param name="width">Map width</param>
        /// <param name="height">Map height</param>
        /// <returns>Tuple of (DetailIndexTexture, DetailMaskTexture)</returns>
        (RenderTexture detailIndex, RenderTexture detailMask) GenerateBlendMaps(
            RenderTexture provinceIDTexture,
            ComputeBuffer provinceTerrainBuffer,
            int width,
            int height);

        /// <summary>
        /// Apply style parameters to material (shader uniforms)
        /// </summary>
        void ApplyToMaterial(Material material, TerrainStyleParams styleParams);

        /// <summary>
        /// Called each frame if RequiresPerFrameUpdate is true
        /// </summary>
        void OnRenderFrame();

        /// <summary>
        /// Get the sample radius used for terrain blending
        /// </summary>
        int GetSampleRadius();

        /// <summary>
        /// Set the sample radius for terrain blending
        /// </summary>
        void SetSampleRadius(int radius);

        /// <summary>
        /// Get the blend sharpness value
        /// </summary>
        float GetBlendSharpness();

        /// <summary>
        /// Set the blend sharpness value
        /// </summary>
        void SetBlendSharpness(float sharpness);

        /// <summary>
        /// Cleanup resources
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// Context provided during terrain renderer initialization
    /// </summary>
    public struct TerrainRendererContext
    {
        /// <summary>Compute shader for blend map generation</summary>
        public ComputeShader TerrainBlendMapCompute;

        /// <summary>Terrain RGB lookup for type mappings</summary>
        public TerrainRGBLookup TerrainRGBLookup;

        /// <summary>Maximum number of terrain types</summary>
        public int MaxTerrainTypes;
    }

    /// <summary>
    /// Style parameters for terrain rendering (from VisualStyleConfiguration)
    /// </summary>
    public struct TerrainStyleParams
    {
        /// <summary>Brightness adjustment for terrain colors (0.5-2.0)</summary>
        public float Brightness;

        /// <summary>Saturation adjustment for terrain colors (0-2.0)</summary>
        public float Saturation;

        /// <summary>World-space tiling for detail textures</summary>
        public float DetailTiling;

        /// <summary>Strength of detail texture blend (0-1)</summary>
        public float DetailStrength;

        /// <summary>Normal map strength</summary>
        public float NormalMapStrength;

        /// <summary>Normal map ambient (shadow darkness)</summary>
        public float NormalMapAmbient;

        /// <summary>Normal map highlight brightness</summary>
        public float NormalMapHighlight;

        /// <summary>Sample radius for terrain blending</summary>
        public int SampleRadius;

        /// <summary>Blend sharpness (1.0 = linear, >1 = sharper)</summary>
        public float BlendSharpness;

        /// <summary>
        /// Create style params from VisualStyleConfiguration
        /// </summary>
        public static TerrainStyleParams FromConfig(Archon.Engine.Map.VisualStyleConfiguration config)
        {
            var mapModes = config.mapModes;
            return new TerrainStyleParams
            {
                Brightness = mapModes.terrainBrightness,
                Saturation = mapModes.terrainSaturation,
                DetailTiling = mapModes.detailTiling,
                DetailStrength = mapModes.detailStrength,
                NormalMapStrength = mapModes.normalMapStrength,
                NormalMapAmbient = mapModes.normalMapAmbient,
                NormalMapHighlight = mapModes.normalMapHighlight,
                SampleRadius = 2, // Default, can be overridden
                BlendSharpness = 1.0f // Default linear blending
            };
        }
    }
}
