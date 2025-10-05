using System.IO;
using Core.Registries;
using Utils;
using UnityEngine;

namespace Core.Loaders
{
    /// <summary>
    /// Loads culture definitions from data files
    /// Cultures are static data with no dependencies on other entities
    /// Following data-linking-architecture.md specifications
    /// </summary>
    public static class CultureLoader
    {
        /// <summary>
        /// Load all cultures from common/cultures directory
        /// </summary>
        public static void LoadCultures(Registry<CultureData> cultureRegistry, string dataPath)
        {
            string culturesPath = Path.Combine(dataPath, "common", "cultures");

            if (!Directory.Exists(culturesPath))
            {
                ArchonLogger.LogWarning($"Cultures directory not found: {culturesPath}");
                CreateDefaultCultures(cultureRegistry);
                return;
            }

            var cultureFiles = Directory.GetFiles(culturesPath, "*.txt");
            ArchonLogger.Log($"CultureLoader: Found {cultureFiles.Length} culture files in {culturesPath}");

            int loaded = 0;
            foreach (var file in cultureFiles)
            {
                try
                {
                    LoadCultureFile(cultureRegistry, file);
                    loaded++;
                }
                catch (System.Exception e)
                {
                    ArchonLogger.LogError($"CultureLoader: Failed to load {file}: {e.Message}");
                }
            }

            ArchonLogger.Log($"CultureLoader: Loaded {loaded}/{cultureFiles.Length} culture files, {cultureRegistry.Count} cultures registered");

            // If no cultures loaded, create defaults
            if (cultureRegistry.Count == 0)
            {
                ArchonLogger.LogWarning("CultureLoader: No cultures loaded, creating defaults");
                CreateDefaultCultures(cultureRegistry);
            }
        }

        /// <summary>
        /// Load cultures from a single file
        /// </summary>
        private static void LoadCultureFile(Registry<CultureData> cultureRegistry, string filePath)
        {
            var content = File.ReadAllText(filePath);

            // For now, create simple culture entries based on filename
            // TODO: In a full implementation, this would parse the actual culture file format
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // Extract culture names from filename or create basic ones
            if (fileName.ToLower().Contains("germanic"))
            {
                RegisterCultureIfNotExists(cultureRegistry, "german", "German", "germanic");
                RegisterCultureIfNotExists(cultureRegistry, "english", "English", "germanic");
                RegisterCultureIfNotExists(cultureRegistry, "dutch", "Dutch", "germanic");
                RegisterCultureIfNotExists(cultureRegistry, "danish", "Danish", "germanic");
                RegisterCultureIfNotExists(cultureRegistry, "swedish", "Swedish", "germanic");
                RegisterCultureIfNotExists(cultureRegistry, "norwegian", "Norwegian", "germanic");
            }
            else if (fileName.ToLower().Contains("latin"))
            {
                RegisterCultureIfNotExists(cultureRegistry, "french", "French", "latin");
                RegisterCultureIfNotExists(cultureRegistry, "spanish", "Spanish", "latin");
                RegisterCultureIfNotExists(cultureRegistry, "italian", "Italian", "latin");
                RegisterCultureIfNotExists(cultureRegistry, "portuguese", "Portuguese", "latin");
            }
            else if (fileName.ToLower().Contains("slavic"))
            {
                RegisterCultureIfNotExists(cultureRegistry, "russian", "Russian", "slavic");
                RegisterCultureIfNotExists(cultureRegistry, "polish", "Polish", "slavic");
                RegisterCultureIfNotExists(cultureRegistry, "czech", "Czech", "slavic");
                RegisterCultureIfNotExists(cultureRegistry, "serbian", "Serbian", "slavic");
            }
            else if (fileName.ToLower().Contains("byzantine"))
            {
                RegisterCultureIfNotExists(cultureRegistry, "greek", "Greek", "byzantine");
                RegisterCultureIfNotExists(cultureRegistry, "bulgarian", "Bulgarian", "byzantine");
            }
        }

        /// <summary>
        /// Register a culture if it doesn't already exist
        /// </summary>
        private static void RegisterCultureIfNotExists(Registry<CultureData> cultureRegistry, string key, string name, string cultureGroup)
        {
            if (!cultureRegistry.Exists(key))
            {
                var culture = new CultureData
                {
                    Name = name,
                    CultureGroup = cultureGroup
                };

                cultureRegistry.Register(key, culture);
            }
        }

        /// <summary>
        /// Create default cultures if no data files found
        /// Ensures the game can run even without complete data
        /// </summary>
        private static void CreateDefaultCultures(Registry<CultureData> cultureRegistry)
        {
            var defaultCultures = new[]
            {
                ("english", "English", "germanic"),
                ("french", "French", "latin"),
                ("german", "German", "germanic"),
                ("spanish", "Spanish", "latin"),
                ("italian", "Italian", "latin"),
                ("swedish", "Swedish", "germanic"),
                ("danish", "Danish", "germanic"),
                ("norwegian", "Norwegian", "germanic"),
                ("polish", "Polish", "slavic"),
                ("russian", "Russian", "slavic"),
                ("greek", "Greek", "byzantine"),
                ("turkish", "Turkish", "turko_semitic"),
                ("arabic", "Arabic", "turko_semitic"),
                ("persian", "Persian", "turko_semitic"),
                ("chinese", "Chinese", "chinese"),
                ("japanese", "Japanese", "japanese")
            };

            foreach (var (key, name, group) in defaultCultures)
            {
                var culture = new CultureData
                {
                    Name = name,
                    CultureGroup = group
                };

                cultureRegistry.Register(key, culture);
            }

            ArchonLogger.Log($"CultureLoader: Created {defaultCultures.Length} default cultures");
        }
    }
}