using UnityEngine;
using Core.Queries;
using Utils;

namespace Map.Rendering
{
    /// <summary>
    /// ENGINE LAYER - Manages fog of war visibility state
    /// Universal grand strategy fog of war mechanics:
    /// - Unexplored: Never seen (0.0)
    /// - Explored: Previously seen but not currently visible (0.5)
    /// - Visible: Currently owned or adjacent to owned (1.0)
    ///
    /// Visual appearance (colors, effects) is defined by GAME layer shaders
    /// </summary>
    public class FogOfWarSystem : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private MapTextureManager textureManager;
        [SerializeField] private ComputeShader fogOfWarCompute;

        [Header("Debug")]
        [SerializeField] private bool logVisibilityUpdates = false;

        // State
        private RenderTexture fogOfWarTexture;
        private ProvinceQueries provinceQueries;
        private int provinceCount;
        private ushort playerCountryID = 0;
        private Material mapMaterial;

        // Visibility tracking (CPU-side cache for exploration state)
        private float[] provinceVisibility; // 0.0 = unexplored, 0.5 = explored, 1.0 = visible

        private bool isInitialized = false;
        private bool fogEnabled = false;

        /// <summary>
        /// Initialize fog of war system
        /// </summary>
        public void Initialize(ProvinceQueries queries, int maxProvinces)
        {
            if (isInitialized)
            {
                ArchonLogger.LogMapRenderingWarning("FogOfWarSystem: Already initialized!");
                return;
            }

            // Get texture manager from the same GameObject
            if (textureManager == null)
            {
                textureManager = GetComponent<MapTextureManager>();
            }

            provinceQueries = queries;
            provinceCount = maxProvinces;
            fogOfWarTexture = textureManager.FogOfWarTexture;

            if (fogOfWarTexture == null)
            {
                ArchonLogger.LogMapRenderingError("FogOfWarSystem: FogOfWarTexture is null!");
                return;
            }

            // Initialize visibility array (all explored for now - no exploration mechanics yet)
            provinceVisibility = new float[provinceCount];
            for (int i = 0; i < provinceCount; i++)
            {
                provinceVisibility[i] = 0.5f; // Explored (not visible until owned/adjacent)
            }

            // Find material to control fog of war enabled state
            var meshRenderer = FindFirstObjectByType<MeshRenderer>();
            if (meshRenderer != null)
            {
                mapMaterial = meshRenderer.material;
                // Disable fog of war initially (until player selects country)
                mapMaterial.SetFloat("_FogOfWarEnabled", 0f);
            }

            isInitialized = true;
            ArchonLogger.LogMapRendering($"FogOfWarSystem: Initialized for {provinceCount} provinces (fog disabled until player selection)");
        }

        /// <summary>
        /// Set the player's country and update visibility
        /// </summary>
        public void SetPlayerCountry(ushort countryID)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogMapRenderingError("FogOfWarSystem: Not initialized!");
                return;
            }

            playerCountryID = countryID;

            // Enable fog of war now that player has selected a country
            if (mapMaterial != null && !fogEnabled)
            {
                mapMaterial.SetFloat("_FogOfWarEnabled", 1f);
                fogEnabled = true;
                if (logVisibilityUpdates)
                {
                    ArchonLogger.LogMapRendering("FogOfWarSystem: Fog of war enabled");
                }
            }

            if (logVisibilityUpdates)
            {
                ArchonLogger.LogMapRendering($"FogOfWarSystem: Player country set to {countryID}");
            }

            // Update visibility based on new player country
            UpdateVisibility();
        }

        /// <summary>
        /// Update visibility based on player's current ownership
        /// Marks owned provinces and adjacent provinces as visible
        /// </summary>
        public void UpdateVisibility()
        {
            if (!isInitialized || playerCountryID == 0)
                return;

            // Get all provinces owned by player
            var ownedProvinces = provinceQueries.GetCountryProvinces(playerCountryID, Unity.Collections.Allocator.Temp);

            if (!ownedProvinces.IsCreated)
                return;

            int visibleCount = 0;
            int exploredCount = 0;

            // Mark all currently visible provinces as explored first
            // (so if we lose a province, it becomes "explored" not "unexplored")
            for (int i = 0; i < provinceCount; i++)
            {
                if (provinceVisibility[i] >= 1.0f) // Was visible
                {
                    provinceVisibility[i] = 0.5f; // Now explored
                }
            }

            // Mark owned provinces as visible
            foreach (ushort provinceID in ownedProvinces)
            {
                if (provinceID < provinceCount)
                {
                    provinceVisibility[provinceID] = 1.0f; // Visible
                    visibleCount++;

                    // TODO: Mark adjacent provinces as visible too
                    // Requires neighbor data from ProvinceQueries
                }
            }

            ownedProvinces.Dispose();

            // Count explored provinces
            for (int i = 0; i < provinceCount; i++)
            {
                if (provinceVisibility[i] == 0.5f)
                    exploredCount++;
            }

            // Upload to GPU texture
            UpdateFogTexture();

            if (logVisibilityUpdates)
            {
                ArchonLogger.LogMapRendering($"FogOfWarSystem: Visibility updated - {visibleCount} visible, {exploredCount} explored");
            }
        }

        /// <summary>
        /// Upload visibility data to GPU texture using compute shader
        /// Maps province visibility to screen space using PopulateFogOfWarTexture.compute
        /// Follows unity-compute-shader-coordination.md pattern (no Graphics.Blit)
        /// </summary>
        private void UpdateFogTexture()
        {
            if (fogOfWarTexture == null || textureManager == null)
                return;

            // Load compute shader if not assigned
            if (fogOfWarCompute == null)
            {
                fogOfWarCompute = Resources.Load<ComputeShader>("PopulateFogOfWarTexture");
                if (fogOfWarCompute == null)
                {
                    ArchonLogger.LogMapRenderingError("FogOfWarSystem: PopulateFogOfWarTexture.compute not found in Resources!");
                    return;
                }
            }

            RenderTexture provinceIDTex = textureManager.ProvinceIDTexture;
            if (provinceIDTex == null)
            {
                ArchonLogger.LogMapRenderingError("FogOfWarSystem: ProvinceIDTexture is null!");
                return;
            }

            int width = fogOfWarTexture.width;
            int height = fogOfWarTexture.height;

            // Create GPU buffer for visibility data
            ComputeBuffer visibilityBuffer = new ComputeBuffer(provinceCount, sizeof(float));
            visibilityBuffer.SetData(provinceVisibility);

            // Set compute shader parameters
            int kernel = fogOfWarCompute.FindKernel("PopulateFogOfWar");
            fogOfWarCompute.SetTexture(kernel, "ProvinceIDTexture", provinceIDTex);
            fogOfWarCompute.SetTexture(kernel, "FogOfWarTexture", fogOfWarTexture);
            fogOfWarCompute.SetBuffer(kernel, "ProvinceVisibilityBuffer", visibilityBuffer);
            fogOfWarCompute.SetInt("MapWidth", width);
            fogOfWarCompute.SetInt("MapHeight", height);

            // Dispatch compute shader (8x8 thread groups)
            const int THREAD_GROUP_SIZE = 8;
            int threadGroupsX = (width + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (height + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            fogOfWarCompute.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

            // Cleanup
            visibilityBuffer.Release();

            if (logVisibilityUpdates)
            {
                ArchonLogger.LogMapRendering("FogOfWarSystem: Fog texture updated via compute shader");
            }
        }

        /// <summary>
        /// Reveal a specific province (mark as explored)
        /// </summary>
        public void RevealProvince(ushort provinceID)
        {
            if (!isInitialized || provinceID >= provinceCount)
                return;

            // Only reveal if unexplored
            if (provinceVisibility[provinceID] < 0.5f)
            {
                provinceVisibility[provinceID] = 0.5f; // Explored
                UpdateFogTexture();
            }
        }

        void OnDestroy()
        {
            // Cleanup handled by MapTextureManager
        }
    }
}
