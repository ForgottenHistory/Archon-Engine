using System.Collections.Generic;
using System.IO;
using Utils;

namespace Core.Loaders
{
    /// <summary>
    /// Loads country tags using ManifestLoader (Pattern 2)
    /// Parses common/country_tags/00_countries.txt to get tag → file mapping
    /// Example: SWE = "countries/Sweden.txt"
    /// Following paradox-data-patterns-guide.md specifications
    /// </summary>
    public class CountryTagLoader : ManifestLoader<object>
    {
        /// <summary>
        /// Load country tags from 00_countries.txt
        /// Returns mapping of country tags to their definition file paths
        /// </summary>
        public static CountryTagLoadResult LoadCountryTags(string dataPath)
        {
            var loader = new CountryTagLoader();
            var manifestPath = Path.Combine(dataPath, "common", "country_tags", "00_countries.txt");

            DominionLogger.Log($"CountryTagLoader: Loading country tags from {manifestPath}");

            var result = loader.LoadManifest(manifestPath);

            if (!result.Success)
            {
                return CountryTagLoadResult.CreateFailed(result.ErrorMessage);
            }

            // Validate country tag format
            var validTags = new Dictionary<string, string>();
            var errors = new List<string>(result.Errors);

            foreach (var kvp in result.Manifest)
            {
                var tag = kvp.Key;
                var filePath = kvp.Value;

                // Validate tag format (should be 3 characters)
                if (tag.Length != 3)
                {
                    var warning = $"Country tag '{tag}' is not 3 characters, skipping";
                    errors.Add(warning);
                    DominionLogger.LogWarning(warning);
                    continue;
                }

                // Validate file path
                if (string.IsNullOrEmpty(filePath))
                {
                    var warning = $"Country tag '{tag}' has empty file path, skipping";
                    errors.Add(warning);
                    DominionLogger.LogWarning(warning);
                    continue;
                }

                validTags[tag] = filePath;
            }

            DominionLogger.Log($"CountryTagLoader: Loaded {validTags.Count} valid country tags");

            return CountryTagLoadResult.CreateSuccess(validTags, errors);
        }

        /// <summary>
        /// Override to add country-specific validation
        /// </summary>
        protected override bool IsComment(string key)
        {
            // Skip comments and special entries
            return base.IsComment(key) ||
                   key.Equals("REB", System.StringComparison.OrdinalIgnoreCase) ||
                   key.Equals("PIR", System.StringComparison.OrdinalIgnoreCase) ||
                   key.Equals("NAT", System.StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Result of country tag loading operation
    /// </summary>
    public struct CountryTagLoadResult
    {
        public bool Success;
        public Dictionary<string, string> CountryTags;
        public List<string> Errors;
        public string ErrorMessage;

        public static CountryTagLoadResult CreateSuccess(Dictionary<string, string> tags, List<string> errors)
        {
            return new CountryTagLoadResult
            {
                Success = true,
                CountryTags = tags,
                Errors = errors
            };
        }

        public static CountryTagLoadResult CreateFailed(string error)
        {
            return new CountryTagLoadResult
            {
                Success = false,
                ErrorMessage = error,
                Errors = new List<string> { error }
            };
        }
    }
}