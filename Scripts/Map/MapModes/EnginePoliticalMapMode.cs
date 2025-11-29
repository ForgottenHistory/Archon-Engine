using UnityEngine;
using Unity.Collections;
using Core.Queries;
using Map.Rendering;

namespace Map.MapModes
{
    /// <summary>
    /// ENGINE LAYER: Political map mode - displays province ownership by country colors
    /// Uses only ENGINE data (CountryQueries) - no GAME layer dependencies
    /// </summary>
    public class EnginePoliticalMapMode : BaseMapModeHandler
    {
        public override MapMode Mode => MapMode.Political;
        public override string Name => "Political";
        public override int ShaderModeID => 0;

        private static readonly Color32 UnownedColor = new Color32(128, 128, 128, 255);
        private static readonly Color32 OceanColor = new Color32(25, 25, 112, 255);

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

        public override void UpdateTextures(MapModeDataTextures dataTextures, ProvinceQueries provinceQueries,
            CountryQueries countryQueries, ProvinceMapping provinceMapping, object gameProvinceSystem = null)
        {
            if (dataTextures?.CountryColorPalette == null)
            {
                ArchonLogger.LogError("EnginePoliticalMapMode: CountryColorPalette not available");
                return;
            }

            var startTime = Time.realtimeSinceStartup;

            using var allCountries = countryQueries.GetAllCountryIds(Allocator.Temp);

            // Palette is fixed at 1024 pixels (matches shader's GetColorUV divisor)
            int paletteSize = 1024;
            var palettePixels = new Color32[paletteSize];

            // Initialize palette (ID 0 = unowned = ocean color)
            for (int i = 0; i < paletteSize; i++)
            {
                palettePixels[i] = OceanColor;
            }

            // Populate palette with country colors from ENGINE CountryQueries
            int countriesAdded = 0;
            for (int i = 0; i < allCountries.Length; i++)
            {
                ushort countryId = allCountries[i];
                if (countryId == 0) continue;
                if (countryId >= paletteSize)
                {
                    ArchonLogger.LogWarning($"EnginePoliticalMapMode: Country ID {countryId} exceeds palette size {paletteSize}, skipping");
                    continue;
                }

                var color = countryQueries.GetColor(countryId);
                palettePixels[countryId] = color;
                countriesAdded++;

                if (countriesAdded <= 5)
                {
                    var tag = countryQueries.GetTag(countryId);
                    ArchonLogger.Log($"EnginePoliticalMapMode: Country ID={countryId} ({tag}) -> R={color.r} G={color.g} B={color.b}", "map_initialization");
                }
            }

            dataTextures.CountryColorPalette.SetPixels32(palettePixels);
            dataTextures.CountryColorPalette.Apply(true);

            var elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
            ArchonLogger.Log($"EnginePoliticalMapMode: Updated palette with {countriesAdded} countries in {elapsed:F2}ms", "map_initialization");
        }

        public override string GetProvinceTooltip(ushort provinceId, ProvinceQueries provinceQueries, CountryQueries countryQueries)
        {
            if (provinceQueries.IsOcean(provinceId))
                return "Ocean";

            var owner = provinceQueries.GetOwner(provinceId);
            var ownerName = owner != 0 ? countryQueries.GetTag(owner) : "Unowned";

            return $"Province {provinceId}\nOwner: {ownerName}";
        }

        public override UpdateFrequency GetUpdateFrequency()
        {
            return UpdateFrequency.PerConquest;
        }
    }
}
