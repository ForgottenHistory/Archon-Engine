using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using Map.Simulation;

namespace Map.Tests.Integration
{
    /// <summary>
    /// Integration tests for texture system components without MonoBehaviour dependencies
    /// Tests integration between simulation data and texture infrastructure
    /// </summary>
    [TestFixture]
    public class TextureSystemIntegrationTests
    {
        private ProvinceSimulation testSimulation;
        private SimulationMapLoader.SimulationMapData testMapData;

        [SetUp]
        public void Setup()
        {
            testSimulation = new ProvinceSimulation(10);
            testSimulation.AddProvince(1, TerrainType.Grassland);
            testSimulation.AddProvince(2, TerrainType.Hills);
            testSimulation.AddProvince(3, TerrainType.Forest);

            testMapData = CreateTestMapData();
        }

        [TearDown]
        public void Teardown()
        {
            testSimulation?.Dispose();
            testMapData.Dispose();
        }

        private SimulationMapLoader.SimulationMapData CreateTestMapData()
        {
            var bounds = new NativeArray<SimulationMapLoader.ProvinceBounds>(3, Allocator.Persistent);
            var colors = new NativeHashMap<ushort, Color32>(3, Allocator.Persistent);

            bounds[0] = new SimulationMapLoader.ProvinceBounds
            {
                ProvinceID = 1,
                MinX = 0, MinY = 0, MaxX = 10, MaxY = 10,
                CenterX = 5, CenterY = 5,
                PixelCount = 121
            };

            bounds[1] = new SimulationMapLoader.ProvinceBounds
            {
                ProvinceID = 2,
                MinX = 20, MinY = 0, MaxX = 30, MaxY = 10,
                CenterX = 25, CenterY = 5,
                PixelCount = 121
            };

            bounds[2] = new SimulationMapLoader.ProvinceBounds
            {
                ProvinceID = 3,
                MinX = 0, MinY = 20, MaxX = 10, MaxY = 30,
                CenterX = 5, CenterY = 25,
                PixelCount = 121
            };

            colors.TryAdd(1, new Color32(255, 0, 0, 255)); // Red
            colors.TryAdd(2, new Color32(0, 255, 0, 255)); // Green
            colors.TryAdd(3, new Color32(0, 0, 255, 255)); // Blue

            return new SimulationMapLoader.SimulationMapData
            {
                Width = 64,
                Height = 64,
                ProvinceBounds = bounds,
                ProvinceColors = colors,
                IsValid = true
            };
        }

        [Test]
        public void SimulationMapData_ShouldContainValidProvinceData()
        {
            Assert.IsTrue(testMapData.IsValid, "Map data should be valid");
            Assert.AreEqual(64, testMapData.Width, "Map width should be set");
            Assert.AreEqual(64, testMapData.Height, "Map height should be set");
            Assert.AreEqual(3, testMapData.ProvinceBounds.Length, "Should have 3 province bounds");
            Assert.AreEqual(3, testMapData.ProvinceColors.Count, "Should have 3 province colors");
        }

        [Test]
        public void ProvinceBounds_ShouldHaveCorrectData()
        {
            for (int i = 0; i < testMapData.ProvinceBounds.Length; i++)
            {
                var bounds = testMapData.ProvinceBounds[i];

                Assert.Greater(bounds.ProvinceID, 0, "Province ID should be positive");
                Assert.GreaterOrEqual(bounds.MinX, 0, "MinX should be non-negative");
                Assert.GreaterOrEqual(bounds.MinY, 0, "MinY should be non-negative");
                Assert.Greater(bounds.MaxX, bounds.MinX, "MaxX should be greater than MinX");
                Assert.Greater(bounds.MaxY, bounds.MinY, "MaxY should be greater than MinY");
                Assert.Greater(bounds.PixelCount, 0, "Pixel count should be positive");

                // Center should be within bounds
                Assert.GreaterOrEqual(bounds.CenterX, bounds.MinX, "CenterX should be within bounds");
                Assert.LessOrEqual(bounds.CenterX, bounds.MaxX, "CenterX should be within bounds");
                Assert.GreaterOrEqual(bounds.CenterY, bounds.MinY, "CenterY should be within bounds");
                Assert.LessOrEqual(bounds.CenterY, bounds.MaxY, "CenterY should be within bounds");

                // Test bounds properties
                Assert.Greater(bounds.Width, 0, "Width should be positive");
                Assert.Greater(bounds.Height, 0, "Height should be positive");
                Assert.AreEqual(bounds.PixelCount, bounds.Width * bounds.Height, "Pixel count should match calculated area");
            }
        }

        [Test]
        public void ProvinceColors_ShouldMatchProvinceIDs()
        {
            foreach (var bounds in testMapData.ProvinceBounds)
            {
                bool hasColor = testMapData.ProvinceColors.TryGetValue(bounds.ProvinceID, out Color32 color);
                Assert.IsTrue(hasColor, $"Province {bounds.ProvinceID} should have a color");
                Assert.AreNotEqual(Color.clear, color, "Province color should not be transparent");
                Assert.AreEqual(255, color.a, "Province color should be fully opaque");
            }
        }

        [Test]
        public void SimulationState_ShouldCorrespondToMapData()
        {
            // Verify simulation has provinces that match map data
            var allProvinces = testSimulation.GetAllProvinces();
            Assert.AreEqual(3, allProvinces.Length, "Simulation should have 3 provinces");

            foreach (var bounds in testMapData.ProvinceBounds)
            {
                Assert.IsTrue(testSimulation.HasProvince(bounds.ProvinceID),
                    $"Simulation should contain province {bounds.ProvinceID}");

                var state = testSimulation.GetProvinceState(bounds.ProvinceID);
                Assert.AreNotEqual((byte)TerrainType.Ocean, state.terrain,
                    "Simulation provinces should not have ocean terrain");
            }
        }

        [Test]
        public void SimulationStateChanges_ShouldBeTrackable()
        {
            var initialVersion = testSimulation.StateVersion;

            // Make changes to simulation
            testSimulation.SetProvinceOwner(1, 100);
            testSimulation.SetProvinceOwner(2, 200);

            Assert.Greater(testSimulation.StateVersion, initialVersion,
                "State version should increase after changes");
            Assert.IsTrue(testSimulation.IsDirty, "Simulation should be dirty after changes");

            var dirtyIndices = testSimulation.GetDirtyIndices();
            Assert.Greater(dirtyIndices.Count, 0, "Should have dirty indices after changes");

            // Clear dirty flags
            testSimulation.ClearDirtyFlags();
            Assert.IsFalse(testSimulation.IsDirty, "Should not be dirty after clearing flags");
            Assert.AreEqual(0, testSimulation.GetDirtyIndices().Count,
                "Should have no dirty indices after clearing");
        }

        [Test]
        public void ProvinceOwnershipUpdates_ShouldAffectCorrectProvinces()
        {
            // Test ownership changes
            ushort[] ownerIDs = { 100, 200, 300 };
            ushort[] provinceIDs = { 1, 2, 3 };

            for (int i = 0; i < provinceIDs.Length; i++)
            {
                testSimulation.SetProvinceOwner(provinceIDs[i], ownerIDs[i]);

                var state = testSimulation.GetProvinceState(provinceIDs[i]);
                Assert.AreEqual(ownerIDs[i], state.ownerID, $"Province {provinceIDs[i]} should have correct owner");
                Assert.AreEqual(ownerIDs[i], state.controllerID, "Controller should match owner by default");
            }
        }

        [Test]
        public void TextureDataToSimulationMapping_ShouldBeEfficient()
        {
            // Test that we can efficiently map between texture coordinates and simulation data
            foreach (var bounds in testMapData.ProvinceBounds)
            {
                // Test center pixel mapping
                var centerCoord = new Unity.Mathematics.int2(bounds.CenterX, bounds.CenterY);

                // Should be able to find province by coordinate
                bool foundProvince = false;
                foreach (var testBounds in testMapData.ProvinceBounds)
                {
                    if (centerCoord.x >= testBounds.MinX && centerCoord.x <= testBounds.MaxX &&
                        centerCoord.y >= testBounds.MinY && centerCoord.y <= testBounds.MaxY)
                    {
                        foundProvince = true;
                        Assert.AreEqual(bounds.ProvinceID, testBounds.ProvinceID,
                            "Center coordinate should map to correct province");
                        break;
                    }
                }

                Assert.IsTrue(foundProvince, $"Should find province for center coordinate {centerCoord}");
            }
        }

        [Test]
        public void MemoryUsage_ShouldBeWithinTargets()
        {
            var (totalBytes, hotBytes, coldBytes) = testSimulation.GetMemoryUsage();

            // Hot data should be exactly 8 bytes per province
            int expectedHotBytes = testSimulation.ProvinceCount * 8;
            Assert.AreEqual(expectedHotBytes, hotBytes, "Hot data should be exactly 8 bytes per province");

            // Total should be reasonable for test data
            Assert.Less(totalBytes, 10240, "Total memory should be under 10KB for test simulation");

            // Map data memory
            long mapDataMemory = testMapData.ProvinceBounds.Length * System.Runtime.InteropServices.Marshal.SizeOf<SimulationMapLoader.ProvinceBounds>();
            mapDataMemory += testMapData.ProvinceColors.Count * (sizeof(ushort) + 4); // ID + Color32

            Assert.Less(mapDataMemory, 1024, "Map data memory should be under 1KB for test data");
        }

        [Test]
        public void SimulationDisposal_ShouldCleanUpResources()
        {
            var disposableSimulation = new ProvinceSimulation(5);
            disposableSimulation.AddProvince(100, TerrainType.Grassland);

            Assert.IsTrue(disposableSimulation.IsInitialized, "Should be initialized");
            Assert.AreEqual(1, disposableSimulation.ProvinceCount, "Should have one province");

            disposableSimulation.Dispose();

            Assert.IsFalse(disposableSimulation.IsInitialized, "Should not be initialized after disposal");
        }

        [Test]
        public void MapDataDisposal_ShouldCleanUpNativeCollections()
        {
            var bounds = new NativeArray<SimulationMapLoader.ProvinceBounds>(2, Allocator.Persistent);
            var colors = new NativeHashMap<ushort, Color32>(2, Allocator.Persistent);

            bounds[0] = new SimulationMapLoader.ProvinceBounds { ProvinceID = 1 };
            bounds[1] = new SimulationMapLoader.ProvinceBounds { ProvinceID = 2 };
            colors.TryAdd(1, Color.red);
            colors.TryAdd(2, Color.blue);

            var mapData = new SimulationMapLoader.SimulationMapData
            {
                Width = 32,
                Height = 32,
                ProvinceBounds = bounds,
                ProvinceColors = colors,
                IsValid = true
            };

            Assert.IsTrue(mapData.ProvinceBounds.IsCreated, "Bounds should be created");
            Assert.IsTrue(mapData.ProvinceColors.IsCreated, "Colors should be created");

            mapData.Dispose();

            Assert.IsFalse(mapData.ProvinceBounds.IsCreated, "Bounds should be disposed");
            Assert.IsFalse(mapData.ProvinceColors.IsCreated, "Colors should be disposed");
        }

        [Test]
        public void LargeProvinceCount_ShouldScaleCorrectly()
        {
            // Test with more provinces to simulate scaling
            var largeSimulation = new ProvinceSimulation(100);

            try
            {
                // Add many provinces
                for (ushort i = 1; i <= 50; i++)
                {
                    largeSimulation.AddProvince(i, TerrainType.Grassland);
                }

                Assert.AreEqual(50, largeSimulation.ProvinceCount, "Should have 50 provinces");

                // Test batch operations
                for (ushort i = 1; i <= 50; i++)
                {
                    largeSimulation.SetProvinceOwner(i, (ushort)(i % 10 + 1)); // Assign to countries 1-10
                }

                Assert.IsTrue(largeSimulation.IsDirty, "Should be dirty after batch operations");

                var dirtyIndices = largeSimulation.GetDirtyIndices();
                Assert.AreEqual(50, dirtyIndices.Count, "All provinces should be dirty");

                var (totalBytes, hotBytes, coldBytes) = largeSimulation.GetMemoryUsage();
                Assert.AreEqual(50 * 8, hotBytes, "Hot memory should scale correctly");
            }
            finally
            {
                largeSimulation.Dispose();
            }
        }
    }
}