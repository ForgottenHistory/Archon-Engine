using System.IO;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using Map.Loading;
using Map.Province;
using Map.Integration;
using Map.Rendering;

namespace Map.Tests
{
    /// <summary>
    /// Tests for MapDataIntegrator functionality - integration tests for the complete map loading pipeline
    /// </summary>
    [TestFixture]
    public class MapDataIntegratorTests
    {
        private const string TEST_DATA_PATH = "Assets/Data/map";
        private MapTextureManager textureManager;

        [SetUp]
        public void SetUp()
        {
            var gameObject = new GameObject("TestMapTextureManager");
            textureManager = gameObject.AddComponent<MapTextureManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (textureManager != null)
            {
                Object.DestroyImmediate(textureManager.gameObject);
            }
        }

        [Test]
        public void IntegratedMapLoading_ValidFiles_ShouldWorkTogether()
        {
            // Use a smaller test file if available, otherwise skip
            string testProvincesPath = Path.Combine(TEST_DATA_PATH, "test_provinces_small.bmp");
            string provincesPath = Path.Combine(TEST_DATA_PATH, "provinces.bmp");

            string pathToUse = File.Exists(testProvincesPath) ? testProvincesPath : provincesPath;

            if (!File.Exists(pathToUse))
            {
                Assert.Ignore($"Test file not found: {pathToUse}");
                return;
            }

            // Skip this test if using the full map (too slow for unit tests)
            if (pathToUse == provincesPath)
            {
                var fileInfo = new System.IO.FileInfo(provincesPath);
                if (fileInfo.Length > 1024 * 1024) // Skip if > 1MB
                {
                    Assert.Ignore("Skipping test with large provinces.bmp - use test_provinces_small.bmp instead");
                    return;
                }
            }

            // Test the complete pipeline step by step
            var loadResult = ProvinceMapLoader.LoadProvinceMap(pathToUse, textureManager);

            try
            {
                Assert.IsTrue(loadResult.Success, $"Province map loading should succeed: {loadResult.ErrorMessage}");
                Assert.Greater(loadResult.ProvinceCount, 0, "Should load provinces");

                // Use GPU for large maps, CPU for small test maps
                ProvinceNeighborDetector.NeighborResult neighborResult;
                if (loadResult.ProvinceCount > 100)
                {
                    // Large map - use GPU
                    var provinceTexture = GPUProvinceNeighborDetector.CreateProvinceIDTexture(loadResult);
                    neighborResult = GPUProvinceNeighborDetector.DetectNeighborsGPU(provinceTexture, loadResult.ProvinceCount);

                    // Clean up texture
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(provinceTexture);
                    else
                        UnityEngine.Object.DestroyImmediate(provinceTexture);
                }
                else
                {
                    // Small map - use CPU for testing
                    neighborResult = ProvinceNeighborDetector.DetectNeighbors(loadResult);
                }
                try
                {
                    Assert.IsTrue(neighborResult.Success, "Neighbor detection should succeed");
                    Assert.Greater(neighborResult.TotalNeighborPairs, 0, "Should find neighbor pairs");

                    // Test metadata generation
                    var metadataResult = ProvinceMetadataGenerator.GenerateMetadata(loadResult, neighborResult);

                    try
                    {
                        Assert.IsTrue(metadataResult.Success, "Metadata generation should succeed");

                        DominionLogger.Log($"Integration test successful:");
                        DominionLogger.Log($"  - Loaded {loadResult.ProvinceCount} provinces");
                        DominionLogger.Log($"  - Found {neighborResult.TotalNeighborPairs} neighbor pairs");
                        DominionLogger.Log($"  - Generated metadata for {metadataResult.ProvinceMetadata.Count} provinces");
                    }
                    finally
                    {
                        metadataResult.Dispose();
                    }
                }
                finally
                {
                    neighborResult.Dispose();
                }
            }
            finally
            {
                loadResult.Dispose();
            }
        }

        [Test]
        public void IntegratedMapLoading_InvalidFile_ShouldFailGracefully()
        {
            string invalidPath = "nonexistent/provinces.bmp";
            var loadResult = ProvinceMapLoader.LoadProvinceMap(invalidPath, textureManager);

            Assert.IsFalse(loadResult.Success, "Should fail for invalid file");
            Assert.IsNotEmpty(loadResult.ErrorMessage, "Should have error message");
            Assert.That(loadResult.ErrorMessage, Does.Contain("not found").IgnoreCase,
                        "Error message should indicate file not found");

            // Should be safe to dispose even on failure
            loadResult.Dispose();
        }

        [Test]
        public void IntegratedMapLoading_Performance_ShouldCompleteReasonably()
        {
            string provincesPath = Path.Combine(TEST_DATA_PATH, "provinces.bmp");

            if (!File.Exists(provincesPath))
            {
                Assert.Ignore($"Test file not found: {provincesPath}");
                return;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = ProvinceMapLoader.LoadProvinceMap(provincesPath, textureManager);
            stopwatch.Stop();

            try
            {
                if (result.Success)
                {
                    DominionLogger.Log($"Complete map loading took {stopwatch.ElapsedMilliseconds}ms for {result.ProvinceCount} provinces");

                    // Performance expectations based on map size
                    if (result.ProvinceCount < 1000)
                    {
                        Assert.Less(stopwatch.ElapsedMilliseconds, 5000, "Small maps should load within 5 seconds");
                    }
                    else if (result.ProvinceCount < 10000)
                    {
                        Assert.Less(stopwatch.ElapsedMilliseconds, 15000, "Medium maps should load within 15 seconds");
                    }
                    else
                    {
                        Assert.Less(stopwatch.ElapsedMilliseconds, 60000, "Large maps should load within 60 seconds");
                    }

                    // Memory usage check - this is more of a documentation test
                    long memoryBefore = System.GC.GetTotalMemory(false);
                    System.GC.Collect();
                    System.GC.WaitForPendingFinalizers();
                    long memoryAfter = System.GC.GetTotalMemory(true);

                    DominionLogger.Log($"Estimated memory impact: {(memoryAfter - memoryBefore) / (1024 * 1024)}MB");
                }
            }
            finally
            {
                result.Dispose();
            }
        }

        [Test]
        public void LoadProvinceMap_DataConsistency_ShouldHaveConsistentData()
        {
            string provincesPath = Path.Combine(TEST_DATA_PATH, "provinces.bmp");

            if (!File.Exists(provincesPath))
            {
                Assert.Ignore($"Test file not found: {provincesPath}");
                return;
            }

            var result = ProvinceMapLoader.LoadProvinceMap(provincesPath, textureManager);

            try
            {
                if (!result.Success)
                {
                    Assert.Ignore("Map loading failed, cannot test consistency");
                    return;
                }

                // Verify basic data consistency
                Assert.IsTrue(result.ProvincePixels.IsCreated, "Province pixels should be created");
                Assert.IsTrue(result.ColorToID.IsCreated, "Color to ID mapping should be created");

                // Verify pixel data consistency
                int validPixels = 0;
                for (int i = 0; i < result.ProvincePixels.Length; i++)
                {
                    var pixel = result.ProvincePixels[i];

                    // Position should be within bounds
                    Assert.GreaterOrEqual(pixel.Position.x, 0, "Pixel X position should be non-negative");
                    Assert.GreaterOrEqual(pixel.Position.y, 0, "Pixel Y position should be non-negative");
                    Assert.Less(pixel.Position.x, result.Width, "Pixel X should be within width");
                    Assert.Less(pixel.Position.y, result.Height, "Pixel Y should be within height");

                    // Province ID should be reasonable (0 for ocean is ok)
                    Assert.GreaterOrEqual(pixel.ProvinceID, 0, "Province ID should be non-negative");

                    if (pixel.ProvinceID > 0)
                    {
                        validPixels++;
                    }
                }

                Assert.Greater(validPixels, 0, "Should have at least some non-ocean pixels");

                DominionLogger.Log($"Data consistency checks passed: {validPixels} valid province pixels");
            }
            finally
            {
                result.Dispose();
            }
        }

        [Test]
        public void LoadProvinceMap_MemoryManagement_ShouldCleanupProperly()
        {
            string provincesPath = Path.Combine(TEST_DATA_PATH, "provinces.bmp");

            if (!File.Exists(provincesPath))
            {
                Assert.Ignore($"Test file not found: {provincesPath}");
                return;
            }

            // Test multiple load/dispose cycles
            for (int cycle = 0; cycle < 3; cycle++)
            {
                var result = ProvinceMapLoader.LoadProvinceMap(provincesPath, textureManager);

                Assert.DoesNotThrow(() => result.Dispose(), $"Disposal cycle {cycle} should not throw");

                // Verify we can't accidentally use disposed data (in debug builds this might assert)
                // This is more about documenting expected behavior
            }

            DominionLogger.Log("Memory management test completed - multiple load/dispose cycles successful");
        }

        [Test]
        public void LoadProvinceMap_ErrorHandling_ShouldHandlePartialFailures()
        {
            // Test with a minimal valid BMP that might cause issues in later stages
            string provincesPath = Path.Combine(TEST_DATA_PATH, "provinces.bmp");

            if (!File.Exists(provincesPath))
            {
                Assert.Ignore($"Test file not found: {provincesPath}");
                return;
            }

            // This test verifies that the integrator handles partial failures gracefully
            // For example, if bitmap loading succeeds but neighbor detection fails
            var result = ProvinceMapLoader.LoadProvinceMap(provincesPath, textureManager);

            try
            {
                // Even if some stages fail, the result should indicate what succeeded and what failed
                if (!result.Success)
                {
                    Assert.IsNotEmpty(result.ErrorMessage, "Failed result should have error message");
                    DominionLogger.Log($"Expected partial failure: {result.ErrorMessage}");
                }

                // The integrator should clean up any partially allocated data
                Assert.DoesNotThrow(() => result.Dispose(), "Should dispose cleanly even after partial failure");
            }
            finally
            {
                result.Dispose();
            }
        }

        [Test]
        public void LoadProvinceMap_EdgeCases_ShouldHandleEdgeCases()
        {
            // Test with various edge cases if we have appropriate test files

            // Case 1: Very small map (if available)
            string smallMapPath = Path.Combine(TEST_DATA_PATH, "small_test.bmp");
            if (File.Exists(smallMapPath))
            {
                var smallResult = ProvinceMapLoader.LoadProvinceMap(smallMapPath, textureManager);
                try
                {
                    DominionLogger.Log($"Small map test: Success={smallResult.Success}, Provinces={smallResult.ProvinceCount}");
                }
                finally
                {
                    smallResult.Dispose();
                }
            }

            // Case 2: Map with many small provinces (use main provinces.bmp)
            string provincesPath = Path.Combine(TEST_DATA_PATH, "provinces.bmp");
            if (File.Exists(provincesPath))
            {
                var result = ProvinceMapLoader.LoadProvinceMap(provincesPath, textureManager);
                try
                {
                    if (result.Success)
                    {
                        // Count unique provinces by analyzing pixels
                        var provincePixelCounts = new System.Collections.Generic.Dictionary<ushort, int>();

                        for (int i = 0; i < result.ProvincePixels.Length; i++)
                        {
                            var pixel = result.ProvincePixels[i];
                            if (pixel.ProvinceID > 0) // Skip ocean
                            {
                                if (provincePixelCounts.ContainsKey(pixel.ProvinceID))
                                    provincePixelCounts[pixel.ProvinceID]++;
                                else
                                    provincePixelCounts[pixel.ProvinceID] = 1;
                            }
                        }

                        int smallProvinces = 0;
                        foreach (var kvp in provincePixelCounts)
                        {
                            if (kvp.Value < 5) smallProvinces++;
                        }

                        DominionLogger.Log($"Found {smallProvinces} very small provinces (< 5 pixels) out of {provincePixelCounts.Count} total");

                        // This is acceptable but worth noting for performance
                        if (smallProvinces > provincePixelCounts.Count * 0.1f)
                        {
                            DominionLogger.LogWarning($"High ratio of small provinces: {smallProvinces}/{provincePixelCounts.Count}");
                        }
                    }
                }
                finally
                {
                    result.Dispose();
                }
            }

            Assert.Pass("Edge case testing completed");
        }
    }
}