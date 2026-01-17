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

                // Parse ENGINE terrain properties only
                // GAME layer parses additional fields via its own loader
                var terrain = new TerrainData
                {
                    TerrainId = terrainId,
                    Name = FormatTerrainName(key),
                    MovementCost = GetFloat(terrainObj, "movement_cost", 1.0f),
                    IsWater = GetBool(terrainObj, "is_water", false)
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

        private static float GetFloat(JObject obj, string key, float defaultValue)
        {
            var token = obj[key];
            return token?.Value<float>() ?? defaultValue;
        }

        private static bool GetBool(JObject obj, string key, bool defaultValue)
        {
            var token = obj[key];
            return token?.Value<bool>() ?? defaultValue;
        }

        private static void CreateDefaultTerrains(Registry<TerrainData> terrainRegistry)
        {
            // Default terrains - ENGINE fields only (name, movement_cost, is_water)
            // GAME layer adds defence, supply, etc. via customData
            var defaultTerrains = new[]
            {
                ("ocean", "Ocean", 1.0f, true),
                ("inland_ocean", "Inland Ocean", 1.0f, true),
                ("grasslands", "Grasslands", 1.0f, false),
                ("plains", "Plains", 1.0f, false),
                ("hills", "Hills", 1.4f, false),
                ("highlands", "Highlands", 1.3f, false),
                ("mountain", "Mountain", 1.5f, false),
                ("desert", "Desert", 1.1f, false),
                ("forest", "Forest", 1.25f, false),
                ("jungle", "Jungle", 1.4f, false),
                ("marsh", "Marsh", 1.3f, false),
                ("snow", "Snow", 1.6f, false),
            };

            byte id = 0;
            foreach (var (key, name, moveCost, isWater) in defaultTerrains)
            {
                var terrain = new TerrainData
                {
                    TerrainId = id,
                    Name = name,
                    MovementCost = moveCost,
                    IsWater = isWater
                };
                terrainRegistry.Register(key, terrain);
                id++;
            }

            ArchonLogger.Log($"TerrainLoader: Created {defaultTerrains.Length} default terrains", "core_data_loading");
        }
    }
}
