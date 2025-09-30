using UnityEngine;
using Unity.Collections;
using Core.Queries;
using System;
using Map.Rendering;

namespace Map.MapModes
{
    /// <summary>
    /// Political map mode handler - displays province ownership by country colors
    /// Uses GPU compute shader for owner texture population (dual-layer architecture)
    /// Performance: ~2ms GPU texture update (vs 50+ seconds CPU), ultra-fast rendering
    /// </summary>
    public class PoliticalMapMode : BaseMapModeHandler
    {
        public override MapMode Mode => MapMode.Political;
        public override string Name => "Political";
        public override int ShaderModeID => 0;

        // Political visualization settings
        private static readonly Color32 UnownedColor = new Color32(128, 128, 128, 255); // Gray
        private static readonly Color32 OceanColor = new Color32(25, 25, 112, 255);     // Dark blue

        // GPU dispatcher for owner texture population (architecture compliance)
        private OwnerTextureDispatcher ownerTextureDispatcher;

        public override void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures)
        {
            DisableAllMapModeKeywords(mapMaterial);
            EnableMapModeKeyword(mapMaterial, "MAP_MODE_POLITICAL");
            SetShaderMode(mapMaterial, ShaderModeID);

            // Get or find the owner texture dispatcher
            if (ownerTextureDispatcher == null)
            {
                ownerTextureDispatcher = UnityEngine.Object.FindFirstObjectByType<OwnerTextureDispatcher>();
                if (ownerTextureDispatcher == null)
                {
                    DominionLogger.LogError("PoliticalMapMode: OwnerTextureDispatcher not found - GPU texture population will not work!");
                }
            }

            LogActivation("Political map mode - showing country ownership");
        }

        public override void OnDeactivate(Material mapMaterial)
        {
            LogDeactivation();
        }

        public override void UpdateTextures(MapModeDataTextures dataTextures, ProvinceQueries provinceQueries, CountryQueries countryQueries, ProvinceMapping provinceMapping)
        {
            if (dataTextures?.ProvinceOwnerTexture == null)
            {
                //DominionLogger.LogError("PoliticalMapMode: Owner texture not available");
                return;
            }

            var startTime = Time.realtimeSinceStartup;

            // Update country color palette
            UpdateCountryColorPalette(dataTextures, countryQueries);

            // GPU-based owner texture population (dual-layer architecture compliance)
            // Architecture: Core ProvinceQueries → GPU compute shader → Owner texture
            // Performance: ~2ms (GPU parallel) vs 50+ seconds (CPU loops)
            // DEBUG: Re-enabled to see what data compute shader receives
            UpdateOwnershipTextureGPU(provinceQueries);

            var updateTime = (Time.realtimeSinceStartup - startTime) * 1000f;
            DominionLogger.Log($"PoliticalMapMode: Updated country color palette and ownership texture in {updateTime:F2}ms");
        }

        public override string GetProvinceTooltip(ushort provinceId, ProvinceQueries provinceQueries, CountryQueries countryQueries)
        {
            if (provinceQueries.IsOcean(provinceId))
            {
                return "Ocean";
            }

            var owner = provinceQueries.GetOwner(provinceId);
            var development = provinceQueries.GetDevelopment(provinceId);

            var ownerName = "Unowned";
            if (owner != 0)
            {
                ownerName = countryQueries.GetTag(owner);
            }

            return $"Province {provinceId}\nOwner: {ownerName}\nDevelopment: {development}";
        }

        public override UpdateFrequency GetUpdateFrequency()
        {
            return UpdateFrequency.PerConquest; // Update when ownership changes
        }

        /// <summary>
        /// Update the ownership texture using GPU compute shader (dual-layer architecture)
        /// Architecture: Core ProvinceQueries → GPU buffer → GPU compute shader → Owner texture
        /// Performance: ~2ms (GPU parallel processing) vs 50+ seconds (CPU pixel loops)
        /// </summary>
        private void UpdateOwnershipTextureGPU(ProvinceQueries provinceQueries)
        {
            if (ownerTextureDispatcher == null)
            {
                DominionLogger.LogError("PoliticalMapMode: OwnerTextureDispatcher not available - cannot update owner texture");
                return;
            }

            // Delegate to GPU compute shader dispatcher
            // This processes ALL pixels in parallel on the GPU instead of CPU loops
            ownerTextureDispatcher.PopulateOwnerTexture(provinceQueries);
        }

        /// <summary>
        /// Update the country color palette with current country colors
        /// </summary>
        private void UpdateCountryColorPalette(MapModeDataTextures dataTextures, CountryQueries countryQueries)
        {
            var colors = new Color32[1024];

            // Set default colors
            colors[0] = UnownedColor; // Index 0 = unowned

            // Get all countries and update their colors
            using var countryIds = countryQueries.GetAllCountryIds(Allocator.Temp);
            int processedCountries = 0;

            DominionLogger.Log($"PoliticalMapMode: Processing {countryIds.Length} countries for color palette");

            for (int i = 0; i < countryIds.Length; i++)
            {
                var countryId = countryIds[i];
                if (countryId < 1024) // Ensure it fits in palette (expanded from 256 to 1024)
                {
                    var color = countryQueries.GetColor(countryId);
                    colors[countryId] = color;
                    processedCountries++;

                    // Debug: Log first few countries
                    if (i < 5)
                    {
                        DominionLogger.Log($"PoliticalMapMode: Country {countryId} color: R={color.r} G={color.g} B={color.b} A={color.a}");
                    }
                }
                else
                {
                    // Log countries that still don't fit
                    if (i < 5)
                    {
                        DominionLogger.LogWarning($"PoliticalMapMode: Country {countryId} exceeds palette size (1024), skipping");
                    }
                }
            }

            // Apply to palette texture
            dataTextures.UpdatePalette(dataTextures.CountryColorPalette, colors);
            DominionLogger.Log($"PoliticalMapMode: Applied {processedCountries} country colors to palette");
        }
    }
}