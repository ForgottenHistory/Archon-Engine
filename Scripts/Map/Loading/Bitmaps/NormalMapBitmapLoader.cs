using UnityEngine;
using Map.Rendering;
using Utils;

namespace Map.Loading.Bitmaps
{
    /// <summary>
    /// Specialized loader for world_normal.bmp files
    /// Loads 24-bit RGB normal maps encoding surface normals as RGB values
    /// Normal map format: RGB(128,128,255) = flat surface (up direction)
    /// </summary>
    public class NormalMapBitmapLoader : BitmapTextureLoader
    {
        protected override string GetBitmapFileName() => "world_normal.bmp";
        protected override string GetLoaderName() => "NormalMapBitmapLoader";

        protected override void PopulateTexture(ParadoxParser.Jobs.BMPLoadResult normalMapData)
        {
            if (textureManager == null || textureManager.NormalMapTexture == null)
            {
                ArchonLogger.LogMapInitError("NormalMapBitmapLoader: Cannot populate - texture manager or normal map texture not available");
                return;
            }

            var normalMapTexture = textureManager.NormalMapTexture;
            int width = normalMapTexture.width;
            int height = normalMapTexture.height;

            // Create pixel array for normal map texture (RGB24 format)
            var pixels = new Color32[width * height];
            var pixelData = normalMapData.GetPixelData();

            if (logProgress)
            {
                ArchonLogger.LogMapInit($"NormalMapBitmapLoader: Processing {normalMapData.Width}x{normalMapData.Height}, {normalMapData.BitsPerPixel}bpp");
            }

            int successfulReads = 0;
            int failedReads = 0;

            // Process 24-bit RGB normal map data
            // Normal maps encode surface normals as RGB where each channel represents X/Y/Z components
            for (int y = 0; y < height && y < normalMapData.Height; y++)
            {
                for (int x = 0; x < width && x < normalMapData.Width; x++)
                {
                    int textureIndex = y * width + x;

                    // Read RGB values from normal map (R=X, G=Y, B=Z)
                    if (ParadoxParser.Bitmap.BMPParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
                    {
                        // Store RGB directly (shader will decode to normal vector)
                        pixels[textureIndex] = new Color32(r, g, b, 255);
                        successfulReads++;
                    }
                    else
                    {
                        // Fallback to flat normal (up direction) if pixel reading fails: RGB(128, 128, 255)
                        pixels[textureIndex] = new Color32(128, 128, 255, 255);
                        failedReads++;
                    }
                }
            }

            if (logProgress)
            {
                ArchonLogger.LogMapInit($"NormalMapBitmapLoader: Read stats - Success: {successfulReads}, Failed: {failedReads}");

                // Sample some normal values to verify data
                if (successfulReads > 0)
                {
                    var n1 = pixels[width * height / 4];
                    var n2 = pixels[width * height / 2];
                    var n3 = pixels[width * height * 3 / 4];
                    ArchonLogger.LogMapInit($"NormalMapBitmapLoader: Normal samples - RGB[{n1.r},{n1.g},{n1.b}] RGB[{n2.r},{n2.g},{n2.b}] RGB[{n3.r},{n3.g},{n3.b}]");
                }
            }

            // Apply normal data to texture
            normalMapTexture.SetPixels32(pixels);
            ApplyTextureAndSync(normalMapTexture);
        }
    }
}
