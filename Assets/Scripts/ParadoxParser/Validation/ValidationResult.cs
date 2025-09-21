using System;
using Unity.Collections;

namespace ParadoxParser.Validation
{
    /// <summary>
    /// Severity levels for validation messages
    /// </summary>
    public enum ValidationSeverity : byte
    {
        Info = 0,
        Warning = 1,
        Error = 2,
        Critical = 3
    }

    /// <summary>
    /// Types of validation errors
    /// </summary>
    public enum ValidationType : byte
    {
        Syntax = 0,
        Semantic = 1,
        Type = 2,
        Range = 3,
        Reference = 4,
        Structure = 5,
        Format = 6
    }

    /// <summary>
    /// Individual validation message
    /// </summary>
    public struct ValidationMessage
    {
        public ValidationType Type;
        public ValidationSeverity Severity;
        public int Line;
        public int Column;
        public int ByteOffset;
        public NativeSlice<byte> Context;
        public uint MessageHash; // Hash of the error message for efficient lookup

        public static ValidationMessage Create(ValidationType type, ValidationSeverity severity,
            int line, int column, int byteOffset, uint messageHash)
        {
            return new ValidationMessage
            {
                Type = type,
                Severity = severity,
                Line = line,
                Column = column,
                ByteOffset = byteOffset,
                Context = default,
                MessageHash = messageHash
            };
        }

        public static ValidationMessage CreateWithContext(ValidationType type, ValidationSeverity severity,
            int line, int column, int byteOffset, uint messageHash, NativeSlice<byte> context)
        {
            return new ValidationMessage
            {
                Type = type,
                Severity = severity,
                Line = line,
                Column = column,
                ByteOffset = byteOffset,
                Context = context,
                MessageHash = messageHash
            };
        }
    }

    /// <summary>
    /// Result of validation operation
    /// </summary>
    public struct ValidationResult
    {
        public bool IsValid;
        public int ErrorCount;
        public int WarningCount;
        public NativeList<ValidationMessage> Messages;

        public static ValidationResult Valid => new ValidationResult
        {
            IsValid = true,
            ErrorCount = 0,
            WarningCount = 0,
            Messages = default
        };

        public static ValidationResult Create(NativeList<ValidationMessage> messages)
        {
            int errorCount = 0;
            int warningCount = 0;

            for (int i = 0; i < messages.Length; i++)
            {
                switch (messages[i].Severity)
                {
                    case ValidationSeverity.Error:
                    case ValidationSeverity.Critical:
                        errorCount++;
                        break;
                    case ValidationSeverity.Warning:
                        warningCount++;
                        break;
                }
            }

            return new ValidationResult
            {
                IsValid = errorCount == 0,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                Messages = messages
            };
        }

        public void AddMessage(ValidationMessage message)
        {
            if (!Messages.IsCreated)
                Messages = new NativeList<ValidationMessage>(16, Allocator.Temp);

            Messages.Add(message);

            switch (message.Severity)
            {
                case ValidationSeverity.Error:
                case ValidationSeverity.Critical:
                    ErrorCount++;
                    IsValid = false;
                    break;
                case ValidationSeverity.Warning:
                    WarningCount++;
                    break;
            }
        }

        public void Dispose()
        {
            if (Messages.IsCreated)
                Messages.Dispose();
        }
    }

    /// <summary>
    /// Configuration for validation behavior
    /// </summary>
    [Serializable]
    public struct ValidationOptions
    {
        /// <summary>
        /// Enable syntax validation
        /// </summary>
        public bool ValidateSyntax;

        /// <summary>
        /// Enable semantic validation
        /// </summary>
        public bool ValidateSemantics;

        /// <summary>
        /// Enable type checking
        /// </summary>
        public bool ValidateTypes;

        /// <summary>
        /// Enable range validation
        /// </summary>
        public bool ValidateRanges;

        /// <summary>
        /// Enable cross-reference validation
        /// </summary>
        public bool ValidateReferences;

        /// <summary>
        /// Maximum validation errors before stopping
        /// </summary>
        public int MaxErrors;

        /// <summary>
        /// Treat warnings as errors
        /// </summary>
        public bool WarningsAsErrors;

        /// <summary>
        /// Default validation options
        /// </summary>
        public static ValidationOptions Default => new ValidationOptions
        {
            ValidateSyntax = true,
            ValidateSemantics = true,
            ValidateTypes = true,
            ValidateRanges = true,
            ValidateReferences = false, // Expensive, off by default
            MaxErrors = 100,
            WarningsAsErrors = false
        };

        /// <summary>
        /// Strict validation options
        /// </summary>
        public static ValidationOptions Strict => new ValidationOptions
        {
            ValidateSyntax = true,
            ValidateSemantics = true,
            ValidateTypes = true,
            ValidateRanges = true,
            ValidateReferences = true,
            MaxErrors = 1000,
            WarningsAsErrors = true
        };

        /// <summary>
        /// Fast validation options (syntax only)
        /// </summary>
        public static ValidationOptions Fast => new ValidationOptions
        {
            ValidateSyntax = true,
            ValidateSemantics = false,
            ValidateTypes = false,
            ValidateRanges = false,
            ValidateReferences = false,
            MaxErrors = 50,
            WarningsAsErrors = false
        };
    }
}