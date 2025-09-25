using NUnit.Framework;
using Unity.Collections;
using Map.Simulation;
using System.Collections.Generic;
using System;

namespace Map.Tests.Simulation
{
    /// <summary>
    /// Tests for ProvinceSimulation class - validates core simulation functionality
    /// Tests the hot data management and performance characteristics
    /// </summary>
    [TestFixture]
    public class ProvinceSimulationTests
    {
        private ProvinceSimulation simulation;

        [SetUp]
        public void Setup()
        {
            simulation = new ProvinceSimulation(1000); // Test capacity
        }

        [TearDown]
        public void Teardown()
        {
            simulation?.Dispose();
        }

        [Test]
        public void ProvinceSimulation_Initialization_ShouldSetCorrectValues()
        {
            Assert.IsTrue(simulation.IsInitialized);
            Assert.AreEqual(0, simulation.ProvinceCount);
            Assert.AreEqual(0, simulation.StateVersion);
            Assert.IsFalse(simulation.IsDirty);
        }

        [Test]
        public void AddProvince_ValidID_ShouldSucceed()
        {
            bool result = simulation.AddProvince(1, TerrainType.Grassland);

            Assert.IsTrue(result, "Adding valid province should succeed");
            Assert.AreEqual(1, simulation.ProvinceCount);
            Assert.IsTrue(simulation.IsDirty, "Simulation should be marked dirty");
            Assert.Greater(simulation.StateVersion, 0u, "State version should be incremented");
        }

        [Test]
        public void AddProvince_OceanID_ShouldFail()
        {
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, "Province ID 0 is reserved for ocean");

            bool result = simulation.AddProvince(0, TerrainType.Ocean);

            Assert.IsFalse(result, "Adding province with ID 0 (ocean) should fail");
            Assert.AreEqual(0, simulation.ProvinceCount);
        }

        [Test]
        public void AddProvince_DuplicateID_ShouldFail()
        {
            simulation.AddProvince(1, TerrainType.Grassland);
            bool result = simulation.AddProvince(1, TerrainType.Forest);

            Assert.IsFalse(result, "Adding duplicate province ID should fail");
            Assert.AreEqual(1, simulation.ProvinceCount, "Province count should not increase");
        }

        [Test]
        public void GetProvinceState_ExistingProvince_ShouldReturnCorrectData()
        {
            ushort provinceID = 123;
            simulation.AddProvince(provinceID, TerrainType.Hills);

            var state = simulation.GetProvinceState(provinceID);

            Assert.AreEqual(0, state.ownerID, "New province should be unowned");
            Assert.AreEqual((byte)TerrainType.Hills, state.terrain);
            Assert.AreEqual(1, state.development, "Should have default development");
        }

        [Test]
        public void GetProvinceState_NonExistentProvince_ShouldReturnDefault()
        {
            var state = simulation.GetProvinceState(999);

            // Should return default state without crashing
            Assert.AreEqual(0, state.ownerID);
            Assert.AreEqual(1, state.terrain); // Default grassland
        }

        [Test]
        public void SetProvinceOwner_ValidProvince_ShouldUpdateState()
        {
            ushort provinceID = 50;
            ushort ownerID = 200;

            simulation.AddProvince(provinceID, TerrainType.Grassland);
            bool result = simulation.SetProvinceOwner(provinceID, ownerID);

            Assert.IsTrue(result, "Setting owner should succeed");

            var state = simulation.GetProvinceState(provinceID);
            Assert.AreEqual(ownerID, state.ownerID);
            Assert.AreEqual(ownerID, state.controllerID, "Owner should control by default");
            Assert.IsTrue(state.IsOwned);
            Assert.IsFalse(state.IsOccupied);
        }

        [Test]
        public void SetProvinceController_ValidProvince_ShouldCreateOccupation()
        {
            ushort provinceID = 75;
            ushort ownerID = 100;
            ushort controllerID = 200;

            simulation.AddProvince(provinceID, TerrainType.Grassland);
            simulation.SetProvinceOwner(provinceID, ownerID);
            bool result = simulation.SetProvinceController(provinceID, controllerID);

            Assert.IsTrue(result, "Setting controller should succeed");

            var state = simulation.GetProvinceState(provinceID);
            Assert.AreEqual(ownerID, state.ownerID);
            Assert.AreEqual(controllerID, state.controllerID);
            Assert.IsTrue(state.IsOwned);
            Assert.IsTrue(state.IsOccupied, "Should be occupied by different controller");
        }

        [Test]
        public void SetProvinceDevelopment_ValidValues_ShouldUpdateCorrectly()
        {
            ushort provinceID = 25;
            byte newDevelopment = 150;

            simulation.AddProvince(provinceID, TerrainType.Grassland);
            bool result = simulation.SetProvinceDevelopment(provinceID, newDevelopment);

            Assert.IsTrue(result);
            Assert.AreEqual(newDevelopment, simulation.GetProvinceState(provinceID).development);
        }

        [Test]
        public void SetProvinceFlag_ValidFlag_ShouldUpdateCorrectly()
        {
            ushort provinceID = 30;

            simulation.AddProvince(provinceID, TerrainType.Grassland);

            // Set flag
            bool result1 = simulation.SetProvinceFlag(provinceID, ProvinceFlags.IsCoastal, true);
            Assert.IsTrue(result1);
            Assert.IsTrue(simulation.GetProvinceState(provinceID).HasFlag(ProvinceFlags.IsCoastal));

            // Clear flag
            bool result2 = simulation.SetProvinceFlag(provinceID, ProvinceFlags.IsCoastal, false);
            Assert.IsTrue(result2);
            Assert.IsFalse(simulation.GetProvinceState(provinceID).HasFlag(ProvinceFlags.IsCoastal));
        }

        [Test]
        public void GetProvincesByOwner_MultipleProvinces_ShouldReturnCorrectList()
        {
            ushort ownerID = 300;
            ushort[] provinceIDs = { 1, 2, 3, 4, 5 };

            // Add provinces and assign some to the test owner
            foreach (ushort id in provinceIDs)
            {
                simulation.AddProvince(id, TerrainType.Grassland);
            }

            simulation.SetProvinceOwner(1, ownerID);
            simulation.SetProvinceOwner(3, ownerID);
            simulation.SetProvinceOwner(5, ownerID);

            var result = new NativeList<ushort>(10, Allocator.Temp);
            simulation.GetProvincesByOwner(ownerID, result);

            Assert.AreEqual(3, result.Length, "Should find 3 provinces owned by test owner");

            var resultArray = result.AsArray();
            Assert.Contains((ushort)1, resultArray.ToArray());
            Assert.Contains((ushort)3, resultArray.ToArray());
            Assert.Contains((ushort)5, resultArray.ToArray());

            result.Dispose();
        }

        [Test]
        public void DirtyFlagSystem_ShouldTrackChangesCorrectly()
        {
            ushort provinceID = 40;

            // Initially clean
            Assert.IsFalse(simulation.IsDirty);
            Assert.AreEqual(0, simulation.GetDirtyIndices().Count);

            // Add province - should become dirty
            simulation.AddProvince(provinceID, TerrainType.Grassland);
            Assert.IsTrue(simulation.IsDirty);
            Assert.Greater(simulation.GetDirtyIndices().Count, 0);

            // Clear dirty flags
            simulation.ClearDirtyFlags();
            Assert.IsFalse(simulation.IsDirty);
            Assert.AreEqual(0, simulation.GetDirtyIndices().Count);

            // Modify province - should become dirty again
            simulation.SetProvinceOwner(provinceID, 100);
            Assert.IsTrue(simulation.IsDirty);
            Assert.Greater(simulation.GetDirtyIndices().Count, 0);
        }

        [Test]
        public void StateVersion_ShouldIncrementOnChanges()
        {
            uint initialVersion = simulation.StateVersion;

            simulation.AddProvince(1, TerrainType.Grassland);
            Assert.Greater(simulation.StateVersion, initialVersion, "Version should increment on add");

            uint version1 = simulation.StateVersion;
            simulation.SetProvinceOwner(1, 100);
            Assert.Greater(simulation.StateVersion, version1, "Version should increment on ownership change");

            uint version2 = simulation.StateVersion;
            simulation.SetProvinceDevelopment(1, 50);
            Assert.Greater(simulation.StateVersion, version2, "Version should increment on development change");
        }

        [Test]
        public void CalculateStateChecksum_ShouldBeConsistent()
        {
            // Add some provinces with known states
            simulation.AddProvince(1, TerrainType.Grassland);
            simulation.AddProvince(2, TerrainType.Forest);
            simulation.SetProvinceOwner(1, 100);

            uint checksum1 = simulation.CalculateStateChecksum();

            // Create identical simulation
            var simulation2 = new ProvinceSimulation(1000);
            simulation2.AddProvince(1, TerrainType.Grassland);
            simulation2.AddProvince(2, TerrainType.Forest);
            simulation2.SetProvinceOwner(1, 100);

            uint checksum2 = simulation2.CalculateStateChecksum();

            Assert.AreEqual(checksum1, checksum2, "Identical states should have same checksum");

            simulation2.Dispose();
        }

        [Test]
        public void CalculateStateChecksum_ShouldChangeBetweenStates()
        {
            simulation.AddProvince(1, TerrainType.Grassland);
            uint checksum1 = simulation.CalculateStateChecksum();

            simulation.SetProvinceOwner(1, 100);
            uint checksum2 = simulation.CalculateStateChecksum();

            Assert.AreNotEqual(checksum1, checksum2, "Different states should have different checksums");
        }

        [Test]
        public void GetMemoryUsage_ShouldReportAccurateValues()
        {
            // Add some provinces
            for (ushort i = 1; i <= 100; i++)
            {
                simulation.AddProvince(i, TerrainType.Grassland);
            }

            var (totalBytes, hotBytes, coldBytes) = simulation.GetMemoryUsage();

            Assert.Greater(totalBytes, 0, "Total memory usage should be positive");
            Assert.Greater(hotBytes, 0, "Hot memory usage should be positive");
            Assert.AreEqual(100 * 8, hotBytes, "Hot memory should be 100 provinces * 8 bytes each");
        }

        [Test]
        public void ValidateState_ValidSimulation_ShouldReturnTrue()
        {
            simulation.AddProvince(1, TerrainType.Grassland);
            simulation.AddProvince(2, TerrainType.Hills);

            bool isValid = simulation.ValidateState(out string errorMessage);

            Assert.IsTrue(isValid, $"Valid simulation should pass validation. Error: {errorMessage}");
            Assert.IsNull(errorMessage, "No error message should be returned for valid state");
        }

        [Test]
        public void GetAllProvinces_ShouldReturnReadOnlyAccess()
        {
            simulation.AddProvince(1, TerrainType.Grassland);
            simulation.AddProvince(2, TerrainType.Forest);

            var allProvinces = simulation.GetAllProvinces();

            Assert.AreEqual(2, allProvinces.Length);
            // ReadOnly access - cannot modify the returned array
        }

        [Test]
        public void ColdDataAccess_ShouldCreateOnDemand()
        {
            ushort provinceID = 123;
            simulation.AddProvince(provinceID, TerrainType.Grassland);

            var coldData = simulation.GetColdData(provinceID);

            Assert.IsNotNull(coldData);
            Assert.AreEqual(provinceID, coldData.ProvinceID);
            Assert.AreEqual($"Province_{provinceID}", coldData.Name, "Should have default name");
        }

        [Test]
        public void ColdDataAccess_ShouldPersistAcrossAccesses()
        {
            ushort provinceID = 456;
            simulation.AddProvince(provinceID, TerrainType.Grassland);

            var coldData1 = simulation.GetColdData(provinceID);
            coldData1.Name = "Test Province";

            var coldData2 = simulation.GetColdData(provinceID);

            Assert.AreEqual("Test Province", coldData2.Name, "Cold data should persist");
            Assert.AreSame(coldData1, coldData2, "Should return same instance");
        }

        [Test]
        public void CapacityLimit_ShouldPreventOverflow()
        {
            // Create small simulation
            var smallSimulation = new ProvinceSimulation(2);

            Assert.IsTrue(smallSimulation.AddProvince(1, TerrainType.Grassland));
            Assert.IsTrue(smallSimulation.AddProvince(2, TerrainType.Forest));

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, "Province capacity exceeded (2)");
            Assert.IsFalse(smallSimulation.AddProvince(3, TerrainType.Hills), "Should fail when capacity exceeded");

            Assert.AreEqual(2, smallSimulation.ProvinceCount, "Count should not exceed capacity");

            smallSimulation.Dispose();
        }

        [Test]
        public void Dispose_ShouldCleanUpResources()
        {
            simulation.AddProvince(1, TerrainType.Grassland);
            Assert.IsTrue(simulation.IsInitialized);

            simulation.Dispose();

            Assert.IsFalse(simulation.IsInitialized, "Should be marked as not initialized after disposal");
        }

        [Test]
        public void LogStatistics_ShouldNotThrow()
        {
            // Add some test data
            for (ushort i = 1; i <= 50; i++)
            {
                simulation.AddProvince(i, TerrainType.Grassland);
                if (i % 10 == 0)
                {
                    simulation.SetProvinceOwner(i, (ushort)(i / 10));
                }
            }

            // Should not throw exception
            Assert.DoesNotThrow(() => simulation.LogStatistics());
        }

        [Test]
        public void PerformanceTargets_ShouldMeetMemoryRequirements()
        {
            // Test with reasonable province count for testing
            const int testProvinces = 1000;

            var largeSimulation = new ProvinceSimulation(testProvinces);

            try
            {
                // Add provinces up to test count
                for (ushort i = 1; i <= testProvinces; i++)
                {
                    largeSimulation.AddProvince(i, TerrainType.Grassland);
                }

                var (totalBytes, hotBytes, coldBytes) = largeSimulation.GetMemoryUsage();

                // Hot data should be exactly 8 bytes per province
                int expectedHotBytes = largeSimulation.ProvinceCount * 8;
                Assert.LessOrEqual(hotBytes, expectedHotBytes * 1.1, // 10% overhead allowance
                    $"Hot memory usage should be close to {expectedHotBytes} bytes");

                // For 1000 provinces, should be well under limits
                // Account for lookup overhead: 1000 provinces * 6 bytes = 6KB + 8KB hot data = ~14KB
                Assert.LessOrEqual(totalBytes, 20 * 1024, // 20KB should be plenty for 1000 provinces
                    $"Total memory should be reasonable. Got {totalBytes} bytes");
            }
            finally
            {
                largeSimulation.Dispose();
            }
        }
    }
}