using UnityEngine;

namespace Map.Rendering.Highlight
{
    /// <summary>
    /// ENGINE: Abstract base class for highlight renderers with common functionality.
    /// Extend this class to create custom highlight renderers.
    /// </summary>
    public abstract class HighlightRendererBase : IHighlightRenderer
    {
        public abstract string RendererId { get; }
        public abstract string DisplayName { get; }
        public virtual bool RequiresPerFrameUpdate => false;

        protected MapTextureManager textureManager;
        protected HighlightRendererContext context;
        protected bool isInitialized;

        // Current highlight state
        protected ushort currentHighlightedProvince;
        protected Color currentHighlightColor = Color.clear;
        protected HighlightMode currentMode = HighlightMode.Fill;

        // Thread group size for compute shader dispatch
        protected const int THREAD_GROUP_SIZE = 8;

        public virtual void Initialize(MapTextureManager texManager, HighlightRendererContext ctx)
        {
            textureManager = texManager;
            context = ctx;

            OnInitialize();
            isInitialized = true;

            ArchonLogger.Log($"HighlightRenderer '{RendererId}' initialized", "map_initialization");
        }

        /// <summary>
        /// Override for custom initialization logic.
        /// Called after textureManager and context are set.
        /// </summary>
        protected abstract void OnInitialize();

        public abstract void HighlightProvince(ushort provinceID, Color color, HighlightMode mode);

        public abstract void HighlightCountry(ushort countryID, Color color);

        public abstract void ClearHighlight();

        public abstract void ApplyToMaterial(Material material, HighlightStyleParams styleParams);

        public virtual void OnRenderFrame()
        {
            // Default: no-op. Override in renderers that need per-frame updates.
        }

        public ushort GetHighlightedProvince() => currentHighlightedProvince;

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
        /// Utility: Apply common highlight style parameters to material.
        /// </summary>
        protected void ApplyCommonStyleParams(Material material, HighlightStyleParams styleParams)
        {
            if (material == null) return;

            material.SetColor("_HighlightSelectionColor", styleParams.SelectionColor);
            material.SetColor("_HighlightHoverColor", styleParams.HoverColor);
            material.SetFloat("_HighlightOpacity", styleParams.OpacityMultiplier);
        }
    }
}
