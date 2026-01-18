using UnityEngine;
using Unity.Collections;
using Map.Rendering;
using Utils;

namespace Map.Loading.Images
{
    /// <summary>
    /// Loads heightmap.png (or heightmap.bmp) and populates the heightmap texture.
    /// Uses ImageParser for unified BMP/PNG support with PNG preferred.
    /// Height values: 0-255 (8-bit) → 0.0-1.0 (normalized float)
    /// </summary>
    public class HeightmapImageLoader
    {
        private MapTextureManager textureManager;
        private bool logProgress;

        public void Initialize(MapTextureManager textures, bool enableLogging = true)
        {
            textureManager = textures;
            logProgress = enableLogging;
        }

        /// <summary>
        /// Load heightmap image and populate heightmap texture.
        /// Tries heightmap.png first, falls back to heightmap.bmp.
        /// </summary>
        public void LoadAndPopulate(string mapDirectory)
        {
            if (textureManager == null || textureManager.HeightmapTexture == null)
            {
                ArchonLogger.LogError("HeightmapImageLoader: Texture manager or heightmap texture not available", "map_initialization");
                return;
            }

            // Try PNG first, then BMP
            string pngPath = System.IO.Path.Combine(mapDirectory, "heightmap.png");
            string bmpPath = System.IO.Path.Combine(mapDirectory, "heightmap.bmp");

            string imagePath = null;
            if (System.IO.File.Exists(pngPath))
            {
                imagePath = pngPath;
            }
            else if (System.IO.File.Exists(bmpPath))
            {
                imagePath = bmpPath;
            }
            else
            {
                ArchonLogger.LogWarning($"HeightmapImageLoader: No heightmap image found (tried {pngPath} and {bmpPath}), using defaults", "map_initialization");
                return;
            }

            if (logProgress)
            {
                ArchonLogger.Log($"HeightmapImageLoader: Loading {imagePath}", "map_initialization");
            }

            // Read file into NativeArray
            byte[] fileBytes = System.IO.File.ReadAllBytes(imagePath);
            var fileData = new NativeArray<byte>(fileBytes, Allocator.Temp);

            try
            {
                // Parse image (auto-detects BMP vs PNG)
                var pixelData = ImageParser.Parse(fileData, Allocator.Temp);

                if (!pixelData.IsSuccess)
                {
                    ArchonLogger.LogError($"HeightmapImageLoader: Failed to parse {imagePath}", "map_initialization");
                    return;
                }

                if (logProgress)
                {
                    ArchonLogger.Log($"HeightmapImageLoader: Parsed {pixelData.Header.Width}x{pixelData.Header.Height} image, format={pixelData.Format}", "map_initialization");
                }

                // Populate texture
                PopulateTexture(pixelData);

                // Dispose pixel data
                pixelData.Dispose();
            }
            finally
            {
                fileData.Dispose();
            }

            if (logProgress)
            {
                ArchonLogger.Log("HeightmapImageLoader: Heightmap texture populated", "map_initialization");
            }
        }

        private void PopulateTexture(ImageParser.ImagePixelData pixelData)
        {
            var heightmapTexture = textureManager.HeightmapTexture;
            int width = heightmapTexture.width;
            int height = heightmapTexture.height;

            // Create pixel array for heightmap texture (R8 format uses Color with only R channel)
            var pixels = new Color[width * height];
            int successCount = 0;
            int failCount = 0;

            for (int y = 0; y < height && y < pixelData.Header.Height; y++)
            {
                for (int x = 0; x < width && x < pixelData.Header.Width; x++)
                {
                    int textureIndex = y * width + x;

                    // Read height value (grayscale - R channel contains height)
                    if (ImageParser.TryGetPixelRGB(pixelData, x, y, out byte heightValue, out byte _, out byte __))
                    {
                        // Convert 8-bit height (0-255) to normalized float (0.0-1.0)
                        float normalizedHeight = heightValue / 255f;
                        pixels[textureIndex] = new Color(normalizedHeight, 0, 0, 1);
                        successCount++;
                    }
                    else
                    {
                        // Fallback to sea level (0.5) if pixel reading fails
                        pixels[textureIndex] = new Color(0.5f, 0, 0, 1);
                        failCount++;
                    }
                }
            }

            if (logProgress)
            {
                ArchonLogger.Log($"HeightmapImageLoader: Read {successCount} pixels, {failCount} failed", "map_initialization");

                // Sample some height values to verify data
                if (successCount > 0)
                {
                    float h1 = pixels[width * height / 4].r;
                    float h2 = pixels[width * height / 2].r;
                    float h3 = pixels[width * height * 3 / 4].r;
                    ArchonLogger.Log($"HeightmapImageLoader: Height samples - [{h1:F3}] [{h2:F3}] [{h3:F3}] (0.0=low, 1.0=high)", "map_initialization");
                }
            }

            // Apply height data to texture
            heightmapTexture.SetPixels(pixels);
            heightmapTexture.Apply(false);
            GL.Flush();

            // Rebind textures to materials
            RebindTextures();
        }

        private void RebindTextures()
        {
            var mapRenderer = Object.FindFirstObjectByType<Rendering.MapRenderer>();
            if (mapRenderer != null)
            {
                var material = mapRenderer.GetMaterial();
                if (material != null)
                {
                    textureManager.BindTexturesToMaterial(material);
                }
            }

            var mapSystemCoordinator = Object.FindFirstObjectByType<Core.MapSystemCoordinator>();
            if (mapSystemCoordinator != null && mapSystemCoordinator.MeshRenderer?.sharedMaterial != null)
            {
                textureManager.BindTexturesToMaterial(mapSystemCoordinator.MeshRenderer.sharedMaterial);
            }
        }
    }

    /// <summary>
    /// Legacy alias for HeightmapImageLoader for backward compatibility.
    /// </summary>
    public class HeightmapBitmapLoader : HeightmapImageLoader { }
}
