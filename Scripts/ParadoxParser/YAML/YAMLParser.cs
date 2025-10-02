using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ParadoxParser.YAML
{
    /// <summary>
    /// High-performance YAML parser for Paradox localization files
    /// Converts YAML tokens into standard parse structure
    /// </summary>
    public static class YAMLParser
    {
        /// <summary>
        /// YAML parsing result
        /// </summary>
        public struct YAMLParseResult
        {
            public bool Success;
            public NativeHashMap<uint, FixedString512Bytes> LocalizationEntries;
            public FixedString64Bytes LanguageCode; // e.g., "l_english"
            public int EntriesFound;
            public ErrorSeverity HighestError;

            public void Dispose()
            {
                if (LocalizationEntries.IsCreated)
                    LocalizationEntries.Dispose();
            }
        }

        /// <summary>
        /// Parse YAML localization file (public wrapper)
        /// </summary>
        public static YAMLParseResult ParseYAML(
            NativeSlice<byte> yamlData,
            NativeSlice<YAMLTokenizer.YAMLToken> tokens,
            Allocator allocator)
        {
            unsafe
            {
                byte* dataPtr = (byte*)NativeSliceUnsafeUtility.GetUnsafeReadOnlyPtr(yamlData);
                YAMLTokenizer.YAMLToken* tokensPtr = (YAMLTokenizer.YAMLToken*)NativeSliceUnsafeUtility.GetUnsafeReadOnlyPtr(tokens);
                return ParseUnsafe(dataPtr, yamlData.Length, tokensPtr, tokens.Length, allocator);
            }
        }

        /// <summary>
        /// Core parse function
        /// </summary>
        private static unsafe YAMLParseResult ParseUnsafe(
            byte* yamlDataPtr,
            int yamlDataLength,
            YAMLTokenizer.YAMLToken* tokensPtr,
            int tokensLength,
            Allocator allocator)
        {
            var result = new YAMLParseResult
            {
                LocalizationEntries = new NativeHashMap<uint, FixedString512Bytes>(100, allocator),
                Success = false,
                EntriesFound = 0
            };

            if (tokensLength == 0)
                return result;

            byte* dataPtr = yamlDataPtr;
            int tokenIndex = 0;

            // Find language block (e.g., "l_english:")
            while (tokenIndex < tokensLength)
            {
                var token = tokensPtr[tokenIndex];

                    if (token.Type == YAMLTokenizer.YAMLTokenType.Identifier)
                    {
                        // Extract language code
                        var languageStr = ExtractString(dataPtr, token.StartOffset, token.Length);

                        // Check if this looks like a language identifier (starts with "l_")
                        if (languageStr.Length > 2 && languageStr[0] == 'l' && languageStr[1] == '_')
                        {
                            // Convert FixedString512Bytes to FixedString64Bytes
                            result.LanguageCode = new FixedString64Bytes();
                            for (int i = 0; i < Math.Min(languageStr.Length, 63); i++)
                            {
                                result.LanguageCode.Append(languageStr[i]);
                            }
                            tokenIndex++;

                            // Expect colon after language identifier
                            if (tokenIndex < tokensLength && tokensPtr[tokenIndex].Type == YAMLTokenizer.YAMLTokenType.Colon)
                            {
                                tokenIndex++; // Skip colon
                                break; // Found language block
                            }
                        }
                    }
                    tokenIndex++;
                }

                if (result.LanguageCode.Length == 0)
                {
                    // No language block found
                    return result;
                }

                // Parse localization entries
                while (tokenIndex < tokensLength)
                {
                    var token = tokensPtr[tokenIndex];

                    // Skip newlines and indentation
                    if (token.Type == YAMLTokenizer.YAMLTokenType.Newline ||
                        token.Type == YAMLTokenizer.YAMLTokenType.Indent)
                    {
                        tokenIndex++;
                        continue;
                    }

                    // End of file
                    if (token.Type == YAMLTokenizer.YAMLTokenType.EndOfFile)
                        break;

                    // Parse localization entry: IDENTIFIER = "STRING"
                    if (token.Type == YAMLTokenizer.YAMLTokenType.Identifier)
                    {
                        uint keyHash = token.StringHash;
                        tokenIndex++;

                        // Skip any whitespace/colons between key and value
                        while (tokenIndex < tokensLength)
                        {
                            var nextToken = tokensPtr[tokenIndex];
                            if (nextToken.Type == YAMLTokenizer.YAMLTokenType.String)
                                break;
                            if (nextToken.Type == YAMLTokenizer.YAMLTokenType.EndOfFile ||
                                nextToken.Type == YAMLTokenizer.YAMLTokenType.Newline)
                                break;
                            tokenIndex++;
                        }

                        // Extract string value
                        if (tokenIndex < tokensLength && tokensPtr[tokenIndex].Type == YAMLTokenizer.YAMLTokenType.String)
                        {
                            var stringToken = tokensPtr[tokenIndex];
                            var value = ExtractQuotedString(dataPtr, stringToken.StartOffset, stringToken.Length);

                            // Store the localization entry
                            result.LocalizationEntries.TryAdd(keyHash, value);
                            result.EntriesFound++;

                            tokenIndex++;
                        }
                    }
                    else
                    {
                        // Unknown token, skip it
                        tokenIndex++;
                    }
                }

                result.Success = true;
                return result;
        }

        /// <summary>
        /// Extract string from byte data
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe FixedString512Bytes ExtractString(byte* data, int offset, int length)
        {
            var result = new FixedString512Bytes();
            int copyLength = Math.Min(length, 511); // Leave room for null terminator

            for (int i = 0; i < copyLength; i++)
            {
                result.Append((char)data[offset + i]);
            }

            return result;
        }

        /// <summary>
        /// Extract quoted string, removing quotes and handling escapes
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe FixedString512Bytes ExtractQuotedString(byte* data, int offset, int length)
        {
            var result = new FixedString512Bytes();

            if (length < 2 || data[offset] != '"')
                return result;

            // Skip opening quote and process until closing quote
            int pos = offset + 1;
            int end = offset + length - 1;

            while (pos < end)
            {
                byte b = data[pos];

                if (b == '\\' && pos + 1 < end)
                {
                    // Handle escape sequences
                    byte nextByte = data[pos + 1];
                    switch (nextByte)
                    {
                        case (byte)'"':
                            result.Append('"');
                            break;
                        case (byte)'\\':
                            result.Append('\\');
                            break;
                        case (byte)'n':
                            result.Append('\n');
                            break;
                        case (byte)'r':
                            result.Append('\r');
                            break;
                        case (byte)'t':
                            result.Append('\t');
                            break;
                        default:
                            // Unknown escape, keep both characters
                            result.Append((char)b);
                            result.Append((char)nextByte);
                            break;
                    }
                    pos += 2;
                }
                else
                {
                    result.Append((char)b);
                    pos++;
                }
            }

            return result;
        }

        /// <summary>
        /// Get localized string by key hash
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetLocalizedString(YAMLParseResult parseResult, uint keyHash, out FixedString512Bytes value)
        {
            return parseResult.LocalizationEntries.TryGetValue(keyHash, out value);
        }

        /// <summary>
        /// Get localized string by key string
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetLocalizedString(YAMLParseResult parseResult, FixedString64Bytes key, out FixedString512Bytes value)
        {
            // Convert FixedString to byte array for hashing
            var keyBytes = new NativeArray<byte>(key.Length, Allocator.Temp);
            for (int i = 0; i < key.Length; i++)
            {
                keyBytes[i] = (byte)key[i];
            }

            uint keyHash = ComputeHash(keyBytes);
            bool result = parseResult.LocalizationEntries.TryGetValue(keyHash, out value);

            keyBytes.Dispose();
            return result;
        }

        /// <summary>
        /// Compute FNV-1a hash of byte array
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ComputeHash(NativeArray<byte> data)
        {
            const uint FNV_OFFSET_BASIS = 2166136261;
            const uint FNV_PRIME = 16777619;

            uint hash = FNV_OFFSET_BASIS;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= FNV_PRIME;
            }
            return hash;
        }
    }
}