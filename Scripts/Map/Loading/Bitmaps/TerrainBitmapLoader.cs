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

        protected override void PopulateTexture(ParadoxParser.Jobs.BMPLoadResult terrainData)
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
                // 8-bit indexed color - use TerrainColorMapper for color lookup
                for (int y = 0; y < height && y < terrainData.Height; y++)
                {
                    for (int x = 0; x < width && x < terrainData.Width; x++)
                    {
                        int textureIndex = y * width + x;

                        // Read palette index from bitmap
                        if (ParadoxParser.Bitmap.BMPParser.TryGetPixelRGB(pixelData, x, y, out byte index, out byte _, out byte __))
                        {
                            // Use centralized terrain color mapper
                            pixels[textureIndex] = TerrainColorMapper.GetTerrainColor(index);
                            successfulReads++;
                        }
                        else
                        {
                            // Fallback to default terrain color
                            pixels[textureIndex] = TerrainColorMapper.GetDefaultTerrainColor();
                            failedReads++;
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

                        if (ParadoxParser.Bitmap.BMPParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
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
        }
    }
}
