using UnityEngine;
using Unity.Collections;
using Core.Queries;
using System;
using Map.Rendering;

namespace Map.MapModes
{
    /// <summary>
    /// Political map mode handler - displays province ownership by country colors
    /// Uses existing texture system with proper shader-based rendering
    /// Performance: Event-driven updates on conquest, ultra-fast rendering
    /// </summary>
    public class PoliticalMapMode : BaseMapModeHandler
    {
        public override MapMode Mode => MapMode.Political;
        public override string Name => "Political";
        public override int ShaderModeID => 0;

        // Political visualization settings
        private static readonly Color32 UnownedColor = new Color32(128, 128, 128, 255); // Gray
        private static readonly Color32 OceanColor = new Color32(25, 25, 112, 255);     // Dark blue

        public override void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures)
        {
            DisableAllMapModeKeywords(mapMaterial);
            EnableMapModeKeyword(mapMaterial, "MAP_MODE_POLITICAL");
            SetShaderMode(mapMaterial, ShaderModeID);

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

            // CRITICAL: Update the GPU texture with ownership data (dual-layer architecture)
            UpdateOwnershipTexture(dataTextures, provinceQueries, provinceMapping);

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
        /// Update the ownership texture with Core simulation data
        /// Following texture-based architecture: Core data → GPU texture (same pattern as Development mode)
        /// </summary>
        private void UpdateOwnershipTexture(MapModeDataTextures dataTextures, ProvinceQueries provinceQueries, ProvinceMapping provinceMapping)
        {
            var texture = dataTextures.ProvinceOwnerTexture;
            if (texture == null)
            {
                DominionLogger.LogError("PoliticalMapMode: ProvinceOwnerTexture is null");
                return;
            }

            // Get all provinces from Core simulation
            using var allProvinces = provinceQueries.GetAllProvinceIds(Allocator.Temp);
            if (allProvinces.Length == 0)
            {
                DominionLogger.LogWarning("PoliticalMapMode: No provinces available");
                return;
            }

            // Get texture dimensions
            int width = texture.width;
            int height = texture.height;
            var pixels = new Color32[width * height];

            // Initialize with ocean/unowned (owner ID 0)
            var oceanColor = new Color32(0, 0, 0, 255); // Owner ID 0 = unowned/ocean
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = oceanColor;
            }

            int processedProvinces = 0;
            int ownedProvinces = 0;
            int unownedProvinces = 0;

            // Update each province's pixels with owner data
            for (int i = 0; i < allProvinces.Length; i++)
            {
                var provinceId = allProvinces[i];

                // Skip ocean and invalid provinces
                if (provinceQueries.IsOcean(provinceId) || !provinceQueries.Exists(provinceId))
                    continue;

                var ownerId = provinceQueries.GetOwner(provinceId);

                // Debug logging for first few provinces
                if (i < 10)
                {
                    DominionLogger.Log($"PoliticalMapMode: Province {provinceId} has owner {ownerId}");
                }

                // Count ownership stats
                if (ownerId == 0)
                    unownedProvinces++;
                else
                    ownedProvinces++;

                // Encode owner ID as color (R channel = low byte, G channel = high byte)
                var ownerColor = new Color32((byte)(ownerId & 0xFF), (byte)((ownerId >> 8) & 0xFF), 0, 255);

                // Get all pixels for this province (following existing architecture)
                var provincePixels = provinceMapping.GetProvincePixels(provinceId);
                if (provincePixels != null)
                {
                    foreach (var pixel in provincePixels)
                    {
                        if (pixel.x >= 0 && pixel.x < width && pixel.y >= 0 && pixel.y < height)
                        {
                            int index = pixel.y * width + pixel.x;
                            if (index >= 0 && index < pixels.Length)
                            {
                                pixels[index] = ownerColor;
                            }
                        }
                    }
                    processedProvinces++;
                }
            }

            // Apply texture changes (following texture-based architecture)
            texture.SetPixels32(pixels);
            texture.Apply(false);

            DominionLogger.Log($"PoliticalMapMode: Populated ownership texture with {processedProvinces} provinces " +
                              $"[Owned: {ownedProvinces}, Unowned: {unownedProvinces}]");
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