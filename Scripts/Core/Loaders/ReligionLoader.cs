using System.IO;
using Core.Registries;
using Utils;
using UnityEngine;

namespace Core.Loaders
{
    /// <summary>
    /// Loads religion definitions from data files
    /// Religions are static data with no dependencies on other entities
    /// Following data-linking-architecture.md specifications
    /// </summary>
    public static class ReligionLoader
    {
        /// <summary>
        /// Load all religions from common/religions directory
        /// </summary>
        public static void LoadReligions(Registry<ReligionData> religionRegistry, string dataPath)
        {
            string religionsPath = Path.Combine(dataPath, "common", "religions");

            if (!Directory.Exists(religionsPath))
            {
                ArchonLogger.LogWarning($"Religions directory not found: {religionsPath}");
                CreateDefaultReligions(religionRegistry);
                return;
            }

            var religionFiles = Directory.GetFiles(religionsPath, "*.txt");
            ArchonLogger.Log($"ReligionLoader: Found {religionFiles.Length} religion files in {religionsPath}");

            int loaded = 0;
            foreach (var file in religionFiles)
            {
                try
                {
                    LoadReligionFile(religionRegistry, file);
                    loaded++;
                }
                catch (System.Exception e)
                {
                    ArchonLogger.LogError($"ReligionLoader: Failed to load {file}: {e.Message}");
                }
            }

            ArchonLogger.Log($"ReligionLoader: Loaded {loaded}/{religionFiles.Length} religion files, {religionRegistry.Count} religions registered");

            // If no religions loaded, create defaults
            if (religionRegistry.Count == 0)
            {
                ArchonLogger.LogWarning("ReligionLoader: No religions loaded, creating defaults");
                CreateDefaultReligions(religionRegistry);
            }
        }

        /// <summary>
        /// Load religions from a single file
        /// </summary>
        private static void LoadReligionFile(Registry<ReligionData> religionRegistry, string filePath)
        {
            var content = File.ReadAllText(filePath);

            // For now, create simple religion entries
            // TODO: In a full implementation, this would parse the actual religion file format
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // Extract religion names from filename or create basic ones
            if (fileName.ToLower().Contains("christian"))
            {
                RegisterReligionIfNotExists(religionRegistry, "catholic", "Catholic", "catholic_icon");
                RegisterReligionIfNotExists(religionRegistry, "orthodox", "Orthodox", "orthodox_icon");
                RegisterReligionIfNotExists(religionRegistry, "protestant", "Protestant", "protestant_icon");
            }
            else if (fileName.ToLower().Contains("muslim"))
            {
                RegisterReligionIfNotExists(religionRegistry, "sunni", "Sunni", "sunni_icon");
                RegisterReligionIfNotExists(religionRegistry, "shiite", "Shiite", "shiite_icon");
            }
            else if (fileName.ToLower().Contains("pagan"))
            {
                RegisterReligionIfNotExists(religionRegistry, "norse_pagan", "Norse Pagan", "norse_icon");
                RegisterReligionIfNotExists(religionRegistry, "slavic_pagan", "Slavic Pagan", "slavic_icon");
            }
            else if (fileName.ToLower().Contains("eastern"))
            {
                RegisterReligionIfNotExists(religionRegistry, "hinduism", "Hinduism", "hindu_icon");
                RegisterReligionIfNotExists(religionRegistry, "buddhism", "Buddhism", "buddhist_icon");
            }
        }

        /// <summary>
        /// Register a religion if it doesn't already exist
        /// </summary>
        private static void RegisterReligionIfNotExists(Registry<ReligionData> religionRegistry, string key, string name, string iconPath)
        {
            if (!religionRegistry.Exists(key))
            {
                var religion = new ReligionData
                {
                    Name = name,
                    IconPath = iconPath
                };

                religionRegistry.Register(key, religion);
            }
        }

        /// <summary>
        /// Create default religions if no data files found
        /// Ensures the game can run even without complete data
        /// </summary>
        private static void CreateDefaultReligions(Registry<ReligionData> religionRegistry)
        {
            var defaultReligions = new[]
            {
                ("catholic", "Catholic"),
                ("orthodox", "Orthodox"),
                ("protestant", "Protestant"),
                ("sunni", "Sunni"),
                ("shiite", "Shiite"),
                ("hinduism", "Hinduism"),
                ("buddhism", "Buddhism"),
                ("norse_pagan", "Norse Pagan"),
                ("animism", "Animism")
            };

            foreach (var (key, name) in defaultReligions)
            {
                var religion = new ReligionData
                {
                    Name = name,
                    IconPath = $"{key}_icon"
                };

                religionRegistry.Register(key, religion);
            }

            ArchonLogger.Log($"ReligionLoader: Created {defaultReligions.Length} default religions");
        }
    }
}