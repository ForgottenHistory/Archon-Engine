using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using Map.Province;

namespace Map.Tests
{
    /// <summary>
    /// Tests for ProvinceIDEncoder functionality
    /// </summary>
    [TestFixture]
    public class ProvinceIDEncoderTests
    {
        [Test]
        public void PackUnpackProvinceID_ShouldRoundTrip()
        {
            // Test various province IDs
            ushort[] testIDs = { 0, 1, 255, 256, 1000, 10000, 65534 };

            foreach (ushort originalID in testIDs)
            {
                Color32 packed = ProvinceIDEncoder.PackProvinceID(originalID);
                ushort unpacked = ProvinceIDEncoder.UnpackProvinceID(packed);

                Assert.AreEqual(originalID, unpacked, $"Province ID {originalID} should round-trip correctly");
            }
        }

        [Test]
        public void PackProvinceID_ShouldUseCorrectChannels()
        {
            // Test low range (0-255) - should use only R channel
            Color32 low = ProvinceIDEncoder.PackProvinceID(100);
            Assert.AreEqual(100, low.r, "Low ID should be in R channel");
            Assert.AreEqual(0, low.g, "Low ID should have G=0");

            // Test high range (256+) - should use both R and G channels
            Color32 high = ProvinceIDEncoder.PackProvinceID(1000);
            Assert.AreEqual(1000 % 256, high.r, "High ID R channel should be remainder");
            Assert.AreEqual(1000 / 256, high.g, "High ID G channel should be quotient");
        }

        [Test]
        public void EncodeProvinceIDs_ShouldHandleBasicColors()
        {
            var colorArray = new NativeArray<Color32>(5, Allocator.Temp);
            colorArray[0] = Color.red;
            colorArray[1] = Color.green;
            colorArray[2] = Color.blue;
            colorArray[3] = Color.yellow;
            colorArray[4] = Color.magenta;

            try
            {
                var result = ProvinceIDEncoder.EncodeProvinceIDs(colorArray);

                try
                {
                    Assert.IsTrue(result.Success, "Encoding should succeed");
                    Assert.AreEqual(5, result.ProvinceCount, "Should have 5 provinces");
                    Assert.IsTrue(result.ColorToID.IsCreated, "ColorToID should be created");
                    Assert.IsTrue(result.IDToColor.IsCreated, "IDToColor should be created");

                    // Verify all colors got unique IDs starting from 1
                    var assignedIDs = new HashSet<ushort>();
                    for (int i = 0; i < colorArray.Length; i++)
                    {
                        var color = colorArray[i];
                        Assert.IsTrue(result.ColorToID.TryGetValue(color, out ushort id), $"Color {color} should have ID");
                        Assert.Greater(id, 0, "Province IDs should be positive");
                        Assert.IsFalse(assignedIDs.Contains(id), $"ID {id} should be unique");
                        assignedIDs.Add(id);

                        // Verify reverse mapping
                        Assert.IsTrue(result.IDToColor.TryGetValue(id, out Color32 mappedColor), $"ID {id} should map back to color");
                        Assert.AreEqual(color, mappedColor, "Reverse mapping should be correct");
                    }
                }
                finally
                {
                    result.Dispose();
                }
            }
            finally
            {
                colorArray.Dispose();
            }
        }

        [Test]
        public void EncodeProvinceIDsFromFrequencies_ShouldSortByFrequency()
        {
            var frequencies = new NativeHashMap<Color32, int>(10, Allocator.Temp);
            frequencies.TryAdd(Color.red, 1000);    // Most frequent - should get ID 1
            frequencies.TryAdd(Color.green, 500);   // Second - should get ID 2
            frequencies.TryAdd(Color.blue, 100);    // Least frequent - should get ID 3

            try
            {
                var result = ProvinceIDEncoder.EncodeProvinceIDsFromFrequencies(frequencies, sortByFrequency: true);

                try
                {
                    Assert.IsTrue(result.Success, "Encoding should succeed");
                    Assert.AreEqual(3, result.ProvinceCount, "Should have 3 provinces");

                    // Verify most frequent color gets lowest ID
                    Assert.IsTrue(result.ColorToID.TryGetValue(Color.red, out ushort redID));
                    Assert.IsTrue(result.ColorToID.TryGetValue(Color.green, out ushort greenID));
                    Assert.IsTrue(result.ColorToID.TryGetValue(Color.blue, out ushort blueID));

                    Assert.Less(redID, greenID, "More frequent color should get lower ID");
                    Assert.Less(greenID, blueID, "More frequent color should get lower ID");
                }
                finally
                {
                    result.Dispose();
                }
            }
            finally
            {
                frequencies.Dispose();
            }
        }

        [Test]
        public void EncodeProvinceIDs_ShouldRejectTooManyColors()
        {
            // Create too many colors (over 65534 limit) - use a smaller number for testing
            int colorCount = 65536; // Over the limit
            var manyColors = new NativeArray<Color32>(colorCount, Allocator.Temp);

            try
            {
                for (int i = 0; i < colorCount; i++)
                {
                    byte r = (byte)(i % 256);
                    byte g = (byte)(i / 256);
                    manyColors[i] = new Color32(r, g, 0, 255);
                }

                var result = ProvinceIDEncoder.EncodeProvinceIDs(manyColors);

                Assert.IsFalse(result.Success, "Should reject too many colors");
                Assert.IsNotEmpty(result.ErrorMessage, "Should have error message");
                Assert.That(result.ErrorMessage, Does.Contain("65534"), "Error should mention limit");

                // Result should still be disposable even on failure
                result.Dispose();
            }
            finally
            {
                manyColors.Dispose();
            }
        }

        [Test]
        public void EncodeProvinceIDs_ShouldHandleEmptyInput()
        {
            var emptyColors = new NativeArray<Color32>(0, Allocator.Temp);

            try
            {
                var result = ProvinceIDEncoder.EncodeProvinceIDs(emptyColors);

                Assert.IsFalse(result.Success, "Should fail for empty input");
                Assert.IsNotEmpty(result.ErrorMessage, "Should have error message");

                result.Dispose();
            }
            finally
            {
                emptyColors.Dispose();
            }
        }

        [Test]
        public void EncodeProvinceIDs_ShouldHandleDuplicateColors()
        {
            var colorsWithDuplicates = new NativeArray<Color32>(5, Allocator.Temp);
            colorsWithDuplicates[0] = Color.red;
            colorsWithDuplicates[1] = Color.green;
            colorsWithDuplicates[2] = Color.red;    // Duplicate
            colorsWithDuplicates[3] = Color.blue;
            colorsWithDuplicates[4] = Color.green;  // Another duplicate

            try
            {
                var result = ProvinceIDEncoder.EncodeProvinceIDs(colorsWithDuplicates);

                try
                {
                    Assert.IsTrue(result.Success, "Should handle duplicates");
                    Assert.AreEqual(3, result.ProvinceCount, "Should have 3 unique colors");

                    // All unique colors should be mapped
                    Assert.IsTrue(result.ColorToID.ContainsKey(Color.red));
                    Assert.IsTrue(result.ColorToID.ContainsKey(Color.green));
                    Assert.IsTrue(result.ColorToID.ContainsKey(Color.blue));
                }
                finally
                {
                    result.Dispose();
                }
            }
            finally
            {
                colorsWithDuplicates.Dispose();
            }
        }

        [Test]
        public void AnalyzeIDDistribution_ShouldNotCrash()
        {
            // Create a simple ID to color mapping
            var idToColor = new NativeHashMap<ushort, Color32>(3, Allocator.Temp);
            idToColor.TryAdd(1, Color.red);
            idToColor.TryAdd(2, Color.green);
            idToColor.TryAdd(100, Color.blue);

            try
            {
                // This is mainly a smoke test to ensure the analysis doesn't crash
                ProvinceIDEncoder.AnalyzeIDDistribution(idToColor);
                Assert.Pass("ID distribution analysis completed without error");
            }
            finally
            {
                idToColor.Dispose();
            }
        }

        [Test]
        public void IsValidProvinceID_ShouldValidateCorrectly()
        {
            // Test valid IDs
            Assert.IsTrue(ProvinceIDEncoder.IsValidProvinceID(1), "ID 1 should be valid");
            Assert.IsTrue(ProvinceIDEncoder.IsValidProvinceID(1000), "ID 1000 should be valid");
            Assert.IsTrue(ProvinceIDEncoder.IsValidProvinceID(65534), "ID 65534 should be valid");

            // Test invalid IDs
            Assert.IsFalse(ProvinceIDEncoder.IsValidProvinceID(0), "ID 0 should be invalid (reserved for ocean)");
            Assert.IsFalse(ProvinceIDEncoder.IsValidProvinceID(65535), "ID 65535 should be invalid (over limit)");
        }

        [Test]
        public void GetMaxProvinceCount_ShouldReturnCorrectLimit()
        {
            int maxCount = ProvinceIDEncoder.GetMaxProvinceCount();
            Assert.AreEqual(65534, maxCount, "Should return 65534 as maximum province count");
        }

        [Test]
        public void MaxProvinceID_ShouldBeCorrect()
        {
            // Test the maximum encodable province ID
            ushort maxID = 65534;

            Color32 packed = ProvinceIDEncoder.PackProvinceID(maxID);
            ushort unpacked = ProvinceIDEncoder.UnpackProvinceID(packed);

            Assert.AreEqual(maxID, unpacked, "Maximum province ID should encode/decode correctly");

            // Test one beyond maximum (should wrap or handle gracefully)
            ushort beyondMax = 65535;
            Color32 packedBeyond = ProvinceIDEncoder.PackProvinceID(beyondMax);
            ushort unpackedBeyond = ProvinceIDEncoder.UnpackProvinceID(packedBeyond);

            // The exact behavior here depends on implementation - document what happens
            ArchonLogger.Log($"Province ID 65535 encodes to ({packedBeyond.r}, {packedBeyond.g}) and decodes to {unpackedBeyond}");
        }
    }
}