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
                DominionLogger.LogError("PoliticalMapMode: Owner texture not available");
                return;
            }

            var startTime = Time.realtimeSinceStartup;

            // Update country color palette
            UpdateCountryColorPalette(dataTextures, countryQueries);

            var updateTime = (Time.realtimeSinceStartup - startTime) * 1000f;
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
        /// Update the country color palette with current country colors
        /// </summary>
        private void UpdateCountryColorPalette(MapModeDataTextures dataTextures, CountryQueries countryQueries)
        {
            var colors = new Color32[256];

            // Set default colors
            colors[0] = UnownedColor; // Index 0 = unowned

            // Get all countries and update their colors
            using var countryIds = countryQueries.GetAllCountryIds(Allocator.Temp);

            for (int i = 0; i < countryIds.Length; i++)
            {
                var countryId = countryIds[i];
                if (countryId < 256) // Ensure it fits in palette
                {
                    var color = countryQueries.GetColor(countryId);
                    colors[countryId] = color;
                }
            }

            // Apply to palette texture
            dataTextures.UpdatePalette(dataTextures.CountryColorPalette, colors);
        }
    }
}