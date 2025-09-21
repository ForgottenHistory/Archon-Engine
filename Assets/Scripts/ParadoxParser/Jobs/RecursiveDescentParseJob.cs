using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using ParadoxParser.Core;
using ParadoxParser.Tokenization;
using ParadoxParser.Utilities;

namespace ParadoxParser.Jobs
{
    /// <summary>
    /// High-performance recursive descent parser for Paradox files
    /// Uses the state machine and specialized parsers for optimal performance
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct RecursiveDescentParseJob : IParseJob
    {
        public NativeSlice<byte> InputData { get; set; }
        public NativeSlice<Token> InputTokens { get; set; }
        public NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> OutputKeyValues { get; set; }
        public NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> OutputBlocks { get; set; }
        public ParseJobOptions Options { get; set; }

        // Result is not settable from interface, but we need to store it
        private ParadoxParser.Core.ParadoxParser.ParseResult _result;
        public ParadoxParser.Core.ParadoxParser.ParseResult Result => _result;

        // Internal working data
        private NativeList<Token> _tokens;
        private int _currentTokenIndex;
        private int _currentDepth;
        private ParserStateInfo _currentState;

        /// <summary>
        /// Create a parse job for the given input
        /// </summary>
        public static RecursiveDescentParseJob Create(
            NativeSlice<byte> inputData,
            NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> outputKeyValues,
            NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> outputBlocks,
            ParseJobOptions options = default)
        {
            if (options.Equals(default(ParseJobOptions)))
                options = ParseJobOptions.Default;

            return new RecursiveDescentParseJob
            {
                InputData = inputData,
                InputTokens = default,
                OutputKeyValues = outputKeyValues,
                OutputBlocks = outputBlocks,
                Options = options,
                _result = default,
                _tokens = default,
                _currentTokenIndex = 0,
                _currentDepth = 0,
                _currentState = new ParserStateInfo()
            };
        }

        /// <summary>
        /// Create a parse job with pre-tokenized input
        /// </summary>
        public static RecursiveDescentParseJob CreateFromTokens(
            NativeSlice<byte> inputData,
            NativeSlice<Token> inputTokens,
            NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> outputKeyValues,
            NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> outputBlocks,
            ParseJobOptions options = default)
        {
            if (options.Equals(default(ParseJobOptions)))
                options = ParseJobOptions.Default;

            return new RecursiveDescentParseJob
            {
                InputData = inputData,
                InputTokens = inputTokens,
                OutputKeyValues = outputKeyValues,
                OutputBlocks = outputBlocks,
                Options = options,
                _result = default,
                _tokens = default,
                _currentTokenIndex = 0,
                _currentDepth = 0,
                _currentState = new ParserStateInfo()
            };
        }

        /// <summary>
        /// Execute the parsing job
        /// </summary>
        public void Execute()
        {
            if (!ValidateInputs())
                return;

            try
            {
                // Tokenize if needed
                if (InputTokens.Length == 0)
                {
                    if (!TokenizeInput())
                        return;
                }
                else
                {
                    // Use pre-tokenized input
                    _tokens = new NativeList<Token>(InputTokens.Length, Allocator.Temp);
                    for (int i = 0; i < InputTokens.Length; i++)
                    {
                        _tokens.Add(InputTokens[i]);
                    }
                }

                // Execute recursive descent parsing
                ExecuteParseLogic();
            }
            finally
            {
                // Clean up temporary data
                if (_tokens.IsCreated)
                    _tokens.Dispose();
            }
        }

        /// <summary>
        /// Validate input parameters
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateInputs()
        {
            if (InputData.Length == 0 && InputTokens.Length == 0)
            {
                _result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                    ParserStateMachine.ParseErrorType.UnexpectedEndOfFile, 1, 1, 0);
                return false;
            }

            if (!OutputKeyValues.IsCreated)
            {
                _result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                    ParserStateMachine.ParseErrorType.InvalidStateTransition, 1, 1, 0);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Tokenize the input data if needed
        /// </summary>
        private bool TokenizeInput()
        {
            // For now, use a simple tokenization approach
            // TODO: Implement proper tokenization using TokenizeJob
            _tokens = new NativeList<Token>(InputData.Length / 4, Allocator.Temp);

            // Create a basic tokenization - this is a placeholder implementation
            // In a real implementation, you would use the TokenizeJob or Tokenizer
            var tokenArray = new NativeArray<Token>(InputData.Length / 4, Allocator.Temp);
            var tokenCount = new NativeReference<int>(0, Allocator.Temp);
            var errorCount = new NativeReference<int>(0, Allocator.Temp);

            NativeArray<byte> inputArray = default;
            try
            {
                // Create NativeArray from NativeSlice without using ToArray()
                inputArray = new NativeArray<byte>(InputData.Length, Allocator.Temp);
                for (int i = 0; i < InputData.Length; i++)
                {
                    inputArray[i] = InputData[i];
                }

                var tokenizeJob = new TokenizeJob
                {
                    InputData = inputArray,
                    StartOffset = 0,
                    Length = InputData.Length,
                    OutputTokens = tokenArray,
                    TokenCount = tokenCount,
                    ErrorCount = errorCount
                };

                tokenizeJob.Execute();

                // Copy tokens to list
                for (int i = 0; i < tokenCount.Value && i < tokenArray.Length; i++)
                {
                    _tokens.Add(tokenArray[i]);
                }

                bool success = errorCount.Value == 0 && tokenCount.Value > 0;
                if (!success)
                {
                    _result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                        ParserStateMachine.ParseErrorType.InvalidStateTransition, 1, 1, 0);
                    return false;
                }

                return true;
            }
            finally
            {
                if (inputArray.IsCreated) inputArray.Dispose();
                if (tokenArray.IsCreated) tokenArray.Dispose();
                if (tokenCount.IsCreated) tokenCount.Dispose();
                if (errorCount.IsCreated) errorCount.Dispose();
            }
        }

        /// <summary>
        /// Main parsing logic using recursive descent
        /// </summary>
        private void ExecuteParseLogic()
        {
            _currentTokenIndex = 0;
            _currentDepth = 0;
            _currentState = new ParserStateInfo();

            // Clear output collections
            OutputKeyValues.Clear();
            if (OutputBlocks.IsCreated)
                OutputBlocks.Clear();

            // Parse the document
            while (_currentTokenIndex < _tokens.Length && _currentDepth < Options.MaxDepth)
            {
                var token = GetCurrentToken();
                if (token.Type == TokenType.EndOfFile)
                    break;

                if (!ParseTopLevelStatement())
                {
                    // Error occurred during parsing
                    return;
                }
            }

            // Check for unclosed blocks
            if (_currentState.BlockDepth > 0)
            {
                _result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                    ParserStateMachine.ParseErrorType.UnmatchedBrace,
                    _currentState.LineNumber, _currentState.ColumnNumber, _currentTokenIndex);
                return;
            }

            _result = ParadoxParser.Core.ParadoxParser.ParseResult.Successful(_currentTokenIndex);
        }

        /// <summary>
        /// Parse a top-level statement (key-value pair or block)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ParseTopLevelStatement()
        {
            var token = GetCurrentToken();

            switch (token.Type)
            {
                case TokenType.Identifier:
                    return ParseKeyValuePair();

                case TokenType.Whitespace:
                case TokenType.Newline:
                    AdvanceToken();
                    return true;

                case TokenType.Hash:
                    return SkipComment();

                case TokenType.RightBrace:
                    // Handle block end at top level (error)
                    _result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                        ParserStateMachine.ParseErrorType.UnmatchedBrace,
                        token.Line, token.Column, _currentTokenIndex);
                    return false;

                default:
                    // Unexpected token at top level
                    _result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                        ParserStateMachine.ParseErrorType.UnexpectedToken,
                        token.Line, token.Column, _currentTokenIndex);
                    return false;
            }
        }

        /// <summary>
        /// Parse a key-value pair or block
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ParseKeyValuePair()
        {
            var keyToken = GetCurrentToken();
            if (keyToken.Type != TokenType.Identifier)
                return false;

            // Extract key
            var keyData = InputData.Slice(keyToken.StartPosition, keyToken.Length);
            var keyHash = FastHasher.HashFNV1a32(keyData);

            AdvanceToken();
            SkipWhitespace();

            // Expect equals
            var equalsToken = GetCurrentToken();
            if (equalsToken.Type != TokenType.Equals)
            {
                _result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                    ParserStateMachine.ParseErrorType.UnexpectedToken,
                    equalsToken.Line, equalsToken.Column, _currentTokenIndex);
                return false;
            }

            AdvanceToken();
            SkipWhitespace();

            // Parse value
            var valueToken = GetCurrentToken();
            var kvp = new ParadoxParser.Core.ParadoxParser.ParsedKeyValue
            {
                KeyHash = keyHash,
                Key = keyData,
                LineNumber = keyToken.Line
            };

            switch (valueToken.Type)
            {
                case TokenType.LeftBrace:
                    return ParseBlockValue(ref kvp);

                case TokenType.String:
                case TokenType.Identifier:
                case TokenType.Number:
                case TokenType.Date:
                    return ParseLiteralValue(ref kvp);

                default:
                    _result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                        ParserStateMachine.ParseErrorType.UnexpectedToken,
                        valueToken.Line, valueToken.Column, _currentTokenIndex);
                    return false;
            }
        }

        /// <summary>
        /// Parse a literal value (string, number, date, etc.)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ParseLiteralValue(ref ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp)
        {
            var valueToken = GetCurrentToken();
            var valueData = InputData.Slice(valueToken.StartPosition, valueToken.Length);

            // Handle special values based on options
            if (Options.ParseSpecialKeywords && TryParseSpecialKeyword(valueData, out var specialValue))
            {
                kvp.Value = specialValue;
            }
            else if (Options.ParseColors && TryParseColorValue(ref kvp))
            {
                // Color parsing handled in TryParseColorValue
                return true;
            }
            else
            {
                // Regular literal value
                kvp.Value = new ParadoxParser.Core.ParadoxParser.ParsedValue
                {
                    Type = ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal,
                    RawData = valueData,
                    BlockStartIndex = -1,
                    BlockLength = 0
                };
            }

            OutputKeyValues.Add(kvp);
            AdvanceToken();
            return true;
        }

        /// <summary>
        /// Parse a block value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ParseBlockValue(ref ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp)
        {
            // Advance past opening brace
            AdvanceToken();
            _currentDepth++;
            _currentState.EnterBlock(false);

            int blockStartIndex = OutputBlocks.IsCreated ? OutputBlocks.Length : 0;
            int itemCount = 0;

            // Check if this should be treated as a list
            bool isList = ShouldTreatAsListBlock();
            if (isList)
            {
                return ParseListBlock(ref kvp);
            }

            // Parse block contents
            while (_currentTokenIndex < _tokens.Length)
            {
                var token = GetCurrentToken();

                switch (token.Type)
                {
                    case TokenType.RightBrace:
                        // End of block
                        AdvanceToken();
                        _currentDepth--;
                        _currentState.ExitBlock();

                        kvp.Value = new ParadoxParser.Core.ParadoxParser.ParsedValue
                        {
                            Type = ParadoxParser.Core.ParadoxParser.ParsedValueType.Block,
                            RawData = default,
                            BlockStartIndex = blockStartIndex,
                            BlockLength = itemCount
                        };

                        OutputKeyValues.Add(kvp);
                        return true;

                    case TokenType.Identifier:
                        // Nested key-value pair
                        if (!ParseKeyValuePair())
                            return false;

                        if (OutputBlocks.IsCreated)
                            OutputBlocks.Add(OutputKeyValues[OutputKeyValues.Length - 1]);
                        itemCount++;
                        break;

                    case TokenType.Whitespace:
                    case TokenType.Newline:
                        AdvanceToken();
                        break;

                    case TokenType.Hash:
                        if (!SkipComment())
                            return false;
                        break;

                    default:
                        _result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                            ParserStateMachine.ParseErrorType.UnexpectedToken,
                            token.Line, token.Column, _currentTokenIndex);
                        return false;
                }
            }

            // Unclosed block
            _result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                ParserStateMachine.ParseErrorType.UnmatchedBrace,
                _currentState.LineNumber, _currentState.ColumnNumber, _currentTokenIndex);
            return false;
        }

        /// <summary>
        /// Parse a list block (space-separated values)
        /// </summary>
        private bool ParseListBlock(ref ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp)
        {
            var listItems = new NativeList<ListParser.ListItem>(32, Allocator.Temp);

            try
            {
                int consumed;
                bool success = ListParser.TryParseList(
                    _tokens.AsArray().Slice(_currentTokenIndex), 0, out consumed,
                    listItems, InputData);

                if (!success)
                {
                    _result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                        ParserStateMachine.ParseErrorType.UnexpectedToken,
                        _currentState.LineNumber, _currentState.ColumnNumber, _currentTokenIndex);
                    return false;
                }

                _currentTokenIndex += consumed;
                _currentDepth--;
                _currentState.ExitBlock();

                kvp.Value = new ParadoxParser.Core.ParadoxParser.ParsedValue
                {
                    Type = ParadoxParser.Core.ParadoxParser.ParsedValueType.List,
                    RawData = default,
                    BlockStartIndex = 0, // List items stored separately
                    BlockLength = listItems.Length
                };

                OutputKeyValues.Add(kvp);
                return true;
            }
            finally
            {
                listItems.Dispose();
            }
        }

        /// <summary>
        /// Determine if a block should be treated as a list
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldTreatAsListBlock()
        {
            // Look ahead to see if this looks like a list pattern
            int lookAhead = _currentTokenIndex;
            int braceCount = 1; // We're already past the opening brace
            bool foundEquals = false;
            bool foundNonWhitespace = false;

            while (lookAhead < _tokens.Length && lookAhead < _currentTokenIndex + 10)
            {
                var token = _tokens[lookAhead];

                switch (token.Type)
                {
                    case TokenType.LeftBrace:
                        braceCount++;
                        break;
                    case TokenType.RightBrace:
                        braceCount--;
                        if (braceCount == 0)
                            return foundNonWhitespace && !foundEquals;
                        break;
                    case TokenType.Equals:
                        foundEquals = true;
                        return false; // Definitely not a list
                    case TokenType.Identifier:
                    case TokenType.Number:
                    case TokenType.String:
                        foundNonWhitespace = true;
                        break;
                }

                lookAhead++;
            }

            return foundNonWhitespace && !foundEquals;
        }

        /// <summary>
        /// Try to parse special Paradox keywords
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryParseSpecialKeyword(NativeSlice<byte> data, out ParadoxParser.Core.ParadoxParser.ParsedValue value)
        {
            value = default;

            // Check for yes/no
            if (IsKeywordYes(data))
            {
                value = CreateBooleanValue(true);
                return true;
            }
            if (IsKeywordNo(data))
            {
                value = CreateBooleanValue(false);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try to parse RGB color values
        /// </summary>
        private bool TryParseColorValue(ref ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp)
        {
            // Check if key suggests this is a color
            if (!IsColorKey(kvp.Key))
                return false;

            var token = GetCurrentToken();
            if (token.Type == TokenType.LeftBrace)
            {
                // Parse "rgb { r g b }" format
                return ParseRGBBlock(ref kvp);
            }

            return false;
        }

        /// <summary>
        /// Parse RGB block format
        /// </summary>
        private bool ParseRGBBlock(ref ParadoxParser.Core.ParadoxParser.ParsedKeyValue kvp)
        {
            // Implementation would parse "rgb { 255 128 64 }" format
            // For now, treat as regular block
            return ParseBlockValue(ref kvp);
        }

        // Helper methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Token GetCurrentToken()
        {
            return _currentTokenIndex < _tokens.Length ? _tokens[_currentTokenIndex] :
                   Token.CreateEndOfFile(_currentTokenIndex, _currentState.LineNumber, _currentState.ColumnNumber);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AdvanceToken()
        {
            if (_currentTokenIndex < _tokens.Length)
            {
                var token = _tokens[_currentTokenIndex];
                if (token.Type == TokenType.Newline)
                    _currentState.NextLine();
                else
                    _currentState.NextColumn(token.Length);

                _currentTokenIndex++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SkipWhitespace()
        {
            while (_currentTokenIndex < _tokens.Length)
            {
                var token = _tokens[_currentTokenIndex];
                if (token.Type != TokenType.Whitespace)
                    break;
                AdvanceToken();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool SkipComment()
        {
            while (_currentTokenIndex < _tokens.Length)
            {
                var token = _tokens[_currentTokenIndex];
                AdvanceToken();
                if (token.Type == TokenType.Newline)
                    break;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsKeywordYes(NativeSlice<byte> data)
        {
            return data.Length == 3 &&
                   data[0] == (byte)'y' &&
                   data[1] == (byte)'e' &&
                   data[2] == (byte)'s';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsKeywordNo(NativeSlice<byte> data)
        {
            return data.Length == 2 &&
                   data[0] == (byte)'n' &&
                   data[1] == (byte)'o';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsKeywordColor(NativeSlice<byte> data)
        {
            return data.Length == 5 &&
                   data[0] == (byte)'c' &&
                   data[1] == (byte)'o' &&
                   data[2] == (byte)'l' &&
                   data[3] == (byte)'o' &&
                   data[4] == (byte)'r';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsKeywordRgb(NativeSlice<byte> data)
        {
            return data.Length == 3 &&
                   data[0] == (byte)'r' &&
                   data[1] == (byte)'g' &&
                   data[2] == (byte)'b';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsColorKey(NativeSlice<byte> key)
        {
            return IsKeywordColor(key) || IsKeywordRgb(key);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ParadoxParser.Core.ParadoxParser.ParsedValue CreateBooleanValue(bool value)
        {
            return new ParadoxParser.Core.ParadoxParser.ParsedValue
            {
                Type = ParadoxParser.Core.ParadoxParser.ParsedValueType.Literal,
                RawData = default,
                BlockStartIndex = value ? 1 : 0, // Use BlockStartIndex to store boolean value
                BlockLength = 0
            };
        }
    }
}