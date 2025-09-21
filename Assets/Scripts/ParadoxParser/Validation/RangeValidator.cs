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
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static NativeArray<RangeRule> GetRangeRulesForContext(SemanticValidator.ValidationContext context)
        {
            switch (context)
            {
                case SemanticValidator.ValidationContext.Country:
                    return CreateCountryRangeRules();
                case SemanticValidator.ValidationContext.Province:
                    return CreateProvinceRangeRules();
                case SemanticValidator.ValidationContext.Technology:
                    return CreateTechnologyRangeRules();
                default:
                    return new NativeArray<RangeRule>(0, Allocator.Temp);
            }
        }

        private static NativeArray<RangeRule> CreateCountryRangeRules()
        {
            var rules = new NativeArray<RangeRule>(10, Allocator.Temp);
            int index = 0;

            // Basic country stats typically range from -3 to +3 in EU4/CK3
            rules[index++] = RangeRule.Create(FastHasher.HashFNV1a32("stability"), -3.0f, 3.0f);
            rules[index++] = RangeRule.Create(FastHasher.HashFNV1a32("legitimacy"), 0.0f, 100.0f);
            rules[index++] = RangeRule.Create(FastHasher.HashFNV1a32("prestige"), -100.0f, 100.0f);

            // Treasury can be negative (debt) but has practical limits
            rules[index++] = RangeRule.Create(FastHasher.HashFNV1a32("treasury"), -999999.0f, 999999.0f);

            // Province ID should be positive integer
            rules[index++] = RangeRule.CreateInteger(FastHasher.HashFNV1a32("capital"), 1, 999999);

            // Administrative efficiency and other percentages
            rules[index++] = RangeRule.ZeroToOne(FastHasher.HashFNV1a32("administrative_efficiency"));
            rules[index++] = RangeRule.Percentage(FastHasher.HashFNV1a32("inflation"));
            rules[index++] = RangeRule.Percentage(FastHasher.HashFNV1a32("corruption"));

            // Monarch point generation (typically 1-6)
            rules[index++] = RangeRule.CreateInteger(FastHasher.HashFNV1a32("adm"), 1, 6);
            rules[index++] = RangeRule.CreateInteger(FastHasher.HashFNV1a32("dip"), 1, 6);

            return rules;
        }

        private static NativeArray<RangeRule> CreateProvinceRangeRules()
        {
            var rules = new NativeArray<RangeRule>(8, Allocator.Temp);
            int index = 0;

            // Province development values (typically 1-30+ in EU4)
            rules[index++] = RangeRule.Create(FastHasher.HashFNV1a32("tax"), 1.0f, 99.0f);
            rules[index++] = RangeRule.Create(FastHasher.HashFNV1a32("production"), 1.0f, 99.0f);
            rules[index++] = RangeRule.Create(FastHasher.HashFNV1a32("manpower"), 1.0f, 99.0f);

            // Development total (sum of tax, production, manpower)
            rules[index++] = RangeRule.Create(FastHasher.HashFNV1a32("development"), 3.0f, 297.0f);

            // Autonomy percentage
            rules[index++] = RangeRule.Percentage(FastHasher.HashFNV1a32("autonomy"));

            // Prosperity progress
            rules[index++] = RangeRule.ZeroToOne(FastHasher.HashFNV1a32("prosperity"));

            // Devastation percentage
            rules[index++] = RangeRule.Percentage(FastHasher.HashFNV1a32("devastation"));

            // Province ID should be positive
            rules[index++] = RangeRule.CreateInteger(FastHasher.HashFNV1a32("id"), 1, 999999);

            return rules;
        }

        private static NativeArray<RangeRule> CreateTechnologyRangeRules()
        {
            var rules = new NativeArray<RangeRule>(6, Allocator.Temp);
            int index = 0;

            // Technology levels (typically 0-32 in EU4)
            rules[index++] = RangeRule.CreateInteger(FastHasher.HashFNV1a32("military"), 0, 32);
            rules[index++] = RangeRule.CreateInteger(FastHasher.HashFNV1a32("diplomatic"), 0, 32);
            rules[index++] = RangeRule.CreateInteger(FastHasher.HashFNV1a32("administrative"), 0, 32);

            // Technology cost modifiers
            rules[index++] = RangeRule.Percentage(FastHasher.HashFNV1a32("tech_cost_modifier"));
            rules[index++] = RangeRule.Percentage(FastHasher.HashFNV1a32("idea_cost"));

            // Innovation value
            rules[index++] = RangeRule.ZeroToOne(FastHasher.HashFNV1a32("innovativeness"));

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