using System;
using System.IO;
using NUnit.Framework;
using Unity.Collections;
using ParadoxParser.Bitmap;

namespace ParadoxParser.Tests
{
    /// <summary>
    /// Tests for BMP parsing functionality
    /// </summary>
    [TestFixture]
    public class BitmapParserTests
    {
        private const string TEST_DATA_PATH = "D:/Stuff/My Games/Dominion/Dominion/Assets/Data/map";

        [Test]
        public void BMPParser_ParseHeader_ShouldExtractValidHeader()
        {
            string provincesPath = Path.Combine(TEST_DATA_PATH, "provinces.bmp");

            if (!File.Exists(provincesPath))
            {
                Assert.Ignore($"Test file not found: {provincesPath}");
                return;
            }

            var fileData = LoadFileToNativeArray(provincesPath);
            try
            {
                var header = BMPParser.ParseHeader(fileData);

                Assert.IsTrue(header.IsValid, "BMP header should be valid");
                Assert.Greater(header.Width, 0, "Width should be positive");
                Assert.Greater(header.Height, 0, "Height should be positive");
                Assert.IsTrue(header.BitsPerPixel == 24 || header.BitsPerPixel == 32, "Should support 24 or 32 bit formats");

                UnityEngine.Debug.Log($"BMP Header: {header.Width}x{header.Height}, {header.BitsPerPixel}bpp");
            }
            finally
            {
                fileData.Dispose();
            }
        }

        [Test]
        public void BMPParser_GetPixelData_ShouldAccessPixels()
        {
            string provincesPath = Path.Combine(TEST_DATA_PATH, "provinces.bmp");

            if (!File.Exists(provincesPath))
            {
                Assert.Ignore($"Test file not found: {provincesPath}");
                return;
            }

            var fileData = LoadFileToNativeArray(provincesPath);
            try
            {
                var header = BMPParser.ParseHeader(fileData);
                Assert.IsTrue(header.IsValid, "Header should be valid");

                var pixelData = BMPParser.GetPixelData(fileData, header);
                Assert.IsTrue(pixelData.Success, "Pixel data extraction should succeed");

                // Test reading a few pixels
                bool pixelRead = BMPParser.TryGetPixelRGB(pixelData, 0, 0, out byte r, out byte g, out byte b);
                Assert.IsTrue(pixelRead, "Should be able to read pixels");

                // Test packed RGB
                bool packedRead = BMPParser.TryGetPixelRGBPacked(pixelData, 0, 0, out int rgb);
                Assert.IsTrue(packedRead, "Should be able to read packed RGB");

                int expectedPacked = (r << 16) | (g << 8) | b;
                Assert.AreEqual(expectedPacked, rgb, "Packed RGB should match individual components");

                UnityEngine.Debug.Log($"First pixel: RGB({r}, {g}, {b}) = 0x{rgb:X6}");
            }
            finally
            {
                fileData.Dispose();
            }
        }

        [Test]
        public void BMPParser_CollectUniqueColors_ShouldFindProvinceColors()
        {
            string provincesPath = Path.Combine(TEST_DATA_PATH, "provinces.bmp");

            if (!File.Exists(provincesPath))
            {
                Assert.Ignore($"Test file not found: {provincesPath}");
                return;
            }

            var fileData = LoadFileToNativeArray(provincesPath);
            try
            {
                var header = BMPParser.ParseHeader(fileData);
                var pixelData = BMPParser.GetPixelData(fileData, header);

                // This might be slow for large images, so we'll sample a small area
                var smallPixelData = new BMPParser.BMPPixelData
                {
                    RawData = pixelData.RawData,
                    Header = new BMPParser.BMPHeader
                    {
                        FileHeader = header.FileHeader,
                        InfoHeader = new BMPParser.BMPInfoHeader
                        {
                            Width = Math.Min(100, header.Width),
                            Height = Math.Min(100, header.Height),
                            BitsPerPixel = header.InfoHeader.BitsPerPixel,
                            HeaderSize = header.InfoHeader.HeaderSize,
                            Planes = header.InfoHeader.Planes,
                            Compression = header.InfoHeader.Compression
                        },
                        Success = true
                    },
                    Success = true
                };

                var uniqueColors = BMPParser.CollectUniqueColors(smallPixelData, Allocator.Temp);
                try
                {
                    Assert.Greater(uniqueColors.Count, 0, "Should find unique colors");
                    UnityEngine.Debug.Log($"Found {uniqueColors.Count} unique colors in 100x100 sample");
                }
                finally
                {
                    uniqueColors.Dispose();
                }
            }
            finally
            {
                fileData.Dispose();
            }
        }

        [Test]
        public void ProvinceMapParser_ParseWithDefinition_ShouldMapColors()
        {
            string provincesPath = Path.Combine(TEST_DATA_PATH, "provinces.bmp");
            string definitionPath = Path.Combine(TEST_DATA_PATH, "definition.csv");

            if (!File.Exists(provincesPath) || !File.Exists(definitionPath))
            {
                Assert.Ignore($"Test files not found: {provincesPath} or {definitionPath}");
                return;
            }

            var bmpData = LoadFileToNativeArray(provincesPath);
            var csvData = LoadFileToNativeArray(definitionPath);

            try
            {
                var provinceMap = ProvinceMapParser.ParseProvinceMap(bmpData, csvData, Allocator.Temp);
                try
                {
                    Assert.IsTrue(provinceMap.Success, "Province map parsing should succeed");
                    Assert.Greater(provinceMap.ProvinceCount, 0, "Should have provinces");

                    // Test getting province at specific coordinates
                    bool foundProvince = ProvinceMapParser.TryGetProvinceAt(provinceMap, 100, 100, out int provinceID);
                    if (foundProvince)
                    {
                        Assert.Greater(provinceID, 0, "Province ID should be positive");
                        UnityEngine.Debug.Log($"Province at (100,100): {provinceID}");
                    }

                    UnityEngine.Debug.Log($"Successfully mapped {provinceMap.ProvinceCount} provinces");
                }
                finally
                {
                    provinceMap.Dispose();
                }
            }
            finally
            {
                bmpData.Dispose();
                csvData.Dispose();
            }
        }

        [Test]
        public void HeightmapParser_ParseHeightmap_ShouldExtractElevation()
        {
            string heightmapPath = Path.Combine(TEST_DATA_PATH, "heightmap.bmp");

            if (!File.Exists(heightmapPath))
            {
                Assert.Ignore($"Test file not found: {heightmapPath}");
                return;
            }

            var fileData = LoadFileToNativeArray(heightmapPath);
            try
            {
                var heightmap = HeightmapParser.ParseHeightmap(fileData, Allocator.Temp);
                try
                {
                    Assert.IsTrue(heightmap.Success, "Heightmap parsing should succeed");
                    Assert.Greater(heightmap.Width, 0, "Width should be positive");
                    Assert.Greater(heightmap.Height, 0, "Height should be positive");

                    // Test getting height at specific coordinates
                    bool heightFound = HeightmapParser.TryGetHeightAt(heightmap, 100, 100, out float height);
                    Assert.IsTrue(heightFound, "Should be able to get height");
                    Assert.IsTrue(height >= 0f && height <= 1f, "Normalized height should be 0-1");

                    // Test terrain classification
                    bool terrainFound = HeightmapParser.TryGetTerrainType(heightmap, 100, 100, out var terrainType);
                    Assert.IsTrue(terrainFound, "Should be able to classify terrain");

                    // Test interpolated sampling
                    bool interpFound = HeightmapParser.TryGetInterpolatedHeight(heightmap, 100.5f, 100.5f, out float interpHeight);
                    Assert.IsTrue(interpFound, "Should be able to interpolate height");

                    UnityEngine.Debug.Log($"Height at (100,100): {height:F3}, Terrain: {terrainType}, Interpolated: {interpHeight:F3}");

                    // Calculate stats
                    var stats = HeightmapParser.CalculateStats(heightmap);
                    Assert.IsTrue(stats.IsValid, "Stats should be valid");
                    UnityEngine.Debug.Log($"Heightmap stats - Avg: {stats.AverageHeight:F3}, Water: {stats.WaterPixels}, Mountains: {stats.MountainPixels}");
                }
                finally
                {
                    heightmap.Dispose();
                }
            }
            finally
            {
                fileData.Dispose();
            }
        }

        /// <summary>
        /// Helper to load file data into NativeArray
        /// </summary>
        private NativeArray<byte> LoadFileToNativeArray(string filePath)
        {
            var fileBytes = File.ReadAllBytes(filePath);
            var nativeArray = new NativeArray<byte>(fileBytes.Length, Allocator.Temp);
            nativeArray.CopyFrom(fileBytes);
            return nativeArray;
        }
    }
}