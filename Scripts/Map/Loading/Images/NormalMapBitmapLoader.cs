using UnityEngine;
using Unity.Collections;
using Map.Rendering;
using Utils;

namespace Map.Loading.Images
{
    /// <summary>
    /// Loads world_normal.png (or world_normal.bmp) and populates the normal map texture.
    /// Uses ImageParser for unified BMP/PNG support with PNG preferred.
    /// Normal map format: RGB(128,128,255) = flat surface (up direction)
    /// </summary>
    public class NormalMapImageLoader
    {
        private MapTextureManager textureManager;
        private bool logProgress;

        public void Initialize(MapTextureManager textures, bool enableLogging = true)
        {
            textureManager = textures;
            logProgress = enableLogging;
        }

        /// <summary>
        /// Load normal map image and populate normal map texture.
        /// Tries world_normal.png first, falls back to world_normal.bmp.
        /// </summary>
        public void LoadAndPopulate(string mapDirectory)
        {
            if (textureManager == null || textureManager.NormalMapTexture == null)
            {
                ArchonLogger.LogError("NormalMapImageLoader: Texture manager or normal map texture not available", "map_initialization");
                return;
            }

            // Try PNG first, then BMP
            string pngPath = System.IO.Path.Combine(mapDirectory, "world_normal.png");
            string bmpPath = System.IO.Path.Combine(mapDirectory, "world_normal.bmp");

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
                // Normal map is optional - will be generated from heightmap if not present
                if (logProgress)
                {
                    ArchonLogger.Log($"NormalMapImageLoader: No normal map image found (tried {pngPath} and {bmpPath}), will generate from heightmap", "map_initialization");
                }
                return;
            }

            if (logProgress)
            {
                ArchonLogger.Log($"NormalMapImageLoader: Loading {imagePath}", "map_initialization");
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
                    ArchonLogger.LogError($"NormalMapImageLoader: Failed to parse {imagePath}", "map_initialization");
                    return;
                }

                if (logProgress)
                {
                    ArchonLogger.Log($"NormalMapImageLoader: Parsed {pixelData.Header.Width}x{pixelData.Header.Height} image, format={pixelData.Format}", "map_initialization");
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
                ArchonLogger.Log("NormalMapImageLoader: Normal map texture populated", "map_initialization");
            }
        }

        private void PopulateTexture(ImageParser.ImagePixelData pixelData)
        {
            var normalMapTexture = textureManager.NormalMapTexture;
            int width = normalMapTexture.width;
            int height = normalMapTexture.height;

            // Create pixel array for normal map texture (RGB24 format)
            var pixels = new Color32[width * height];
            int successCount = 0;
            int failCount = 0;

            // Process 24-bit RGB normal map data
            // Normal maps encode surface normals as RGB where each channel represents X/Y/Z components
            for (int y = 0; y < height && y < pixelData.Header.Height; y++)
            {
                for (int x = 0; x < width && x < pixelData.Header.Width; x++)
                {
                    int textureIndex = y * width + x;

                    // Read RGB values from normal map (R=X, G=Y, B=Z)
                    if (ImageParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
                    {
                        // Store RGB directly (shader will decode to normal vector)
                        pixels[textureIndex] = new Color32(r, g, b, 255);
                        successCount++;
                    }
                    else
                    {
                        // Fallback to flat normal (up direction) if pixel reading fails: RGB(128, 128, 255)
                        pixels[textureIndex] = new Color32(128, 128, 255, 255);
                        failCount++;
                    }
                }
            }

            if (logProgress)
            {
                ArchonLogger.Log($"NormalMapImageLoader: Read {successCount} pixels, {failCount} failed", "map_initialization");

                // Sample some normal values to verify data
                if (successCount > 0)
                {
                    var n1 = pixels[width * height / 4];
                    var n2 = pixels[width * height / 2];
                    var n3 = pixels[width * height * 3 / 4];
                    ArchonLogger.Log($"NormalMapImageLoader: Normal samples - RGB[{n1.r},{n1.g},{n1.b}] RGB[{n2.r},{n2.g},{n2.b}] RGB[{n3.r},{n3.g},{n3.b}]", "map_initialization");
                }
            }

            // Apply normal data to texture
            normalMapTexture.SetPixels32(pixels);
            normalMapTexture.Apply(false);
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

            var coordinator = Object.FindFirstObjectByType<Rendering.MapRenderingCoordinator>();
            if (coordinator != null && coordinator.MapMaterial != null)
            {
                textureManager.BindTexturesToMaterial(coordinator.MapMaterial);
            }
        }
    }

    /// <summary>
    /// Legacy alias for NormalMapImageLoader for backward compatibility.
    /// </summary>
    public class NormalMapBitmapLoader : NormalMapImageLoader { }
}
