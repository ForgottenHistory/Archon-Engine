using System;

namespace ParadoxParser.Tokenization
{
    /// <summary>
    /// Enumeration of all possible token types in Paradox file format
    /// Covers common structures like key-value pairs, blocks, lists, and operators
    /// </summary>
    public enum TokenType : byte
    {
        // End of file/stream
        EndOfFile = 0,

        // Whitespace and formatting
        Whitespace,
        Newline,

        // Comments
        Comment,

        // Literals and values
        Identifier,         // country_tag, culture_name, etc.
        String,            // "quoted string"
        Number,            // 123, 45.67, -89
        Date,              // 1444.11.11
        Boolean,           // yes/no

        // Operators and delimiters
        Equals,            // =
        GreaterThan,       // >
        LessThan,          // <
        GreaterEquals,     // >=
        LessEquals,        // <=
        NotEquals,         // !=

        // Brackets and braces
        LeftBrace,         // {
        RightBrace,        // }
        LeftBracket,       // [
        RightBracket,      // ]
        LeftParen,         // (
        RightParen,        // )

        // Special characters
        Comma,             // ,
        Semicolon,         // ;
        Colon,             // :
        Dot,               // .
        Hash,              // #

        // Basic arithmetic operators
        Plus,              // +
        Minus,             // -
        Asterisk,          // *
        Slash,             // /
        Percent,           // %

        // Paradox-specific operators
        Add,               // += (often used in effects)
        Subtract,          // -=
        Multiply,          // *=
        Divide,            // /=
        Modulo,            // %=

        // Logical operators
        LogicalAnd,        // &&
        LogicalOr,         // ||

        // Bitwise operators
        Ampersand,         // &
        Pipe,              // |
        Caret,             // ^
        Tilde,             // ~
        LeftShift,         // <<
        RightShift,        // >>
        LeftShiftAssign,   // <<=
        RightShiftAssign,  // >>=

        // Special operators
        Exclamation,       // !
        Question,          // ?
        At,                // @
        Dollar,            // $
        Arrow,             // ->
        ScopeResolution,   // ::

        // Special tokens
        Invalid,           // Unrecognized token
        Unknown,           // Token that couldn't be classified

        // Compound tokens (combinations of above)
        Assignment,        // Complete key = value
        Block,             // Complete { ... } block
        List,              // Space or comma-separated values

        // Context-specific tokens
        Variable,          // @variable_name
        Scope,             // THIS, ROOT, PREV, etc.
        Event,             // Special event syntax
        Effect,            // Special effect syntax
        Trigger,           // Special trigger syntax

        // File structure tokens
        Namespace,         // File-level namespace declaration
        Import,            // @import or similar
        Define,            // @define or similar

        // Special values
        Null,              // Empty/null value
        Default,           // Default value indicator

        // Token count for validation
        Count
    }

    /// <summary>
    /// Extension methods for TokenType to provide utility functions
    /// </summary>
    public static class TokenTypeExtensions
    {
        /// <summary>
        /// Check if token type represents a literal value
        /// </summary>
        public static bool IsLiteral(this TokenType type)
        {
            return type >= TokenType.Identifier && type <= TokenType.Boolean;
        }

        /// <summary>
        /// Check if token type represents an operator
        /// </summary>
        public static bool IsOperator(this TokenType type)
        {
            return type >= TokenType.Equals && type <= TokenType.ScopeResolution;
        }

        /// <summary>
        /// Check if token type represents a bracket/delimiter
        /// </summary>
        public static bool IsBracket(this TokenType type)
        {
            return type >= TokenType.LeftBrace && type <= TokenType.RightParen;
        }

        /// <summary>
        /// Check if token type represents whitespace or formatting
        /// </summary>
        public static bool IsWhitespace(this TokenType type)
        {
            return type == TokenType.Whitespace || type == TokenType.Newline;
        }

        /// <summary>
        /// Check if token type should be skipped during parsing
        /// </summary>
        public static bool ShouldSkip(this TokenType type)
        {
            return type.IsWhitespace() || type == TokenType.Comment;
        }

        /// <summary>
        /// Check if token type opens a block/scope
        /// </summary>
        public static bool IsOpenBracket(this TokenType type)
        {
            return type == TokenType.LeftBrace ||
                   type == TokenType.LeftBracket ||
                   type == TokenType.LeftParen;
        }

        /// <summary>
        /// Check if token type closes a block/scope
        /// </summary>
        public static bool IsCloseBracket(this TokenType type)
        {
            return type == TokenType.RightBrace ||
                   type == TokenType.RightBracket ||
                   type == TokenType.RightParen;
        }

        /// <summary>
        /// Get the matching close bracket for an open bracket
        /// </summary>
        public static TokenType GetMatchingBracket(this TokenType openBracket)
        {
            return openBracket switch
            {
                TokenType.LeftBrace => TokenType.RightBrace,
                TokenType.LeftBracket => TokenType.RightBracket,
                TokenType.LeftParen => TokenType.RightParen,
                _ => TokenType.Invalid
            };
        }

        /// <summary>
        /// Get human-readable name for token type
        /// </summary>
        public static string GetDisplayName(this TokenType type)
        {
            return type switch
            {
                TokenType.EndOfFile => "End of File",
                TokenType.Whitespace => "Whitespace",
                TokenType.Newline => "Newline",
                TokenType.Comment => "Comment",
                TokenType.Identifier => "Identifier",
                TokenType.String => "String",
                TokenType.Number => "Number",
                TokenType.Date => "Date",
                TokenType.Boolean => "Boolean",
                TokenType.Equals => "=",
                TokenType.GreaterThan => ">",
                TokenType.LessThan => "<",
                TokenType.GreaterEquals => ">=",
                TokenType.LessEquals => "<=",
                TokenType.NotEquals => "!=",
                TokenType.LeftBrace => "{",
                TokenType.RightBrace => "}",
                TokenType.LeftBracket => "[",
                TokenType.RightBracket => "]",
                TokenType.LeftParen => "(",
                TokenType.RightParen => ")",
                TokenType.Comma => ",",
                TokenType.Semicolon => ";",
                TokenType.Colon => ":",
                TokenType.Dot => ".",
                TokenType.Hash => "#",
                TokenType.Plus => "+",
                TokenType.Minus => "-",
                TokenType.Asterisk => "*",
                TokenType.Slash => "/",
                TokenType.Percent => "%",
                TokenType.Add => "+=",
                TokenType.Subtract => "-=",
                TokenType.Multiply => "*=",
                TokenType.Divide => "/=",
                TokenType.Modulo => "%=",
                TokenType.LogicalAnd => "&&",
                TokenType.LogicalOr => "||",
                TokenType.Ampersand => "&",
                TokenType.Pipe => "|",
                TokenType.Caret => "^",
                TokenType.Tilde => "~",
                TokenType.LeftShift => "<<",
                TokenType.RightShift => ">>",
                TokenType.LeftShiftAssign => "<<=",
                TokenType.RightShiftAssign => ">>=",
                TokenType.Exclamation => "!",
                TokenType.Question => "?",
                TokenType.At => "@",
                TokenType.Dollar => "$",
                TokenType.Arrow => "->",
                TokenType.ScopeResolution => "::",
                TokenType.Variable => "Variable",
                TokenType.Scope => "Scope",
                TokenType.Invalid => "Invalid",
                TokenType.Unknown => "Unknown",
                _ => type.ToString()
            };
        }
    }
}