using System;
using System.Runtime.InteropServices;
using Unity.Collections;

namespace ParadoxParser.Tokenization
{
    /// <summary>
    /// Represents a single token in the parsed Paradox file
    /// Optimized for Unity's Native Collections and Burst compilation
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Token : IEquatable<Token>
    {
        /// <summary>
        /// Type of this token
        /// </summary>
        public TokenType Type;

        /// <summary>
        /// Starting position in the source data (byte offset)
        /// </summary>
        public int StartPosition;

        /// <summary>
        /// Length of the token in bytes
        /// </summary>
        public int Length;

        /// <summary>
        /// Line number where this token appears (1-based)
        /// </summary>
        public int Line;

        /// <summary>
        /// Column number where this token starts (1-based)
        /// </summary>
        public int Column;

        /// <summary>
        /// Hash of the token's string value for fast comparison
        /// Used for identifier lookup and string interning
        /// </summary>
        public uint Hash;

        /// <summary>
        /// String ID from the string pool (if applicable)
        /// -1 if not interned
        /// </summary>
        public int StringId;

        /// <summary>
        /// Numeric value for number tokens (as bits for float/int)
        /// </summary>
        public long NumericValue;

        /// <summary>
        /// Additional flags for token properties
        /// </summary>
        public TokenFlags Flags;

        /// <summary>
        /// End position in source data (convenience property)
        /// </summary>
        public int EndPosition => StartPosition + Length;

        /// <summary>
        /// Check if this token has a valid string ID
        /// </summary>
        public bool HasStringId => StringId >= 0;

        /// <summary>
        /// Check if this token represents a valid value
        /// </summary>
        public bool IsValid => Type != TokenType.Invalid && Type != TokenType.Unknown;

        /// <summary>
        /// Check if this token is end of file
        /// </summary>
        public bool IsEndOfFile => Type == TokenType.EndOfFile;

        /// <summary>
        /// Create a simple token
        /// </summary>
        public static Token Create(TokenType type, int startPosition, int length, int line, int column)
        {
            return new Token
            {
                Type = type,
                StartPosition = startPosition,
                Length = length,
                Line = line,
                Column = column,
                Hash = 0,
                StringId = -1,
                NumericValue = 0,
                Flags = TokenFlags.None
            };
        }

        /// <summary>
        /// Create a token with string value
        /// </summary>
        public static Token CreateString(int startPosition, int length, int line, int column, uint hash, int stringId = -1)
        {
            return new Token
            {
                Type = TokenType.String,
                StartPosition = startPosition,
                Length = length,
                Line = line,
                Column = column,
                Hash = hash,
                StringId = stringId,
                NumericValue = 0,
                Flags = TokenFlags.None
            };
        }

        /// <summary>
        /// Create a token with identifier value
        /// </summary>
        public static Token CreateIdentifier(int startPosition, int length, int line, int column, uint hash, int stringId = -1)
        {
            return new Token
            {
                Type = TokenType.Identifier,
                StartPosition = startPosition,
                Length = length,
                Line = line,
                Column = column,
                Hash = hash,
                StringId = stringId,
                NumericValue = 0,
                Flags = TokenFlags.None
            };
        }

        /// <summary>
        /// Create a numeric token
        /// </summary>
        public static Token CreateNumber(int startPosition, int length, int line, int column, long numericValue, TokenFlags flags = TokenFlags.None)
        {
            return new Token
            {
                Type = TokenType.Number,
                StartPosition = startPosition,
                Length = length,
                Line = line,
                Column = column,
                Hash = 0,
                StringId = -1,
                NumericValue = numericValue,
                Flags = flags
            };
        }

        /// <summary>
        /// Create end of file token
        /// </summary>
        public static Token CreateEndOfFile(int position, int line, int column)
        {
            return new Token
            {
                Type = TokenType.EndOfFile,
                StartPosition = position,
                Length = 0,
                Line = line,
                Column = column,
                Hash = 0,
                StringId = -1,
                NumericValue = 0,
                Flags = TokenFlags.None
            };
        }

        public bool Equals(Token other)
        {
            return Type == other.Type &&
                   StartPosition == other.StartPosition &&
                   Length == other.Length &&
                   Hash == other.Hash;
        }

        public override bool Equals(object obj)
        {
            return obj is Token other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Type, StartPosition, Length, Hash);
        }

        public override string ToString()
        {
            return $"{Type.GetDisplayName()} at {Line}:{Column} (pos {StartPosition}-{EndPosition})";
        }
    }

    /// <summary>
    /// Flags for additional token properties
    /// </summary>
    [Flags]
    public enum TokenFlags : byte
    {
        None = 0,
        IsFloat = 1 << 0,          // Number is floating point
        IsNegative = 1 << 1,       // Number is negative
        IsHex = 1 << 2,            // Number is hexadecimal
        IsQuoted = 1 << 3,         // String is quoted
        IsEscaped = 1 << 4,        // String contains escape sequences
        IsMultiline = 1 << 5,      // Token spans multiple lines
        IsReserved = 1 << 6,       // Identifier is a reserved keyword
        IsVariable = 1 << 7        // Identifier is a variable reference (@var)
    }
}