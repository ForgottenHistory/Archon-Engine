using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ParadoxParser.YAML
{
    /// <summary>
    /// Error severity levels
    /// </summary>
    public enum ErrorSeverity : byte
    {
        Info = 0,
        Warning = 1,
        Error = 2,
        Critical = 3
    }

    /// <summary>
    /// High-performance YAML tokenizer for Paradox localization files
    /// Handles YAML format natively without preprocessing
    /// Updated: Selective Burst compilation for compatible functions
    /// </summary>
    public static class YAMLTokenizer
    {
        /// <summary>
        /// YAML token types
        /// </summary>
        public enum YAMLTokenType : byte
        {
            Identifier = 0,      // PROV1, l_english
            VersionNumber = 1,   // :0, :1
            Colon = 2,          // :
            String = 3,         // "Stockholm"
            Newline = 4,        // \n
            Indent = 5,         // Leading spaces
            EndOfFile = 6
        }

        /// <summary>
        /// YAML token structure
        /// </summary>
        public struct YAMLToken
        {
            public YAMLTokenType Type;
            public int StartOffset;
            public int Length;
            public int Line;
            public int Column;
            public uint StringHash;      // For identifiers
            public int VersionNumber;    // For version tokens
        }

        /// <summary>
        /// YAML tokenization result
        /// </summary>
        public struct YAMLTokenizeResult
        {
            public bool Success;
            public int TokensGenerated;
            public int BytesProcessed;
            public int Line;
            public int Column;
            public ErrorSeverity HighestError;
        }

        /// <summary>
        /// Tokenize YAML data into tokens (public wrapper)
        /// </summary>
        public static YAMLTokenizeResult TokenizeYAML(
            NativeSlice<byte> yamlData,
            NativeList<YAMLToken> tokens)
        {
            unsafe
            {
                byte* dataPtr = (byte*)yamlData.GetUnsafePtr();
                return TokenizeUnsafe(dataPtr, yamlData.Length, tokens);
            }
        }

        /// <summary>
        /// Core tokenization function
        /// </summary>
        private static unsafe YAMLTokenizeResult TokenizeUnsafe(
            byte* yamlDataPtr,
            int yamlDataLength,
            NativeList<YAMLToken> tokens)
        {
            if (!tokens.IsCreated || yamlDataLength == 0)
            {
                return new YAMLTokenizeResult { Success = false };
            }

            tokens.Clear();

            byte* dataPtr = yamlDataPtr;
            int length = yamlDataLength;
                int pos = 0;
                int line = 1;
                int column = 1;
                int lineStart = 0;

                while (pos < length)
                {
                    // Skip UTF-8 BOM if present
                    if (pos == 0 && length >= 3 && dataPtr[0] == 0xEF && dataPtr[1] == 0xBB && dataPtr[2] == 0xBF)
                    {
                        pos += 3;
                        column += 3;
                        continue;
                    }

                    byte currentByte = dataPtr[pos];

                    // Handle newlines
                    if (currentByte == '\n')
                    {
                        tokens.Add(new YAMLToken
                        {
                            Type = YAMLTokenType.Newline,
                            StartOffset = pos,
                            Length = 1,
                            Line = line,
                            Column = column
                        });

                        pos++;
                        line++;
                        column = 1;
                        lineStart = pos;
                        continue;
                    }

                    // Handle carriage return (Windows line endings)
                    if (currentByte == '\r')
                    {
                        pos++;
                        if (pos < length && dataPtr[pos] == '\n')
                        {
                            // CRLF - treat as single newline
                            tokens.Add(new YAMLToken
                            {
                                Type = YAMLTokenType.Newline,
                                StartOffset = pos - 1,
                                Length = 2,
                                Line = line,
                                Column = column
                            });
                            pos++;
                        }
                        line++;
                        column = 1;
                        lineStart = pos;
                        continue;
                    }

                    // Handle leading spaces (indentation)
                    if (currentByte == ' ' && column == 1)
                    {
                        int indentStart = pos;
                        while (pos < length && dataPtr[pos] == ' ')
                        {
                            pos++;
                            column++;
                        }

                        tokens.Add(new YAMLToken
                        {
                            Type = YAMLTokenType.Indent,
                            StartOffset = indentStart,
                            Length = pos - indentStart,
                            Line = line,
                            Column = 1
                        });
                        continue;
                    }

                    // Skip other whitespace
                    if (currentByte == ' ' || currentByte == '\t')
                    {
                        pos++;
                        column++;
                        continue;
                    }

                    // Handle quoted strings
                    if (currentByte == '"')
                    {
                        int stringStart = pos;
                        pos++; // Skip opening quote
                        column++;

                        while (pos < length && dataPtr[pos] != '"')
                        {
                            if (dataPtr[pos] == '\\' && pos + 1 < length)
                            {
                                // Skip escaped character
                                pos += 2;
                                column += 2;
                            }
                            else
                            {
                                pos++;
                                column++;
                            }
                        }

                        if (pos < length && dataPtr[pos] == '"')
                        {
                            pos++; // Skip closing quote
                            column++;
                        }

                        tokens.Add(new YAMLToken
                        {
                            Type = YAMLTokenType.String,
                            StartOffset = stringStart,
                            Length = pos - stringStart,
                            Line = line,
                            Column = column - (pos - stringStart)
                        });
                        continue;
                    }

                    // Handle colon
                    if (currentByte == ':')
                    {
                        tokens.Add(new YAMLToken
                        {
                            Type = YAMLTokenType.Colon,
                            StartOffset = pos,
                            Length = 1,
                            Line = line,
                            Column = column
                        });

                        pos++;
                        column++;
                        continue;
                    }

                    // Handle identifiers and version numbers
                    if (IsIdentifierStart(currentByte))
                    {
                        int identifierStart = pos;

                        // Read identifier part
                        while (pos < length && IsIdentifierChar(dataPtr[pos]))
                        {
                            pos++;
                            column++;
                        }

                        // Check if followed by version number (:0, :1, etc.)
                        if (pos < length && dataPtr[pos] == ':' && pos + 1 < length && IsDigit(dataPtr[pos + 1]))
                        {
                            // Parse version number
                            pos++; // Skip :
                            column++;
                            int versionStart = pos;

                            while (pos < length && IsDigit(dataPtr[pos]))
                            {
                                pos++;
                                column++;
                            }

                            // Extract version number
                            int version = 0;
                            for (int i = versionStart; i < pos; i++)
                            {
                                version = version * 10 + (dataPtr[i] - '0');
                            }

                            tokens.Add(new YAMLToken
                            {
                                Type = YAMLTokenType.Identifier,
                                StartOffset = identifierStart,
                                Length = versionStart - identifierStart - 1, // Exclude the :
                                Line = line,
                                Column = column - (pos - identifierStart),
                                StringHash = ComputeHashUnsafe(dataPtr + identifierStart, versionStart - identifierStart - 1),
                                VersionNumber = version
                            });
                        }
                        else
                        {
                            // Regular identifier without version
                            tokens.Add(new YAMLToken
                            {
                                Type = YAMLTokenType.Identifier,
                                StartOffset = identifierStart,
                                Length = pos - identifierStart,
                                Line = line,
                                Column = column - (pos - identifierStart),
                                StringHash = ComputeHashUnsafe(dataPtr + identifierStart, pos - identifierStart),
                                VersionNumber = -1
                            });
                        }
                        continue;
                    }

                    // Unknown character - skip it
                    pos++;
                    column++;
                }

                // Add EOF token
                tokens.Add(new YAMLToken
                {
                    Type = YAMLTokenType.EndOfFile,
                    StartOffset = pos,
                    Length = 0,
                    Line = line,
                    Column = column
                });

            return new YAMLTokenizeResult
            {
                Success = true,
                TokensGenerated = tokens.Length,
                BytesProcessed = pos,
                Line = line,
                Column = column,
                HighestError = ErrorSeverity.Info
            };
        }

        /// <summary>
        /// Check if byte can start an identifier
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsIdentifierStart(byte b)
        {
            return (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || b == '_';
        }

        /// <summary>
        /// Check if byte can be part of identifier
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsIdentifierChar(byte b)
        {
            return (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') ||
                   (b >= '0' && b <= '9') || b == '_';
        }

        /// <summary>
        /// Check if byte is a digit
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDigit(byte b)
        {
            return b >= '0' && b <= '9';
        }

        /// <summary>
        /// Compute FNV-1a hash from unsafe pointer
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe uint ComputeHashUnsafe(byte* data, int length)
        {
            const uint FNV_OFFSET_BASIS = 2166136261;
            const uint FNV_PRIME = 16777619;

            uint hash = FNV_OFFSET_BASIS;
            for (int i = 0; i < length; i++)
            {
                hash ^= data[i];
                hash *= FNV_PRIME;
            }
            return hash;
        }
    }
}