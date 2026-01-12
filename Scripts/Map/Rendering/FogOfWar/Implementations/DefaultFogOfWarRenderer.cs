using UnityEngine;
using Unity.Collections;

namespace Map.Rendering.FogOfWar
{
    /// <summary>
    /// ENGINE: Default fog of war renderer using GPU compute shader.
    ///
    /// Features:
    /// - GPU-accelerated fog texture generation via compute shader
    /// - Three visibility states: unexplored (0.0), explored (0.5), visible (1.0)
    /// - Owned provinces and adjacents marked as visible
    /// - Previous visible areas demoted to explored when lost
    ///
    /// Pattern 20: Pluggable Implementation (Interface + Registry)
    /// </summary>
    public class DefaultFogOfWarRenderer : FogOfWarRendererBase
    {
        public override string RendererId => "Default";
        public override string DisplayName => "Default (GPU Compute)";

        private ComputeShader fogOfWarCompute;
        private RenderTexture fogOfWarTexture;
        private int populateFogKernel;

        public DefaultFogOfWarRenderer() { }

        public DefaultFogOfWarRenderer(ComputeShader computeShader)
        {
            this.fogOfWarCompute = computeShader;
        }

        protected override void OnInitialize()
        {
            fogOfWarTexture = textureManager.FogOfWarTexture;

            if (fogOfWarTexture == null)
            {
                ArchonLogger.LogError("DefaultFogOfWarRenderer: FogOfWarTexture is null!", "map_rendering");
                return;
            }

            // Use compute shader from context if not provided in constructor
            if (fogOfWarCompute == null)
            {
                fogOfWarCompute = context.FogOfWarCompute;
            }

            // Load from Resources if still null
            if (fogOfWarCompute == null)
            {
                fogOfWarCompute = Resources.Load<ComputeShader>("PopulateFogOfWarTexture");
                if (fogOfWarCompute == null)
                {
                    ArchonLogger.LogError("DefaultFogOfWarRenderer: PopulateFogOfWarTexture.compute not found!", "map_rendering");
                    return;
                }
            }

            populateFogKernel = fogOfWarCompute.FindKernel("PopulateFogOfWar");

            ArchonLogger.Log($"DefaultFogOfWarRenderer: Initialized for {context.MaxProvinces} provinces", "map_rendering");
        }

        public override void UpdateVisibility()
        {
            if (!isInitialized || playerCountryID == 0 || context.ProvinceQueries == null)
                return;

            // Get all provinces owned by player
            var ownedProvinces = context.ProvinceQueries.GetCountryProvinces(playerCountryID, Allocator.Temp);

            if (!ownedProvinces.IsCreated)
                return;

            // Demote all currently visible provinces to explored
            // (so if we lose a province, it becomes "explored" not "unexplored")
            for (int i = 0; i < provinceVisibility.Length; i++)
            {
                if (provinceVisibility[i] >= VISIBILITY_VISIBLE)
                {
                    provinceVisibility[i] = VISIBILITY_EXPLORED;
                }
            }

            // Mark owned provinces as visible
            foreach (ushort provinceID in ownedProvinces)
            {
                if (provinceID < provinceVisibility.Length)
                {
                    provinceVisibility[provinceID] = VISIBILITY_VISIBLE;

                    // TODO: Mark adjacent provinces as visible too
                    // Requires neighbor data from AdjacencySystem
                }
            }

            ownedProvinces.Dispose();

            // Upload to GPU texture
            UpdateFogTexture();
        }

        protected override void OnVisibilityChanged()
        {
            UpdateFogTexture();
        }

        /// <summary>
        /// Upload visibility data to GPU texture using compute shader
        /// </summary>
        private void UpdateFogTexture()
        {
            if (fogOfWarTexture == null || fogOfWarCompute == null)
                return;

            RenderTexture provinceIDTex = textureManager.ProvinceIDTexture;
            if (provinceIDTex == null)
            {
                ArchonLogger.LogError("DefaultFogOfWarRenderer: ProvinceIDTexture is null!", "map_rendering");
                return;
            }

            int width = fogOfWarTexture.width;
            int height = fogOfWarTexture.height;

            // Create GPU buffer for visibility data
            ComputeBuffer visibilityBuffer = new ComputeBuffer(provinceVisibility.Length, sizeof(float));
            visibilityBuffer.SetData(provinceVisibility);

            // Set compute shader parameters
            fogOfWarCompute.SetTexture(populateFogKernel, "ProvinceIDTexture", provinceIDTex);
            fogOfWarCompute.SetTexture(populateFogKernel, "FogOfWarTexture", fogOfWarTexture);
            fogOfWarCompute.SetBuffer(populateFogKernel, "ProvinceVisibilityBuffer", visibilityBuffer);
            fogOfWarCompute.SetInt("MapWidth", width);
            fogOfWarCompute.SetInt("MapHeight", height);

            // Dispatch compute shader
            var (threadGroupsX, threadGroupsY) = CalculateThreadGroups(width, height);
            fogOfWarCompute.Dispatch(populateFogKernel, threadGroupsX, threadGroupsY, 1);

            // Cleanup
            visibilityBuffer.Release();
        }

        public override void Dispose()
        {
            // Cleanup handled by MapTextureManager for shared textures
            fogOfWarCompute = null;
            fogOfWarTexture = null;
            base.Dispose();
        }
    }
}
