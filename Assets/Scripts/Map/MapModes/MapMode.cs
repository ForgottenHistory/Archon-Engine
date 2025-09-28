using UnityEngine;
using Map.Rendering;

namespace Map.MapModes
{
    /// <summary>
    /// Base class for all map display modes
    /// Follows dual-layer architecture: updates GPU textures based on simulation data
    /// Each mapmode is responsible for updating textures and shader settings
    /// </summary>
    public abstract class MapMode
    {
        /// <summary>
        /// Human-readable name for this mapmode
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Shader mode ID used in material _MapMode property
        /// </summary>
        public abstract int ShaderModeID { get; }

        /// <summary>
        /// Shader keyword to enable for this mapmode (e.g., "MAP_MODE_COUNTRY")
        /// </summary>
        public abstract string ShaderKeyword { get; }

        /// <summary>
        /// Update GPU textures based on current simulation state
        /// This is where the Core → Map data flow happens
        /// </summary>
        /// <param name="textureManager">Manages all map textures</param>
        public abstract void UpdateGPUTextures(MapTextureManager textureManager);

        /// <summary>
        /// Apply mapmode-specific shader settings
        /// Called after texture updates to configure rendering
        /// </summary>
        /// <param name="mapMaterial">The main map rendering material</param>
        public abstract void ApplyShaderSettings(Material mapMaterial);

        /// <summary>
        /// Called when this mapmode becomes active
        /// Use for initialization or one-time setup
        /// </summary>
        public virtual void OnActivate() { }

        /// <summary>
        /// Called when this mapmode becomes inactive
        /// Use for cleanup or state saving
        /// </summary>
        public virtual void OnDeactivate() { }

        /// <summary>
        /// Whether this mapmode requires frequent updates
        /// True for dynamic modes (political), false for static modes (terrain)
        /// </summary>
        public virtual bool RequiresFrequentUpdates => false;

        /// <summary>
        /// Helper method to safely enable a shader keyword
        /// </summary>
        protected void EnableShaderKeyword(Material material, string keyword)
        {
            if (material != null && !string.IsNullOrEmpty(keyword))
            {
                material.EnableKeyword(keyword);
            }
        }

        /// <summary>
        /// Helper method to disable all mapmode keywords
        /// </summary>
        protected void DisableAllMapModeKeywords(Material material)
        {
            if (material == null) return;

            material.DisableKeyword("MAP_MODE_POLITICAL");
            material.DisableKeyword("MAP_MODE_COUNTRY");
            material.DisableKeyword("MAP_MODE_TERRAIN");
            material.DisableKeyword("MAP_MODE_DEVELOPMENT");
            material.DisableKeyword("MAP_MODE_CULTURE");
            material.DisableKeyword("MAP_MODE_DEBUG");
            material.DisableKeyword("MAP_MODE_BORDERS");
        }
    }
}