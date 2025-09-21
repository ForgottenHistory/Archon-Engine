using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using ParadoxParser.Core;

namespace ParadoxParser.Tests
{
    public class ErrorHandlingTests
    {
        [SetUp]
        public void Setup()
        {
            // Setup runs before each test
        }

        [TearDown]
        public void TearDown()
        {
            // Cleanup runs after each test
        }

        #region ErrorResult Tests

        [Test]
        public void ErrorResult_Success_ShouldBeValid()
        {
            var result = ErrorResult.Success;

            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(result.HasError);
            Assert.AreEqual(ErrorCode.None, result.Code);
        }

        [Test]
        public void ErrorResult_Error_ShouldHaveCorrectProperties()
        {
            var result = ErrorResult.Error(ErrorCode.ParseSyntaxError, ErrorSeverity.Error, 42, 10, "test context");

            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.HasError);
            Assert.IsTrue(result.IsError);
            Assert.IsFalse(result.IsWarning);
            Assert.AreEqual(ErrorCode.ParseSyntaxError, result.Code);
            Assert.AreEqual(ErrorSeverity.Error, result.Severity);
            Assert.AreEqual(42, result.Line);
            Assert.AreEqual(10, result.Column);
            Assert.AreEqual("test context".GetHashCode(), result.ContextHash);
        }

        [Test]
        public void ErrorResult_Warning_ShouldHaveCorrectSeverity()
        {
            var result = ErrorResult.Warning(ErrorCode.ValidationFieldInvalid, 5, 15);

            Assert.IsTrue(result.HasError);
            Assert.IsFalse(result.IsError);
            Assert.IsTrue(result.IsWarning);
            Assert.AreEqual(ErrorSeverity.Warning, result.Severity);
        }

        [Test]
        public void ErrorResult_ToString_ShouldFormatCorrectly()
        {
            var successResult = ErrorResult.Success;
            Assert.AreEqual("Success", successResult.ToString());

            var errorResult = ErrorResult.Error(ErrorCode.ParseSyntaxError, ErrorSeverity.Error, 42, 10);
            var errorString = errorResult.ToString();
            Assert.IsTrue(errorString.Contains("Error"));
            Assert.IsTrue(errorString.Contains("ParseSyntaxError"));
            Assert.IsTrue(errorString.Contains("line 42"));
            Assert.IsTrue(errorString.Contains("column 10"));
        }

        #endregion

        #region Result<T> Tests

        [Test]
        public void Result_Success_ShouldReturnValue()
        {
            var result = Result<int>.Success(42);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(result.HasError);
            Assert.AreEqual(42, result.Value);

            Assert.IsTrue(result.TryGetValue(out int value));
            Assert.AreEqual(42, value);
        }

        [Test]
        public void Result_Failure_ShouldReturnError()
        {
            var error = ErrorResult.Error(ErrorCode.FileNotFound, ErrorSeverity.Error, 1, 1);
            var result = Result<int>.Failure(error);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.HasError);
            Assert.AreEqual(default(int), result.Value);
            Assert.AreEqual(ErrorCode.FileNotFound, result.Error.Code);

            Assert.IsFalse(result.TryGetValue(out int value));
            Assert.AreEqual(default(int), value);
        }

        [Test]
        public void Result_GetValueOrDefault_ShouldWork()
        {
            var successResult = Result<int>.Success(42);
            Assert.AreEqual(42, successResult.GetValueOrDefault(100));

            var failureResult = Result<int>.Failure(ErrorCode.FileNotFound);
            Assert.AreEqual(100, failureResult.GetValueOrDefault(100));
        }

        #endregion

        #region ValidationResult Tests

        [Test]
        public void ValidationResult_Valid_ShouldBeCorrect()
        {
            var result = ValidationResult.Valid;

            Assert.IsTrue(result.IsValid);
            Assert.IsFalse(result.HasErrors);
            Assert.IsFalse(result.HasWarnings);
            Assert.AreEqual(0, result.TotalIssues);
            Assert.AreEqual(ErrorCode.None, result.FirstErrorCode);
        }

        [Test]
        public void ValidationResult_Invalid_ShouldHaveCorrectProperties()
        {
            var result = ValidationResult.Invalid(ErrorCode.ParseSyntaxError, 42, 10);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.HasErrors);
            Assert.AreEqual(1, result.ErrorCount);
            Assert.AreEqual(0, result.WarningCount);
            Assert.AreEqual(ErrorSeverity.Error, result.HighestSeverity);
            Assert.AreEqual(ErrorCode.ParseSyntaxError, result.FirstErrorCode);
            Assert.AreEqual(42, result.FirstErrorLineNumber);
            Assert.AreEqual(10, result.FirstErrorColumnNumber);
        }

        #endregion

        #region ErrorAccumulator Tests

        [Test]
        public void ErrorAccumulator_ShouldAccumulateErrors()
        {
            using var accumulator = new ErrorAccumulator(Allocator.Temp, 100);

            Assert.IsTrue(accumulator.IsCreated);
            Assert.AreEqual(0, accumulator.TotalCount);

            // Add various error types
            Assert.IsTrue(accumulator.TryAddError(ErrorCode.ParseSyntaxError, ErrorSeverity.Error, 1, 1));
            Assert.IsTrue(accumulator.TryAddWarning(ErrorCode.ValidationFieldInvalid, 2, 5));
            Assert.IsTrue(accumulator.TryAddInfo(ErrorCode.None, 3, 10));

            Assert.AreEqual(1, accumulator.ErrorCount);
            Assert.AreEqual(1, accumulator.WarningCount);
            Assert.AreEqual(1, accumulator.InfoCount);
            Assert.AreEqual(3, accumulator.TotalCount);
            Assert.AreEqual(ErrorSeverity.Error, accumulator.HighestSeverity);
        }

        [Test]
        public void ErrorAccumulator_ShouldRespectMaxErrors()
        {
            using var accumulator = new ErrorAccumulator(Allocator.Temp, 2);

            Assert.IsTrue(accumulator.TryAddError(ErrorCode.ParseSyntaxError));
            Assert.IsTrue(accumulator.TryAddError(ErrorCode.ParseUnexpectedToken));
            Assert.IsFalse(accumulator.TryAddError(ErrorCode.ParseInvalidNumber));

            Assert.IsTrue(accumulator.HasReachedLimit);
            Assert.AreEqual(2, accumulator.TotalCount);
        }

        [Test]
        public void ErrorAccumulator_GetValidationResult_ShouldWork()
        {
            using var accumulator = new ErrorAccumulator(Allocator.Temp);

            accumulator.TryAddError(ErrorCode.ParseSyntaxError, ErrorSeverity.Error, 42, 10);
            accumulator.TryAddWarning(ErrorCode.ValidationFieldInvalid, 50, 5);

            var result = accumulator.GetValidationResult();

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(1, result.ErrorCount);
            Assert.AreEqual(1, result.WarningCount);
            Assert.AreEqual(ErrorSeverity.Error, result.HighestSeverity);
            Assert.AreEqual(ErrorCode.ParseSyntaxError, result.FirstErrorCode);
            Assert.AreEqual(42, result.FirstErrorLineNumber);
        }

        [Test]
        public void ErrorAccumulator_ToArray_ShouldWork()
        {
            using var accumulator = new ErrorAccumulator(Allocator.Temp);

            accumulator.TryAddError(ErrorCode.ParseSyntaxError);
            accumulator.TryAddWarning(ErrorCode.ValidationFieldInvalid);

            using var errors = accumulator.ToArray(Allocator.Temp);

            Assert.AreEqual(2, errors.Length);
            Assert.AreEqual(ErrorCode.ParseSyntaxError, errors[0].Code);
            Assert.AreEqual(ErrorCode.ValidationFieldInvalid, errors[1].Code);
        }

        #endregion

        #region ErrorUtilities Tests

        [Test]
        public void ErrorUtilities_GetErrorMessage_ShouldReturnValidMessages()
        {
            var message = ErrorUtilities.GetErrorMessage(ErrorCode.FileNotFound);
            Assert.IsFalse(string.IsNullOrEmpty(message));
            Assert.AreEqual("File not found", message);

            var unknownMessage = ErrorUtilities.GetErrorMessage((ErrorCode)9999);
            Assert.AreEqual("Unrecognized error", unknownMessage);
        }

        [Test]
        public void ErrorUtilities_IsRecoverable_ShouldClassifyCorrectly()
        {
            Assert.IsTrue(ErrorUtilities.IsRecoverable(ErrorCode.ParseSyntaxError));
            Assert.IsTrue(ErrorUtilities.IsRecoverable(ErrorCode.ValidationFieldInvalid));
            Assert.IsFalse(ErrorUtilities.IsRecoverable(ErrorCode.FileNotFound));
            Assert.IsFalse(ErrorUtilities.IsRecoverable(ErrorCode.MemoryAllocationFailed));
        }

        [Test]
        public void ErrorUtilities_GetDefaultSeverity_ShouldReturnCorrectSeverity()
        {
            Assert.AreEqual(ErrorSeverity.Info, ErrorUtilities.GetDefaultSeverity(ErrorCode.None));
            Assert.AreEqual(ErrorSeverity.Warning, ErrorUtilities.GetDefaultSeverity(ErrorCode.PerformanceThresholdExceeded));
            Assert.AreEqual(ErrorSeverity.Critical, ErrorUtilities.GetDefaultSeverity(ErrorCode.MemoryCorruption));
            Assert.AreEqual(ErrorSeverity.Error, ErrorUtilities.GetDefaultSeverity(ErrorCode.ParseSyntaxError));
        }

        #endregion

        #region ValidationResultCollection Tests

        [Test]
        public void ValidationResultCollection_ShouldCollectErrors()
        {
            using var collection = new ValidationResultCollection(Allocator.Temp);

            collection.AddError(ErrorCode.ParseSyntaxError, 1, 5);
            collection.AddWarning(ErrorCode.ValidationFieldInvalid, 2, 10);
            collection.AddInfo(ErrorCode.None, 3, 15);

            Assert.AreEqual(1, collection.ErrorCount);
            Assert.AreEqual(1, collection.WarningCount);
            Assert.AreEqual(1, collection.InfoCount);

            var summary = collection.GetSummary();
            Assert.IsFalse(summary.IsValid);
            Assert.AreEqual(1, summary.ErrorCount);
            Assert.AreEqual(ErrorSeverity.Error, summary.HighestSeverity);
        }

        [Test]
        public void ValidationResultCollection_GetErrors_ShouldReturnOnlyErrors()
        {
            using var collection = new ValidationResultCollection(Allocator.Temp);

            collection.AddError(ErrorCode.ParseSyntaxError);
            collection.AddWarning(ErrorCode.ValidationFieldInvalid);
            collection.AddError(ErrorCode.ParseUnexpectedToken);

            using var errors = collection.GetErrors(Allocator.Temp);

            Assert.AreEqual(2, errors.Length);
            Assert.IsTrue(errors[0].IsError);
            Assert.IsTrue(errors[1].IsError);
        }

        #endregion

        #region Performance Tests

        [Test]
        public void ErrorAccumulator_Performance_ShouldBeEfficient()
        {
            const int errorCount = 10000;

            using var accumulator = new ErrorAccumulator(Allocator.Temp, errorCount);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < errorCount; i++)
            {
                accumulator.TryAddError(ErrorCode.ParseSyntaxError, ErrorSeverity.Error, i, i % 100);
            }

            stopwatch.Stop();

            Assert.AreEqual(errorCount, accumulator.ErrorCount);

            // Should be able to add 10k errors in under 10ms
            Assert.Less(stopwatch.ElapsedMilliseconds, 10,
                $"Adding {errorCount} errors took {stopwatch.ElapsedMilliseconds}ms, expected < 10ms");

            Debug.Log($"Added {errorCount} errors in {stopwatch.ElapsedMilliseconds}ms ({errorCount / (float)stopwatch.ElapsedMilliseconds * 1000:F0} errors/second)");
        }

        [Test]
        public void ValidationResult_Performance_ShouldBeEfficient()
        {
            const int iterationCount = 100000;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < iterationCount; i++)
            {
                var result = ValidationResult.Invalid(ErrorCode.ParseSyntaxError, i, i % 100);
                Assert.IsFalse(result.IsValid);
            }

            stopwatch.Stop();

            // Should be able to create 100k validation results in reasonable time
            Assert.Less(stopwatch.ElapsedMilliseconds, 200,
                $"Creating {iterationCount} validation results took {stopwatch.ElapsedMilliseconds}ms, expected < 200ms");

            Debug.Log($"Created {iterationCount} validation results in {stopwatch.ElapsedMilliseconds}ms ({iterationCount / (float)stopwatch.ElapsedMilliseconds * 1000:F0} results/second)");
        }

        #endregion

        #region Memory Tests

        [Test]
        public void ErrorAccumulator_ShouldDisposeCleanly()
        {
            var accumulator = new ErrorAccumulator(Allocator.Temp);
            accumulator.TryAddError(ErrorCode.ParseSyntaxError);

            Assert.IsTrue(accumulator.IsCreated);

            accumulator.Dispose();

            // After disposal, should not be usable
            Assert.IsFalse(accumulator.TryAddError(ErrorCode.ParseSyntaxError));
        }

        [Test]
        public void ValidationResultCollection_ShouldDisposeCleanly()
        {
            var collection = new ValidationResultCollection(Allocator.Temp);
            collection.AddError(ErrorCode.ParseSyntaxError);

            Assert.IsTrue(collection.IsCreated);

            collection.Dispose();

            // After disposal, should not be usable
            Assert.AreEqual(0, collection.ErrorCount);
        }

        #endregion
    }
}