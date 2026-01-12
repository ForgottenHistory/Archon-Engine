using UnityEngine;
using UnityEngine.Rendering;

namespace Map.Rendering.Terrain
{
    /// <summary>
    /// ENGINE: Default terrain renderer using Imperator Rome-style 4-channel blending.
    ///
    /// Features:
    /// - GPU-accelerated blend map generation via compute shader
    /// - Configurable sample radius (5x5 default) for terrain transition width
    /// - Configurable blend sharpness (1.0 = linear, >1 = sharper transitions)
    /// - Outputs DetailIndexTexture (4 terrain indices) + DetailMaskTexture (4 blend weights)
    ///
    /// Pattern 20: Pluggable Implementation (Interface + Registry)
    /// </summary>
    public class DefaultTerrainRenderer : TerrainRendererBase
    {
        public override string RendererId => "Default";
        public override string DisplayName => "Default (4-Channel Blend)";

        private ComputeShader terrainBlendMapCompute;
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

        public DefaultTerrainRenderer() { }

        public DefaultTerrainRenderer(ComputeShader computeShader)
        {
            this.terrainBlendMapCompute = computeShader;
        }

        protected override void OnInitialize()
        {
            // Use compute shader from context if not provided in constructor
            if (terrainBlendMapCompute == null)
            {
                terrainBlendMapCompute = context.TerrainBlendMapCompute;
            }

            if (terrainBlendMapCompute == null)
            {
                ArchonLogger.LogError("DefaultTerrainRenderer: No compute shader provided!", "map_rendering");
                return;
            }

            generateKernel = terrainBlendMapCompute.FindKernel("GenerateBlendMaps");

            ArchonLogger.Log($"DefaultTerrainRenderer: Initialized (radius={sampleRadius}, sharpness={blendSharpness})", "map_rendering");
        }

        public override (RenderTexture detailIndex, RenderTexture detailMask) GenerateBlendMaps(
            RenderTexture provinceIDTexture,
            ComputeBuffer provinceTerrainBuffer,
            int width,
            int height)
        {
            if (terrainBlendMapCompute == null)
            {
                ArchonLogger.LogError("DefaultTerrainRenderer: No compute shader - cannot generate blend maps", "map_rendering");
                return (null, null);
            }

            if (provinceIDTexture == null || provinceTerrainBuffer == null)
            {
                ArchonLogger.LogError("DefaultTerrainRenderer: Missing input textures/buffers", "map_rendering");
                return (null, null);
            }

            ArchonLogger.Log($"DefaultTerrainRenderer: Generating blend maps ({width}x{height}, radius={sampleRadius})", "map_rendering");

            // Create output textures
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

                // Dispatch compute shader
                var (threadGroupsX, threadGroupsY) = CalculateThreadGroups(width, height);
                terrainBlendMapCompute.Dispatch(generateKernel, threadGroupsX, threadGroupsY, 1);

                // GPU synchronization - wait for compute shader to complete
                var syncRequest = AsyncGPUReadback.Request(detailIndexTexture);
                syncRequest.WaitForCompletion();

                ArchonLogger.Log("DefaultTerrainRenderer: Blend map generation complete", "map_rendering");

                return (detailIndexTexture, detailMaskTexture);
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"DefaultTerrainRenderer: Failed to generate blend maps: {e.Message}", "map_rendering");

                // Clean up on failure
                if (detailIndexTexture != null)
                    detailIndexTexture.Release();
                if (detailMaskTexture != null)
                    detailMaskTexture.Release();

                return (null, null);
            }
        }

        public override void Dispose()
        {
            terrainBlendMapCompute = null;
            base.Dispose();
        }
    }
}
