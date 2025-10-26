using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Registries;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Utils;

namespace Core.Loaders
{
    /// <summary>
    /// Loads water province definitions and terrain data from JSON5 format
    /// Integrates with the terrain system to properly distinguish water vs land provinces
    /// Following dual-layer architecture - loads into simulation layer for GPU texture updates
    /// </summary>
    public static class WaterProvinceLoader
    {
        /// <summary>
        /// Water province data loaded from JSON5 files
        /// Hot data only - accessed frequently during province initialization
        /// </summary>
        public struct WaterProvinceData
        {
            public HashSet<int> SeaProvinces;
            public HashSet<int> LakeProvinces;
            public HashSet<int> OceanProvinces;
            public Dictionary<string, Color32> TerrainColors;
            public int MapWidth;
            public int MapHeight;
            public int MaxProvinces;
        }

        /// <summary>
        /// Terrain category data from terrain.json5
        /// </summary>
        public struct TerrainCategory
        {
            public string Name;
            public Color32 Color;
            public bool IsWater;
            public bool InlandSea;
            public float MovementCost;
            public HashSet<int> TerrainOverride; // Province IDs that use this terrain
        }

        private static WaterProvinceData _cachedWaterData;
        private static Dictionary<string, TerrainCategory> _cachedTerrainCategories;
        private static bool _dataLoaded = false;

        /// <summary>
        /// Load water province and terrain definitions from JSON5 files
        /// Follows the dual-layer architecture - loads data for simulation layer
        /// </summary>
        public static WaterProvinceData LoadWaterProvinceData(string dataPath)
        {
            if (_dataLoaded)
            {
                return _cachedWaterData;
            }

            try
            {
                // Load both water provinces and terrain data
                var waterData = LoadWaterProvinceDefinitions(dataPath);
                var terrainData = LoadTerrainDefinitions(dataPath);

                // Merge terrain categories into water data
                MergeTerrainWithWaterData(ref waterData, terrainData);

                _cachedWaterData = waterData;
                _cachedTerrainCategories = terrainData;
                _dataLoaded = true;

                ArchonLogger.Log($"WaterProvinceLoader: Loaded water provinces - {waterData.SeaProvinces.Count} sea, {waterData.LakeProvinces.Count} lakes, {waterData.OceanProvinces.Count} ocean provinces", "core_data_loading");
                ArchonLogger.Log($"WaterProvinceLoader: Loaded {terrainData.Count} terrain categories from terrain.json5", "core_data_loading");

                return _cachedWaterData;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"WaterProvinceLoader: Failed to load water province data: {e.Message}", "core_data_loading");
                return CreateDefaultWaterData();
            }
        }

        /// <summary>
        /// Load water province definitions from default.json5 (original Paradox format)
        /// </summary>
        private static WaterProvinceData LoadWaterProvinceDefinitions(string dataPath)
        {
            // Use default.json5 (converted from default.map with original Paradox organization)
            string defaultMapPath = Path.Combine(dataPath, "map", "default.json5");
            if (File.Exists(defaultMapPath))
            {
                return LoadDefaultMapJson5(defaultMapPath);
            }

            ArchonLogger.LogWarning("WaterProvinceLoader: default.json5 not found, using defaults", "core_data_loading");
            return CreateDefaultWaterData();
        }


        /// <summary>
        /// Load from default.json5 (converted from default.map)
        /// Extract sea_starts and other water definitions using original Paradox organization
        /// </summary>
        private static WaterProvinceData LoadDefaultMapJson5(string filePath)
        {
            var json = Json5Loader.LoadJson5File(filePath);

            var data = new WaterProvinceData
            {
                SeaProvinces = new HashSet<int>(),
                LakeProvinces = new HashSet<int>(),
                OceanProvinces = new HashSet<int>(),
                TerrainColors = new Dictionary<string, Color32>()
            };

            // Load map dimensions (at root level in original Paradox format)
            data.MapWidth = Json5Loader.GetInt(json, "width", 5632);
            data.MapHeight = Json5Loader.GetInt(json, "height", 2048);
            data.MaxProvinces = Json5Loader.GetInt(json, "max_provinces", 4942);

            // Extract water provinces using original Paradox naming
            var seaStarts = Json5Loader.GetIntArray(json, "sea_starts");
            data.SeaProvinces = new HashSet<int>(seaStarts);

            var lakeStarts = Json5Loader.GetIntArray(json, "lake_starts");
            data.LakeProvinces = new HashSet<int>(lakeStarts);

            var oceanStarts = Json5Loader.GetIntArray(json, "ocean_starts");
            data.OceanProvinces = new HashSet<int>(oceanStarts);

            // Load terrain colors if present
            var terrainColors = Json5Loader.GetObject(json, "terrain_colors");
            if (terrainColors != null)
            {
                foreach (var property in terrainColors.Properties())
                {
                    var colorHex = property.Value.Value<string>();
                    if (ColorUtility.TryParseHtmlString(colorHex, out Color color))
                    {
                        data.TerrainColors[property.Name] = color;
                    }
                }
            }
            else
            {
                // Default terrain colors
                data.TerrainColors["sea"] = new Color32(31, 95, 153, 255);      // #1f5f99
                data.TerrainColors["lake"] = new Color32(74, 139, 194, 255);    // #4a8bc2
                data.TerrainColors["ocean"] = new Color32(13, 47, 79, 255);     // #0d2f4f
                data.TerrainColors["unowned_land"] = new Color32(196, 184, 150, 255); // #c4b896
            }

            return data;
        }

        /// <summary>
        /// Load terrain categories from terrain.json5
        /// </summary>
        private static Dictionary<string, TerrainCategory> LoadTerrainDefinitions(string dataPath)
        {
            string terrainPath = Path.Combine(dataPath, "map", "terrain.json5");
            var terrainCategories = new Dictionary<string, TerrainCategory>();

            if (!File.Exists(terrainPath))
            {
                ArchonLogger.LogWarning($"TerrainLoader: terrain.json5 not found at {terrainPath}", "core_data_loading");
                return terrainCategories;
            }

            var json = Json5Loader.LoadJson5File(terrainPath);
            var categories = Json5Loader.GetObject(json, "categories");

            if (categories == null)
            {
                ArchonLogger.LogWarning("TerrainLoader: No 'categories' section found in terrain.json5", "core_data_loading");
                return terrainCategories;
            }

            foreach (var property in categories.Properties())
            {
                var categoryName = property.Name;
                var categoryData = property.Value as JObject;

                if (categoryData == null) continue;

                var category = new TerrainCategory
                {
                    Name = categoryName,
                    Color = Json5Loader.GetColor32(categoryData, "color", Color.white),
                    IsWater = Json5Loader.GetBool(categoryData, "is_water", false),
                    InlandSea = Json5Loader.GetBool(categoryData, "inland_sea", false),
                    MovementCost = Json5Loader.GetFloat(categoryData, "movement_cost", 1.0f),
                    TerrainOverride = new HashSet<int>(Json5Loader.GetIntArray(categoryData, "terrain_override"))
                };

                terrainCategories[categoryName] = category;
            }

            return terrainCategories;
        }

        /// <summary>
        /// Merge terrain category overrides into water province data
        /// Terrain overrides can specify specific provinces that should use certain terrain types
        /// </summary>
        private static void MergeTerrainWithWaterData(ref WaterProvinceData waterData, Dictionary<string, TerrainCategory> terrainCategories)
        {
            foreach (var category in terrainCategories.Values)
            {
                if (!category.IsWater) continue;

                // Add terrain override provinces to appropriate water sets
                foreach (var provinceId in category.TerrainOverride)
                {
                    if (category.Name == "ocean" || category.Name == "inland_ocean")
                    {
                        if (category.InlandSea)
                        {
                            waterData.LakeProvinces.Add(provinceId);
                        }
                        else
                        {
                            waterData.OceanProvinces.Add(provinceId);
                        }
                    }
                    else if (category.Name.Contains("sea"))
                    {
                        waterData.SeaProvinces.Add(provinceId);
                    }
                }
            }
        }

        /// <summary>
        /// Create default water province data when files are missing
        /// Ensures the system can run without data files
        /// </summary>
        private static WaterProvinceData CreateDefaultWaterData()
        {
            return new WaterProvinceData
            {
                SeaProvinces = new HashSet<int>(),
                LakeProvinces = new HashSet<int>(),
                OceanProvinces = new HashSet<int>(),
                TerrainColors = new Dictionary<string, Color32>
                {
                    ["sea"] = new Color32(31, 95, 153, 255),      // #1f5f99
                    ["lake"] = new Color32(74, 139, 194, 255),    // #4a8bc2
                    ["ocean"] = new Color32(13, 47, 79, 255),     // #0d2f4f
                    ["unowned_land"] = new Color32(196, 184, 150, 255) // #c4b896
                },
                MapWidth = 5632,
                MapHeight = 2048,
                MaxProvinces = 4942
            };
        }

        /// <summary>
        /// Check if a province ID is a water province (any type)
        /// Hot path function - used during province initialization
        /// </summary>
        public static bool IsWaterProvince(int provinceId)
        {
            if (!_dataLoaded) return false;

            return _cachedWaterData.SeaProvinces.Contains(provinceId) ||
                   _cachedWaterData.LakeProvinces.Contains(provinceId) ||
                   _cachedWaterData.OceanProvinces.Contains(provinceId);
        }

        /// <summary>
        /// Get terrain type ID for a province based on water/land classification
        /// Returns: 0 = water (ocean/sea/lake), 1+ = land terrain types
        /// Used by the shader to determine rendering approach
        /// </summary>
        public static byte GetTerrainTypeForProvince(int provinceId)
        {
            if (!_dataLoaded) return 1; // Default to land

            // Check water types first (terrain type 0 = water for shader)
            if (_cachedWaterData.SeaProvinces.Contains(provinceId) ||
                _cachedWaterData.LakeProvinces.Contains(provinceId) ||
                _cachedWaterData.OceanProvinces.Contains(provinceId))
            {
                return 0; // Water terrain type
            }

            // Check terrain category overrides for specific land types
            if (_cachedTerrainCategories != null)
            {
                foreach (var category in _cachedTerrainCategories.Values)
                {
                    if (!category.IsWater && category.TerrainOverride.Contains(provinceId))
                    {
                        // Return terrain type based on category name
                        return GetTerrainTypeId(category.Name);
                    }
                }
            }

            return 1; // Default land terrain type
        }

        /// <summary>
        /// Convert terrain category name to terrain type ID
        /// Following EU4/Paradox conventions for terrain types
        /// </summary>
        private static byte GetTerrainTypeId(string categoryName)
        {
            return categoryName switch
            {
                "ocean" or "inland_ocean" => 0,      // Water
                "grasslands" or "farmlands" => 1,    // Plains
                "hills" or "highlands" => 2,         // Hills
                "mountains" => 3,                     // Mountains
                "woods" or "forest" => 4,             // Forest
                "marsh" => 5,                         // Marsh
                "desert" or "coastal_desert" => 6,    // Desert
                "steppes" or "drylands" => 7,         // Steppes
                "arctic" or "glacier" => 8,           // Arctic
                "jungle" or "tropical" => 9,          // Jungle
                _ => 1                                // Default to plains
            };
        }

        /// <summary>
        /// Get terrain color for a specific terrain type
        /// Used by GPU texture population
        /// </summary>
        public static Color32 GetTerrainColor(string terrainType)
        {
            if (!_dataLoaded) return Color.white;

            if (_cachedWaterData.TerrainColors.TryGetValue(terrainType, out Color32 color))
            {
                return color;
            }

            if (_cachedTerrainCategories?.TryGetValue(terrainType, out TerrainCategory category) == true)
            {
                return category.Color;
            }

            return Color.white; // Fallback
        }

        /// <summary>
        /// Clear cached data - useful for testing or runtime reloading
        /// </summary>
        public static void ClearCache()
        {
            _dataLoaded = false;
            _cachedWaterData = default;
            _cachedTerrainCategories = null;
        }
    }
}