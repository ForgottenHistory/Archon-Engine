using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using System.IO;
using Map.Simulation;
using ParadoxParser.Bitmap;

namespace Map.Tests.Simulation
{
    /// <summary>
    /// Tests for SimulationMapLoader - validates bitmap to simulation conversion
    /// Tests Task 1.2: Bitmap to Simulation Conversion functionality
    /// </summary>
    [TestFixture]
    public class SimulationMapLoaderTests
    {
        private string testMapPath;
        private string testCsvPath;

        [SetUp]
        public void Setup()
        {
            // Use temp directory for test files
            string tempDir = Path.GetTempPath();
            testMapPath = Path.Combine(tempDir, "test_provinces.bmp");
            testCsvPath = Path.Combine(tempDir, "test_definition.csv");
        }

        [TearDown]
        public void Teardown()
        {
            // Clean up test files
            if (File.Exists(testMapPath)) File.Delete(testMapPath);
            if (File.Exists(testCsvPath)) File.Delete(testCsvPath);
        }

        [Test]
        public void LoadSimulationFromBitmap_MissingFile_ShouldFail()
        {
            var result = SimulationMapLoader.LoadSimulationFromBitmap("nonexistent.bmp");

            Assert.IsFalse(result.Success, "Should fail for missing file");
            Assert.IsNotNull(result.ErrorMessage);
            Assert.That(result.ErrorMessage.ToLower().Contains("not found"));
        }

        [Test]
        public void LoadSimulationFromBitmap_ValidBitmap_ShouldCreateSimulation()
        {
            // Create a small test bitmap
            CreateTestBitmap(testMapPath, 4, 4, new Color32[]
            {
                Color.black, Color.red, Color.green, Color.blue, // Row 0
                Color.red, Color.red, Color.green, Color.blue,   // Row 1
                Color.red, Color.red, Color.yellow, Color.blue, // Row 2
                Color.black, Color.red, Color.yellow, Color.yellow // Row 3
            });

            var result = SimulationMapLoader.LoadSimulationFromBitmap(testMapPath);

            try
            {
                Assert.IsTrue(result.Success, $"Load should succeed. Error: {result.ErrorMessage}");
                Assert.IsNotNull(result.Simulation, "Simulation should be created");
                Assert.IsTrue(result.Simulation.IsInitialized, "Simulation should be initialized");
                Assert.Greater(result.Simulation.ProvinceCount, 0, "Should have provinces");

                // Should have at most 5 unique colors (black=ocean, red, green, blue, yellow)
                Assert.LessOrEqual(result.Simulation.ProvinceCount, 5);

                // Validate map data
                Assert.IsTrue(result.MapData.IsValid, "Map data should be valid");
                Assert.AreEqual(4, result.MapData.Width, "Map width should match");
                Assert.AreEqual(4, result.MapData.Height, "Map height should match");
                Assert.Greater(result.MapData.ProvinceBounds.Length, 0, "Should have province bounds");
            }
            finally
            {
                result.Dispose();
            }
        }

        [Test]
        public void LoadSimulationFromBitmap_WithDefinitionCSV_ShouldUseProvinceIDs()
        {
            // Create test bitmap
            CreateTestBitmap(testMapPath, 2, 2, new Color32[]
            {
                new Color32(100, 150, 200, 255), new Color32(50, 100, 150, 255),
                new Color32(200, 100, 50, 255), Color.black
            });

            // Create matching definition CSV
            CreateTestDefinitionCSV(testCsvPath, new[]
            {
                ("province", "red", "green", "blue", "name", "x"),
                ("1", "100", "150", "200", "Province1", ""),
                ("2", "50", "100", "150", "Province2", ""),
                ("3", "200", "100", "50", "Province3", ""),
                ("0", "0", "0", "0", "Ocean", "") // Ocean
            });

            var result = SimulationMapLoader.LoadSimulationFromBitmap(testMapPath, testCsvPath);

            try
            {
                Assert.IsTrue(result.Success, $"Load should succeed. Error: {result.ErrorMessage}");
                Assert.AreEqual(3, result.Simulation.ProvinceCount, "Should have 3 provinces (excluding ocean)");

                // Verify specific provinces exist (ocean ID 0 is reserved but not in simulation)
                Assert.IsFalse(result.Simulation.HasProvince(0), "Ocean should not be in simulation layer");
                Assert.IsTrue(result.Simulation.HasProvince(1), "Should have province 1");
                Assert.IsTrue(result.Simulation.HasProvince(2), "Should have province 2");
                Assert.IsTrue(result.Simulation.HasProvince(3), "Should have province 3");
            }
            finally
            {
                result.Dispose();
            }
        }

        [Test]
        public void LoadSimulationFromBitmap_TooManyProvinces_ShouldFail()
        {
            // Create test bitmap with many colors
            var colors = new Color32[100 * 100];
            for (int i = 0; i < colors.Length; i++)
            {
                // Create unique color for each pixel
                byte r = (byte)(i % 256);
                byte g = (byte)((i / 256) % 256);
                byte b = (byte)((i / (256 * 256)) % 256);
                colors[i] = new Color32(r, g, b, 255);
            }

            CreateTestBitmap(testMapPath, 100, 100, colors);

            // Try to load with very small max provinces limit
            var result = SimulationMapLoader.LoadSimulationFromBitmap(testMapPath, null, maxProvinces: 100);

            Assert.IsFalse(result.Success, "Should fail when too many provinces");
            Assert.IsNotNull(result.ErrorMessage);
            Assert.That(result.ErrorMessage.ToLower().Contains("too many"));

            result.Dispose();
        }

        [Test]
        public void LoadSimulationFromBitmap_MemoryValidation_ShouldEnforceTargets()
        {
            // Create bitmap that would exceed memory targets
            var colors = new Color32[1000];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color32((byte)(i % 255 + 1), (byte)(i / 255 % 255 + 1), 128, 255);
            }

            CreateTestBitmap(testMapPath, 1000, 1, colors);

            // Should pass with reasonable limit
            var result1 = SimulationMapLoader.LoadSimulationFromBitmap(testMapPath, null, maxProvinces: 10000);
            Assert.IsTrue(result1.Success, "Should pass with adequate limit");
            result1.Dispose();

            // Should fail with tiny limit that would exceed memory target
            var result2 = SimulationMapLoader.LoadSimulationFromBitmap(testMapPath, null, maxProvinces: 50);
            Assert.IsFalse(result2.Success, "Should fail when exceeding memory target");
            result2.Dispose();
        }

        [Test]
        public void SimulationMapData_ProvinceBounds_ShouldCalculateCorrectly()
        {
            // Create bitmap with known layout
            CreateTestBitmap(testMapPath, 4, 4, new Color32[]
            {
                Color.red, Color.red, Color.green, Color.green,     // Row 0
                Color.red, Color.red, Color.green, Color.green,     // Row 1
                Color.blue, Color.blue, Color.yellow, Color.yellow, // Row 2
                Color.blue, Color.blue, Color.yellow, Color.yellow  // Row 3
            });

            var result = SimulationMapLoader.LoadSimulationFromBitmap(testMapPath);

            try
            {
                Assert.IsTrue(result.Success);

                // Find bounds for each color region
                bool foundRedBounds = false, foundGreenBounds = false;

                for (int i = 0; i < result.MapData.ProvinceBounds.Length; i++)
                {
                    var bounds = result.MapData.ProvinceBounds[i];

                    if (result.MapData.ProvinceColors.TryGetValue(bounds.ProvinceID, out Color32 color))
                    {
                        if (color.r == 255 && color.g == 0 && color.b == 0) // Red
                        {
                            Assert.AreEqual(0, bounds.MinX, "Red region should start at X=0");
                            Assert.AreEqual(0, bounds.MinY, "Red region should start at Y=0");
                            Assert.AreEqual(1, bounds.MaxX, "Red region should end at X=1");
                            Assert.AreEqual(1, bounds.MaxY, "Red region should end at Y=1");
                            Assert.AreEqual(4, bounds.PixelCount, "Red region should have 4 pixels");
                            foundRedBounds = true;
                        }
                        else if (color.r == 0 && color.g == 255 && color.b == 0) // Green
                        {
                            Assert.AreEqual(2, bounds.MinX, "Green region should start at X=2");
                            Assert.AreEqual(0, bounds.MinY, "Green region should start at Y=0");
                            Assert.AreEqual(3, bounds.MaxX, "Green region should end at X=3");
                            Assert.AreEqual(1, bounds.MaxY, "Green region should end at Y=1");
                            Assert.AreEqual(4, bounds.PixelCount, "Green region should have 4 pixels");
                            foundGreenBounds = true;
                        }
                    }
                }

                Assert.IsTrue(foundRedBounds, "Should find red bounds");
                Assert.IsTrue(foundGreenBounds, "Should find green bounds");
            }
            finally
            {
                result.Dispose();
            }
        }

        [Test]
        public void SimulationLoadResult_Dispose_ShouldCleanUpResources()
        {
            CreateTestBitmap(testMapPath, 2, 2, new Color32[]
            {
                Color.red, Color.green,
                Color.blue, Color.yellow
            });

            var result = SimulationMapLoader.LoadSimulationFromBitmap(testMapPath);
            Assert.IsTrue(result.Success);

            // Verify resources are allocated
            Assert.IsNotNull(result.Simulation);
            Assert.IsTrue(result.MapData.ProvinceBounds.IsCreated);
            Assert.IsTrue(result.MapData.ProvinceColors.IsCreated);

            // Dispose should clean everything up
            result.Dispose();

            // Verify cleanup (simulation should be disposed, arrays should be disposed)
            Assert.IsFalse(result.Simulation.IsInitialized);
            Assert.IsFalse(result.MapData.ProvinceBounds.IsCreated);
            Assert.IsFalse(result.MapData.ProvinceColors.IsCreated);
        }

        [Test]
        public void TerrainDetermination_ShouldAssignBasedOnID()
        {
            CreateTestBitmap(testMapPath, 2, 2, new Color32[]
            {
                Color.black, Color.red, // Ocean, Province 1
                Color.green, Color.blue // Province 2, Province 3
            });

            var result = SimulationMapLoader.LoadSimulationFromBitmap(testMapPath);

            try
            {
                Assert.IsTrue(result.Success);

                // Ocean should not be in simulation layer (handled by GPU textures)
                Assert.IsFalse(result.Simulation.HasProvince(0), "Ocean should not be in simulation");

                // All provinces in simulation should have valid terrain (non-ocean)
                var allProvinces = result.Simulation.GetAllProvinces();
                Assert.Greater(allProvinces.Length, 0, "Should have at least one province");

                foreach (var state in allProvinces)
                {
                    // All provinces in simulation should have valid non-ocean terrain
                    Assert.That(state.terrain >= 1 && state.terrain <= 6,
                        $"Province should have valid terrain, got {state.terrain}");
                    Assert.AreNotEqual((byte)TerrainType.Ocean, state.terrain,
                        "Provinces in simulation should not have ocean terrain");
                }
            }
            finally
            {
                result.Dispose();
            }
        }

        #region Helper Methods

        /// <summary>
        /// Create a test BMP file with specified dimensions and pixel colors
        /// </summary>
        private void CreateTestBitmap(string filePath, int width, int height, Color32[] pixels)
        {
            if (pixels.Length != width * height)
                throw new System.ArgumentException($"Pixel array length ({pixels.Length}) must equal width * height ({width * height})");

            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            // Create simple RGB bitmap format directly

            // Create a simple 24-bit BMP manually for testing
            using (var stream = new FileStream(filePath, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                int imageSize = width * height * 3; // 3 bytes per pixel (RGB)
                int fileSize = 54 + imageSize; // 54 = BMP header size

                // BMP File Header (14 bytes)
                writer.Write((ushort)0x4D42); // "BM"
                writer.Write((uint)fileSize);
                writer.Write((uint)0); // Reserved
                writer.Write((uint)54); // Offset to pixel data

                // BMP Info Header (40 bytes)
                writer.Write((uint)40); // Header size
                writer.Write((int)width);
                writer.Write((int)height);
                writer.Write((ushort)1); // Planes
                writer.Write((ushort)24); // Bits per pixel
                writer.Write((uint)0); // Compression
                writer.Write((uint)imageSize);
                writer.Write((int)2835); // X pixels per meter
                writer.Write((int)2835); // Y pixels per meter
                writer.Write((uint)0); // Colors used
                writer.Write((uint)0); // Colors important

                // Pixel data (bottom-up, BGR format)
                for (int y = height - 1; y >= 0; y--) // BMP is bottom-up
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color32 pixel = pixels[y * width + x];
                        writer.Write(pixel.b); // Blue
                        writer.Write(pixel.g); // Green
                        writer.Write(pixel.r); // Red
                    }

                    // BMP rows must be padded to 4-byte boundary
                    int padding = (4 - (width * 3) % 4) % 4;
                    for (int p = 0; p < padding; p++)
                        writer.Write((byte)0);
                }
            }

            Object.DestroyImmediate(texture);
        }

        /// <summary>
        /// Create a test definition CSV file
        /// </summary>
        private void CreateTestDefinitionCSV(string filePath, (string, string, string, string, string, string)[] rows)
        {
            using (var writer = new StreamWriter(filePath))
            {
                foreach (var row in rows)
                {
                    writer.WriteLine($"{row.Item1};{row.Item2};{row.Item3};{row.Item4};{row.Item5};{row.Item6}");
                }
            }
        }


        #endregion
    }
}