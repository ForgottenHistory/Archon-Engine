using UnityEngine;
using Map.Rendering;
using Utils;

namespace Map.MapModes
{
    /// <summary>
    /// Political mapmode: shows political borders and province boundaries
    /// This is a temporary implementation to maintain existing functionality
    /// Will be properly implemented in Phase 2 of the refactoring
    /// </summary>
    public class PoliticalMapMode : MapMode
    {
        public override string Name => "Political";
        public override int ShaderModeID => 0;
        public override string ShaderKeyword => "MAP_MODE_POLITICAL";
        public override bool RequiresFrequentUpdates => false; // Static borders

        public override void UpdateGPUTextures(MapTextureManager textureManager)
        {
            // TODO: Implement proper political mapmode texture updates
            // For now, this maintains existing behavior
            if (textureManager != null)
            {
                DominionLogger.Log("PoliticalMapMode: Using existing texture state (legacy compatibility)");
            }
        }

        public override void ApplyShaderSettings(Material mapMaterial)
        {
            if (mapMaterial == null) return;

            // Disable all other mapmode keywords
            DisableAllMapModeKeywords(mapMaterial);

            // Enable political mapmode
            EnableShaderKeyword(mapMaterial, ShaderKeyword);

            // Set shader mode ID
            mapMaterial.SetInt("_MapMode", ShaderModeID);
        }

        public override void OnActivate()
        {
            DominionLogger.Log("PoliticalMapMode: Activated - showing political borders");
        }

        public override void OnDeactivate()
        {
            DominionLogger.Log("PoliticalMapMode: Deactivated");
        }
    }
}