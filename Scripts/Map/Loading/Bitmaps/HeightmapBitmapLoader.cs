using UnityEngine;
using Map.Rendering;
using Utils;

namespace Map.Loading.Bitmaps
{
    /// <summary>
    /// Specialized loader for heightmap.bmp files
    /// Converts 8-bit grayscale heightmaps to R8 texture format
    /// Height values: 0-255 (8-bit) → 0.0-1.0 (normalized float)
    /// </summary>
    public class HeightmapBitmapLoader : BitmapTextureLoader
    {
        protected override string GetBitmapFileName() => "heightmap.bmp";
        protected override string GetLoaderName() => "HeightmapBitmapLoader";

        protected override void PopulateTexture(ParadoxParser.Jobs.BMPLoadResult heightmapData)
        {
            if (textureManager == null || textureManager.HeightmapTexture == null)
            {
                ArchonLogger.LogError("HeightmapBitmapLoader: Cannot populate - texture manager or heightmap texture not available");
                return;
            }

            var heightmapTexture = textureManager.HeightmapTexture;
            int width = heightmapTexture.width;
            int height = heightmapTexture.height;

            // Create pixel array for heightmap texture (R8 format uses Color with only R channel)
            var pixels = new Color[width * height];
            var pixelData = heightmapData.GetPixelData();

            if (logProgress)
            {
                ArchonLogger.LogMapInit($"HeightmapBitmapLoader: Processing {heightmapData.Width}x{heightmapData.Height}, {heightmapData.BitsPerPixel}bpp");
            }

            int successfulReads = 0;
            int failedReads = 0;

            // Process 8-bit indexed heightmap data
            // For grayscale heightmaps, the palette index IS the height value (0-255)
            for (int y = 0; y < height && y < heightmapData.Height; y++)
            {
                for (int x = 0; x < width && x < heightmapData.Width; x++)
                {
                    int textureIndex = y * width + x;

                    // Read palette index as height value
                    if (ParadoxParser.Bitmap.BMPParser.TryGetPixelRGB(pixelData, x, y, out byte heightValue, out byte _, out byte __))
                    {
                        // Convert 8-bit height (0-255) to normalized float (0.0-1.0)
                        float normalizedHeight = heightValue / 255f;
                        pixels[textureIndex] = new Color(normalizedHeight, 0, 0, 1);
                        successfulReads++;
                    }
                    else
                    {
                        // Fallback to sea level (0.5) if pixel reading fails
                        pixels[textureIndex] = new Color(0.5f, 0, 0, 1);
                        failedReads++;
                    }
                }
            }

            if (logProgress)
            {
                ArchonLogger.LogMapInit($"HeightmapBitmapLoader: Read stats - Success: {successfulReads}, Failed: {failedReads}");

                // Sample some height values to verify data
                if (successfulReads > 0)
                {
                    float h1 = pixels[width * height / 4].r;
                    float h2 = pixels[width * height / 2].r;
                    float h3 = pixels[width * height * 3 / 4].r;
                    ArchonLogger.LogMapInit($"HeightmapBitmapLoader: Height samples - [{h1:F3}] [{h2:F3}] [{h3:F3}] (0.0=low, 1.0=high)");
                }
            }

            // Apply height data to texture
            heightmapTexture.SetPixels(pixels);
            ApplyTextureAndSync(heightmapTexture);
        }
    }
}
