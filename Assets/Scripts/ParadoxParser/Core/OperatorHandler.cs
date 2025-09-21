using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Tokenization;
using ParadoxParser.Utilities;

namespace ParadoxParser.Core
{
    /// <summary>
    /// Handles all Paradox operator parsing and evaluation
    /// Supports comparison, assignment, and logical operators
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class OperatorHandler
    {
        /// <summary>
        /// Parsed operator expression result
        /// </summary>
        public struct OperatorExpression
        {
            public OperatorType Type;
            public NativeSlice<byte> LeftOperand;
            public NativeSlice<byte> RightOperand;
            public bool Success;

            public static OperatorExpression Failed => new OperatorExpression { Success = false };

            public static OperatorExpression Create(OperatorType type, NativeSlice<byte> left, NativeSlice<byte> right)
            {
                return new OperatorExpression
                {
                    Type = type,
                    LeftOperand = left,
                    RightOperand = right,
                    Success = true
                };
            }
        }

        /// <summary>
        /// Types of operators in Paradox files
        /// </summary>
        public enum OperatorType : byte
        {
            // Assignment operators
            Assign = 0,         // =
            Add,                // +=  (add to existing value)
            Subtract,           // -=  (subtract from existing value)
            Multiply,           // *=  (multiply existing value)
            Divide,             // /=  (divide existing value)
            Modulo,             // %=  (modulo with existing value)

            // Comparison operators
            Equals,             // == (comparison)
            NotEquals,          // !=
            GreaterThan,        // >
            LessThan,           // <
            GreaterEquals,      // >=
            LessEquals,         // <=

            // Logical operators
            LogicalAnd,         // &&
            LogicalOr,          // ||
            LogicalNot,         // !

            // Bitwise operators (rare in Paradox but supported)
            BitwiseAnd,         // &
            BitwiseOr,          // |
            BitwiseXor,         // ^
            BitwiseNot,         // ~
            LeftShift,          // <<
            RightShift,         // >>

            // Special Paradox operators
            Append,             // Used in lists/arrays
            Replace,            // Replace existing value entirely
            Remove,             // Remove from collection

            // Scope operators
            ScopeResolution,    // :: (namespace/scope access)
            Arrow,              // -> (pointer/reference access)

            Invalid
        }

        /// <summary>
        /// Parse an operator expression from tokens
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OperatorExpression ParseOperatorExpression(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData,
            out int tokensConsumed)
        {
            tokensConsumed = 0;

            if (startIndex + 2 >= tokens.Length)
                return OperatorExpression.Failed;

            var leftToken = tokens[startIndex];
            var opToken = tokens[startIndex + 1];
            var rightToken = tokens[startIndex + 2];

            // Validate pattern: operand operator operand
            if (!IsValidOperand(leftToken) || !IsValidOperand(rightToken))
                return OperatorExpression.Failed;

            var operatorType = GetOperatorType(opToken);
            if (operatorType == OperatorType.Invalid)
                return OperatorExpression.Failed;

            var leftData = sourceData.Slice(leftToken.StartPosition, leftToken.Length);
            var rightData = sourceData.Slice(rightToken.StartPosition, rightToken.Length);

            tokensConsumed = 3;
            return OperatorExpression.Create(operatorType, leftData, rightData);
        }

        /// <summary>
        /// Get operator type from token
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OperatorType GetOperatorType(Token token)
        {
            return token.Type switch
            {
                TokenType.Equals => OperatorType.Assign,
                TokenType.Add => OperatorType.Add,
                TokenType.Subtract => OperatorType.Subtract,
                TokenType.Multiply => OperatorType.Multiply,
                TokenType.Divide => OperatorType.Divide,
                TokenType.Modulo => OperatorType.Modulo,
                TokenType.NotEquals => OperatorType.NotEquals,
                TokenType.GreaterThan => OperatorType.GreaterThan,
                TokenType.LessThan => OperatorType.LessThan,
                TokenType.GreaterEquals => OperatorType.GreaterEquals,
                TokenType.LessEquals => OperatorType.LessEquals,
                TokenType.LogicalAnd => OperatorType.LogicalAnd,
                TokenType.LogicalOr => OperatorType.LogicalOr,
                TokenType.Exclamation => OperatorType.LogicalNot,
                TokenType.Ampersand => OperatorType.BitwiseAnd,
                TokenType.Pipe => OperatorType.BitwiseOr,
                TokenType.Caret => OperatorType.BitwiseXor,
                TokenType.Tilde => OperatorType.BitwiseNot,
                TokenType.LeftShift => OperatorType.LeftShift,
                TokenType.RightShift => OperatorType.RightShift,
                TokenType.ScopeResolution => OperatorType.ScopeResolution,
                TokenType.Arrow => OperatorType.Arrow,
                _ => OperatorType.Invalid
            };
        }

        /// <summary>
        /// Check if token can be used as an operand
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidOperand(Token token)
        {
            return token.Type switch
            {
                TokenType.Identifier or TokenType.Number or TokenType.String or
                TokenType.Date or TokenType.Boolean => true,
                _ => false
            };
        }

        /// <summary>
        /// Evaluate a comparison operator with two values
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EvaluateComparison(OperatorType op, NativeSlice<byte> left, NativeSlice<byte> right)
        {
            return op switch
            {
                OperatorType.Equals => AreEqual(left, right),
                OperatorType.NotEquals => !AreEqual(left, right),
                OperatorType.GreaterThan => CompareNumeric(left, right) > 0,
                OperatorType.LessThan => CompareNumeric(left, right) < 0,
                OperatorType.GreaterEquals => CompareNumeric(left, right) >= 0,
                OperatorType.LessEquals => CompareNumeric(left, right) <= 0,
                _ => false
            };
        }

        /// <summary>
        /// Check if operator is a comparison operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsComparisonOperator(OperatorType op)
        {
            return op switch
            {
                OperatorType.Equals or OperatorType.NotEquals or OperatorType.GreaterThan or
                OperatorType.LessThan or OperatorType.GreaterEquals or OperatorType.LessEquals => true,
                _ => false
            };
        }

        /// <summary>
        /// Check if operator is an assignment operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAssignmentOperator(OperatorType op)
        {
            return op switch
            {
                OperatorType.Assign or OperatorType.Add or OperatorType.Subtract or
                OperatorType.Multiply or OperatorType.Divide or OperatorType.Modulo => true,
                _ => false
            };
        }

        /// <summary>
        /// Check if operator is a logical operator
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLogicalOperator(OperatorType op)
        {
            return op switch
            {
                OperatorType.LogicalAnd or OperatorType.LogicalOr or OperatorType.LogicalNot => true,
                _ => false
            };
        }

        /// <summary>
        /// Get operator precedence for parsing order
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetPrecedence(OperatorType op)
        {
            return op switch
            {
                // Highest precedence
                OperatorType.LogicalNot or OperatorType.BitwiseNot => 10,

                // Multiplicative
                OperatorType.Multiply or OperatorType.Divide or OperatorType.Modulo => 8,

                // Additive
                OperatorType.Add or OperatorType.Subtract => 7,

                // Shift
                OperatorType.LeftShift or OperatorType.RightShift => 6,

                // Relational
                OperatorType.GreaterThan or OperatorType.LessThan or
                OperatorType.GreaterEquals or OperatorType.LessEquals => 5,

                // Equality
                OperatorType.Equals or OperatorType.NotEquals => 4,

                // Bitwise AND
                OperatorType.BitwiseAnd => 3,

                // Bitwise XOR
                OperatorType.BitwiseXor => 2,

                // Bitwise OR
                OperatorType.BitwiseOr => 1,

                // Logical AND
                OperatorType.LogicalAnd => 1,

                // Logical OR
                OperatorType.LogicalOr => 0,

                // Assignment (lowest precedence)
                OperatorType.Assign => 0,

                _ => -1 // Invalid
            };
        }

        /// <summary>
        /// Apply an arithmetic operator to two numeric values
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryApplyArithmetic(OperatorType op, NativeSlice<byte> left, NativeSlice<byte> right, out float result)
        {
            result = 0f;

            var leftResult = FastNumberParser.ParseFloat(left);
            var rightResult = FastNumberParser.ParseFloat(right);

            if (!leftResult.Success || !rightResult.Success)
                return false;

            result = op switch
            {
                OperatorType.Add => leftResult.Value + rightResult.Value,
                OperatorType.Subtract => leftResult.Value - rightResult.Value,
                OperatorType.Multiply => leftResult.Value * rightResult.Value,
                OperatorType.Divide => rightResult.Value != 0 ? leftResult.Value / rightResult.Value : 0f,
                OperatorType.Modulo => rightResult.Value != 0 ? leftResult.Value % rightResult.Value : 0f,
                _ => 0f
            };

            return true;
        }

        /// <summary>
        /// Compare two byte slices for equality
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AreEqual(NativeSlice<byte> left, NativeSlice<byte> right)
        {
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Compare two numeric values
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareNumeric(NativeSlice<byte> left, NativeSlice<byte> right)
        {
            var leftResult = FastNumberParser.ParseFloat(left);
            var rightResult = FastNumberParser.ParseFloat(right);

            if (!leftResult.Success || !rightResult.Success)
            {
                // Fall back to string comparison
                return CompareBytes(left, right);
            }

            return leftResult.Value.CompareTo(rightResult.Value);
        }

        /// <summary>
        /// Compare two byte slices lexicographically
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareBytes(NativeSlice<byte> left, NativeSlice<byte> right)
        {
            int minLength = left.Length < right.Length ? left.Length : right.Length;

            for (int i = 0; i < minLength; i++)
            {
                int comparison = left[i].CompareTo(right[i]);
                if (comparison != 0)
                    return comparison;
            }

            return left.Length.CompareTo(right.Length);
        }

        /// <summary>
        /// Check if an operator expression represents a conditional statement
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsConditional(OperatorExpression expr)
        {
            return IsComparisonOperator(expr.Type) || IsLogicalOperator(expr.Type);
        }

        /// <summary>
        /// Get string representation of operator type
        /// </summary>
        public static string GetOperatorSymbol(OperatorType op)
        {
            return op switch
            {
                OperatorType.Assign => "=",
                OperatorType.Add => "+=",
                OperatorType.Subtract => "-=",
                OperatorType.Multiply => "*=",
                OperatorType.Divide => "/=",
                OperatorType.Modulo => "%=",
                OperatorType.Equals => "==",
                OperatorType.NotEquals => "!=",
                OperatorType.GreaterThan => ">",
                OperatorType.LessThan => "<",
                OperatorType.GreaterEquals => ">=",
                OperatorType.LessEquals => "<=",
                OperatorType.LogicalAnd => "&&",
                OperatorType.LogicalOr => "||",
                OperatorType.LogicalNot => "!",
                OperatorType.BitwiseAnd => "&",
                OperatorType.BitwiseOr => "|",
                OperatorType.BitwiseXor => "^",
                OperatorType.BitwiseNot => "~",
                OperatorType.LeftShift => "<<",
                OperatorType.RightShift => ">>",
                OperatorType.ScopeResolution => "::",
                OperatorType.Arrow => "->",
                _ => "?"
            };
        }
    }
}