using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Tokenization;
using ParadoxParser.Utilities;

namespace ParadoxParser.Core
{
    /// <summary>
    /// High-performance parser state machine for Paradox files
    /// Handles state transitions and token processing
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class ParserStateMachine
    {
        /// <summary>
        /// Parser operation result
        /// </summary>
        public struct ParseOperation
        {
            public bool Success;
            public ParserStateInfo NewState;
            public int TokensConsumed;
            public ParseErrorType ErrorType;

            public static ParseOperation Successful(ParserStateInfo newState, int tokensConsumed = 1)
            {
                return new ParseOperation
                {
                    Success = true,
                    NewState = newState,
                    TokensConsumed = tokensConsumed,
                    ErrorType = ParseErrorType.None
                };
            }

            public static ParseOperation Failed(ParseErrorType errorType, ParserStateInfo currentState)
            {
                var errorState = currentState;
                errorState.State = ParserState.Error;
                return new ParseOperation
                {
                    Success = false,
                    NewState = errorState,
                    TokensConsumed = 0,
                    ErrorType = errorType
                };
            }
        }

        /// <summary>
        /// Parse error types
        /// </summary>
        public enum ParseErrorType : byte
        {
            None = 0,
            UnexpectedToken = 1,
            InvalidStateTransition = 2,
            UnmatchedBrace = 3,
            UnterminatedString = 4,
            InvalidNumber = 5,
            InvalidDate = 6,
            MaxDepthExceeded = 7,
            UnexpectedEndOfFile = 8
        }

        private const int MAX_BLOCK_DEPTH = 64; // Prevent stack overflow

        /// <summary>
        /// Process a single token and return the new parser state
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ParseOperation ProcessToken(Token token, ParserStateInfo currentState)
        {
            // Check depth limits
            if (currentState.BlockDepth >= MAX_BLOCK_DEPTH)
            {
                return ParseOperation.Failed(ParseErrorType.MaxDepthExceeded, currentState);
            }

            return currentState.State switch
            {
                ParserState.Initial => ProcessInitialState(token, currentState),
                ParserState.ExpectingKey => ProcessExpectingKey(token, currentState),
                ParserState.ExpectingEquals => ProcessExpectingEquals(token, currentState),
                ParserState.ExpectingValue => ProcessExpectingValue(token, currentState),
                ParserState.InBlock => ProcessInBlock(token, currentState),
                ParserState.InList => ProcessInList(token, currentState),
                ParserState.InQuotedString => ProcessInQuotedString(token, currentState),
                ParserState.InLiteral => ProcessInLiteral(token, currentState),
                ParserState.InNumber => ProcessInNumber(token, currentState),
                ParserState.InDate => ProcessInDate(token, currentState),
                ParserState.ExpectingEndOfStatement => ProcessExpectingEndOfStatement(token, currentState),
                ParserState.InComment => ProcessInComment(token, currentState),
                ParserState.Error => ParseOperation.Failed(ParseErrorType.InvalidStateTransition, currentState),
                _ => ParseOperation.Failed(ParseErrorType.InvalidStateTransition, currentState)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation ProcessInitialState(Token token, ParserStateInfo state)
        {
            return token.Type switch
            {
                TokenType.Identifier => TransitionToExpectingEquals(state),
                TokenType.Hash => TransitionToComment(state),
                TokenType.Whitespace or TokenType.Newline => ParseOperation.Successful(state, 1), // Skip whitespace
                TokenType.EndOfFile => ParseOperation.Successful(state, 1), // Graceful end
                _ => ParseOperation.Failed(ParseErrorType.UnexpectedToken, state)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation ProcessExpectingKey(Token token, ParserStateInfo state)
        {
            return token.Type switch
            {
                TokenType.Identifier => TransitionToExpectingEquals(state),
                TokenType.RightBrace when state.IsInContainer => TransitionToBlockEnd(state),
                TokenType.Hash => TransitionToComment(state),
                TokenType.Whitespace or TokenType.Newline => ParseOperation.Successful(state, 1),
                TokenType.EndOfFile when state.IsAtRootLevel => ParseOperation.Successful(state, 1),
                _ => ParseOperation.Failed(ParseErrorType.UnexpectedToken, state)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation ProcessExpectingEquals(Token token, ParserStateInfo state)
        {
            return token.Type switch
            {
                TokenType.Equals => TransitionToExpectingValue(state),
                TokenType.Whitespace => ParseOperation.Successful(state, 1),
                _ => ParseOperation.Failed(ParseErrorType.UnexpectedToken, state)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation ProcessExpectingValue(Token token, ParserStateInfo state)
        {
            return token.Type switch
            {
                TokenType.LeftBrace => TransitionToBlock(state, false), // Regular block
                TokenType.String => TransitionToQuotedString(state),
                TokenType.Identifier => TransitionToLiteral(state),
                TokenType.Number => TransitionToNumber(state),
                TokenType.Date => TransitionToDate(state),
                TokenType.Whitespace => ParseOperation.Successful(state, 1),
                _ => ParseOperation.Failed(ParseErrorType.UnexpectedToken, state)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation ProcessInBlock(Token token, ParserStateInfo state)
        {
            return token.Type switch
            {
                TokenType.Identifier => TransitionToExpectingEquals(state),
                TokenType.RightBrace => TransitionToBlockEnd(state),
                TokenType.Hash => TransitionToComment(state),
                TokenType.Whitespace or TokenType.Newline => ParseOperation.Successful(state, 1),
                _ => ParseOperation.Failed(ParseErrorType.UnexpectedToken, state)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation ProcessInList(Token token, ParserStateInfo state)
        {
            return token.Type switch
            {
                TokenType.Identifier or TokenType.Number => TransitionToLiteral(state),
                TokenType.String => TransitionToQuotedString(state),
                TokenType.RightBrace => TransitionToBlockEnd(state),
                TokenType.Whitespace => ParseOperation.Successful(state, 1),
                _ => ParseOperation.Failed(ParseErrorType.UnexpectedToken, state)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation ProcessInQuotedString(Token token, ParserStateInfo state)
        {
            // Quoted string token should be complete, move to end of statement
            var newState = state;
            newState.State = ParserState.ExpectingEndOfStatement;
            return ParseOperation.Successful(newState, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation ProcessInLiteral(Token token, ParserStateInfo state)
        {
            var newState = state;
            newState.State = state.IsInList ? ParserState.InList : ParserState.ExpectingEndOfStatement;
            return ParseOperation.Successful(newState, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation ProcessInNumber(Token token, ParserStateInfo state)
        {
            var newState = state;
            newState.State = ParserState.ExpectingEndOfStatement;
            return ParseOperation.Successful(newState, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation ProcessInDate(Token token, ParserStateInfo state)
        {
            var newState = state;
            newState.State = ParserState.ExpectingEndOfStatement;
            return ParseOperation.Successful(newState, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation ProcessExpectingEndOfStatement(Token token, ParserStateInfo state)
        {
            return token.Type switch
            {
                TokenType.Newline => TransitionToExpectingKey(state),
                TokenType.Hash => TransitionToComment(state),
                TokenType.RightBrace when state.IsInContainer => TransitionToBlockEnd(state),
                TokenType.Whitespace => ParseOperation.Successful(state, 1),
                TokenType.EndOfFile => ParseOperation.Successful(state, 1),
                _ => ParseOperation.Failed(ParseErrorType.UnexpectedToken, state)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation ProcessInComment(Token token, ParserStateInfo state)
        {
            return token.Type switch
            {
                TokenType.Newline => TransitionToExpectingKey(state),
                TokenType.EndOfFile => ParseOperation.Successful(state, 1),
                _ => ParseOperation.Successful(state, 1) // Consume all tokens in comment
            };
        }

        // State transition helpers
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation TransitionToExpectingEquals(ParserStateInfo state)
        {
            var newState = state;
            newState.State = ParserState.ExpectingEquals;
            return ParseOperation.Successful(newState, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation TransitionToExpectingValue(ParserStateInfo state)
        {
            var newState = state;
            newState.State = ParserState.ExpectingValue;
            return ParseOperation.Successful(newState, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation TransitionToExpectingKey(ParserStateInfo state)
        {
            var newState = state;
            newState.State = state.IsInContainer ? ParserState.InBlock : ParserState.ExpectingKey;
            return ParseOperation.Successful(newState, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation TransitionToBlock(ParserStateInfo state, bool isList)
        {
            var newState = state;
            newState.EnterBlock(isList);
            return ParseOperation.Successful(newState, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation TransitionToBlockEnd(ParserStateInfo state)
        {
            var newState = state;
            newState.ExitBlock();
            return ParseOperation.Successful(newState, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation TransitionToQuotedString(ParserStateInfo state)
        {
            var newState = state;
            newState.State = ParserState.InQuotedString;
            return ParseOperation.Successful(newState, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation TransitionToLiteral(ParserStateInfo state)
        {
            var newState = state;
            newState.State = ParserState.InLiteral;
            return ParseOperation.Successful(newState, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation TransitionToNumber(ParserStateInfo state)
        {
            var newState = state;
            newState.State = ParserState.InNumber;
            return ParseOperation.Successful(newState, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation TransitionToDate(ParserStateInfo state)
        {
            var newState = state;
            newState.State = ParserState.InDate;
            return ParseOperation.Successful(newState, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParseOperation TransitionToComment(ParserStateInfo state)
        {
            var newState = state;
            newState.State = ParserState.InComment;
            return ParseOperation.Successful(newState, 1);
        }

        /// <summary>
        /// Check if current state allows list detection
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanDetectList(ParserStateInfo state, Token nextToken)
        {
            return state.State == ParserState.ExpectingValue &&
                   nextToken.Type == TokenType.LeftBrace;
        }

        /// <summary>
        /// Detect if a block should be treated as a list based on content preview
        /// </summary>
        public static bool ShouldTreatAsListBlock(NativeSlice<Token> tokens, int startIndex)
        {
            // Look ahead to see if this looks like a list pattern
            int braceCount = 0;
            bool foundNonWhitespace = false;
            bool foundEquals = false;

            for (int i = startIndex; i < tokens.Length && i < startIndex + 10; i++)
            {
                var token = tokens[i];

                switch (token.Type)
                {
                    case TokenType.LeftBrace:
                        braceCount++;
                        break;
                    case TokenType.RightBrace:
                        braceCount--;
                        if (braceCount == 0) return foundNonWhitespace && !foundEquals; // End of block
                        break;
                    case TokenType.Equals:
                        foundEquals = true;
                        return false; // Definitely not a list if we find equals
                    case TokenType.Identifier or TokenType.Number:
                        foundNonWhitespace = true;
                        break;
                    case TokenType.Whitespace or TokenType.Newline:
                        break; // Skip whitespace
                    default:
                        break;
                }
            }

            return foundNonWhitespace && !foundEquals;
        }
    }
}