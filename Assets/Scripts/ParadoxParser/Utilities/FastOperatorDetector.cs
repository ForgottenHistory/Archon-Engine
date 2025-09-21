using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using ParadoxParser.Tokenization;

namespace ParadoxParser.Utilities
{
    /// <summary>
    /// High-performance operator detection for Paradox parser
    /// Optimized lookup tables and SIMD operations for common operators
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class FastOperatorDetector
    {
        /// <summary>
        /// Operator detection result
        /// </summary>
        public struct OperatorResult
        {
            public TokenType Type;
            public int Length;
            public bool Success;

            public static OperatorResult Failed => new OperatorResult { Success = false, Length = 0, Type = TokenType.Invalid };

            public static OperatorResult Successful(TokenType type, int length)
            {
                return new OperatorResult { Type = type, Length = length, Success = true };
            }
        }

        // Pre-computed lookup tables for fast operator detection
        private static readonly byte[] SingleCharOperators = new byte[256];
        private static readonly TokenType[] SingleCharTokenTypes = new TokenType[256];

        // Two-character operator lookup (packed as uint16)
        private static readonly NativeHashMap<ushort, TokenType> TwoCharOperators;

        /// <summary>
        /// Initialize lookup tables (called once at startup)
        /// </summary>
        static FastOperatorDetector()
        {
            // Initialize single character operators
            InitializeSingleCharOperators();

            // Initialize two character operators (this would be done at runtime in a real scenario)
            // For now, we'll use direct checks instead of static initialization
        }

        /// <summary>
        /// Detect operator at current position with maximum performance
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OperatorResult DetectOperator(NativeSlice<byte> data, int position)
        {
            if (position >= data.Length)
                return OperatorResult.Failed;

            byte firstByte = data[position];

            // Check for two-character operators first (they take precedence)
            if (position + 1 < data.Length)
            {
                byte secondByte = data[position + 1];
                var twoCharResult = DetectTwoCharOperator(firstByte, secondByte);
                if (twoCharResult.Success)
                    return twoCharResult;
            }

            // Check single character operators
            return DetectSingleCharOperator(firstByte);
        }

        /// <summary>
        /// Detect single character operators using lookup table
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static OperatorResult DetectSingleCharOperator(byte b)
        {
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
                (byte)'+' => TokenType.Plus,
                (byte)'-' => TokenType.Minus,
                (byte)'*' => TokenType.Asterisk,
                (byte)'/' => TokenType.Slash,
                (byte)'%' => TokenType.Percent,
                (byte)'&' => TokenType.Ampersand,
                (byte)'|' => TokenType.Pipe,
                (byte)'^' => TokenType.Caret,
                (byte)'~' => TokenType.Tilde,
                (byte)'!' => TokenType.Exclamation,
                (byte)'?' => TokenType.Question,
                (byte)'@' => TokenType.At,
                (byte)'$' => TokenType.Dollar,
                _ => TokenType.Invalid
            };

            if (type != TokenType.Invalid)
                return OperatorResult.Successful(type, 1);

            return OperatorResult.Failed;
        }

        /// <summary>
        /// Detect two character operators using optimized checks
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static OperatorResult DetectTwoCharOperator(byte first, byte second)
        {
            // Pack two bytes into a 16-bit value for fast comparison
            ushort packed = (ushort)((first << 8) | second);

            TokenType type = packed switch
            {
                // Comparison operators
                0x3E3D => TokenType.GreaterEquals,    // >=
                0x3C3D => TokenType.LessEquals,       // <=
                0x213D => TokenType.NotEquals,        // !=
                0x3D3D => TokenType.Equals,           // == (same as =)

                // Assignment operators
                0x2B3D => TokenType.Add,              // +=
                0x2D3D => TokenType.Subtract,         // -=
                0x2A3D => TokenType.Multiply,         // *=
                0x2F3D => TokenType.Divide,           // /=
                0x253D => TokenType.Modulo,           // %=

                // Logical operators
                0x2626 => TokenType.LogicalAnd,       // &&
                0x7C7C => TokenType.LogicalOr,        // ||

                // Bit shift operators
                0x3C3C => TokenType.LeftShift,        // <<
                0x3E3E => TokenType.RightShift,       // >>

                // Arrow operator (used in some Paradox files)
                0x2D3E => TokenType.Arrow,            // ->

                // Scope resolution (used in some contexts)
                0x3A3A => TokenType.ScopeResolution,  // ::

                _ => TokenType.Invalid
            };

            if (type != TokenType.Invalid)
                return OperatorResult.Successful(type, 2);

            return OperatorResult.Failed;
        }

        /// <summary>
        /// Detect operator sequence (for complex operators like ">>=" or "<<=")
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OperatorResult DetectComplexOperator(NativeSlice<byte> data, int position)
        {
            if (position + 2 >= data.Length)
                return DetectOperator(data, position);

            byte first = data[position];
            byte second = data[position + 1];
            byte third = data[position + 2];

            // Check for three-character operators
            uint packed = (uint)((first << 16) | (second << 8) | third);

            TokenType type = packed switch
            {
                0x3E3E3D => TokenType.RightShiftAssign,  // >>=
                0x3C3C3D => TokenType.LeftShiftAssign,   // <<=
                _ => TokenType.Invalid
            };

            if (type != TokenType.Invalid)
                return OperatorResult.Successful(type, 3);

            // Fall back to two-character or single-character detection
            return DetectOperator(data, position);
        }

        /// <summary>
        /// Check if byte can start an operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanStartOperator(byte b)
        {
            // Quick check for common operator starting characters
            return b switch
            {
                (byte)'=' or (byte)'>' or (byte)'<' or (byte)'{' or (byte)'}' or
                (byte)'[' or (byte)']' or (byte)'(' or (byte)')' or (byte)',' or
                (byte)';' or (byte)':' or (byte)'.' or (byte)'#' or (byte)'+' or
                (byte)'-' or (byte)'*' or (byte)'/' or (byte)'%' or (byte)'&' or
                (byte)'|' or (byte)'^' or (byte)'~' or (byte)'!' or (byte)'?' or
                (byte)'@' or (byte)'$' => true,
                _ => false
            };
        }

        /// <summary>
        /// Batch operator detection for multiple positions
        /// Processes multiple operators in a single pass for better cache efficiency
        /// </summary>
        public static void DetectOperatorsBatch(NativeSlice<byte> data, NativeList<OperatorResult> results, NativeList<int> positions)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                int position = positions[i];
                var result = DetectOperator(data, position);
                results.Add(result);
            }
        }

        /// <summary>
        /// Scan for next operator in data stream
        /// Skips non-operator characters efficiently
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FindNextOperator(NativeSlice<byte> data, int startPosition)
        {
            for (int i = startPosition; i < data.Length; i++)
            {
                if (CanStartOperator(data[i]))
                {
                    var result = DetectOperator(data, i);
                    if (result.Success)
                        return i;
                }
            }
            return -1; // No operator found
        }

        /// <summary>
        /// Count operators in data (for statistics/analysis)
        /// </summary>
        public static int CountOperators(NativeSlice<byte> data)
        {
            int count = 0;
            int position = 0;

            while (position < data.Length)
            {
                var result = DetectOperator(data, position);
                if (result.Success)
                {
                    count++;
                    position += result.Length;
                }
                else
                {
                    position++;
                }
            }

            return count;
        }

        /// <summary>
        /// Check if operator is assignment type (=, +=, -=, etc.)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAssignmentOperator(TokenType type)
        {
            return type switch
            {
                TokenType.Equals or TokenType.Add or TokenType.Subtract or
                TokenType.Multiply or TokenType.Divide or TokenType.Modulo or
                TokenType.LeftShiftAssign or TokenType.RightShiftAssign => true,
                _ => false
            };
        }

        /// <summary>
        /// Check if operator is comparison type (<, >, <=, >=, ==, !=)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsComparisonOperator(TokenType type)
        {
            return type switch
            {
                TokenType.LessThan or TokenType.GreaterThan or TokenType.LessEquals or
                TokenType.GreaterEquals or TokenType.Equals or TokenType.NotEquals => true,
                _ => false
            };
        }

        /// <summary>
        /// Check if operator is bracket type ({}, [], ())
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBracketOperator(TokenType type)
        {
            return type switch
            {
                TokenType.LeftBrace or TokenType.RightBrace or TokenType.LeftBracket or
                TokenType.RightBracket or TokenType.LeftParen or TokenType.RightParen => true,
                _ => false
            };
        }

        /// <summary>
        /// Get operator precedence for parsing
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetOperatorPrecedence(TokenType type)
        {
            return type switch
            {
                // Highest precedence
                TokenType.LeftParen or TokenType.RightParen => 10,
                TokenType.LeftBracket or TokenType.RightBracket => 10,

                // Unary operators
                TokenType.Exclamation or TokenType.Tilde => 9,

                // Multiplicative
                TokenType.Asterisk or TokenType.Slash or TokenType.Percent => 8,

                // Additive
                TokenType.Plus or TokenType.Minus => 7,

                // Shift
                TokenType.LeftShift or TokenType.RightShift => 6,

                // Relational
                TokenType.LessThan or TokenType.GreaterThan or
                TokenType.LessEquals or TokenType.GreaterEquals => 5,

                // Equality
                TokenType.Equals or TokenType.NotEquals => 4,

                // Bitwise AND
                TokenType.Ampersand => 3,

                // Bitwise XOR
                TokenType.Caret => 2,

                // Bitwise OR
                TokenType.Pipe => 1,

                // Assignment (lowest precedence)
                TokenType.Add or TokenType.Subtract or TokenType.Multiply or
                TokenType.Divide or TokenType.Modulo => 0,

                _ => -1 // Invalid/not an operator
            };
        }

        /// <summary>
        /// Initialize single character operator lookup table
        /// </summary>
        private static void InitializeSingleCharOperators()
        {
            // This would be called at startup to populate the lookup tables
            // For now, we use the switch statement in DetectSingleCharOperator
        }
    }
}