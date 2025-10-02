using System;
using Unity.Collections;
using Unity.Jobs;
using ParadoxParser.Core;
using ParadoxParser.Tokenization;

namespace ParadoxParser.Jobs
{
    /// <summary>
    /// Interface for Paradox file parsing jobs
    /// Provides a unified API for different parsing strategies
    /// </summary>
    public interface IParseJob : IJob
    {
        /// <summary>
        /// Input data containing the raw file content
        /// </summary>
        NativeSlice<byte> InputData { get; set; }

        /// <summary>
        /// Pre-tokenized input (optional - will tokenize if not provided)
        /// </summary>
        NativeSlice<Token> InputTokens { get; set; }

        /// <summary>
        /// Output parsed key-value pairs
        /// </summary>
        NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> OutputKeyValues { get; set; }

        /// <summary>
        /// Output for nested blocks/lists
        /// </summary>
        NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> OutputBlocks { get; set; }

        /// <summary>
        /// Parse result information
        /// </summary>
        ParadoxParser.Core.ParadoxParser.ParseResult Result { get; }

        /// <summary>
        /// Parsing options and flags
        /// </summary>
        ParseJobOptions Options { get; set; }
    }

    /// <summary>
    /// Configuration options for parse jobs
    /// </summary>
    [Serializable]
    public struct ParseJobOptions
    {
        /// <summary>
        /// Whether to parse color values (e.g., "rgb { 255 128 64 }")
        /// </summary>
        public bool ParseColors;

        /// <summary>
        /// Whether to handle special Paradox keywords (yes/no, etc.)
        /// </summary>
        public bool ParseSpecialKeywords;

        /// <summary>
        /// Whether to support modifier blocks (e.g., "modifier = { ... }")
        /// </summary>
        public bool ParseModifierBlocks;

        /// <summary>
        /// Whether to process include directives
        /// </summary>
        public bool ProcessIncludes;

        /// <summary>
        /// Maximum parsing depth (prevents infinite recursion)
        /// </summary>
        public int MaxDepth;

        /// <summary>
        /// Whether to preserve comments in output
        /// </summary>
        public bool PreserveComments;

        /// <summary>
        /// Whether to validate date formats strictly
        /// </summary>
        public bool StrictDateValidation;

        /// <summary>
        /// Default options for most Paradox files
        /// </summary>
        public static ParseJobOptions Default => new ParseJobOptions
        {
            ParseColors = true,
            ParseSpecialKeywords = true,
            ParseModifierBlocks = true,
            ProcessIncludes = false, // Usually handled at higher level
            MaxDepth = 64,
            PreserveComments = false,
            StrictDateValidation = true
        };

        /// <summary>
        /// Minimal options for fast parsing
        /// </summary>
        public static ParseJobOptions Fast => new ParseJobOptions
        {
            ParseColors = false,
            ParseSpecialKeywords = false,
            ParseModifierBlocks = false,
            ProcessIncludes = false,
            MaxDepth = 32,
            PreserveComments = false,
            StrictDateValidation = false
        };

        /// <summary>
        /// Complete options for thorough parsing
        /// </summary>
        public static ParseJobOptions Complete => new ParseJobOptions
        {
            ParseColors = true,
            ParseSpecialKeywords = true,
            ParseModifierBlocks = true,
            ProcessIncludes = true,
            MaxDepth = 128,
            PreserveComments = true,
            StrictDateValidation = true
        };
    }

    /// <summary>
    /// Parse error details
    /// </summary>
    public struct ParseError
    {
        public ParserStateMachine.ParseErrorType Type;
        public int Line;
        public int Column;
        public int ByteOffset;
        public NativeSlice<byte> Context; // Surrounding text for debugging

        public bool HasError => Type != ParserStateMachine.ParseErrorType.None;

        public static ParseError None => new ParseError
        {
            Type = ParserStateMachine.ParseErrorType.None,
            Line = 0,
            Column = 0,
            ByteOffset = 0,
            Context = default
        };

        public static ParseError Create(ParserStateMachine.ParseErrorType type, int line, int column, int byteOffset)
        {
            return new ParseError
            {
                Type = type,
                Line = line,
                Column = column,
                ByteOffset = byteOffset,
                Context = default
            };
        }
    }

    /// <summary>
    /// Extended parse result with detailed information
    /// </summary>
    public struct ExtendedParseResult
    {
        public bool Success;
        public int TokensProcessed;
        public int KeyValuePairsFound;
        public int BlocksFound;
        public ParseError Error;
        public float ParseTimeMs;

        public static ExtendedParseResult Successful(int tokensProcessed, int keyValuePairs, int blocks, float parseTime)
        {
            return new ExtendedParseResult
            {
                Success = true,
                TokensProcessed = tokensProcessed,
                KeyValuePairsFound = keyValuePairs,
                BlocksFound = blocks,
                Error = ParseError.None,
                ParseTimeMs = parseTime
            };
        }

        public static ExtendedParseResult Failed(ParseError error, int tokensProcessed, float parseTime)
        {
            return new ExtendedParseResult
            {
                Success = false,
                TokensProcessed = tokensProcessed,
                KeyValuePairsFound = 0,
                BlocksFound = 0,
                Error = error,
                ParseTimeMs = parseTime
            };
        }
    }

    /// <summary>
    /// Base class for parse job implementations
    /// Provides common functionality and validation
    /// </summary>
    public abstract class BaseParseJob : IParseJob
    {
        public NativeSlice<byte> InputData { get; set; }
        public NativeSlice<Token> InputTokens { get; set; }
        public NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> OutputKeyValues { get; set; }
        public NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> OutputBlocks { get; set; }
        public ParadoxParser.Core.ParadoxParser.ParseResult Result { get; protected set; }
        public ParseJobOptions Options { get; set; }

        protected BaseParseJob()
        {
            Options = ParseJobOptions.Default;
        }

        /// <summary>
        /// Validate inputs before parsing
        /// </summary>
        protected virtual bool ValidateInputs()
        {
            if (InputData.Length == 0 && InputTokens.Length == 0)
            {
                Result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                    ParserStateMachine.ParseErrorType.UnexpectedEndOfFile, 1, 1, 0);
                return false;
            }

            if (!OutputKeyValues.IsCreated)
            {
                Result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                    ParserStateMachine.ParseErrorType.InvalidStateTransition, 1, 1, 0);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Abstract method for implementing the parsing logic
        /// </summary>
        protected abstract void ExecuteParseLogic();

        /// <summary>
        /// IJob.Execute implementation
        /// </summary>
        public void Execute()
        {
            if (!ValidateInputs())
                return;

            try
            {
                ExecuteParseLogic();
            }
            catch (System.Exception)
            {
                Result = ParadoxParser.Core.ParadoxParser.ParseResult.Failed(
                    ParserStateMachine.ParseErrorType.InvalidStateTransition, 1, 1, 0);
            }
        }
    }
}