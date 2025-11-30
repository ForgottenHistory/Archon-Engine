using UnityEngine;
using Map.Rendering;
using Map.Loading.Data;
using Utils;

namespace Map.Loading.Bitmaps
{
    /// <summary>
    /// Specialized loader for terrain.bmp files
    /// Handles 8-bit indexed terrain bitmaps with terrain color mappings
    /// </summary>
    public class TerrainBitmapLoader : BitmapTextureLoader
    {
        protected override string GetBitmapFileName() => "terrain.bmp";
        protected override string GetLoaderName() => "TerrainBitmapLoader";

        protected override void PopulateTexture(BMPLoadResult terrainData)
        {
            if (textureManager == null || textureManager.ProvinceTerrainTexture == null)
            {
                ArchonLogger.LogError("TerrainBitmapLoader: Cannot populate - texture manager or terrain texture not available", "map_initialization");
                return;
            }

            var terrainTexture = textureManager.ProvinceTerrainTexture;
            int width = terrainTexture.width;
            int height = terrainTexture.height;

            // Create pixel array for terrain texture
            var pixels = new Color32[width * height];
            var pixelData = terrainData.GetPixelData();

            if (logProgress)
            {
                ArchonLogger.Log($"TerrainBitmapLoader: Processing {terrainData.Width}x{terrainData.Height}, {terrainData.BitsPerPixel}bpp", "map_initialization");
            }

            int successfulReads = 0;
            int failedReads = 0;

            // Handle indexed color format (8-bit) vs direct RGB
            if (terrainData.BitsPerPixel == 8)
            {
                // 8-bit indexed color - use REAL palette from BMP file
                var palette = terrainData.Palette;

                if (palette != null && palette.Length > 0)
                {
                    if (logProgress)
                    {
                        ArchonLogger.Log($"TerrainBitmapLoader: Using BMP palette with {palette.Length} colors", "map_initialization");

                        // Print entire palette to compare with terrain_rgb.json5
                        ArchonLogger.Log($"TerrainBitmapLoader: Extracted palette ({palette.Length} colors):", "map_initialization");
                        for (int i = 0; i < palette.Length; i++)
                        {
                            ArchonLogger.Log($"  Palette[{i}] = RGB({palette[i].r},{palette[i].g},{palette[i].b})", "map_initialization");
                        }
                    }

                    // Track indices found for province 357 debugging
                    System.Collections.Generic.Dictionary<byte, int> province357Indices = new System.Collections.Generic.Dictionary<byte, int>();

                    // Read palette indices and convert to RGB using REAL palette colors
                    for (int y = 0; y < height && y < terrainData.Height; y++)
                    {
                        for (int x = 0; x < width && x < terrainData.Width; x++)
                        {
                            int textureIndex = y * width + x;

                            // Read palette index from bitmap
                            if (BMPParser.TryGetPixelRGB(pixelData, x, y, out byte index, out byte _, out byte __))
                            {
                                // Log specific coordinate for province 357 debugging
                                if (logProgress && x == 3162 && y == 865)
                                {
                                    ArchonLogger.Log($"TerrainBitmapLoader: Pixel at (3162,865) - Read index={index} → RGB({palette[index].r},{palette[index].g},{palette[index].b})", "map_initialization");
                                }

                                // Use REAL palette RGB color from BMP file
                                if (index < palette.Length)
                                {
                                    pixels[textureIndex] = palette[index];

                                    // Track indices for province 357 (check provinces.bmp at same coordinate)
                                    // We'll log all unique indices found to see what's being read
                                    if (!province357Indices.ContainsKey(index))
                                    {
                                        province357Indices[index] = 0;
                                    }
                                    province357Indices[index]++;
                                }
                                else
                                {
                                    pixels[textureIndex] = new Color32(0, 0, 0, 255); // Black for out-of-range
                                }
                                successfulReads++;
                            }
                            else
                            {
                                pixels[textureIndex] = new Color32(0, 0, 0, 255);
                                failedReads++;
                            }
                        }
                    }

                    // Log all indices found in terrain.bmp
                    if (logProgress)
                    {
                        ArchonLogger.Log($"TerrainBitmapLoader: Found {province357Indices.Count} unique terrain palette indices across entire map:", "map_initialization");
                        foreach (var kvp in province357Indices)
                        {
                            byte idx = kvp.Key;
                            int count = kvp.Value;
                            ArchonLogger.Log($"  Index[{idx}] → RGB({palette[idx].r},{palette[idx].g},{palette[idx].b}) - {count} pixels", "map_initialization");
                        }
                    }
                }
                else
                {
                    // Fallback to TerrainColorMapper if no palette available
                    ArchonLogger.LogWarning("TerrainBitmapLoader: No palette found in 8-bit BMP, using TerrainColorMapper fallback", "map_initialization");

                    for (int y = 0; y < height && y < terrainData.Height; y++)
                    {
                        for (int x = 0; x < width && x < terrainData.Width; x++)
                        {
                            int textureIndex = y * width + x;

                            if (BMPParser.TryGetPixelRGB(pixelData, x, y, out byte index, out byte _, out byte __))
                            {
                                pixels[textureIndex] = TerrainColorMapper.GetTerrainColor(index);
                                successfulReads++;
                            }
                            else
                            {
                                pixels[textureIndex] = TerrainColorMapper.GetDefaultTerrainColor();
                                failedReads++;
                            }
                        }
                    }
                }
            }
            else
            {
                // Direct RGB format (24-bit or 32-bit)
                for (int y = 0; y < height && y < terrainData.Height; y++)
                {
                    for (int x = 0; x < width && x < terrainData.Width; x++)
                    {
                        int textureIndex = y * width + x;

                        if (BMPParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
                        {
                            pixels[textureIndex] = new Color32(r, g, b, 255);
                            successfulReads++;
                        }
                        else
                        {
                            pixels[textureIndex] = TerrainColorMapper.GetDefaultTerrainColor();
                            failedReads++;
                        }
                    }
                }
            }

            if (logProgress)
            {
                ArchonLogger.Log($"TerrainBitmapLoader: Read stats - Success: {successfulReads}, Failed: {failedReads}", "map_initialization");

                // Sample some terrain colors to verify
                Color32 sample1 = pixels[width * height / 4];
                Color32 sample2 = pixels[width * height / 2];
                Color32 sample3 = pixels[width * height * 3 / 4];
                ArchonLogger.Log($"TerrainBitmapLoader: Texture samples - [{sample1.r},{sample1.g},{sample1.b}] [{sample2.r},{sample2.g},{sample2.b}] [{sample3.r},{sample3.g},{sample3.b}]", "map_initialization");
            }

            // Apply terrain colors to texture
            terrainTexture.SetPixels32(pixels);
            ApplyTextureAndSync(terrainTexture);

            // Verify the texture stored the pixel correctly by reading it back
            if (logProgress)
            {
                Color32 verifyPixel = terrainTexture.GetPixel(3162, 865);
                ArchonLogger.Log($"TerrainBitmapLoader: VERIFY texture at (3162,865) after upload = RGB({verifyPixel.r},{verifyPixel.g},{verifyPixel.b})", "map_initialization");
            }

            // Generate terrain type texture (R8, terrain indices) from terrain colors
            // Must happen AFTER terrain.bmp is loaded into ProvinceTerrainTexture
            if (logProgress)
            {
                ArchonLogger.Log("TerrainBitmapLoader: Generating terrain type texture from loaded terrain colors", "map_initialization");
            }

            textureManager.GenerateTerrainTypeTexture();
        }
    }
}
