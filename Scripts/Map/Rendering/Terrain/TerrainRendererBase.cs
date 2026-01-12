using UnityEngine;

namespace Map.Rendering.Terrain
{
    /// <summary>
    /// ENGINE: Abstract base class for terrain renderer implementations.
    /// Provides common utilities and state management.
    ///
    /// Pattern 20: Pluggable Implementation (Interface + Registry)
    /// </summary>
    public abstract class TerrainRendererBase : ITerrainRenderer
    {
        // Required by interface
        public abstract string RendererId { get; }
        public abstract string DisplayName { get; }
        public virtual bool RequiresPerFrameUpdate => false;

        // Common state
        protected MapTextureManager textureManager;
        protected TerrainRendererContext context;
        protected bool isInitialized;

        // Blend map generation parameters
        protected int sampleRadius = 2; // 5x5 sampling
        protected float blendSharpness = 1.0f; // Linear blending

        // Thread group size for compute shaders
        protected const int THREAD_GROUP_SIZE = 8;

        // Cached shader property IDs
        protected static readonly int TerrainBrightnessID = Shader.PropertyToID("_TerrainBrightness");
        protected static readonly int TerrainSaturationID = Shader.PropertyToID("_TerrainSaturation");
        protected static readonly int DetailTilingID = Shader.PropertyToID("_DetailTiling");
        protected static readonly int DetailStrengthID = Shader.PropertyToID("_DetailStrength");
        protected static readonly int NormalMapStrengthID = Shader.PropertyToID("_NormalMapStrength");
        protected static readonly int NormalMapAmbientID = Shader.PropertyToID("_NormalMapAmbient");
        protected static readonly int NormalMapHighlightID = Shader.PropertyToID("_NormalMapHighlight");

        public virtual void Initialize(MapTextureManager textureManager, TerrainRendererContext context)
        {
            this.textureManager = textureManager;
            this.context = context;
            this.isInitialized = true;

            OnInitialize();
        }

        /// <summary>
        /// Override to perform implementation-specific initialization
        /// </summary>
        protected virtual void OnInitialize() { }

        public abstract (RenderTexture detailIndex, RenderTexture detailMask) GenerateBlendMaps(
            RenderTexture provinceIDTexture,
            ComputeBuffer provinceTerrainBuffer,
            int width,
            int height);

        public virtual void ApplyToMaterial(Material material, TerrainStyleParams styleParams)
        {
            if (material == null) return;

            material.SetFloat(TerrainBrightnessID, styleParams.Brightness);
            material.SetFloat(TerrainSaturationID, styleParams.Saturation);
            material.SetFloat(DetailTilingID, styleParams.DetailTiling);
            material.SetFloat(DetailStrengthID, styleParams.DetailStrength);
            material.SetFloat(NormalMapStrengthID, styleParams.NormalMapStrength);
            material.SetFloat(NormalMapAmbientID, styleParams.NormalMapAmbient);
            material.SetFloat(NormalMapHighlightID, styleParams.NormalMapHighlight);
        }

        public virtual void OnRenderFrame() { }

        public int GetSampleRadius() => sampleRadius;

        public virtual void SetSampleRadius(int radius)
        {
            sampleRadius = Mathf.Clamp(radius, 1, 10);
        }

        public float GetBlendSharpness() => blendSharpness;

        public virtual void SetBlendSharpness(float sharpness)
        {
            blendSharpness = Mathf.Clamp(sharpness, 0.1f, 5.0f);
        }

        /// <summary>
        /// Calculate thread groups for compute shader dispatch
        /// </summary>
        protected (int x, int y) CalculateThreadGroups(int width, int height)
        {
            int threadGroupsX = (width + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (height + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            return (threadGroupsX, threadGroupsY);
        }

        /// <summary>
        /// Create RenderTexture for blend maps with explicit format
        /// </summary>
        protected RenderTexture CreateBlendMapTexture(int width, int height, string name)
        {
            var descriptor = new RenderTextureDescriptor(
                width,
                height,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                0 // No depth buffer
            );

            descriptor.enableRandomWrite = true;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            var texture = new RenderTexture(descriptor);
            texture.name = name;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Create();

            return texture;
        }

        public virtual void Dispose()
        {
            isInitialized = false;
        }
    }
}
