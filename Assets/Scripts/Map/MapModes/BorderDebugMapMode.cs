using UnityEngine;
using Map.Rendering;
using Utils;

namespace Map.MapModes
{
    /// <summary>
    /// Border debug mapmode: shows only the border texture for debugging
    /// Useful for verifying border generation and border compute shader functionality
    /// </summary>
    public class BorderDebugMapMode : MapMode
    {
        public override string Name => "Border Debug";
        public override int ShaderModeID => 10;
        public override string ShaderKeyword => "MAP_MODE_BORDERS";
        public override bool RequiresFrequentUpdates => false; // Borders are static unless provinces change

        public override void UpdateGPUTextures(MapTextureManager textureManager)
        {
            // Border debug mode uses existing border texture
            // No additional texture updates needed
            if (textureManager != null)
            {
                DominionLogger.Log("BorderDebugMapMode: Using existing border texture for debug visualization");
            }
        }

        public override void ApplyShaderSettings(Material mapMaterial)
        {
            if (mapMaterial == null) return;

            // Disable all other mapmode keywords
            DisableAllMapModeKeywords(mapMaterial);

            // Enable border debug mapmode
            EnableShaderKeyword(mapMaterial, ShaderKeyword);

            // Set shader mode ID
            mapMaterial.SetInt("_MapMode", ShaderModeID);
        }

        public override void OnActivate()
        {
            DominionLogger.Log("BorderDebugMapMode: Activated - showing border texture only");
        }

        public override void OnDeactivate()
        {
            DominionLogger.Log("BorderDebugMapMode: Deactivated");
        }
    }
}