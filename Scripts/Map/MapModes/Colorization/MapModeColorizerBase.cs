using UnityEngine;
using Map.Rendering;

namespace Map.MapModes.Colorization
{
    /// <summary>
    /// ENGINE: Abstract base class for map mode colorizers.
    /// Provides common utilities for GPU compute shader-based colorization.
    ///
    /// Subclasses implement:
    /// - ColorizerId, DisplayName - identification
    /// - InitializeColorizer() - load compute shader, create buffers
    /// - DoColorize() - dispatch compute shader
    /// - DisposeResources() - release GPU resources
    /// </summary>
    public abstract class MapModeColorizerBase : IMapModeColorizer
    {
        // Context from initialization
        protected MapModeColorizerContext context;
        protected bool isInitialized = false;

        // Common compute shader constants
        protected const int THREAD_GROUP_SIZE = 8;

        // Abstract properties for identification
        public abstract string ColorizerId { get; }
        public abstract string DisplayName { get; }
        public virtual bool RequiresPerFrameUpdate => false;

        public void Initialize(MapModeColorizerContext ctx)
        {
            if (isInitialized)
            {
                ArchonLogger.LogWarning($"{ColorizerId}: Already initialized", "map_modes");
                return;
            }

            context = ctx;

            // Call subclass initialization
            InitializeColorizer();

            isInitialized = true;
            ArchonLogger.Log($"{ColorizerId}: Initialized", "map_modes");
        }

        /// <summary>
        /// Subclass initialization hook.
        /// Load compute shaders, create buffers, etc.
        /// </summary>
        protected abstract void InitializeColorizer();

        public void Colorize(
            RenderTexture provinceIDTexture,
            RenderTexture outputTexture,
            float[] provinceValues,
            ColorizationStyleParams styleParams)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError($"{ColorizerId}: Not initialized!", "map_modes");
                return;
            }

            if (provinceIDTexture == null || outputTexture == null)
            {
                ArchonLogger.LogError($"{ColorizerId}: Null textures provided!", "map_modes");
                return;
            }

            // Call subclass colorization
            DoColorize(provinceIDTexture, outputTexture, provinceValues, styleParams);
        }

        /// <summary>
        /// Subclass colorization implementation.
        /// Dispatch compute shader with appropriate parameters.
        /// </summary>
        protected abstract void DoColorize(
            RenderTexture provinceIDTexture,
            RenderTexture outputTexture,
            float[] provinceValues,
            ColorizationStyleParams styleParams);

        public virtual void OnRenderFrame()
        {
            // Default: no per-frame updates
            // Override in animated colorizers
        }

        public void Dispose()
        {
            if (!isInitialized) return;

            DisposeResources();
            isInitialized = false;

            ArchonLogger.Log($"{ColorizerId}: Disposed", "map_modes");
        }

        /// <summary>
        /// Subclass resource cleanup.
        /// Release compute buffers, etc.
        /// </summary>
        protected abstract void DisposeResources();

        #region Utility Methods

        /// <summary>
        /// Calculate thread groups for compute shader dispatch.
        /// </summary>
        protected (int x, int y) CalculateThreadGroups(int width, int height)
        {
            int groupsX = Mathf.CeilToInt(width / (float)THREAD_GROUP_SIZE);
            int groupsY = Mathf.CeilToInt(height / (float)THREAD_GROUP_SIZE);
            return (groupsX, groupsY);
        }

        /// <summary>
        /// Create or resize a compute buffer for province values.
        /// </summary>
        protected ComputeBuffer EnsureProvinceValueBuffer(ComputeBuffer existing, int minSize)
        {
            int requiredSize = Mathf.Max(65536, minSize);

            if (existing != null && existing.count >= requiredSize)
            {
                return existing;
            }

            existing?.Release();
            return new ComputeBuffer(requiredSize, sizeof(float));
        }

        /// <summary>
        /// Create or resize a compute buffer for gradient colors.
        /// </summary>
        protected ComputeBuffer EnsureGradientBuffer(ComputeBuffer existing, int colorCount)
        {
            if (existing != null && existing.count == colorCount)
            {
                return existing;
            }

            existing?.Release();
            return new ComputeBuffer(colorCount, sizeof(float) * 4);
        }

        /// <summary>
        /// Sample gradient at specified points and upload to buffer.
        /// </summary>
        protected void UploadGradientColors(ComputeBuffer buffer, ColorGradient gradient, int sampleCount)
        {
            Vector4[] colors = new Vector4[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount > 1 ? (float)i / (sampleCount - 1) : 0.5f;
                Color32 color = gradient.Evaluate(t);

                colors[i] = new Vector4(
                    color.r / 255f,
                    color.g / 255f,
                    color.b / 255f,
                    color.a / 255f
                );
            }

            buffer.SetData(colors);
        }

        /// <summary>
        /// Convert Color to Vector4 for shader.
        /// </summary>
        protected Vector4 ColorToVector4(Color color)
        {
            return new Vector4(color.r, color.g, color.b, color.a);
        }

        #endregion
    }
}
