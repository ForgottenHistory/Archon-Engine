using UnityEngine;
using UnityEngine.Rendering;
using Utils;
using Map.Rendering;

namespace Map.Rendering.Terrain
{
    /// <summary>
    /// ENGINE: Generates terrain blend maps for Imperator Rome-style 4-channel blending
    /// Pre-computes DetailIndexTexture + DetailMaskTexture at load time (~50-100ms)
    ///
    /// Algorithm:
    /// - Sample ProvinceIDTexture in 5x5 radius per pixel
    /// - Count terrain types via ProvinceTerrainBuffer lookup
    /// - Take top 4 terrain types by frequency
    /// - Normalize to weights (0-1 range)
    /// - Write indices to DetailIndexTexture, weights to DetailMaskTexture
    ///
    /// Output:
    /// - DetailIndexTexture (RGBA8): 4 material indices per pixel
    /// - DetailMaskTexture (RGBA8): 4 blend weights per pixel
    ///
    /// Usage: MapDataLoader calls Generate() after province terrain analysis complete
    /// </summary>
    public class TerrainBlendMapGenerator : MonoBehaviour
    {
        [Header("Compute Shader")]
        [SerializeField] private ComputeShader terrainBlendMapCompute;

        [Header("Parameters")]
        [Tooltip("Sample radius for terrain blending (2 = 5x5 sampling, 5 = 11x11 sampling). Higher values create smoother, wider transitions between terrain types.")]
        [SerializeField] private int sampleRadius = 2; // 5x5 sampling (radius 2)

        [Tooltip("Blend sharpness control (1.0 = linear blending, >1.0 = sharper transitions, <1.0 = softer/wider blending). Uses power function to adjust weight distribution.")]
        [SerializeField] private float blendSharpness = 1.0f; // 1.0 = linear, >1 = sharper, <1 = softer

        [Header("Debug")]
        [SerializeField] private bool logGeneration = true;

        // Kernel index
        private int generateKernel;

        // Cached shader property IDs
        private static readonly int ProvinceIDTextureID = Shader.PropertyToID("ProvinceIDTexture");
        private static readonly int ProvinceTerrainBufferID = Shader.PropertyToID("ProvinceTerrainBuffer");
        private static readonly int DetailIndexTextureID = Shader.PropertyToID("DetailIndexTexture");
        private static readonly int DetailMaskTextureID = Shader.PropertyToID("DetailMaskTexture");
        private static readonly int MapWidthID = Shader.PropertyToID("MapWidth");
        private static readonly int MapHeightID = Shader.PropertyToID("MapHeight");
        private static readonly int SampleRadiusID = Shader.PropertyToID("SampleRadius");
        private static readonly int BlendSharpnessID = Shader.PropertyToID("BlendSharpness");

        // Pluggable renderer support
        private bool rendererRegistered = false;
        private bool isInitialized = false;

        /// <summary>
        /// Initialize compute shader kernels. Called by ArchonEngine.
        /// </summary>
        public void InitializeKernels()
        {
            if (isInitialized) return;
            isInitialized = true;

            if (terrainBlendMapCompute == null)
            {
                ArchonLogger.LogError("TerrainBlendMapGenerator: No compute shader assigned!", "map_rendering");
                return;
            }

            generateKernel = terrainBlendMapCompute.FindKernel("GenerateBlendMaps");
        }

        /// <summary>
        /// Initialize and register default terrain renderer with MapRendererRegistry.
        /// Call this during map initialization.
        /// </summary>
        public void Initialize(MapTextureManager textureManager)
        {
            InitializeKernels();
            RegisterDefaultRenderer(textureManager);
        }

        /// <summary>
        /// Register ENGINE's default terrain renderer with MapRendererRegistry.
        /// GAME layer can register additional custom renderers via MapRendererRegistry.Instance.RegisterTerrainRenderer().
        /// </summary>
        private void RegisterDefaultRenderer(MapTextureManager textureManager)
        {
            if (rendererRegistered) return;

            var registry = MapRendererRegistry.Instance;
            if (registry == null)
            {
                ArchonLogger.LogWarning("TerrainBlendMapGenerator: MapRendererRegistry not found, cannot register renderer", "map_rendering");
                return;
            }

            // Build context for renderer initialization
            var context = new TerrainRendererContext
            {
                TerrainBlendMapCompute = terrainBlendMapCompute,
                TerrainRGBLookup = null, // Can be set if needed
                MaxTerrainTypes = 32
            };

            // Create and register default renderer
            var defaultRenderer = new DefaultTerrainRenderer(terrainBlendMapCompute);
            defaultRenderer.Initialize(textureManager, context);
            defaultRenderer.SetSampleRadius(sampleRadius);
            defaultRenderer.SetBlendSharpness(blendSharpness);
            registry.RegisterTerrainRenderer(defaultRenderer);

            rendererRegistered = true;
            ArchonLogger.Log("TerrainBlendMapGenerator: Registered default terrain renderer", "map_rendering");
        }

        /// <summary>
        /// Get the active terrain renderer from registry.
        /// </summary>
        public ITerrainRenderer GetActiveTerrainRenderer(string rendererId = null)
        {
            var registry = MapRendererRegistry.Instance;
            if (registry == null) return null;

            return registry.GetTerrainRenderer(rendererId);
        }

        /// <summary>
        /// Generate terrain blend maps (DetailIndexTexture + DetailMaskTexture)
        /// Call this after ProvinceTerrainAnalyzer completes
        /// </summary>
        /// <param name="provinceIDTexture">Province ID texture (5632x2048 RG16)</param>
        /// <param name="provinceTerrainBuffer">Province terrain buffer (65536 entries, uint per province)</param>
        /// <param name="width">Map width (5632)</param>
        /// <param name="height">Map height (2048)</param>
        /// <returns>Tuple of (DetailIndexTexture, DetailMaskTexture)</returns>
        public (RenderTexture detailIndex, RenderTexture detailMask) Generate(
            RenderTexture provinceIDTexture,
            ComputeBuffer provinceTerrainBuffer,
            int width,
            int height)
        {
            if (terrainBlendMapCompute == null)
            {
                ArchonLogger.LogError("TerrainBlendMapGenerator: No compute shader - cannot generate blend maps", "map_rendering");
                return (null, null);
            }

            if (provinceIDTexture == null || provinceTerrainBuffer == null)
            {
                ArchonLogger.LogError("TerrainBlendMapGenerator: Missing input textures/buffers", "map_rendering");
                return (null, null);
            }

            if (logGeneration)
            {
                ArchonLogger.Log($"TerrainBlendMapGenerator: Starting blend map generation ({width}x{height}, radius={sampleRadius})", "map_rendering");
            }

            // Create output textures with explicit GraphicsFormat (UAV-compatible)
            // CRITICAL: Use R8G8B8A8_UNorm to avoid TYPELESS format (see explicit-graphics-format.md)
            RenderTexture detailIndexTexture = CreateBlendMapTexture(width, height, "TerrainDetailIndex");
            RenderTexture detailMaskTexture = CreateBlendMapTexture(width, height, "TerrainDetailMask");

            try
            {
                // Set input textures/buffers
                terrainBlendMapCompute.SetTexture(generateKernel, ProvinceIDTextureID, provinceIDTexture);
                terrainBlendMapCompute.SetBuffer(generateKernel, ProvinceTerrainBufferID, provinceTerrainBuffer);

                // Set output textures
                terrainBlendMapCompute.SetTexture(generateKernel, DetailIndexTextureID, detailIndexTexture);
                terrainBlendMapCompute.SetTexture(generateKernel, DetailMaskTextureID, detailMaskTexture);

                // Set parameters
                terrainBlendMapCompute.SetInt(MapWidthID, width);
                terrainBlendMapCompute.SetInt(MapHeightID, height);
                terrainBlendMapCompute.SetInt(SampleRadiusID, sampleRadius);
                terrainBlendMapCompute.SetFloat(BlendSharpnessID, blendSharpness);

                // Dispatch compute shader (8x8 thread groups)
                int threadGroupsX = (width + 7) / 8;
                int threadGroupsY = (height + 7) / 8;

                if (logGeneration)
                {
                    ArchonLogger.Log($"TerrainBlendMapGenerator: Dispatching ({threadGroupsX}x{threadGroupsY} thread groups)", "map_rendering");
                }

                terrainBlendMapCompute.Dispatch(generateKernel, threadGroupsX, threadGroupsY, 1);

                // CRITICAL: GPU synchronization - wait for compute shader to complete
                // Subsequent shaders (MapModeTerrain) will read these textures
                // See unity-compute-shader-coordination.md for why this is needed
                var syncRequest = AsyncGPUReadback.Request(detailIndexTexture);
                syncRequest.WaitForCompletion();

                if (logGeneration)
                {
                    ArchonLogger.Log("TerrainBlendMapGenerator: Blend map generation complete", "map_rendering");

                    // DEBUG: Read back a few pixels to verify data
                    RenderTexture.active = detailMaskTexture;
                    Texture2D temp = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                    temp.ReadPixels(new Rect(width / 2, height / 2, 1, 1), 0, 0);
                    temp.Apply();
                    RenderTexture.active = null;

                    Color testPixel = temp.GetPixel(0, 0);
                    ArchonLogger.Log($"TerrainBlendMapGenerator: Test pixel at center = RGBA({testPixel.r:F3}, {testPixel.g:F3}, {testPixel.b:F3}, {testPixel.a:F3})", "map_rendering");
                    Destroy(temp);
                }

                return (detailIndexTexture, detailMaskTexture);
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"TerrainBlendMapGenerator: Failed to generate blend maps: {e.Message}", "map_rendering");

                // Clean up on failure
                if (detailIndexTexture != null)
                    detailIndexTexture.Release();
                if (detailMaskTexture != null)
                    detailMaskTexture.Release();

                return (null, null);
            }
        }

        /// <summary>
        /// Create RenderTexture for blend maps with explicit format
        /// Uses R8G8B8A8_UNorm to avoid TYPELESS format issues (see explicit-graphics-format.md)
        /// </summary>
        private RenderTexture CreateBlendMapTexture(int width, int height, string name)
        {
            // Use RenderTextureDescriptor with explicit GraphicsFormat
            // CRITICAL: Must use R8G8B8A8_UNorm for UAV compatibility
            var descriptor = new RenderTextureDescriptor(
                width,
                height,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                0  // No depth buffer
            );

            // Configure for compute shader write
            descriptor.enableRandomWrite = true;  // Required for RWTexture2D
            descriptor.useMipMap = false;         // No mipmaps for blend maps
            descriptor.autoGenerateMips = false;

            // Create texture
            var texture = new RenderTexture(descriptor);
            texture.name = name;
            texture.filterMode = FilterMode.Bilinear;  // Bilinear filtering for smooth transitions
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Create();

            return texture;
        }
    }
}
