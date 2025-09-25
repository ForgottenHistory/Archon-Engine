using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Map.Province;
using Map.Loading;

namespace Map.Tests
{
    /// <summary>
    /// Simplified tests for ProvinceMetadataGenerator functionality
    /// </summary>
    [TestFixture]
    public class ProvinceMetadataGeneratorTests
    {
        [Test]
        public void GenerateMetadata_WithValidData_ShouldSucceed()
        {
            // Create a simple test load result with proper initialization
            var pixels = new NativeArray<ProvinceMapLoader.ProvincePixel>(6, Allocator.Temp);

            // Add ocean pixels (ID 0)
            pixels[0] = new ProvinceMapLoader.ProvincePixel { Position = new int2(0, 0), ProvinceID = 0, Color = Color.black };
            pixels[1] = new ProvinceMapLoader.ProvincePixel { Position = new int2(1, 0), ProvinceID = 0, Color = Color.black };

            // Add province 1 pixels
            pixels[2] = new ProvinceMapLoader.ProvincePixel { Position = new int2(2, 0), ProvinceID = 1, Color = Color.red };
            pixels[3] = new ProvinceMapLoader.ProvincePixel { Position = new int2(0, 1), ProvinceID = 1, Color = Color.red };

            // Add province 2 pixels
            pixels[4] = new ProvinceMapLoader.ProvincePixel { Position = new int2(1, 1), ProvinceID = 2, Color = Color.blue };
            pixels[5] = new ProvinceMapLoader.ProvincePixel { Position = new int2(2, 1), ProvinceID = 2, Color = Color.blue };

            var colorToID = new NativeHashMap<Color32, ushort>(3, Allocator.Temp);
            colorToID.TryAdd(Color.black, 0); // Ocean
            colorToID.TryAdd(Color.red, 1);   // Province 1
            colorToID.TryAdd(Color.blue, 2);  // Province 2

            var loadResult = new ProvinceMapLoader.LoadResult
            {
                Success = true,
                ProvinceCount = 2, // Only counting non-ocean provinces
                Width = 3,
                Height = 2,
                ColorToID = colorToID,
                ProvincePixels = pixels
            };

            // Create properly initialized neighbor result
            var provinceNeighbors = new NativeHashMap<ushort, ProvinceNeighborDetector.ProvinceNeighborData>(2, Allocator.Temp);
            var provinceBounds = new NativeHashMap<ushort, ProvinceNeighborDetector.ProvinceBounds>(2, Allocator.Temp);
            var coastalProvinces = new NativeHashSet<ushort>(2, Allocator.Temp);

            // Initialize with some basic data
            provinceNeighbors.TryAdd(1, new ProvinceNeighborDetector.ProvinceNeighborData());
            provinceNeighbors.TryAdd(2, new ProvinceNeighborDetector.ProvinceNeighborData());

            var neighborResult = new ProvinceNeighborDetector.NeighborResult
            {
                Success = true,
                ProvinceNeighbors = provinceNeighbors,
                ProvinceBounds = provinceBounds,
                CoastalProvinces = coastalProvinces,
                TotalNeighborPairs = 1
            };

            try
            {
                var result = ProvinceMetadataGenerator.GenerateMetadata(loadResult, neighborResult);

                try
                {
                    Debug.Log($"Result.Success: {result.Success}");
                    Debug.Log($"Result.ProvinceMetadata.IsCreated: {result.ProvinceMetadata.IsCreated}");
                    Debug.Log($"Result.ErrorMessage: '{result.ErrorMessage}'");

                    if (!result.Success)
                    {
                        Debug.LogError($"Metadata generation failed: {result.ErrorMessage}");
                    }

                    Assert.IsTrue(result.Success, $"Metadata generation should succeed. Error: {result.ErrorMessage}");

                    if (result.ProvinceMetadata.IsCreated)
                    {
                        Debug.Log($"Generated metadata for {result.ProvinceMetadata.Count} provinces");
                    }
                    else
                    {
                        Debug.LogError("ProvinceMetadata NativeHashMap was not created!");
                    }

                    Assert.IsTrue(result.ProvinceMetadata.IsCreated, "Province metadata should be created");
                }
                finally
                {
                    result.Dispose();
                }
            }
            finally
            {
                loadResult.Dispose();
                neighborResult.Dispose();
            }
        }

        [Test]
        public void GenerateMetadata_WithInvalidInput_ShouldFail()
        {
            var invalidLoadResult = new ProvinceMapLoader.LoadResult
            {
                Success = false,
                ErrorMessage = "Test invalid input"
            };

            var invalidNeighborResult = new ProvinceNeighborDetector.NeighborResult
            {
                Success = false,
                ErrorMessage = "Test invalid neighbor data"
            };

            var result = ProvinceMetadataGenerator.GenerateMetadata(invalidLoadResult, invalidNeighborResult);

            Assert.IsFalse(result.Success, "Should fail with invalid input");
            Assert.IsNotEmpty(result.ErrorMessage, "Should have error message");

            result.Dispose();
        }

        [Test]
        public void GenerateMetadata_MemoryManagement_ShouldDisposeCorrectly()
        {
            // Create minimal valid data
            var pixels = new NativeArray<ProvinceMapLoader.ProvincePixel>(1, Allocator.Temp);
            pixels[0] = new ProvinceMapLoader.ProvincePixel { Position = new int2(0, 0), ProvinceID = 1, Color = Color.red };

            var colorToID = new NativeHashMap<Color32, ushort>(1, Allocator.Temp);
            colorToID.TryAdd(Color.red, 1);

            var loadResult = new ProvinceMapLoader.LoadResult
            {
                Success = true,
                ProvinceCount = 1,
                Width = 1,
                Height = 1,
                ColorToID = colorToID,
                ProvincePixels = pixels
            };

            var neighborResult = new ProvinceNeighborDetector.NeighborResult
            {
                Success = true,
                ProvinceNeighbors = new NativeHashMap<ushort, ProvinceNeighborDetector.ProvinceNeighborData>(1, Allocator.Temp),
                ProvinceBounds = new NativeHashMap<ushort, ProvinceNeighborDetector.ProvinceBounds>(1, Allocator.Temp),
                CoastalProvinces = new NativeHashSet<ushort>(1, Allocator.Temp),
                TotalNeighborPairs = 0
            };

            try
            {
                var result = ProvinceMetadataGenerator.GenerateMetadata(loadResult, neighborResult);

                // Test disposal doesn't throw
                Assert.DoesNotThrow(() => result.Dispose(), "Dispose should not throw");

                Debug.Log("Memory management test completed successfully");
            }
            finally
            {
                loadResult.Dispose();
                neighborResult.Dispose();
            }
        }
    }
}