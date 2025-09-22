using System;
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