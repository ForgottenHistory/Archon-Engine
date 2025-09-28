using UnityEngine;
using Map.Rendering;
using Core;
using Core.Data;
using Core.Queries;
using Utils;

namespace Map.MapModes
{
    /// <summary>
    /// Country mapmode: displays provinces colored by their owning country
    /// Uses country color data from Core.Data.CountryData system
    /// Follows dual-layer architecture: reads simulation state, updates GPU textures
    /// </summary>
    public class CountryMapMode : MapMode
    {
        public override string Name => "Country";
        public override int ShaderModeID => 4; // New ID for country mapmode
        public override string ShaderKeyword => "MAP_MODE_COUNTRY";
        public override bool RequiresFrequentUpdates => true; // Province ownership can change

        // Color for unowned provinces
        private static readonly Color32 UnownedColor = new Color32(128, 128, 128, 255); // Gray

        public override void UpdateGPUTextures(MapTextureManager textureManager)
        {
            // Get current simulation state from Core layer
            var gameState = Object.FindFirstObjectByType<GameState>();
            if (gameState?.CountryQueries == null)
            {
                DominionLogger.LogWarning("CountryMapMode: CountryQueries not available");
                return;
            }

            // Update the color palette with country colors using query system
            UpdateColorPalette(textureManager, gameState.CountryQueries);
        }

        public override void ApplyShaderSettings(Material mapMaterial)
        {
            if (mapMaterial == null) return;

            // Disable all other mapmode keywords
            DisableAllMapModeKeywords(mapMaterial);

            // Enable country mapmode
            EnableShaderKeyword(mapMaterial, ShaderKeyword);

            // Set shader mode ID
            mapMaterial.SetInt("_MapMode", ShaderModeID);
        }

        public override void OnActivate()
        {
            DominionLogger.Log("CountryMapMode: Activated - showing country colors");
        }

        public override void OnDeactivate()
        {
            DominionLogger.Log("CountryMapMode: Deactivated");
        }


        /// <summary>
        /// Update the color palette with current country colors using query system
        /// </summary>
        private void UpdateColorPalette(MapTextureManager textureManager, Core.Queries.CountryQueries countryQueries)
        {
            var paletteColors = new Color32[256];

            // Initialize with default colors
            for (int i = 0; i < 256; i++)
            {
                paletteColors[i] = UnownedColor;
            }

            // Get all country IDs using the query system
            using var countryIds = countryQueries.GetAllCountryIds(Unity.Collections.Allocator.TempJob);

            // Update palette with actual country colors
            for (int i = 0; i < countryIds.Length; i++)
            {
                var countryId = countryIds[i];
                if (countryId < 256) // Ensure it fits in our palette
                {
                    var color = countryQueries.GetColor(countryId);
                    paletteColors[countryId] = color;
                }
            }

            // Apply to texture manager
            textureManager.SetPaletteColors(paletteColors);
            textureManager.ApplyPaletteChanges();

            DominionLogger.Log($"CountryMapMode: Updated color palette with {countryIds.Length} country colors");
        }

        /// <summary>
        /// Generate a consistent color for countries not in the registry
        /// </summary>
        private Color32 GenerateDefaultColorFromTag(string tag)
        {
            // Use simple hash to generate consistent colors
            uint hash = 2166136261u;
            foreach (char c in tag)
            {
                hash ^= (byte)c;
                hash *= 16777619u;
            }

            byte r = (byte)((hash >> 16) & 0xFF);
            byte g = (byte)((hash >> 8) & 0xFF);
            byte b = (byte)(hash & 0xFF);

            // Ensure minimum brightness
            if (r + g + b < 200)
            {
                r = (byte)Mathf.Max(r, 100);
                g = (byte)Mathf.Max(g, 100);
                b = (byte)Mathf.Max(b, 100);
            }

            return new Color32(r, g, b, 255);
        }
    }
}