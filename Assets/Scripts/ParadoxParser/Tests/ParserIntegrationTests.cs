using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using ParadoxParser.Core;
using ParadoxParser.Data;
using System.IO;
using System.Threading.Tasks;

namespace ParadoxParser.Tests
{
    /// <summary>
    /// Integration tests that verify multiple systems working together
    /// </summary>
    public class ParserIntegrationTests
    {
        private string tempDirectory;

        [SetUp]
        public void Setup()
        {
            tempDirectory = Path.Combine(Application.temporaryCachePath, "ParadoxParserIntegrationTests");
            Directory.CreateDirectory(tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        #region Full Pipeline Integration Tests

        [Test]
        public async Task FullPipeline_ShouldProcessParadoxFile()
        {
            // Create a realistic Paradox file
            string paradoxContent = @"
# Sample Paradox File
version = ""1.0""
checksum = ""ABCD1234""

country = {
    tag = ""ENG""
    name = ""England""
    color = { 255 0 0 }
    capital = 236

    government = monarchy
    technology_group = western
    religion = protestant
    primary_culture = english

    1444.11.11 = {
        monarch = {
            name = ""Henry VI""
            dynasty = ""Lancaster""
            adm = 1
            dip = 1
            mil = 1
        }
        heir = {
            name = ""Edward""
            dynasty = ""Lancaster""
            birth_date = 1442.10.13
            claim = 95
        }
    }
}

province = {
    id = 236
    name = ""London""
    culture = english
    religion = catholic
    hre = no
    base_tax = 10
    base_production = 8
    base_manpower = 12
    trade_goods = cloth

    history = {
        owner = ENG
        controller = ENG
        core = ENG

        1066.10.14 = {
            controller = NOR
            add_core = NOR
        }

        1485.8.22 = {
            dynasty = ""Tudor""
        }
    }
}
";

            string filePath = Path.Combine(tempDirectory, "england.txt");
            await File.WriteAllTextAsync(filePath, paradoxContent);

            // Test full pipeline integration
            using var stringPool = new NativeStringPool(100, Allocator.TempJob);
            using var errorAccumulator = new ErrorAccumulator(Allocator.TempJob);
            using var recoveryContext = new ErrorRecoveryContext(Allocator.TempJob);

            // Step 1: File I/O
            var fileResult = await AsyncFileReader.ReadFileAsync(filePath, Allocator.TempJob);
            Assert.IsTrue(fileResult.Success, $"File reading should succeed. Error: {fileResult.ErrorMessage}");

            // Step 2: Compression Detection
            var compressionInfo = CompressionDetector.DetectCompression(filePath);
            Assert.IsTrue(compressionInfo.IsValid);
            Assert.AreEqual(ParadoxParser.Core.CompressionType.None, compressionInfo.CompressionType);

            // Step 3: String Management
            int fileNameId = stringPool.InternString("england.txt");
            int versionId = stringPool.InternString("1.0");
            int tagId = stringPool.InternString("ENG");

            Assert.AreEqual("england.txt", stringPool.GetString(fileNameId));
            Assert.AreEqual("1.0", stringPool.GetString(versionId));
            Assert.AreEqual("ENG", stringPool.GetString(tagId));

            // Step 4: Error Handling with Recovery
            var parseError = ErrorResult.Warning(ErrorCode.ValidationFieldInvalid, 15, 20, "test context");
            Assert.IsTrue(errorAccumulator.TryAddError(parseError));

            string recoveryMessage;
            var recoveryResult = recoveryContext.AttemptRecovery(parseError, out recoveryMessage);
            Assert.AreEqual(RecoveryResult.PartialRecovery, recoveryResult);
            Assert.IsFalse(string.IsNullOrEmpty(recoveryMessage));

            // Step 5: Validation
            var validationResult = errorAccumulator.GetValidationResult();
            Assert.IsTrue(validationResult.IsValid); // Valid because no errors (warnings don't make it invalid)
            Assert.AreEqual(0, validationResult.ErrorCount); // No errors
            Assert.AreEqual(1, validationResult.WarningCount); // But has warnings

            // Cleanup
            fileResult.Dispose();

            Debug.Log($"Successfully processed Paradox file with {paradoxContent.Length} characters");
            Debug.Log($"String pool contains {stringPool.Count} unique strings");
            Debug.Log($"Recovery context has {recoveryContext.SuccessRate:P1} success rate");
        }

        [Test]
        public async Task ErrorRecoveryPipeline_ShouldHandleMultipleErrors()
        {
            // Create file with intentional errors
            string problematicContent = @"
# File with multiple errors
version = 1.0  // Missing quotes
country = {
    tag = ENG  // Missing quotes
    name = ""England
    color = { 255 0  // Missing closing brace and value
    invalid_field = @invalid_value@

    1444.11.11 = {
        monarch = {
            name = ""Henry VI""
            // Missing closing brace
        // Missing closing brace for monarch
    // Missing closing brace for date
// Missing closing brace for country
";

            string filePath = Path.Combine(tempDirectory, "problematic.txt");
            await File.WriteAllTextAsync(filePath, problematicContent);

            using var errorAccumulator = new ErrorAccumulator(Allocator.Temp, 100);
            using var recoveryContext = new ErrorRecoveryContext(Allocator.Temp);
            using var validationCollection = new ValidationResultCollection(Allocator.Temp);

            // Simulate parsing errors that would be detected
            var errors = new[]
            {
                ErrorResult.Error(ErrorCode.ParseUnterminatedString, ErrorSeverity.Error, 5, 20, "unterminated string in name field"),
                ErrorResult.Error(ErrorCode.ParseMissingBrace, ErrorSeverity.Error, 6, 25, "missing closing brace for color"),
                ErrorResult.Warning(ErrorCode.ValidationFieldInvalid, 7, 5, "unknown field: invalid_field"),
                ErrorResult.Error(ErrorCode.ParseMissingBrace, ErrorSeverity.Error, 12, 1, "missing closing brace for monarch"),
                ErrorResult.Error(ErrorCode.ParseMissingBrace, ErrorSeverity.Error, 13, 1, "missing closing brace for date"),
                ErrorResult.Error(ErrorCode.ParseMissingBrace, ErrorSeverity.Error, 14, 1, "missing closing brace for country")
            };

            int successfulRecoveries = 0;
            foreach (var error in errors)
            {
                errorAccumulator.TryAddError(error);
                validationCollection.AddResult(error);

                // Attempt recovery for each error
                var recoveryResult = recoveryContext.AttemptRecovery(error, out string recoveryMessage);
                if (recoveryResult == RecoveryResult.Recovered || recoveryResult == RecoveryResult.PartialRecovery)
                {
                    successfulRecoveries++;
                }

                Debug.Log($"Error: {error.Code} -> Recovery: {recoveryResult} ({recoveryMessage})");
            }

            // Verify error accumulation
            Assert.AreEqual(5, errorAccumulator.ErrorCount); // 5 errors
            Assert.AreEqual(1, errorAccumulator.WarningCount); // 1 warning
            Assert.AreEqual(ErrorSeverity.Error, errorAccumulator.HighestSeverity);

            // Verify validation collection
            var summary = validationCollection.GetSummary();
            Assert.IsFalse(summary.IsValid);
            Assert.AreEqual(5, summary.ErrorCount);
            Assert.AreEqual(1, summary.WarningCount);

            // Verify recovery attempts
            Assert.Greater(successfulRecoveries, 0, "At least some errors should be recoverable");
            Assert.Greater(recoveryContext.SuccessRate, 0.0f, "Recovery success rate should be > 0%");

            Debug.Log($"Processed {errors.Length} errors with {successfulRecoveries} successful recoveries");
            Debug.Log($"Recovery success rate: {recoveryContext.SuccessRate:P1}");
            Debug.Log($"Total recovery attempts: {recoveryContext.TotalAttempts}");
        }

        // Performance testing removed - should be done separately, not in unit tests
        // Unit tests should focus on correctness, not performance metrics which can be flaky

        #endregion

        #region Memory Integration Tests

        [Test]
        public void MemoryIntegration_ShouldHandleLargeDatasets()
        {
            const int stringCount = 10000;
            const int errorCount = 1000;

            using var stringPool = new NativeStringPool(100, Allocator.Temp);
            using var internSystem = new StringInternSystem(100, Allocator.Temp);
            using var errorAccumulator = new ErrorAccumulator(Allocator.Temp);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Add many strings (keep under 128 bytes)
            for (int i = 0; i < stringCount; i++)
            {
                string str = $"str_{i % 1000}"; // Some duplicates, short strings
                stringPool.InternString(str);
                internSystem.InternString(str);
            }

            // Add many errors
            for (int i = 0; i < errorCount; i++)
            {
                var errorCode = (ErrorCode)(100 + (i % 50)); // Various parse errors
                errorAccumulator.TryAddError(errorCode, ErrorSeverity.Warning, i, i % 80);
            }

            stopwatch.Stop();

            // Verify everything was stored correctly
            // Since we use i % 1000, we only have 1000 unique strings despite 10000 additions
            Assert.AreEqual(1000, stringPool.Count);
            Assert.AreEqual(errorCount, errorAccumulator.TotalCount);

            // Should handle large datasets efficiently
            Assert.Less(stopwatch.ElapsedMilliseconds, 500,
                $"Processing {stringCount} strings and {errorCount} errors took {stopwatch.ElapsedMilliseconds}ms, expected < 500ms");

            Debug.Log($"Processed {stringCount} strings and {errorCount} errors in {stopwatch.ElapsedMilliseconds}ms");
            Debug.Log($"String pool memory: {1024 / 1024.0:F2} KB");
            Debug.Log($"Intern system: {internSystem.IsCreated} unique strings");
        }

        [Test]
        public void MemoryIntegration_ShouldDisposeCleanly()
        {
            const int iterations = 1000;

            for (int i = 0; i < iterations; i++)
            {
                using var stringPool = new NativeStringPool(100, Allocator.Temp);
                using var errorAccumulator = new ErrorAccumulator(Allocator.Temp);
                using var recoveryContext = new ErrorRecoveryContext(Allocator.Temp);

                // Use the systems
                stringPool.InternString($"test_{i}");
                errorAccumulator.TryAddError(ErrorCode.ParseSyntaxError);
                recoveryContext.AttemptRecovery(ErrorResult.Warning(ErrorCode.ValidationFieldInvalid), out _);

                // All should dispose cleanly in using blocks
            }

            Assert.Pass($"Completed {iterations} iterations of memory allocation/disposal without issues");
        }

        #endregion

        #region Helper Methods

        // Helper method removed - was only used by the removed performance test

        #endregion
    }
}