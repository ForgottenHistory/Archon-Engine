using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Tokenization;
using ParadoxParser.Utilities;

namespace ParadoxParser.Core
{
    /// <summary>
    /// Specialized parser for key-value pairs and lists
    /// Optimized for common Paradox patterns
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class KeyValueParser
    {
        /// <summary>
        /// Parse a simple key-value pair (key = value)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryParseKeyValue(
            NativeSlice<Token> tokens,
            int startIndex,
            out ParadoxParser.ParsedKeyValue result,
            out int tokensConsumed,
            NativeSlice<byte> sourceData)
        {
            result = default;
            tokensConsumed = 0;

            if (startIndex + 2 >= tokens.Length)
                return false;

            var keyToken = tokens[startIndex];
            var equalsToken = tokens[startIndex + 1];
            var valueToken = tokens[startIndex + 2];

            // Validate pattern: Identifier = Value
            if (keyToken.Type != TokenType.Identifier ||
                equalsToken.Type != TokenType.Equals ||
                !IsValidValueToken(valueToken))
            {
                return false;
            }

            // Extract key and value data
            var keyData = sourceData.Slice(keyToken.StartPosition, keyToken.Length);
            var valueData = sourceData.Slice(valueToken.StartPosition, valueToken.Length);

            result = new ParadoxParser.ParsedKeyValue
            {
                KeyHash = FastHasher.HashFNV1a32(keyData),
                Key = keyData,
                Value = new ParadoxParser.ParsedValue
                {
                    Type = ParadoxParser.ParsedValueType.Literal,
                    RawData = valueData,
                    BlockStartIndex = -1,
                    BlockLength = 0
                },
                LineNumber = keyToken.Line
            };

            tokensConsumed = 3;
            return true;
        }

        /// <summary>
        /// Parse a list value (key = { item1 item2 item3 })
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryParseListValue(
            NativeSlice<Token> tokens,
            int startIndex,
            out ParadoxParser.ParsedKeyValue result,
            out int tokensConsumed,
            NativeSlice<byte> sourceData,
            NativeList<NativeSlice<byte>> listItems)
        {
            result = default;
            tokensConsumed = 0;
            listItems.Clear();

            if (startIndex + 3 >= tokens.Length)
                return false;

            var keyToken = tokens[startIndex];
            var equalsToken = tokens[startIndex + 1];
            var leftBraceToken = tokens[startIndex + 2];

            // Validate start pattern: Identifier = {
            if (keyToken.Type != TokenType.Identifier ||
                equalsToken.Type != TokenType.Equals ||
                leftBraceToken.Type != TokenType.LeftBrace)
            {
                return false;
            }

            int tokenIndex = startIndex + 3;
            bool foundClosingBrace = false;

            // Parse list items until closing brace
            while (tokenIndex < tokens.Length)
            {
                var token = tokens[tokenIndex];

                switch (token.Type)
                {
                    case TokenType.RightBrace:
                        foundClosingBrace = true;
                        tokenIndex++;
                        break;

                    case TokenType.Whitespace:
                    case TokenType.Newline:
                        tokenIndex++; // Skip whitespace
                        continue;

                    case TokenType.Identifier:
                    case TokenType.Number:
                    case TokenType.String:
                        // Add list item
                        var itemData = sourceData.Slice(token.StartPosition, token.Length);
                        listItems.Add(itemData);
                        tokenIndex++;
                        continue;

                    default:
                        return false; // Invalid token in list
                }

                if (foundClosingBrace)
                    break;
            }

            if (!foundClosingBrace)
                return false;

            // Create result
            var keyData = sourceData.Slice(keyToken.StartPosition, keyToken.Length);
            result = new ParadoxParser.ParsedKeyValue
            {
                KeyHash = FastHasher.HashFNV1a32(keyData),
                Key = keyData,
                Value = new ParadoxParser.ParsedValue
                {
                    Type = ParadoxParser.ParsedValueType.List,
                    RawData = default, // List items stored separately
                    BlockStartIndex = 0, // Index into listItems
                    BlockLength = listItems.Length
                },
                LineNumber = keyToken.Line
            };

            tokensConsumed = tokenIndex - startIndex;
            return true;
        }

        /// <summary>
        /// Parse a block value (key = { nested key-value pairs })
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryParseBlockValue(
            NativeSlice<Token> tokens,
            int startIndex,
            out ParadoxParser.ParsedKeyValue result,
            out int tokensConsumed,
            NativeSlice<byte> sourceData,
            NativeList<ParadoxParser.ParsedKeyValue> childKeyValues)
        {
            result = default;
            tokensConsumed = 0;
            childKeyValues.Clear();

            if (startIndex + 3 >= tokens.Length)
                return false;

            var keyToken = tokens[startIndex];
            var equalsToken = tokens[startIndex + 1];
            var leftBraceToken = tokens[startIndex + 2];

            // Validate start pattern: Identifier = {
            if (keyToken.Type != TokenType.Identifier ||
                equalsToken.Type != TokenType.Equals ||
                leftBraceToken.Type != TokenType.LeftBrace)
            {
                return false;
            }

            int tokenIndex = startIndex + 3;
            int braceDepth = 1;
            int blockStartIndex = childKeyValues.Length;

            // Parse nested content
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

                    case TokenType.Whitespace:
                    case TokenType.Newline:
                    case TokenType.Hash: // Skip comments
                        tokenIndex++;
                        break;

                    case TokenType.Identifier:
                        // Try to parse nested key-value pair
                        if (TryParseKeyValue(tokens, tokenIndex, out var nestedKvp, out var nestedConsumed, sourceData))
                        {
                            childKeyValues.Add(nestedKvp);
                            tokenIndex += nestedConsumed;
                        }
                        else
                        {
                            tokenIndex++; // Skip if can't parse
                        }
                        break;

                    default:
                        tokenIndex++; // Skip unknown tokens
                        break;
                }
            }

            if (braceDepth != 0)
                return false; // Unmatched braces

            // Create result
            var keyData = sourceData.Slice(keyToken.StartPosition, keyToken.Length);
            result = new ParadoxParser.ParsedKeyValue
            {
                KeyHash = FastHasher.HashFNV1a32(keyData),
                Key = keyData,
                Value = new ParadoxParser.ParsedValue
                {
                    Type = ParadoxParser.ParsedValueType.Block,
                    RawData = default,
                    BlockStartIndex = blockStartIndex,
                    BlockLength = childKeyValues.Length - blockStartIndex
                },
                LineNumber = keyToken.Line
            };

            tokensConsumed = tokenIndex - startIndex;
            return true;
        }

        /// <summary>
        /// Check if token is a valid value token
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidValueToken(Token token)
        {
            return token.Type switch
            {
                TokenType.Identifier or TokenType.Number or
                TokenType.String or TokenType.Date => true,
                _ => false
            };
        }

        /// <summary>
        /// Extract string value from parsed value data
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetStringValue(ParadoxParser.ParsedValue value, out NativeSlice<byte> stringData)
        {
            stringData = default;

            if (value.Type != ParadoxParser.ParsedValueType.Literal)
                return false;

            stringData = value.RawData;
            return true;
        }

        /// <summary>
        /// Extract integer value from parsed value data
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetIntValue(ParadoxParser.ParsedValue value, out int intValue)
        {
            intValue = 0;

            if (value.Type != ParadoxParser.ParsedValueType.Literal)
                return false;

            var parseResult = FastNumberParser.ParseInt32(value.RawData);
            if (parseResult.Success)
            {
                intValue = parseResult.Value;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Extract float value from parsed value data
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFloatValue(ParadoxParser.ParsedValue value, out float floatValue)
        {
            floatValue = 0f;

            if (value.Type != ParadoxParser.ParsedValueType.Literal)
                return false;

            var parseResult = FastNumberParser.ParseFloat(value.RawData);
            if (parseResult.Success)
            {
                floatValue = parseResult.Value;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Extract date value from parsed value data
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetDateValue(ParadoxParser.ParsedValue value, out FastDateParser.ParadoxDate dateValue)
        {
            dateValue = default;

            if (value.Type != ParadoxParser.ParsedValueType.Literal)
                return false;

            var parseResult = FastDateParser.ParseDate(value.RawData);
            if (parseResult.Success)
            {
                dateValue = parseResult.Date;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Compare two key slices for equality (case-sensitive)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool KeyEquals(NativeSlice<byte> key1, NativeSlice<byte> key2)
        {
            if (key1.Length != key2.Length)
                return false;

            for (int i = 0; i < key1.Length; i++)
            {
                if (key1[i] != key2[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Compare key slice with string (case-sensitive)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool KeyEquals(NativeSlice<byte> key, string str)
        {
            if (key.Length != str.Length)
                return false;

            for (int i = 0; i < key.Length; i++)
            {
                if (key[i] != (byte)str[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Hash a string for key lookup
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint HashKey(string key)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(key);
            var nativeBytes = new NativeArray<byte>(bytes, Allocator.Temp);
            try
            {
                return FastHasher.HashFNV1a32(nativeBytes.GetSubArray(0, bytes.Length));
            }
            finally
            {
                nativeBytes.Dispose();
            }
        }
    }
}