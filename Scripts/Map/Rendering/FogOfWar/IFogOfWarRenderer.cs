using UnityEngine;
using Core.Queries;

namespace Map.Rendering.FogOfWar
{
    /// <summary>
    /// ENGINE: Interface for pluggable fog of war rendering implementations.
    ///
    /// ENGINE provides default compute-shader based implementation.
    /// GAME can register custom implementations for different fog styles:
    /// - Different visual effects (animated fog, stylized clouds)
    /// - Alternative visibility rules
    /// - Performance optimizations for specific use cases
    ///
    /// Pattern 20: Pluggable Implementation (Interface + Registry)
    /// </summary>
    public interface IFogOfWarRenderer
    {
        /// <summary>
        /// Unique identifier for this renderer (e.g., "Default", "Stylized", "Minimal")
        /// </summary>
        string RendererId { get; }

        /// <summary>
        /// Human-readable name for UI/debugging
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Whether this renderer needs per-frame updates (e.g., animated fog)
        /// </summary>
        bool RequiresPerFrameUpdate { get; }

        /// <summary>
        /// Initialize the renderer with required dependencies
        /// </summary>
        void Initialize(MapTextureManager textureManager, FogOfWarRendererContext context);

        /// <summary>
        /// Set the player's country for visibility calculations
        /// </summary>
        void SetPlayerCountry(ushort countryID);

        /// <summary>
        /// Update visibility state based on current ownership
        /// </summary>
        void UpdateVisibility();

        /// <summary>
        /// Reveal a specific province (mark as explored)
        /// </summary>
        void RevealProvince(ushort provinceID);

        /// <summary>
        /// Set whether fog of war is enabled
        /// </summary>
        void SetEnabled(bool enabled);

        /// <summary>
        /// Get current enabled state
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Apply style parameters to material (shader uniforms)
        /// </summary>
        void ApplyToMaterial(Material material, FogOfWarStyleParams styleParams);

        /// <summary>
        /// Called each frame if RequiresPerFrameUpdate is true
        /// </summary>
        void OnRenderFrame();

        /// <summary>
        /// Get visibility level for a province (0.0 = unexplored, 0.5 = explored, 1.0 = visible)
        /// </summary>
        float GetProvinceVisibility(ushort provinceID);

        /// <summary>
        /// Get the current player country ID
        /// </summary>
        ushort GetPlayerCountry();

        /// <summary>
        /// Cleanup resources
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// Visibility state for provinces
    /// </summary>
    public enum VisibilityState
    {
        /// <summary>Never seen (0.0)</summary>
        Unexplored = 0,
        /// <summary>Previously seen but not currently visible (0.5)</summary>
        Explored = 1,
        /// <summary>Currently owned or adjacent to owned (1.0)</summary>
        Visible = 2
    }

    /// <summary>
    /// Context provided during renderer initialization
    /// </summary>
    public struct FogOfWarRendererContext
    {
        /// <summary>Compute shader for fog texture generation</summary>
        public ComputeShader FogOfWarCompute;

        /// <summary>Province queries for ownership lookups</summary>
        public ProvinceQueries ProvinceQueries;

        /// <summary>Maximum number of provinces</summary>
        public int MaxProvinces;
    }

    /// <summary>
    /// Style parameters for fog of war rendering (from VisualStyleConfiguration)
    /// </summary>
    public struct FogOfWarStyleParams
    {
        /// <summary>Color for unexplored provinces</summary>
        public Color UnexploredColor;

        /// <summary>Color tint for explored but not visible provinces</summary>
        public Color ExploredTint;

        /// <summary>Desaturation amount for explored areas (0-1)</summary>
        public float ExploredDesaturation;

        /// <summary>Color of animated fog noise</summary>
        public Color NoiseColor;

        /// <summary>Scale of fog noise pattern</summary>
        public float NoiseScale;

        /// <summary>Strength of noise effect (0-1)</summary>
        public float NoiseStrength;

        /// <summary>Animation speed of drifting fog</summary>
        public float NoiseSpeed;

        /// <summary>
        /// Create style params from VisualStyleConfiguration
        /// </summary>
        public static FogOfWarStyleParams FromConfig(Archon.Engine.Map.VisualStyleConfiguration config)
        {
            var fog = config.fogOfWar;
            return new FogOfWarStyleParams
            {
                UnexploredColor = fog.unexploredColor,
                ExploredTint = fog.exploredTint,
                ExploredDesaturation = fog.exploredDesaturation,
                NoiseColor = fog.noiseColor,
                NoiseScale = fog.noiseScale,
                NoiseStrength = fog.noiseStrength,
                NoiseSpeed = fog.noiseSpeed
            };
        }
    }
}
