using UnityEngine;
using Core.Queries;

namespace Map.Rendering.FogOfWar
{
    /// <summary>
    /// ENGINE: Abstract base class for fog of war renderer implementations.
    /// Provides common utilities and state management.
    ///
    /// Pattern 20: Pluggable Implementation (Interface + Registry)
    /// </summary>
    public abstract class FogOfWarRendererBase : IFogOfWarRenderer
    {
        // Required by interface
        public abstract string RendererId { get; }
        public abstract string DisplayName { get; }
        public virtual bool RequiresPerFrameUpdate => false;

        // Common state
        protected MapTextureManager textureManager;
        protected FogOfWarRendererContext context;
        protected bool isInitialized;
        protected bool fogEnabled;
        protected ushort playerCountryID;

        // Visibility tracking (CPU-side cache)
        protected float[] provinceVisibility;

        // Thread group size for compute shaders
        protected const int THREAD_GROUP_SIZE = 8;

        /// <summary>
        /// Visibility constants
        /// </summary>
        protected const float VISIBILITY_UNEXPLORED = 0.0f;
        protected const float VISIBILITY_EXPLORED = 0.5f;
        protected const float VISIBILITY_VISIBLE = 1.0f;

        public bool IsEnabled => fogEnabled;

        public virtual void Initialize(MapTextureManager textureManager, FogOfWarRendererContext context)
        {
            this.textureManager = textureManager;
            this.context = context;
            this.isInitialized = true;
            this.fogEnabled = false;
            this.playerCountryID = 0;

            // Initialize visibility array (all explored by default - no exploration mechanics yet)
            provinceVisibility = new float[context.MaxProvinces];
            for (int i = 0; i < context.MaxProvinces; i++)
            {
                provinceVisibility[i] = VISIBILITY_EXPLORED;
            }

            OnInitialize();
        }

        /// <summary>
        /// Override to perform implementation-specific initialization
        /// </summary>
        protected virtual void OnInitialize() { }

        public virtual void SetPlayerCountry(ushort countryID)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError($"{RendererId}FogOfWarRenderer: Not initialized!", "map_rendering");
                return;
            }

            playerCountryID = countryID;

            // Enable fog when player selects country
            if (!fogEnabled && countryID != 0)
            {
                fogEnabled = true;
            }

            UpdateVisibility();
        }

        public abstract void UpdateVisibility();

        public virtual void RevealProvince(ushort provinceID)
        {
            if (!isInitialized || provinceID >= provinceVisibility.Length)
                return;

            // Only reveal if unexplored
            if (provinceVisibility[provinceID] < VISIBILITY_EXPLORED)
            {
                provinceVisibility[provinceID] = VISIBILITY_EXPLORED;
                OnVisibilityChanged();
            }
        }

        public virtual void SetEnabled(bool enabled)
        {
            fogEnabled = enabled;
        }

        public virtual void ApplyToMaterial(Material material, FogOfWarStyleParams styleParams)
        {
            if (material == null) return;

            material.SetFloat("_FogOfWarEnabled", fogEnabled ? 1f : 0f);
            material.SetColor("_FogUnexploredColor", styleParams.UnexploredColor);
            material.SetColor("_FogExploredColor", styleParams.ExploredTint);
            material.SetFloat("_FogExploredDesaturation", styleParams.ExploredDesaturation);
            material.SetColor("_FogNoiseColor", styleParams.NoiseColor);
            material.SetFloat("_FogNoiseScale", styleParams.NoiseScale);
            material.SetFloat("_FogNoiseStrength", styleParams.NoiseStrength);
            material.SetFloat("_FogNoiseSpeed", styleParams.NoiseSpeed);
        }

        public virtual void OnRenderFrame() { }

        public float GetProvinceVisibility(ushort provinceID)
        {
            if (provinceID >= provinceVisibility.Length)
                return VISIBILITY_UNEXPLORED;
            return provinceVisibility[provinceID];
        }

        public ushort GetPlayerCountry() => playerCountryID;

        /// <summary>
        /// Called when visibility state changes - override to update textures
        /// </summary>
        protected virtual void OnVisibilityChanged() { }

        /// <summary>
        /// Calculate thread groups for compute shader dispatch
        /// </summary>
        protected (int x, int y) CalculateThreadGroups(int width, int height)
        {
            int threadGroupsX = (width + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            int threadGroupsY = (height + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
            return (threadGroupsX, threadGroupsY);
        }

        public virtual void Dispose()
        {
            provinceVisibility = null;
            isInitialized = false;
        }
    }
}
