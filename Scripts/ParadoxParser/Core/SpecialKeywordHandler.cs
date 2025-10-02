using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Utilities;

namespace ParadoxParser.Core
{
    /// <summary>
    /// Handles special keywords common in Paradox files
    /// Provides fast lookup and conversion for boolean values, special constants, etc.
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class SpecialKeywordHandler
    {
        /// <summary>
        /// Result of special keyword parsing
        /// </summary>
        public struct KeywordParseResult
        {
            public bool Success;
            public SpecialKeywordType Type;
            public bool BooleanValue;
            public float NumericValue;
            public uint Hash;

            public static KeywordParseResult Failed => new KeywordParseResult { Success = false };

            public static KeywordParseResult Boolean(bool value)
            {
                return new KeywordParseResult
                {
                    Success = true,
                    Type = SpecialKeywordType.Boolean,
                    BooleanValue = value,
                    NumericValue = value ? 1f : 0f,
                    Hash = 0
                };
            }

            public static KeywordParseResult Numeric(float value, SpecialKeywordType type = SpecialKeywordType.Numeric)
            {
                return new KeywordParseResult
                {
                    Success = true,
                    Type = type,
                    BooleanValue = value != 0f,
                    NumericValue = value,
                    Hash = 0
                };
            }

            public static KeywordParseResult Special(SpecialKeywordType type, uint hash)
            {
                return new KeywordParseResult
                {
                    Success = true,
                    Type = type,
                    BooleanValue = false,
                    NumericValue = 0f,
                    Hash = hash
                };
            }
        }

        /// <summary>
        /// Types of special keywords
        /// </summary>
        public enum SpecialKeywordType : byte
        {
            Boolean = 0,        // yes, no, true, false
            Numeric,            // Special numeric constants
            Scope,              // THIS, ROOT, PREV, etc.
            Event,              // Special event keywords
            Modifier,           // Special modifier keywords
            Color,              // Named colors
            Country,            // Special country tags
            Province,           // Special province references
            Unknown
        }

        // Pre-computed hashes for common keywords (for fast lookup)
        private static readonly uint YES_HASH = ComputeHash("yes");
        private static readonly uint NO_HASH = ComputeHash("no");
        private static readonly uint TRUE_HASH = ComputeHash("true");
        private static readonly uint FALSE_HASH = ComputeHash("false");
        private static readonly uint THIS_HASH = ComputeHash("THIS");
        private static readonly uint ROOT_HASH = ComputeHash("ROOT");
        private static readonly uint PREV_HASH = ComputeHash("PREV");
        private static readonly uint FROM_HASH = ComputeHash("FROM");

        /// <summary>
        /// Parse a special keyword from byte data
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static KeywordParseResult ParseKeyword(NativeSlice<byte> data)
        {
            if (data.Length == 0)
                return KeywordParseResult.Failed;

            uint hash = FastHasher.HashFNV1a32(data);

            // Fast hash-based lookup for common keywords
            if (hash == YES_HASH && IsExactMatch(data, "yes"))
                return KeywordParseResult.Boolean(true);
            if (hash == NO_HASH && IsExactMatch(data, "no"))
                return KeywordParseResult.Boolean(false);
            if (hash == TRUE_HASH && IsExactMatch(data, "true"))
                return KeywordParseResult.Boolean(true);
            if (hash == FALSE_HASH && IsExactMatch(data, "false"))
                return KeywordParseResult.Boolean(false);

            // Check scope keywords
            if (hash == THIS_HASH && IsExactMatch(data, "THIS"))
                return KeywordParseResult.Special(SpecialKeywordType.Scope, hash);
            if (hash == ROOT_HASH && IsExactMatch(data, "ROOT"))
                return KeywordParseResult.Special(SpecialKeywordType.Scope, hash);
            if (hash == PREV_HASH && IsExactMatch(data, "PREV"))
                return KeywordParseResult.Special(SpecialKeywordType.Scope, hash);
            if (hash == FROM_HASH && IsExactMatch(data, "FROM"))
                return KeywordParseResult.Special(SpecialKeywordType.Scope, hash);

            // Check for numeric keywords
            if (TryParseNumericKeyword(data, out var numericResult))
                return numericResult;

            // Check for color keywords
            if (TryParseColorKeyword(data, out var colorResult))
                return colorResult;

            // Check for event keywords
            if (TryParseEventKeyword(data, out var eventResult))
                return eventResult;

            return KeywordParseResult.Failed;
        }

        /// <summary>
        /// Check if data represents a boolean keyword
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBooleanKeyword(NativeSlice<byte> data)
        {
            var result = ParseKeyword(data);
            return result.Success && result.Type == SpecialKeywordType.Boolean;
        }

        /// <summary>
        /// Check if data represents a scope keyword
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsScopeKeyword(NativeSlice<byte> data)
        {
            var result = ParseKeyword(data);
            return result.Success && result.Type == SpecialKeywordType.Scope;
        }

        /// <summary>
        /// Get boolean value from keyword
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool GetBooleanValue(NativeSlice<byte> data, bool defaultValue = false)
        {
            var result = ParseKeyword(data);
            return result.Success && result.Type == SpecialKeywordType.Boolean ? result.BooleanValue : defaultValue;
        }

        /// <summary>
        /// Try to parse numeric special keywords
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryParseNumericKeyword(NativeSlice<byte> data, out KeywordParseResult result)
        {
            result = KeywordParseResult.Failed;

            // Check for infinity/negative infinity
            if (IsExactMatch(data, "inf") || IsExactMatch(data, "infinity"))
            {
                result = KeywordParseResult.Numeric(float.PositiveInfinity);
                return true;
            }

            if (IsExactMatch(data, "-inf") || IsExactMatch(data, "-infinity"))
            {
                result = KeywordParseResult.Numeric(float.NegativeInfinity);
                return true;
            }

            // Check for NaN
            if (IsExactMatch(data, "nan") || IsExactMatch(data, "NaN"))
            {
                result = KeywordParseResult.Numeric(float.NaN);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try to parse color keywords
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryParseColorKeyword(NativeSlice<byte> data, out KeywordParseResult result)
        {
            result = KeywordParseResult.Failed;

            // Common color names in Paradox files
            if (IsExactMatch(data, "red"))
            {
                result = KeywordParseResult.Special(SpecialKeywordType.Color, FastHasher.HashFNV1a32(data));
                return true;
            }
            if (IsExactMatch(data, "green"))
            {
                result = KeywordParseResult.Special(SpecialKeywordType.Color, FastHasher.HashFNV1a32(data));
                return true;
            }
            if (IsExactMatch(data, "blue"))
            {
                result = KeywordParseResult.Special(SpecialKeywordType.Color, FastHasher.HashFNV1a32(data));
                return true;
            }
            if (IsExactMatch(data, "white"))
            {
                result = KeywordParseResult.Special(SpecialKeywordType.Color, FastHasher.HashFNV1a32(data));
                return true;
            }
            if (IsExactMatch(data, "black"))
            {
                result = KeywordParseResult.Special(SpecialKeywordType.Color, FastHasher.HashFNV1a32(data));
                return true;
            }
            if (IsExactMatch(data, "yellow"))
            {
                result = KeywordParseResult.Special(SpecialKeywordType.Color, FastHasher.HashFNV1a32(data));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try to parse event keywords
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryParseEventKeyword(NativeSlice<byte> data, out KeywordParseResult result)
        {
            result = KeywordParseResult.Failed;

            // Common event scope keywords
            if (IsExactMatch(data, "country_event") || IsExactMatch(data, "province_event") ||
                IsExactMatch(data, "character_event") || IsExactMatch(data, "narrative_event"))
            {
                result = KeywordParseResult.Special(SpecialKeywordType.Event, FastHasher.HashFNV1a32(data));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get all boolean keywords as a list
        /// </summary>
        public static void GetBooleanKeywords(NativeList<NativeSlice<byte>> keywords)
        {
            keywords.Clear();
            // Note: In a real implementation, you'd create NativeSlice<byte> from static data
            // For now, this is just the interface
        }

        /// <summary>
        /// Get all scope keywords
        /// </summary>
        public static void GetScopeKeywords(NativeList<NativeSlice<byte>> keywords)
        {
            keywords.Clear();
            // Note: In a real implementation, you'd create NativeSlice<byte> from static data
        }

        /// <summary>
        /// Check if a keyword is case-sensitive
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCaseSensitive(SpecialKeywordType type)
        {
            return type switch
            {
                SpecialKeywordType.Boolean => false,  // yes/no/true/false are case-insensitive
                SpecialKeywordType.Scope => true,     // THIS/ROOT/etc. are case-sensitive
                SpecialKeywordType.Event => false,    // Events are usually case-insensitive
                SpecialKeywordType.Color => false,    // Colors are case-insensitive
                _ => true                              // Default to case-sensitive
            };
        }

        /// <summary>
        /// Convert a special keyword to its canonical form
        /// </summary>
        public static bool TryGetCanonicalForm(NativeSlice<byte> input, out NativeSlice<byte> canonical)
        {
            canonical = default;

            var result = ParseKeyword(input);
            if (!result.Success)
                return false;

            // For boolean keywords, return canonical forms
            if (result.Type == SpecialKeywordType.Boolean)
            {
                if (result.BooleanValue)
                {
                    // Return "yes" as canonical true
                    canonical = CreateSliceFromString("yes");
                }
                else
                {
                    // Return "no" as canonical false
                    canonical = CreateSliceFromString("no");
                }
                return true;
            }

            // For other types, return original
            canonical = input;
            return true;
        }

        /// <summary>
        /// Validate that a keyword is used in the correct context
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidInContext(NativeSlice<byte> keyword, NativeSlice<byte> contextKey)
        {
            var result = ParseKeyword(keyword);
            if (!result.Success)
                return true; // Not a special keyword, so valid in any context

            // Scope keywords are only valid in certain contexts
            if (result.Type == SpecialKeywordType.Scope)
            {
                // Check if context key suggests scope usage is appropriate
                return IsValidScopeContext(contextKey);
            }

            return true; // Other keywords are generally context-independent
        }

        // Helper methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsExactMatch(NativeSlice<byte> data, string str)
        {
            if (data.Length != str.Length)
                return false;

            for (int i = 0; i < str.Length; i++)
            {
                if (data[i] != (byte)str[i])
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidScopeContext(NativeSlice<byte> contextKey)
        {
            // Check if the context key suggests this is a scope-aware context
            // Common scope contexts: "limit", "trigger", "effect", "modifier", etc.
            return IsExactMatch(contextKey, "limit") ||
                   IsExactMatch(contextKey, "trigger") ||
                   IsExactMatch(contextKey, "effect") ||
                   IsExactMatch(contextKey, "modifier") ||
                   IsExactMatch(contextKey, "if") ||
                   IsExactMatch(contextKey, "else_if");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ComputeHash(string str)
        {
            // Simple compile-time hash computation
            uint hash = 2166136261u; // FNV offset basis
            for (int i = 0; i < str.Length; i++)
            {
                hash ^= (byte)str[i];
                hash *= 16777619u; // FNV prime
            }
            return hash;
        }

        private static NativeSlice<byte> CreateSliceFromString(string str)
        {
            // Note: In a real implementation, you'd need to manage the lifetime
            // of the byte data. This is simplified for the interface.
            return default;
        }
    }
}