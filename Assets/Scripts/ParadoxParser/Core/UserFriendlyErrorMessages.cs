using System;
using System.Runtime.InteropServices;
using Unity.Collections;

namespace ParadoxParser.Core
{
    public enum MessageAudience : byte
    {
        Developer = 0,
        ContentCreator = 1,
        EndUser = 2,
        Debug = 3
    }

    public enum MessageStyle : byte
    {
        Technical = 0,
        Friendly = 1,
        Concise = 2,
        Detailed = 3,
        Instructional = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UserFriendlyMessage
    {
        public FixedString512Bytes Title;
        public FixedString512Bytes Description;
        public FixedString512Bytes Suggestion;
        public FixedString128Bytes Category;
        public MessageAudience TargetAudience;
        public MessageStyle Style;
        public bool IncludesExample;
        public bool IncludesTechnicalDetails;

        public static UserFriendlyMessage Create(string title, string description, string suggestion = "", MessageAudience audience = MessageAudience.ContentCreator, MessageStyle style = MessageStyle.Friendly)
        {
            return new UserFriendlyMessage
            {
                Title = new FixedString512Bytes(title),
                Description = new FixedString512Bytes(description),
                Suggestion = new FixedString512Bytes(suggestion ?? ""),
                Category = new FixedString128Bytes(""),
                TargetAudience = audience,
                Style = style,
                IncludesExample = false,
                IncludesTechnicalDetails = false
            };
        }

        public override string ToString()
        {
            string result = Title.ToString();
            if (Description.Length > 0)
            {
                result += $"\n{Description}";
            }
            if (Suggestion.Length > 0)
            {
                result += $"\n💡 {Suggestion}";
            }
            return result;
        }
    }

    public static class UserFriendlyErrorMessages
    {
        public static UserFriendlyMessage GetUserFriendlyMessage(ErrorCode errorCode, MessageAudience audience = MessageAudience.ContentCreator, MessageStyle style = MessageStyle.Friendly)
        {
            return errorCode switch
            {
                // File I/O Errors
                ErrorCode.FileNotFound => CreateFileNotFoundMessage(audience, style),
                ErrorCode.FileAccessDenied => CreateFileAccessDeniedMessage(audience, style),
                ErrorCode.FileCorrupted => CreateFileCorruptedMessage(audience, style),
                ErrorCode.FileTooLarge => CreateFileTooLargeMessage(audience, style),
                ErrorCode.FileFormatUnsupported => CreateFileFormatUnsupportedMessage(audience, style),

                // Parse Errors
                ErrorCode.ParseSyntaxError => CreateParseSyntaxErrorMessage(audience, style),
                ErrorCode.ParseUnexpectedToken => CreateParseUnexpectedTokenMessage(audience, style),
                ErrorCode.ParseUnterminatedString => CreateParseUnterminatedStringMessage(audience, style),
                ErrorCode.ParseInvalidNumber => CreateParseInvalidNumberMessage(audience, style),
                ErrorCode.ParseInvalidDate => CreateParseInvalidDateMessage(audience, style),
                ErrorCode.ParseMissingBrace => CreateParseMissingBraceMessage(audience, style),
                ErrorCode.ParseExtraBrace => CreateParseExtraBraceMessage(audience, style),
                ErrorCode.ParseInvalidOperator => CreateParseInvalidOperatorMessage(audience, style),
                ErrorCode.ParseInvalidIdentifier => CreateParseInvalidIdentifierMessage(audience, style),
                ErrorCode.ParseMissingAssignment => CreateParseMissingAssignmentMessage(audience, style),
                ErrorCode.ParseCircularReference => CreateParseCircularReferenceMessage(audience, style),
                ErrorCode.ParseDepthExceeded => CreateParseDepthExceededMessage(audience, style),

                // Validation Errors
                ErrorCode.ValidationFieldRequired => CreateValidationFieldRequiredMessage(audience, style),
                ErrorCode.ValidationFieldInvalid => CreateValidationFieldInvalidMessage(audience, style),
                ErrorCode.ValidationRangeError => CreateValidationRangeErrorMessage(audience, style),
                ErrorCode.ValidationTypeError => CreateValidationTypeErrorMessage(audience, style),
                ErrorCode.ValidationDuplicateKey => CreateValidationDuplicateKeyMessage(audience, style),
                ErrorCode.ValidationMissingReference => CreateValidationMissingReferenceMessage(audience, style),

                // Memory & System Errors
                ErrorCode.MemoryAllocationFailed => CreateMemoryAllocationFailedMessage(audience, style),
                ErrorCode.SystemResourceUnavailable => CreateSystemResourceUnavailableMessage(audience, style),
                ErrorCode.PerformanceTimeout => CreatePerformanceTimeoutMessage(audience, style),

                // Default
                _ => CreateGenericErrorMessage(errorCode, audience, style)
            };
        }

        public static string FormatErrorForUser(ErrorResult error, MessageAudience audience = MessageAudience.ContentCreator, bool includeLocation = true, bool includeRecoveryHint = true)
        {
            var message = GetUserFriendlyMessage(error.Code, audience);
            string result = message.ToString();

            if (includeLocation && error.Line >= 0)
            {
                string location = $"Line {error.Line}";
                if (error.Column >= 0)
                {
                    location += $", Column {error.Column}";
                }
                result = $"📍 {location}\n{result}";
            }

            if (includeRecoveryHint && ErrorUtilities.IsRecoverable(error.Code))
            {
                var strategy = ErrorRecoveryUtilities.GetRecommendedStrategy(error.Code, error.Severity);
                string recoveryHint = GetRecoveryHintForUser(strategy, audience);
                if (!string.IsNullOrEmpty(recoveryHint))
                {
                    result += $"\n🔧 {recoveryHint}";
                }
            }

            return result;
        }

        public static string GetContextualHelp(ErrorCode errorCode, MessageAudience audience = MessageAudience.ContentCreator)
        {
            return errorCode switch
            {
                ErrorCode.ParseSyntaxError => GetSyntaxErrorHelp(audience),
                ErrorCode.ParseInvalidDate => GetDateFormatHelp(audience),
                ErrorCode.ValidationDuplicateKey => GetDuplicateKeyHelp(audience),
                ErrorCode.ParseCircularReference => GetCircularReferenceHelp(audience),
                _ => ""
            };
        }

        #region File I/O Error Messages

        private static UserFriendlyMessage CreateFileNotFoundMessage(MessageAudience audience, MessageStyle style)
        {
            return audience switch
            {
                MessageAudience.EndUser => UserFriendlyMessage.Create(
                    "File Not Found",
                    "The game couldn't find a required file. This might happen if files were moved or deleted.",
                    "Check that all game files are in the correct folders, or try reinstalling the mod.",
                    audience, style),

                MessageAudience.ContentCreator => UserFriendlyMessage.Create(
                    "Missing File",
                    "A file referenced in your mod could not be found. This often happens when files are moved or renamed without updating references.",
                    "Verify the file path is correct and the file exists in the expected location.",
                    audience, style),

                _ => UserFriendlyMessage.Create(
                    "File Not Found",
                    "The specified file path does not exist or is inaccessible.",
                    "Check file path and permissions.",
                    audience, style)
            };
        }

        private static UserFriendlyMessage CreateFileAccessDeniedMessage(MessageAudience audience, MessageStyle style)
        {
            return audience switch
            {
                MessageAudience.EndUser => UserFriendlyMessage.Create(
                    "File Access Denied",
                    "The game doesn't have permission to read or write a necessary file.",
                    "Try running the game as administrator, or check if the file is write-protected.",
                    audience, style),

                _ => UserFriendlyMessage.Create(
                    "Access Denied",
                    "Insufficient permissions to access the file.",
                    "Check file permissions and ensure the application has read/write access.",
                    audience, style)
            };
        }

        private static UserFriendlyMessage CreateFileCorruptedMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Corrupted File",
                "The file appears to be damaged or corrupted and cannot be read properly.",
                "Try restoring the file from backup or reinstalling the mod.",
                audience, style);
        }

        private static UserFriendlyMessage CreateFileTooLargeMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "File Too Large",
                "The file is too large to process. This can happen with very large mod files.",
                "Consider splitting large files into smaller ones or optimizing the content.",
                audience, style);
        }

        private static UserFriendlyMessage CreateFileFormatUnsupportedMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Unsupported File Format",
                "The file format is not recognized or supported.",
                "Ensure you're using the correct Paradox file format (.txt, .yml, etc.).",
                audience, style);
        }

        #endregion

        #region Parse Error Messages

        private static UserFriendlyMessage CreateParseSyntaxErrorMessage(MessageAudience audience, MessageStyle style)
        {
            return audience switch
            {
                MessageAudience.ContentCreator => UserFriendlyMessage.Create(
                    "Syntax Error",
                    "There's a problem with the structure or format of your script. This usually means missing brackets, quotes, or incorrect formatting.",
                    "Check for missing brackets { }, quotes \" \", or incorrect indentation around this area.",
                    audience, style),

                _ => UserFriendlyMessage.Create(
                    "Syntax Error",
                    "The file contains invalid syntax or formatting.",
                    "Review the file structure and fix any formatting issues.",
                    audience, style)
            };
        }

        private static UserFriendlyMessage CreateParseUnexpectedTokenMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Unexpected Symbol",
                "Found a symbol or word that doesn't belong in this context.",
                "Check for typos, missing operators, or symbols in the wrong place.",
                audience, style);
        }

        private static UserFriendlyMessage CreateParseUnterminatedStringMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Missing Closing Quote",
                "A text string was started with a quote but never closed.",
                "Add the missing closing quote (\") at the end of the text.",
                audience, style);
        }

        private static UserFriendlyMessage CreateParseInvalidNumberMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Invalid Number",
                "Expected a number but found something else, or the number format is incorrect.",
                "Use only digits, decimal points, and minus signs for numbers (e.g., 123, -45.67).",
                audience, style);
        }

        private static UserFriendlyMessage CreateParseInvalidDateMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Invalid Date Format",
                "The date is not in the expected format for Paradox games.",
                "Use the format YYYY.MM.DD (e.g., 1444.11.11 for November 11th, 1444).",
                audience, style);
        }

        private static UserFriendlyMessage CreateParseMissingBraceMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Missing Closing Bracket",
                "An opening bracket { was found but no matching closing bracket }.",
                "Add the missing closing bracket } to match the opening bracket.",
                audience, style);
        }

        private static UserFriendlyMessage CreateParseExtraBraceMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Extra Closing Bracket",
                "Found a closing bracket } without a matching opening bracket {.",
                "Remove the extra bracket or add a matching opening bracket.",
                audience, style);
        }

        private static UserFriendlyMessage CreateParseInvalidOperatorMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Invalid Operator",
                "Found an operator (like =, >, <) that's not valid in this context.",
                "Check that you're using the correct operator for this type of assignment or comparison.",
                audience, style);
        }

        private static UserFriendlyMessage CreateParseInvalidIdentifierMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Invalid Name/Identifier",
                "A name or identifier contains invalid characters or doesn't follow naming rules.",
                "Names should start with a letter and contain only letters, numbers, and underscores.",
                audience, style);
        }

        private static UserFriendlyMessage CreateParseMissingAssignmentMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Missing Assignment",
                "Expected an equals sign (=) to assign a value.",
                "Add an equals sign (=) between the property name and its value.",
                audience, style);
        }

        private static UserFriendlyMessage CreateParseCircularReferenceMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Circular Reference",
                "Found a reference loop where items reference each other in a circle, which would cause infinite loops.",
                "Review your references and remove any circular dependencies.",
                audience, style);
        }

        private static UserFriendlyMessage CreateParseDepthExceededMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Too Much Nesting",
                "The file has too many nested levels of brackets and structures.",
                "Simplify the structure by reducing nesting levels or splitting into multiple files.",
                audience, style);
        }

        #endregion

        #region Validation Error Messages

        private static UserFriendlyMessage CreateValidationFieldRequiredMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Missing Required Field",
                "A required property or field is missing from this definition.",
                "Add the missing field with an appropriate value.",
                audience, style);
        }

        private static UserFriendlyMessage CreateValidationFieldInvalidMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Invalid Field Value",
                "The value for this field is not valid or acceptable.",
                "Check the documentation for valid values for this field.",
                audience, style);
        }

        private static UserFriendlyMessage CreateValidationRangeErrorMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Value Out of Range",
                "The number is outside the allowed range for this field.",
                "Use a value within the valid range for this property.",
                audience, style);
        }

        private static UserFriendlyMessage CreateValidationTypeErrorMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Wrong Data Type",
                "Expected a different type of value (e.g., expected number but got text).",
                "Check that you're using the correct type of value for this field.",
                audience, style);
        }

        private static UserFriendlyMessage CreateValidationDuplicateKeyMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Duplicate Definition",
                "This item is defined more than once, which can cause conflicts.",
                "Remove the duplicate definition or rename one of them.",
                audience, style);
        }

        private static UserFriendlyMessage CreateValidationMissingReferenceMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Missing Reference",
                "This refers to something that doesn't exist or hasn't been defined.",
                "Make sure the referenced item exists and is spelled correctly.",
                audience, style);
        }

        #endregion

        #region System Error Messages

        private static UserFriendlyMessage CreateMemoryAllocationFailedMessage(MessageAudience audience, MessageStyle style)
        {
            return audience switch
            {
                MessageAudience.EndUser => UserFriendlyMessage.Create(
                    "Out of Memory",
                    "The game has run out of available memory and cannot continue.",
                    "Try closing other applications or restarting the game.",
                    audience, style),

                _ => UserFriendlyMessage.Create(
                    "Memory Allocation Failed",
                    "Unable to allocate sufficient memory for the operation.",
                    "Reduce memory usage or increase available system memory.",
                    audience, style)
            };
        }

        private static UserFriendlyMessage CreateSystemResourceUnavailableMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "System Resource Unavailable",
                "A required system resource is not available or has been exhausted.",
                "Close other applications and try again, or restart your computer.",
                audience, style);
        }

        private static UserFriendlyMessage CreatePerformanceTimeoutMessage(MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Operation Timed Out",
                "The operation took too long to complete and was cancelled to prevent freezing.",
                "Try processing smaller files or check for very complex nested structures.",
                audience, style);
        }

        #endregion

        private static UserFriendlyMessage CreateGenericErrorMessage(ErrorCode errorCode, MessageAudience audience, MessageStyle style)
        {
            return UserFriendlyMessage.Create(
                "Unexpected Error",
                $"An unexpected error occurred: {errorCode}",
                "Check the technical documentation or report this issue.",
                audience, style);
        }

        private static string GetRecoveryHintForUser(RecoveryStrategy strategy, MessageAudience audience)
        {
            return strategy switch
            {
                RecoveryStrategy.Skip => "This error can be skipped - processing will continue.",
                RecoveryStrategy.SkipToNextLine => "Moving to the next line to continue processing.",
                RecoveryStrategy.UseDefault => "A default value will be used instead.",
                RecoveryStrategy.IgnoreAndContinue => "This error can be safely ignored.",
                RecoveryStrategy.RepairAndContinue => "Attempting automatic repair.",
                RecoveryStrategy.AbortProcessing => "This error requires manual intervention.",
                _ => ""
            };
        }

        #region Contextual Help

        private static string GetSyntaxErrorHelp(MessageAudience audience)
        {
            return audience switch
            {
                MessageAudience.ContentCreator =>
                    "Common syntax issues:\n" +
                    "• Missing brackets: every { needs a matching }\n" +
                    "• Missing quotes: text values need \"quotes\"\n" +
                    "• Missing equals signs: property = value\n" +
                    "• Incorrect indentation: use tabs consistently",

                _ => "Check file syntax and formatting."
            };
        }

        private static string GetDateFormatHelp(MessageAudience audience)
        {
            return "Paradox date format: YYYY.MM.DD\n" +
                   "Examples:\n" +
                   "• 1444.11.11 (November 11, 1444)\n" +
                   "• 1936.1.1 (January 1, 1936)\n" +
                   "• 1066.10.14 (October 14, 1066)";
        }

        private static string GetDuplicateKeyHelp(MessageAudience audience)
        {
            return "Each identifier must be unique. If you need multiple similar items:\n" +
                   "• Use different names (e.g., event_1, event_2)\n" +
                   "• Use namespaces or prefixes\n" +
                   "• Consider using lists instead of multiple definitions";
        }

        private static string GetCircularReferenceHelp(MessageAudience audience)
        {
            return "Circular references create infinite loops. Common causes:\n" +
                   "• Country A is a vassal of Country B, which is a vassal of Country A\n" +
                   "• Event A triggers Event B, which triggers Event A\n" +
                   "• Province ownership chains that loop back";
        }

        #endregion
    }

    public static class ErrorMessageCustomization
    {
        public static UserFriendlyMessage CustomizeForContext(UserFriendlyMessage baseMessage, string fileName, int lineNumber, string contextInfo = "")
        {
            var customized = baseMessage;

            if (!string.IsNullOrEmpty(fileName))
            {
                string fileContext = $"In file: {fileName}";
                if (lineNumber > 0)
                {
                    fileContext += $" (line {lineNumber})";
                }

                var newDescription = $"{fileContext}\n{baseMessage.Description}";
                customized.Description = new FixedString512Bytes(newDescription);
            }

            if (!string.IsNullOrEmpty(contextInfo))
            {
                var newSuggestion = $"{baseMessage.Suggestion}\nContext: {contextInfo}";
                customized.Suggestion = new FixedString512Bytes(newSuggestion);
            }

            return customized;
        }

        public static string FormatForPlatform(UserFriendlyMessage message, bool useRichText = true, bool useEmojis = true)
        {
            string result = message.ToString();

            if (!useEmojis)
            {
                result = result.Replace("📍", "[Location]")
                              .Replace("💡", "[Tip]")
                              .Replace("🔧", "[Fix]")
                              .Replace("⚠️", "[Warning]")
                              .Replace("❌", "[Error]")
                              .Replace("✅", "[Success]");
            }

            if (!useRichText)
            {
                // Strip any rich text tags if present
                result = System.Text.RegularExpressions.Regex.Replace(result, "<[^>]*>", "");
            }

            return result;
        }
    }
}