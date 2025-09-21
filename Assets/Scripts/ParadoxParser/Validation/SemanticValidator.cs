using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Core;
using ParadoxParser.Utilities;

namespace ParadoxParser.Validation
{
    /// <summary>
    /// Semantic validation for parsed Paradox data
    /// Validates logical consistency, known keys, context-specific rules
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class SemanticValidator
    {
        /// <summary>
        /// Semantic validation message hashes
        /// </summary>
        public static class MessageHashes
        {
            public static readonly uint UnknownKey = FastHasher.HashFNV1a32("UNKNOWN_KEY");
            public static readonly uint InvalidContext = FastHasher.HashFNV1a32("INVALID_CONTEXT");
            public static readonly uint DuplicateKey = FastHasher.HashFNV1a32("DUPLICATE_KEY");
            public static readonly uint RequiredKeyMissing = FastHasher.HashFNV1a32("REQUIRED_KEY_MISSING");
            public static readonly uint InvalidKeyValue = FastHasher.HashFNV1a32("INVALID_KEY_VALUE");
            public static readonly uint DeprecatedKey = FastHasher.HashFNV1a32("DEPRECATED_KEY");
            public static readonly uint ConflictingKeys = FastHasher.HashFNV1a32("CONFLICTING_KEYS");
        }

        /// <summary>
        /// Context types for semantic validation
        /// </summary>
        public enum ValidationContext : byte
        {
            Root = 0,
            Country = 1,
            Province = 2,
            Technology = 3,
            Modifier = 4,
            Event = 5,
            Decision = 6,
            Building = 7,
            TradeGood = 8,
            Culture = 9,
            Religion = 10
        }

        /// <summary>
        /// Validation rule for key-value pairs
        /// </summary>
        public struct ValidationRule
        {
            public uint KeyHash;
            public ValidationContext Context;
            public bool Required;
            public bool AllowDuplicates;
            public bool Deprecated;

            public static ValidationRule Create(uint keyHash, ValidationContext context,
                bool required = false, bool allowDuplicates = false, bool deprecated = false)
            {
                return new ValidationRule
                {
                    KeyHash = keyHash,
                    Context = context,
                    Required = required,
                    AllowDuplicates = allowDuplicates,
                    Deprecated = deprecated
                };
            }
        }

        /// <summary>
        /// Validate semantic correctness of parsed data
        /// </summary>
        public static ValidationResult ValidateSemantics(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            ValidationContext context,
            ValidationOptions options)
        {
            var messages = new NativeList<ValidationMessage>(32, Allocator.Temp);
            var result = ValidationResult.Valid;
            result.Messages = messages;

            try
            {
                if (options.ValidateSemantics)
                {
                    ValidateKnownKeys(keyValues, sourceData, context, ref result, options);
                    ValidateDuplicateKeys(keyValues, ref result, options);
                    ValidateRequiredKeys(keyValues, context, ref result, options);
                    ValidateKeyConflicts(keyValues, context, ref result, options);
                }

                return ValidationResult.Create(result.Messages);
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Validate that keys are known in the current context
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateKnownKeys(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            ValidationContext context,
            ref ValidationResult result,
            ValidationOptions options)
        {
            var knownKeys = GetKnownKeysForContext(context);

            for (int i = 0; i < keyValues.Length; i++)
            {
                var kvp = keyValues[i];
                bool isKnown = false;

                // Check if key is known in this context
                for (int j = 0; j < knownKeys.Length; j++)
                {
                    if (knownKeys[j].KeyHash == kvp.KeyHash &&
                        knownKeys[j].Context == context)
                    {
                        isKnown = true;

                        // Check if deprecated
                        if (knownKeys[j].Deprecated)
                        {
                            var deprecatedMessage = ValidationMessage.CreateWithContext(
                                ValidationType.Semantic,
                                ValidationSeverity.Warning,
                                kvp.LineNumber,
                                0, // Column not available
                                0, // ByteOffset not available
                                MessageHashes.DeprecatedKey,
                                kvp.Key);
                            result.AddMessage(deprecatedMessage);
                        }
                        break;
                    }
                }

                if (!isKnown)
                {
                    var message = ValidationMessage.CreateWithContext(
                        ValidationType.Semantic,
                        ValidationSeverity.Warning,
                        kvp.LineNumber,
                        0,
                        0,
                        MessageHashes.UnknownKey,
                        kvp.Key);
                    result.AddMessage(message);
                }

                if (result.ErrorCount >= options.MaxErrors)
                    break;
            }

            knownKeys.Dispose();
        }

        /// <summary>
        /// Validate for duplicate keys where not allowed
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateDuplicateKeys(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            ref ValidationResult result,
            ValidationOptions options)
        {
            var seenKeys = new NativeHashMap<uint, int>(keyValues.Length, Allocator.Temp);

            try
            {
                for (int i = 0; i < keyValues.Length; i++)
                {
                    var kvp = keyValues[i];

                    if (seenKeys.TryGetValue(kvp.KeyHash, out var firstOccurrence))
                    {
                        // Duplicate found
                        var message = ValidationMessage.CreateWithContext(
                            ValidationType.Semantic,
                            ValidationSeverity.Warning,
                            kvp.LineNumber,
                            0,
                            0,
                            MessageHashes.DuplicateKey,
                            kvp.Key);
                        result.AddMessage(message);
                    }
                    else
                    {
                        seenKeys.Add(kvp.KeyHash, i);
                    }

                    if (result.ErrorCount >= options.MaxErrors)
                        break;
                }
            }
            finally
            {
                seenKeys.Dispose();
            }
        }

        /// <summary>
        /// Validate that required keys are present
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateRequiredKeys(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            ValidationContext context,
            ref ValidationResult result,
            ValidationOptions options)
        {
            var requiredKeys = GetRequiredKeysForContext(context);
            var presentKeys = new NativeHashSet<uint>(keyValues.Length, Allocator.Temp);

            try
            {
                // Build set of present keys
                for (int i = 0; i < keyValues.Length; i++)
                {
                    presentKeys.Add(keyValues[i].KeyHash);
                }

                // Check for missing required keys
                for (int i = 0; i < requiredKeys.Length; i++)
                {
                    if (!presentKeys.Contains(requiredKeys[i]))
                    {
                        var message = ValidationMessage.Create(
                            ValidationType.Semantic,
                            ValidationSeverity.Error,
                            1, // Line unknown
                            1, // Column unknown
                            0, // Offset unknown
                            MessageHashes.RequiredKeyMissing);
                        result.AddMessage(message);
                    }
                }
            }
            finally
            {
                requiredKeys.Dispose();
                presentKeys.Dispose();
            }
        }

        /// <summary>
        /// Validate for conflicting keys
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateKeyConflicts(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            ValidationContext context,
            ref ValidationResult result,
            ValidationOptions options)
        {
            var conflicts = GetKeyConflictsForContext(context);

            try
            {
                for (int i = 0; i < conflicts.Length; i += 2)
                {
                    if (i + 1 >= conflicts.Length)
                        break;

                    var key1Hash = conflicts[i];
                    var key2Hash = conflicts[i + 1];

                    bool hasKey1 = false, hasKey2 = false;
                    int key1Line = 0, key2Line = 0;

                    for (int j = 0; j < keyValues.Length; j++)
                    {
                        if (keyValues[j].KeyHash == key1Hash)
                        {
                            hasKey1 = true;
                            key1Line = keyValues[j].LineNumber;
                        }
                        else if (keyValues[j].KeyHash == key2Hash)
                        {
                            hasKey2 = true;
                            key2Line = keyValues[j].LineNumber;
                        }
                    }

                    if (hasKey1 && hasKey2)
                    {
                        var message = ValidationMessage.Create(
                            ValidationType.Semantic,
                            ValidationSeverity.Error,
                            key2Line, // Report on second occurrence
                            0,
                            0,
                            MessageHashes.ConflictingKeys);
                        result.AddMessage(message);
                    }
                }
            }
            finally
            {
                conflicts.Dispose();
            }
        }

        /// <summary>
        /// Get known keys for a validation context
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static NativeArray<ValidationRule> GetKnownKeysForContext(ValidationContext context)
        {
            // This would normally be loaded from configuration
            // For now, return a basic set based on context
            switch (context)
            {
                case ValidationContext.Country:
                    return CreateCountryValidationRules();
                case ValidationContext.Province:
                    return CreateProvinceValidationRules();
                case ValidationContext.Technology:
                    return CreateTechnologyValidationRules();
                default:
                    return new NativeArray<ValidationRule>(0, Allocator.Temp);
            }
        }

        /// <summary>
        /// Get required keys for a validation context
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static NativeArray<uint> GetRequiredKeysForContext(ValidationContext context)
        {
            switch (context)
            {
                case ValidationContext.Country:
                    var countryRequired = new NativeArray<uint>(2, Allocator.Temp);
                    countryRequired[0] = FastHasher.HashFNV1a32("tag");
                    countryRequired[1] = FastHasher.HashFNV1a32("government");
                    return countryRequired;

                case ValidationContext.Province:
                    var provinceRequired = new NativeArray<uint>(1, Allocator.Temp);
                    provinceRequired[0] = FastHasher.HashFNV1a32("owner");
                    return provinceRequired;

                default:
                    return new NativeArray<uint>(0, Allocator.Temp);
            }
        }

        /// <summary>
        /// Get conflicting key pairs for a validation context
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static NativeArray<uint> GetKeyConflictsForContext(ValidationContext context)
        {
            switch (context)
            {
                case ValidationContext.Country:
                    var conflicts = new NativeArray<uint>(2, Allocator.Temp);
                    conflicts[0] = FastHasher.HashFNV1a32("monarchy");
                    conflicts[1] = FastHasher.HashFNV1a32("republic");
                    return conflicts;

                default:
                    return new NativeArray<uint>(0, Allocator.Temp);
            }
        }

        // Helper methods to create validation rules
        private static NativeArray<ValidationRule> CreateCountryValidationRules()
        {
            var rules = new NativeArray<ValidationRule>(10, Allocator.Temp);
            int index = 0;

            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("tag"), ValidationContext.Country, required: true);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("government"), ValidationContext.Country, required: true);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("technology_group"), ValidationContext.Country);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("capital"), ValidationContext.Country);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("primary_culture"), ValidationContext.Country);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("religion"), ValidationContext.Country);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("stability"), ValidationContext.Country);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("legitimacy"), ValidationContext.Country);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("prestige"), ValidationContext.Country);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("treasury"), ValidationContext.Country);

            return rules;
        }

        private static NativeArray<ValidationRule> CreateProvinceValidationRules()
        {
            var rules = new NativeArray<ValidationRule>(8, Allocator.Temp);
            int index = 0;

            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("owner"), ValidationContext.Province, required: true);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("controller"), ValidationContext.Province);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("culture"), ValidationContext.Province);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("religion"), ValidationContext.Province);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("trade_goods"), ValidationContext.Province);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("tax"), ValidationContext.Province);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("production"), ValidationContext.Province);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("manpower"), ValidationContext.Province);

            return rules;
        }

        private static NativeArray<ValidationRule> CreateTechnologyValidationRules()
        {
            var rules = new NativeArray<ValidationRule>(3, Allocator.Temp);
            int index = 0;

            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("military"), ValidationContext.Technology);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("diplomatic"), ValidationContext.Technology);
            rules[index++] = ValidationRule.Create(FastHasher.HashFNV1a32("administrative"), ValidationContext.Technology);

            return rules;
        }
    }
}