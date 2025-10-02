using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ParadoxParser.Core
{
    public enum RecoveryStrategy : byte
    {
        None = 0,
        Skip = 1,
        SkipToNextLine = 2,
        SkipToNextBlock = 3,
        UseDefault = 4,
        RetryWithFallback = 5,
        IgnoreAndContinue = 6,
        AbortProcessing = 7,
        RepairAndContinue = 8
    }

    public enum RecoveryResult : byte
    {
        Failed = 0,
        Recovered = 1,
        PartialRecovery = 2,
        RequiresManualIntervention = 3,
        AbortRequested = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RecoveryAction
    {
        public RecoveryStrategy Strategy;
        public RecoveryResult ExpectedResult;
        public ErrorCode TargetErrorCode;
        public ErrorSeverity MinSeverity;
        public int MaxRetries;
        public bool StopOnSuccess;
        public bool LogRecoveryAttempts;

        public static RecoveryAction Skip => new RecoveryAction
        {
            Strategy = RecoveryStrategy.Skip,
            ExpectedResult = RecoveryResult.Recovered,
            TargetErrorCode = ErrorCode.None,
            MinSeverity = ErrorSeverity.Info,
            MaxRetries = 1,
            StopOnSuccess = true,
            LogRecoveryAttempts = false
        };

        public static RecoveryAction SkipToNextLine => new RecoveryAction
        {
            Strategy = RecoveryStrategy.SkipToNextLine,
            ExpectedResult = RecoveryResult.PartialRecovery,
            TargetErrorCode = ErrorCode.None,
            MinSeverity = ErrorSeverity.Warning,
            MaxRetries = 1,
            StopOnSuccess = true,
            LogRecoveryAttempts = true
        };

        public static RecoveryAction UseDefault => new RecoveryAction
        {
            Strategy = RecoveryStrategy.UseDefault,
            ExpectedResult = RecoveryResult.PartialRecovery,
            TargetErrorCode = ErrorCode.None,
            MinSeverity = ErrorSeverity.Warning,
            MaxRetries = 1,
            StopOnSuccess = true,
            LogRecoveryAttempts = true
        };

        public static RecoveryAction Abort => new RecoveryAction
        {
            Strategy = RecoveryStrategy.AbortProcessing,
            ExpectedResult = RecoveryResult.AbortRequested,
            TargetErrorCode = ErrorCode.None,
            MinSeverity = ErrorSeverity.Critical,
            MaxRetries = 0,
            StopOnSuccess = false,
            LogRecoveryAttempts = true
        };

        public static RecoveryAction RepairAndContinue => new RecoveryAction
        {
            Strategy = RecoveryStrategy.RepairAndContinue,
            ExpectedResult = RecoveryResult.Recovered,
            TargetErrorCode = ErrorCode.None,
            MinSeverity = ErrorSeverity.Warning,
            MaxRetries = 2,
            StopOnSuccess = true,
            LogRecoveryAttempts = true
        };

        public static RecoveryAction AbortProcessing => new RecoveryAction
        {
            Strategy = RecoveryStrategy.AbortProcessing,
            ExpectedResult = RecoveryResult.AbortRequested,
            TargetErrorCode = ErrorCode.None,
            MinSeverity = ErrorSeverity.Critical,
            MaxRetries = 0,
            StopOnSuccess = false,
            LogRecoveryAttempts = true
        };

        public static RecoveryAction IgnoreAndContinue => new RecoveryAction
        {
            Strategy = RecoveryStrategy.IgnoreAndContinue,
            ExpectedResult = RecoveryResult.PartialRecovery,
            TargetErrorCode = ErrorCode.None,
            MinSeverity = ErrorSeverity.Info,
            MaxRetries = 1,
            StopOnSuccess = true,
            LogRecoveryAttempts = false
        };

        public static RecoveryAction Create(RecoveryStrategy strategy, ErrorCode targetError = ErrorCode.None, ErrorSeverity minSeverity = ErrorSeverity.Warning)
        {
            return new RecoveryAction
            {
                Strategy = strategy,
                ExpectedResult = GetDefaultExpectedResult(strategy),
                TargetErrorCode = targetError,
                MinSeverity = minSeverity,
                MaxRetries = GetDefaultMaxRetries(strategy),
                StopOnSuccess = true,
                LogRecoveryAttempts = true
            };
        }

        private static RecoveryResult GetDefaultExpectedResult(RecoveryStrategy strategy)
        {
            return strategy switch
            {
                RecoveryStrategy.Skip => RecoveryResult.Recovered,
                RecoveryStrategy.SkipToNextLine => RecoveryResult.PartialRecovery,
                RecoveryStrategy.SkipToNextBlock => RecoveryResult.PartialRecovery,
                RecoveryStrategy.UseDefault => RecoveryResult.PartialRecovery,
                RecoveryStrategy.RetryWithFallback => RecoveryResult.Recovered,
                RecoveryStrategy.IgnoreAndContinue => RecoveryResult.PartialRecovery,
                RecoveryStrategy.AbortProcessing => RecoveryResult.AbortRequested,
                RecoveryStrategy.RepairAndContinue => RecoveryResult.Recovered,
                _ => RecoveryResult.Failed
            };
        }

        private static int GetDefaultMaxRetries(RecoveryStrategy strategy)
        {
            return strategy switch
            {
                RecoveryStrategy.RetryWithFallback => 3,
                RecoveryStrategy.RepairAndContinue => 2,
                RecoveryStrategy.AbortProcessing => 0,
                _ => 1
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ErrorRecoveryContext : IDisposable
    {
        private NativeHashMap<int, RecoveryAction> m_RecoveryStrategies;
        private NativeList<RecoveryAttempt> m_RecoveryHistory;
        private NativeReference<int> m_TotalAttempts;
        private NativeReference<int> m_SuccessfulRecoveries;
        private bool m_IsCreated;
        private bool m_IsDisposed;

        public bool IsCreated => m_IsCreated;
        public int TotalAttempts => m_TotalAttempts.IsCreated ? m_TotalAttempts.Value : 0;
        public int SuccessfulRecoveries => m_SuccessfulRecoveries.IsCreated ? m_SuccessfulRecoveries.Value : 0;
        public float SuccessRate => TotalAttempts > 0 ? (float)SuccessfulRecoveries / TotalAttempts : 0f;

        public ErrorRecoveryContext(Allocator allocator)
        {
            m_RecoveryStrategies = new NativeHashMap<int, RecoveryAction>(50, allocator);
            m_RecoveryHistory = new NativeList<RecoveryAttempt>(allocator);
            m_TotalAttempts = new NativeReference<int>(allocator);
            m_SuccessfulRecoveries = new NativeReference<int>(allocator);
            m_IsCreated = true;
            m_IsDisposed = false;

            m_TotalAttempts.Value = 0;
            m_SuccessfulRecoveries.Value = 0;

            SetupDefaultRecoveryStrategies();
        }

        public void RegisterRecoveryStrategy(ErrorCode errorCode, RecoveryAction action)
        {
            if (m_IsCreated)
            {
                m_RecoveryStrategies[(int)errorCode] = action;
            }
        }

        public bool TryGetRecoveryStrategy(ErrorCode errorCode, out RecoveryAction action)
        {
            action = default;
            if (!m_IsCreated) return false;

            if (m_RecoveryStrategies.TryGetValue((int)errorCode, out action))
                return true;

            action = GetDefaultStrategyForError(errorCode);
            return action.Strategy != RecoveryStrategy.None;
        }

        public RecoveryResult AttemptRecovery(ErrorResult error, out string recoveryMessage)
        {
            recoveryMessage = "";

            if (!m_IsCreated)
            {
                recoveryMessage = "Recovery context not initialized";
                return RecoveryResult.Failed;
            }

            m_TotalAttempts.Value++;

            if (!TryGetRecoveryStrategy(error.Code, out RecoveryAction action))
            {
                recoveryMessage = $"No recovery strategy available for {error.Code}";
                return RecoveryResult.Failed;
            }

            if (error.Severity < action.MinSeverity)
            {
                recoveryMessage = $"Error severity {error.Severity} below minimum {action.MinSeverity} for recovery";
                return RecoveryResult.Failed;
            }

            var attempt = new RecoveryAttempt
            {
                ErrorCode = error.Code,
                Strategy = action.Strategy,
                AttemptTime = DateTime.Now.Ticks,
                LineNumber = error.Line,
                ColumnNumber = error.Column
            };

            RecoveryResult result = ExecuteRecoveryStrategy(action, error, out recoveryMessage);
            attempt.Result = result;
            attempt.WasSuccessful = result == RecoveryResult.Recovered || result == RecoveryResult.PartialRecovery;

            if (attempt.WasSuccessful)
            {
                m_SuccessfulRecoveries.Value++;
            }

            m_RecoveryHistory.Add(attempt);

            if (action.LogRecoveryAttempts)
            {
                LogRecoveryAttempt(attempt, recoveryMessage);
            }

            return result;
        }

        private RecoveryResult ExecuteRecoveryStrategy(RecoveryAction action, ErrorResult error, out string message)
        {
            message = "";

            switch (action.Strategy)
            {
                case RecoveryStrategy.Skip:
                    message = "Skipped problematic token/element";
                    return RecoveryResult.Recovered;

                case RecoveryStrategy.SkipToNextLine:
                    message = "Skipped to next line";
                    return RecoveryResult.PartialRecovery;

                case RecoveryStrategy.SkipToNextBlock:
                    message = "Skipped to next block/section";
                    return RecoveryResult.PartialRecovery;

                case RecoveryStrategy.UseDefault:
                    message = GetDefaultValueMessage(error.Code);
                    return RecoveryResult.PartialRecovery;

                case RecoveryStrategy.RetryWithFallback:
                    message = "Retrying with fallback parsing method";
                    return RecoveryResult.Recovered;

                case RecoveryStrategy.IgnoreAndContinue:
                    message = "Ignored error and continued processing";
                    return RecoveryResult.PartialRecovery;

                case RecoveryStrategy.AbortProcessing:
                    message = "Aborting processing due to critical error";
                    return RecoveryResult.AbortRequested;

                case RecoveryStrategy.RepairAndContinue:
                    message = GetRepairMessage(error.Code);
                    return AttemptRepair(error.Code) ? RecoveryResult.Recovered : RecoveryResult.Failed;

                default:
                    message = "Unknown recovery strategy";
                    return RecoveryResult.Failed;
            }
        }

        private void SetupDefaultRecoveryStrategies()
        {
            RegisterRecoveryStrategy(ErrorCode.ParseSyntaxError, RecoveryAction.SkipToNextLine);
            RegisterRecoveryStrategy(ErrorCode.ParseUnexpectedToken, RecoveryAction.Skip);
            RegisterRecoveryStrategy(ErrorCode.ParseUnterminatedString, RecoveryAction.RepairAndContinue);
            RegisterRecoveryStrategy(ErrorCode.ParseInvalidNumber, RecoveryAction.UseDefault);
            RegisterRecoveryStrategy(ErrorCode.ParseInvalidDate, RecoveryAction.UseDefault);
            RegisterRecoveryStrategy(ErrorCode.ParseMissingBrace, RecoveryAction.RepairAndContinue);
            RegisterRecoveryStrategy(ErrorCode.ParseExtraBrace, RecoveryAction.Skip);
            RegisterRecoveryStrategy(ErrorCode.ParseInvalidOperator, RecoveryAction.SkipToNextLine);
            RegisterRecoveryStrategy(ErrorCode.ParseInvalidIdentifier, RecoveryAction.Skip);
            RegisterRecoveryStrategy(ErrorCode.ParseMissingAssignment, RecoveryAction.SkipToNextLine);
            RegisterRecoveryStrategy(ErrorCode.ParseCircularReference, RecoveryAction.AbortProcessing);
            RegisterRecoveryStrategy(ErrorCode.ParseDepthExceeded, RecoveryAction.AbortProcessing);

            RegisterRecoveryStrategy(ErrorCode.ValidationFieldRequired, RecoveryAction.UseDefault);
            RegisterRecoveryStrategy(ErrorCode.ValidationFieldInvalid, RecoveryAction.UseDefault);
            RegisterRecoveryStrategy(ErrorCode.ValidationRangeError, RecoveryAction.UseDefault);
            RegisterRecoveryStrategy(ErrorCode.ValidationTypeError, RecoveryAction.UseDefault);
            RegisterRecoveryStrategy(ErrorCode.ValidationDuplicateKey, RecoveryAction.IgnoreAndContinue);

            RegisterRecoveryStrategy(ErrorCode.FileNotFound, RecoveryAction.IgnoreAndContinue);
            RegisterRecoveryStrategy(ErrorCode.FileAccessDenied, RecoveryAction.AbortProcessing);
            RegisterRecoveryStrategy(ErrorCode.FileCorrupted, RecoveryAction.AbortProcessing);

            RegisterRecoveryStrategy(ErrorCode.MemoryAllocationFailed, RecoveryAction.AbortProcessing);
            RegisterRecoveryStrategy(ErrorCode.MemoryOutOfBounds, RecoveryAction.AbortProcessing);
            RegisterRecoveryStrategy(ErrorCode.MemoryCorruption, RecoveryAction.AbortProcessing);

            RegisterRecoveryStrategy(ErrorCode.InternalError, RecoveryAction.AbortProcessing);
        }

        private RecoveryAction GetDefaultStrategyForError(ErrorCode errorCode)
        {
            if (IsParseError(errorCode))
                return RecoveryAction.SkipToNextLine;
            if (IsValidationError(errorCode))
                return RecoveryAction.UseDefault;
            if (IsFileError(errorCode))
                return RecoveryAction.IgnoreAndContinue;
            if (IsMemoryError(errorCode))
                return RecoveryAction.Abort;

            return new RecoveryAction { Strategy = RecoveryStrategy.None };
        }

        private static bool IsParseError(ErrorCode code) => (int)code >= 100 && (int)code < 200;
        private static bool IsValidationError(ErrorCode code) => (int)code >= 200 && (int)code < 300;
        private static bool IsFileError(ErrorCode code) => (int)code >= 1 && (int)code < 100;
        private static bool IsMemoryError(ErrorCode code) => (int)code >= 300 && (int)code < 400;

        private string GetDefaultValueMessage(ErrorCode errorCode)
        {
            return errorCode switch
            {
                ErrorCode.ParseInvalidNumber => "Used default value: 0",
                ErrorCode.ParseInvalidDate => "Used default date: 1.1.1",
                ErrorCode.ValidationFieldRequired => "Used empty/default value for required field",
                ErrorCode.ValidationFieldInvalid => "Corrected invalid field to default value",
                ErrorCode.ValidationRangeError => "Clamped value to valid range",
                ErrorCode.ValidationTypeError => "Converted to expected type",
                _ => "Applied default value"
            };
        }

        private string GetRepairMessage(ErrorCode errorCode)
        {
            return errorCode switch
            {
                ErrorCode.ParseUnterminatedString => "Added missing closing quote",
                ErrorCode.ParseMissingBrace => "Added missing closing brace",
                _ => "Applied automatic repair"
            };
        }

        private bool AttemptRepair(ErrorCode errorCode)
        {
            return errorCode switch
            {
                ErrorCode.ParseUnterminatedString => true,
                ErrorCode.ParseMissingBrace => true,
                ErrorCode.ParseExtraBrace => true,
                _ => false
            };
        }

        private void LogRecoveryAttempt(RecoveryAttempt attempt, string message)
        {
            string location = attempt.LineNumber >= 0 ? $" at line {attempt.LineNumber}" : "";
            string icon = attempt.WasSuccessful ? "✅" : "❌";

            UnityEngine.Debug.Log($"{icon} Recovery {attempt.Result}: {attempt.Strategy} for {attempt.ErrorCode}{location} - {message}");
        }

        public void Dispose()
        {
            if (m_IsDisposed) return;
            m_IsDisposed = true;
            m_IsCreated = false;

            try
            {
                if (m_RecoveryStrategies.IsCreated)
                    m_RecoveryStrategies.Dispose();
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }

            try
            {
                if (m_RecoveryHistory.IsCreated)
                    m_RecoveryHistory.Dispose();
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }

            try
            {
                if (m_TotalAttempts.IsCreated)
                    m_TotalAttempts.Dispose();
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }

            try
            {
                if (m_SuccessfulRecoveries.IsCreated)
                    m_SuccessfulRecoveries.Dispose();
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RecoveryAttempt
    {
        public ErrorCode ErrorCode;
        public RecoveryStrategy Strategy;
        public RecoveryResult Result;
        public long AttemptTime;
        public int LineNumber;
        public int ColumnNumber;
        public bool WasSuccessful;

        public override string ToString()
        {
            string location = LineNumber >= 0 ? $" at line {LineNumber}" : "";
            string success = WasSuccessful ? "✅" : "❌";
            return $"{success} {Strategy} for {ErrorCode}{location} -> {Result}";
        }
    }

    public static class ErrorRecoveryUtilities
    {
        public static bool CanAttemptRecovery(ErrorResult error)
        {
            return ErrorUtilities.IsRecoverable(error.Code) &&
                   error.Severity <= ErrorSeverity.Error;
        }

        public static RecoveryStrategy GetRecommendedStrategy(ErrorCode errorCode, ErrorSeverity severity)
        {
            if (severity >= ErrorSeverity.Critical)
                return RecoveryStrategy.AbortProcessing;

            return errorCode switch
            {
                ErrorCode.ParseSyntaxError => RecoveryStrategy.SkipToNextLine,
                ErrorCode.ParseUnexpectedToken => RecoveryStrategy.Skip,
                ErrorCode.ParseUnterminatedString => RecoveryStrategy.RepairAndContinue,
                ErrorCode.ParseInvalidNumber => RecoveryStrategy.UseDefault,
                ErrorCode.ParseInvalidDate => RecoveryStrategy.UseDefault,
                ErrorCode.ParseMissingBrace => RecoveryStrategy.RepairAndContinue,
                ErrorCode.ParseExtraBrace => RecoveryStrategy.Skip,

                ErrorCode.ValidationFieldRequired => RecoveryStrategy.UseDefault,
                ErrorCode.ValidationFieldInvalid => RecoveryStrategy.UseDefault,
                ErrorCode.ValidationRangeError => RecoveryStrategy.UseDefault,
                ErrorCode.ValidationDuplicateKey => RecoveryStrategy.IgnoreAndContinue,

                ErrorCode.FileNotFound => RecoveryStrategy.IgnoreAndContinue,
                ErrorCode.FileAccessDenied => RecoveryStrategy.AbortProcessing,

                ErrorCode.MemoryAllocationFailed => RecoveryStrategy.AbortProcessing,
                ErrorCode.InternalError => RecoveryStrategy.AbortProcessing,

                _ => RecoveryStrategy.IgnoreAndContinue
            };
        }

        public static string GetRecoveryDescription(RecoveryStrategy strategy)
        {
            return strategy switch
            {
                RecoveryStrategy.None => "No recovery action",
                RecoveryStrategy.Skip => "Skip the problematic element",
                RecoveryStrategy.SkipToNextLine => "Skip to the next line",
                RecoveryStrategy.SkipToNextBlock => "Skip to the next block or section",
                RecoveryStrategy.UseDefault => "Use a default value",
                RecoveryStrategy.RetryWithFallback => "Retry with fallback method",
                RecoveryStrategy.IgnoreAndContinue => "Ignore the error and continue",
                RecoveryStrategy.AbortProcessing => "Stop processing immediately",
                RecoveryStrategy.RepairAndContinue => "Attempt automatic repair",
                _ => "Unknown recovery strategy"
            };
        }

        public static bool IsDestructiveRecovery(RecoveryStrategy strategy)
        {
            return strategy == RecoveryStrategy.Skip ||
                   strategy == RecoveryStrategy.SkipToNextLine ||
                   strategy == RecoveryStrategy.SkipToNextBlock ||
                   strategy == RecoveryStrategy.IgnoreAndContinue;
        }

        public static bool RequiresUserIntervention(RecoveryStrategy strategy)
        {
            return strategy == RecoveryStrategy.AbortProcessing;
        }
    }
}