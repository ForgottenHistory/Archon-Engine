using UnityEngine;
using System.Collections.Generic;

namespace MapModes
{
    public class CountryMapMode : BaseMapMode
    {
        private CountryDataLoader countryDataLoader;
        private ProvinceDefinitionLoader provinceDefinitionLoader;
        private MapDefinitionLoader mapDefinitionLoader;

        public override string ModeName => "Country";

        public override void Initialize(MapController controller)
        {
            base.Initialize(controller);

            // Find required loaders
            countryDataLoader = Object.FindObjectOfType<CountryDataLoader>();
            provinceDefinitionLoader = Object.FindObjectOfType<ProvinceDefinitionLoader>();
            mapDefinitionLoader = Object.FindObjectOfType<MapDefinitionLoader>();

            if (countryDataLoader == null)
            {
                Debug.LogError("CountryMapMode requires CountryDataLoader component");
                return;
            }

            if (provinceDefinitionLoader == null)
            {
                Debug.LogError("CountryMapMode requires ProvinceDefinitionLoader component");
                return;
            }

            // Load country data if not already loaded
            if (!countryDataLoader.IsLoaded)
            {
                countryDataLoader.LoadCountryData();
            }
        }

        public override void UpdateProvinceColor(int provinceId)
        {
            Color newColor = GetProvinceColor(provinceId);
            provinceColors[provinceId] = newColor;
        }

        public override Color GetProvinceColor(int provinceId)
        {
            if (countryDataLoader == null || !countryDataLoader.IsLoaded)
                return GetFallbackColor(provinceId);

            // Get province ownership
            var ownership = countryDataLoader.GetProvinceOwnership(provinceId);
            if (ownership == null || string.IsNullOrEmpty(ownership.owner))
            {
                return GetUnownedProvinceColor(provinceId);
            }

            // Get country color
            var countryColor = countryDataLoader.GetCountryColor(ownership.owner);

            // Fallback to a generated color if country has no defined color
            if (countryColor == Color.gray && !string.IsNullOrEmpty(ownership.owner))
            {
                countryColor = GenerateCountryColor(ownership.owner);
            }

            return countryColor;
        }

        private Color GetUnownedProvinceColor(int provinceId)
        {
            // Check if it's a sea province
            if (mapDefinitionLoader != null && mapDefinitionLoader.IsLoaded)
            {
                var mapDef = mapDefinitionLoader.GetMapDefinition();
                if (mapDef != null && mapDef.IsSeaProvince(provinceId))
                {
                    return new Color(0.2f, 0.4f, 0.8f, 1f); // Sea blue
                }
            }

            // Unowned land province
            return new Color(0.5f, 0.5f, 0.5f, 1f); // Gray
        }

        private Color GetFallbackColor(int provinceId)
        {
            // If country data isn't available, use province definition color
            if (provinceDefinitionLoader != null && provinceDefinitionLoader.IsLoaded)
            {
                var provinceDef = provinceDefinitionLoader.GetProvinceByID(provinceId);
                if (provinceDef != null)
                {
                    return provinceDef.color;
                }
            }

            // Ultimate fallback
            return Color.white;
        }

        private Color GenerateCountryColor(string countryTag)
        {
            // Generate a consistent color based on country tag hash
            int hash = countryTag.GetHashCode();

            // Use hash to generate RGB values
            float r = ((hash & 0xFF0000) >> 16) / 255f;
            float g = ((hash & 0x00FF00) >> 8) / 255f;
            float b = (hash & 0x0000FF) / 255f;

            // Ensure minimum brightness and saturation
            r = Mathf.Max(r, 0.3f);
            g = Mathf.Max(g, 0.3f);
            b = Mathf.Max(b, 0.3f);

            // Normalize to prevent too bright colors
            float max = Mathf.Max(r, Mathf.Max(g, b));
            if (max > 0.9f)
            {
                float scale = 0.9f / max;
                r *= scale;
                g *= scale;
                b *= scale;
            }

            return new Color(r, g, b, 1f);
        }

        public override void OnEnterMode()
        {
            base.OnEnterMode();

            if (countryDataLoader != null && countryDataLoader.IsLoaded)
            {
                Debug.Log($"Country Map Mode: Showing {countryDataLoader.CountriesByTag?.Count ?? 0} countries " +
                         $"across {countryDataLoader.ProvinceOwners?.Count ?? 0} provinces");
            }
        }

        public override void OnExitMode()
        {
            base.OnExitMode();
        }

        public string GetProvinceTooltip(int provinceId)
        {
            if (countryDataLoader == null || !countryDataLoader.IsLoaded)
                return $"Province {provinceId}";

            var ownership = countryDataLoader.GetProvinceOwnership(provinceId);
            if (ownership == null)
                return $"Province {provinceId}: Unowned";

            var country = countryDataLoader.GetCountryByTag(ownership.owner);
            string countryName = country?.name ?? ownership.owner;

            string tooltip = $"Province {provinceId}\n";
            tooltip += $"Owner: {countryName} ({ownership.owner})";

            if (!string.IsNullOrEmpty(ownership.culture))
                tooltip += $"\nCulture: {ownership.culture}";

            if (!string.IsNullOrEmpty(ownership.religion))
                tooltip += $"\nReligion: {ownership.religion}";

            return tooltip;
        }

        // Debug method to list countries by province count
        [ContextMenu("Log Country Statistics")]
        public void LogCountryStatistics()
        {
            if (countryDataLoader == null || !countryDataLoader.IsLoaded)
            {
                Debug.Log("Country data not loaded");
                return;
            }

            var countryProvinceCount = new Dictionary<string, int>();

            foreach (var ownership in countryDataLoader.ProvinceOwners.Values)
            {
                if (!string.IsNullOrEmpty(ownership.owner))
                {
                    countryProvinceCount[ownership.owner] = countryProvinceCount.GetValueOrDefault(ownership.owner, 0) + 1;
                }
            }

            Debug.Log("Countries by province count:");
            var sortedCountries = new List<KeyValuePair<string, int>>(countryProvinceCount);
            sortedCountries.Sort((a, b) => b.Value.CompareTo(a.Value));

            for (int i = 0; i < Mathf.Min(10, sortedCountries.Count); i++)
            {
                var kvp = sortedCountries[i];
                var country = countryDataLoader.GetCountryByTag(kvp.Key);
                string name = country?.name ?? kvp.Key;
                Debug.Log($"{i + 1}. {name} ({kvp.Key}): {kvp.Value} provinces");
            }
        }
    }
}