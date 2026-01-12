using UnityEngine;

namespace Map.Rendering.Border
{
    /// <summary>
    /// ENGINE: Abstract base class for border renderers with common functionality.
    /// Handles both GPU compute and CPU-based implementations.
    /// Extend this class to create custom border renderers.
    /// </summary>
    public abstract class BorderRendererBase : IBorderRenderer
    {
        public abstract string RendererId { get; }
        public abstract string DisplayName { get; }
        public virtual bool RequiresPerFrameUpdate => false;

        protected MapTextureManager textureManager;
        protected BorderRendererContext context;
        protected bool isInitialized;

        // Thread group size for compute shader dispatch (matches BorderDetection.compute)
        protected const int THREAD_GROUP_SIZE = 8;

        public virtual void Initialize(MapTextureManager texManager, BorderRendererContext ctx)
        {
            textureManager = texManager;
            context = ctx;

            OnInitialize();
            isInitialized = true;

            ArchonLogger.Log($"BorderRenderer '{RendererId}' initialized", "map_initialization");
        }

        /// <summary>
        /// Override for custom initialization logic.
        /// Called after textureManager and context are set.
        /// </summary>
        protected abstract void OnInitialize();

        public abstract void GenerateBorders(BorderGenerationParams parameters);

        public abstract void ApplyToMaterial(Material material, BorderStyleParams styleParams);

        public virtual void OnRenderFrame()
        {
            // Default: no-op. Override in renderers that need per-frame updates.
        }

        public abstract void Dispose();

        /// <summary>
        /// Utility: Calculate thread groups for compute shader dispatch.
        /// </summary>
        protected (int x, int y) CalculateThreadGroups(int width, int height)
        {
            return (
                (width + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE,
                (height + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE
            );
        }

        /// <summary>
        /// Utility: Get shader mode integer value for material.
        /// </summary>
        protected int GetShaderModeValue(string rendererId)
        {
            return rendererId switch
            {
                "None" => 0,
                "DistanceField" => 1,
                "PixelPerfect" => 2,
                "MeshGeometry" => 3,
                _ => 0 // Custom renderers default to 0 (no built-in shader support)
            };
        }

        /// <summary>
        /// Utility: Apply common border style parameters to material.
        /// </summary>
        protected void ApplyCommonStyleParams(Material material, BorderStyleParams styleParams)
        {
            material.SetColor("_CountryBorderColor", styleParams.CountryBorderColor);
            material.SetColor("_ProvinceBorderColor", styleParams.ProvinceBorderColor);
            material.SetFloat("_CountryBorderStrength", styleParams.CountryBorderStrength);
            material.SetFloat("_ProvinceBorderStrength", styleParams.ProvinceBorderStrength);
        }
    }
}
