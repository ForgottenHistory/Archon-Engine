using UnityEngine;
using Map.Rendering;
using Core;
using Core.Queries;
using Utils;

namespace Map.MapModes
{
    /// <summary>
    /// Culture mapmode: shows province culture groups
    /// Uses culture data from simulation to color provinces by culture
    /// Follows dual-layer architecture: reads simulation state, updates GPU textures
    /// </summary>
    public class CultureMapMode : MapMode
    {
        public override string Name => "Culture";
        public override int ShaderModeID => 3;
        public override string ShaderKeyword => "MAP_MODE_CULTURE";
        public override bool RequiresFrequentUpdates => false; // Culture changes rarely

        // Color for provinces with unknown/undefined culture
        private static readonly Color32 UnknownCulture = new Color32(64, 64, 64, 255); // Dark gray

        public override void UpdateGPUTextures(MapTextureManager textureManager)
        {
            // Get current simulation state from Core layer
            var gameState = Object.FindFirstObjectByType<GameState>();
            if (gameState?.ProvinceQueries == null)
            {
                DominionLogger.LogWarning("CultureMapMode: ProvinceQueries not available");
                return;
            }

            // Update the color palette with culture-based colors
            UpdateCulturePalette(textureManager, gameState.ProvinceQueries);
        }

        public override void ApplyShaderSettings(Material mapMaterial)
        {
            if (mapMaterial == null) return;

            // Disable all other mapmode keywords
            DisableAllMapModeKeywords(mapMaterial);

            // Enable culture mapmode
            EnableShaderKeyword(mapMaterial, ShaderKeyword);

            // Set shader mode ID
            mapMaterial.SetInt("_MapMode", ShaderModeID);
        }

        public override void OnActivate()
        {
            DominionLogger.Log("CultureMapMode: Activated - showing culture groups");
        }

        public override void OnDeactivate()
        {
            DominionLogger.Log("CultureMapMode: Deactivated");
        }

        /// <summary>
        /// Update color palette based on culture types
        /// </summary>
        private void UpdateCulturePalette(MapTextureManager textureManager, ProvinceQueries provinceQueries)
        {
            var paletteColors = new Color32[256];

            // Initialize with unknown culture color
            for (int i = 0; i < 256; i++)
            {
                paletteColors[i] = UnknownCulture;
            }

            // Generate distinct colors for different culture IDs
            for (int i = 1; i < 256; i++)
            {
                paletteColors[i] = GenerateCultureColor(i);
            }

            // Apply to texture manager
            textureManager.SetPaletteColors(paletteColors);
            textureManager.ApplyPaletteChanges();

            DominionLogger.Log("CultureMapMode: Updated culture color palette");
        }

        /// <summary>
        /// Generate a distinct color for each culture ID
        /// </summary>
        private Color32 GenerateCultureColor(int cultureId)
        {
            // Use HSV color space to generate visually distinct colors
            float hue = (cultureId * 137.508f) % 360f; // Golden angle for good distribution
            float saturation = 0.6f + (cultureId % 4) * 0.1f; // Vary saturation
            float value = 0.7f + (cultureId % 3) * 0.1f; // Vary brightness

            Color color = Color.HSVToRGB(hue / 360f, saturation, value);
            return new Color32(
                (byte)(color.r * 255),
                (byte)(color.g * 255),
                (byte)(color.b * 255),
                255
            );
        }
    }
}