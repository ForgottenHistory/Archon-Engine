using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Map.Province;
using Map.Loading;

namespace Map.Tests
{
    /// <summary>
    /// Tests for ProvinceNeighborDetector functionality
    /// </summary>
    [TestFixture]
    public class ProvinceNeighborDetectorTests
    {
        /// <summary>
        /// Helper method to create mock ProvinceMapLoader.LoadResult for testing
        /// </summary>
        private ProvinceMapLoader.LoadResult CreateMockLoadResult(int width, int height, ushort[] provinceIDs)
        {
            var pixels = new NativeArray<ProvinceMapLoader.ProvincePixel>(width * height, Allocator.TempJob);
            var colorToID = new NativeHashMap<Color32, ushort>(10, Allocator.TempJob);

            for (int i = 0; i < provinceIDs.Length; i++)
            {
                int x = i % width;
                int y = i / width;
                ushort id = provinceIDs[i];
                Color32 color = new Color32((byte)(id * 50), (byte)(id * 100), (byte)(id * 150), 255);

                pixels[i] = new ProvinceMapLoader.ProvincePixel
                {
                    Position = new int2(x, y),
                    ProvinceID = id,
                    Color = color
                };

                if (!colorToID.ContainsKey(color))
                {
                    colorToID.TryAdd(color, id);
                }
            }

            return new ProvinceMapLoader.LoadResult
            {
                Success = true,
                ProvinceCount = colorToID.Count,
                Width = width,
                Height = height,
                ColorToID = colorToID,
                ProvincePixels = pixels
            };
        }

        [Test]
        public void DetectNeighbors_SimpleGrid_ShouldFindCorrectNeighbors()
        {
            // Create a simple 3x3 grid with 3 provinces
            // Layout:
            // 1 1 2
            // 1 1 2
            // 3 3 3
            var mockLoadResult = CreateMockLoadResult(3, 3, new ushort[]
            {
                1, 1, 2, // Row 0
                1, 1, 2, // Row 1
                3, 3, 3  // Row 2
            });

            try
            {
                var result = ProvinceNeighborDetector.DetectNeighbors(mockLoadResult);

                try
                {
                    Assert.IsTrue(result.Success, "Neighbor detection should succeed");
                    Assert.Greater(result.TotalNeighborPairs, 0, "Should find neighbors");
                    Assert.IsTrue(result.ProvinceNeighbors.IsCreated, "Province neighbors should be created");

                    // Check that provinces have neighbor data
                    bool hasProvince1 = result.ProvinceNeighbors.ContainsKey(1);
                    bool hasProvince2 = result.ProvinceNeighbors.ContainsKey(2);
                    bool hasProvince3 = result.ProvinceNeighbors.ContainsKey(3);

                    Assert.IsTrue(hasProvince1, "Should have neighbor data for province 1");
                    Assert.IsTrue(hasProvince2, "Should have neighbor data for province 2");
                    Assert.IsTrue(hasProvince3, "Should have neighbor data for province 3");

                    Debug.Log($"Found {result.TotalNeighborPairs} total neighbor pairs");
                }
                finally
                {
                    result.Dispose();
                }
            }
            finally
            {
                mockLoadResult.Dispose();
            }
        }

        [Test]
        public void DetectNeighbors_SingleProvince_ShouldReturnEmpty()
        {
            // Create a 2x2 grid with all same province
            var mockLoadResult = CreateMockLoadResult(2, 2, new ushort[] { 1, 1, 1, 1 });

            try
            {
                var result = ProvinceNeighborDetector.DetectNeighbors(mockLoadResult);

                try
                {
                    Assert.IsTrue(result.Success, "Detection should succeed");
                    Assert.AreEqual(0, result.TotalNeighborPairs, "Single province should have no neighbor pairs");
                }
                finally
                {
                    result.Dispose();
                }
            }
            finally
            {
                mockLoadResult.Dispose();
            }
        }

        [Test]
        public void DetectNeighbors_WithOcean_ShouldDetectCoastal()
        {
            // Create a 3x3 grid with ocean (ID 0) and provinces
            // Layout:
            // 0 0 0  (ocean)
            // 0 1 2
            // 0 1 2
            var mockLoadResult = CreateMockLoadResult(3, 3, new ushort[]
            {
                0, 0, 0, // Row 0 (ocean)
                0, 1, 2, // Row 1
                0, 1, 2  // Row 2
            });

            try
            {
                var result = ProvinceNeighborDetector.DetectNeighbors(mockLoadResult);

                try
                {
                    Assert.IsTrue(result.Success, "Detection should succeed");

                    // Both province 1 and 2 should be coastal (adjacent to ocean)
                    if (result.CoastalProvinces.IsCreated)
                    {
                        Assert.Greater(result.CoastalProvinces.Count, 0, "Should find coastal provinces");
                        Assert.IsTrue(result.CoastalProvinces.Contains(1), "Province 1 should be coastal");
                        Assert.IsTrue(result.CoastalProvinces.Contains(2), "Province 2 should be coastal");

                        Debug.Log($"Found {result.CoastalProvinces.Count} coastal provinces");
                    }
                }
                finally
                {
                    result.Dispose();
                }
            }
            finally
            {
                mockLoadResult.Dispose();
            }
        }

        [Test]
        public void DetectNeighbors_InvalidInput_ShouldFail()
        {
            var invalidLoadResult = new ProvinceMapLoader.LoadResult
            {
                Success = false,
                ErrorMessage = "Test invalid input"
            };

            var result = ProvinceNeighborDetector.DetectNeighbors(invalidLoadResult);

            Assert.IsFalse(result.Success, "Should fail with invalid input");
            Assert.IsNotEmpty(result.ErrorMessage, "Should have error message");

            result.Dispose();
        }

        [Test]
        public void DetectNeighbors_LargeMap_ShouldPerformReasonably()
        {
            // Create a larger test map (50x50) with checkerboard pattern
            const int size = 50;
            var provinceIDs = new ushort[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = y * size + x;
                    // Checkerboard pattern: alternates between province 1 and 2
                    provinceIDs[index] = (ushort)(((x + y) % 2) + 1);
                }
            }

            var mockLoadResult = CreateMockLoadResult(size, size, provinceIDs);

            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var result = ProvinceNeighborDetector.DetectNeighbors(mockLoadResult);
                stopwatch.Stop();

                try
                {
                    Assert.IsTrue(result.Success, "Large map detection should succeed");
                    Assert.Greater(result.TotalNeighborPairs, 0, "Should find many neighbors");

                    Debug.Log($"Neighbor detection on {size}x{size} map took {stopwatch.ElapsedMilliseconds}ms");
                    Debug.Log($"Found {result.TotalNeighborPairs} total neighbor pairs");

                    // Performance expectation (this is more of a benchmark)
                    Assert.Less(stopwatch.ElapsedMilliseconds, 2000, "Should complete within 2 seconds");
                }
                finally
                {
                    result.Dispose();
                }
            }
            finally
            {
                mockLoadResult.Dispose();
            }
        }
    }
}