using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using ParadoxParser.Tokenization;
using ParadoxParser.Core;

namespace ParadoxParser.Jobs
{
    /// <summary>
    /// High-performance Burst-compiled tokenization job
    /// Processes Paradox file data into tokens using SIMD optimizations
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast)]
    public struct TokenizeJob : IJob
    {
        // Input data
        [ReadOnly] public NativeArray<byte> InputData;
        [ReadOnly] public int StartOffset;
        [ReadOnly] public int Length;

        // Output data
        [WriteOnly] public NativeArray<Token> OutputTokens;

        // Shared state for error tracking
        public NativeReference<int> TokenCount;
        public NativeReference<int> ErrorCount;
        public NativeReference<int> LineNumber;
        public NativeReference<int> ColumnNumber;

        // Configuration
        [ReadOnly] public int MaxTokens;
        [ReadOnly] public bool SkipWhitespace;
        [ReadOnly] public bool SkipComments;

        public void Execute()
        {
            // Initialize state
            int position = StartOffset;
            int endPosition = math.min(StartOffset + Length, InputData.Length);
            int tokenIndex = 0;
            int line = LineNumber.Value;
            int column = ColumnNumber.Value;
            int lastLineStart = position;
            int errorCount = 0;

            while (position < endPosition && tokenIndex < MaxTokens)
            {
                // Skip whitespace efficiently using SIMD when possible
                position = SkipWhitespaceOptimized(position, endPosition, ref line, ref column, ref lastLineStart);

                if (position >= endPosition)
                    break;

                int tokenStart = position;
                int tokenLine = line;
                int tokenColumn = column;

                var token = TokenizeNext(ref position, endPosition, tokenLine, tokenColumn, ref line, ref column, ref lastLineStart, ref errorCount);

                // Skip tokens based on configuration
                if (ShouldSkipToken(token))
                    continue;

                OutputTokens[tokenIndex] = token;
                tokenIndex++;

                if (token.Type == TokenType.EndOfFile)
                    break;
            }

            // Add EOF token if we haven't already
            if (tokenIndex < MaxTokens && (tokenIndex == 0 || OutputTokens[tokenIndex - 1].Type != TokenType.EndOfFile))
            {
                OutputTokens[tokenIndex] = Token.CreateEndOfFile(position, line, column);
                tokenIndex++;
            }

            // Update shared state
            TokenCount.Value = tokenIndex;
            ErrorCount.Value = errorCount;
            LineNumber.Value = line;
            ColumnNumber.Value = column;
        }

        /// <summary>
        /// Optimized whitespace skipping using SIMD operations where possible
        /// </summary>
        private int SkipWhitespaceOptimized(int position, int endPosition, ref int line, ref int column, ref int lastLineStart)
        {
            // Fast path: skip whitespace using optimized chunks of 8 bytes
            while (position + 8 <= endPosition)
            {
                bool allWhitespace = true;

                // Check 8 bytes at once
                for (int i = 0; i < 8; i++)
                {
                    byte b = InputData[position + i];
                    if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r' && b != (byte)'\n')
                    {
                        allWhitespace = false;
                        break;
                    }
                }

                // If all bytes are whitespace, skip the entire chunk
                if (allWhitespace)
                {
                    // Count newlines and update tracking
                    int newlines = 0;
                    int lastNewlinePos = -1;

                    for (int i = 0; i < 8; i++)
                    {
                        byte b = InputData[position + i];
                        if (b == (byte)'\n')
                        {
                            newlines++;
                            lastNewlinePos = i;
                        }
                    }

                    line += newlines;

                    if (newlines > 0)
                    {
                        lastLineStart = position + lastNewlinePos + 1;
                        column = 1;
                    }
                    else
                    {
                        column += 8;
                    }

                    position += 8;
                    continue;
                }

                // Mixed content, fall back to byte-by-byte processing
                break;
            }

            // Fallback: process remaining bytes individually
            while (position < endPosition)
            {
                byte b = InputData[position];
                if (!IsWhitespace(b))
                    break;

                if (b == (byte)'\n')
                {
                    line++;
                    column = 1;
                    lastLineStart = position + 1;
                }
                else if (b != (byte)'\r')
                {
                    column++;
                }

                position++;
            }

            return position;
        }

        /// <summary>
        /// Tokenize the next token at current position
        /// </summary>
        private Token TokenizeNext(ref int position, int endPosition, int tokenLine, int tokenColumn,
                                 ref int line, ref int column, ref int lastLineStart, ref int errorCount)
        {
            if (position >= endPosition)
                return Token.CreateEndOfFile(position, line, column);

            int tokenStart = position;
            byte currentByte = InputData[position];

            // Comments
            if (currentByte == (byte)'#')
            {
                return TokenizeComment(ref position, endPosition, tokenLine, tokenColumn, ref line, ref column, ref lastLineStart);
            }

            // String literals
            if (currentByte == (byte)'"')
            {
                return TokenizeString(ref position, endPosition, tokenLine, tokenColumn, ref line, ref column, ref errorCount);
            }

            // Numbers (including negative and dates)
            if (IsDigit(currentByte) || (currentByte == (byte)'-' && position + 1 < endPosition && IsDigit(InputData[position + 1])))
            {
                return TokenizeNumber(ref position, endPosition, tokenLine, tokenColumn, ref column);
            }

            // Multi-character operators
            var operatorToken = TryTokenizeOperator(ref position, endPosition, tokenLine, tokenColumn, ref column);
            if (operatorToken.Type != TokenType.Invalid)
                return operatorToken;

            // Single character tokens
            var singleCharToken = TryTokenizeSingleChar(ref position, endPosition, tokenLine, tokenColumn, ref column);
            if (singleCharToken.Type != TokenType.Invalid)
                return singleCharToken;

            // Identifiers and keywords
            if (IsIdentifierStart(currentByte))
            {
                return TokenizeIdentifier(ref position, endPosition, tokenLine, tokenColumn, ref column);
            }

            // Unknown character
            position++;
            if (currentByte != (byte)'\r')
                column++;

            errorCount++;
            return Token.Create(TokenType.Unknown, tokenStart, 1, tokenLine, tokenColumn);
        }

        /// <summary>
        /// Tokenize a comment efficiently
        /// </summary>
        private Token TokenizeComment(ref int position, int endPosition, int tokenLine, int tokenColumn,
                                    ref int line, ref int column, ref int lastLineStart)
        {
            int start = position;
            position++; // Skip '#'
            column++;

            // Find end of line using SIMD where possible
            int commentEnd = FindEndOfLine(position, endPosition);

            // Update position and column
            int commentLength = commentEnd - position;
            column += commentLength;
            position = commentEnd;

            return Token.Create(TokenType.Comment, start, position - start, tokenLine, tokenColumn);
        }

        /// <summary>
        /// Find end of line efficiently using SIMD
        /// </summary>
        private int FindEndOfLine(int position, int endPosition)
        {
            // Optimized search for newlines in 8-byte chunks
            while (position + 8 <= endPosition)
            {
                // Check 8 bytes at once
                for (int i = 0; i < 8; i++)
                {
                    byte b = InputData[position + i];
                    if (b == (byte)'\n' || b == (byte)'\r')
                    {
                        return position + i;
                    }
                }

                position += 8;
            }

            // Fallback for remaining bytes
            while (position < endPosition)
            {
                byte b = InputData[position];
                if (b == (byte)'\n' || b == (byte)'\r')
                    break;
                position++;
            }

            return position;
        }

        /// <summary>
        /// Tokenize string with escape sequence handling
        /// </summary>
        private Token TokenizeString(ref int position, int endPosition, int tokenLine, int tokenColumn,
                                   ref int line, ref int column, ref int errorCount)
        {
            int start = position;
            position++; // Skip opening quote
            column++;

            bool hasEscape = false;
            bool terminated = false;

            while (position < endPosition)
            {
                byte b = InputData[position];

                if (b == (byte)'"')
                {
                    position++;
                    column++;
                    terminated = true;
                    break;
                }

                if (b == (byte)'\\' && position + 1 < endPosition)
                {
                    hasEscape = true;
                    position += 2; // Skip escape and next character
                    column += 2;
                }
                else
                {
                    if (b == (byte)'\n')
                    {
                        line++;
                        column = 1;
                    }
                    else if (b != (byte)'\r')
                    {
                        column++;
                    }
                    position++;
                }
            }

            if (!terminated)
                errorCount++;

            var flags = TokenFlags.IsQuoted;
            if (hasEscape)
                flags |= TokenFlags.IsEscaped;

            // Calculate hash for string content (excluding quotes)
            uint hash = 0;
            if (position - start >= 2)
            {
                hash = CalculateHashFast(start + 1, position - start - 2);
            }

            return new Token
            {
                Type = TokenType.String,
                StartPosition = start,
                Length = position - start,
                Line = tokenLine,
                Column = tokenColumn,
                Hash = hash,
                StringId = -1,
                NumericValue = 0,
                Flags = flags
            };
        }

        /// <summary>
        /// Tokenize number with optimized parsing
        /// </summary>
        private Token TokenizeNumber(ref int position, int endPosition, int tokenLine, int tokenColumn, ref int column)
        {
            int start = position;
            bool isNegative = false;
            bool isFloat = false;
            long intValue = 0;
            double floatValue = 0.0;

            // Handle negative sign
            if (InputData[position] == (byte)'-')
            {
                isNegative = true;
                position++;
                column++;
            }

            // Parse integer part
            while (position < endPosition && IsDigit(InputData[position]))
            {
                byte digit = (byte)(InputData[position] - (byte)'0');
                intValue = intValue * 10 + digit;
                position++;
                column++;
            }

            // Check for decimal point
            if (position < endPosition && InputData[position] == (byte)'.' &&
                position + 1 < endPosition && IsDigit(InputData[position + 1]))
            {
                isFloat = true;
                position++; // Skip '.'
                column++;

                floatValue = intValue;
                double fractionalMultiplier = 0.1;

                // Parse fractional part
                while (position < endPosition && IsDigit(InputData[position]))
                {
                    byte digit = (byte)(InputData[position] - (byte)'0');
                    floatValue += digit * fractionalMultiplier;
                    fractionalMultiplier *= 0.1;
                    position++;
                    column++;
                }
            }

            // Apply negative sign
            if (isNegative)
            {
                intValue = -intValue;
                floatValue = -floatValue;
            }

            var flags = TokenFlags.None;
            if (isNegative) flags |= TokenFlags.IsNegative;
            if (isFloat) flags |= TokenFlags.IsFloat;

            // Check if this could be a date (YYYY.MM.DD format)
            bool isDate = CheckDateFormat(start, position - start);
            if (isDate)
            {
                return new Token
                {
                    Type = TokenType.Date,
                    StartPosition = start,
                    Length = position - start,
                    Line = tokenLine,
                    Column = tokenColumn,
                    Hash = 0,
                    StringId = -1,
                    NumericValue = intValue,
                    Flags = flags
                };
            }

            // Store numeric value as bits
            long numericBits = isFloat ? math.aslong(floatValue) : intValue;

            return new Token
            {
                Type = TokenType.Number,
                StartPosition = start,
                Length = position - start,
                Line = tokenLine,
                Column = tokenColumn,
                Hash = 0,
                StringId = -1,
                NumericValue = numericBits,
                Flags = flags
            };
        }

        /// <summary>
        /// Check if number matches date format YYYY.MM.DD
        /// </summary>
        private bool CheckDateFormat(int start, int length)
        {
            if (length != 10) return false; // YYYY.MM.DD = 10 characters

            if (start + 4 >= InputData.Length || InputData[start + 4] != (byte)'.')
                return false;
            if (start + 7 >= InputData.Length || InputData[start + 7] != (byte)'.')
                return false;

            // Verify all other positions are digits
            for (int i = 0; i < 4; i++)
                if (!IsDigit(InputData[start + i])) return false;
            for (int i = 5; i < 7; i++)
                if (!IsDigit(InputData[start + i])) return false;
            for (int i = 8; i < 10; i++)
                if (!IsDigit(InputData[start + i])) return false;

            return true;
        }

        /// <summary>
        /// Try to tokenize multi-character operators
        /// </summary>
        private Token TryTokenizeOperator(ref int position, int endPosition, int tokenLine, int tokenColumn, ref int column)
        {
            if (position + 1 >= endPosition)
                return Token.Create(TokenType.Invalid, position, 0, tokenLine, tokenColumn);

            byte first = InputData[position];
            byte second = InputData[position + 1];

            // Two-character operators
            TokenType type = TokenType.Invalid;

            if (first == (byte)'>' && second == (byte)'=') type = TokenType.GreaterEquals;
            else if (first == (byte)'<' && second == (byte)'=') type = TokenType.LessEquals;
            else if (first == (byte)'!' && second == (byte)'=') type = TokenType.NotEquals;
            else if (first == (byte)'+' && second == (byte)'=') type = TokenType.Add;
            else if (first == (byte)'-' && second == (byte)'=') type = TokenType.Subtract;
            else if (first == (byte)'*' && second == (byte)'=') type = TokenType.Multiply;
            else if (first == (byte)'/' && second == (byte)'=') type = TokenType.Divide;

            if (type != TokenType.Invalid)
            {
                position += 2;
                column += 2;
                return Token.Create(type, position - 2, 2, tokenLine, tokenColumn);
            }

            return Token.Create(TokenType.Invalid, position, 0, tokenLine, tokenColumn);
        }

        /// <summary>
        /// Try to tokenize single character tokens
        /// </summary>
        private Token TryTokenizeSingleChar(ref int position, int endPosition, int tokenLine, int tokenColumn, ref int column)
        {
            byte b = InputData[position];
            TokenType type = b switch
            {
                (byte)'=' => TokenType.Equals,
                (byte)'>' => TokenType.GreaterThan,
                (byte)'<' => TokenType.LessThan,
                (byte)'{' => TokenType.LeftBrace,
                (byte)'}' => TokenType.RightBrace,
                (byte)'[' => TokenType.LeftBracket,
                (byte)']' => TokenType.RightBracket,
                (byte)'(' => TokenType.LeftParen,
                (byte)')' => TokenType.RightParen,
                (byte)',' => TokenType.Comma,
                (byte)';' => TokenType.Semicolon,
                (byte)':' => TokenType.Colon,
                (byte)'.' => TokenType.Dot,
                (byte)'#' => TokenType.Hash,
                _ => TokenType.Invalid
            };

            if (type != TokenType.Invalid)
            {
                position++;
                column++;
                return Token.Create(type, position - 1, 1, tokenLine, tokenColumn);
            }

            return Token.Create(TokenType.Invalid, position, 0, tokenLine, tokenColumn);
        }

        /// <summary>
        /// Tokenize identifier or keyword
        /// </summary>
        private Token TokenizeIdentifier(ref int position, int endPosition, int tokenLine, int tokenColumn, ref int column)
        {
            int start = position;
            bool isVariable = InputData[position] == (byte)'@';

            if (isVariable)
            {
                position++;
                column++;
            }

            while (position < endPosition && IsIdentifierContinue(InputData[position]))
            {
                position++;
                column++;
            }

            // Calculate hash
            uint hash = CalculateHashFast(start, position - start);

            var flags = isVariable ? TokenFlags.IsVariable : TokenFlags.None;

            // Check for boolean keywords
            if (!isVariable && IsBooleanKeyword(start, position - start))
            {
                bool isYes = (position - start == 3); // "yes" vs "no"
                return new Token
                {
                    Type = TokenType.Boolean,
                    StartPosition = start,
                    Length = position - start,
                    Line = tokenLine,
                    Column = tokenColumn,
                    Hash = hash,
                    StringId = -1,
                    NumericValue = isYes ? 1 : 0,
                    Flags = flags
                };
            }

            return new Token
            {
                Type = TokenType.Identifier,
                StartPosition = start,
                Length = position - start,
                Line = tokenLine,
                Column = tokenColumn,
                Hash = hash,
                StringId = -1,
                NumericValue = 0,
                Flags = flags
            };
        }

        /// <summary>
        /// Check if identifier is boolean keyword
        /// </summary>
        private bool IsBooleanKeyword(int start, int length)
        {
            if (length == 3 && start + 2 < InputData.Length)
            {
                return InputData[start] == (byte)'y' &&
                       InputData[start + 1] == (byte)'e' &&
                       InputData[start + 2] == (byte)'s';
            }
            if (length == 2 && start + 1 < InputData.Length)
            {
                return InputData[start] == (byte)'n' &&
                       InputData[start + 1] == (byte)'o';
            }
            return false;
        }

        /// <summary>
        /// Fast hash calculation using FNV-1a algorithm
        /// </summary>
        private uint CalculateHashFast(int start, int length)
        {
            uint hash = 2166136261u; // FNV-1a offset basis
            int end = math.min(start + length, InputData.Length);

            for (int i = start; i < end; i++)
            {
                hash ^= InputData[i];
                hash *= 16777619u; // FNV-1a prime
            }

            return hash;
        }

        /// <summary>
        /// Check if token should be skipped based on configuration
        /// </summary>
        private bool ShouldSkipToken(Token token)
        {
            if (SkipWhitespace && token.Type.IsWhitespace())
                return true;
            if (SkipComments && token.Type == TokenType.Comment)
                return true;
            return false;
        }

        // Utility methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWhitespace(byte b) => b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDigit(byte b) => b >= (byte)'0' && b <= (byte)'9';

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsIdentifierStart(byte b) => (b >= (byte)'a' && b <= (byte)'z') ||
                                                          (b >= (byte)'A' && b <= (byte)'Z') ||
                                                          b == (byte)'_' || b == (byte)'@';

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsIdentifierContinue(byte b) => IsIdentifierStart(b) || IsDigit(b) || b == (byte)'-';
    }
}