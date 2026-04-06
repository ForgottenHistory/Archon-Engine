using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Utils;

namespace Core.Loaders
{
    /// <summary>
    /// Loads map configuration from default.json5.
    /// Provides water province lists (sea_starts, lake_starts, ocean_starts)
    /// used for terrain assignment when no terrain.png is available.
    /// </summary>
    public static class MapConfigLoader
    {
        public struct MapConfig
        {
            public int Width;
            public int Height;
            public int MaxProvinces;
            public HashSet<ushort> SeaProvinces;
            public HashSet<ushort> LakeProvinces;
            public HashSet<ushort> OceanProvinces;

            /// <summary>
            /// Returns true if the province is any type of water (sea, lake, or ocean).
            /// </summary>
            public bool IsWater(ushort provinceId)
            {
                return SeaProvinces.Contains(provinceId)
                    || LakeProvinces.Contains(provinceId)
                    || OceanProvinces.Contains(provinceId);
            }
        }

        /// <summary>
        /// Load map configuration from default.json5.
        /// Returns null if file not found (optional file).
        /// </summary>
        public static MapConfig? Load(string dataDirectory)
        {
            string path = Path.Combine(dataDirectory, "map", "default.json5");

            if (!File.Exists(path))
            {
                ArchonLogger.Log("MapConfigLoader: default.json5 not found, skipping", "core_data_loading");
                return null;
            }

            try
            {
                JObject json = Json5Loader.LoadJson5File(path);

                var config = new MapConfig
                {
                    Width = json["width"]?.Value<int>() ?? 0,
                    Height = json["height"]?.Value<int>() ?? 0,
                    MaxProvinces = json["max_provinces"]?.Value<int>() ?? 0,
                    SeaProvinces = LoadProvinceSet(json, "sea_starts"),
                    LakeProvinces = LoadProvinceSet(json, "lake_starts"),
                    OceanProvinces = LoadProvinceSet(json, "ocean_starts")
                };

                int totalWater = config.SeaProvinces.Count + config.LakeProvinces.Count + config.OceanProvinces.Count;
                ArchonLogger.Log($"MapConfigLoader: Loaded default.json5 ({config.SeaProvinces.Count} sea, {config.LakeProvinces.Count} lake, {config.OceanProvinces.Count} ocean = {totalWater} water provinces)", "core_data_loading");

                return config;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"MapConfigLoader: Failed to load default.json5: {e.Message}", "core_data_loading");
                return null;
            }
        }

        private static HashSet<ushort> LoadProvinceSet(JObject json, string key)
        {
            var set = new HashSet<ushort>();
            var array = json[key] as JArray;
            if (array == null) return set;

            foreach (var item in array)
            {
                int id = item.Value<int>();
                if (id > 0 && id <= ushort.MaxValue)
                    set.Add((ushort)id);
            }

            return set;
        }
    }
}
