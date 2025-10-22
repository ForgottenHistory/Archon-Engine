using System.IO;
using Core.Registries;
using Utils;
using UnityEngine;

namespace Core.Loaders
{
    /// <summary>
    /// Loads terrain type definitions from data files
    /// Terrain types are static data with no dependencies on other entities
    /// Following data-linking-architecture.md specifications
    /// </summary>
    public static class TerrainLoader
    {
        /// <summary>
        /// Load all terrain types from map/terrain.txt file
        /// </summary>
        public static void LoadTerrains(Registry<Core.Registries.TerrainData> terrainRegistry, string dataPath)
        {
            string terrainFilePath = Path.Combine(dataPath, "map", "terrain.txt");

            if (!File.Exists(terrainFilePath))
            {
                ArchonLogger.LogCoreDataLoadingWarning($"Terrain file not found: {terrainFilePath}");
                CreateDefaultTerrains(terrainRegistry);
                return;
            }

            try
            {
                LoadTerrainFile(terrainRegistry, terrainFilePath);
                ArchonLogger.LogCoreDataLoading($"TerrainLoader: Loaded terrain file, {terrainRegistry.Count} terrains registered");
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogCoreDataLoadingError($"TerrainLoader: Failed to load {terrainFilePath}: {e.Message}");
                CreateDefaultTerrains(terrainRegistry);
            }

            // If no terrains loaded, create defaults
            if (terrainRegistry.Count == 0)
            {
                ArchonLogger.LogCoreDataLoadingWarning("TerrainLoader: No terrains loaded, creating defaults");
                CreateDefaultTerrains(terrainRegistry);
            }
        }

        /// <summary>
        /// Load terrain types from the terrain file
        /// </summary>
        private static void LoadTerrainFile(Registry<Core.Registries.TerrainData> terrainRegistry, string filePath)
        {
            var content = File.ReadAllText(filePath);

            // For now, create standard terrain types
            // TODO: In a full implementation, this would parse the actual terrain file format

            // Create basic terrain types with IDs matching common EU4/Paradox conventions
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

        /// <summary>
        /// Register a terrain type if it doesn't already exist
        /// </summary>
        private static void RegisterTerrainIfNotExists(Registry<Core.Registries.TerrainData> terrainRegistry, string key, string name, byte terrainId)
        {
            if (!terrainRegistry.Exists(key))
            {
                var terrain = new Core.Registries.TerrainData
                {
                    Name = name,
                    TerrainId = terrainId
                };

                terrainRegistry.Register(key, terrain);
            }
        }

        /// <summary>
        /// Create default terrain types if no data files found
        /// Ensures the game can run even without complete data
        /// </summary>
        private static void CreateDefaultTerrains(Registry<Core.Registries.TerrainData> terrainRegistry)
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
                var terrain = new Core.Registries.TerrainData
                {
                    Name = name,
                    TerrainId = terrainId
                };

                terrainRegistry.Register(key, terrain);
            }

            ArchonLogger.LogCoreDataLoading($"TerrainLoader: Created {defaultTerrains.Length} default terrains");
        }
    }
}