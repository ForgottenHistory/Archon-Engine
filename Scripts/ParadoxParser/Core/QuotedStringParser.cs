using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Tokenization;

namespace ParadoxParser.Core
{
    /// <summary>
    /// Specialized parser for quoted strings in Paradox files
    /// Handles escape sequences and different quote types
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class QuotedStringParser
    {
        /// <summary>
        /// Quoted string parse result
        /// </summary>
        public struct QuotedStringResult
        {
            public bool Success;
            public NativeSlice<byte> Content;     // The unescaped content
            public int BytesConsumed;             // Including quotes
            public bool HasEscapes;               // Whether string contained escape sequences

            public static QuotedStringResult Successful(NativeSlice<byte> content, int bytesConsumed, bool hasEscapes)
            {
                return new QuotedStringResult
                {
                    Success = true,
                    Content = content,
                    BytesConsumed = bytesConsumed,
                    HasEscapes = hasEscapes
                };
            }

            public static QuotedStringResult Failed => new QuotedStringResult { Success = false };
        }

        /// <summary>
        /// Parse a quoted string from byte data
        /// Supports both single and double quotes
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static QuotedStringResult ParseQuotedString(NativeSlice<byte> data, NativeArray<byte> outputBuffer)
        {
            if (data.Length < 2)
                return QuotedStringResult.Failed;

            byte quoteChar = data[0];
            if (quoteChar != (byte)'"' && quoteChar != (byte)'\'')
                return QuotedStringResult.Failed;

            return ParseQuotedStringInternal(data, quoteChar, outputBuffer);
        }

        /// <summary>
        /// Parse quoted string with known quote character
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static QuotedStringResult ParseQuotedStringInternal(
            NativeSlice<byte> data,
            byte quoteChar,
            NativeArray<byte> outputBuffer)
        {
            int inputIndex = 1; // Skip opening quote
            int outputIndex = 0;
            bool hasEscapes = false;
            bool foundClosingQuote = false;

            while (inputIndex < data.Length && outputIndex < outputBuffer.Length)
            {
                byte currentByte = data[inputIndex];

                // Check for closing quote
                if (currentByte == quoteChar)
                {
                    foundClosingQuote = true;
                    inputIndex++; // Include closing quote in consumed count
                    break;
                }

                // Check for escape sequence
                if (currentByte == (byte)'\\' && inputIndex + 1 < data.Length)
                {
                    hasEscapes = true;
                    byte escapedChar = data[inputIndex + 1];

                    var unescapedChar = UnescapeCharacter(escapedChar);
                    if (unescapedChar.HasValue)
                    {
                        outputBuffer[outputIndex] = unescapedChar.Value;
                        outputIndex++;
                        inputIndex += 2; // Skip both \ and escaped char
                    }
                    else
                    {
                        // Unknown escape sequence - keep literal
                        outputBuffer[outputIndex] = currentByte;
                        outputIndex++;
                        inputIndex++;
                    }
                }
                else
                {
                    // Regular character
                    outputBuffer[outputIndex] = currentByte;
                    outputIndex++;
                    inputIndex++;
                }
            }

            if (!foundClosingQuote)
                return QuotedStringResult.Failed;

            // Create slice for the unescaped content
            var contentSlice = new NativeSlice<byte>(outputBuffer, 0, outputIndex);

            return QuotedStringResult.Successful(contentSlice, inputIndex, hasEscapes);
        }

        /// <summary>
        /// Unescape a character following a backslash
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte? UnescapeCharacter(byte escapedChar)
        {
            return escapedChar switch
            {
                (byte)'n' => (byte)'\n',      // Newline
                (byte)'r' => (byte)'\r',      // Carriage return
                (byte)'t' => (byte)'\t',      // Tab
                (byte)'\\' => (byte)'\\',     // Backslash
                (byte)'"' => (byte)'"',       // Double quote
                (byte)'\'' => (byte)'\'',     // Single quote
                (byte)'0' => (byte)'\0',      // Null character
                _ => null                     // Unknown escape
            };
        }

        /// <summary>
        /// Quick check if data starts with a quote character
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool StartsWithQuote(NativeSlice<byte> data)
        {
            return data.Length > 0 && (data[0] == (byte)'"' || data[0] == (byte)'\'');
        }

        /// <summary>
        /// Find the end of a quoted string (including closing quote)
        /// Returns -1 if no valid closing quote found
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FindQuotedStringEnd(NativeSlice<byte> data)
        {
            if (data.Length < 2)
                return -1;

            byte quoteChar = data[0];
            if (quoteChar != (byte)'"' && quoteChar != (byte)'\'')
                return -1;

            for (int i = 1; i < data.Length; i++)
            {
                byte currentByte = data[i];

                if (currentByte == quoteChar)
                {
                    return i + 1; // Include closing quote
                }

                // Skip escaped characters
                if (currentByte == (byte)'\\' && i + 1 < data.Length)
                {
                    i++; // Skip next character
                }
            }

            return -1; // No closing quote found
        }

        /// <summary>
        /// Parse quoted string from a token (assumes token is already identified as quoted)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static QuotedStringResult ParseFromToken(
            Token token,
            NativeSlice<byte> sourceData,
            NativeArray<byte> outputBuffer)
        {
            var tokenData = sourceData.Slice(token.StartPosition, token.Length);
            return ParseQuotedString(tokenData, outputBuffer);
        }

        /// <summary>
        /// Compare quoted string content with a regular string
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ContentEquals(QuotedStringResult quotedString, NativeSlice<byte> compareWith)
        {
            if (!quotedString.Success)
                return false;

            return KeyEquals(quotedString.Content, compareWith);
        }

        /// <summary>
        /// Compare quoted string content with another quoted string
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ContentEquals(QuotedStringResult str1, QuotedStringResult str2)
        {
            if (!str1.Success || !str2.Success)
                return false;

            return KeyEquals(str1.Content, str2.Content);
        }

        /// <summary>
        /// Helper method for byte slice comparison
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool KeyEquals(NativeSlice<byte> slice1, NativeSlice<byte> slice2)
        {
            if (slice1.Length != slice2.Length)
                return false;

            for (int i = 0; i < slice1.Length; i++)
            {
                if (slice1[i] != slice2[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Get hash code for quoted string content (for fast lookups)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetContentHash(QuotedStringResult quotedString)
        {
            if (!quotedString.Success)
                return 0;

            return Utilities.FastHasher.HashFNV1a32(quotedString.Content);
        }

        /// <summary>
        /// Check if quoted string represents a boolean value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryParseBool(QuotedStringResult quotedString, out bool result)
        {
            result = false;

            if (!quotedString.Success)
                return false;

            var content = quotedString.Content;

            // Check for "yes", "true", "1"
            if (IsBooleanTrue(content))
            {
                result = true;
                return true;
            }

            // Check for "no", "false", "0"
            if (IsBooleanFalse(content))
            {
                result = false;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Check if content represents boolean true
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsBooleanTrue(NativeSlice<byte> content)
        {
            if (content.Length == 1 && content[0] == (byte)'1')
                return true;

            if (content.Length == 3 &&
                content[0] == (byte)'y' &&
                content[1] == (byte)'e' &&
                content[2] == (byte)'s')
                return true;

            if (content.Length == 4 &&
                content[0] == (byte)'t' &&
                content[1] == (byte)'r' &&
                content[2] == (byte)'u' &&
                content[3] == (byte)'e')
                return true;

            return false;
        }

        /// <summary>
        /// Check if content represents boolean false
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsBooleanFalse(NativeSlice<byte> content)
        {
            if (content.Length == 1 && content[0] == (byte)'0')
                return true;

            if (content.Length == 2 &&
                content[0] == (byte)'n' &&
                content[1] == (byte)'o')
                return true;

            if (content.Length == 5 &&
                content[0] == (byte)'f' &&
                content[1] == (byte)'a' &&
                content[2] == (byte)'l' &&
                content[3] == (byte)'s' &&
                content[4] == (byte)'e')
                return true;

            return false;
        }

        /// <summary>
        /// Extract numeric value from quoted string
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryParseInt(QuotedStringResult quotedString, out int result)
        {
            result = 0;

            if (!quotedString.Success)
                return false;

            var parseResult = Utilities.FastNumberParser.ParseInt32(quotedString.Content);
            if (parseResult.Success)
            {
                result = parseResult.Value;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Extract float value from quoted string
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryParseFloat(QuotedStringResult quotedString, out float result)
        {
            result = 0f;

            if (!quotedString.Success)
                return false;

            var parseResult = Utilities.FastNumberParser.ParseFloat(quotedString.Content);
            if (parseResult.Success)
            {
                result = parseResult.Value;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Create a temporary buffer for string unescaping
        /// Caller is responsible for disposing
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NativeArray<byte> CreateTempBuffer(int maxSize = 1024)
        {
            return new NativeArray<byte>(maxSize, Allocator.Temp);
        }
    }
}