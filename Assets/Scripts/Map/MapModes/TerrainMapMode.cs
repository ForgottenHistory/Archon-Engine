using UnityEngine;
using Map.Rendering;
using Utils;

namespace Map.MapModes
{
    /// <summary>
    /// Terrain mapmode: shows province terrain types
    /// Displays raw province colors from the province color texture
    /// This is a temporary implementation to maintain existing functionality
    /// Will be properly implemented with terrain data in Phase 2
    /// </summary>
    public class TerrainMapMode : MapMode
    {
        public override string Name => "Terrain";
        public override int ShaderModeID => 1;
        public override string ShaderKeyword => "MAP_MODE_TERRAIN";
        public override bool RequiresFrequentUpdates => false; // Static terrain data

        public override void UpdateGPUTextures(MapTextureManager textureManager)
        {
            // TODO: Implement proper terrain mapmode texture updates
            // For now, this maintains existing behavior showing raw province colors
            if (textureManager != null)
            {
                DominionLogger.Log("TerrainMapMode: Using existing province color texture (legacy compatibility)");
            }
        }

        public override void ApplyShaderSettings(Material mapMaterial)
        {
            if (mapMaterial == null) return;

            // Disable all other mapmode keywords
            DisableAllMapModeKeywords(mapMaterial);

            // Enable terrain mapmode
            EnableShaderKeyword(mapMaterial, ShaderKeyword);

            // Set shader mode ID
            mapMaterial.SetInt("_MapMode", ShaderModeID);

            // Apply terrain-specific settings
            mapMaterial.SetFloat("_BorderStrength", 0.8f);
            mapMaterial.SetColor("_BorderColor", new Color(0.0f, 0.0f, 0.0f, 1.0f));
        }

        public override void OnActivate()
        {
            DominionLogger.Log("TerrainMapMode: Activated - showing terrain types");
        }

        public override void OnDeactivate()
        {
            DominionLogger.Log("TerrainMapMode: Deactivated");
        }
    }
}