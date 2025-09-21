using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using ParadoxParser.Core;
using ParadoxParser.Data;
using ParadoxParser.Tokenization;

namespace ParadoxParser.Jobs
{
    /// <summary>
    /// Parallel tokenization job for large files
    /// Splits data into chunks and processes them concurrently
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast)]
    public struct ParallelTokenizeJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Data;
        [ReadOnly] public NativeArray<ChunkInfo> ChunkInfos;
        [ReadOnly] public NativeStringPool StringPool;

        [WriteOnly] public NativeArray<TokenChunk> Results;

        // Error accumulation per chunk
        [NativeDisableParallelForRestriction]
        public NativeArray<int> ErrorCounts;

        public void Execute(int index)
        {
            var chunkInfo = ChunkInfos[index];
            var chunkData = Data.Slice(chunkInfo.StartOffset, chunkInfo.Length);

            // Create temporary collections for this chunk
            var tokens = new NativeList<Token>(chunkInfo.EstimatedTokens, Allocator.Temp);

            try
            {
                // Initialize tokenizer for this chunk
                var tokenizer = new ChunkTokenizer
                {
                    Data = chunkData,
                    Position = 0,
                    Line = chunkInfo.StartLine,
                    Column = chunkInfo.StartColumn,
                    ChunkStartOffset = chunkInfo.StartOffset
                };

                // Tokenize the chunk
                int errorCount = 0;
                while (tokenizer.Position < chunkData.Length && tokens.Length < chunkInfo.EstimatedTokens * 2)
                {
                    var token = tokenizer.NextToken(StringPool, out bool hasError);
                    if (hasError) errorCount++;

                    if (token.Type == TokenType.EndOfFile)
                        break;

                    tokens.Add(token);
                }

                // Store results
                var result = new TokenChunk
                {
                    ChunkIndex = index,
                    StartOffset = chunkInfo.StartOffset,
                    EndOffset = chunkInfo.StartOffset + chunkInfo.Length,
                    TokenCount = tokens.Length,
                    StartLine = chunkInfo.StartLine,
                    StartColumn = chunkInfo.StartColumn,
                    EndLine = tokenizer.Line,
                    EndColumn = tokenizer.Column,
                    ErrorCount = errorCount
                };

                // Copy tokens to result (this will be merged later)
                result.FirstTokenHash = tokens.Length > 0 ? tokens[0].Hash : 0;
                result.LastTokenHash = tokens.Length > 0 ? tokens[tokens.Length - 1].Hash : 0;

                Results[index] = result;
                ErrorCounts[index] = errorCount;
            }
            finally
            {
                if (tokens.IsCreated) tokens.Dispose();
            }
        }
    }

    /// <summary>
    /// Information about a chunk to be processed
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ChunkInfo
    {
        public int StartOffset;
        public int Length;
        public int StartLine;
        public int StartColumn;
        public int EstimatedTokens;
    }

    /// <summary>
    /// Result of processing a chunk
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TokenChunk
    {
        public int ChunkIndex;
        public int StartOffset;
        public int EndOffset;
        public int TokenCount;
        public int StartLine;
        public int StartColumn;
        public int EndLine;
        public int EndColumn;
        public int ErrorCount;
        public uint FirstTokenHash;
        public uint LastTokenHash;
    }

    /// <summary>
    /// Simplified tokenizer for chunk processing
    /// Optimized for parallel execution
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ChunkTokenizer
    {
        public NativeSlice<byte> Data;
        public int Position;
        public int Line;
        public int Column;
        public int ChunkStartOffset;

        /// <summary>
        /// Get next token from current position
        /// </summary>
        public Token NextToken(NativeStringPool stringPool, out bool hasError)
        {
            hasError = false;

            if (Position >= Data.Length)
                return CreateEndOfFileToken();

            // Skip whitespace
            SkipWhitespace();

            if (Position >= Data.Length)
                return CreateEndOfFileToken();

            int tokenStart = Position;
            int tokenLine = Line;
            int tokenColumn = Column;

            byte currentByte = Data[Position];

            // Comments
            if (currentByte == (byte)'#')
                return TokenizeComment(tokenStart, tokenLine, tokenColumn);

            // String literals
            if (currentByte == (byte)'\"')
                return TokenizeString(tokenStart, tokenLine, tokenColumn, out hasError);

            // Numbers
            if (IsDigit(currentByte) || (currentByte == (byte)'-' && Position + 1 < Data.Length && IsDigit(Data[Position + 1])))
                return TokenizeNumber(tokenStart, tokenLine, tokenColumn);

            // Single character tokens
            var singleChar = TryTokenizeSingleChar(tokenStart, tokenLine, tokenColumn);
            if (singleChar.Type != TokenType.Invalid)
                return singleChar;

            // Identifiers
            if (IsIdentifierStart(currentByte))
                return TokenizeIdentifier(tokenStart, tokenLine, tokenColumn, stringPool);

            // Unknown character
            Position++;
            Column++;
            hasError = true;
            return Token.Create(TokenType.Unknown, ChunkStartOffset + tokenStart, 1, tokenLine, tokenColumn);
        }

        /// <summary>
        /// Skip whitespace characters and update position tracking
        /// </summary>
        private void SkipWhitespace()
        {
            while (Position < Data.Length)
            {
                byte b = Data[Position];
                if (!IsWhitespace(b))
                    break;

                AdvanceByte();
            }
        }

        /// <summary>
        /// Advance position by one byte
        /// </summary>
        private void AdvanceByte()
        {
            if (Position >= Data.Length)
                return;

            byte b = Data[Position];
            Position++;

            if (b == (byte)'\n')
            {
                Line++;
                Column = 1;
            }
            else if (b != (byte)'\r')
            {
                Column++;
            }
        }

        /// <summary>
        /// Tokenize a comment
        /// </summary>
        private Token TokenizeComment(int start, int line, int column)
        {
            AdvanceByte(); // Skip '#'

            while (Position < Data.Length && Data[Position] != (byte)'\n' && Data[Position] != (byte)'\r')
            {
                AdvanceByte();
            }

            return Token.Create(TokenType.Comment, ChunkStartOffset + start, Position - start, line, column);
        }

        /// <summary>
        /// Tokenize a string literal
        /// </summary>
        private Token TokenizeString(int start, int line, int column, out bool hasError)
        {
            hasError = false;
            AdvanceByte(); // Skip opening quote

            bool hasEscape = false;
            while (Position < Data.Length)
            {
                byte b = Data[Position];
                if (b == (byte)'\"')
                {
                    AdvanceByte(); // Skip closing quote
                    break;
                }
                if (b == (byte)'\\')
                {
                    hasEscape = true;
                    AdvanceByte(); // Skip escape character
                    if (Position < Data.Length)
                        AdvanceByte(); // Skip escaped character
                }
                else
                {
                    AdvanceByte();
                }
            }

            var flags = hasEscape ? TokenFlags.IsEscaped : TokenFlags.None;
            flags |= TokenFlags.IsQuoted;

            // Calculate hash for string content (excluding quotes)
            var stringSlice = Data.Slice(start + 1, math.max(0, Position - start - 2));
            uint hash = CalculateHash(stringSlice);

            return new Token
            {
                Type = TokenType.String,
                StartPosition = ChunkStartOffset + start,
                Length = Position - start,
                Line = line,
                Column = column,
                Hash = hash,
                StringId = -1,
                NumericValue = 0,
                Flags = flags
            };
        }

        /// <summary>
        /// Tokenize a number
        /// </summary>
        private Token TokenizeNumber(int start, int line, int column)
        {
            bool isNegative = false;
            bool isFloat = false;

            // Handle negative sign
            if (Position < Data.Length && Data[Position] == (byte)'-')
            {
                isNegative = true;
                AdvanceByte();
            }

            // Read integer part
            while (Position < Data.Length && IsDigit(Data[Position]))
            {
                AdvanceByte();
            }

            // Check for decimal point
            if (Position < Data.Length && Data[Position] == (byte)'.')
            {
                if (Position + 1 < Data.Length && IsDigit(Data[Position + 1]))
                {
                    isFloat = true;
                    AdvanceByte(); // Skip '.'

                    // Read fractional part
                    while (Position < Data.Length && IsDigit(Data[Position]))
                    {
                        AdvanceByte();
                    }
                }
            }

            var flags = TokenFlags.None;
            if (isNegative) flags |= TokenFlags.IsNegative;
            if (isFloat) flags |= TokenFlags.IsFloat;

            return Token.CreateNumber(ChunkStartOffset + start, Position - start, line, column, 0, flags);
        }

        /// <summary>
        /// Try to tokenize single character tokens
        /// </summary>
        private Token TryTokenizeSingleChar(int start, int line, int column)
        {
            if (Position >= Data.Length)
                return Token.Create(TokenType.Invalid, ChunkStartOffset + start, 0, line, column);

            byte b = Data[Position];
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
                _ => TokenType.Invalid
            };

            if (type != TokenType.Invalid)
            {
                AdvanceByte();
                return Token.Create(type, ChunkStartOffset + start, 1, line, column);
            }

            return Token.Create(TokenType.Invalid, ChunkStartOffset + start, 0, line, column);
        }

        /// <summary>
        /// Tokenize an identifier
        /// </summary>
        private Token TokenizeIdentifier(int start, int line, int column, NativeStringPool stringPool)
        {
            bool isVariable = Position < Data.Length && Data[Position] == (byte)'@';
            if (isVariable)
                AdvanceByte(); // Skip '@'

            while (Position < Data.Length && IsIdentifierContinue(Data[Position]))
            {
                AdvanceByte();
            }

            // Calculate hash
            var slice = Data.Slice(start, Position - start);
            uint hash = CalculateHash(slice);

            var flags = isVariable ? TokenFlags.IsVariable : TokenFlags.None;

            // Check for boolean keywords
            if (!isVariable && IsBooleanKeyword(slice))
            {
                return new Token
                {
                    Type = TokenType.Boolean,
                    StartPosition = ChunkStartOffset + start,
                    Length = Position - start,
                    Line = line,
                    Column = column,
                    Hash = hash,
                    StringId = -1,
                    NumericValue = slice.Length == 3 ? 1 : 0, // "yes" = 1, "no" = 0
                    Flags = flags
                };
            }

            return Token.CreateIdentifier(ChunkStartOffset + start, Position - start, line, column, hash, -1);
        }

        /// <summary>
        /// Check if identifier is a boolean keyword
        /// </summary>
        private static bool IsBooleanKeyword(NativeSlice<byte> slice)
        {
            if (slice.Length == 3)
                return slice[0] == (byte)'y' && slice[1] == (byte)'e' && slice[2] == (byte)'s';
            if (slice.Length == 2)
                return slice[0] == (byte)'n' && slice[1] == (byte)'o';
            return false;
        }

        /// <summary>
        /// Create end of file token
        /// </summary>
        private Token CreateEndOfFileToken()
        {
            return Token.CreateEndOfFile(ChunkStartOffset + Position, Line, Column);
        }

        /// <summary>
        /// Calculate FNV-1a hash for byte slice
        /// </summary>
        private static uint CalculateHash(NativeSlice<byte> slice)
        {
            uint hash = 2166136261u; // FNV-1a offset basis
            for (int i = 0; i < slice.Length; i++)
            {
                hash ^= slice[i];
                hash *= 16777619u; // FNV-1a prime
            }
            return hash;
        }

        private static bool IsWhitespace(byte b) => b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';
        private static bool IsDigit(byte b) => b >= (byte)'0' && b <= (byte)'9';
        private static bool IsIdentifierStart(byte b) => (b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'A' && b <= (byte)'Z') || b == (byte)'_' || b == (byte)'@';
        private static bool IsIdentifierContinue(byte b) => IsIdentifierStart(b) || IsDigit(b) || b == (byte)'-';
    }
}