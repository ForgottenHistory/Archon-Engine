using UnityEngine;
using Unity.Collections;
using Core.Queries;
using System;
using Map.Rendering;

namespace Map.MapModes
{
    /// <summary>
    /// Political map mode handler - displays province ownership by country colors
    /// EXACTLY COPIES Development mode approach - direct texture pixel writing
    /// Performance: Direct pixel write to texture, shader samples texture directly
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
            // EXACTLY LIKE DEVELOPMENT MODE
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
            if (dataTextures?.CountryColorPalette == null)
            {
                DominionLogger.LogError("PoliticalMapMode: CountryColorPalette not available");
                return;
            }

            var startTime = Time.realtimeSinceStartup;

            // Get Core data
            using var allProvinces = provinceQueries.GetAllProvinceIds(Allocator.Temp);

            if (allProvinces.Length == 0)
            {
                DominionLogger.LogWarning("PoliticalMapMode: No provinces available");
                return;
            }

            // Update the GPU palette with country colors
            UpdatePoliticalTexture(dataTextures, allProvinces, provinceQueries, countryQueries, provinceMapping);

            var elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            DominionLogger.LogMapInit($"PoliticalMapMode: Updated political texture in {elapsed:F2}ms");
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
        /// Update the political texture with Core simulation data
        /// EXACTLY COPIES Development mode pattern: Core data → GPU texture
        /// </summary>
        private void UpdatePoliticalTexture(MapModeDataTextures dataTextures, NativeArray<ushort> provinces,
                                            ProvinceQueries provinceQueries, CountryQueries countryQueries, ProvinceMapping provinceMapping)
        {
            // Political mode uses GPU palette system: ProvinceOwnerTexture + CountryColorPalette
            // The shader reads owner ID from ProvinceOwnerTexture, then looks up color in CountryColorPalette
            var palette = dataTextures.CountryColorPalette;
            if (palette == null)
            {
                DominionLogger.LogError("PoliticalMapMode: CountryColorPalette is null");
                return;
            }

            // Get all country IDs first to determine required palette size
            using var allCountries = countryQueries.GetAllCountryIds(Allocator.Temp);

            // Palette is fixed at 1024 pixels (matches shader's GetColorUV divisor)
            // We populate the entries we need, rest stay at default
            int paletteSize = 1024;
            var palettePixels = new Color32[paletteSize];

            // Initialize palette (ID 0 = unowned = ocean color)
            for (int i = 0; i < paletteSize; i++)
            {
                palettePixels[i] = OceanColor;
            }

            // Populate palette with country colors
            int countriesAdded = 0;
            for (int i = 0; i < allCountries.Length; i++)
            {
                ushort countryId = allCountries[i];
                if (countryId == 0) continue;
                if (countryId >= paletteSize)
                {
                    DominionLogger.LogWarning($"PoliticalMapMode: Country ID {countryId} exceeds palette size {paletteSize}, skipping");
                    continue;
                }

                var color = countryQueries.GetColor(countryId);
                palettePixels[countryId] = color;
                countriesAdded++;

                // Debug first few AND Castile (ID 151)
                if (countriesAdded <= 10 || countryId == 151)
                {
                    var tag = countryQueries.GetTag(countryId);
                    DominionLogger.LogMapInit($"PoliticalMapMode: Country ID={countryId} ({tag}) → R={color.r} G={color.g} B={color.b}");
                }
            }

            // Apply palette (don't resize - keep 1024 to match shader divisor)
            palette.SetPixels32(palettePixels);
            palette.Apply(true);  // Force GPU upload with mipmaps regeneration (even though we don't use mipmaps)

            DominionLogger.LogMapInit($"PoliticalMapMode: Updated CountryColorPalette (1024 slots) with {countriesAdded} country colors (GPU will use ProvinceOwnerTexture + palette for rendering)");
        }

    }
}