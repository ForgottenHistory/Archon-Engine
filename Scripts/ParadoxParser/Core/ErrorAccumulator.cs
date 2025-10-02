using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace ParadoxParser.Core
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ErrorAccumulator : IDisposable
    {
        internal NativeQueue<ErrorResult> m_ErrorQueue;
        internal NativeReference<int> m_ErrorCount;
        internal NativeReference<int> m_WarningCount;
        internal NativeReference<int> m_InfoCount;
        internal NativeReference<ErrorSeverity> m_HighestSeverity;
        private bool m_IsCreated;
        internal int m_MaxErrors;

        public bool IsCreated => m_IsCreated;
        public int ErrorCount => m_ErrorCount.IsCreated ? m_ErrorCount.Value : 0;
        public int WarningCount => m_WarningCount.IsCreated ? m_WarningCount.Value : 0;
        public int InfoCount => m_InfoCount.IsCreated ? m_InfoCount.Value : 0;
        public int TotalCount => ErrorCount + WarningCount + InfoCount;
        public ErrorSeverity HighestSeverity => m_HighestSeverity.IsCreated ? m_HighestSeverity.Value : ErrorSeverity.Info;
        public bool HasReachedLimit => TotalCount >= m_MaxErrors;

        public ErrorAccumulator(Allocator allocator, int maxErrors = 1000)
        {
            m_ErrorQueue = new NativeQueue<ErrorResult>(allocator);
            m_ErrorCount = new NativeReference<int>(allocator);
            m_WarningCount = new NativeReference<int>(allocator);
            m_InfoCount = new NativeReference<int>(allocator);
            m_HighestSeverity = new NativeReference<ErrorSeverity>(allocator);
            m_IsCreated = true;
            m_IsDisposed = false;
            m_MaxErrors = maxErrors;

            m_ErrorCount.Value = 0;
            m_WarningCount.Value = 0;
            m_InfoCount.Value = 0;
            m_HighestSeverity.Value = ErrorSeverity.Info;
        }

        public bool TryAddError(ErrorResult error)
        {
            if (!m_IsCreated || HasReachedLimit) return false;

            m_ErrorQueue.Enqueue(error);

            switch (error.Severity)
            {
                case ErrorSeverity.Error:
                case ErrorSeverity.Critical:
                    m_ErrorCount.Value++;
                    break;
                case ErrorSeverity.Warning:
                    m_WarningCount.Value++;
                    break;
                case ErrorSeverity.Info:
                    m_InfoCount.Value++;
                    break;
            }

            if (error.Severity > m_HighestSeverity.Value)
            {
                m_HighestSeverity.Value = error.Severity;
            }

            return true;
        }

        public bool TryAddError(ErrorCode code, ErrorSeverity severity = ErrorSeverity.Error, int line = -1, int column = -1, string context = null)
        {
            var error = ErrorResult.Error(code, severity, line, column, context);
            return TryAddError(error);
        }

        public bool TryAddWarning(ErrorCode code, int line = -1, int column = -1, string context = null)
        {
            return TryAddError(code, ErrorSeverity.Warning, line, column, context);
        }

        public bool TryAddInfo(ErrorCode code, int line = -1, int column = -1, string context = null)
        {
            return TryAddError(code, ErrorSeverity.Info, line, column, context);
        }

        public ValidationResult GetValidationResult()
        {
            if (!m_IsCreated || m_IsDisposed)
                return ValidationResult.Invalid(ErrorCode.InternalError);

            var errorCount = ErrorCount;
            var warningCount = WarningCount;
            var infoCount = InfoCount;

            if (errorCount == 0 && warningCount == 0 && infoCount == 0)
                return ValidationResult.Valid;

            ErrorCode firstErrorCode = ErrorCode.None;
            int firstErrorLine = -1;
            int firstErrorColumn = -1;

            try
            {
                if (!m_ErrorQueue.IsCreated || m_ErrorQueue.Count == 0)
                {
                    return new ValidationResult
                    {
                        IsValid = errorCount == 0,
                        HighestSeverity = HighestSeverity,
                        ErrorCount = errorCount,
                        WarningCount = warningCount,
                        InfoCount = infoCount,
                        FirstErrorCode = firstErrorCode,
                        FirstErrorLineNumber = firstErrorLine,
                        FirstErrorColumnNumber = firstErrorColumn
                    };
                }

                var queueCopy = new NativeArray<ErrorResult>(TotalCount, Allocator.Temp);
                int index = 0;
                while (m_ErrorQueue.Count > 0 && index < queueCopy.Length)
                {
                    var error = m_ErrorQueue.Dequeue();
                    queueCopy[index++] = error;

                    if (firstErrorCode == ErrorCode.None && error.IsError)
                    {
                        firstErrorCode = error.Code;
                        firstErrorLine = error.Line;
                        firstErrorColumn = error.Column;
                    }

                    m_ErrorQueue.Enqueue(error);
                }

                queueCopy.Dispose();
            }
            catch (System.ObjectDisposedException)
            {
                // Queue was disposed during operation, return basic result
            }
            catch (System.InvalidOperationException)
            {
                // Queue is corrupted, return basic result
            }

            return new ValidationResult
            {
                IsValid = errorCount == 0,
                HighestSeverity = HighestSeverity,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                InfoCount = infoCount,
                FirstErrorCode = firstErrorCode,
                FirstErrorLineNumber = firstErrorLine,
                FirstErrorColumnNumber = firstErrorColumn
            };
        }

        public NativeArray<ErrorResult> ToArray(Allocator allocator)
        {
            if (!m_IsCreated || m_IsDisposed || TotalCount == 0)
                return new NativeArray<ErrorResult>(0, allocator);

            try
            {
                if (!m_ErrorQueue.IsCreated || m_ErrorQueue.Count == 0)
                    return new NativeArray<ErrorResult>(0, allocator);

                var result = new NativeArray<ErrorResult>(TotalCount, allocator);
                var tempQueue = new NativeQueue<ErrorResult>(Allocator.Temp);

                int index = 0;
                while (m_ErrorQueue.Count > 0 && index < result.Length)
                {
                    var error = m_ErrorQueue.Dequeue();
                    result[index++] = error;
                    tempQueue.Enqueue(error);
                }

                while (tempQueue.Count > 0)
                {
                    m_ErrorQueue.Enqueue(tempQueue.Dequeue());
                }

                tempQueue.Dispose();
                return result;
            }
            catch (System.ObjectDisposedException)
            {
                // Queue was disposed during operation
                return new NativeArray<ErrorResult>(0, allocator);
            }
            catch (System.InvalidOperationException)
            {
                // Queue is corrupted
                return new NativeArray<ErrorResult>(0, allocator);
            }
        }

        public void Clear()
        {
            if (m_IsCreated)
            {
                while (m_ErrorQueue.Count > 0)
                {
                    m_ErrorQueue.Dequeue();
                }

                m_ErrorCount.Value = 0;
                m_WarningCount.Value = 0;
                m_InfoCount.Value = 0;
                m_HighestSeverity.Value = ErrorSeverity.Info;
            }
        }

        private bool m_IsDisposed;

        public void Dispose()
        {
            if (m_IsDisposed) return;
            m_IsDisposed = true;
            m_IsCreated = false;

            try
            {
                if (m_ErrorQueue.IsCreated)
                {
                    m_ErrorQueue.Dispose();
                }
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }
            catch (System.InvalidOperationException) { /* Queue corrupted */ }

            try
            {
                if (m_ErrorCount.IsCreated)
                {
                    m_ErrorCount.Dispose();
                }
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }

            try
            {
                if (m_WarningCount.IsCreated)
                {
                    m_WarningCount.Dispose();
                }
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }

            try
            {
                if (m_InfoCount.IsCreated)
                {
                    m_InfoCount.Dispose();
                }
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }

            try
            {
                if (m_HighestSeverity.IsCreated)
                {
                    m_HighestSeverity.Dispose();
                }
            }
            catch (System.ObjectDisposedException) { /* Already disposed */ }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ParallelErrorAccumulator : IDisposable
    {
        private NativeQueue<ErrorResult>.ParallelWriter m_ErrorWriter;
        private NativeReference<int> m_ErrorCount;
        private NativeReference<int> m_WarningCount;
        private NativeReference<int> m_InfoCount;
        private NativeReference<ErrorSeverity> m_HighestSeverity;
        private bool m_IsCreated;
        private int m_MaxErrors;

        public bool IsCreated => m_IsCreated;

        public static ParallelErrorAccumulator Create(ref ErrorAccumulator mainAccumulator)
        {
            return new ParallelErrorAccumulator
            {
                m_ErrorWriter = mainAccumulator.m_ErrorQueue.AsParallelWriter(),
                m_ErrorCount = mainAccumulator.m_ErrorCount,
                m_WarningCount = mainAccumulator.m_WarningCount,
                m_InfoCount = mainAccumulator.m_InfoCount,
                m_HighestSeverity = mainAccumulator.m_HighestSeverity,
                m_IsCreated = true,
                m_MaxErrors = mainAccumulator.m_MaxErrors
            };
        }

        public bool TryAddError(ErrorResult error)
        {
            if (!m_IsCreated) return false;

            int currentTotal = m_ErrorCount.Value + m_WarningCount.Value + m_InfoCount.Value;
            if (currentTotal >= m_MaxErrors) return false;

            m_ErrorWriter.Enqueue(error);

            switch (error.Severity)
            {
                case ErrorSeverity.Error:
                case ErrorSeverity.Critical:
                    System.Threading.Interlocked.Increment(ref UnsafeUtility.AsRef<int>(m_ErrorCount.GetUnsafePtr()));
                    break;
                case ErrorSeverity.Warning:
                    System.Threading.Interlocked.Increment(ref UnsafeUtility.AsRef<int>(m_WarningCount.GetUnsafePtr()));
                    break;
                case ErrorSeverity.Info:
                    System.Threading.Interlocked.Increment(ref UnsafeUtility.AsRef<int>(m_InfoCount.GetUnsafePtr()));
                    break;
            }

            if (error.Severity > m_HighestSeverity.Value)
            {
                var currentSeverity = m_HighestSeverity.Value;
                while (error.Severity > currentSeverity)
                {
                    var originalSeverity = System.Threading.Interlocked.CompareExchange(
                        ref UnsafeUtility.AsRef<int>(m_HighestSeverity.GetUnsafePtr()), (int)error.Severity, (int)currentSeverity);
                    if (originalSeverity == (int)currentSeverity) break;
                    currentSeverity = (ErrorSeverity)originalSeverity;
                }
            }

            return true;
        }

        public bool TryAddError(ErrorCode code, ErrorSeverity severity = ErrorSeverity.Error, int line = -1, int column = -1, string context = null)
        {
            var error = ErrorResult.Error(code, severity, line, column, context);
            return TryAddError(error);
        }

        public void Dispose()
        {
            m_IsCreated = false;
        }
    }

    public struct ErrorAccumulationJob : IJob
    {
        public ErrorAccumulator Accumulator;
        public NativeArray<ErrorResult> ErrorsToAdd;

        public void Execute()
        {
            for (int i = 0; i < ErrorsToAdd.Length; i++)
            {
                Accumulator.TryAddError(ErrorsToAdd[i]);
            }
        }
    }

    public struct ParallelErrorAccumulationJob : IJobParallelFor
    {
        public ParallelErrorAccumulator Accumulator;
        [ReadOnly] public NativeArray<ErrorResult> ErrorsToAdd;

        public void Execute(int index)
        {
            Accumulator.TryAddError(ErrorsToAdd[index]);
        }
    }

    public static class ErrorAccumulatorExtensions
    {
        public static ValidationResultCollection ToValidationCollection(this ErrorAccumulator accumulator, Allocator allocator)
        {
            var collection = new ValidationResultCollection(allocator);
            var errors = accumulator.ToArray(Allocator.Temp);

            for (int i = 0; i < errors.Length; i++)
            {
                collection.AddResult(errors[i]);
            }

            errors.Dispose();
            return collection;
        }

        public static void AddToCollection(this ErrorAccumulator accumulator, ref ValidationResultCollection collection)
        {
            var errors = accumulator.ToArray(Allocator.Temp);

            for (int i = 0; i < errors.Length; i++)
            {
                collection.AddResult(errors[i]);
            }

            errors.Dispose();
        }

        public static JobHandle ScheduleErrorAccumulation(this ErrorAccumulator accumulator, NativeArray<ErrorResult> errors, JobHandle dependsOn = default)
        {
            var job = new ErrorAccumulationJob
            {
                Accumulator = accumulator,
                ErrorsToAdd = errors
            };

            return job.Schedule(dependsOn);
        }

        public static JobHandle ScheduleParallelErrorAccumulation(this ParallelErrorAccumulator accumulator, NativeArray<ErrorResult> errors, int batchSize = 32, JobHandle dependsOn = default)
        {
            var job = new ParallelErrorAccumulationJob
            {
                Accumulator = accumulator,
                ErrorsToAdd = errors
            };

            return job.Schedule(errors.Length, batchSize, dependsOn);
        }
    }
}