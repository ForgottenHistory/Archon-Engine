using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Core;
using ParadoxParser.Utilities;

namespace ParadoxParser.Validation
{
    /// <summary>
    /// Range validation for numeric values in Paradox data
    /// Ensures values fall within expected ranges for game mechanics
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class RangeValidator
    {
        /// <summary>
        /// Range validation message hashes
        /// </summary>
        public static class MessageHashes
        {
            public static readonly uint ValueTooLow = FastHasher.HashFNV1a32("VALUE_TOO_LOW");
            public static readonly uint ValueTooHigh = FastHasher.HashFNV1a32("VALUE_TOO_HIGH");
            public static readonly uint InvalidRange = FastHasher.HashFNV1a32("INVALID_RANGE");
            public static readonly uint OutOfBounds = FastHasher.HashFNV1a32("OUT_OF_BOUNDS");
        }

        /// <summary>
        /// Range constraint for a specific key
        /// </summary>
        public struct RangeRule
        {
            public uint KeyHash;
            public float MinValue;
            public float MaxValue;
            public bool HasMin;
            public bool HasMax;
            public bool IntegerOnly;

            public static RangeRule Create(uint keyHash, float? min = null, float? max = null, bool integerOnly = false)
            {
                return new RangeRule
                {
                    KeyHash = keyHash,
                    MinValue = min ?? float.MinValue,
                    MaxValue = max ?? float.MaxValue,
                    HasMin = min.HasValue,
                    HasMax = max.HasValue,
                    IntegerOnly = integerOnly
                };
            }

            public static RangeRule CreateInteger(uint keyHash, int? min = null, int? max = null)
            {
                return new RangeRule
                {
                    KeyHash = keyHash,
                    MinValue = min ?? int.MinValue,
                    MaxValue = max ?? int.MaxValue,
                    HasMin = min.HasValue,
                    HasMax = max.HasValue,
                    IntegerOnly = true
                };
            }

            public static RangeRule Percentage(uint keyHash)
            {
                return Create(keyHash, -100.0f, 100.0f);
            }

            public static RangeRule ZeroToOne(uint keyHash)
            {
                return Create(keyHash, 0.0f, 1.0f);
            }

            public static RangeRule Positive(uint keyHash)
            {
                return Create(keyHash, min: 0.0f);
            }
        }

        /// <summary>
        /// Validate numeric ranges for parsed values
        /// </summary>
        public static ValidationResult ValidateRanges(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            SemanticValidator.ValidationContext context,
            ValidationOptions options)
        {
            var messages = new NativeList<ValidationMessage>(32, Allocator.Temp);
            var result = ValidationResult.Valid;
            result.Messages = messages;

            try
            {
                if (options.ValidateRanges)
                {
                    var rangeRules = GetRangeRulesForContext(context);

                    try
                    {
                        ValidateValueRanges(keyValues, sourceData, rangeRules, ref result, options);
                    }
                    finally
                    {
                        rangeRules.Dispose();
                    }
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
        /// Validate individual value ranges
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateValueRanges(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            NativeArray<RangeRule> rangeRules,
            ref ValidationResult result,
            ValidationOptions options)
        {
            for (int i = 0; i < keyValues.Length; i++)
            {
                var kvp = keyValues[i];

                // Find range rule for this key
                RangeRule? rule = null;
                for (int j = 0; j < rangeRules.Length; j++)
                {
                    if (rangeRules[j].KeyHash == kvp.KeyHash)
                    {
                        rule = rangeRules[j];
                        break;
                    }
                }

                if (rule.HasValue)
                {
                    ValidateValueRange(kvp, sourceData, rule.Value, ref result);
                }

                if (result.ErrorCount >= options.MaxErrors)
                    break;
            }
        }

        /// <summary>
        /// Validate a single value's range
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateValueRange(
            ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp,
            NativeSlice<byte> sourceData,
            RangeRule rule,
            ref ValidationResult result)
        {
            // Only validate literal values that could be numeric
            if (kvp.Value.Type != ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal)
                return;

            var parseResult = FastNumberParser.ParseFloat(kvp.Value.RawData);
            if (!parseResult.Success)
                return; // Type validation will catch this

            float value = parseResult.Value;

            // Check integer constraint
            if (rule.IntegerOnly && (value != (float)(int)value))
            {
                var message = ValidationMessage.CreateWithContext(
                    ValidationType.Range,
                    ValidationSeverity.Error,
                    kvp.LineNumber,
                    0,
                    0,
                    MessageHashes.InvalidRange,
                    kvp.Key);
                result.AddMessage(message);
                return;
            }

            // Check minimum value
            if (rule.HasMin && value < rule.MinValue)
            {
                var message = ValidationMessage.CreateWithContext(
                    ValidationType.Range,
                    ValidationSeverity.Error,
                    kvp.LineNumber,
                    0,
                    0,
                    MessageHashes.ValueTooLow,
                    kvp.Key);
                result.AddMessage(message);
            }

            // Check maximum value
            if (rule.HasMax && value > rule.MaxValue)
            {
                var message = ValidationMessage.CreateWithContext(
                    ValidationType.Range,
                    ValidationSeverity.Error,
                    kvp.LineNumber,
                    0,
                    0,
                    MessageHashes.ValueTooHigh,
                    kvp.Key);
                result.AddMessage(message);
            }
        }

        /// <summary>
        /// Get range rules for a validation context
        /// In a real implementation, these would be loaded from configuration files
        /// For now, returns empty rules to keep the system completely generic
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static NativeArray<RangeRule> GetRangeRulesForContext(SemanticValidator.ValidationContext context)
        {
            // TODO: Load rules from configuration files based on context
            // This keeps the validation system completely generic
            return new NativeArray<RangeRule>(0, Allocator.Temp);
        }

        /// <summary>
        /// Example method showing how to create range rules
        /// Applications should implement their own rule creation based on configuration
        /// </summary>
        public static NativeArray<RangeRule> CreateExampleRules(Allocator allocator)
        {
            var rules = new NativeArray<RangeRule>(5, allocator);
            int index = 0;

            // Generic examples - not tied to any specific game
            rules[index++] = RangeRule.Create(FastHasher.HashFNV1a32("numeric_value"), 0.0f, 100.0f);
            rules[index++] = RangeRule.CreateInteger(FastHasher.HashFNV1a32("id"), 1, 999999);
            rules[index++] = RangeRule.Percentage(FastHasher.HashFNV1a32("percentage"));
            rules[index++] = RangeRule.ZeroToOne(FastHasher.HashFNV1a32("ratio"));
            rules[index++] = RangeRule.Positive(FastHasher.HashFNV1a32("count"));

            return rules;
        }

        /// <summary>
        /// Validate a custom range rule
        /// </summary>
        public static bool ValidateCustomRange(float value, float min, float max, out ValidationSeverity severity)
        {
            severity = ValidationSeverity.Info;

            if (value < min)
            {
                severity = ValidationSeverity.Error;
                return false;
            }

            if (value > max)
            {
                severity = ValidationSeverity.Error;
                return false;
            }

            // Check for values that are technically valid but unusual
            float range = max - min;
            if (range > 0)
            {
                float normalized = (value - min) / range;
                if (normalized < 0.01f || normalized > 0.99f)
                {
                    severity = ValidationSeverity.Warning;
                }
            }

            return true;
        }

        /// <summary>
        /// Get suggested value range for a key hash
        /// </summary>
        public static (float min, float max, bool hasRange) GetSuggestedRange(uint keyHash, SemanticValidator.ValidationContext context)
        {
            var rules = GetRangeRulesForContext(context);

            try
            {
                for (int i = 0; i < rules.Length; i++)
                {
                    if (rules[i].KeyHash == keyHash)
                    {
                        return (rules[i].MinValue, rules[i].MaxValue, rules[i].HasMin || rules[i].HasMax);
                    }
                }

                return (0, 0, false);
            }
            finally
            {
                rules.Dispose();
            }
        }
    }
}