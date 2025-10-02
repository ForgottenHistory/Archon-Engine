using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ParadoxParser.Core
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ValidationResult : IEquatable<ValidationResult>
    {
        public bool IsValid;
        public ErrorSeverity HighestSeverity;
        public int ErrorCount;
        public int WarningCount;
        public int InfoCount;
        public int FirstErrorLineNumber;
        public int FirstErrorColumnNumber;
        public ErrorCode FirstErrorCode;

        public static ValidationResult Valid => new ValidationResult
        {
            IsValid = true,
            HighestSeverity = ErrorSeverity.Info,
            ErrorCount = 0,
            WarningCount = 0,
            InfoCount = 0,
            FirstErrorLineNumber = -1,
            FirstErrorColumnNumber = -1,
            FirstErrorCode = ErrorCode.None
        };

        public static ValidationResult Invalid(ErrorCode errorCode, int line = -1, int column = -1)
        {
            return new ValidationResult
            {
                IsValid = false,
                HighestSeverity = ErrorSeverity.Error,
                ErrorCount = 1,
                WarningCount = 0,
                InfoCount = 0,
                FirstErrorLineNumber = line,
                FirstErrorColumnNumber = column,
                FirstErrorCode = errorCode
            };
        }

        public bool HasErrors => ErrorCount > 0;
        public bool HasWarnings => WarningCount > 0;
        public bool HasAnyIssues => ErrorCount > 0 || WarningCount > 0;
        public int TotalIssues => ErrorCount + WarningCount + InfoCount;

        public bool Equals(ValidationResult other)
        {
            return IsValid == other.IsValid &&
                   HighestSeverity == other.HighestSeverity &&
                   ErrorCount == other.ErrorCount &&
                   WarningCount == other.WarningCount &&
                   InfoCount == other.InfoCount;
        }

        public override bool Equals(object obj) => obj is ValidationResult other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(IsValid, HighestSeverity, ErrorCount, WarningCount, InfoCount);

        public override string ToString()
        {
            if (IsValid && TotalIssues == 0)
                return "Valid";

            return $"Validation: {ErrorCount} errors, {WarningCount} warnings, {InfoCount} info ({HighestSeverity} severity)";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ValidationResultCollection : IDisposable
    {
        private NativeList<ErrorResult> m_Errors;
        private NativeList<ErrorResult> m_Warnings;
        private NativeList<ErrorResult> m_InfoMessages;
        private bool m_IsCreated;

        public bool IsCreated => m_IsCreated;
        public int ErrorCount => m_Errors.IsCreated ? m_Errors.Length : 0;
        public int WarningCount => m_Warnings.IsCreated ? m_Warnings.Length : 0;
        public int InfoCount => m_InfoMessages.IsCreated ? m_InfoMessages.Length : 0;
        public int TotalCount => ErrorCount + WarningCount + InfoCount;

        public ValidationResultCollection(Allocator allocator)
        {
            m_Errors = new NativeList<ErrorResult>(allocator);
            m_Warnings = new NativeList<ErrorResult>(allocator);
            m_InfoMessages = new NativeList<ErrorResult>(allocator);
            m_IsCreated = true;
        }

        public void AddError(ErrorCode code, int line = -1, int column = -1, string context = null)
        {
            if (!m_IsCreated) return;
            m_Errors.Add(ErrorResult.Error(code, ErrorSeverity.Error, line, column, context));
        }

        public void AddWarning(ErrorCode code, int line = -1, int column = -1, string context = null)
        {
            if (!m_IsCreated) return;
            m_Warnings.Add(ErrorResult.Warning(code, line, column, context));
        }

        public void AddInfo(ErrorCode code, int line = -1, int column = -1, string context = null)
        {
            if (!m_IsCreated) return;
            m_InfoMessages.Add(ErrorResult.Info(code, line, column, context));
        }

        public void AddResult(ErrorResult result)
        {
            if (!m_IsCreated || !result.HasError) return;

            switch (result.Severity)
            {
                case ErrorSeverity.Error:
                case ErrorSeverity.Critical:
                    m_Errors.Add(result);
                    break;
                case ErrorSeverity.Warning:
                    m_Warnings.Add(result);
                    break;
                case ErrorSeverity.Info:
                    m_InfoMessages.Add(result);
                    break;
            }
        }

        public ValidationResult GetSummary()
        {
            if (!m_IsCreated)
            {
                return ValidationResult.Invalid(ErrorCode.InternalError);
            }

            var errorCount = ErrorCount;
            var warningCount = WarningCount;
            var infoCount = InfoCount;

            if (errorCount == 0 && warningCount == 0 && infoCount == 0)
            {
                return ValidationResult.Valid;
            }

            ErrorSeverity highestSeverity = ErrorSeverity.Info;
            ErrorCode firstErrorCode = ErrorCode.None;
            int firstErrorLine = -1;
            int firstErrorColumn = -1;

            if (errorCount > 0)
            {
                highestSeverity = ErrorSeverity.Error;
                var firstError = m_Errors[0];
                firstErrorCode = firstError.Code;
                firstErrorLine = firstError.Line;
                firstErrorColumn = firstError.Column;
            }
            else if (warningCount > 0)
            {
                highestSeverity = ErrorSeverity.Warning;
                var firstWarning = m_Warnings[0];
                firstErrorCode = firstWarning.Code;
                firstErrorLine = firstWarning.Line;
                firstErrorColumn = firstWarning.Column;
            }

            return new ValidationResult
            {
                IsValid = errorCount == 0,
                HighestSeverity = highestSeverity,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                InfoCount = infoCount,
                FirstErrorCode = firstErrorCode,
                FirstErrorLineNumber = firstErrorLine,
                FirstErrorColumnNumber = firstErrorColumn
            };
        }

        public NativeArray<ErrorResult> GetErrors(Allocator allocator)
        {
            if (!m_IsCreated || ErrorCount == 0)
                return new NativeArray<ErrorResult>(0, allocator);

            var result = new NativeArray<ErrorResult>(ErrorCount, allocator);
            for (int i = 0; i < ErrorCount; i++)
            {
                result[i] = m_Errors[i];
            }
            return result;
        }

        public NativeArray<ErrorResult> GetWarnings(Allocator allocator)
        {
            if (!m_IsCreated || WarningCount == 0)
                return new NativeArray<ErrorResult>(0, allocator);

            var result = new NativeArray<ErrorResult>(WarningCount, allocator);
            for (int i = 0; i < WarningCount; i++)
            {
                result[i] = m_Warnings[i];
            }
            return result;
        }

        public NativeArray<ErrorResult> GetAllIssues(Allocator allocator)
        {
            if (!m_IsCreated || TotalCount == 0)
                return new NativeArray<ErrorResult>(0, allocator);

            var result = new NativeArray<ErrorResult>(TotalCount, allocator);
            int index = 0;

            for (int i = 0; i < ErrorCount; i++)
                result[index++] = m_Errors[i];
            for (int i = 0; i < WarningCount; i++)
                result[index++] = m_Warnings[i];
            for (int i = 0; i < InfoCount; i++)
                result[index++] = m_InfoMessages[i];

            return result;
        }

        public void Clear()
        {
            if (m_IsCreated)
            {
                if (m_Errors.IsCreated) m_Errors.Clear();
                if (m_Warnings.IsCreated) m_Warnings.Clear();
                if (m_InfoMessages.IsCreated) m_InfoMessages.Clear();
            }
        }

        public void Dispose()
        {
            if (m_IsCreated)
            {
                if (m_Errors.IsCreated) m_Errors.Dispose();
                if (m_Warnings.IsCreated) m_Warnings.Dispose();
                if (m_InfoMessages.IsCreated) m_InfoMessages.Dispose();
                m_IsCreated = false;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FileValidationResult
    {
        public FixedString512Bytes FilePath;
        public ValidationResult Result;
        public long FileSize;
        public long ProcessingTime;
        public int LinesProcessed;
        public bool WasProcessed;

        public static FileValidationResult Create(string filePath, ValidationResult result)
        {
            return new FileValidationResult
            {
                FilePath = new FixedString512Bytes(filePath),
                Result = result,
                FileSize = 0,
                ProcessingTime = 0,
                LinesProcessed = 0,
                WasProcessed = true
            };
        }

        public override string ToString()
        {
            if (!WasProcessed)
                return $"{FilePath}: Not processed";

            return $"{FilePath}: {Result}";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct BatchValidationResult : IDisposable
    {
        private NativeList<FileValidationResult> m_FileResults;
        private bool m_IsCreated;

        public bool IsCreated => m_IsCreated;
        public int FileCount => m_FileResults.IsCreated ? m_FileResults.Length : 0;

        public BatchValidationResult(int initialCapacity, Allocator allocator)
        {
            m_FileResults = new NativeList<FileValidationResult>(initialCapacity, allocator);
            m_IsCreated = true;
        }

        public void AddFileResult(FileValidationResult fileResult)
        {
            if (m_IsCreated)
            {
                m_FileResults.Add(fileResult);
            }
        }

        public ValidationResult GetOverallResult()
        {
            if (!m_IsCreated || FileCount == 0)
                return ValidationResult.Valid;

            int totalErrors = 0;
            int totalWarnings = 0;
            int totalInfo = 0;
            ErrorSeverity highestSeverity = ErrorSeverity.Info;
            ErrorCode firstErrorCode = ErrorCode.None;
            int firstErrorLine = -1;
            int firstErrorColumn = -1;
            bool foundFirstError = false;

            for (int i = 0; i < FileCount; i++)
            {
                var fileResult = m_FileResults[i];
                var result = fileResult.Result;

                totalErrors += result.ErrorCount;
                totalWarnings += result.WarningCount;
                totalInfo += result.InfoCount;

                if (result.HighestSeverity > highestSeverity)
                {
                    highestSeverity = result.HighestSeverity;
                }

                if (!foundFirstError && result.HasErrors)
                {
                    firstErrorCode = result.FirstErrorCode;
                    firstErrorLine = result.FirstErrorLineNumber;
                    firstErrorColumn = result.FirstErrorColumnNumber;
                    foundFirstError = true;
                }
            }

            return new ValidationResult
            {
                IsValid = totalErrors == 0,
                HighestSeverity = highestSeverity,
                ErrorCount = totalErrors,
                WarningCount = totalWarnings,
                InfoCount = totalInfo,
                FirstErrorCode = firstErrorCode,
                FirstErrorLineNumber = firstErrorLine,
                FirstErrorColumnNumber = firstErrorColumn
            };
        }

        public NativeArray<FileValidationResult> GetFileResults(Allocator allocator)
        {
            if (!m_IsCreated || FileCount == 0)
                return new NativeArray<FileValidationResult>(0, allocator);

            var result = new NativeArray<FileValidationResult>(FileCount, allocator);
            for (int i = 0; i < FileCount; i++)
            {
                result[i] = m_FileResults[i];
            }
            return result;
        }

        public NativeArray<FileValidationResult> GetFailedFiles(Allocator allocator)
        {
            if (!m_IsCreated || FileCount == 0)
                return new NativeArray<FileValidationResult>(0, allocator);

            var failedFiles = new NativeList<FileValidationResult>(allocator);
            for (int i = 0; i < FileCount; i++)
            {
                var fileResult = m_FileResults[i];
                if (!fileResult.Result.IsValid)
                {
                    failedFiles.Add(fileResult);
                }
            }

            var result = new NativeArray<FileValidationResult>(failedFiles.Length, allocator);
            for (int i = 0; i < failedFiles.Length; i++)
            {
                result[i] = failedFiles[i];
            }

            failedFiles.Dispose();
            return result;
        }

        public void Dispose()
        {
            if (m_IsCreated)
            {
                if (m_FileResults.IsCreated)
                    m_FileResults.Dispose();
                m_IsCreated = false;
            }
        }
    }

    public static class ValidationUtilities
    {
        public static bool RequiresImmediateAction(ValidationResult result)
        {
            return result.HighestSeverity >= ErrorSeverity.Error;
        }

        public static bool CanContinueProcessing(ValidationResult result)
        {
            return result.IsValid || result.HighestSeverity <= ErrorSeverity.Warning;
        }

        public static string GetSeverityDisplayName(ErrorSeverity severity)
        {
            return severity switch
            {
                ErrorSeverity.Info => "Information",
                ErrorSeverity.Warning => "Warning",
                ErrorSeverity.Error => "Error",
                ErrorSeverity.Critical => "Critical Error",
                _ => "Unknown"
            };
        }

        public static ValidationResult CombineResults(ValidationResult a, ValidationResult b)
        {
            return new ValidationResult
            {
                IsValid = a.IsValid && b.IsValid,
                HighestSeverity = (ErrorSeverity)Math.Max((int)a.HighestSeverity, (int)b.HighestSeverity),
                ErrorCount = a.ErrorCount + b.ErrorCount,
                WarningCount = a.WarningCount + b.WarningCount,
                InfoCount = a.InfoCount + b.InfoCount,
                FirstErrorCode = a.HasErrors ? a.FirstErrorCode : b.FirstErrorCode,
                FirstErrorLineNumber = a.HasErrors ? a.FirstErrorLineNumber : b.FirstErrorLineNumber,
                FirstErrorColumnNumber = a.HasErrors ? a.FirstErrorColumnNumber : b.FirstErrorColumnNumber
            };
        }
    }
}