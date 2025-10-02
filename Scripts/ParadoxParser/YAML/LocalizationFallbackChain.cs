using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ParadoxParser.Data;

namespace ParadoxParser.YAML
{
    /// <summary>
    /// Advanced fallback chain system for localization
    /// Supports complex fallback hierarchies and regional variants
    /// </summary>
    public static class LocalizationFallbackChain
    {
        /// <summary>
        /// Fallback chain configuration
        /// </summary>
        public struct FallbackChain
        {
            public NativeList<FixedString64Bytes> Languages;
            public NativeHashMap<FixedString64Bytes, FixedString64Bytes> RegionalFallbacks;
            public FixedString64Bytes PrimaryLanguage;
            public bool UseRegionalFallbacks;

            public void Dispose()
            {
                if (Languages.IsCreated)
                    Languages.Dispose();
                if (RegionalFallbacks.IsCreated)
                    RegionalFallbacks.Dispose();
            }
        }

        /// <summary>
        /// Result of fallback chain resolution
        /// </summary>
        public struct FallbackResult
        {
            public FixedString512Bytes Value;
            public FixedString64Bytes ResolvedLanguage;
            public int FallbackLevel; // 0 = primary, 1+ = fallback levels
            public bool Success;
        }

        /// <summary>
        /// Create a fallback chain with common language hierarchies
        /// </summary>
        public static FallbackChain CreateStandardFallbackChain(
            FixedString64Bytes primaryLanguage,
            Allocator allocator)
        {
            var chain = new FallbackChain
            {
                Languages = new NativeList<FixedString64Bytes>(8, allocator),
                RegionalFallbacks = new NativeHashMap<FixedString64Bytes, FixedString64Bytes>(16, allocator),
                PrimaryLanguage = primaryLanguage,
                UseRegionalFallbacks = true
            };

            // Add primary language first
            chain.Languages.Add(primaryLanguage);

            // Add standard fallback hierarchy based on primary language
            AddStandardFallbacks(ref chain, primaryLanguage);

            // Setup regional fallbacks
            SetupRegionalFallbacks(ref chain);

            return chain;
        }

        /// <summary>
        /// Create a custom fallback chain
        /// </summary>
        public static FallbackChain CreateCustomFallbackChain(
            NativeArray<FixedString64Bytes> languageOrder,
            Allocator allocator)
        {
            var chain = new FallbackChain
            {
                Languages = new NativeList<FixedString64Bytes>(languageOrder.Length, allocator),
                RegionalFallbacks = new NativeHashMap<FixedString64Bytes, FixedString64Bytes>(16, allocator),
                PrimaryLanguage = languageOrder.Length > 0 ? languageOrder[0] : default,
                UseRegionalFallbacks = true
            };

            foreach (var lang in languageOrder)
            {
                chain.Languages.Add(lang);
            }

            SetupRegionalFallbacks(ref chain);
            return chain;
        }

        /// <summary>
        /// Resolve localization with fallback chain
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FallbackResult ResolveWithFallback(
            MultiLanguageExtractor.MultiLanguageResult multiLangResult,
            FallbackChain fallbackChain,
            uint keyHash)
        {
            var result = new FallbackResult { Success = false };

            for (int i = 0; i < fallbackChain.Languages.Length; i++)
            {
                var language = fallbackChain.Languages[i];

                // Try direct language match
                if (TryGetFromLanguage(multiLangResult, language, keyHash, out result.Value))
                {
                    result.ResolvedLanguage = language;
                    result.FallbackLevel = i;
                    result.Success = true;
                    return result;
                }

                // Try regional fallback if enabled
                if (fallbackChain.UseRegionalFallbacks &&
                    fallbackChain.RegionalFallbacks.TryGetValue(language, out var regionalFallback))
                {
                    if (TryGetFromLanguage(multiLangResult, regionalFallback, keyHash, out result.Value))
                    {
                        result.ResolvedLanguage = regionalFallback;
                        result.FallbackLevel = i;
                        result.Success = true;
                        return result;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Resolve localization with fallback chain (string key overload)
        /// </summary>
        public static FallbackResult ResolveWithFallback(
            MultiLanguageExtractor.MultiLanguageResult multiLangResult,
            FallbackChain fallbackChain,
            FixedString64Bytes key)
        {
            // Convert key to hash using same method as parser
            var keyBytes = new NativeArray<byte>(key.Length, Allocator.Temp);
            for (int i = 0; i < key.Length; i++)
            {
                keyBytes[i] = (byte)key[i];
            }

            uint keyHash = ParadoxParser.Utilities.FastHasher.HashFNV1a32(keyBytes);
            keyBytes.Dispose();

            return ResolveWithFallback(multiLangResult, fallbackChain, keyHash);
        }

        /// <summary>
        /// Get fallback statistics
        /// </summary>
        public static void GetFallbackStatistics(
            MultiLanguageExtractor.MultiLanguageResult multiLangResult,
            FallbackChain fallbackChain,
            NativeArray<uint> testKeys,
            out NativeArray<int> fallbackLevelCounts,
            out float primaryLanguageHitRate,
            Allocator allocator)
        {
            fallbackLevelCounts = new NativeArray<int>(fallbackChain.Languages.Length + 1, allocator);
            int primaryHits = 0;
            int totalResolved = 0;

            foreach (var key in testKeys)
            {
                var result = ResolveWithFallback(multiLangResult, fallbackChain, key);
                if (result.Success)
                {
                    totalResolved++;
                    if (result.FallbackLevel == 0)
                        primaryHits++;

                    if (result.FallbackLevel < fallbackLevelCounts.Length)
                        fallbackLevelCounts[result.FallbackLevel]++;
                }
                else
                {
                    // Count unresolved
                    fallbackLevelCounts[fallbackLevelCounts.Length - 1]++;
                }
            }

            primaryLanguageHitRate = testKeys.Length > 0 ? (float)primaryHits / testKeys.Length : 0f;
        }

        /// <summary>
        /// Add standard fallback languages based on primary language
        /// </summary>
        private static void AddStandardFallbacks(ref FallbackChain chain, FixedString64Bytes primaryLanguage)
        {
            // English as universal fallback (if not already primary)
            var english = new FixedString64Bytes("l_english");
            if (!primaryLanguage.Equals(english))
            {
                chain.Languages.Add(english);
            }

            // Add language family fallbacks
            if (primaryLanguage.ToString().Contains("german"))
            {
                AddIfNotExists(ref chain, "l_german");
                AddIfNotExists(ref chain, "l_english");
            }
            else if (primaryLanguage.ToString().Contains("french"))
            {
                AddIfNotExists(ref chain, "l_french");
                AddIfNotExists(ref chain, "l_english");
            }
            else if (primaryLanguage.ToString().Contains("spanish"))
            {
                AddIfNotExists(ref chain, "l_spanish");
                AddIfNotExists(ref chain, "l_english");
            }
            else if (primaryLanguage.ToString().Contains("russian"))
            {
                AddIfNotExists(ref chain, "l_russian");
                AddIfNotExists(ref chain, "l_english");
            }
            else if (primaryLanguage.ToString().Contains("chinese"))
            {
                AddIfNotExists(ref chain, "l_simp_chinese");
                AddIfNotExists(ref chain, "l_english");
            }

            // Developer fallback (often has debug strings)
            AddIfNotExists(ref chain, "l_developer");
        }

        /// <summary>
        /// Setup regional fallbacks (e.g., en_US -> en_GB -> en)
        /// </summary>
        private static void SetupRegionalFallbacks(ref FallbackChain chain)
        {
            // English variants
            chain.RegionalFallbacks.TryAdd(new FixedString64Bytes("l_english_us"), new FixedString64Bytes("l_english"));
            chain.RegionalFallbacks.TryAdd(new FixedString64Bytes("l_english_gb"), new FixedString64Bytes("l_english"));

            // German variants
            chain.RegionalFallbacks.TryAdd(new FixedString64Bytes("l_german_de"), new FixedString64Bytes("l_german"));
            chain.RegionalFallbacks.TryAdd(new FixedString64Bytes("l_german_at"), new FixedString64Bytes("l_german"));

            // French variants
            chain.RegionalFallbacks.TryAdd(new FixedString64Bytes("l_french_fr"), new FixedString64Bytes("l_french"));
            chain.RegionalFallbacks.TryAdd(new FixedString64Bytes("l_french_ca"), new FixedString64Bytes("l_french"));

            // Spanish variants
            chain.RegionalFallbacks.TryAdd(new FixedString64Bytes("l_spanish_es"), new FixedString64Bytes("l_spanish"));
            chain.RegionalFallbacks.TryAdd(new FixedString64Bytes("l_spanish_mx"), new FixedString64Bytes("l_spanish"));

            // Chinese variants
            chain.RegionalFallbacks.TryAdd(new FixedString64Bytes("l_trad_chinese"), new FixedString64Bytes("l_simp_chinese"));
        }

        /// <summary>
        /// Helper to add language if not already in chain
        /// </summary>
        private static void AddIfNotExists(ref FallbackChain chain, string language)
        {
            var langKey = new FixedString64Bytes(language);
            for (int i = 0; i < chain.Languages.Length; i++)
            {
                if (chain.Languages[i].Equals(langKey))
                    return;
            }
            chain.Languages.Add(langKey);
        }

        /// <summary>
        /// Try to get localization from specific language
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetFromLanguage(
            MultiLanguageExtractor.MultiLanguageResult multiLangResult,
            FixedString64Bytes language,
            uint keyHash,
            out FixedString512Bytes value)
        {
            value = default;

            if (!multiLangResult.LanguageData.TryGetValue(language, out var langResult))
                return false;

            return YAMLParser.TryGetLocalizedString(langResult, keyHash, out value);
        }
    }
}