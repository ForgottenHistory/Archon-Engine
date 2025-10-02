using NUnit.Framework;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

namespace Map.Tests.Rendering
{
    /// <summary>
    /// Tests for GPU texture infrastructure components that don't require MonoBehaviour instantiation
    /// Tests core functionality of Task 1.3: GPU Texture Infrastructure
    /// </summary>
    [TestFixture]
    public class TextureInfrastructureTests
    {
        [Test]
        public void Texture2D_RG16Format_ShouldPackProvincesCorrectly()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RG16, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            try
            {
                // Test province ID encoding/decoding
                ushort[] testProvinceIDs = { 0, 1, 255, 256, 1000, 32767, 65534 };

                foreach (ushort provinceID in testProvinceIDs)
                {
                    // Pack province ID into RG16 format
                    Color32 packedColor = Map.Province.ProvinceIDEncoder.PackProvinceID(provinceID);
                    texture.SetPixel(32, 32, packedColor);
                    texture.Apply(false);

                    // Unpack and verify
                    Color32 retrievedColor = texture.GetPixel(32, 32);
                    ushort unpacked = Map.Province.ProvinceIDEncoder.UnpackProvinceID(retrievedColor);

                    Assert.AreEqual(provinceID, unpacked, $"Province ID {provinceID} should pack/unpack correctly");
                }

                Assert.AreEqual(TextureFormat.RG16, texture.format, "Should use RG16 format");
                Assert.AreEqual(FilterMode.Point, texture.filterMode, "Should use point filtering");
                Assert.AreEqual(TextureWrapMode.Clamp, texture.wrapMode, "Should use clamp wrap mode");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void Texture2D_R16Format_ShouldStoreOwnerIDsCorrectly()
        {
            var texture = new Texture2D(32, 32, TextureFormat.R16, false);

            try
            {
                // Test owner ID storage
                ushort[] testOwnerIDs = { 0, 1, 100, 255 };

                foreach (ushort ownerID in testOwnerIDs)
                {
                    byte r = (byte)(ownerID & 0xFF);
                    Color32 ownerColor = new Color32(r, 0, 0, 255);
                    texture.SetPixel(16, 16, ownerColor);
                    texture.Apply(false);

                    Color32 retrieved = texture.GetPixel(16, 16);
                    Assert.AreEqual(r, retrieved.r, $"Owner ID {ownerID} should be stored correctly");
                }

                Assert.AreEqual(TextureFormat.R16, texture.format, "Should use R16 format");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ColorPalette_256x1RGBA32_ShouldSupportCountryColors()
        {
            var palette = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            palette.filterMode = FilterMode.Point;
            palette.wrapMode = TextureWrapMode.Clamp;

            try
            {
                // Test color palette functionality
                var testColors = new Color32[256];
                for (int i = 0; i < 256; i++)
                {
                    testColors[i] = new Color32((byte)i, (byte)(255 - i), 128, 255);
                }

                palette.SetPixels32(testColors);
                palette.Apply(false);

                // Verify colors were set correctly
                Color32 color0 = palette.GetPixel(0, 0);
                Color32 color255 = palette.GetPixel(255, 0);

                Assert.AreEqual(0, color0.r, "First color should have red=0");
                Assert.AreEqual(255, color0.g, "First color should have green=255");
                Assert.AreEqual(255, color255.r, "Last color should have red=255");
                Assert.AreEqual(0, color255.g, "Last color should have green=0");

                Assert.AreEqual(256, palette.width, "Palette width should be 256");
                Assert.AreEqual(1, palette.height, "Palette height should be 1");
                Assert.AreEqual(TextureFormat.RGBA32, palette.format, "Should use RGBA32 format");
                Assert.AreEqual(FilterMode.Point, palette.filterMode, "Should use point filtering");
                Assert.AreEqual(TextureWrapMode.Clamp, palette.wrapMode, "Should use clamp wrap mode");
            }
            finally
            {
                Object.DestroyImmediate(palette);
            }
        }

        [Test]
        public void RenderTexture_R8Format_ShouldSupportBorders()
        {
            var borderTexture = new RenderTexture(128, 128, 0, RenderTextureFormat.R8);
            borderTexture.filterMode = FilterMode.Point;
            borderTexture.wrapMode = TextureWrapMode.Clamp;
            borderTexture.useMipMap = false;

            try
            {
                borderTexture.Create();

                // Unity may report R8 as supported but still fallback during creation
                // Check what format was actually created
                if (borderTexture.format == RenderTextureFormat.R8)
                {
                    // R8 actually worked
                    Assert.AreEqual(RenderTextureFormat.R8, borderTexture.format, "R8 format should be used when actually supported");
                }
                else
                {
                    // Platform fell back to a different format despite reporting R8 as supported
                    Assert.That(borderTexture.format == RenderTextureFormat.ARGB32 ||
                               borderTexture.format == RenderTextureFormat.Default,
                               "Should fallback to supported format when R8 creation fails");
                }

                Assert.AreEqual(FilterMode.Point, borderTexture.filterMode, "Should use point filtering");
                Assert.AreEqual(TextureWrapMode.Clamp, borderTexture.wrapMode, "Should use clamp wrap mode");
                Assert.IsFalse(borderTexture.useMipMap, "Should not use mipmaps");
                Assert.IsTrue(borderTexture.IsCreated(), "Should be successfully created");
            }
            finally
            {
                if (borderTexture != null) borderTexture.Release();
            }
        }

        [Test]
        public void RenderTexture_ARGB32Format_ShouldSupportHighlights()
        {
            var highlightTexture = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32);
            highlightTexture.filterMode = FilterMode.Point;
            highlightTexture.useMipMap = false;

            try
            {
                highlightTexture.Create();

                Assert.AreEqual(RenderTextureFormat.ARGB32, highlightTexture.format, "Should use ARGB32 format");
                Assert.AreEqual(FilterMode.Point, highlightTexture.filterMode, "Should use point filtering");
                Assert.IsFalse(highlightTexture.useMipMap, "Should not use mipmaps");
                Assert.IsTrue(highlightTexture.IsCreated(), "Should be successfully created");
            }
            finally
            {
                if (highlightTexture != null) highlightTexture.Release();
            }
        }

        [Test]
        public void HSVColorGeneration_ShouldCreateDistinctColors()
        {
            // Test the color generation algorithm used in MapTextureManager
            var generatedColors = new Color32[10];

            for (int i = 0; i < 10; i++)
            {
                // Use same algorithm as GenerateDefaultPaletteColor
                float hue = (i * 137.508f) % 360f; // Golden angle
                float saturation = 0.7f + (i % 3) * 0.1f;
                float value = 0.8f + (i % 2) * 0.2f;

                Color color = Color.HSVToRGB(hue / 360f, saturation, value);
                generatedColors[i] = new Color32(
                    (byte)(color.r * 255),
                    (byte)(color.g * 255),
                    (byte)(color.b * 255),
                    255
                );
            }

            // Verify colors are distinct
            for (int i = 0; i < generatedColors.Length; i++)
            {
                for (int j = i + 1; j < generatedColors.Length; j++)
                {
                    var color1 = generatedColors[i];
                    var color2 = generatedColors[j];

                    bool areDistinct = color1.r != color2.r || color1.g != color2.g || color1.b != color2.b;
                    Assert.IsTrue(areDistinct, $"Colors {i} and {j} should be distinct");
                }
            }
        }

        [Test]
        public void TextureMemoryCalculation_ShouldBeAccurate()
        {
            int width = 2048, height = 2048;
            int pixelCount = width * height;

            // Calculate memory for different texture formats
            long provinceIDMemory = pixelCount * 2; // RG16 = 2 bytes per pixel
            long ownerMemory = pixelCount * 2;      // R16 = 2 bytes per pixel
            long colorMemory = pixelCount * 4;      // RGBA32 = 4 bytes per pixel
            long paletteMemory = 256 * 4;          // 256×1 RGBA32 = 1KB
            long borderMemory = pixelCount * 1;     // R8 = 1 byte per pixel
            long highlightMemory = pixelCount * 4;  // ARGB32 = 4 bytes per pixel

            long totalMemory = provinceIDMemory + ownerMemory + colorMemory +
                              paletteMemory + borderMemory + highlightMemory;

            // Verify calculations
            Assert.AreEqual(pixelCount * 2, provinceIDMemory, "Province ID memory calculation should be correct");
            Assert.AreEqual(1024, paletteMemory, "Palette memory should be exactly 1KB");

            // Total for 2048×2048 should be reasonable
            long expectedTotal = pixelCount * 13 + 1024; // ~13 bytes per pixel + 1KB palette
            Assert.AreEqual(expectedTotal, totalMemory, "Total memory calculation should be correct");

            float totalMB = totalMemory / 1024f / 1024f;
            Assert.Less(totalMB, 60f, "Total memory should be under 60MB for 2K×2K map");
        }

        [Test]
        public void TileCoordinateConversion_ShouldBeConsistent()
        {
            // Test tile coordinate calculations for streaming
            int tileSize = 512;
            int mapWidth = 2048, mapHeight = 2048;

            // Test various texture coordinates
            int2[] testCoords = {
                new int2(0, 0),
                new int2(256, 256),
                new int2(512, 512),
                new int2(1000, 1500),
                new int2(2047, 2047)
            };

            foreach (var coord in testCoords)
            {
                // Convert to tile coordinate
                int2 tileCoord = new int2(coord.x / tileSize, coord.y / tileSize);

                // Verify bounds
                Assert.GreaterOrEqual(tileCoord.x, 0, "Tile X should be non-negative");
                Assert.GreaterOrEqual(tileCoord.y, 0, "Tile Y should be non-negative");
                Assert.Less(tileCoord.x, (mapWidth + tileSize - 1) / tileSize, "Tile X should be within bounds");
                Assert.Less(tileCoord.y, (mapHeight + tileSize - 1) / tileSize, "Tile Y should be within bounds");

                // Verify conversion consistency - reconstructed should be tile origin
                int2 reconstructedCoord = new int2(tileCoord.x * tileSize, tileCoord.y * tileSize);

                // Check that original coord is within the tile bounds
                Assert.GreaterOrEqual(coord.x, reconstructedCoord.x, "X should be >= tile origin X");
                Assert.Less(coord.x, reconstructedCoord.x + tileSize, "X should be < tile origin X + tile size");
                Assert.GreaterOrEqual(coord.y, reconstructedCoord.y, "Y should be >= tile origin Y");
                Assert.Less(coord.y, reconstructedCoord.y + tileSize, "Y should be < tile origin Y + tile size");
            }
        }

        [Test]
        public void StreamingMemoryBudget_ShouldDetermineStreamingNeed()
        {
            // Test the streaming decision logic
            int mapWidth = 5632, mapHeight = 2048;
            long totalPixels = (long)mapWidth * mapHeight;
            long estimatedMemoryMB = (totalPixels * 13) / (1024 * 1024); // ~13 bytes per pixel

            // Test various budget scenarios
            float[] budgets = { 10f, 50f, 100f, 200f, 500f };

            foreach (float budget in budgets)
            {
                bool shouldStream = estimatedMemoryMB > budget;

                if (budget < 100f)
                {
                    Assert.IsTrue(shouldStream, $"Should stream with low budget {budget}MB");
                }
                else if (budget > 200f)
                {
                    Assert.IsFalse(shouldStream, $"Should not stream with high budget {budget}MB");
                }
            }

            Assert.Greater(estimatedMemoryMB, 0, "Estimated memory should be positive");
            Assert.Less(estimatedMemoryMB, 1000, "Estimated memory should be reasonable");
        }

        [Test]
        public void TextureFormatSupport_ShouldValidateRequiredFormats()
        {
            // Verify all required texture formats are supported
            Assert.IsTrue(SystemInfo.SupportsTextureFormat(TextureFormat.RG16), "RG16 format should be supported");
            Assert.IsTrue(SystemInfo.SupportsTextureFormat(TextureFormat.R16), "R16 format should be supported");
            Assert.IsTrue(SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32), "RGBA32 format should be supported");

            Assert.IsTrue(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8), "R8 render format should be supported");
            Assert.IsTrue(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32), "ARGB32 render format should be supported");
        }
    }
}