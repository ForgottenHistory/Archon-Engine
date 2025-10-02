using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Tokenization;
using ParadoxParser.Utilities;

namespace ParadoxParser.Core
{
    /// <summary>
    /// High-performance Paradox file parser
    /// Handles nested blocks, lists, and key-value pairs
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class ParadoxParser
    {
        /// <summary>
        /// Parse result containing the parsed data structure
        /// </summary>
        public struct ParseResult
        {
            public bool Success;
            public int TokensProcessed;
            public ParserStateMachine.ParseErrorType ErrorType;
            public int ErrorLine;
            public int ErrorColumn;

            public static ParseResult Successful(int tokensProcessed)
            {
                return new ParseResult
                {
                    Success = true,
                    TokensProcessed = tokensProcessed,
                    ErrorType = ParserStateMachine.ParseErrorType.None,
                    ErrorLine = 0,
                    ErrorColumn = 0
                };
            }

            public static ParseResult Failed(ParserStateMachine.ParseErrorType errorType, int line, int column, int tokensProcessed)
            {
                return new ParseResult
                {
                    Success = false,
                    TokensProcessed = tokensProcessed,
                    ErrorType = errorType,
                    ErrorLine = line,
                    ErrorColumn = column
                };
            }
        }

        /// <summary>
        /// Parsed key-value pair
        /// </summary>
        public struct ParsedKeyValue
        {
            public uint KeyHash;           // Hash of the key for fast lookups
            public NativeSlice<byte> Key;  // Raw key data
            public ParsedValue Value;      // The parsed value
            public int LineNumber;         // Source line for debugging
        }

        /// <summary>
        /// Parsed value (can be literal, block, or list)
        /// </summary>
        public struct ParsedValue
        {
            public ParsedValueType Type;
            public NativeSlice<byte> RawData;  // Raw token data
            public int BlockStartIndex;        // For blocks/lists: index in child array
            public int BlockLength;            // For blocks/lists: number of children

            public bool IsLiteral => Type == ParsedValueType.Literal;
            public bool IsBlock => Type == ParsedValueType.Block;
            public bool IsList => Type == ParsedValueType.List;
        }

        /// <summary>
        /// Types of parsed values
        /// </summary>
        public enum ParsedValueType : byte
        {
            Literal = 0,
            Block = 1,
            List = 2
        }

        /// <summary>
        /// Parse tokens into structured data
        /// </summary>
        public static ParseResult Parse(
            NativeSlice<Token> tokens,
            NativeList<ParsedKeyValue> keyValues,
            NativeList<ParsedKeyValue> childBlocks,
            NativeSlice<byte> sourceData,
            Allocator allocator = Allocator.TempJob)
        {
            var state = new ParserStateInfo();
            int tokenIndex = 0;
            int blockStackDepth = 0;

            // Stack for tracking nested blocks
            var blockStack = new NativeArray<BlockInfo>(64, allocator);

            try
            {
                while (tokenIndex < tokens.Length)
                {
                    var token = tokens[tokenIndex];


                    // Skip comments, whitespace, and newlines
                    if (token.Type.ShouldSkip())
                    {
                        tokenIndex++;
                        continue;
                    }

                    // Update position tracking
                    UpdatePositionTracking(ref state, token);

                    // Process the token through state machine
                    var operation = ParserStateMachine.ProcessToken(token, state);

                    // Debug: Show failure details around latest token range
                    if (tokenIndex >= 260 && tokenIndex <= 290) // Capture failures at tokens 262, 284
                    {
                        string tokenContent = "N/A";
                        if (token.StartPosition >= 0 && token.StartPosition < sourceData.Length && token.Length > 0)
                        {
                            var tokenData = sourceData.Slice(token.StartPosition, Math.Min(token.Length, 50)); // Max 50 chars
                            tokenContent = System.Text.Encoding.UTF8.GetString(tokenData.ToArray());
                            tokenContent = tokenContent.Replace('\n', '\\').Replace('\r', '\\').Replace('\t', '\\'); // Escape special chars
                        }
                        //UnityEngine.Debug.Log($"Token {tokenIndex}: Type={token.Type}, Content='{tokenContent}', CurrentState={state.State}, Success={operation.Success}, NewState={operation.NewState.State}, TokensConsumed={operation.TokensConsumed}");
                    }

                    if (!operation.Success)
                    {
                        return ParseResult.Failed(operation.ErrorType, state.LineNumber, state.ColumnNumber, tokenIndex);
                    }

                    // Handle specific parsing operations based on state transitions
                    var parseOp = HandleParseOperation(
                        tokens, ref tokenIndex, operation,
                        keyValues, childBlocks, sourceData,
                        blockStack, ref blockStackDepth);

                    if (!parseOp.Success)
                    {
                        return ParseResult.Failed(parseOp.ErrorType, state.LineNumber, state.ColumnNumber, tokenIndex);
                    }

                    state = operation.NewState;
                    tokenIndex += operation.TokensConsumed;
                }

                // Allow unclosed blocks for tolerant parsing of Paradox files
                // Many Paradox files have missing closing braces
                if (state.BlockDepth > 0)
                {
                    //UnityEngine.Debug.LogWarning($"File has {state.BlockDepth} unclosed blocks - parsing anyway");
                }

                return ParseResult.Successful(tokenIndex);
            }
            finally
            {
                blockStack.Dispose();
            }
        }

        /// <summary>
        /// Information about a block being parsed
        /// </summary>
        private struct BlockInfo
        {
            public int StartIndex;        // Index in keyValues where this block starts
            public bool IsList;           // Whether this is a list or regular block
            public uint ParentKeyHash;    // Hash of the key that opened this block
        }

        /// <summary>
        /// Handle specific parsing operations based on state transitions
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParserStateMachine.ParseOperation HandleParseOperation(
            NativeSlice<Token> tokens,
            ref int tokenIndex,
            ParserStateMachine.ParseOperation operation,
            NativeList<ParsedKeyValue> keyValues,
            NativeList<ParsedKeyValue> childBlocks,
            NativeSlice<byte> sourceData,
            NativeArray<BlockInfo> blockStack,
            ref int blockStackDepth)
        {
            var currentState = operation.NewState;

            switch (currentState.State)
            {
                case ParserState.ExpectingEquals:
                    return HandleKeyToken(tokens, tokenIndex, keyValues, sourceData, currentState);

                case ParserState.InLiteral:
                case ParserState.InNumber:
                case ParserState.InDate:
                case ParserState.InQuotedString:
                    return HandleValueToken(tokens, tokenIndex, keyValues, sourceData, currentState);

                default:
                    return operation; // No special handling needed - state machine handles block transitions
            }
        }

        /// <summary>
        /// Handle parsing a key token
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParserStateMachine.ParseOperation HandleKeyToken(
            NativeSlice<Token> tokens,
            int tokenIndex,
            NativeList<ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            ParserStateInfo state)
        {
            var token = tokens[tokenIndex];
            var keyData = sourceData.Slice(token.StartPosition, token.Length);
            var keyHash = FastHasher.HashFNV1a32(keyData);

            // Store the key - we'll fill in the value later
            var kvp = new ParsedKeyValue
            {
                KeyHash = keyHash,
                Key = keyData,
                Value = default,
                LineNumber = state.LineNumber
            };

            keyValues.Add(kvp);
            return ParserStateMachine.ParseOperation.Successful(state, 0);
        }

        /// <summary>
        /// Handle parsing a value token
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParserStateMachine.ParseOperation HandleValueToken(
            NativeSlice<Token> tokens,
            int tokenIndex,
            NativeList<ParsedKeyValue> keyValues,
            NativeSlice<byte> sourceData,
            ParserStateInfo state)
        {
            if (keyValues.Length == 0)
            {
                return ParserStateMachine.ParseOperation.Failed(ParserStateMachine.ParseErrorType.UnexpectedToken, state);
            }

            var token = tokens[tokenIndex];
            var valueData = sourceData.Slice(token.StartPosition, token.Length);

            // Update the last key-value pair with this value
            var kvp = keyValues[keyValues.Length - 1];
            kvp.Value = new ParsedValue
            {
                Type = ParsedValueType.Literal,
                RawData = valueData,
                BlockStartIndex = -1,
                BlockLength = 0
            };
            keyValues[keyValues.Length - 1] = kvp;

            return ParserStateMachine.ParseOperation.Successful(state, 0);
        }

        /// <summary>
        /// Handle start of a block or list
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParserStateMachine.ParseOperation HandleBlockStart(
            NativeSlice<Token> tokens,
            int tokenIndex,
            NativeList<ParsedKeyValue> keyValues,
            NativeArray<BlockInfo> blockStack,
            ref int blockStackDepth,
            bool isList)
        {
            if (blockStackDepth >= blockStack.Length)
            {
                return ParserStateMachine.ParseOperation.Failed(
                    ParserStateMachine.ParseErrorType.MaxDepthExceeded,
                    new ParserStateInfo());
            }

            uint parentKeyHash = 0;
            if (keyValues.Length > 0)
            {
                parentKeyHash = keyValues[keyValues.Length - 1].KeyHash;
            }

            // Push block info onto stack
            blockStack[blockStackDepth] = new BlockInfo
            {
                StartIndex = keyValues.Length,
                IsList = isList,
                ParentKeyHash = parentKeyHash
            };
            blockStackDepth++;

            return ParserStateMachine.ParseOperation.Successful(new ParserStateInfo(), 0);
        }

        /// <summary>
        /// Handle end of a block or list
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ParserStateMachine.ParseOperation HandleBlockEnd(
            NativeList<ParsedKeyValue> keyValues,
            NativeList<ParsedKeyValue> childBlocks,
            NativeArray<BlockInfo> blockStack,
            ref int blockStackDepth)
        {
            if (blockStackDepth == 0)
            {
                return ParserStateMachine.ParseOperation.Failed(
                    ParserStateMachine.ParseErrorType.UnmatchedBrace,
                    new ParserStateInfo());
            }

            blockStackDepth--;
            var blockInfo = blockStack[blockStackDepth];

            // Update parent key-value pair with block information
            if (keyValues.Length > 0)
            {
                var kvp = keyValues[keyValues.Length - 1];
                kvp.Value = new ParsedValue
                {
                    Type = blockInfo.IsList ? ParsedValueType.List : ParsedValueType.Block,
                    RawData = default,
                    BlockStartIndex = blockInfo.StartIndex,
                    BlockLength = keyValues.Length - blockInfo.StartIndex
                };
                keyValues[keyValues.Length - 1] = kvp;
            }

            return ParserStateMachine.ParseOperation.Successful(new ParserStateInfo(), 0);
        }

        /// <summary>
        /// Update line and column tracking based on token
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UpdatePositionTracking(ref ParserStateInfo state, Token token)
        {
            if (token.Type == TokenType.Newline)
            {
                state.NextLine();
            }
            else
            {
                state.NextColumn(token.Length);
            }
        }

        /// <summary>
        /// Find a key-value pair by key hash
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FindKeyValue(NativeSlice<ParsedKeyValue> keyValues, uint keyHash)
        {
            for (int i = 0; i < keyValues.Length; i++)
            {
                if (keyValues[i].KeyHash == keyHash)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Get all values for a specific key (handles multiple occurrences)
        /// </summary>
        public static void FindAllKeyValues(
            NativeSlice<ParsedKeyValue> keyValues,
            uint keyHash,
            NativeList<int> indices)
        {
            indices.Clear();
            for (int i = 0; i < keyValues.Length; i++)
            {
                if (keyValues[i].KeyHash == keyHash)
                    indices.Add(i);
            }
        }

        /// <summary>
        /// Helper to compute key hash from string
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ComputeKeyHash(NativeSlice<byte> key)
        {
            return FastHasher.HashFNV1a32(key);
        }
    }
}