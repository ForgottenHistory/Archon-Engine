using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ParadoxParser.Core
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ErrorResult : IEquatable<ErrorResult>
    {
        public ErrorCode Code;
        public ErrorSeverity Severity;
        public int Line;
        public int Column;
        public int ContextHash;
        public bool HasError;

        public static ErrorResult Success => new ErrorResult { HasError = false, Code = ErrorCode.None };

        public static ErrorResult Error(ErrorCode code, ErrorSeverity severity = ErrorSeverity.Error, int line = -1, int column = -1, string context = null)
        {
            return new ErrorResult
            {
                HasError = true,
                Code = code,
                Severity = severity,
                Line = line,
                Column = column,
                ContextHash = context?.GetHashCode() ?? 0
            };
        }

        public static ErrorResult Warning(ErrorCode code, int line = -1, int column = -1, string context = null)
        {
            return Error(code, ErrorSeverity.Warning, line, column, context);
        }

        public static ErrorResult Info(ErrorCode code, int line = -1, int column = -1, string context = null)
        {
            return Error(code, ErrorSeverity.Info, line, column, context);
        }

        public bool IsSuccess => !HasError;
        public bool IsError => HasError && Severity == ErrorSeverity.Error;
        public bool IsWarning => HasError && Severity == ErrorSeverity.Warning;
        public bool IsInfo => HasError && Severity == ErrorSeverity.Info;

        public bool Equals(ErrorResult other)
        {
            return Code == other.Code &&
                   Severity == other.Severity &&
                   Line == other.Line &&
                   Column == other.Column &&
                   HasError == other.HasError;
        }

        public override bool Equals(object obj) => obj is ErrorResult other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Code, Severity, Line, Column, HasError);

        public override string ToString()
        {
            if (!HasError) return "Success";

            string location = Line >= 0 ? $" at line {Line}" : "";
            if (Column >= 0) location += $", column {Column}";

            return $"{Severity}: {Code}{location}";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct Result<T> : IDisposable where T : unmanaged
    {
        private T m_Value;
        private ErrorResult m_Error;
        private bool m_HasValue;

        public bool IsSuccess => !m_Error.HasError && m_HasValue;
        public bool HasError => m_Error.HasError;
        public ErrorResult Error => m_Error;
        public T Value => IsSuccess ? m_Value : default(T);

        public static Result<T> Success(T value)
        {
            return new Result<T>
            {
                m_Value = value,
                m_Error = ErrorResult.Success,
                m_HasValue = true
            };
        }

        public static Result<T> Failure(ErrorResult error)
        {
            return new Result<T>
            {
                m_Value = default(T),
                m_Error = error,
                m_HasValue = false
            };
        }

        public static Result<T> Failure(ErrorCode code, ErrorSeverity severity = ErrorSeverity.Error, int line = -1, int column = -1)
        {
            return Failure(ErrorResult.Error(code, severity, line, column));
        }

        public bool TryGetValue(out T value)
        {
            value = IsSuccess ? m_Value : default(T);
            return IsSuccess;
        }

        public T GetValueOrDefault(T defaultValue = default(T))
        {
            return IsSuccess ? m_Value : defaultValue;
        }

        public void Dispose()
        {
            // For types that implement IDisposable, this would call their Dispose
            // For now, just reset
            m_Value = default(T);
            m_HasValue = false;
        }
    }

    public enum ErrorCode : ushort
    {
        None = 0,

        // File I/O Errors (1-99)
        FileNotFound = 1,
        FileAccessDenied = 2,
        FileCorrupted = 3,
        FileTooLarge = 4,
        FileFormatUnsupported = 5,
        DirectoryNotFound = 6,
        DiskSpaceInsufficient = 7,
        FileInUse = 8,

        // Parse Errors (100-199)
        ParseSyntaxError = 100,
        ParseUnexpectedToken = 101,
        ParseUnterminatedString = 102,
        ParseInvalidNumber = 103,
        ParseInvalidDate = 104,
        ParseMissingBrace = 105,
        ParseExtraBrace = 106,
        ParseInvalidOperator = 107,
        ParseInvalidIdentifier = 108,
        ParseMissingAssignment = 109,
        ParseCircularReference = 110,
        ParseDepthExceeded = 111,

        // Validation Errors (200-299)
        ValidationFieldRequired = 200,
        ValidationFieldInvalid = 201,
        ValidationRangeError = 202,
        ValidationTypeError = 203,
        ValidationDuplicateKey = 204,
        ValidationMissingReference = 205,
        ValidationInvalidReference = 206,
        ValidationConstraintViolation = 207,

        // Memory Errors (300-399)
        MemoryAllocationFailed = 300,
        MemoryOutOfBounds = 301,
        MemoryCorruption = 302,
        MemoryLeak = 303,
        MemoryFragmentation = 304,

        // Performance Errors (400-499)
        PerformanceTimeout = 400,
        PerformanceThresholdExceeded = 401,
        PerformanceCacheOverflow = 402,

        // Configuration Errors (500-599)
        ConfigurationMissing = 500,
        ConfigurationInvalid = 501,
        ConfigurationVersionMismatch = 502,

        // System Errors (600-699)
        SystemResourceUnavailable = 600,
        SystemPermissionDenied = 601,
        SystemCompatibilityError = 602,

        // User Errors (700-799)
        UserInputInvalid = 700,
        UserActionNotAllowed = 701,
        UserDataInconsistent = 702,

        // Unknown/Other (800+)
        UnknownError = 800,
        NotImplemented = 801,
        InternalError = 802
    }

    public static class ErrorCodeExtensions
    {
        public static bool Equals(this ErrorCode self, ErrorCode other)
        {
            return self == other;
        }

        public static int GetHashCode(this ErrorCode self)
        {
            return ((int)self).GetHashCode();
        }
    }

    public enum ErrorSeverity : byte
    {
        Info = 0,
        Warning = 1,
        Error = 2,
        Critical = 3
    }

    public static class ErrorUtilities
    {
        public static string GetErrorMessage(ErrorCode code)
        {
            return code switch
            {
                ErrorCode.None => "No error",

                // File I/O
                ErrorCode.FileNotFound => "File not found",
                ErrorCode.FileAccessDenied => "Access denied to file",
                ErrorCode.FileCorrupted => "File appears to be corrupted",
                ErrorCode.FileTooLarge => "File is too large to process",
                ErrorCode.FileFormatUnsupported => "Unsupported file format",
                ErrorCode.DirectoryNotFound => "Directory not found",
                ErrorCode.DiskSpaceInsufficient => "Insufficient disk space",
                ErrorCode.FileInUse => "File is currently in use",

                // Parse Errors
                ErrorCode.ParseSyntaxError => "Syntax error in file",
                ErrorCode.ParseUnexpectedToken => "Unexpected token",
                ErrorCode.ParseUnterminatedString => "Unterminated string",
                ErrorCode.ParseInvalidNumber => "Invalid number format",
                ErrorCode.ParseInvalidDate => "Invalid date format",
                ErrorCode.ParseMissingBrace => "Missing closing brace",
                ErrorCode.ParseExtraBrace => "Unexpected closing brace",
                ErrorCode.ParseInvalidOperator => "Invalid operator",
                ErrorCode.ParseInvalidIdentifier => "Invalid identifier",
                ErrorCode.ParseMissingAssignment => "Missing assignment operator",
                ErrorCode.ParseCircularReference => "Circular reference detected",
                ErrorCode.ParseDepthExceeded => "Maximum nesting depth exceeded",

                // Validation
                ErrorCode.ValidationFieldRequired => "Required field is missing",
                ErrorCode.ValidationFieldInvalid => "Field contains invalid data",
                ErrorCode.ValidationRangeError => "Value is outside valid range",
                ErrorCode.ValidationTypeError => "Incorrect data type",
                ErrorCode.ValidationDuplicateKey => "Duplicate key found",
                ErrorCode.ValidationMissingReference => "Referenced item not found",
                ErrorCode.ValidationInvalidReference => "Invalid reference",
                ErrorCode.ValidationConstraintViolation => "Constraint violation",

                // Memory
                ErrorCode.MemoryAllocationFailed => "Memory allocation failed",
                ErrorCode.MemoryOutOfBounds => "Memory access out of bounds",
                ErrorCode.MemoryCorruption => "Memory corruption detected",
                ErrorCode.MemoryLeak => "Memory leak detected",
                ErrorCode.MemoryFragmentation => "Memory fragmentation detected",

                // Performance
                ErrorCode.PerformanceTimeout => "Operation timed out",
                ErrorCode.PerformanceThresholdExceeded => "Performance threshold exceeded",
                ErrorCode.PerformanceCacheOverflow => "Cache overflow",

                // Configuration
                ErrorCode.ConfigurationMissing => "Configuration file missing",
                ErrorCode.ConfigurationInvalid => "Invalid configuration",
                ErrorCode.ConfigurationVersionMismatch => "Configuration version mismatch",

                // System
                ErrorCode.SystemResourceUnavailable => "System resource unavailable",
                ErrorCode.SystemPermissionDenied => "System permission denied",
                ErrorCode.SystemCompatibilityError => "System compatibility error",

                // User
                ErrorCode.UserInputInvalid => "Invalid user input",
                ErrorCode.UserActionNotAllowed => "Action not allowed",
                ErrorCode.UserDataInconsistent => "User data is inconsistent",

                // Other
                ErrorCode.UnknownError => "Unknown error occurred",
                ErrorCode.NotImplemented => "Feature not implemented",
                ErrorCode.InternalError => "Internal error",

                _ => "Unrecognized error"
            };
        }

        public static bool IsRecoverable(ErrorCode code)
        {
            return code switch
            {
                // Recoverable errors
                ErrorCode.ParseSyntaxError => true,
                ErrorCode.ParseUnexpectedToken => true,
                ErrorCode.ValidationFieldInvalid => true,
                ErrorCode.ValidationRangeError => true,
                ErrorCode.UserInputInvalid => true,
                ErrorCode.PerformanceTimeout => true,

                // Non-recoverable errors
                ErrorCode.FileNotFound => false,
                ErrorCode.FileAccessDenied => false,
                ErrorCode.MemoryAllocationFailed => false,
                ErrorCode.SystemResourceUnavailable => false,
                ErrorCode.InternalError => false,

                _ => false
            };
        }

        public static ErrorSeverity GetDefaultSeverity(ErrorCode code)
        {
            return code switch
            {
                ErrorCode.None => ErrorSeverity.Info,

                // Warnings
                ErrorCode.PerformanceThresholdExceeded => ErrorSeverity.Warning,
                ErrorCode.MemoryFragmentation => ErrorSeverity.Warning,
                ErrorCode.ValidationFieldInvalid => ErrorSeverity.Warning,

                // Critical errors
                ErrorCode.MemoryCorruption => ErrorSeverity.Critical,
                ErrorCode.SystemResourceUnavailable => ErrorSeverity.Critical,
                ErrorCode.InternalError => ErrorSeverity.Critical,

                // Everything else is Error level
                _ => ErrorSeverity.Error
            };
        }
    }
}