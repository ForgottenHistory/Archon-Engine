using UnityEngine;
using Map.Rendering;
using Core;
using Core.Queries;
using Utils;

namespace Map.MapModes
{
    /// <summary>
    /// Development mapmode: shows province development levels
    /// Uses development data from simulation to color provinces by development
    /// Follows dual-layer architecture: reads simulation state, updates GPU textures
    /// </summary>
    public class DevelopmentMapMode : MapMode
    {
        public override string Name => "Development";
        public override int ShaderModeID => 2;
        public override string ShaderKeyword => "MAP_MODE_DEVELOPMENT";
        public override bool RequiresFrequentUpdates => true; // Development can change

        // Color gradient for development levels
        private static readonly Color32 LowDevelopment = new Color32(139, 69, 19, 255);   // Brown
        private static readonly Color32 MediumDevelopment = new Color32(255, 165, 0, 255); // Orange
        private static readonly Color32 HighDevelopment = new Color32(255, 215, 0, 255);   // Gold

        public override void UpdateGPUTextures(MapTextureManager textureManager)
        {
            // Get current simulation state from Core layer
            var gameState = Object.FindFirstObjectByType<GameState>();
            if (gameState?.ProvinceQueries == null)
            {
                DominionLogger.LogWarning("DevelopmentMapMode: ProvinceQueries not available");
                return;
            }

            // Update the color palette with development-based colors
            UpdateDevelopmentPalette(textureManager, gameState.ProvinceQueries);
        }

        public override void ApplyShaderSettings(Material mapMaterial)
        {
            if (mapMaterial == null) return;

            // Disable all other mapmode keywords
            DisableAllMapModeKeywords(mapMaterial);

            // Enable development mapmode
            EnableShaderKeyword(mapMaterial, ShaderKeyword);

            // Set shader mode ID
            mapMaterial.SetInt("_MapMode", ShaderModeID);
        }

        public override void OnActivate()
        {
            DominionLogger.Log("DevelopmentMapMode: Activated - showing province development");
        }

        public override void OnDeactivate()
        {
            DominionLogger.Log("DevelopmentMapMode: Deactivated");
        }

        /// <summary>
        /// Update color palette based on province development levels
        /// </summary>
        private void UpdateDevelopmentPalette(MapTextureManager textureManager, ProvinceQueries provinceQueries)
        {
            var paletteColors = new Color32[256];

            // Create development-based color palette
            for (int i = 0; i < 256; i++)
            {
                // Map development levels (0-255) to color gradient
                float developmentRatio = i / 255.0f;
                paletteColors[i] = LerpDevelopmentColor(developmentRatio);
            }

            // Apply to texture manager
            textureManager.SetPaletteColors(paletteColors);
            textureManager.ApplyPaletteChanges();

            DominionLogger.Log("DevelopmentMapMode: Updated development color palette");
        }

        /// <summary>
        /// Interpolate color based on development level
        /// </summary>
        private Color32 LerpDevelopmentColor(float developmentRatio)
        {
            if (developmentRatio < 0.5f)
            {
                // Low to medium development
                return Color32.Lerp(LowDevelopment, MediumDevelopment, developmentRatio * 2.0f);
            }
            else
            {
                // Medium to high development
                return Color32.Lerp(MediumDevelopment, HighDevelopment, (developmentRatio - 0.5f) * 2.0f);
            }
        }
    }
}