using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using ParadoxParser.Core;
using ParadoxParser.Data;

namespace ParadoxParser.Tokenization
{
    /// <summary>
    /// High-performance byte-level tokenizer for Paradox files
    /// Designed for Unity's Job System and Burst compilation
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Tokenizer : IDisposable
    {
        private NativeArray<byte> m_Data;
        private NativeStringPool m_StringPool;
        private ErrorAccumulator m_ErrorAccumulator;
        private int m_Position;
        private int m_Line;
        private int m_Column;
        private int m_LastLineStart;
        private bool m_IsCreated;
        private bool m_IsDisposed;

        /// <summary>
        /// Current position in byte stream
        /// </summary>
        public int Position => m_Position;

        /// <summary>
        /// Current line number (1-based)
        /// </summary>
        public int Line => m_Line;

        /// <summary>
        /// Current column number (1-based)
        /// </summary>
        public int Column => m_Column;

        /// <summary>
        /// Total data length
        /// </summary>
        public int Length => m_Data.IsCreated ? m_Data.Length : 0;

        /// <summary>
        /// Check if at end of data
        /// </summary>
        public bool IsAtEnd => m_Position >= Length;

        /// <summary>
        /// Check if tokenizer is created and valid
        /// </summary>
        public bool IsCreated => m_IsCreated && !m_IsDisposed;

        /// <summary>
        /// Create new tokenizer
        /// </summary>
        public Tokenizer(NativeArray<byte> data, NativeStringPool stringPool, ErrorAccumulator errorAccumulator)
        {
            m_Data = data;
            m_StringPool = stringPool;
            m_ErrorAccumulator = errorAccumulator;
            m_Position = 0;
            m_Line = 1;
            m_Column = 1;
            m_LastLineStart = 0;
            m_IsCreated = true;
            m_IsDisposed = false;
        }

        /// <summary>
        /// Tokenize the entire data stream
        /// </summary>
        public TokenStream Tokenize(Allocator allocator)
        {
            if (!IsCreated)
                return default;

            // Estimate token count (roughly 1 token per 10 bytes)
            int estimatedTokens = math.max(1024, Length / 10);
            var stream = new TokenStream(estimatedTokens, allocator);

            while (!IsAtEnd)
            {
                var token = NextToken();
                if (!stream.TryAddToken(token))
                {
                    // Stream is full, could resize or report error
                    m_ErrorAccumulator.TryAddError(ErrorCode.InternalError, ErrorSeverity.Error, m_Line, m_Column, "Token stream capacity exceeded");
                    break;
                }

                if (token.Type == TokenType.EndOfFile)
                    break;
            }

            stream.Complete();
            return stream;
        }

        /// <summary>
        /// Get next token from the stream
        /// </summary>
        public Token NextToken()
        {
            if (!IsCreated || IsAtEnd)
                return CreateEndOfFileToken();

            // Skip whitespace but track position
            SkipWhitespace();

            if (IsAtEnd)
                return CreateEndOfFileToken();

            int tokenStart = m_Position;
            int tokenLine = m_Line;
            int tokenColumn = m_Column;

            byte currentByte = PeekByte();

            // Comments
            if (currentByte == (byte)'#')
            {
                return TokenizeComment(tokenStart, tokenLine, tokenColumn);
            }

            // String literals
            if (currentByte == (byte)'"')
            {
                return TokenizeString(tokenStart, tokenLine, tokenColumn);
            }

            // Numbers (including negative)
            if (IsDigit(currentByte) || (currentByte == (byte)'-' && IsDigit(PeekByte(1))))
            {
                return TokenizeNumber(tokenStart, tokenLine, tokenColumn);
            }

            // Dates (YYYY.MM.DD format)
            if (IsDigit(currentByte) && CouldBeDate())
            {
                var numberToken = TokenizeNumber(tokenStart, tokenLine, tokenColumn);
                if (numberToken.Type == TokenType.Number && IsDateFormat(numberToken))
                {
                    return Token.Create(TokenType.Date, numberToken.StartPosition, numberToken.Length, numberToken.Line, numberToken.Column);
                }
                return numberToken;
            }

            // Multi-character operators
            var operatorToken = TryTokenizeOperator(tokenStart, tokenLine, tokenColumn);
            if (operatorToken.Type != TokenType.Invalid)
                return operatorToken;

            // Single character tokens
            var singleCharToken = TryTokenizeSingleChar(tokenStart, tokenLine, tokenColumn);
            if (singleCharToken.Type != TokenType.Invalid)
                return singleCharToken;

            // Identifiers and keywords
            if (IsIdentifierStart(currentByte))
            {
                return TokenizeIdentifier(tokenStart, tokenLine, tokenColumn);
            }

            // Unknown character
            AdvanceByte();
            m_ErrorAccumulator.TryAddError(ErrorCode.ParseUnexpectedToken, ErrorSeverity.Warning, tokenLine, tokenColumn, $"Unexpected character: {(char)currentByte}");
            return Token.Create(TokenType.Unknown, tokenStart, 1, tokenLine, tokenColumn);
        }

        /// <summary>
        /// Peek at byte at current position + offset
        /// </summary>
        private byte PeekByte(int offset = 0)
        {
            int pos = m_Position + offset;
            if (pos < 0 || pos >= Length)
                return 0;
            return m_Data[pos];
        }

        /// <summary>
        /// Advance position by one byte and update line/column tracking
        /// </summary>
        private byte AdvanceByte()
        {
            if (IsAtEnd)
                return 0;

            byte b = m_Data[m_Position];
            m_Position++;

            if (b == (byte)'\n')
            {
                m_Line++;
                m_Column = 1;
                m_LastLineStart = m_Position;
            }
            else if (b != (byte)'\r') // Don't increment column for \r
            {
                m_Column++;
            }

            return b;
        }

        /// <summary>
        /// Skip whitespace characters
        /// </summary>
        private void SkipWhitespace()
        {
            while (!IsAtEnd)
            {
                byte b = PeekByte();
                if (!IsWhitespace(b))
                    break;
                AdvanceByte();
            }
        }

        /// <summary>
        /// Check if byte is whitespace
        /// </summary>
        private static bool IsWhitespace(byte b)
        {
            return b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';
        }

        /// <summary>
        /// Check if byte is a digit
        /// </summary>
        private static bool IsDigit(byte b)
        {
            return b >= (byte)'0' && b <= (byte)'9';
        }

        /// <summary>
        /// Check if byte can start an identifier
        /// </summary>
        private static bool IsIdentifierStart(byte b)
        {
            return (b >= (byte)'a' && b <= (byte)'z') ||
                   (b >= (byte)'A' && b <= (byte)'Z') ||
                   b == (byte)'_' || b == (byte)'@';
        }

        /// <summary>
        /// Check if byte can continue an identifier
        /// </summary>
        private static bool IsIdentifierContinue(byte b)
        {
            return IsIdentifierStart(b) || IsDigit(b) || b == (byte)'-';
        }

        /// <summary>
        /// Check if current position could be start of a date
        /// </summary>
        private bool CouldBeDate()
        {
            // Look for pattern: YYYY.MM.DD
            if (m_Position + 9 >= Length)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (!IsDigit(PeekByte(i)))
                    return false;
            }

            return PeekByte(4) == (byte)'.';
        }

        /// <summary>
        /// Check if a number token represents a date format
        /// </summary>
        private bool IsDateFormat(Token numberToken)
        {
            if (numberToken.Length != 10) // YYYY.MM.DD = 10 chars
                return false;

            // Check for dots in correct positions
            return PeekByte(numberToken.StartPosition + 4 - m_Position) == (byte)'.' &&
                   PeekByte(numberToken.StartPosition + 7 - m_Position) == (byte)'.';
        }

        /// <summary>
        /// Tokenize a comment
        /// </summary>
        private Token TokenizeComment(int start, int line, int column)
        {
            AdvanceByte(); // Skip '#'

            while (!IsAtEnd && PeekByte() != (byte)'\n' && PeekByte() != (byte)'\r')
            {
                AdvanceByte();
            }

            return Token.Create(TokenType.Comment, start, m_Position - start, line, column);
        }

        /// <summary>
        /// Tokenize a string literal
        /// </summary>
        private Token TokenizeString(int start, int line, int column)
        {
            AdvanceByte(); // Skip opening quote

            bool hasEscape = false;
            while (!IsAtEnd)
            {
                byte b = PeekByte();
                if (b == (byte)'"')
                {
                    AdvanceByte(); // Skip closing quote
                    break;
                }
                if (b == (byte)'\\')
                {
                    hasEscape = true;
                    AdvanceByte(); // Skip escape character
                    if (!IsAtEnd)
                        AdvanceByte(); // Skip escaped character
                }
                else
                {
                    AdvanceByte();
                }
            }

            var flags = hasEscape ? TokenFlags.IsEscaped : TokenFlags.None;
            flags |= TokenFlags.IsQuoted;

            // Intern the string value (excluding quotes)
            var stringSlice = m_Data.Slice(start + 1, m_Position - start - 2);
            uint hash = CalculateHash(stringSlice);
            int stringId = -1; // TODO: Intern with string pool

            return new Token
            {
                Type = TokenType.String,
                StartPosition = start,
                Length = m_Position - start,
                Line = line,
                Column = column,
                Hash = hash,
                StringId = stringId,
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
            if (PeekByte() == (byte)'-')
            {
                isNegative = true;
                AdvanceByte();
            }

            // Read integer part
            while (!IsAtEnd && IsDigit(PeekByte()))
            {
                AdvanceByte();
            }

            // Check for decimal point
            if (!IsAtEnd && PeekByte() == (byte)'.')
            {
                // Peek ahead to see if this is a decimal number or date
                if (!IsAtEnd && IsDigit(PeekByte(1)))
                {
                    isFloat = true;
                    AdvanceByte(); // Skip '.'

                    // Read fractional part
                    while (!IsAtEnd && IsDigit(PeekByte()))
                    {
                        AdvanceByte();
                    }
                }
            }

            var flags = TokenFlags.None;
            if (isNegative) flags |= TokenFlags.IsNegative;
            if (isFloat) flags |= TokenFlags.IsFloat;

            // TODO: Parse actual numeric value
            long numericValue = 0;

            return Token.CreateNumber(start, m_Position - start, line, column, numericValue, flags);
        }

        /// <summary>
        /// Try to tokenize multi-character operators
        /// </summary>
        private Token TryTokenizeOperator(int start, int line, int column)
        {
            byte first = PeekByte();
            byte second = PeekByte(1);

            // Two-character operators
            if (first == (byte)'>' && second == (byte)'=')
            {
                AdvanceByte(); AdvanceByte();
                return Token.Create(TokenType.GreaterEquals, start, 2, line, column);
            }
            if (first == (byte)'<' && second == (byte)'=')
            {
                AdvanceByte(); AdvanceByte();
                return Token.Create(TokenType.LessEquals, start, 2, line, column);
            }
            if (first == (byte)'!' && second == (byte)'=')
            {
                AdvanceByte(); AdvanceByte();
                return Token.Create(TokenType.NotEquals, start, 2, line, column);
            }
            if (first == (byte)'+' && second == (byte)'=')
            {
                AdvanceByte(); AdvanceByte();
                return Token.Create(TokenType.Add, start, 2, line, column);
            }
            if (first == (byte)'-' && second == (byte)'=')
            {
                AdvanceByte(); AdvanceByte();
                return Token.Create(TokenType.Subtract, start, 2, line, column);
            }
            if (first == (byte)'*' && second == (byte)'=')
            {
                AdvanceByte(); AdvanceByte();
                return Token.Create(TokenType.Multiply, start, 2, line, column);
            }
            if (first == (byte)'/' && second == (byte)'=')
            {
                AdvanceByte(); AdvanceByte();
                return Token.Create(TokenType.Divide, start, 2, line, column);
            }

            return Token.Create(TokenType.Invalid, start, 0, line, column);
        }

        /// <summary>
        /// Try to tokenize single character tokens
        /// </summary>
        private Token TryTokenizeSingleChar(int start, int line, int column)
        {
            byte b = PeekByte();
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
                AdvanceByte();
                return Token.Create(type, start, 1, line, column);
            }

            return Token.Create(TokenType.Invalid, start, 0, line, column);
        }

        /// <summary>
        /// Tokenize an identifier or keyword
        /// </summary>
        private Token TokenizeIdentifier(int start, int line, int column)
        {
            bool isVariable = PeekByte() == (byte)'@';
            if (isVariable)
                AdvanceByte(); // Skip '@'

            while (!IsAtEnd && IsIdentifierContinue(PeekByte()))
            {
                AdvanceByte();
            }

            // Calculate hash and intern string
            var slice = m_Data.Slice(start, m_Position - start);
            uint hash = CalculateHash(slice);
            int stringId = -1; // TODO: Intern with string pool

            var flags = isVariable ? TokenFlags.IsVariable : TokenFlags.None;

            // Check for boolean keywords
            if (!isVariable && IsBooleanKeyword(slice))
            {
                return new Token
                {
                    Type = TokenType.Boolean,
                    StartPosition = start,
                    Length = m_Position - start,
                    Line = line,
                    Column = column,
                    Hash = hash,
                    StringId = stringId,
                    NumericValue = slice.Length == 3 ? 1 : 0, // "yes" = 1, "no" = 0
                    Flags = flags
                };
            }

            return Token.CreateIdentifier(start, m_Position - start, line, column, hash, stringId);
        }

        /// <summary>
        /// Check if identifier is a boolean keyword (yes/no)
        /// </summary>
        private bool IsBooleanKeyword(NativeSlice<byte> slice)
        {
            if (slice.Length == 3)
            {
                return slice[0] == (byte)'y' && slice[1] == (byte)'e' && slice[2] == (byte)'s';
            }
            if (slice.Length == 2)
            {
                return slice[0] == (byte)'n' && slice[1] == (byte)'o';
            }
            return false;
        }

        /// <summary>
        /// Calculate hash for byte slice
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

        /// <summary>
        /// Create end of file token
        /// </summary>
        private Token CreateEndOfFileToken()
        {
            return Token.CreateEndOfFile(m_Position, m_Line, m_Column);
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            m_IsDisposed = true;
            m_IsCreated = false;
            // Note: We don't dispose m_Data, m_StringPool, or m_ErrorAccumulator
            // as they are owned by the caller
        }
    }
}