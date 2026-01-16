using System.IO;
using Core.Registries;
using Newtonsoft.Json.Linq;

namespace Core.Loaders
{
    /// <summary>
    /// Loads terrain type definitions from terrain.json5.
    /// This is the single source of truth for terrain definitions.
    /// TerrainColorMapper (Map layer) should use the same file.
    ///
    /// Terrain IDs are assigned sequentially based on order in the file,
    /// ensuring consistency between Core and Map layers.
    /// </summary>
    [LoaderMetadata("terrain", Description = "Load terrain type definitions", Priority = 10, Required = true)]
    public class TerrainLoader : ILoaderFactory
    {
        public void Load(LoaderContext context)
        {
            LoadTerrains(context.Registries.Terrains, context.DataPath);
        }

        /// <summary>
        /// Load all terrain types from map/terrain.json5 file.
        /// </summary>
        public static void LoadTerrains(Registry<TerrainData> terrainRegistry, string dataPath)
        {
            string terrainFilePath = Path.Combine(dataPath, "map", "terrain.json5");

            if (!File.Exists(terrainFilePath))
            {
                ArchonLogger.LogWarning($"TerrainLoader: terrain.json5 not found at {terrainFilePath}, using defaults", "core_data_loading");
                CreateDefaultTerrains(terrainRegistry);
                return;
            }

            try
            {
                LoadTerrainJson5(terrainRegistry, terrainFilePath);
                ArchonLogger.Log($"TerrainLoader: Loaded {terrainRegistry.Count} terrains from terrain.json5", "core_data_loading");
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"TerrainLoader: Failed to load {terrainFilePath}: {e.Message}", "core_data_loading");
                CreateDefaultTerrains(terrainRegistry);
            }

            if (terrainRegistry.Count == 0)
            {
                ArchonLogger.LogWarning("TerrainLoader: No terrains loaded, creating defaults", "core_data_loading");
                CreateDefaultTerrains(terrainRegistry);
            }
        }

        private static void LoadTerrainJson5(Registry<TerrainData> terrainRegistry, string filePath)
        {
            var json = Json5Loader.LoadJson5File(filePath);
            var categories = json["categories"] as JObject;

            if (categories == null)
            {
                ArchonLogger.LogWarning("TerrainLoader: No 'categories' section found in terrain.json5", "core_data_loading");
                return;
            }

            byte terrainId = 0;
            foreach (var property in categories.Properties())
            {
                string key = property.Name;
                var terrainObj = property.Value as JObject;

                if (terrainObj == null)
                    continue;

                // Parse terrain properties
                var terrain = new TerrainData
                {
                    TerrainId = terrainId,
                    Name = FormatTerrainName(key),
                    IsWater = GetBool(terrainObj, "is_water", false),
                    MovementCost = GetFloat(terrainObj, "movement_cost", 1.0f),
                    DefenceBonus = GetInt(terrainObj, "defence", 0),
                    SupplyLimit = GetInt(terrainObj, "supply_limit", 5)
                };

                // Parse color if present (for reference, mainly used by Map layer)
                var colorArray = terrainObj["color"] as JArray;
                if (colorArray != null && colorArray.Count >= 3)
                {
                    terrain.ColorR = (byte)colorArray[0].Value<int>();
                    terrain.ColorG = (byte)colorArray[1].Value<int>();
                    terrain.ColorB = (byte)colorArray[2].Value<int>();
                }

                terrainRegistry.Register(key, terrain);
                terrainId++;
            }
        }

        private static string FormatTerrainName(string key)
        {
            // Convert snake_case to Title Case
            // e.g., "inland_ocean" -> "Inland Ocean"
            if (string.IsNullOrEmpty(key))
                return key;

            var words = key.Split('_');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
                }
            }
            return string.Join(" ", words);
        }

        private static bool GetBool(JObject obj, string key, bool defaultValue)
        {
            var token = obj[key];
            return token?.Value<bool>() ?? defaultValue;
        }

        private static float GetFloat(JObject obj, string key, float defaultValue)
        {
            var token = obj[key];
            return token?.Value<float>() ?? defaultValue;
        }

        private static int GetInt(JObject obj, string key, int defaultValue)
        {
            var token = obj[key];
            return token?.Value<int>() ?? defaultValue;
        }

        private static void CreateDefaultTerrains(Registry<TerrainData> terrainRegistry)
        {
            // Default terrains matching the typical terrain.json5 order
            var defaultTerrains = new[]
            {
                ("ocean", "Ocean", true, 1.0f, 0, 0),
                ("inland_ocean", "Inland Ocean", true, 1.0f, 0, 0),
                ("grasslands", "Grasslands", false, 1.0f, 0, 8),
                ("plains", "Plains", false, 1.0f, 0, 8),
                ("hills", "Hills", false, 1.4f, 1, 5),
                ("highlands", "Highlands", false, 1.3f, 1, 4),
                ("mountain", "Mountain", false, 1.5f, 2, 3),
                ("desert", "Desert", false, 1.1f, 0, 3),
                ("forest", "Forest", false, 1.25f, 1, 4),
                ("jungle", "Jungle", false, 1.4f, 1, 3),
                ("marsh", "Marsh", false, 1.3f, 0, 3),
                ("snow", "Snow", false, 1.6f, 2, 2),
            };

            byte id = 0;
            foreach (var (key, name, isWater, moveCost, defence, supply) in defaultTerrains)
            {
                var terrain = new TerrainData
                {
                    TerrainId = id,
                    Name = name,
                    IsWater = isWater,
                    MovementCost = moveCost,
                    DefenceBonus = defence,
                    SupplyLimit = supply
                };
                terrainRegistry.Register(key, terrain);
                id++;
            }

            ArchonLogger.Log($"TerrainLoader: Created {defaultTerrains.Length} default terrains", "core_data_loading");
        }
    }
}
