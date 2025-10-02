using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using ParadoxParser.Tokenization;
using ParadoxParser.Utilities;

namespace ParadoxParser.Core
{
    /// <summary>
    /// Handles include file directives in Paradox files
    /// Processes @include "filename" and similar constructs
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class IncludeFileHandler
    {
        /// <summary>
        /// Result of include file parsing
        /// </summary>
        public struct IncludeResult
        {
            public bool Success;
            public IncludeType Type;
            public NativeSlice<byte> FilePath;
            public IncludeCondition Condition;
            public int BytesConsumed;

            public static IncludeResult Failed => new IncludeResult { Success = false };

            public static IncludeResult Create(IncludeType type, NativeSlice<byte> filePath, int bytesConsumed)
            {
                return new IncludeResult
                {
                    Success = true,
                    Type = type,
                    FilePath = filePath,
                    Condition = IncludeCondition.None,
                    BytesConsumed = bytesConsumed
                };
            }

            public static IncludeResult CreateConditional(IncludeType type, NativeSlice<byte> filePath, IncludeCondition condition, int bytesConsumed)
            {
                return new IncludeResult
                {
                    Success = true,
                    Type = type,
                    FilePath = filePath,
                    Condition = condition,
                    BytesConsumed = bytesConsumed
                };
            }
        }

        /// <summary>
        /// Types of include directives
        /// </summary>
        public enum IncludeType : byte
        {
            Standard = 0,       // @include "file.txt"
            Optional,           // @include_optional "file.txt" (don't fail if missing)
            Once,               // @include_once "file.txt" (include only once)
            Conditional,        // @include_if condition "file.txt"
            Directory,          // @include_dir "directory/" (include all files in directory)
            Wildcard,           // @include "*.txt" (include matching files)
            Relative,           // @include "./relative/path.txt"
            Absolute            // @include "/absolute/path.txt"
        }

        /// <summary>
        /// Conditions for conditional includes
        /// </summary>
        public struct IncludeCondition
        {
            public ConditionType Type;
            public NativeSlice<byte> Variable;
            public NativeSlice<byte> Value;
            public bool Negated;

            public static IncludeCondition None => new IncludeCondition { Type = ConditionType.None };

            public static IncludeCondition CreateVariable(NativeSlice<byte> variable, bool negated = false)
            {
                return new IncludeCondition
                {
                    Type = ConditionType.Variable,
                    Variable = variable,
                    Value = default,
                    Negated = negated
                };
            }

            public static IncludeCondition Equals(NativeSlice<byte> variable, NativeSlice<byte> value, bool negated = false)
            {
                return new IncludeCondition
                {
                    Type = ConditionType.Equals,
                    Variable = variable,
                    Value = value,
                    Negated = negated
                };
            }
        }

        /// <summary>
        /// Types of include conditions
        /// </summary>
        public enum ConditionType : byte
        {
            None = 0,
            Variable,           // @include_if VAR "file.txt"
            Equals,             // @include_if VAR=value "file.txt"
            NotEquals,          // @include_if VAR!=value "file.txt"
            Defined,            // @include_if defined(VAR) "file.txt"
            NotDefined          // @include_if !defined(VAR) "file.txt"
        }

        /// <summary>
        /// File inclusion tracking
        /// </summary>
        public struct IncludeTracker
        {
            public NativeHashSet<uint> IncludedFiles;    // Hash set of included file paths
            public int MaxDepth;                         // Maximum include depth
            public int CurrentDepth;                     // Current nesting level

            public static IncludeTracker Create(int maxDepth = 16)
            {
                return new IncludeTracker
                {
                    IncludedFiles = new NativeHashSet<uint>(64, Allocator.Persistent),
                    MaxDepth = maxDepth,
                    CurrentDepth = 0
                };
            }

            public void Dispose()
            {
                if (IncludedFiles.IsCreated)
                    IncludedFiles.Dispose();
            }

            public bool WasIncluded(NativeSlice<byte> filePath)
            {
                uint hash = FastHasher.HashFNV1a32(filePath);
                return IncludedFiles.Contains(hash);
            }

            public void MarkIncluded(NativeSlice<byte> filePath)
            {
                uint hash = FastHasher.HashFNV1a32(filePath);
                IncludedFiles.Add(hash);
            }
        }

        /// <summary>
        /// Parse an include directive from tokens
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IncludeResult ParseInclude(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData)
        {
            if (startIndex >= tokens.Length)
                return IncludeResult.Failed;

            var token = tokens[startIndex];

            // Check for include directive patterns
            if (token.Type == TokenType.At && startIndex + 1 < tokens.Length)
            {
                var nextToken = tokens[startIndex + 1];
                if (nextToken.Type == TokenType.Identifier)
                {
                    var directiveData = sourceData.Slice(nextToken.StartPosition, nextToken.Length);
                    return ParseIncludeDirective(tokens, startIndex, sourceData, directiveData);
                }
            }

            return IncludeResult.Failed;
        }

        /// <summary>
        /// Parse specific include directive types
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IncludeResult ParseIncludeDirective(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData,
            NativeSlice<byte> directive)
        {
            // Determine include type from directive
            var includeType = GetIncludeType(directive);
            if (includeType == IncludeType.Standard && !IsIncludeDirective(directive))
                return IncludeResult.Failed;

            // Parse based on type
            return includeType switch
            {
                IncludeType.Standard => ParseStandardInclude(tokens, startIndex + 2, sourceData),
                IncludeType.Optional => ParseOptionalInclude(tokens, startIndex + 2, sourceData),
                IncludeType.Once => ParseOnceInclude(tokens, startIndex + 2, sourceData),
                IncludeType.Conditional => ParseConditionalInclude(tokens, startIndex + 2, sourceData),
                IncludeType.Directory => ParseDirectoryInclude(tokens, startIndex + 2, sourceData),
                _ => ParseStandardInclude(tokens, startIndex + 2, sourceData)
            };
        }

        /// <summary>
        /// Parse standard @include "filename" directive
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IncludeResult ParseStandardInclude(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData)
        {
            if (startIndex >= tokens.Length)
                return IncludeResult.Failed;

            var token = tokens[startIndex];
            if (token.Type != TokenType.String)
                return IncludeResult.Failed;

            var filePath = ExtractFilePath(token, sourceData);
            return IncludeResult.Create(IncludeType.Standard, filePath, 3); // @ + include + "path"
        }

        /// <summary>
        /// Parse optional include directive
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IncludeResult ParseOptionalInclude(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData)
        {
            if (startIndex >= tokens.Length)
                return IncludeResult.Failed;

            var token = tokens[startIndex];
            if (token.Type != TokenType.String)
                return IncludeResult.Failed;

            var filePath = ExtractFilePath(token, sourceData);
            return IncludeResult.Create(IncludeType.Optional, filePath, 3);
        }

        /// <summary>
        /// Parse include_once directive
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IncludeResult ParseOnceInclude(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData)
        {
            if (startIndex >= tokens.Length)
                return IncludeResult.Failed;

            var token = tokens[startIndex];
            if (token.Type != TokenType.String)
                return IncludeResult.Failed;

            var filePath = ExtractFilePath(token, sourceData);
            return IncludeResult.Create(IncludeType.Once, filePath, 3);
        }

        /// <summary>
        /// Parse conditional include directive
        /// </summary>
        private static IncludeResult ParseConditionalInclude(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData)
        {
            // Parse: @include_if CONDITION "filename"
            if (startIndex + 1 >= tokens.Length)
                return IncludeResult.Failed;

            // Parse condition
            var conditionResult = ParseIncludeCondition(tokens, startIndex, sourceData, out var conditionTokens);
            if (conditionResult.Type == ConditionType.None)
                return IncludeResult.Failed;

            // Parse file path
            int fileTokenIndex = startIndex + conditionTokens;
            if (fileTokenIndex >= tokens.Length || tokens[fileTokenIndex].Type != TokenType.String)
                return IncludeResult.Failed;

            var filePath = ExtractFilePath(tokens[fileTokenIndex], sourceData);
            return IncludeResult.CreateConditional(IncludeType.Conditional, filePath, conditionResult, 3 + conditionTokens);
        }

        /// <summary>
        /// Parse directory include directive
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IncludeResult ParseDirectoryInclude(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData)
        {
            if (startIndex >= tokens.Length)
                return IncludeResult.Failed;

            var token = tokens[startIndex];
            if (token.Type != TokenType.String)
                return IncludeResult.Failed;

            var dirPath = ExtractFilePath(token, sourceData);
            return IncludeResult.Create(IncludeType.Directory, dirPath, 3);
        }

        /// <summary>
        /// Parse include condition
        /// </summary>
        private static IncludeCondition ParseIncludeCondition(
            NativeSlice<Token> tokens,
            int startIndex,
            NativeSlice<byte> sourceData,
            out int tokensConsumed)
        {
            tokensConsumed = 0;

            if (startIndex >= tokens.Length)
                return IncludeCondition.None;

            var token = tokens[startIndex];
            if (token.Type == TokenType.Identifier)
            {
                var variable = sourceData.Slice(token.StartPosition, token.Length);

                // Check for operators
                if (startIndex + 2 < tokens.Length)
                {
                    var opToken = tokens[startIndex + 1];
                    var valueToken = tokens[startIndex + 2];

                    if (opToken.Type == TokenType.Equals && valueToken.Type == TokenType.Identifier)
                    {
                        var value = sourceData.Slice(valueToken.StartPosition, valueToken.Length);
                        tokensConsumed = 3;
                        return IncludeCondition.Equals(variable, value);
                    }
                    else if (opToken.Type == TokenType.NotEquals && valueToken.Type == TokenType.Identifier)
                    {
                        var value = sourceData.Slice(valueToken.StartPosition, valueToken.Length);
                        tokensConsumed = 3;
                        return IncludeCondition.Equals(variable, value, true);
                    }
                }

                // Simple variable check
                tokensConsumed = 1;
                return IncludeCondition.CreateVariable(variable);
            }

            return IncludeCondition.None;
        }

        /// <summary>
        /// Determine include type from directive name
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IncludeType GetIncludeType(NativeSlice<byte> directive)
        {
            if (IsKeyword(directive, "include"))
                return IncludeType.Standard;
            if (IsKeyword(directive, "include_optional"))
                return IncludeType.Optional;
            if (IsKeyword(directive, "include_once"))
                return IncludeType.Once;
            if (IsKeyword(directive, "include_if"))
                return IncludeType.Conditional;
            if (IsKeyword(directive, "include_dir"))
                return IncludeType.Directory;

            return IncludeType.Standard;
        }

        /// <summary>
        /// Check if directive is an include directive
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsIncludeDirective(NativeSlice<byte> directive)
        {
            return IsKeyword(directive, "include") ||
                   IsKeyword(directive, "include_optional") ||
                   IsKeyword(directive, "include_once") ||
                   IsKeyword(directive, "include_if") ||
                   IsKeyword(directive, "include_dir");
        }

        /// <summary>
        /// Extract file path from string token
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static NativeSlice<byte> ExtractFilePath(Token token, NativeSlice<byte> sourceData)
        {
            var tokenData = sourceData.Slice(token.StartPosition, token.Length);

            // Remove quotes if present
            if (tokenData.Length >= 2 && tokenData[0] == (byte)'"' && tokenData[tokenData.Length - 1] == (byte)'"')
            {
                return tokenData.Slice(1, tokenData.Length - 2);
            }

            return tokenData;
        }

        /// <summary>
        /// Validate include file path
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidFilePath(NativeSlice<byte> filePath)
        {
            if (filePath.Length == 0)
                return false;

            // Check for invalid characters
            for (int i = 0; i < filePath.Length; i++)
            {
                byte b = filePath[i];
                if (b < 32 || b == (byte)'<' || b == (byte)'>' || b == (byte)'|' || b == (byte)'*' || b == (byte)'?')
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Check if file path is relative
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsRelativePath(NativeSlice<byte> filePath)
        {
            if (filePath.Length == 0)
                return false;

            // Check for relative path indicators
            return filePath[0] == (byte)'.' ||
                   (filePath.Length >= 2 && filePath[0] == (byte)'.' && filePath[1] == (byte)'/') ||
                   (filePath.Length >= 3 && filePath[0] == (byte)'.' && filePath[1] == (byte)'.' && filePath[2] == (byte)'/');
        }

        /// <summary>
        /// Check if file path is absolute
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAbsolutePath(NativeSlice<byte> filePath)
        {
            if (filePath.Length == 0)
                return false;

            // Unix-style absolute path
            if (filePath[0] == (byte)'/')
                return true;

            // Windows-style absolute path
            if (filePath.Length >= 3 &&
                ((filePath[0] >= (byte)'A' && filePath[0] <= (byte)'Z') || (filePath[0] >= (byte)'a' && filePath[0] <= (byte)'z')) &&
                filePath[1] == (byte)':' && filePath[2] == (byte)'\\')
                return true;

            return false;
        }

        /// <summary>
        /// Check if file path contains wildcards
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasWildcards(NativeSlice<byte> filePath)
        {
            for (int i = 0; i < filePath.Length; i++)
            {
                if (filePath[i] == (byte)'*' || filePath[i] == (byte)'?')
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Evaluate include condition
        /// </summary>
        public static bool EvaluateCondition(IncludeCondition condition, NativeHashMap<uint, NativeSlice<byte>> variables)
        {
            if (condition.Type == ConditionType.None)
                return true;

            uint varHash = FastHasher.HashFNV1a32(condition.Variable);

            switch (condition.Type)
            {
                case ConditionType.Variable:
                    bool exists = variables.ContainsKey(varHash);
                    return condition.Negated ? !exists : exists;

                case ConditionType.Equals:
                    if (!variables.TryGetValue(varHash, out var value))
                        return condition.Negated;
                    bool equal = AreEqual(value, condition.Value);
                    return condition.Negated ? !equal : equal;

                default:
                    return true;
            }
        }

        // Helper methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsKeyword(NativeSlice<byte> data, string keyword)
        {
            if (data.Length != keyword.Length)
                return false;

            for (int i = 0; i < keyword.Length; i++)
            {
                if (data[i] != (byte)keyword[i])
                    return false;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AreEqual(NativeSlice<byte> a, NativeSlice<byte> b)
        {
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }
    }
}