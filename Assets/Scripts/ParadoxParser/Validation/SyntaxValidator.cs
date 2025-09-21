using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ParadoxParser.Core;
using ParadoxParser.Tokenization;
using ParadoxParser.Utilities;

namespace ParadoxParser.Validation
{
    /// <summary>
    /// High-performance syntax validator for Paradox files
    /// Validates proper token sequences, bracket matching, etc.
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class SyntaxValidator
    {
        /// <summary>
        /// Common validation message hashes
        /// </summary>
        public static class MessageHashes
        {
            public static readonly uint UnmatchedOpenBrace = FastHasher.HashFNV1a32("UNMATCHED_OPEN_BRACE");
            public static readonly uint UnmatchedCloseBrace = FastHasher.HashFNV1a32("UNMATCHED_CLOSE_BRACE");
            public static readonly uint ExpectedEquals = FastHasher.HashFNV1a32("EXPECTED_EQUALS");
            public static readonly uint ExpectedValue = FastHasher.HashFNV1a32("EXPECTED_VALUE");
            public static readonly uint UnexpectedToken = FastHasher.HashFNV1a32("UNEXPECTED_TOKEN");
            public static readonly uint EmptyBlock = FastHasher.HashFNV1a32("EMPTY_BLOCK");
            public static readonly uint InvalidIdentifier = FastHasher.HashFNV1a32("INVALID_IDENTIFIER");
            public static readonly uint UnterminatedString = FastHasher.HashFNV1a32("UNTERMINATED_STRING");
        }

        /// <summary>
        /// Validate syntax of tokenized input
        /// </summary>
        public static ValidationResult ValidateSyntax(
            NativeSlice<Token> tokens,
            NativeSlice<byte> sourceData,
            ValidationOptions options)
        {
            var messages = new NativeList<ValidationMessage>(32, Allocator.Temp);
            var result = ValidationResult.Valid;
            result.Messages = messages;

            try
            {
                ValidateTokenSequence(tokens, sourceData, ref result, options);
                ValidateBracketMatching(tokens, ref result, options);
                ValidateKeyValuePairs(tokens, sourceData, ref result, options);

                return ValidationResult.Create(result.Messages);
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Validate overall token sequence structure
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateTokenSequence(
            NativeSlice<Token> tokens,
            NativeSlice<byte> sourceData,
            ref ValidationResult result,
            ValidationOptions options)
        {
            if (!options.ValidateSyntax)
                return;

            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];

                // Check for invalid token types in sequence
                switch (token.Type)
                {
                    case TokenType.String:
                        ValidateStringToken(token, sourceData, ref result, i);
                        break;

                    case TokenType.Identifier:
                        ValidateIdentifierToken(token, sourceData, ref result, i);
                        break;

                    case TokenType.Number:
                        ValidateNumberToken(token, sourceData, ref result, i);
                        break;

                    case TokenType.Date:
                        ValidateDateToken(token, sourceData, ref result, i);
                        break;
                }

                // Stop if we hit max errors
                if (result.ErrorCount >= options.MaxErrors)
                    break;
            }
        }

        /// <summary>
        /// Validate bracket matching and nesting
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateBracketMatching(
            NativeSlice<Token> tokens,
            ref ValidationResult result,
            ValidationOptions options)
        {
            if (!options.ValidateSyntax)
                return;

            var braceStack = new NativeList<int>(32, Allocator.Temp);

            try
            {
                for (int i = 0; i < tokens.Length; i++)
                {
                    var token = tokens[i];

                    switch (token.Type)
                    {
                        case TokenType.LeftBrace:
                            braceStack.Add(i);
                            break;

                        case TokenType.RightBrace:
                            if (braceStack.Length == 0)
                            {
                                // Unmatched closing brace
                                var message = ValidationMessage.Create(
                                    ValidationType.Syntax,
                                    ValidationSeverity.Error,
                                    token.Line,
                                    token.Column,
                                    token.StartPosition,
                                    MessageHashes.UnmatchedCloseBrace);
                                result.AddMessage(message);
                            }
                            else
                            {
                                braceStack.RemoveAtSwapBack(braceStack.Length - 1);
                            }
                            break;
                    }

                    if (result.ErrorCount >= options.MaxErrors)
                        break;
                }

                // Check for unmatched opening braces
                for (int i = 0; i < braceStack.Length; i++)
                {
                    var tokenIndex = braceStack[i];
                    var token = tokens[tokenIndex];

                    var message = ValidationMessage.Create(
                        ValidationType.Syntax,
                        ValidationSeverity.Error,
                        token.Line,
                        token.Column,
                        token.StartPosition,
                        MessageHashes.UnmatchedOpenBrace);
                    result.AddMessage(message);

                    if (result.ErrorCount >= options.MaxErrors)
                        break;
                }
            }
            finally
            {
                braceStack.Dispose();
            }
        }

        /// <summary>
        /// Validate key-value pair structure
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateKeyValuePairs(
            NativeSlice<Token> tokens,
            NativeSlice<byte> sourceData,
            ref ValidationResult result,
            ValidationOptions options)
        {
            if (!options.ValidateSyntax)
                return;

            for (int i = 0; i < tokens.Length - 2; i++)
            {
                var token = tokens[i];

                if (token.Type == TokenType.Identifier)
                {
                    // Look for key = value pattern
                    int nextIndex = SkipWhitespace(tokens, i + 1);
                    if (nextIndex < tokens.Length)
                    {
                        var nextToken = tokens[nextIndex];
                        if (nextToken.Type == TokenType.Equals)
                        {
                            // Found equals, expect value next
                            int valueIndex = SkipWhitespace(tokens, nextIndex + 1);
                            if (valueIndex >= tokens.Length)
                            {
                                var message = ValidationMessage.Create(
                                    ValidationType.Syntax,
                                    ValidationSeverity.Error,
                                    nextToken.Line,
                                    nextToken.Column,
                                    nextToken.StartPosition,
                                    MessageHashes.ExpectedValue);
                                result.AddMessage(message);
                            }
                            else
                            {
                                var valueToken = tokens[valueIndex];
                                if (!IsValidValueToken(valueToken))
                                {
                                    var message = ValidationMessage.Create(
                                        ValidationType.Syntax,
                                        ValidationSeverity.Error,
                                        valueToken.Line,
                                        valueToken.Column,
                                        valueToken.StartPosition,
                                        MessageHashes.UnexpectedToken);
                                    result.AddMessage(message);
                                }
                            }
                        }
                        else if (IsValueToken(nextToken))
                        {
                            // Identifier followed by value without equals
                            var message = ValidationMessage.Create(
                                ValidationType.Syntax,
                                ValidationSeverity.Error,
                                nextToken.Line,
                                nextToken.Column,
                                nextToken.StartPosition,
                                MessageHashes.ExpectedEquals);
                            result.AddMessage(message);
                        }
                    }
                }

                if (result.ErrorCount >= options.MaxErrors)
                    break;
            }
        }

        /// <summary>
        /// Validate string token format
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateStringToken(
            Token token,
            NativeSlice<byte> sourceData,
            ref ValidationResult result,
            int tokenIndex)
        {
            if (token.Length < 2)
            {
                var message = ValidationMessage.Create(
                    ValidationType.Syntax,
                    ValidationSeverity.Error,
                    token.Line,
                    token.Column,
                    token.StartPosition,
                    MessageHashes.UnterminatedString);
                result.AddMessage(message);
                return;
            }

            var startByte = sourceData[token.StartPosition];
            var endByte = sourceData[token.StartPosition + token.Length - 1];

            // Check for proper quote matching
            if ((startByte == (byte)'"' && endByte != (byte)'"') ||
                (startByte == (byte)'\'' && endByte != (byte)'\''))
            {
                var message = ValidationMessage.Create(
                    ValidationType.Syntax,
                    ValidationSeverity.Error,
                    token.Line,
                    token.Column,
                    token.StartPosition,
                    MessageHashes.UnterminatedString);
                result.AddMessage(message);
            }
        }

        /// <summary>
        /// Validate identifier token format
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateIdentifierToken(
            Token token,
            NativeSlice<byte> sourceData,
            ref ValidationResult result,
            int tokenIndex)
        {
            if (token.Length == 0)
            {
                var message = ValidationMessage.Create(
                    ValidationType.Syntax,
                    ValidationSeverity.Error,
                    token.Line,
                    token.Column,
                    token.StartPosition,
                    MessageHashes.InvalidIdentifier);
                result.AddMessage(message);
                return;
            }

            // Check first character is valid (letter or underscore)
            var firstByte = sourceData[token.StartPosition];
            if (!IsValidIdentifierStart(firstByte))
            {
                var message = ValidationMessage.Create(
                    ValidationType.Syntax,
                    ValidationSeverity.Warning,
                    token.Line,
                    token.Column,
                    token.StartPosition,
                    MessageHashes.InvalidIdentifier);
                result.AddMessage(message);
            }
        }

        /// <summary>
        /// Validate number token format
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateNumberToken(
            Token token,
            NativeSlice<byte> sourceData,
            ref ValidationResult result,
            int tokenIndex)
        {
            var numberData = sourceData.Slice(token.StartPosition, token.Length);
            var parseResult = FastNumberParser.ParseFloat(numberData);

            if (!parseResult.Success)
            {
                var message = ValidationMessage.Create(
                    ValidationType.Syntax,
                    ValidationSeverity.Error,
                    token.Line,
                    token.Column,
                    token.StartPosition,
                    MessageHashes.UnexpectedToken);
                result.AddMessage(message);
            }
        }

        /// <summary>
        /// Validate date token format
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateDateToken(
            Token token,
            NativeSlice<byte> sourceData,
            ref ValidationResult result,
            int tokenIndex)
        {
            var dateData = sourceData.Slice(token.StartPosition, token.Length);
            var parseResult = FastDateParser.ParseDate(dateData);

            if (!parseResult.Success)
            {
                var message = ValidationMessage.Create(
                    ValidationType.Syntax,
                    ValidationSeverity.Error,
                    token.Line,
                    token.Column,
                    token.StartPosition,
                    MessageHashes.UnexpectedToken);
                result.AddMessage(message);
            }
        }

        // Helper methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SkipWhitespace(NativeSlice<Token> tokens, int startIndex)
        {
            while (startIndex < tokens.Length &&
                   (tokens[startIndex].Type == TokenType.Whitespace ||
                    tokens[startIndex].Type == TokenType.Newline))
            {
                startIndex++;
            }
            return startIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidValueToken(Token token)
        {
            return token.Type == TokenType.String ||
                   token.Type == TokenType.Number ||
                   token.Type == TokenType.Date ||
                   token.Type == TokenType.Identifier ||
                   token.Type == TokenType.LeftBrace;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValueToken(Token token)
        {
            return token.Type == TokenType.String ||
                   token.Type == TokenType.Number ||
                   token.Type == TokenType.Date ||
                   token.Type == TokenType.LeftBrace;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidIdentifierStart(byte b)
        {
            return (b >= (byte)'a' && b <= (byte)'z') ||
                   (b >= (byte)'A' && b <= (byte)'Z') ||
                   b == (byte)'_';
        }
    }
}