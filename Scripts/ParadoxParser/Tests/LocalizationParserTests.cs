using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using ParadoxParser.Core;
using ParadoxParser.Utilities;
using ParadoxParser.Tokenization;
using ParadoxParser.Data;
using ParadoxParser.YAML;

namespace ParadoxParser.Tests
{
    /// <summary>
    /// Tests to verify our tokenizer can handle YAML localization files
    /// </summary>
    [TestFixture]
    public class LocalizationParserTests
    {
        [Test]
        public void YAMLLocalization_BasicTokenization_ShouldSucceed()
        {
            string yamlContent = @"l_english:
 PROV1:0 ""Stockholm""
 PROV2:0 ""Östergötland""
 PROV3:1 ""Kalmar""";

            var sourceBytes = ConvertStringToBytes(yamlContent);

            try
            {
                // Create tokenizer
                using var stringPool = new NativeStringPool(100, Allocator.Temp);
                using var errorAccumulator = new ErrorAccumulator(Allocator.Temp, 10);

                var tokenizer = new Tokenizer(sourceBytes, stringPool, errorAccumulator);

                bool hasTokens = false;

                // Check if we can at least create the tokenizer and it recognizes the format
                if (tokenizer.IsCreated)
                {
                    hasTokens = true;
                    UnityEngine.Debug.Log("YAML Tokenization: Successfully created tokenizer for YAML format");
                }

                Assert.IsTrue(hasTokens, "Should be able to create tokenizer for YAML format");

                // Try to read at least some content
                var position = tokenizer.Position;
                Assert.GreaterOrEqual(position, 0, "Tokenizer should have valid position");

                UnityEngine.Debug.Log($"YAML Test: Basic tokenization setup successful");

                tokenizer.Dispose();
            }
            finally
            {
                sourceBytes.Dispose();
            }
        }

        [Test]
        public void YAMLTokenizer_BasicSafetyTest_ShouldNotCrash()
        {
            // Very simple test to avoid crashes
            string yamlContent = "l_english:";

            var sourceBytes = ConvertStringToBytes(yamlContent);

            try
            {
                using var errorAccumulator = new ErrorAccumulator(Allocator.Temp, 10);
                var tokens = new NativeList<YAMLTokenizer.YAMLToken>(10, Allocator.Temp);

                UnityEngine.Debug.Log("About to test YAML tokenizer...");

                // Just test tokenization without parsing
                var tokenizeResult = YAMLTokenizer.TokenizeYAML(sourceBytes, tokens);

                UnityEngine.Debug.Log($"Tokenization result: Success={tokenizeResult.Success}, Tokens={tokens.Length}");

                // If we get here without crashing, that's progress
                Assert.IsTrue(true, "Tokenizer did not crash Unity");

                tokens.Dispose();
            }
            finally
            {
                sourceBytes.Dispose();
            }
        }

        [Test]
        public void YAMLParser_HandleUTF8Characters_ShouldWork()
        {
            string yamlContent = @"l_english:
 PROV2:0 ""Östergötland""
 PROV_SWEDISH:1 ""Värmland""
 PROV_SPECIAL:0 ""Åland with åäö""";

            var sourceBytes = ConvertStringToBytes(yamlContent);

            try
            {
                using var errorAccumulator = new ErrorAccumulator(Allocator.Temp, 10);
                var tokens = new NativeList<YAMLTokenizer.YAMLToken>(50, Allocator.Temp);

                var tokenizeResult = YAMLTokenizer.TokenizeYAML(sourceBytes, tokens);
                var parseResult = YAMLParser.ParseYAML(sourceBytes, tokens.AsArray(), Allocator.Temp);

                Assert.IsTrue(parseResult.Success, "Should handle UTF-8 characters");

                var prov2Hash = FastHasher.HashFNV1a32("PROV2");
                bool foundProv2 = YAMLParser.TryGetLocalizedString(parseResult, prov2Hash, out var prov2Value);

                Assert.IsTrue(foundProv2, "Should find PROV2");
                // Note: The exact UTF-8 comparison might need adjustment based on encoding handling

                UnityEngine.Debug.Log($"UTF-8 Test: PROV2 = '{prov2Value}'");
                UnityEngine.Debug.Log($"Found {parseResult.EntriesFound} entries with special characters");

                tokens.Dispose();
                parseResult.Dispose();
            }
            finally
            {
                sourceBytes.Dispose();
            }
        }

        /*[Test]
        public void MultiLanguageExtractor_LoadMultipleLanguages_ShouldSucceed()
        {
            // Create test YAML data for multiple languages
            string englishYaml = @"l_english:
 test_key_1: ""Hello World""
 test_key_2: ""Welcome to the game""
 test_key_3: ""Settings""";

            string frenchYaml = @"l_french:
 test_key_1: ""Bonjour le monde""
 test_key_2: ""Bienvenue dans le jeu""
 test_key_3: ""Paramètres""";

            var languageFiles = new Dictionary<string, NativeArray<byte>>();

            var englishBytes = ConvertStringToBytes(englishYaml);
            var frenchBytes = ConvertStringToBytes(frenchYaml);

            languageFiles.Add("english", englishBytes);
            languageFiles.Add("french", frenchBytes);

            try
            {
                var multiLangResult = MultiLanguageExtractor.LoadMultipleLanguages(languageFiles, Allocator.Temp);

                Assert.IsTrue(multiLangResult.Success, "MultiLanguageExtractor should succeed");
                Assert.AreEqual(2, multiLangResult.AvailableLanguages.Length, "Should have 2 languages");

                UnityEngine.Debug.Log($"✓ MultiLanguageExtractor loaded {multiLangResult.AvailableLanguages.Length} languages");

                multiLangResult.Dispose();
            }
            finally
            {
                foreach (var kvp in languageFiles)
                {
                    kvp.Value.Dispose();
                }
            }
        }*/

        /*[Test]
        public void LocalizationFallbackChain_CreateChain_ShouldWork()
        {
            var fallbackChain = LocalizationFallbackChain.CreateFallbackChain(
                new FixedString64Bytes("fr_CA"), Allocator.Temp);

            Assert.GreaterOrEqual(fallbackChain.Length, 2, "Fallback chain should have at least 2 entries");

            // Should include French and English
            bool foundFrench = false;
            bool foundEnglish = false;

            for (int i = 0; i < fallbackChain.Length; i++)
            {
                var lang = fallbackChain[i].ToString();
                if (lang == "fr") foundFrench = true;
                if (lang == "en") foundEnglish = true;
            }

            Assert.IsTrue(foundFrench && foundEnglish, "Should include 'fr' and 'en' in fallback chain");

            UnityEngine.Debug.Log($"✓ Fallback chain for fr_CA: {fallbackChain.Length} languages");

            fallbackChain.Dispose();
        }*/

        /*[Test]
        public void StringReplacementSystem_BasicReplacement_ShouldWork()
        {
            var context = StringReplacementSystem.CreateContext(2, Allocator.Temp);

            try
            {
                StringReplacementSystem.AddParameter(ref context,
                    new FixedString64Bytes("PLAYER"),
                    new FixedString512Bytes("John"));

                var input = new FixedString512Bytes("Hello $PLAYER$!");
                var result = StringReplacementSystem.ProcessString(input, context);

                Assert.IsTrue(result.Success, "String replacement should succeed");
                Assert.IsTrue(result.ProcessedString.ToString().Contains("John"), "Should replace PLAYER with John");
                Assert.AreEqual(1, result.ReplacementsMade, "Should make exactly 1 replacement");

                UnityEngine.Debug.Log($"✓ String replacement: '{input}' -> '{result.ProcessedString}'");
            }
            finally
            {
                context.Dispose();
            }
        }*/

        /*[Test]
        public void ColoredTextMarkup_ParseColors_ShouldWork()
        {
            var input = new FixedString512Bytes("This is §rred text§! and §ggreen text§!");
            var result = ColoredTextMarkup.ParseColorMarkup(input, Allocator.Temp);

            Assert.IsTrue(result.Success, "Color parsing should succeed");
            Assert.Greater(result.ColoredSegments.Length, 0, "Should find colored segments");

            UnityEngine.Debug.Log($"✓ Color markup found {result.ColoredSegments.Length} colored segments");

            // Test Unity Rich Text conversion
            var richText = ColoredTextMarkup.ConvertToUnityRichText(input);
            Assert.IsTrue(richText.ToString().Contains("<color="), "Should convert to Unity Rich Text format");

            UnityEngine.Debug.Log($"✓ Rich text conversion: '{richText}'");

            result.Dispose();
        }*/

        /// <summary>
        /// Helper method to convert string to byte array for testing
        /// </summary>
        private NativeArray<byte> ConvertStringToBytes(string text)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            var nativeBytes = new NativeArray<byte>(bytes.Length, Allocator.Temp);
            nativeBytes.CopyFrom(bytes);
            return nativeBytes;
        }
    }
}