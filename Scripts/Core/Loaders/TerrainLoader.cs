using System.IO;
using Core.Registries;
using Utils;

namespace Core.Loaders
{
    /// <summary>
    /// Loads terrain type definitions from data files.
    /// Terrain types are static data with no dependencies on other entities.
    /// </summary>
    [LoaderMetadata("terrain", Description = "Load terrain type definitions", Priority = 10, Required = true)]
    public class TerrainLoader : ILoaderFactory
    {
        public void Load(LoaderContext context)
        {
            LoadTerrains(context.Registries.Terrains, context.DataPath);
        }

        /// <summary>
        /// Load all terrain types from map/terrain.txt file.
        /// </summary>
        public static void LoadTerrains(Registry<TerrainData> terrainRegistry, string dataPath)
        {
            string terrainFilePath = Path.Combine(dataPath, "map", "terrain.txt");

            if (!File.Exists(terrainFilePath))
            {
                ArchonLogger.LogWarning($"Terrain file not found: {terrainFilePath}", "core_data_loading");
                CreateDefaultTerrains(terrainRegistry);
                return;
            }

            try
            {
                LoadTerrainFile(terrainRegistry, terrainFilePath);
                ArchonLogger.Log($"TerrainLoader: Loaded terrain file, {terrainRegistry.Count} terrains registered", "core_data_loading");
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

        private static void LoadTerrainFile(Registry<TerrainData> terrainRegistry, string filePath)
        {
            var content = File.ReadAllText(filePath);

            // Create standard terrain types
            // TODO: Parse actual terrain file format
            RegisterTerrainIfNotExists(terrainRegistry, "ocean", "Ocean", 0);
            RegisterTerrainIfNotExists(terrainRegistry, "grasslands", "Grasslands", 1);
            RegisterTerrainIfNotExists(terrainRegistry, "hills", "Hills", 2);
            RegisterTerrainIfNotExists(terrainRegistry, "mountains", "Mountains", 3);
            RegisterTerrainIfNotExists(terrainRegistry, "woods", "Woods", 4);
            RegisterTerrainIfNotExists(terrainRegistry, "forest", "Forest", 5);
            RegisterTerrainIfNotExists(terrainRegistry, "marsh", "Marsh", 6);
            RegisterTerrainIfNotExists(terrainRegistry, "desert", "Desert", 7);
            RegisterTerrainIfNotExists(terrainRegistry, "coastal_desert", "Coastal Desert", 8);
            RegisterTerrainIfNotExists(terrainRegistry, "steppes", "Steppes", 9);
            RegisterTerrainIfNotExists(terrainRegistry, "farmlands", "Farmlands", 10);
            RegisterTerrainIfNotExists(terrainRegistry, "drylands", "Drylands", 11);
            RegisterTerrainIfNotExists(terrainRegistry, "highlands", "Highlands", 12);
            RegisterTerrainIfNotExists(terrainRegistry, "arctic", "Arctic", 13);
            RegisterTerrainIfNotExists(terrainRegistry, "glacial", "Glacial", 14);
            RegisterTerrainIfNotExists(terrainRegistry, "tropical", "Tropical", 15);
            RegisterTerrainIfNotExists(terrainRegistry, "jungle", "Jungle", 16);
        }

        private static void RegisterTerrainIfNotExists(Registry<TerrainData> terrainRegistry, string key, string name, byte terrainId)
        {
            if (!terrainRegistry.Exists(key))
            {
                var terrain = new TerrainData
                {
                    Name = name,
                    TerrainId = terrainId
                };
                terrainRegistry.Register(key, terrain);
            }
        }

        private static void CreateDefaultTerrains(Registry<TerrainData> terrainRegistry)
        {
            var defaultTerrains = new[]
            {
                ("ocean", "Ocean", (byte)0),
                ("grasslands", "Grasslands", (byte)1),
                ("hills", "Hills", (byte)2),
                ("mountains", "Mountains", (byte)3),
                ("woods", "Woods", (byte)4),
                ("forest", "Forest", (byte)5),
                ("marsh", "Marsh", (byte)6),
                ("desert", "Desert", (byte)7),
                ("steppes", "Steppes", (byte)9),
                ("farmlands", "Farmlands", (byte)10),
                ("arctic", "Arctic", (byte)13),
                ("jungle", "Jungle", (byte)16)
            };

            foreach (var (key, name, terrainId) in defaultTerrains)
            {
                var terrain = new TerrainData
                {
                    Name = name,
                    TerrainId = terrainId
                };
                terrainRegistry.Register(key, terrain);
            }

            ArchonLogger.Log($"TerrainLoader: Created {defaultTerrains.Length} default terrains", "core_data_loading");
        }
    }
}
