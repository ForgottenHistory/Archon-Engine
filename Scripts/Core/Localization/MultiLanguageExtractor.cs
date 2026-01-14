using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Core.Localization
{
    /// <summary>
    /// Multi-language extraction utilities for localization management
    /// Handles multiple language files and provides unified access
    /// </summary>
    public static class MultiLanguageExtractor
    {
        /// <summary>
        /// Container for multiple language results
        /// </summary>
        public struct MultiLanguageResult
        {
            public NativeHashMap<FixedString64Bytes, YAMLParser.YAMLParseResult> LanguageData;
            public NativeList<FixedString64Bytes> AvailableLanguages;
            public FixedString64Bytes DefaultLanguage;
            public int TotalEntries;
            public bool IsSuccess;

            public void Dispose()
            {
                if (LanguageData.IsCreated)
                {
                    // Dispose individual language results
                    var keys = LanguageData.GetKeyArray(Allocator.Temp);
                    foreach (var key in keys)
                    {
                        if (LanguageData.TryGetValue(key, out var result))
                        {
                            result.Dispose();
                        }
                    }
                    keys.Dispose();
                    LanguageData.Dispose();
                }

                if (AvailableLanguages.IsCreated)
                    AvailableLanguages.Dispose();
            }
        }

        /// <summary>
        /// Load multiple language files and create unified language data
        /// </summary>
        public static MultiLanguageResult LoadMultipleLanguages(
            Dictionary<string, NativeArray<byte>> languageFiles,
            Allocator allocator)
        {
            var result = new MultiLanguageResult
            {
                LanguageData = new NativeHashMap<FixedString64Bytes, YAMLParser.YAMLParseResult>(languageFiles.Count, allocator),
                AvailableLanguages = new NativeList<FixedString64Bytes>(languageFiles.Count, allocator),
                IsSuccess = false,
                TotalEntries = 0
            };

            try
            {
                foreach (var languageFile in languageFiles)
                {
                    string langCode = languageFile.Key;
                    var fileData = languageFile.Value;

                    // Parse the language file
                    var tokens = new NativeList<YAMLTokenizer.YAMLToken>(1000, Allocator.Temp);
                    var tokenizeResult = YAMLTokenizer.TokenizeYAML(fileData, tokens);

                    if (tokenizeResult.IsSuccess)
                    {
                        var parseResult = YAMLParser.ParseYAML(fileData, tokens.AsArray(), allocator);

                        if (parseResult.IsSuccess)
                        {
                            var langKey = new FixedString64Bytes();
                            for (int i = 0; i < Math.Min(langCode.Length, 63); i++)
                            {
                                langKey.Append(langCode[i]);
                            }

                            result.LanguageData.TryAdd(langKey, parseResult);
                            result.AvailableLanguages.Add(langKey);
                            result.TotalEntries += parseResult.EntriesFound;

                            // Set first language as default if not set
                            if (result.DefaultLanguage.Length == 0)
                            {
                                result.DefaultLanguage = langKey;
                            }
                        }
                    }

                    tokens.Dispose();
                }

                result.IsSuccess = result.AvailableLanguages.Length > 0;
            }
            catch (Exception)
            {
                result.Dispose();
                result.IsSuccess = false;
            }

            return result;
        }

        /// <summary>
        /// Get localized string with fallback support
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetLocalizedString(
            MultiLanguageResult multiLangResult,
            FixedString64Bytes preferredLanguage,
            uint keyHash,
            out FixedString512Bytes value,
            out FixedString64Bytes actualLanguage)
        {
            value = default;
            actualLanguage = default;

            // Try preferred language first
            if (multiLangResult.LanguageData.TryGetValue(preferredLanguage, out var preferredResult))
            {
                if (YAMLParser.TryGetLocalizedString(preferredResult, keyHash, out value))
                {
                    actualLanguage = preferredLanguage;
                    return true;
                }
            }

            // Fall back to default language
            if (!preferredLanguage.Equals(multiLangResult.DefaultLanguage) &&
                multiLangResult.LanguageData.TryGetValue(multiLangResult.DefaultLanguage, out var defaultResult))
            {
                if (YAMLParser.TryGetLocalizedString(defaultResult, keyHash, out value))
                {
                    actualLanguage = multiLangResult.DefaultLanguage;
                    return true;
                }
            }

            // Try any available language
            foreach (var lang in multiLangResult.AvailableLanguages)
            {
                if (lang.Equals(preferredLanguage) || lang.Equals(multiLangResult.DefaultLanguage))
                    continue;

                if (multiLangResult.LanguageData.TryGetValue(lang, out var langResult))
                {
                    if (YAMLParser.TryGetLocalizedString(langResult, keyHash, out value))
                    {
                        actualLanguage = lang;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Get all available translations for a key
        /// </summary>
        public static NativeHashMap<FixedString64Bytes, FixedString512Bytes> GetAllTranslations(
            MultiLanguageResult multiLangResult,
            uint keyHash,
            Allocator allocator)
        {
            var translations = new NativeHashMap<FixedString64Bytes, FixedString512Bytes>(
                multiLangResult.AvailableLanguages.Length, allocator);

            foreach (var lang in multiLangResult.AvailableLanguages)
            {
                if (multiLangResult.LanguageData.TryGetValue(lang, out var langResult))
                {
                    if (YAMLParser.TryGetLocalizedString(langResult, keyHash, out var value))
                    {
                        translations.TryAdd(lang, value);
                    }
                }
            }

            return translations;
        }

        /// <summary>
        /// Get language statistics
        /// </summary>
        public static void GetLanguageStatistics(
            MultiLanguageResult multiLangResult,
            out int totalLanguages,
            out int totalUniqueKeys,
            out float averageCompleteness)
        {
            totalLanguages = multiLangResult.AvailableLanguages.Length;
            totalUniqueKeys = 0;
            averageCompleteness = 0f;

            if (totalLanguages == 0)
                return;

            // Count unique keys across all languages
            var allKeys = new NativeHashSet<uint>(1000, Allocator.Temp);

            foreach (var lang in multiLangResult.AvailableLanguages)
            {
                if (multiLangResult.LanguageData.TryGetValue(lang, out var langResult))
                {
                    var keys = langResult.LocalizationEntries.GetKeyArray(Allocator.Temp);
                    foreach (var key in keys)
                    {
                        allKeys.Add(key);
                    }
                    keys.Dispose();
                }
            }

            totalUniqueKeys = allKeys.Count;

            // Calculate average completeness
            float totalCompleteness = 0f;
            foreach (var lang in multiLangResult.AvailableLanguages)
            {
                if (multiLangResult.LanguageData.TryGetValue(lang, out var langResult))
                {
                    float completeness = totalUniqueKeys > 0 ?
                        (float)langResult.EntriesFound / totalUniqueKeys : 0f;
                    totalCompleteness += completeness;
                }
            }

            averageCompleteness = totalLanguages > 0 ? totalCompleteness / totalLanguages : 0f;

            allKeys.Dispose();
        }

        /// <summary>
        /// Find missing translations for a specific language
        /// </summary>
        public static NativeList<uint> FindMissingTranslations(
            MultiLanguageResult multiLangResult,
            FixedString64Bytes targetLanguage,
            FixedString64Bytes referenceLanguage,
            Allocator allocator)
        {
            var missingKeys = new NativeList<uint>(100, allocator);

            if (!multiLangResult.LanguageData.TryGetValue(referenceLanguage, out var refResult) ||
                !multiLangResult.LanguageData.TryGetValue(targetLanguage, out var targetResult))
            {
                return missingKeys;
            }

            var refKeys = refResult.LocalizationEntries.GetKeyArray(Allocator.Temp);

            foreach (var key in refKeys)
            {
                if (!targetResult.LocalizationEntries.ContainsKey(key))
                {
                    missingKeys.Add(key);
                }
            }

            refKeys.Dispose();
            return missingKeys;
        }
    }
}
