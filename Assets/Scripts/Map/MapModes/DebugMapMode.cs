using UnityEngine;
using Map.Rendering;
using Utils;

namespace Map.MapModes
{
    /// <summary>
    /// Debug mapmode: shows province IDs as colors for debugging
    /// Useful for verifying province mapping and texture coordinate systems
    /// </summary>
    public class DebugMapMode : MapMode
    {
        public override string Name => "Debug";
        public override int ShaderModeID => 99;
        public override string ShaderKeyword => "MAP_MODE_DEBUG";
        public override bool RequiresFrequentUpdates => false; // Debug data is static

        public override void UpdateGPUTextures(MapTextureManager textureManager)
        {
            // Debug mode uses existing province ID texture
            // No additional texture updates needed
            if (textureManager != null)
            {
                DominionLogger.Log("DebugMapMode: Using existing province ID texture for debug visualization");
            }
        }

        public override void ApplyShaderSettings(Material mapMaterial)
        {
            if (mapMaterial == null) return;

            // Disable all other mapmode keywords
            DisableAllMapModeKeywords(mapMaterial);

            // Enable debug mapmode
            EnableShaderKeyword(mapMaterial, ShaderKeyword);

            // Set shader mode ID
            mapMaterial.SetInt("_MapMode", ShaderModeID);
        }

        public override void OnActivate()
        {
            DominionLogger.Log("DebugMapMode: Activated - showing province IDs as colors");
        }

        public override void OnDeactivate()
        {
            DominionLogger.Log("DebugMapMode: Deactivated");
        }
    }
}