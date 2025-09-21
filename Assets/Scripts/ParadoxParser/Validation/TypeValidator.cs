using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Core;
using ParadoxParser.Utilities;

namespace ParadoxParser.Validation
{
    /// <summary>
    /// Type validation for Paradox data values
    /// Ensures values match expected types for their keys
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class TypeValidator
    {
        /// <summary>
        /// Expected data types for validation
        /// </summary>
        public enum ExpectedType : byte
        {
            Any = 0,
            String = 1,
            Integer = 2,
            Float = 3,
            Boolean = 4,
            Date = 5,
            Block = 6,
            List = 7,
            Color = 8,
            Percentage = 9
        }

        /// <summary>
        /// Type validation message hashes
        /// </summary>
        public static class MessageHashes
        {
            public static readonly uint WrongType = FastHasher.HashFNV1a32("WRONG_TYPE");
            public static readonly uint InvalidNumber = FastHasher.HashFNV1a32("INVALID_NUMBER");
            public static readonly uint InvalidDate = FastHasher.HashFNV1a32("INVALID_DATE");
            public static readonly uint InvalidBoolean = FastHasher.HashFNV1a32("INVALID_BOOLEAN");
            public static readonly uint InvalidColor = FastHasher.HashFNV1a32("INVALID_COLOR");
            public static readonly uint InvalidPercentage = FastHasher.HashFNV1a32("INVALID_PERCENTAGE");
        }

        /// <summary>
        /// Type expectation rule
        /// </summary>
        public struct TypeRule
        {
            public uint KeyHash;
            public ExpectedType Type;
            public bool StrictType; // If true, exact type match required

            public static TypeRule Create(uint keyHash, ExpectedType type, bool strict = false)
            {
                return new TypeRule
                {
                    KeyHash = keyHash,
                    Type = type,
                    StrictType = strict
                };
            }
        }

        /// <summary>
        /// Validate types of parsed values
        /// </summary>
        public static ValidationResult ValidateTypes(
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
                if (options.ValidateTypes)
                {
                    var typeRules = GetTypeRulesForContext(context);

                    try
                    {
                        ValidateValueTypes(keyValues, sourceData, typeRules, ref result, options);
                    }
                    finally
                    {
                        typeRules.Dispose();
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
        /// Validate individual value types
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateValueTypes(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            NativeArray<TypeRule> typeRules,
            ref ValidationResult result,
            ValidationOptions options)
        {
            for (int i = 0; i < keyValues.Length; i++)
            {
                var kvp = keyValues[i];

                // Find type rule for this key
                TypeRule? rule = null;
                for (int j = 0; j < typeRules.Length; j++)
                {
                    if (typeRules[j].KeyHash == kvp.KeyHash)
                    {
                        rule = typeRules[j];
                        break;
                    }
                }

                if (rule.HasValue)
                {
                    ValidateValueType(kvp, sourceData, rule.Value, ref result);
                }

                if (result.ErrorCount >= options.MaxErrors)
                    break;
            }
        }

        /// <summary>
        /// Validate a single value's type
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateValueType(
            ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp,
            NativeSlice<byte> sourceData,
            TypeRule rule,
            ref ValidationResult result)
        {
            var actualType = GetActualValueType(kvp, sourceData);

            if (!IsTypeCompatible(actualType, rule.Type, rule.StrictType))
            {
                var message = ValidationMessage.CreateWithContext(
                    ValidationType.Type,
                    ValidationSeverity.Error,
                    kvp.LineNumber,
                    0,
                    0,
                    MessageHashes.WrongType,
                    kvp.Key);
                result.AddMessage(message);
                return;
            }

            // Additional type-specific validation
            switch (rule.Type)
            {
                case ExpectedType.Integer:
                case ExpectedType.Float:
                    ValidateNumber(kvp, sourceData, rule.Type, ref result);
                    break;

                case ExpectedType.Date:
                    ValidateDate(kvp, sourceData, ref result);
                    break;

                case ExpectedType.Boolean:
                    ValidateBoolean(kvp, sourceData, ref result);
                    break;

                case ExpectedType.Color:
                    ValidateColor(kvp, sourceData, ref result);
                    break;

                case ExpectedType.Percentage:
                    ValidatePercentage(kvp, sourceData, ref result);
                    break;
            }
        }

        /// <summary>
        /// Get the actual type of a parsed value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ExpectedType GetActualValueType(
            ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp,
            NativeSlice<byte> sourceData)
        {
            switch (kvp.Value.Type)
            {
                case ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal:
                    return InferLiteralType(kvp.Value.RawData);

                case ParadoxParser.Core.ParadoxParser.ParsedValueType.Block:
                    return ExpectedType.Block;

                case ParadoxParser.Core.ParadoxParser.ParsedValueType.List:
                    return ExpectedType.List;

                default:
                    return ExpectedType.Any;
            }
        }

        /// <summary>
        /// Infer the type of a literal value from its content
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ExpectedType InferLiteralType(NativeSlice<byte> data)
        {
            if (data.Length == 0)
                return ExpectedType.String;

            // Check for quoted string
            if (data.Length >= 2 &&
                ((data[0] == (byte)'"' && data[data.Length - 1] == (byte)'"') ||
                 (data[0] == (byte)'\'' && data[data.Length - 1] == (byte)'\'')))
            {
                return ExpectedType.String;
            }

            // Check for boolean
            if (IsBooleanValue(data))
                return ExpectedType.Boolean;

            // Check for date (YYYY.MM.DD format)
            if (IsDateFormat(data))
                return ExpectedType.Date;

            // Check for number
            if (IsNumericValue(data))
            {
                // Check if it contains decimal point
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] == (byte)'.')
                        return ExpectedType.Float;
                }
                return ExpectedType.Integer;
            }

            // Default to string/identifier
            return ExpectedType.String;
        }

        /// <summary>
        /// Check if two types are compatible
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsTypeCompatible(ExpectedType actual, ExpectedType expected, bool strict)
        {
            if (expected == ExpectedType.Any)
                return true;

            if (actual == expected)
                return true;

            if (strict)
                return false;

            // Allow some implicit conversions
            switch (expected)
            {
                case ExpectedType.Float:
                    return actual == ExpectedType.Integer;

                case ExpectedType.String:
                    return true; // Most things can be treated as strings

                case ExpectedType.Percentage:
                    return actual == ExpectedType.Float || actual == ExpectedType.Integer;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Validate number format and range
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateNumber(
            ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp,
            NativeSlice<byte> sourceData,
            ExpectedType expectedType,
            ref ValidationResult result)
        {
            var parseResult = FastNumberParser.ParseFloat(kvp.Value.RawData);
            if (!parseResult.Success)
            {
                var message = ValidationMessage.CreateWithContext(
                    ValidationType.Type,
                    ValidationSeverity.Error,
                    kvp.LineNumber,
                    0,
                    0,
                    MessageHashes.InvalidNumber,
                    kvp.Key);
                result.AddMessage(message);
            }
        }

        /// <summary>
        /// Validate date format
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateDate(
            ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp,
            NativeSlice<byte> sourceData,
            ref ValidationResult result)
        {
            // TODO: Implement DateParser or use existing date parsing logic
            var parseResult = new { Success = true }; // Placeholder
            if (!parseResult.Success)
            {
                var message = ValidationMessage.CreateWithContext(
                    ValidationType.Type,
                    ValidationSeverity.Error,
                    kvp.LineNumber,
                    0,
                    0,
                    MessageHashes.InvalidDate,
                    kvp.Key);
                result.AddMessage(message);
            }
        }

        /// <summary>
        /// Validate boolean format
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateBoolean(
            ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp,
            NativeSlice<byte> sourceData,
            ref ValidationResult result)
        {
            if (!IsBooleanValue(kvp.Value.RawData))
            {
                var message = ValidationMessage.CreateWithContext(
                    ValidationType.Type,
                    ValidationSeverity.Error,
                    kvp.LineNumber,
                    0,
                    0,
                    MessageHashes.InvalidBoolean,
                    kvp.Key);
                result.AddMessage(message);
            }
        }

        /// <summary>
        /// Validate color format
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateColor(
            ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp,
            NativeSlice<byte> sourceData,
            ref ValidationResult result)
        {
            // Colors are typically blocks like "rgb { 255 128 64 }"
            if (kvp.Value.Type != ParadoxParser.Core.ParadoxParser.ParsedValueType.Block)
            {
                var message = ValidationMessage.CreateWithContext(
                    ValidationType.Type,
                    ValidationSeverity.Error,
                    kvp.LineNumber,
                    0,
                    0,
                    MessageHashes.InvalidColor,
                    kvp.Key);
                result.AddMessage(message);
            }
        }

        /// <summary>
        /// Validate percentage format
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidatePercentage(
            ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp,
            NativeSlice<byte> sourceData,
            ref ValidationResult result)
        {
            var parseResult = FastNumberParser.ParseFloat(kvp.Value.RawData);
            if (!parseResult.Success)
            {
                var message = ValidationMessage.CreateWithContext(
                    ValidationType.Type,
                    ValidationSeverity.Error,
                    kvp.LineNumber,
                    0,
                    0,
                    MessageHashes.InvalidPercentage,
                    kvp.Key);
                result.AddMessage(message);
            }
            else if (parseResult.Value < -100.0f || parseResult.Value > 100.0f)
            {
                // Warning for values outside typical percentage range
                var message = ValidationMessage.CreateWithContext(
                    ValidationType.Type,
                    ValidationSeverity.Warning,
                    kvp.LineNumber,
                    0,
                    0,
                    MessageHashes.InvalidPercentage,
                    kvp.Key);
                result.AddMessage(message);
            }
        }

        // Helper methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsBooleanValue(NativeSlice<byte> data)
        {
            return IsKeywordMatchString(data, "yes") ||
                   IsKeywordMatchString(data, "no") ||
                   IsKeywordMatchString(data, "true") ||
                   IsKeywordMatchString(data, "false");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDateFormat(NativeSlice<byte> data)
        {
            // Basic check for YYYY.MM.DD format
            if (data.Length != 10)
                return false;

            return data[4] == (byte)'.' && data[7] == (byte)'.';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNumericValue(NativeSlice<byte> data)
        {
            if (data.Length == 0)
                return false;

            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (i == 0 && (b == (byte)'-' || b == (byte)'+'))
                    continue; // Allow leading sign

                if (!(b >= (byte)'0' && b <= (byte)'9') && b != (byte)'.')
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsKeywordMatchString(NativeSlice<byte> data, string keyword)
        {
            if (data.Length != keyword.Length)
                return false;

            for (int i = 0; i < keyword.Length; i++)
            {
                if (data[i] != (byte)keyword[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Get type rules for a validation context
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static NativeArray<TypeRule> GetTypeRulesForContext(SemanticValidator.ValidationContext context)
        {
            switch (context)
            {
                case SemanticValidator.ValidationContext.Country:
                    return CreateCountryTypeRules();
                case SemanticValidator.ValidationContext.Province:
                    return CreateProvinceTypeRules();
                case SemanticValidator.ValidationContext.Technology:
                    return CreateTechnologyTypeRules();
                default:
                    return new NativeArray<TypeRule>(0, Allocator.Temp);
            }
        }

        private static NativeArray<TypeRule> CreateCountryTypeRules()
        {
            var rules = new NativeArray<TypeRule>(8, Allocator.Temp);
            int index = 0;

            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("tag"), ExpectedType.String, strict: true);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("government"), ExpectedType.String);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("capital"), ExpectedType.Integer);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("stability"), ExpectedType.Float);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("legitimacy"), ExpectedType.Float);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("prestige"), ExpectedType.Float);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("treasury"), ExpectedType.Float);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("history"), ExpectedType.Block);

            return rules;
        }

        private static NativeArray<TypeRule> CreateProvinceTypeRules()
        {
            var rules = new NativeArray<TypeRule>(6, Allocator.Temp);
            int index = 0;

            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("owner"), ExpectedType.String);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("tax"), ExpectedType.Float);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("production"), ExpectedType.Float);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("manpower"), ExpectedType.Float);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("discovered_by"), ExpectedType.List);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("history"), ExpectedType.Block);

            return rules;
        }

        private static NativeArray<TypeRule> CreateTechnologyTypeRules()
        {
            var rules = new NativeArray<TypeRule>(3, Allocator.Temp);
            int index = 0;

            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("military"), ExpectedType.Integer);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("diplomatic"), ExpectedType.Integer);
            rules[index++] = TypeRule.Create(FastHasher.HashFNV1a32("administrative"), ExpectedType.Integer);

            return rules;
        }
    }
}