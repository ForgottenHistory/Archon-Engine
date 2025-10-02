using System;

namespace ParadoxParser.Core
{
    /// <summary>
    /// Parser state enumeration for Paradox file parsing
    /// Represents the current context and expected next tokens
    /// </summary>
    public enum ParserState : byte
    {
        /// <summary>
        /// Initial state - expecting key or end of file
        /// </summary>
        Initial = 0,

        /// <summary>
        /// Expecting a key (identifier) at the start of a line or block
        /// </summary>
        ExpectingKey = 1,

        /// <summary>
        /// Expecting an equals sign after a key
        /// </summary>
        ExpectingEquals = 2,

        /// <summary>
        /// Expecting a value after equals (could be literal, block, or list)
        /// </summary>
        ExpectingValue = 3,

        /// <summary>
        /// Inside a block (between { and })
        /// </summary>
        InBlock = 4,

        /// <summary>
        /// Inside a list (between { and } but parsing space-separated values)
        /// </summary>
        InList = 5,

        /// <summary>
        /// Parsing a quoted string value
        /// </summary>
        InQuotedString = 6,

        /// <summary>
        /// Parsing an unquoted identifier or literal value
        /// </summary>
        InLiteral = 7,

        /// <summary>
        /// Parsing a numeric value (integer, float, or date)
        /// </summary>
        InNumber = 8,

        /// <summary>
        /// Parsing a date value (YYYY.MM.DD format)
        /// </summary>
        InDate = 9,

        /// <summary>
        /// Expecting end of statement (newline, comment, or block end)
        /// </summary>
        ExpectingEndOfStatement = 10,

        /// <summary>
        /// Inside a comment (# to end of line)
        /// </summary>
        InComment = 11,

        /// <summary>
        /// Error state - parsing failed
        /// </summary>
        Error = 255
    }

    /// <summary>
    /// Parser state information with additional context
    /// </summary>
    public struct ParserStateInfo
    {
        public ParserState State;
        public int BlockDepth;
        public int LineNumber;
        public int ColumnNumber;
        public bool IsInList;
        public bool ExpectingListItem;

        public ParserStateInfo(ParserState state = ParserState.Initial)
        {
            State = state;
            BlockDepth = 0;
            LineNumber = 1;
            ColumnNumber = 1;
            IsInList = false;
            ExpectingListItem = false;
        }

        /// <summary>
        /// Enter a new block (increase depth)
        /// </summary>
        public void EnterBlock(bool isList = false)
        {
            BlockDepth++;
            State = isList ? ParserState.InList : ParserState.InBlock;
            IsInList = isList;
            ExpectingListItem = isList;
        }

        /// <summary>
        /// Exit current block (decrease depth)
        /// </summary>
        public void ExitBlock()
        {
            if (BlockDepth > 0)
            {
                BlockDepth--;
                State = BlockDepth > 0 ? ParserState.InBlock : ParserState.ExpectingEndOfStatement;
                IsInList = false;
                ExpectingListItem = false;
            }
        }

        /// <summary>
        /// Advance to next line
        /// </summary>
        public void NextLine()
        {
            LineNumber++;
            ColumnNumber = 1;
        }

        /// <summary>
        /// Advance column position
        /// </summary>
        public void NextColumn(int count = 1)
        {
            ColumnNumber += count;
        }

        /// <summary>
        /// Check if we're at the root level (not in any blocks)
        /// </summary>
        public readonly bool IsAtRootLevel => BlockDepth == 0;

        /// <summary>
        /// Check if we're inside any kind of block or list
        /// </summary>
        public readonly bool IsInContainer => BlockDepth > 0;
    }

    /// <summary>
    /// State transition validation and logic
    /// </summary>
    public static class ParserStateTransitions
    {
        /// <summary>
        /// Check if a state transition is valid
        /// </summary>
        public static bool IsValidTransition(ParserState from, ParserState to)
        {
            return from switch
            {
                ParserState.Initial => to is ParserState.ExpectingKey or ParserState.InComment,

                ParserState.ExpectingKey => to is ParserState.ExpectingEquals or ParserState.InComment or ParserState.Error,

                ParserState.ExpectingEquals => to is ParserState.ExpectingValue or ParserState.InComment or ParserState.Error,

                ParserState.ExpectingValue => to is ParserState.InLiteral or ParserState.InNumber or ParserState.InDate or
                                                  ParserState.InQuotedString or ParserState.InBlock or ParserState.InList or
                                                  ParserState.InComment or ParserState.Error,

                ParserState.InBlock => to is ParserState.ExpectingKey or ParserState.ExpectingEndOfStatement or
                                           ParserState.InComment or ParserState.Error,

                ParserState.InList => to is ParserState.InLiteral or ParserState.InNumber or ParserState.InDate or
                                          ParserState.InQuotedString or ParserState.ExpectingEndOfStatement or
                                          ParserState.InComment or ParserState.Error,

                ParserState.InQuotedString => to is ParserState.ExpectingEndOfStatement or ParserState.InComment or ParserState.Error,

                ParserState.InLiteral => to is ParserState.ExpectingEndOfStatement or ParserState.ExpectingEquals or
                                             ParserState.InComment or ParserState.Error,

                ParserState.InNumber => to is ParserState.ExpectingEndOfStatement or ParserState.InComment or ParserState.Error,

                ParserState.InDate => to is ParserState.ExpectingEndOfStatement or ParserState.InComment or ParserState.Error,

                ParserState.ExpectingEndOfStatement => to is ParserState.ExpectingKey or ParserState.InComment or ParserState.Error,

                ParserState.InComment => to is ParserState.ExpectingKey or ParserState.ExpectingEndOfStatement or ParserState.Error,

                ParserState.Error => false, // Can't transition out of error state

                _ => false
            };
        }

        /// <summary>
        /// Get the expected next state based on current state and context
        /// </summary>
        public static ParserState GetExpectedNextState(ParserStateInfo stateInfo)
        {
            return stateInfo.State switch
            {
                ParserState.Initial => ParserState.ExpectingKey,
                ParserState.ExpectingKey => ParserState.ExpectingEquals,
                ParserState.ExpectingEquals => ParserState.ExpectingValue,
                ParserState.ExpectingValue => ParserState.InLiteral, // Default - will be refined based on token
                ParserState.InBlock => ParserState.ExpectingKey,
                ParserState.InList when stateInfo.ExpectingListItem => ParserState.InLiteral,
                ParserState.InList => ParserState.ExpectingEndOfStatement,
                ParserState.InQuotedString => ParserState.ExpectingEndOfStatement,
                ParserState.InLiteral => stateInfo.IsInList ? ParserState.InList : ParserState.ExpectingEndOfStatement,
                ParserState.InNumber => ParserState.ExpectingEndOfStatement,
                ParserState.InDate => ParserState.ExpectingEndOfStatement,
                ParserState.ExpectingEndOfStatement => stateInfo.IsInContainer ? ParserState.ExpectingKey : ParserState.ExpectingKey,
                ParserState.InComment => ParserState.ExpectingKey,
                _ => ParserState.Error
            };
        }
    }
}