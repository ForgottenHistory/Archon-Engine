using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Tokenization;
using ParadoxParser.Utilities;

namespace ParadoxParser.Core
{
    /// <summary>
    /// Specialized handler for modifier blocks in Paradox files
    /// Handles "modifier = { ... }" structures commonly used for effects and bonuses
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class ModifierBlockHandler
    {
        /// <summary>
        /// Parsed modifier block result
        /// </summary>
        public struct ModifierBlockResult
        {
            public bool Success;
            public ModifierType Type;
            public NativeList<ModifierEntry> Entries;
            public int BytesConsumed;

            public static ModifierBlockResult Failed => new ModifierBlockResult { Success = false };

            public static ModifierBlockResult Create(ModifierType type, NativeList<ModifierEntry> entries, int bytesConsumed)
            {
                return new ModifierBlockResult
                {
                    Success = true,
                    Type = type,
                    Entries = entries,
                    BytesConsumed = bytesConsumed
                };
            }
        }

        /// <summary>
        /// Individual modifier entry
        /// </summary>
        public struct ModifierEntry
        {
            public uint KeyHash;
            public NativeSlice<byte> Key;
            public float Value;
            public ModifierOperation Operation;
            public bool IsPercentage;

            public static ModifierEntry Create(NativeSlice<byte> key, float value, ModifierOperation operation = ModifierOperation.Add)
            {
                return new ModifierEntry
                {
                    KeyHash = FastHasher.HashFNV1a32(key),
                    Key = key,
                    Value = value,
                    Operation = operation,
                    IsPercentage = false
                };
            }
        }

        /// <summary>
        /// Types of modifier blocks
        /// </summary>
        public enum ModifierType : byte
        {
            Generic = 0,        // Generic modifier block
            Country,            // Country-specific modifiers
            Province,           // Province-specific modifiers
            Unit,               // Military unit modifiers
            Trade,              // Trade-related modifiers
            Technology,         // Technology bonuses
            Government,         // Government modifiers
            Religion,           // Religious modifiers
            Culture,            // Cultural modifiers
            Economy,            // Economic modifiers
            Military,           // Military modifiers
            Diplomatic,         // Diplomatic modifiers
            Special             // Special event/decision modifiers
        }

        /// <summary>
        /// Operations that can be applied to modifier values
        /// </summary>
        public enum ModifierOperation : byte
        {
            Add = 0,           // Add to base value (most common)
            Multiply,          // Multiply base value
            Set,               // Set absolute value
            Minimum,           // Set minimum value
            Maximum,           // Set maximum value
            Percentage         // Add percentage of base value
        }

        /// <summary>
        /// Parse a modifier block from tokens
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ModifierBlockResult ParseModifierBlock(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData,
            NativeSlice<byte> contextKey)
        {
            if (startIndex >= tokens.Length)
                return ModifierBlockResult.Failed;

            // Determine modifier type from context
            var modifierType = DetermineModifierType(contextKey);

            // Parse the block content
            var entries = new NativeList<ModifierEntry>(32, Allocator.Temp);

            try
            {
                int consumed = ParseModifierEntries(tokens, startIndex, sourceData, entries, modifierType);
                if (consumed > 0)
                {
                    return ModifierBlockResult.Create(modifierType, entries, consumed);
                }

                return ModifierBlockResult.Failed;
            }
            catch
            {
                entries.Dispose();
                return ModifierBlockResult.Failed;
            }
        }

        /// <summary>
        /// Check if a key represents a modifier block
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsModifierBlock(NativeSlice<byte> key)
        {
            return IsKeyword(key, "modifier") ||
                   IsKeyword(key, "country_modifier") ||
                   IsKeyword(key, "province_modifier") ||
                   IsKeyword(key, "trade_modifier") ||
                   IsKeyword(key, "unit_modifier") ||
                   IsKeyword(key, "tech_modifier");
        }

        /// <summary>
        /// Determine modifier type from context key
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ModifierType DetermineModifierType(NativeSlice<byte> contextKey)
        {
            if (IsKeyword(contextKey, "country_modifier"))
                return ModifierType.Country;
            if (IsKeyword(contextKey, "province_modifier"))
                return ModifierType.Province;
            if (IsKeyword(contextKey, "trade_modifier"))
                return ModifierType.Trade;
            if (IsKeyword(contextKey, "unit_modifier"))
                return ModifierType.Unit;
            if (IsKeyword(contextKey, "tech_modifier"))
                return ModifierType.Technology;
            if (IsKeyword(contextKey, "government_modifier"))
                return ModifierType.Government;
            if (IsKeyword(contextKey, "religion_modifier"))
                return ModifierType.Religion;
            if (IsKeyword(contextKey, "culture_modifier"))
                return ModifierType.Culture;

            return ModifierType.Generic;
        }

        /// <summary>
        /// Parse individual modifier entries from a block
        /// </summary>
        private static int ParseModifierEntries(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData,
            NativeList<ModifierEntry> entries,
            ModifierType modifierType)
        {
            if (startIndex >= tokens.Length || tokens[startIndex].Type != TokenType.LeftBrace)
                return 0;

            int tokenIndex = startIndex + 1;
            int braceDepth = 1;

            while (tokenIndex < tokens.Length && braceDepth > 0)
            {
                var token = tokens[tokenIndex];

                switch (token.Type)
                {
                    case TokenType.LeftBrace:
                        braceDepth++;
                        tokenIndex++;
                        break;

                    case TokenType.RightBrace:
                        braceDepth--;
                        tokenIndex++;
                        break;

                    case TokenType.Identifier:
                        // Parse key-value pair
                        if (TryParseModifierEntry(tokens, tokenIndex, sourceData, out var entry, out var consumed))
                        {
                            entries.Add(entry);
                            tokenIndex += consumed;
                        }
                        else
                        {
                            tokenIndex++;
                        }
                        break;

                    case TokenType.Whitespace:
                    case TokenType.Newline:
                    case TokenType.Hash:
                        tokenIndex++;
                        break;

                    default:
                        tokenIndex++;
                        break;
                }
            }

            return braceDepth == 0 ? tokenIndex - startIndex : 0;
        }

        /// <summary>
        /// Try to parse a single modifier entry (key = value)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryParseModifierEntry(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData,
            out ModifierEntry entry,
            out int tokensConsumed)
        {
            entry = default;
            tokensConsumed = 0;

            if (startIndex + 2 >= tokens.Length)
                return false;

            var keyToken = tokens[startIndex];
            var equalsToken = tokens[startIndex + 1];
            var valueToken = tokens[startIndex + 2];

            // Validate pattern: key = value
            if (keyToken.Type != TokenType.Identifier ||
                equalsToken.Type != TokenType.Equals ||
                valueToken.Type != TokenType.Number)
            {
                return false;
            }

            // Extract key and value
            var keyData = sourceData.Slice(keyToken.StartPosition, keyToken.Length);
            var valueData = sourceData.Slice(valueToken.StartPosition, valueToken.Length);

            var parseResult = FastNumberParser.ParseFloat(valueData);
            if (!parseResult.Success)
                return false;

            // Determine operation and format
            var operation = DetermineModifierOperation(keyData, parseResult.Value);
            bool isPercentage = IsPercentageModifier(keyData);

            entry = new ModifierEntry
            {
                KeyHash = FastHasher.HashFNV1a32(keyData),
                Key = keyData,
                Value = parseResult.Value,
                Operation = operation,
                IsPercentage = isPercentage
            };

            tokensConsumed = 3;
            return true;
        }

        /// <summary>
        /// Determine the operation type for a modifier key
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ModifierOperation DetermineModifierOperation(NativeSlice<byte> key, float value)
        {
            // Check for specific operation keywords in the key
            if (ContainsKeyword(key, "factor") || ContainsKeyword(key, "mult"))
                return ModifierOperation.Multiply;
            if (ContainsKeyword(key, "min"))
                return ModifierOperation.Minimum;
            if (ContainsKeyword(key, "max"))
                return ModifierOperation.Maximum;
            if (ContainsKeyword(key, "set") || ContainsKeyword(key, "base"))
                return ModifierOperation.Set;

            // Check for percentage indicators
            if (IsPercentageModifier(key))
                return ModifierOperation.Percentage;

            // Default to additive
            return ModifierOperation.Add;
        }

        /// <summary>
        /// Check if a modifier key represents a percentage value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPercentageModifier(NativeSlice<byte> key)
        {
            return ContainsKeyword(key, "percent") ||
                   ContainsKeyword(key, "modifier") ||
                   EndsWithKeyword(key, "_modifier");
        }

        /// <summary>
        /// Validate that a modifier key is appropriate for the modifier type
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidModifierKey(NativeSlice<byte> key, ModifierType type)
        {
            switch (type)
            {
                case ModifierType.Country:
                    return IsCountryModifierKey(key);
                case ModifierType.Province:
                    return IsProvinceModifierKey(key);
                case ModifierType.Military:
                    return IsMilitaryModifierKey(key);
                case ModifierType.Trade:
                    return IsTradeModifierKey(key);
                default:
                    return true; // Generic modifiers accept any key
            }
        }

        /// <summary>
        /// Get the effective value of a modifier considering its operation
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetEffectiveValue(ModifierEntry entry, float baseValue)
        {
            return entry.Operation switch
            {
                ModifierOperation.Add => baseValue + entry.Value,
                ModifierOperation.Multiply => baseValue * entry.Value,
                ModifierOperation.Set => entry.Value,
                ModifierOperation.Minimum => baseValue < entry.Value ? entry.Value : baseValue,
                ModifierOperation.Maximum => baseValue > entry.Value ? entry.Value : baseValue,
                ModifierOperation.Percentage => baseValue * (1.0f + entry.Value / 100.0f),
                _ => baseValue + entry.Value
            };
        }

        /// <summary>
        /// Calculate the total effect of multiple modifiers on a base value
        /// </summary>
        public static float CalculateModifiedValue(float baseValue, NativeSlice<ModifierEntry> modifiers)
        {
            float result = baseValue;

            // Apply modifiers in order of precedence
            // 1. Set operations (override base value)
            for (int i = 0; i < modifiers.Length; i++)
            {
                if (modifiers[i].Operation == ModifierOperation.Set)
                {
                    result = modifiers[i].Value;
                    break; // Only apply the last set operation
                }
            }

            // 2. Additive modifiers
            for (int i = 0; i < modifiers.Length; i++)
            {
                if (modifiers[i].Operation == ModifierOperation.Add)
                {
                    result += modifiers[i].Value;
                }
            }

            // 3. Percentage modifiers
            for (int i = 0; i < modifiers.Length; i++)
            {
                if (modifiers[i].Operation == ModifierOperation.Percentage)
                {
                    result *= (1.0f + modifiers[i].Value / 100.0f);
                }
            }

            // 4. Multiplicative modifiers
            for (int i = 0; i < modifiers.Length; i++)
            {
                if (modifiers[i].Operation == ModifierOperation.Multiply)
                {
                    result *= modifiers[i].Value;
                }
            }

            // 5. Min/Max constraints
            for (int i = 0; i < modifiers.Length; i++)
            {
                if (modifiers[i].Operation == ModifierOperation.Minimum && result < modifiers[i].Value)
                {
                    result = modifiers[i].Value;
                }
                else if (modifiers[i].Operation == ModifierOperation.Maximum && result > modifiers[i].Value)
                {
                    result = modifiers[i].Value;
                }
            }

            return result;
        }

        // Helper methods for modifier key validation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCountryModifierKey(NativeSlice<byte> key)
        {
            return ContainsKeyword(key, "tax") ||
                   ContainsKeyword(key, "production") ||
                   ContainsKeyword(key, "trade") ||
                   ContainsKeyword(key, "diplomatic") ||
                   ContainsKeyword(key, "military") ||
                   ContainsKeyword(key, "stability");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsProvinceModifierKey(NativeSlice<byte> key)
        {
            return ContainsKeyword(key, "goods") ||
                   ContainsKeyword(key, "population") ||
                   ContainsKeyword(key, "development") ||
                   ContainsKeyword(key, "fort") ||
                   ContainsKeyword(key, "supply");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsMilitaryModifierKey(NativeSlice<byte> key)
        {
            return ContainsKeyword(key, "discipline") ||
                   ContainsKeyword(key, "morale") ||
                   ContainsKeyword(key, "combat") ||
                   ContainsKeyword(key, "siege") ||
                   ContainsKeyword(key, "attrition");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsTradeModifierKey(NativeSlice<byte> key)
        {
            return ContainsKeyword(key, "trade") ||
                   ContainsKeyword(key, "merchant") ||
                   ContainsKeyword(key, "goods") ||
                   ContainsKeyword(key, "income");
        }

        // String utility methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsKeyword(NativeSlice<byte> data, string keyword)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ContainsKeyword(NativeSlice<byte> data, string keyword)
        {
            if (data.Length < keyword.Length)
                return false;

            for (int i = 0; i <= data.Length - keyword.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < keyword.Length; j++)
                {
                    if (data[i + j] != (byte)keyword[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool EndsWithKeyword(NativeSlice<byte> data, string keyword)
        {
            if (data.Length < keyword.Length)
                return false;

            int offset = data.Length - keyword.Length;
            for (int i = 0; i < keyword.Length; i++)
            {
                if (data[offset + i] != (byte)keyword[i])
                    return false;
            }
            return true;
        }
    }
}