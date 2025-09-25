using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using ParadoxParser.Bitmap;
using Map.Province;
using Map.Rendering;
using System.Collections.Generic;
using System.IO;

namespace Map.Loading
{
    /// <summary>
    /// High-performance province map loader using optimized ParadoxParser
    /// Handles provinces.bmp loading with validation and error handling
    /// </summary>
    public static class ProvinceMapLoader
    {
        /// <summary>
        /// Result structure for province bitmap loading
        /// </summary>
        public struct LoadResult
        {
            public bool Success;
            public int ProvinceCount;
            public int Width;
            public int Height;
            public string ErrorMessage;
            public NativeHashMap<Color32, ushort> ColorToID;
            public NativeArray<ProvincePixel> ProvincePixels;

            public void Dispose()
            {
                if (ColorToID.IsCreated) ColorToID.Dispose();
                if (ProvincePixels.IsCreated) ProvincePixels.Dispose();
            }
        }

        /// <summary>
        /// Structure representing a pixel and its province ID
        /// </summary>
        public struct ProvincePixel
        {
            public int2 Position;
            public ushort ProvinceID;
            public Color32 Color;
        }

        /// <summary>
        /// Load provinces.bmp using ParadoxParser with full validation
        /// </summary>
        /// <param name="filePath">Path to provinces.bmp file</param>
        /// <param name="expectedWidth">Expected map width (0 = any)</param>
        /// <param name="expectedHeight">Expected map height (0 = any)</param>
        /// <returns>Load result with province data</returns>
        public static LoadResult LoadProvinceMap(string filePath, int expectedWidth = 0, int expectedHeight = 0)
        {
            var result = new LoadResult();

            // Validate file exists
            if (!File.Exists(filePath))
            {
                result.ErrorMessage = $"Province map file not found: {filePath}";
                return result;
            }

            // Load file data
            NativeArray<byte> fileData;
            try
            {
                byte[] rawData = File.ReadAllBytes(filePath);
                fileData = new NativeArray<byte>(rawData, Allocator.TempJob);
            }
            catch (System.Exception e)
            {
                result.ErrorMessage = $"Failed to read file {filePath}: {e.Message}";
                return result;
            }

            Debug.Log($"Loading province map: {filePath} ({fileData.Length / 1024f:F1} KB)");

            try
            {
                result = ProcessBitmap(fileData, expectedWidth, expectedHeight);
            }
            finally
            {
                if (fileData.IsCreated)
                    fileData.Dispose();
            }

            return result;
        }

        /// <summary>
        /// Process bitmap data using ParadoxParser
        /// </summary>
        private static LoadResult ProcessBitmap(NativeArray<byte> fileData, int expectedWidth, int expectedHeight)
        {
            var result = new LoadResult();

            // Parse BMP header using ParadoxParser
            var header = BMPParser.ParseHeader(fileData);

            if (!header.IsValid)
            {
                result.ErrorMessage = "Invalid BMP file format or unsupported BMP type";
                return result;
            }

            result.Width = header.Width;
            result.Height = header.Height;

            // Validate dimensions
            if (expectedWidth > 0 && header.Width != expectedWidth)
            {
                result.ErrorMessage = $"Width mismatch: expected {expectedWidth}, got {header.Width}";
                return result;
            }

            if (expectedHeight > 0 && header.Height != expectedHeight)
            {
                result.ErrorMessage = $"Height mismatch: expected {expectedHeight}, got {header.Height}";
                return result;
            }

            Debug.Log($"BMP Info: {header.Width}x{header.Height}, {header.BitsPerPixel}bpp");

            // Get pixel data using ParadoxParser
            var pixelData = BMPParser.GetPixelData(fileData, header);
            if (!pixelData.Success)
            {
                result.ErrorMessage = "Failed to get pixel data from BMP";
                return result;
            }

            // Process pixels and build province mapping
            return ProcessPixelData(pixelData, result);
        }

        /// <summary>
        /// Process pixel data and create province mapping
        /// </summary>
        private static LoadResult ProcessPixelData(BMPParser.BMPPixelData pixelData, LoadResult result)
        {
            var header = pixelData.Header;
            int totalPixels = header.Width * header.Height;

            // Initialize data structures
            var colorFrequencies = new NativeHashMap<Color32, int>(1000, Allocator.TempJob);
            var provincePixels = new NativeList<ProvincePixel>(totalPixels, Allocator.TempJob);
            int oceanPixelCount = 0;

            Debug.Log($"Processing {totalPixels} pixels...");

            // Process each pixel
            for (int y = 0; y < header.Height; y++)
            {
                for (int x = 0; x < header.Width; x++)
                {
                    // Extract color using ParadoxParser (handles different BMP formats)
                    Color32 pixelColor = ExtractPixelColor(pixelData, x, y);

                    // Handle special cases
                    if (IsOceanColor(pixelColor))
                    {
                        oceanPixelCount++;
                        // Ocean gets ID 0 - add directly to pixels
                        provincePixels.Add(new ProvincePixel
                        {
                            Position = new int2(x, y),
                            ProvinceID = 0,
                            Color = pixelColor
                        });
                        continue;
                    }

                    // Count color frequency for later encoding
                    if (colorFrequencies.TryGetValue(pixelColor, out int currentCount))
                    {
                        colorFrequencies[pixelColor] = currentCount + 1;
                    }
                    else
                    {
                        colorFrequencies.TryAdd(pixelColor, 1);
                    }

                    // Add pixel with temporary ID 0 (will be updated after encoding)
                    provincePixels.Add(new ProvincePixel
                    {
                        Position = new int2(x, y),
                        ProvinceID = 0, // Temporary - will be updated
                        Color = pixelColor
                    });
                }

                // Progress logging for large maps
                if (y > 0 && y % 200 == 0)
                {
                    float progress = (float)y / header.Height;
                    Debug.Log($"Processing: {progress * 100f:F1}% complete ({colorFrequencies.Count} unique colors found)");
                }
            }

            // Validate we found provinces
            if (colorFrequencies.Count == 0)
            {
                result.ErrorMessage = "No provinces found in bitmap";
                colorFrequencies.Dispose();
                provincePixels.Dispose();
                return result;
            }

            // Encode province IDs using the new encoding system
            Debug.Log($"Encoding {colorFrequencies.Count} unique colors to province IDs...");

            var encodingResult = ProvinceIDEncoder.EncodeProvinceIDsFromFrequencies(colorFrequencies, sortByFrequency: true);

            if (!encodingResult.Success)
            {
                result.ErrorMessage = $"Province ID encoding failed: {encodingResult.ErrorMessage}";
                colorFrequencies.Dispose();
                provincePixels.Dispose();
                encodingResult.Dispose();
                return result;
            }

            // Update pixel data with correct province IDs
            for (int i = 0; i < provincePixels.Length; i++)
            {
                var pixel = provincePixels[i];

                // Skip ocean pixels (already have ID 0)
                if (pixel.ProvinceID == 0 && IsOceanColor(pixel.Color))
                    continue;

                // Get correct province ID from encoding
                if (encodingResult.ColorToID.TryGetValue(pixel.Color, out ushort correctID))
                {
                    pixel.ProvinceID = correctID;
                    provincePixels[i] = pixel;
                }
                else
                {
                    Debug.LogError($"Failed to find province ID for color {pixel.Color}");
                }
            }

            // Log statistics
            Debug.Log($"Province map loaded successfully:");
            Debug.Log($"  - Dimensions: {header.Width}x{header.Height}");
            Debug.Log($"  - Provinces: {encodingResult.ProvinceCount}");
            Debug.Log($"  - Ocean pixels: {oceanPixelCount}");
            Debug.Log($"  - Total pixels: {totalPixels}");

            // Check for very small provinces (potential errors)
            int smallProvinceCount = 0;
            foreach (var kvp in colorFrequencies)
            {
                if (kvp.Value < 4) // Less than 4 pixels
                {
                    smallProvinceCount++;
                    if (smallProvinceCount <= 5) // Only log first few
                    {
                        var provinceID = encodingResult.ColorToID[kvp.Key];
                        Debug.LogWarning($"Very small province: ID {provinceID}, Color {kvp.Key} has only {kvp.Value} pixels");
                    }
                }
            }

            if (smallProvinceCount > 5)
            {
                Debug.LogWarning($"Found {smallProvinceCount} very small provinces (< 4 pixels)");
            }

            // Analyze ID distribution for debugging
            ProvinceIDEncoder.AnalyzeIDDistribution(encodingResult.IDToColor);

            // Return success
            result.Success = true;
            result.ProvinceCount = encodingResult.ProvinceCount;
            result.ColorToID = encodingResult.ColorToID; // Transfer ownership

            // Create a copy of the pixel data (AsArray() returns a view that becomes invalid when list is disposed)
            result.ProvincePixels = new NativeArray<ProvincePixel>(provincePixels.AsArray(), Allocator.Persistent);

            // Clean up
            colorFrequencies.Dispose();
            provincePixels.Dispose();
            // Note: Don't dispose encodingResult.ColorToID as we transferred ownership

            return result;
        }

        /// <summary>
        /// Extract pixel color at coordinates using ParadoxParser
        /// </summary>
        private static Color32 ExtractPixelColor(BMPParser.BMPPixelData pixelData, int x, int y)
        {
            // Use ParadoxParser's optimized pixel extraction
            if (BMPParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
            {
                return new Color32(r, g, b, 255);
            }

            // Return black if extraction failed
            return Color.black;
        }

        /// <summary>
        /// Check if color represents ocean/water (pure blue or black)
        /// </summary>
        private static bool IsOceanColor(Color32 color)
        {
            // Pure black (common for wasteland/ocean)
            if (color.r == 0 && color.g == 0 && color.b == 0)
                return true;

            // Pure blue (sometimes used for ocean)
            if (color.r == 0 && color.g == 0 && color.b == 255)
                return true;

            return false;
        }

        /// <summary>
        /// Create error texture for missing/invalid provinces
        /// </summary>
        public static Texture2D CreateErrorTexture(int width, int height)
        {
            var errorTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            errorTexture.name = "ProvinceMap_Error";

            // Create checkerboard pattern for error visualization
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    bool isChecker = ((x / 16) + (y / 16)) % 2 == 0;
                    pixels[index] = isChecker ? new Color32(255, 0, 255, 255) : Color.black; // Magenta/Black
                }
            }

            errorTexture.SetPixels32(pixels);
            errorTexture.Apply(false);

            return errorTexture;
        }

        /// <summary>
        /// Validate province count is within limits
        /// </summary>
        public static bool ValidateProvinceCount(int provinceCount, out string errorMessage)
        {
            errorMessage = "";

            if (provinceCount == 0)
            {
                errorMessage = "No provinces found in map";
                return false;
            }

            if (provinceCount >= 65535)
            {
                errorMessage = $"Too many provinces ({provinceCount}). Maximum is 65,534.";
                return false;
            }

            if (provinceCount > 20000)
            {
                errorMessage = $"Warning: Very high province count ({provinceCount}). Performance may be impacted.";
                // Return true but with warning
            }

            return true;
        }
    }
}