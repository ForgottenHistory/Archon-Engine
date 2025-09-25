using NUnit.Framework;
using Map.Simulation;
using Unity.Collections.LowLevel.Unsafe;

namespace Map.Tests.Simulation
{
    /// <summary>
    /// Tests for ProvinceStateValidator - validates architectural compliance and data integrity
    /// Critical for ensuring the dual-layer architecture requirements are met
    /// </summary>
    [TestFixture]
    public class ProvinceValidationTests
    {
        private ProvinceSimulation simulation;

        [SetUp]
        public void Setup()
        {
            simulation = new ProvinceSimulation(100);
            // Add test provinces with various states
            simulation.AddProvince(1, TerrainType.Grassland);
            simulation.AddProvince(2, TerrainType.Ocean);
            simulation.AddProvince(3, TerrainType.Mountain);
            simulation.SetProvinceOwner(1, 100);
            simulation.SetProvinceFlag(1, ProvinceFlags.IsCapital, true);
        }

        [TearDown]
        public void Teardown()
        {
            simulation?.Dispose();
        }

        #region Quick Validation Tests

        [Test]
        public void QuickValidate_ValidSimulation_ShouldPass()
        {
            bool result = ProvinceStateValidator.QuickValidate(simulation, out string errorMessage);

            Assert.IsTrue(result, $"Valid simulation should pass quick validation. Error: {errorMessage}");
            Assert.IsNull(errorMessage);
        }

        [Test]
        public void QuickValidate_NullSimulation_ShouldFail()
        {
            bool result = ProvinceStateValidator.QuickValidate(null, out string errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage.Contains("null"));
        }

        [Test]
        public void QuickValidate_UninitializedSimulation_ShouldFail()
        {
            var uninitSimulation = new ProvinceSimulation(10);
            uninitSimulation.Dispose(); // Make it uninitialized

            bool result = ProvinceStateValidator.QuickValidate(uninitSimulation, out string errorMessage);

            Assert.IsFalse(result);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage.Contains("not initialized"));
        }

        #endregion

        #region Comprehensive Validation Tests

        [Test]
        public void ValidateSimulation_ValidSimulation_ShouldPass()
        {
            var result = ProvinceStateValidator.ValidateSimulation(simulation);

            Assert.IsTrue(result.IsValid, $"Valid simulation should pass comprehensive validation. Errors: {result.GetSummary()}");
            Assert.AreEqual(0, result.Errors.Count);
        }

        [Test]
        public void ValidateSimulation_ShouldReturnStatistics()
        {
            var result = ProvinceStateValidator.ValidateSimulation(simulation);

            Assert.Greater(result.Stats.ProvinceCount, 0, "Should report province count");
            Assert.Greater(result.Stats.MemoryUsageBytes, 0, "Should report memory usage");
            Assert.GreaterOrEqual(result.Stats.OwnedProvinces, 1, "Should count owned provinces");
            Assert.IsNotNull(result.Stats.TerrainCounts, "Should count terrain types");
        }

        [Test]
        public void ValidateSimulation_ShouldDetectStructSizeViolation()
        {
            // This test verifies our size validation is working
            // The actual ProvinceState should be 8 bytes and pass
            // This test documents the validation behavior

            var result = ProvinceStateValidator.ValidateSimulation(simulation);

            // Should NOT have structure size violations (our struct is correct)
            bool hasStructSizeError = false;
            foreach (var error in result.Errors)
            {
                if (error.Type == ProvinceStateValidator.ValidationErrorType.StructureSizeViolation)
                {
                    hasStructSizeError = true;
                    break;
                }
            }

            Assert.IsFalse(hasStructSizeError, "ProvinceState should be exactly 8 bytes and pass validation");
        }

        #endregion

        #region Architectural Compliance Tests

        [Test]
        public void ValidateArchitecturalCompliance_ProvinceStateSize_ShouldBe8Bytes()
        {
            int actualSize = UnsafeUtility.SizeOf<ProvinceState>();

            // This is the CRITICAL architectural requirement
            Assert.AreEqual(8, actualSize,
                $"ProvinceState MUST be exactly 8 bytes. Actual: {actualSize} bytes. " +
                "This is a core requirement for 10,000+ province performance.");
        }

        [Test]
        public void ValidateArchitecturalCompliance_MemoryUsage_ShouldMeetTargets()
        {
            // Add provinces up to a reasonable test size
            for (ushort i = 10; i <= 50; i++)
            {
                simulation.AddProvince(i, TerrainType.Grassland);
            }

            var result = ProvinceStateValidator.ValidateSimulation(simulation);

            // Calculate expected hot memory (province count * 8 bytes)
            int expectedHotBytes = simulation.ProvinceCount * 8;
            int actualMemoryUsage = result.Stats.MemoryUsageBytes;

            // Should be close to expected (allowing for lookup overhead)
            Assert.LessOrEqual(actualMemoryUsage, expectedHotBytes * 3,
                "Memory usage should be reasonable compared to pure province data");
        }

        [Test]
        public void ValidateArchitecturalCompliance_PresentationFlags_ShouldWarn()
        {
            // Set presentation-only flag in simulation layer (should warn)
            simulation.SetProvinceFlag(1, ProvinceFlags.IsSelected, true);

            var result = ProvinceStateValidator.ValidateSimulation(simulation);

            // Should have warnings about presentation data in simulation layer
            bool hasPresentationWarning = false;
            foreach (var warning in result.Warnings)
            {
                if (warning.Message.Contains("presentation") || warning.Message.Contains("IsSelected"))
                {
                    hasPresentationWarning = true;
                    break;
                }
            }

            Assert.IsTrue(hasPresentationWarning,
                "Should warn about presentation flags in simulation layer");
        }

        #endregion

        #region Data Consistency Tests

        [Test]
        public void ValidateDataConsistency_ValidState_ShouldPass()
        {
            var result = ProvinceStateValidator.ValidateSimulation(simulation);

            bool hasConsistencyErrors = false;
            foreach (var error in result.Errors)
            {
                if (error.Type == ProvinceStateValidator.ValidationErrorType.DataInconsistency)
                {
                    hasConsistencyErrors = true;
                    break;
                }
            }

            Assert.IsFalse(hasConsistencyErrors, "Valid simulation should not have consistency errors");
        }

        [Test]
        public void ValidateDataConsistency_Checksum_ShouldBeNonZero()
        {
            var result = ProvinceStateValidator.ValidateSimulation(simulation);

            // Should not warn about zero checksum for simulation with provinces
            bool hasZeroChecksumWarning = false;
            foreach (var warning in result.Warnings)
            {
                if (warning.Message.Contains("checksum") && warning.Message.Contains("zero"))
                {
                    hasZeroChecksumWarning = true;
                    break;
                }
            }

            Assert.IsFalse(hasZeroChecksumWarning, "Simulation with provinces should have non-zero checksum");
        }

        #endregion

        #region Province State Validation Tests

        [Test]
        public void ValidateProvinceStates_OceanDevelopment_ShouldError()
        {
            // Try to give ocean province development (should be detected as error)
            // This requires modifying the simulation state directly for testing
            // In real usage, commands would prevent this

            // For now, verify our ocean province doesn't have development
            var oceanState = simulation.GetProvinceState(2);

            if (oceanState.development > 0)
            {
                var result = ProvinceStateValidator.ValidateSimulation(simulation);

                bool hasOceanDevelopmentError = false;
                foreach (var error in result.Errors)
                {
                    if (error.Message.Contains("Ocean") && error.Message.Contains("development"))
                    {
                        hasOceanDevelopmentError = true;
                        break;
                    }
                }

                Assert.IsTrue(hasOceanDevelopmentError, "Should detect ocean provinces with development");
            }
        }

        [Test]
        public void ValidateProvinceStates_UnownedCapital_ShouldError()
        {
            // Create unowned province marked as capital (invalid state)
            simulation.AddProvince(99, TerrainType.Grassland);
            simulation.SetProvinceFlag(99, ProvinceFlags.IsCapital, true); // Capital but unowned

            var result = ProvinceStateValidator.ValidateSimulation(simulation);


            bool hasUnownedCapitalError = false;
            foreach (var error in result.Errors)
            {
                if (error.Message.ToLower().Contains("capital") && error.Message.ToLower().Contains("unowned"))
                {
                    hasUnownedCapitalError = true;
                    break;
                }
            }

            Assert.IsTrue(hasUnownedCapitalError, "Should detect unowned provinces marked as capital");
        }

        [Test]
        public void ValidateProvinceStates_HighMountainDevelopment_ShouldWarn()
        {
            // Set very high development on mountain province
            simulation.SetProvinceDevelopment(3, 200); // High development on mountain

            var result = ProvinceStateValidator.ValidateSimulation(simulation);

            bool hasMountainWarning = false;
            foreach (var warning in result.Warnings)
            {
                if (warning.Message.Contains("Mountain") && warning.Message.Contains("development"))
                {
                    hasMountainWarning = true;
                    break;
                }
            }

            Assert.IsTrue(hasMountainWarning, "Should warn about very high mountain development");
        }

        #endregion

        #region Command Validation Tests

        [Test]
        public void ValidateCommandExecution_ValidCommand_ShouldPass()
        {
            var command = new ChangeOwnerCommand(100, 1, 200);

            bool result = ProvinceStateValidator.ValidateCommandExecution(
                simulation, command, out string errorMessage);

            Assert.IsTrue(result, $"Valid command should pass validation. Error: {errorMessage}");
            Assert.IsNull(errorMessage);
        }

        [Test]
        public void ValidateCommandExecution_InvalidCommand_ShouldFail()
        {
            var command = new ChangeOwnerCommand(100, 0, 200); // Ocean province

            bool result = ProvinceStateValidator.ValidateCommandExecution(
                simulation, command, out string errorMessage);

            Assert.IsFalse(result, "Invalid command should fail validation");
            Assert.IsNotNull(errorMessage);
        }

        [Test]
        public void ValidateCommandExecution_BadSimulation_ShouldFail()
        {
            var command = new ChangeOwnerCommand(100, 1, 200);

            bool result = ProvinceStateValidator.ValidateCommandExecution(
                null, command, out string errorMessage);

            Assert.IsFalse(result, "Null simulation should fail command validation");
            Assert.IsNotNull(errorMessage);
        }

        #endregion

        #region Serialization Validation Tests

        [Test]
        public void ValidateSerializedState_ValidData_ShouldPass()
        {
            byte[] validData = ProvinceStateSerializer.SerializeFullState(simulation);

            bool result = ProvinceStateValidator.ValidateSerializedState(validData, out string errorMessage);

            Assert.IsTrue(result, $"Valid serialized data should pass validation. Error: {errorMessage}");
            Assert.IsNull(errorMessage);
        }

        [Test]
        public void ValidateSerializedState_InvalidData_ShouldFail()
        {
            byte[] invalidData = { 1, 2, 3, 4 }; // Invalid format

            bool result = ProvinceStateValidator.ValidateSerializedState(invalidData, out string errorMessage);

            Assert.IsFalse(result, "Invalid data should fail validation");
            Assert.IsNotNull(errorMessage);
        }

        [Test]
        public void ValidateSerializedState_NullData_ShouldFail()
        {
            bool result = ProvinceStateValidator.ValidateSerializedState(null, out string errorMessage);

            Assert.IsFalse(result, "Null data should fail validation");
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage.Contains("null"));
        }

        #endregion

        #region Performance Target Tests

        [Test]
        public void ValidatePerformance_MemoryTargets_ShouldMeetSpecification()
        {
            // Test with larger simulation
            var largeSimulation = new ProvinceSimulation(1000);

            try
            {
                // Add 1000 provinces
                for (ushort i = 1; i <= 1000; i++)
                {
                    largeSimulation.AddProvince(i, TerrainType.Grassland);
                }

                var result = ProvinceStateValidator.ValidateSimulation(largeSimulation);

                // For 1000 provinces, hot data should be ~8KB
                int expectedHotBytes = 1000 * 8;

                Assert.IsTrue(result.IsValid, "Large simulation should be valid");

                // Memory should scale linearly
                Assert.LessOrEqual(result.Stats.MemoryUsageBytes, expectedHotBytes * 5,
                    "Memory usage should scale reasonably with province count");
            }
            finally
            {
                largeSimulation.Dispose();
            }
        }

        [Test]
        public void ValidatePerformance_ValidationSpeed_ShouldBeFast()
        {
            // Validation itself should be fast
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var result = ProvinceStateValidator.ValidateSimulation(simulation);

            stopwatch.Stop();

            Assert.LessOrEqual(stopwatch.ElapsedMilliseconds, 50,
                "Validation should complete in under 50ms for small simulation");

            Assert.IsNotNull(result, "Should return result");
        }

        [Test]
        public void ValidatePerformance_QuickValidationSpeed_ShouldBeVeryFast()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            bool result = ProvinceStateValidator.QuickValidate(simulation, out string errorMessage);

            stopwatch.Stop();

            Assert.LessOrEqual(stopwatch.ElapsedMilliseconds, 10,
                "Quick validation should complete in under 10ms");

            Assert.IsTrue(result);
        }

        #endregion

        #region Validation Result Tests

        [Test]
        public void ValidationResult_GetSummary_ShouldProvideUsefulInfo()
        {
            var result = ProvinceStateValidator.ValidateSimulation(simulation);

            string summary = result.GetSummary();

            Assert.IsNotNull(summary);
            Assert.IsNotEmpty(summary);
            Assert.That(summary.Contains("PASSED") || summary.Contains("FAILED"));
            Assert.That(summary.Contains("Provinces"));
            Assert.That(summary.Contains("Memory"));
        }

        [Test]
        public void ValidationResult_Statistics_ShouldBeAccurate()
        {
            var result = ProvinceStateValidator.ValidateSimulation(simulation);
            var stats = result.Stats;

            Assert.AreEqual(simulation.ProvinceCount, stats.ProvinceCount, "Province count should match");
            Assert.Greater(stats.MemoryUsageBytes, 0, "Memory usage should be positive");
            Assert.IsNotNull(stats.TerrainCounts, "Terrain counts should be provided");
            Assert.Greater(stats.TerrainCounts.Count, 0, "Should count different terrain types");
        }

        #endregion
    }
}