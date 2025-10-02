using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace ParadoxParser.Core
{
    public static class ErrorDebugVisualizer
    {
        private static readonly Color[] SeverityColors = {
            new Color(0.7f, 0.7f, 0.7f, 1.0f),     // Info - Light Gray
            new Color(1.0f, 0.8f, 0.0f, 1.0f),     // Warning - Orange
            new Color(1.0f, 0.3f, 0.3f, 1.0f),     // Error - Red
            new Color(0.8f, 0.0f, 0.8f, 1.0f)      // Critical - Purple
        };

        public static void LogValidationResult(ValidationResult result, string context = "")
        {
            string prefix = string.IsNullOrEmpty(context) ? "" : $"[{context}] ";

            if (result.IsValid && result.TotalIssues == 0)
            {
                Debug.Log($"{prefix}✓ Validation passed successfully");
                return;
            }

            Color severityColor = GetSeverityColor(result.HighestSeverity);
            string severityIcon = GetSeverityIcon(result.HighestSeverity);

            string message = $"{prefix}{severityIcon} Validation completed with {result.TotalIssues} issues:\n" +
                           $"  • {result.ErrorCount} errors\n" +
                           $"  • {result.WarningCount} warnings\n" +
                           $"  • {result.InfoCount} info messages";

            if (result.FirstErrorCode != ErrorCode.None)
            {
                message += $"\n  First error: {result.FirstErrorCode}";
                if (result.FirstErrorLineNumber >= 0)
                {
                    message += $" at line {result.FirstErrorLineNumber}";
                    if (result.FirstErrorColumnNumber >= 0)
                    {
                        message += $", column {result.FirstErrorColumnNumber}";
                    }
                }
            }

            LogWithColor(message, severityColor, result.HighestSeverity);
        }

        public static void LogErrorResult(ErrorResult error, string context = "")
        {
            string prefix = string.IsNullOrEmpty(context) ? "" : $"[{context}] ";

            if (!error.HasError)
            {
                Debug.Log($"{prefix}✓ No error");
                return;
            }

            string icon = GetSeverityIcon(error.Severity);
            string location = "";

            if (error.Line >= 0)
            {
                location = $" at line {error.Line}";
                if (error.Column >= 0)
                {
                    location += $", column {error.Column}";
                }
            }

            string message = $"{prefix}{icon} {error.Severity}: {error.Code}{location}\n" +
                           $"  Message: {ErrorUtilities.GetErrorMessage(error.Code)}";

            Color severityColor = GetSeverityColor(error.Severity);
            LogWithColor(message, severityColor, error.Severity);
        }

        public static void LogErrorCollection(ValidationResultCollection collection, string context = "", int maxErrorsToShow = 10)
        {
            string prefix = string.IsNullOrEmpty(context) ? "" : $"[{context}] ";

            var summary = collection.GetSummary();
            LogValidationResult(summary, context);

            if (summary.TotalIssues == 0) return;

            Debug.Log($"{prefix}--- Error Details ---");

            var allErrors = collection.GetAllIssues(Allocator.Temp);
            int errorsToShow = Mathf.Min(allErrors.Length, maxErrorsToShow);

            for (int i = 0; i < errorsToShow; i++)
            {
                var error = allErrors[i];
                string icon = GetSeverityIcon(error.Severity);
                string location = "";

                if (error.Line >= 0)
                {
                    location = $" (L{error.Line}";
                    if (error.Column >= 0)
                    {
                        location += $":C{error.Column}";
                    }
                    location += ")";
                }

                string message = $"{prefix}  {i + 1}. {icon} {error.Code}{location}";
                Color severityColor = GetSeverityColor(error.Severity);
                LogWithColor(message, severityColor, error.Severity);
            }

            if (allErrors.Length > maxErrorsToShow)
            {
                Debug.Log($"{prefix}  ... and {allErrors.Length - maxErrorsToShow} more issues");
            }

            allErrors.Dispose();
        }

        public static void LogFileValidationResults(NativeArray<FileValidationResult> fileResults, string context = "", int maxFilesToShow = 10)
        {
            string prefix = string.IsNullOrEmpty(context) ? "" : $"[{context}] ";

            int totalErrors = 0;
            int totalWarnings = 0;
            int totalInfo = 0;
            int failedFiles = 0;

            for (int i = 0; i < fileResults.Length; i++)
            {
                var fileResult = fileResults[i];
                totalErrors += fileResult.Result.ErrorCount;
                totalWarnings += fileResult.Result.WarningCount;
                totalInfo += fileResult.Result.InfoCount;

                if (!fileResult.Result.IsValid) failedFiles++;
            }

            Debug.Log($"{prefix}📁 Batch validation completed:\n" +
                     $"  • {fileResults.Length} files processed\n" +
                     $"  • {failedFiles} files with errors\n" +
                     $"  • {totalErrors} total errors\n" +
                     $"  • {totalWarnings} total warnings\n" +
                     $"  • {totalInfo} total info messages");

            if (failedFiles > 0)
            {
                Debug.Log($"{prefix}--- Failed Files ---");
                int filesShown = 0;

                for (int i = 0; i < fileResults.Length && filesShown < maxFilesToShow; i++)
                {
                    var fileResult = fileResults[i];
                    if (!fileResult.Result.IsValid)
                    {
                        string icon = GetSeverityIcon(fileResult.Result.HighestSeverity);
                        string message = $"{prefix}  {filesShown + 1}. {icon} {fileResult.FilePath}\n" +
                                       $"     Errors: {fileResult.Result.ErrorCount}, Warnings: {fileResult.Result.WarningCount}";

                        Color severityColor = GetSeverityColor(fileResult.Result.HighestSeverity);
                        LogWithColor(message, severityColor, fileResult.Result.HighestSeverity);
                        filesShown++;
                    }
                }

                if (failedFiles > maxFilesToShow)
                {
                    Debug.Log($"{prefix}  ... and {failedFiles - maxFilesToShow} more failed files");
                }
            }
        }

        public static void LogErrorAccumulator(ErrorAccumulator accumulator, string context = "")
        {
            string prefix = string.IsNullOrEmpty(context) ? "" : $"[{context}] ";

            var result = accumulator.GetValidationResult();
            LogValidationResult(result, context);

            if (accumulator.HasReachedLimit)
            {
                Debug.LogWarning($"{prefix}⚠️ Error accumulator reached maximum capacity. Some errors may have been dropped.");
            }
        }

        public static string FormatErrorForConsole(ErrorResult error)
        {
            if (!error.HasError) return "No error";

            string location = "";
            if (error.Line >= 0)
            {
                location = $" at line {error.Line}";
                if (error.Column >= 0)
                {
                    location += $", column {error.Column}";
                }
            }

            return $"{GetSeverityIcon(error.Severity)} {error.Severity}: {error.Code}{location} - {ErrorUtilities.GetErrorMessage(error.Code)}";
        }

        public static string FormatValidationSummary(ValidationResult result)
        {
            if (result.IsValid && result.TotalIssues == 0)
                return "✓ Validation passed";

            string icon = GetSeverityIcon(result.HighestSeverity);
            return $"{icon} {result.ErrorCount} errors, {result.WarningCount} warnings, {result.InfoCount} info ({result.HighestSeverity} severity)";
        }

        private static Color GetSeverityColor(ErrorSeverity severity)
        {
            int index = (int)severity;
            return index >= 0 && index < SeverityColors.Length ? SeverityColors[index] : Color.white;
        }

        private static string GetSeverityIcon(ErrorSeverity severity)
        {
            return severity switch
            {
                ErrorSeverity.Info => "ℹ️",
                ErrorSeverity.Warning => "⚠️",
                ErrorSeverity.Error => "❌",
                ErrorSeverity.Critical => "🔥",
                _ => "❓"
            };
        }

        private static void LogWithColor(string message, Color color, ErrorSeverity severity)
        {
            string colorHex = ColorUtility.ToHtmlStringRGB(color);
            string formattedMessage = $"<color=#{colorHex}>{message}</color>";

            switch (severity)
            {
                case ErrorSeverity.Info:
                    Debug.Log(formattedMessage);
                    break;
                case ErrorSeverity.Warning:
                    Debug.LogWarning(formattedMessage);
                    break;
                case ErrorSeverity.Error:
                case ErrorSeverity.Critical:
                    Debug.LogError(formattedMessage);
                    break;
                default:
                    Debug.Log(formattedMessage);
                    break;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ErrorVisualizationSettings
    {
        public bool EnableColoredOutput;
        public bool ShowLineNumbers;
        public bool ShowColumnNumbers;
        public bool ShowContextHash;
        public bool GroupBySeverity;
        public int MaxErrorsPerCategory;
        public bool ShowRecoveryHints;

        public static ErrorVisualizationSettings Default => new ErrorVisualizationSettings
        {
            EnableColoredOutput = true,
            ShowLineNumbers = true,
            ShowColumnNumbers = true,
            ShowContextHash = false,
            GroupBySeverity = true,
            MaxErrorsPerCategory = 10,
            ShowRecoveryHints = true
        };

        public static ErrorVisualizationSettings Minimal => new ErrorVisualizationSettings
        {
            EnableColoredOutput = false,
            ShowLineNumbers = false,
            ShowColumnNumbers = false,
            ShowContextHash = false,
            GroupBySeverity = false,
            MaxErrorsPerCategory = 5,
            ShowRecoveryHints = false
        };

        public static ErrorVisualizationSettings Detailed => new ErrorVisualizationSettings
        {
            EnableColoredOutput = true,
            ShowLineNumbers = true,
            ShowColumnNumbers = true,
            ShowContextHash = true,
            GroupBySeverity = true,
            MaxErrorsPerCategory = 50,
            ShowRecoveryHints = true
        };
    }

    public static class AdvancedErrorVisualizer
    {
        public static void LogDetailedValidation(ValidationResultCollection collection, ErrorVisualizationSettings settings, string context = "")
        {
            string prefix = string.IsNullOrEmpty(context) ? "" : $"[{context}] ";

            var summary = collection.GetSummary();

            if (settings.EnableColoredOutput)
            {
                ErrorDebugVisualizer.LogValidationResult(summary, context);
            }
            else
            {
                Debug.Log($"{prefix}{ErrorDebugVisualizer.FormatValidationSummary(summary)}");
            }

            if (summary.TotalIssues == 0) return;

            if (settings.GroupBySeverity)
            {
                LogGroupedBySeverity(collection, settings, prefix);
            }
            else
            {
                LogChronological(collection, settings, prefix);
            }
        }

        private static void LogGroupedBySeverity(ValidationResultCollection collection, ErrorVisualizationSettings settings, string prefix)
        {
            if (collection.ErrorCount > 0)
            {
                Debug.Log($"{prefix}--- ERRORS ---");
                var errors = collection.GetErrors(Allocator.Temp);
                LogErrorGroup(errors, settings, prefix, Mathf.Min(errors.Length, settings.MaxErrorsPerCategory));
                errors.Dispose();
            }

            if (collection.WarningCount > 0)
            {
                Debug.Log($"{prefix}--- WARNINGS ---");
                var warnings = collection.GetWarnings(Allocator.Temp);
                LogErrorGroup(warnings, settings, prefix, Mathf.Min(warnings.Length, settings.MaxErrorsPerCategory));
                warnings.Dispose();
            }
        }

        private static void LogChronological(ValidationResultCollection collection, ErrorVisualizationSettings settings, string prefix)
        {
            Debug.Log($"{prefix}--- ALL ISSUES (Chronological) ---");
            var allIssues = collection.GetAllIssues(Allocator.Temp);
            LogErrorGroup(allIssues, settings, prefix, Mathf.Min(allIssues.Length, settings.MaxErrorsPerCategory));
            allIssues.Dispose();
        }

        private static void LogErrorGroup(NativeArray<ErrorResult> errors, ErrorVisualizationSettings settings, string prefix, int maxToShow)
        {
            for (int i = 0; i < maxToShow; i++)
            {
                var error = errors[i];
                string message = FormatDetailedError(error, settings, i + 1);

                if (settings.EnableColoredOutput)
                {
                    Color color = GetSeverityColor(error.Severity);
                    LogWithColor($"{prefix}  {message}", color, error.Severity);
                }
                else
                {
                    Debug.Log($"{prefix}  {message}");
                }

                if (settings.ShowRecoveryHints && ErrorUtilities.IsRecoverable(error.Code))
                {
                    Debug.Log($"{prefix}    💡 Hint: This error is recoverable - parsing can continue");
                }
            }

            if (errors.Length > maxToShow)
            {
                Debug.Log($"{prefix}  ... and {errors.Length - maxToShow} more issues of this type");
            }
        }

        private static string FormatDetailedError(ErrorResult error, ErrorVisualizationSettings settings, int index)
        {
            string result = $"{index}. {GetSeverityIcon(error.Severity)} {error.Code}";

            if (settings.ShowLineNumbers && error.Line >= 0)
            {
                result += $" (Line {error.Line}";
                if (settings.ShowColumnNumbers && error.Column >= 0)
                {
                    result += $", Col {error.Column}";
                }
                result += ")";
            }

            if (settings.ShowContextHash && error.ContextHash != 0)
            {
                result += $" [Context: {error.ContextHash:X8}]";
            }

            result += $" - {ErrorUtilities.GetErrorMessage(error.Code)}";

            return result;
        }

        private static Color GetSeverityColor(ErrorSeverity severity)
        {
            return severity switch
            {
                ErrorSeverity.Info => new Color(0.7f, 0.7f, 0.7f, 1.0f),
                ErrorSeverity.Warning => new Color(1.0f, 0.8f, 0.0f, 1.0f),
                ErrorSeverity.Error => new Color(1.0f, 0.3f, 0.3f, 1.0f),
                ErrorSeverity.Critical => new Color(0.8f, 0.0f, 0.8f, 1.0f),
                _ => Color.white
            };
        }

        private static string GetSeverityIcon(ErrorSeverity severity)
        {
            return severity switch
            {
                ErrorSeverity.Info => "ℹ️",
                ErrorSeverity.Warning => "⚠️",
                ErrorSeverity.Error => "❌",
                ErrorSeverity.Critical => "🔥",
                _ => "❓"
            };
        }

        private static void LogWithColor(string message, Color color, ErrorSeverity severity)
        {
            string colorHex = ColorUtility.ToHtmlStringRGB(color);
            string formattedMessage = $"<color=#{colorHex}>{message}</color>";

            switch (severity)
            {
                case ErrorSeverity.Info:
                    Debug.Log(formattedMessage);
                    break;
                case ErrorSeverity.Warning:
                    Debug.LogWarning(formattedMessage);
                    break;
                case ErrorSeverity.Error:
                case ErrorSeverity.Critical:
                    Debug.LogError(formattedMessage);
                    break;
                default:
                    Debug.Log(formattedMessage);
                    break;
            }
        }
    }
}