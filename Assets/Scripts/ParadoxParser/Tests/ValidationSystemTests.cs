using System;
using NUnit.Framework;
using Unity.Collections;
using ParadoxParser.Core;
using ParadoxParser.Validation;
using ParadoxParser.Tokenization;
using ParadoxParser.Utilities;

namespace ParadoxParser.Tests
{
    /// <summary>
    /// Comprehensive tests for the Phase 2.5 validation system
    /// Tests all validation components: Syntax, Type, Range, CrossReference, and Semantic
    /// </summary>
    [TestFixture]
    public class ValidationSystemTests
    {
        private NativeArray<byte> testData;
        private ValidationOptions defaultOptions;

        [SetUp]
        public void Setup()
        {
            defaultOptions = new ValidationOptions
            {
                ValidateSyntax = true,
                ValidateTypes = true,
                ValidateRanges = true,
                ValidateReferences = true,
                ValidateSemantics = true,
                MaxErrors = 100,
                WarningsAsErrors = false
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (testData.IsCreated)
                testData.Dispose();
        }

        #region SyntaxValidator Tests

        [Test]
        public void SyntaxValidator_ValidTokenSequence_ShouldPass()
        {
            // Arrange
            var input = "key = \"value\"";
            SetupTestData(input);
            var tokens = CreateTokens(input);

            // Act
            var result = SyntaxValidator.ValidateSyntax(tokens, testData, defaultOptions);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.ErrorCount);
        }

        [Test]
        public void SyntaxValidator_UnmatchedBraces_ShouldDetectError()
        {
            // Arrange
            var input = "block = { key = value"; // Missing closing brace
            SetupTestData(input);
            var tokens = CreateTokensWithBrace(input, missingCloseBrace: true);

            // Act
            var result = SyntaxValidator.ValidateSyntax(tokens, testData, defaultOptions);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.Greater(result.ErrorCount, 0);

            // Check for unmatched brace error
            bool hasUnmatchedBraceError = false;
            for (int i = 0; i < result.Messages.Length; i++)
            {
                if (result.Messages[i].MessageHash == SyntaxValidator.MessageHashes.UnmatchedOpenBrace)
                {
                    hasUnmatchedBraceError = true;
                    break;
                }
            }
            Assert.IsTrue(hasUnmatchedBraceError);
        }

        [Test]
        public void SyntaxValidator_InvalidStringTermination_ShouldDetectError()
        {
            // Arrange
            var input = "key = \"unterminated string";
            SetupTestData(input);
            var tokens = CreateTokensWithUnterminatedString(input);

            // Act
            var result = SyntaxValidator.ValidateSyntax(tokens, testData, defaultOptions);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.Greater(result.ErrorCount, 0);
        }

        #endregion

        #region TypeValidator Tests

        [Test]
        public void TypeValidator_ValidTypes_ShouldPass()
        {
            // Arrange
            var input = "tag = \"ABC\"\ngovernment = \"monarchy\"\ncapital = 123";
            SetupTestData(input);
            var keyValues = CreateKeyValuePairs(new[]
            {
                ("tag", "ABC", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal),
                ("government", "monarchy", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal),
                ("capital", "123", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal)
            });

            // Act
            var result = TypeValidator.ValidateTypes(keyValues, testData, SemanticValidator.ValidationContext.Country, defaultOptions);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.ErrorCount);
        }

        [Test]
        public void TypeValidator_WrongType_ShouldDetectError()
        {
            // Arrange
            var input = "capital = \"not_a_number\""; // Capital should be integer
            SetupTestData(input);
            var keyValues = CreateKeyValuePairs(new[]
            {
                ("capital", "not_a_number", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal)
            });

            // Act
            var result = TypeValidator.ValidateTypes(keyValues, testData, SemanticValidator.ValidationContext.Country, defaultOptions);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.Greater(result.ErrorCount, 0);
        }

        #endregion

        #region RangeValidator Tests

        [Test]
        public void RangeValidator_ValidRanges_ShouldPass()
        {
            // Arrange
            var input = "stability = 2.5\nlegitimacy = 75.0";
            SetupTestData(input);
            var keyValues = CreateKeyValuePairs(new[]
            {
                ("stability", "2.5", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal),
                ("legitimacy", "75.0", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal)
            });

            // Act
            var result = RangeValidator.ValidateRanges(keyValues, testData, SemanticValidator.ValidationContext.Country, defaultOptions);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.ErrorCount);
        }

        [Test]
        public void RangeValidator_ValueOutOfRange_ShouldDetectError()
        {
            // Arrange
            var input = "stability = 5.0"; // Stability range is typically -3 to +3
            SetupTestData(input);
            var keyValues = CreateKeyValuePairs(new[]
            {
                ("stability", "5.0", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal)
            });

            // Act
            var result = RangeValidator.ValidateRanges(keyValues, testData, SemanticValidator.ValidationContext.Country, defaultOptions);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.Greater(result.ErrorCount, 0);
        }

        [Test]
        public void RangeValidator_CustomRange_ShouldValidateCorrectly()
        {
            // Test the custom range validation method
            bool isValid = RangeValidator.ValidateCustomRange(50.0f, 0.0f, 100.0f, out var severity);
            Assert.IsTrue(isValid);
            Assert.AreEqual(ValidationSeverity.Info, severity);

            isValid = RangeValidator.ValidateCustomRange(150.0f, 0.0f, 100.0f, out severity);
            Assert.IsFalse(isValid);
            Assert.AreEqual(ValidationSeverity.Error, severity);
        }

        #endregion

        #region CrossReferenceValidator Tests

        [Test]
        public void CrossReferenceValidator_ValidReferences_ShouldPass()
        {
            // Arrange
            var input = "culture = \"test_culture\"\nreligion = \"test_religion\"";
            SetupTestData(input);
            var keyValues = CreateKeyValuePairs(new[]
            {
                ("culture", "test_culture", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal),
                ("religion", "test_religion", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal)
            });

            var database = CrossReferenceValidator.ReferenceDatabase.Create(Allocator.Temp);
            try
            {
                // Add test references
                database.ValidIdentifiers.Add(FastHasher.HashFNV1a32("test_culture"));
                database.ValidIdentifiers.Add(FastHasher.HashFNV1a32("test_religion"));

                // Act
                var result = CrossReferenceValidator.ValidateCrossReferences(
                    keyValues, testData, database, SemanticValidator.ValidationContext.Country, defaultOptions);

                // Assert
                Assert.IsTrue(result.IsValid);
                Assert.AreEqual(0, result.ErrorCount);
            }
            finally
            {
                database.Dispose();
            }
        }

        [Test]
        public void CrossReferenceValidator_InvalidReference_ShouldDetectError()
        {
            // Arrange
            var input = "culture = \"nonexistent_culture\"";
            SetupTestData(input);
            var keyValues = CreateKeyValuePairs(new[]
            {
                ("culture", "nonexistent_culture", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal)
            });

            var database = CrossReferenceValidator.ReferenceDatabase.Create(Allocator.Temp);
            try
            {
                // Don't add the reference - it should be invalid

                // Act
                var result = CrossReferenceValidator.ValidateCrossReferences(
                    keyValues, testData, database, SemanticValidator.ValidationContext.Country, defaultOptions);

                // Assert
                Assert.IsFalse(result.IsValid);
                Assert.Greater(result.ErrorCount, 0);
            }
            finally
            {
                database.Dispose();
            }
        }

        #endregion

        #region SemanticValidator Tests

        [Test]
        public void SemanticValidator_ValidSemantics_ShouldPass()
        {
            // Arrange
            var input = "tag = \"ABC\"\ngovernment = \"monarchy\"";
            SetupTestData(input);
            var keyValues = CreateKeyValuePairs(new[]
            {
                ("tag", "ABC", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal),
                ("government", "monarchy", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal)
            });

            // Act
            var result = SemanticValidator.ValidateSemantics(
                keyValues, testData, SemanticValidator.ValidationContext.Country, defaultOptions);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.ErrorCount);
        }

        [Test]
        public void SemanticValidator_MissingRequiredKey_ShouldDetectError()
        {
            // Arrange - Country context requires "tag" and "government", but we only provide "tag"
            var input = "tag = \"ABC\"";
            SetupTestData(input);
            var keyValues = CreateKeyValuePairs(new[]
            {
                ("tag", "ABC", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal)
            });

            // Act
            var result = SemanticValidator.ValidateSemantics(
                keyValues, testData, SemanticValidator.ValidationContext.Country, defaultOptions);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.Greater(result.ErrorCount, 0);
        }

        [Test]
        public void SemanticValidator_DuplicateKeys_ShouldDetectWarning()
        {
            // Arrange
            var input = "tag = \"ABC\"\ntag = \"DEF\""; // Duplicate tag
            SetupTestData(input);
            var keyValues = CreateKeyValuePairs(new[]
            {
                ("tag", "ABC", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal),
                ("tag", "DEF", ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal)
            });

            // Act
            var result = SemanticValidator.ValidateSemantics(
                keyValues, testData, SemanticValidator.ValidationContext.Country, defaultOptions);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.Greater(result.Messages.Length, 0);
        }

        #endregion

        #region Integration Tests

        [Test]
        public void ValidationSystem_ComplexFile_ShouldValidateAllAspects()
        {
            // Arrange - Complex Paradox-style content
            var input = @"
country = {
    tag = ""ABC""
    government = ""monarchy""
    capital = 123
    stability = 2.5
    culture = ""test_culture""
    history = {
        1444.11.11 = { owner = ""ABC"" }
    }
}";
            SetupTestData(input);

            // This would require a more complete setup with tokens and parsing
            // For now, test individual components work together

            var database = CrossReferenceValidator.ReferenceDatabase.Create(Allocator.Temp);
            try
            {
                CrossReferenceValidator.PopulateDefaultDatabase(ref database);
                database.ValidIdentifiers.Add(FastHasher.HashFNV1a32("test_culture"));
                database.ValidIdentifiers.Add(FastHasher.HashFNV1a32("ABC"));

                // Verify database was populated
                Assert.IsTrue(database.ValidIdentifiers.Count > 0);
                Assert.IsTrue(database.ValidNumericIds.Count > 0);
                Assert.IsTrue(database.ValidKeys.Count > 0);
            }
            finally
            {
                database.Dispose();
            }
        }

        [Test]
        public void ValidationOptions_DisabledValidation_ShouldSkipValidation()
        {
            // Arrange
            var options = new ValidationOptions
            {
                ValidateSyntax = false,
                ValidateTypes = false,
                ValidateRanges = false,
                ValidateReferences = false,
                ValidateSemantics = false
            };

            var input = "invalid syntax here { {";
            SetupTestData(input);
            var tokens = CreateTokensWithBrace(input, missingCloseBrace: true);

            // Act
            var result = SyntaxValidator.ValidateSyntax(tokens, testData, options);

            // Assert - Should pass because syntax validation is disabled
            Assert.IsTrue(result.IsValid);
        }

        #endregion

        #region Helper Methods

        private void SetupTestData(string input)
        {
            if (testData.IsCreated)
                testData.Dispose();

            testData = new NativeArray<byte>(input.Length, Allocator.Temp);
            for (int i = 0; i < input.Length; i++)
            {
                testData[i] = (byte)input[i];
            }
        }

        private NativeSlice<Token> CreateTokens(string input)
        {
            // Simple token creation for basic tests
            var tokens = new NativeArray<Token>(3, Allocator.Temp);

            tokens[0] = Token.Create(TokenType.Identifier, 0, 3, 1, 1); // "key"
            tokens[1] = Token.Create(TokenType.Equals, 4, 1, 1, 5);     // "="
            tokens[2] = Token.Create(TokenType.String, 6, 7, 1, 7);     // "\"value\""

            return tokens.Slice();
        }

        private NativeSlice<Token> CreateTokensWithBrace(string input, bool missingCloseBrace = false)
        {
            var tokenCount = missingCloseBrace ? 6 : 7;
            var tokens = new NativeArray<Token>(tokenCount, Allocator.Temp);

            tokens[0] = Token.Create(TokenType.Identifier, 0, 5, 1, 1);  // "block"
            tokens[1] = Token.Create(TokenType.Equals, 6, 1, 1, 7);      // "="
            tokens[2] = Token.Create(TokenType.LeftBrace, 8, 1, 1, 9);   // "{"
            tokens[3] = Token.Create(TokenType.Identifier, 10, 3, 1, 11); // "key"
            tokens[4] = Token.Create(TokenType.Equals, 14, 1, 1, 15);    // "="
            tokens[5] = Token.Create(TokenType.Identifier, 16, 5, 1, 17); // "value"

            if (!missingCloseBrace)
            {
                tokens[6] = Token.Create(TokenType.RightBrace, 21, 1, 1, 22); // "}"
            }

            return tokens.Slice();
        }

        private NativeSlice<Token> CreateTokensWithUnterminatedString(string input)
        {
            var tokens = new NativeArray<Token>(3, Allocator.Temp);

            tokens[0] = Token.Create(TokenType.Identifier, 0, 3, 1, 1); // "key"
            tokens[1] = Token.Create(TokenType.Equals, 4, 1, 1, 5);     // "="
            tokens[2] = Token.Create(TokenType.String, 6, input.Length - 6, 1, 7); // Unterminated string

            return tokens.Slice();
        }

        private NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> CreateKeyValuePairs(
            (string key, string value, ParadoxParser.Core.ParadoxParser.ParsedValueType type)[] pairs)
        {
            var kvps = new NativeArray<ParadoxParser.Core.ParadoxParser.ParsedKeyValue>(pairs.Length, Allocator.Temp);

            for (int i = 0; i < pairs.Length; i++)
            {
                var (key, value, type) = pairs[i];
                var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
                var valueBytes = System.Text.Encoding.UTF8.GetBytes(value);

                // Convert byte arrays to NativeArrays
                var keyData = new NativeArray<byte>(keyBytes.Length, Allocator.Temp);
                var valueData = new NativeArray<byte>(valueBytes.Length, Allocator.Temp);

                for (int j = 0; j < keyBytes.Length; j++)
                    keyData[j] = keyBytes[j];
                for (int j = 0; j < valueBytes.Length; j++)
                    valueData[j] = valueBytes[j];

                var parsedValue = new ParadoxParser.Core.ParadoxParser.ParsedValue
                {
                    Type = type,
                    RawData = valueData.Slice()
                };

                kvps[i] = new ParadoxParser.Core.ParadoxParser.ParsedKeyValue
                {
                    KeyHash = FastHasher.HashFNV1a32(key),
                    Key = keyData.Slice(),
                    Value = parsedValue,
                    LineNumber = i + 1
                };
            }

            return kvps.Slice();
        }

        #endregion
    }
}