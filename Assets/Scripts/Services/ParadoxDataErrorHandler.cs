using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ProvinceSystem.Utils;

namespace ProvinceSystem.Services
{
    /// <summary>
    /// Comprehensive error handling and logging system for ParadoxDataLib integration
    /// Provides detailed error context, recovery suggestions, and integration with FileLogger
    /// </summary>
    public static class ParadoxDataErrorHandler
    {
        private static readonly Dictionary<ParadoxDataErrorType, string> _errorMessages;
        private static readonly Dictionary<ParadoxDataErrorType, string> _recoverySuggestions;
        private static readonly List<ParadoxDataError> _errorHistory;
        private static bool _isInitialized = false;

        static ParadoxDataErrorHandler()
        {
            _errorMessages = new Dictionary<ParadoxDataErrorType, string>();
            _recoverySuggestions = new Dictionary<ParadoxDataErrorType, string>();
            _errorHistory = new List<ParadoxDataError>();
            InitializeErrorMappings();
            _isInitialized = true;
        }

        private static void InitializeErrorMappings()
        {
            // File Not Found Errors
            _errorMessages[ParadoxDataErrorType.DefinitionFileNotFound] = "Province definition file (definition.csv) could not be found";
            _recoverySuggestions[ParadoxDataErrorType.DefinitionFileNotFound] = "Ensure definition.csv exists in Data/map/ directory and verify file path configuration";

            _errorMessages[ParadoxDataErrorType.DefaultMapFileNotFound] = "Default map file (default.map) could not be found";
            _recoverySuggestions[ParadoxDataErrorType.DefaultMapFileNotFound] = "Ensure default.map exists in Data/map/ directory. This file defines sea/lake provinces";

            _errorMessages[ParadoxDataErrorType.ProvinceBitmapNotFound] = "Province bitmap file (provinces.bmp) could not be found";
            _recoverySuggestions[ParadoxDataErrorType.ProvinceBitmapNotFound] = "Ensure provinces.bmp exists in Data/map/ directory and matches definition.csv colors";

            // Parsing Errors
            _errorMessages[ParadoxDataErrorType.CSVParsingFailed] = "Failed to parse CSV data - invalid format or corrupt file";
            _recoverySuggestions[ParadoxDataErrorType.CSVParsingFailed] = "Verify CSV file format: ID;R;G;B;Name;Unused. Check for special characters or encoding issues";

            _errorMessages[ParadoxDataErrorType.ParadoxNodeParsingFailed] = "Failed to parse Paradox file structure";
            _recoverySuggestions[ParadoxDataErrorType.ParadoxNodeParsingFailed] = "Check file syntax for proper braces, quotes, and nesting. Verify file is not corrupted";

            _errorMessages[ParadoxDataErrorType.BitmapParsingFailed] = "Failed to parse bitmap file - unsupported format or corruption";
            _recoverySuggestions[ParadoxDataErrorType.BitmapParsingFailed] = "Ensure bitmap is valid BMP format, check file size and color depth compatibility";

            // Data Validation Errors
            _errorMessages[ParadoxDataErrorType.ProvinceIdMismatch] = "Province ID in definition doesn't match bitmap color mapping";
            _recoverySuggestions[ParadoxDataErrorType.ProvinceIdMismatch] = "Verify definition.csv IDs correspond to correct RGB colors in provinces.bmp";

            _errorMessages[ParadoxDataErrorType.MissingRequiredData] = "Required data components are missing or incomplete";
            _recoverySuggestions[ParadoxDataErrorType.MissingRequiredData] = "Ensure all required files are present and contain valid data before proceeding";

            _errorMessages[ParadoxDataErrorType.DataCorruption] = "Data corruption detected - inconsistent or invalid values";
            _recoverySuggestions[ParadoxDataErrorType.DataCorruption] = "Re-download game files or check for file system errors. Verify data integrity";

            // Memory/Performance Errors
            _errorMessages[ParadoxDataErrorType.OutOfMemory] = "Insufficient memory to load large datasets";
            _recoverySuggestions[ParadoxDataErrorType.OutOfMemory] = "Enable data streaming, reduce map size, or increase available memory. Consider lazy loading";

            _errorMessages[ParadoxDataErrorType.LoadTimeout] = "Data loading operation timed out";
            _recoverySuggestions[ParadoxDataErrorType.LoadTimeout] = "Increase timeout values, check disk speed, or enable background loading for large files";

            // Integration Errors
            _errorMessages[ParadoxDataErrorType.UnityIntegrationFailed] = "Failed to integrate ParadoxDataLib with Unity systems";
            _recoverySuggestions[ParadoxDataErrorType.UnityIntegrationFailed] = "Check Unity version compatibility, verify component references and scene setup";

            _errorMessages[ParadoxDataErrorType.ServiceInitializationFailed] = "ParadoxDataManager service failed to initialize";
            _recoverySuggestions[ParadoxDataErrorType.ServiceInitializationFailed] = "Check component dependencies, verify singleton pattern, and review initialization order";

            // Cache Errors
            _errorMessages[ParadoxDataErrorType.CacheCorruption] = "Data cache corruption detected";
            _recoverySuggestions[ParadoxDataErrorType.CacheCorruption] = "Clear cache data, restart application, or disable caching temporarily";

            // Localization Errors
            _errorMessages[ParadoxDataErrorType.LocalizationLoadFailed] = "Failed to load localization data";
            _recoverySuggestions[ParadoxDataErrorType.LocalizationLoadFailed] = "Check localization file format, verify encoding (UTF-8), and ensure key-value syntax";
        }

        /// <summary>
        /// Handle a ParadoxDataLib integration error with comprehensive logging and recovery suggestions
        /// </summary>
        public static ParadoxDataError HandleError(ParadoxDataErrorType errorType, Exception originalException = null, string additionalContext = null)
        {
            var error = new ParadoxDataError
            {
                ErrorType = errorType,
                Message = GetErrorMessage(errorType),
                RecoverySuggestion = GetRecoverySuggestion(errorType),
                OriginalException = originalException,
                AdditionalContext = additionalContext,
                Timestamp = DateTime.Now,
                StackTrace = originalException?.StackTrace ?? Environment.StackTrace
            };

            // Add to error history
            _errorHistory.Add(error);

            // Log to Unity console
            LogErrorToUnity(error);

            // Log to file system if available
            LogErrorToFile(error);

            // Trigger error event if needed
            TriggerErrorEvent(error);

            return error;
        }

        /// <summary>
        /// Handle multiple related errors with aggregated context
        /// </summary>
        public static List<ParadoxDataError> HandleErrors(IEnumerable<(ParadoxDataErrorType type, Exception exception, string context)> errors)
        {
            var handledErrors = new List<ParadoxDataError>();

            foreach (var (type, exception, context) in errors)
            {
                handledErrors.Add(HandleError(type, exception, context));
            }

            // Log aggregate error summary
            LogAggregateErrorSummary(handledErrors);

            return handledErrors;
        }

        /// <summary>
        /// Create and handle a custom ParadoxDataException with detailed context
        /// </summary>
        public static ParadoxDataException CreateException(ParadoxDataErrorType errorType, Exception innerException = null, string additionalContext = null)
        {
            var error = HandleError(errorType, innerException, additionalContext);

            var exceptionMessage = BuildExceptionMessage(error);
            return new ParadoxDataException(exceptionMessage, innerException)
            {
                ErrorType = errorType,
                ErrorDetails = error
            };
        }

        /// <summary>
        /// Log performance warning when operations exceed expected thresholds
        /// </summary>
        public static void LogPerformanceWarning(string operation, TimeSpan duration, TimeSpan expectedDuration, string suggestions = null)
        {
            if (duration > expectedDuration)
            {
                var message = $"Performance Warning: {operation} took {duration.TotalMilliseconds:F0}ms (expected <{expectedDuration.TotalMilliseconds:F0}ms)";

                if (!string.IsNullOrEmpty(suggestions))
                {
                    message += $"\nSuggestions: {suggestions}";
                }

                Debug.LogWarning($"[ParadoxDataPerformance] {message}");

                ProvinceSystem.Utils.DominionLogger.Log($"[PERFORMANCE] {message}");
            }
        }

        /// <summary>
        /// Get error statistics and history for debugging
        /// </summary>
        public static ParadoxDataErrorStats GetErrorStatistics()
        {
            var stats = new ParadoxDataErrorStats();
            var errorCounts = new Dictionary<ParadoxDataErrorType, int>();

            foreach (var error in _errorHistory)
            {
                if (errorCounts.ContainsKey(error.ErrorType))
                    errorCounts[error.ErrorType]++;
                else
                    errorCounts[error.ErrorType] = 1;
            }

            stats.TotalErrors = _errorHistory.Count;
            stats.ErrorCounts = errorCounts;
            stats.LastError = _errorHistory.Count > 0 ? _errorHistory[_errorHistory.Count - 1] : null;
            stats.FirstErrorTime = _errorHistory.Count > 0 ? _errorHistory[0].Timestamp : DateTime.MinValue;
            stats.LastErrorTime = _errorHistory.Count > 0 ? _errorHistory[_errorHistory.Count - 1].Timestamp : DateTime.MinValue;

            return stats;
        }

        /// <summary>
        /// Clear error history (useful for testing or after resolving issues)
        /// </summary>
        public static void ClearErrorHistory()
        {
            _errorHistory.Clear();
            Debug.Log("[ParadoxDataErrorHandler] Error history cleared");
        }

        /// <summary>
        /// Export error report for debugging or support
        /// </summary>
        public static string ExportErrorReport()
        {
            var report = new StringBuilder();
            report.AppendLine("=== ParadoxDataLib Integration Error Report ===");
            report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"Unity Version: {Application.unityVersion}");
            report.AppendLine($"Platform: {Application.platform}");
            report.AppendLine($"Total Errors: {_errorHistory.Count}");
            report.AppendLine();

            foreach (var error in _errorHistory)
            {
                report.AppendLine($"[{error.Timestamp:HH:mm:ss}] {error.ErrorType}");
                report.AppendLine($"Message: {error.Message}");
                report.AppendLine($"Recovery: {error.RecoverySuggestion}");

                if (!string.IsNullOrEmpty(error.AdditionalContext))
                {
                    report.AppendLine($"Context: {error.AdditionalContext}");
                }

                if (error.OriginalException != null)
                {
                    report.AppendLine($"Exception: {error.OriginalException.GetType().Name} - {error.OriginalException.Message}");
                }

                report.AppendLine();
            }

            return report.ToString();
        }

        #region Private Helper Methods

        private static string GetErrorMessage(ParadoxDataErrorType errorType)
        {
            return _errorMessages.TryGetValue(errorType, out string message) ? message : $"Unknown error type: {errorType}";
        }

        private static string GetRecoverySuggestion(ParadoxDataErrorType errorType)
        {
            return _recoverySuggestions.TryGetValue(errorType, out string suggestion) ? suggestion : "Check logs for additional details and verify file integrity";
        }

        private static void LogErrorToUnity(ParadoxDataError error)
        {
            var logMessage = $"[ParadoxDataError] {error.ErrorType}: {error.Message}";

            if (!string.IsNullOrEmpty(error.RecoverySuggestion))
            {
                logMessage += $"\nSuggestion: {error.RecoverySuggestion}";
            }

            if (!string.IsNullOrEmpty(error.AdditionalContext))
            {
                logMessage += $"\nContext: {error.AdditionalContext}";
            }

            Debug.LogError(logMessage);
        }

        private static void LogErrorToFile(ParadoxDataError error)
        {
            var fileMessage = $"[ERROR] {error.ErrorType} - {error.Message}";

            if (!string.IsNullOrEmpty(error.AdditionalContext))
            {
                fileMessage += $" | Context: {error.AdditionalContext}";
            }

            if (error.OriginalException != null)
            {
                fileMessage += $" | Exception: {error.OriginalException.GetType().Name}";
            }

            ProvinceSystem.Utils.DominionLogger.Log(fileMessage);
        }

        private static void TriggerErrorEvent(ParadoxDataError error)
        {
            // Find ParadoxDataManager and trigger error event if available
            var manager = ParadoxDataManager.Instance;
            if (manager != null)
            {
                var exception = new ParadoxDataException(error.Message, error.OriginalException)
                {
                    ErrorType = error.ErrorType,
                    ErrorDetails = error
                };

                // This will need to be updated when ParadoxDataManager's OnLoadError event signature is confirmed
                // manager.OnLoadError?.Invoke(exception);
            }
        }

        private static void LogAggregateErrorSummary(List<ParadoxDataError> errors)
        {
            if (errors.Count > 1)
            {
                var summary = $"Multiple errors occurred ({errors.Count} total):";
                foreach (var error in errors)
                {
                    summary += $"\n- {error.ErrorType}";
                }

                Debug.LogError($"[ParadoxDataErrorHandler] {summary}");

                ProvinceSystem.Utils.DominionLogger.Log($"[ERROR_SUMMARY] {summary}");
            }
        }

        private static string BuildExceptionMessage(ParadoxDataError error)
        {
            var message = error.Message;

            if (!string.IsNullOrEmpty(error.RecoverySuggestion))
            {
                message += $" | Recovery: {error.RecoverySuggestion}";
            }

            if (!string.IsNullOrEmpty(error.AdditionalContext))
            {
                message += $" | Context: {error.AdditionalContext}";
            }

            return message;
        }

        #endregion
    }

    #region Supporting Types

    /// <summary>
    /// Enumeration of all possible ParadoxDataLib integration error types
    /// </summary>
    public enum ParadoxDataErrorType
    {
        // File Access Errors
        DefinitionFileNotFound,
        DefaultMapFileNotFound,
        ProvinceBitmapNotFound,
        HistoryDirectoryNotFound,
        LocalizationFileNotFound,

        // Parsing Errors
        CSVParsingFailed,
        ParadoxNodeParsingFailed,
        BitmapParsingFailed,
        HistoryParsingFailed,
        LocalizationParsingFailed,

        // Data Validation Errors
        ProvinceIdMismatch,
        ColorMappingInconsistent,
        MissingRequiredData,
        DataCorruption,
        InvalidProvinceDefinition,

        // Memory/Performance Errors
        OutOfMemory,
        LoadTimeout,
        PerformanceDegradation,
        CacheOverflow,

        // Integration Errors
        UnityIntegrationFailed,
        ServiceInitializationFailed,
        ComponentDependencyMissing,
        EventSystemFailure,

        // Cache Errors
        CacheCorruption,
        CacheAccessDenied,
        CacheSerializationFailed,

        // Localization Errors
        LocalizationLoadFailed,
        LocalizationKeyMissing,
        LocalizationEncodingError,

        // Generic Errors
        UnknownError,
        ConfigurationError,
        PermissionDenied
    }

    /// <summary>
    /// Detailed error information for ParadoxDataLib integration issues
    /// </summary>
    public class ParadoxDataError
    {
        public ParadoxDataErrorType ErrorType { get; set; }
        public string Message { get; set; }
        public string RecoverySuggestion { get; set; }
        public Exception OriginalException { get; set; }
        public string AdditionalContext { get; set; }
        public DateTime Timestamp { get; set; }
        public string StackTrace { get; set; }

        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss}] {ErrorType}: {Message}";
        }
    }

    /// <summary>
    /// Enhanced ParadoxDataException with detailed error context
    /// </summary>
    public class ParadoxDataException : Exception
    {
        public ParadoxDataErrorType ErrorType { get; set; }
        public ParadoxDataError ErrorDetails { get; set; }

        public ParadoxDataException(string message) : base(message) { }
        public ParadoxDataException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Statistics about ParadoxDataLib integration errors
    /// </summary>
    public class ParadoxDataErrorStats
    {
        public int TotalErrors { get; set; }
        public Dictionary<ParadoxDataErrorType, int> ErrorCounts { get; set; }
        public ParadoxDataError LastError { get; set; }
        public DateTime FirstErrorTime { get; set; }
        public DateTime LastErrorTime { get; set; }

        public ParadoxDataErrorStats()
        {
            ErrorCounts = new Dictionary<ParadoxDataErrorType, int>();
        }

        public override string ToString()
        {
            return $"ParadoxDataErrorStats: {TotalErrors} total errors, last error: {LastError?.ErrorType}";
        }
    }

    #endregion
}