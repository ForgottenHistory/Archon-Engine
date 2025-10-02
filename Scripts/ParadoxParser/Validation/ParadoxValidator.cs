using System;
using Unity.Collections;
using Unity.Jobs;
using ParadoxParser.Core;
using ParadoxParser.Tokenization;

namespace ParadoxParser.Validation
{
    /// <summary>
    /// Main validation coordinator for Paradox parser
    /// Orchestrates all validation types and provides unified validation interface
    /// </summary>
    public static class ParadoxValidator
    {
        /// <summary>
        /// Complete validation result combining all validation types
        /// </summary>
        public struct CompleteValidationResult
        {
            public bool IsValid;
            public int TotalErrors;
            public int TotalWarnings;
            public ValidationResult SyntaxResult;
            public ValidationResult SemanticResult;
            public ValidationResult TypeResult;
            public ValidationResult RangeResult;
            public ValidationResult ReferenceResult;

            public void Dispose()
            {
                SyntaxResult.Dispose();
                SemanticResult.Dispose();
                TypeResult.Dispose();
                RangeResult.Dispose();
                ReferenceResult.Dispose();
            }
        }

        /// <summary>
        /// Validate tokens (syntax validation only)
        /// </summary>
        public static ValidationResult ValidateTokens(
            NativeSlice<Token> tokens,
            NativeSlice<byte> sourceData,
            ValidationOptions options = default)
        {
            if (options.Equals(default(ValidationOptions)))
                options = ValidationOptions.Default;

            return SyntaxValidator.ValidateSyntax(tokens, sourceData, options);
        }

        /// <summary>
        /// Validate parsed key-value pairs (semantic validation)
        /// </summary>
        public static ValidationResult ValidateParsedData(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            SemanticValidator.ValidationContext context = SemanticValidator.ValidationContext.Root,
            ValidationOptions options = default)
        {
            if (options.Equals(default(ValidationOptions)))
                options = ValidationOptions.Default;

            return SemanticValidator.ValidateSemantics(keyValues, sourceData, context, options);
        }

        /// <summary>
        /// Validate data types
        /// </summary>
        public static ValidationResult ValidateTypes(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            SemanticValidator.ValidationContext context = SemanticValidator.ValidationContext.Root,
            ValidationOptions options = default)
        {
            if (options.Equals(default(ValidationOptions)))
                options = ValidationOptions.Default;

            return TypeValidator.ValidateTypes(keyValues, sourceData, context, options);
        }

        /// <summary>
        /// Validate numeric ranges
        /// </summary>
        public static ValidationResult ValidateRanges(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            SemanticValidator.ValidationContext context = SemanticValidator.ValidationContext.Root,
            ValidationOptions options = default)
        {
            if (options.Equals(default(ValidationOptions)))
                options = ValidationOptions.Default;

            return RangeValidator.ValidateRanges(keyValues, sourceData, context, options);
        }

        /// <summary>
        /// Validate cross-references
        /// </summary>
        public static ValidationResult ValidateReferences(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            CrossReferenceValidator.ReferenceDatabase database,
            SemanticValidator.ValidationContext context = SemanticValidator.ValidationContext.Root,
            ValidationOptions options = default)
        {
            if (options.Equals(default(ValidationOptions)))
                options = ValidationOptions.Default;

            return CrossReferenceValidator.ValidateCrossReferences(keyValues, sourceData, database, context, options);
        }

        /// <summary>
        /// Perform complete validation (all types)
        /// </summary>
        public static CompleteValidationResult ValidateComplete(
            NativeSlice<Token> tokens,
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            CrossReferenceValidator.ReferenceDatabase database,
            SemanticValidator.ValidationContext context = SemanticValidator.ValidationContext.Root,
            ValidationOptions options = default)
        {
            if (options.Equals(default(ValidationOptions)))
                options = ValidationOptions.Default;

            var result = new CompleteValidationResult();

            try
            {
                // 1. Syntax validation
                if (options.ValidateSyntax)
                {
                    result.SyntaxResult = SyntaxValidator.ValidateSyntax(tokens, sourceData, options);
                }
                else
                {
                    result.SyntaxResult = ValidationResult.Valid;
                }

                // 2. Semantic validation
                if (options.ValidateSemantics)
                {
                    result.SemanticResult = SemanticValidator.ValidateSemantics(keyValues, sourceData, context, options);
                }
                else
                {
                    result.SemanticResult = ValidationResult.Valid;
                }

                // 3. Type validation
                if (options.ValidateTypes)
                {
                    result.TypeResult = TypeValidator.ValidateTypes(keyValues, sourceData, context, options);
                }
                else
                {
                    result.TypeResult = ValidationResult.Valid;
                }

                // 4. Range validation
                if (options.ValidateRanges)
                {
                    result.RangeResult = RangeValidator.ValidateRanges(keyValues, sourceData, context, options);
                }
                else
                {
                    result.RangeResult = ValidationResult.Valid;
                }

                // 5. Reference validation
                if (options.ValidateReferences)
                {
                    result.ReferenceResult = CrossReferenceValidator.ValidateCrossReferences(
                        keyValues, sourceData, database, context, options);
                }
                else
                {
                    result.ReferenceResult = ValidationResult.Valid;
                }

                // Combine results
                result.TotalErrors = result.SyntaxResult.ErrorCount +
                                   result.SemanticResult.ErrorCount +
                                   result.TypeResult.ErrorCount +
                                   result.RangeResult.ErrorCount +
                                   result.ReferenceResult.ErrorCount;

                result.TotalWarnings = result.SyntaxResult.WarningCount +
                                      result.SemanticResult.WarningCount +
                                      result.TypeResult.WarningCount +
                                      result.RangeResult.WarningCount +
                                      result.ReferenceResult.WarningCount;

                result.IsValid = result.TotalErrors == 0 && (!options.WarningsAsErrors || result.TotalWarnings == 0);

                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Create a job for parallel validation
        /// </summary>
        public static ValidationJob CreateValidationJob(
            NativeSlice<Token> tokens,
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            SemanticValidator.ValidationContext context = SemanticValidator.ValidationContext.Root,
            ValidationOptions options = default)
        {
            if (options.Equals(default(ValidationOptions)))
                options = ValidationOptions.Default;

            return new ValidationJob
            {
                Tokens = tokens,
                KeyValues = keyValues,
                SourceData = sourceData,
                Context = context,
                Options = options
            };
        }

        /// <summary>
        /// Get validation message description by hash
        /// </summary>
        public static string GetMessageDescription(uint messageHash)
        {
            // Syntax messages
            if (messageHash == SyntaxValidator.MessageHashes.UnmatchedOpenBrace)
                return "Unmatched opening brace '{' - missing closing brace";
            if (messageHash == SyntaxValidator.MessageHashes.UnmatchedCloseBrace)
                return "Unmatched closing brace '}' - no corresponding opening brace";
            if (messageHash == SyntaxValidator.MessageHashes.ExpectedEquals)
                return "Expected '=' after key name";
            if (messageHash == SyntaxValidator.MessageHashes.ExpectedValue)
                return "Expected value after '='";
            if (messageHash == SyntaxValidator.MessageHashes.UnexpectedToken)
                return "Unexpected token in this context";
            if (messageHash == SyntaxValidator.MessageHashes.EmptyBlock)
                return "Empty block - consider removing or adding content";
            if (messageHash == SyntaxValidator.MessageHashes.InvalidIdentifier)
                return "Invalid identifier format";
            if (messageHash == SyntaxValidator.MessageHashes.UnterminatedString)
                return "Unterminated string literal";

            // Semantic messages
            if (messageHash == SemanticValidator.MessageHashes.UnknownKey)
                return "Unknown key in this context";
            if (messageHash == SemanticValidator.MessageHashes.DuplicateKey)
                return "Duplicate key found";
            if (messageHash == SemanticValidator.MessageHashes.RequiredKeyMissing)
                return "Required key is missing";
            if (messageHash == SemanticValidator.MessageHashes.DeprecatedKey)
                return "This key is deprecated";
            if (messageHash == SemanticValidator.MessageHashes.ConflictingKeys)
                return "Conflicting keys found";

            // Type messages
            if (messageHash == TypeValidator.MessageHashes.WrongType)
                return "Value type does not match expected type";
            if (messageHash == TypeValidator.MessageHashes.InvalidNumber)
                return "Invalid number format";
            if (messageHash == TypeValidator.MessageHashes.InvalidDate)
                return "Invalid date format (expected YYYY.MM.DD)";
            if (messageHash == TypeValidator.MessageHashes.InvalidBoolean)
                return "Invalid boolean value (expected yes/no or true/false)";
            if (messageHash == TypeValidator.MessageHashes.InvalidColor)
                return "Invalid color format";

            // Range messages
            if (messageHash == RangeValidator.MessageHashes.ValueTooLow)
                return "Value is below minimum allowed range";
            if (messageHash == RangeValidator.MessageHashes.ValueTooHigh)
                return "Value is above maximum allowed range";
            if (messageHash == RangeValidator.MessageHashes.InvalidRange)
                return "Value is outside valid range";

            // Reference messages
            if (messageHash == CrossReferenceValidator.MessageHashes.InvalidReference)
                return "Invalid reference";
            if (messageHash == CrossReferenceValidator.MessageHashes.MissingProvince)
                return "Referenced province does not exist";
            if (messageHash == CrossReferenceValidator.MessageHashes.MissingCountry)
                return "Referenced country does not exist";
            if (messageHash == CrossReferenceValidator.MessageHashes.CircularReference)
                return "Circular reference detected";

            return "Unknown validation error";
        }
    }

    /// <summary>
    /// Job for parallel validation processing
    /// </summary>
    public struct ValidationJob : IJob
    {
        [ReadOnly] public NativeSlice<Token> Tokens;
        [ReadOnly] public NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> KeyValues;
        [ReadOnly] public NativeSlice<byte> SourceData;
        [ReadOnly] public SemanticValidator.ValidationContext Context;
        [ReadOnly] public ValidationOptions Options;

        public NativeReference<bool> IsValid;
        public NativeReference<int> ErrorCount;
        public NativeReference<int> WarningCount;

        public void Execute()
        {
            int totalErrors = 0;
            int totalWarnings = 0;

            // Syntax validation
            if (Options.ValidateSyntax)
            {
                var syntaxResult = SyntaxValidator.ValidateSyntax(Tokens, SourceData, Options);
                totalErrors += syntaxResult.ErrorCount;
                totalWarnings += syntaxResult.WarningCount;
                syntaxResult.Dispose();
            }

            // Semantic validation
            if (Options.ValidateSemantics)
            {
                var semanticResult = SemanticValidator.ValidateSemantics(KeyValues, SourceData, Context, Options);
                totalErrors += semanticResult.ErrorCount;
                totalWarnings += semanticResult.WarningCount;
                semanticResult.Dispose();
            }

            // Type validation
            if (Options.ValidateTypes)
            {
                var typeResult = TypeValidator.ValidateTypes(KeyValues, SourceData, Context, Options);
                totalErrors += typeResult.ErrorCount;
                totalWarnings += typeResult.WarningCount;
                typeResult.Dispose();
            }

            // Range validation
            if (Options.ValidateRanges)
            {
                var rangeResult = RangeValidator.ValidateRanges(KeyValues, SourceData, Context, Options);
                totalErrors += rangeResult.ErrorCount;
                totalWarnings += rangeResult.WarningCount;
                rangeResult.Dispose();
            }

            // Set results
            ErrorCount.Value = totalErrors;
            WarningCount.Value = totalWarnings;
            IsValid.Value = totalErrors == 0 && (!Options.WarningsAsErrors || totalWarnings == 0);
        }
    }
}