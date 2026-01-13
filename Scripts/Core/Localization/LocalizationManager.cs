using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace Core.Localization
{
    /// <summary>
    /// High-level facade for the localization system.
    /// Provides simple API for getting localized strings.
    ///
    /// Usage:
    ///   LocalizationManager.Initialize("path/to/localisation");
    ///   string name = LocalizationManager.Get("PROV123");  // "Province Name"
    ///   string country = LocalizationManager.Get("RED");    // "Red Empire"
    /// </summary>
    public static class LocalizationManager
    {
        // Current language
        private static string _currentLanguage = "english";

        // Loaded localization data
        private static MultiLanguageExtractor.MultiLanguageResult _multiLangResult;
        private static LocalizationFallbackChain.FallbackChain _fallbackChain;

        // Simple cache for frequently accessed strings (managed, for UI layer)
        private static Dictionary<string, string> _cache = new Dictionary<string, string>(1000);

        // State
        private static bool _isInitialized;
        private static string _loadedPath;

        /// <summary>
        /// Whether the localization system is initialized
        /// </summary>
        public static bool IsInitialized => _isInitialized;

        /// <summary>
        /// Current active language
        /// </summary>
        public static string CurrentLanguage => _currentLanguage;

        /// <summary>
        /// Available languages after loading
        /// </summary>
        public static IReadOnlyList<string> AvailableLanguages
        {
            get
            {
                if (!_isInitialized) return Array.Empty<string>();

                var languages = new List<string>();
                for (int i = 0; i < _multiLangResult.AvailableLanguages.Length; i++)
                {
                    languages.Add(_multiLangResult.AvailableLanguages[i].ToString());
                }
                return languages;
            }
        }

        /// <summary>
        /// Initialize the localization system by loading all language files from a directory
        /// </summary>
        /// <param name="localisationPath">Path to localisation folder (contains language subfolders)</param>
        /// <param name="defaultLanguage">Default language to use (e.g., "english")</param>
        public static void Initialize(string localisationPath, string defaultLanguage = "english")
        {
            if (_isInitialized)
            {
                ArchonLogger.LogWarning("LocalizationManager already initialized. Call Shutdown() first to reinitialize.", "core_simulation");
                return;
            }

            _currentLanguage = defaultLanguage;
            _loadedPath = localisationPath;

            try
            {
                // Load all language files
                var languageFiles = LoadLanguageFiles(localisationPath);

                if (languageFiles.Count == 0)
                {
                    ArchonLogger.Log($"No localization files found in: {localisationPath} (keys will be used as fallback)", "core_simulation");
                    _isInitialized = true; // Still mark as initialized, will return keys as fallback
                    return;
                }

                // Parse all files
                _multiLangResult = MultiLanguageExtractor.LoadMultipleLanguages(languageFiles, Allocator.Persistent);

                // Dispose the file byte arrays (no longer needed after parsing)
                foreach (var kvp in languageFiles)
                {
                    kvp.Value.Dispose();
                }

                // Create fallback chain
                var langKey = new FixedString64Bytes();
                langKey.Append('l');
                langKey.Append('_');
                foreach (char c in defaultLanguage)
                {
                    if (langKey.Length >= 63) break;
                    langKey.Append(c);
                }

                _fallbackChain = LocalizationFallbackChain.CreateStandardFallbackChain(langKey, Allocator.Persistent);

                _isInitialized = true;
                _cache.Clear();

                ArchonLogger.Log($"LocalizationManager initialized: {_multiLangResult.AvailableLanguages.Length} languages, {_multiLangResult.TotalEntries} total entries", "core_simulation");
            }
            catch (Exception e)
            {
                ArchonLogger.LogError($"Failed to initialize LocalizationManager: {e.Message}", "core_simulation");
                _isInitialized = true; // Mark initialized anyway, will return keys as fallback
            }
        }

        /// <summary>
        /// Get a localized string by key
        /// </summary>
        /// <param name="key">The localization key (e.g., "PROV123", "RED", "UI_OK")</param>
        /// <returns>Localized string, or the key itself if not found</returns>
        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            // Check cache first
            if (_cache.TryGetValue(key, out string cached))
                return cached;

            // Not initialized - return key
            if (!_isInitialized || !_multiLangResult.Success)
            {
                return key;
            }

            // Convert key to FixedString for lookup
            var fixedKey = new FixedString64Bytes();
            foreach (char c in key)
            {
                if (fixedKey.Length >= 63) break;
                fixedKey.Append(c);
            }

            // Try to resolve with fallback chain
            var result = LocalizationFallbackChain.ResolveWithFallback(
                _multiLangResult, _fallbackChain, fixedKey);

            string value;
            if (result.Success)
            {
                value = result.Value.ToString();
            }
            else
            {
                // Return key as fallback
                value = key;
            }

            // Cache the result
            _cache[key] = value;
            return value;
        }

        /// <summary>
        /// Get a localized string with parameter substitution
        /// </summary>
        /// <param name="key">The localization key</param>
        /// <param name="parameters">Parameters to substitute (key-value pairs)</param>
        /// <returns>Localized string with parameters replaced</returns>
        public static string Get(string key, params (string key, string value)[] parameters)
        {
            string baseString = Get(key);

            if (parameters == null || parameters.Length == 0)
                return baseString;

            // Simple string replacement for common case
            // For complex cases, use StringReplacementSystem directly
            foreach (var (paramKey, paramValue) in parameters)
            {
                baseString = baseString.Replace($"${paramKey}$", paramValue);
                baseString = baseString.Replace($"{{{paramKey}}}", paramValue);
                baseString = baseString.Replace($"[{paramKey}]", paramValue);
            }

            return baseString;
        }

        /// <summary>
        /// Check if a localization key exists
        /// </summary>
        public static bool HasKey(string key)
        {
            if (!_isInitialized || !_multiLangResult.Success || string.IsNullOrEmpty(key))
                return false;

            var fixedKey = new FixedString64Bytes();
            foreach (char c in key)
            {
                if (fixedKey.Length >= 63) break;
                fixedKey.Append(c);
            }

            var result = LocalizationFallbackChain.ResolveWithFallback(
                _multiLangResult, _fallbackChain, fixedKey);

            return result.Success;
        }

        /// <summary>
        /// Change the current language
        /// </summary>
        /// <param name="language">Language code (e.g., "english", "french", "german")</param>
        public static void SetLanguage(string language)
        {
            if (_currentLanguage == language)
                return;

            _currentLanguage = language;
            _cache.Clear(); // Clear cache to reload strings in new language

            // Rebuild fallback chain for new language
            if (_fallbackChain.Languages.IsCreated)
            {
                _fallbackChain.Dispose();
            }

            var langKey = new FixedString64Bytes();
            langKey.Append('l');
            langKey.Append('_');
            foreach (char c in language)
            {
                if (langKey.Length >= 63) break;
                langKey.Append(c);
            }

            _fallbackChain = LocalizationFallbackChain.CreateStandardFallbackChain(langKey, Allocator.Persistent);

            ArchonLogger.Log($"Language changed to: {language}", "core_simulation");
        }

        /// <summary>
        /// Clear the string cache (call after dynamic content changes)
        /// </summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }

        /// <summary>
        /// Shutdown and release all resources
        /// </summary>
        public static void Shutdown()
        {
            if (!_isInitialized)
                return;

            if (_multiLangResult.LanguageData.IsCreated)
            {
                _multiLangResult.Dispose();
            }

            if (_fallbackChain.Languages.IsCreated)
            {
                _fallbackChain.Dispose();
            }

            _cache.Clear();
            _isInitialized = false;
            _loadedPath = null;

            ArchonLogger.Log("LocalizationManager shutdown complete", "core_simulation");
        }

        /// <summary>
        /// Load all YAML files from localisation directory structure
        /// </summary>
        private static Dictionary<string, NativeArray<byte>> LoadLanguageFiles(string basePath)
        {
            var result = new Dictionary<string, NativeArray<byte>>();

            if (!Directory.Exists(basePath))
            {
                ArchonLogger.Log($"Localisation directory not found: {basePath} (localization disabled)", "core_simulation");
                return result;
            }

            // Scan for language subdirectories (e.g., english/, french/)
            foreach (var langDir in Directory.GetDirectories(basePath))
            {
                string langName = Path.GetFileName(langDir);
                string langKey = $"l_{langName}";

                // Concatenate all .yml files in this language directory
                var allBytes = new List<byte>();

                foreach (var ymlFile in Directory.GetFiles(langDir, "*.yml"))
                {
                    try
                    {
                        byte[] fileBytes = File.ReadAllBytes(ymlFile);

                        // Skip UTF-8 BOM if present
                        int startIndex = 0;
                        if (fileBytes.Length >= 3 &&
                            fileBytes[0] == 0xEF &&
                            fileBytes[1] == 0xBB &&
                            fileBytes[2] == 0xBF)
                        {
                            startIndex = 3;
                        }

                        for (int i = startIndex; i < fileBytes.Length; i++)
                        {
                            allBytes.Add(fileBytes[i]);
                        }

                        // Add newline between files
                        allBytes.Add((byte)'\n');
                    }
                    catch (Exception e)
                    {
                        ArchonLogger.LogWarning($"Failed to read localization file {ymlFile}: {e.Message}", "core_simulation");
                    }
                }

                if (allBytes.Count > 0)
                {
                    var nativeArray = new NativeArray<byte>(allBytes.Count, Allocator.Persistent);
                    for (int i = 0; i < allBytes.Count; i++)
                    {
                        nativeArray[i] = allBytes[i];
                    }
                    result[langKey] = nativeArray;

                    ArchonLogger.Log($"Loaded {langKey}: {allBytes.Count} bytes from {Directory.GetFiles(langDir, "*.yml").Length} files", "core_simulation");
                }
            }

            return result;
        }

        /// <summary>
        /// Get statistics about loaded localization data
        /// </summary>
        public static (int languages, int totalEntries, float completeness) GetStatistics()
        {
            if (!_isInitialized || !_multiLangResult.Success)
                return (0, 0, 0f);

            MultiLanguageExtractor.GetLanguageStatistics(
                _multiLangResult,
                out int totalLanguages,
                out int totalUniqueKeys,
                out float averageCompleteness);

            return (totalLanguages, totalUniqueKeys, averageCompleteness);
        }
    }
}
