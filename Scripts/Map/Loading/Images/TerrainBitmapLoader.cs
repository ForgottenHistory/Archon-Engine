using UnityEngine;
using Unity.Collections;
using Map.Rendering;
using Map.Rendering.Terrain;
using Utils;

namespace Map.Loading.Images
{
    /// <summary>
    /// Loads terrain.png (or terrain.bmp) and populates the terrain texture.
    /// Uses ImageParser for unified BMP/PNG support.
    /// Terrain colors from the image are used directly - TerrainRGBLookup converts to indices later.
    /// </summary>
    public class TerrainImageLoader
    {
        private MapTextureManager textureManager;
        private bool logProgress;

        public void Initialize(MapTextureManager textures, bool enableLogging = true)
        {
            textureManager = textures;
            logProgress = enableLogging;
        }

        /// <summary>
        /// Load terrain image and populate terrain texture.
        /// Tries terrain.png first, falls back to terrain.bmp.
        /// </summary>
        public void LoadAndPopulate(string mapDirectory)
        {
            if (textureManager == null || textureManager.ProvinceTerrainTexture == null)
            {
                ArchonLogger.LogError("TerrainImageLoader: Texture manager or terrain texture not available", "map_initialization");
                return;
            }

            // Try PNG first, then BMP
            string pngPath = System.IO.Path.Combine(mapDirectory, "terrain.png");
            string bmpPath = System.IO.Path.Combine(mapDirectory, "terrain.bmp");

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
                ArchonLogger.LogWarning($"TerrainImageLoader: No terrain image found (tried {pngPath} and {bmpPath})", "map_initialization");
                return;
            }

            if (logProgress)
            {
                ArchonLogger.Log($"TerrainImageLoader: Loading {imagePath}", "map_initialization");
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
                    ArchonLogger.LogError($"TerrainImageLoader: Failed to parse {imagePath}", "map_initialization");
                    return;
                }

                if (logProgress)
                {
                    ArchonLogger.Log($"TerrainImageLoader: Parsed {pixelData.Header.Width}x{pixelData.Header.Height} image, format={pixelData.Format}", "map_initialization");
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
                ArchonLogger.Log("TerrainImageLoader: Terrain texture populated", "map_initialization");
            }
        }

        private void PopulateTexture(ImageParser.ImagePixelData pixelData)
        {
            var terrainTexture = textureManager.ProvinceTerrainTexture;
            int width = terrainTexture.width;
            int height = terrainTexture.height;

            var pixels = new Color32[width * height];
            int successCount = 0;
            int failCount = 0;

            // Track unique colors for debugging
            var uniqueColors = new System.Collections.Generic.HashSet<int>();

            for (int y = 0; y < height && y < pixelData.Header.Height; y++)
            {
                for (int x = 0; x < width && x < pixelData.Header.Width; x++)
                {
                    int textureIndex = y * width + x;

                    if (ImageParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
                    {
                        pixels[textureIndex] = new Color32(r, g, b, 255);
                        successCount++;

                        // Track unique colors
                        int packed = (r << 16) | (g << 8) | b;
                        uniqueColors.Add(packed);
                    }
                    else
                    {
                        pixels[textureIndex] = new Color32(86, 124, 27, 255); // Default grasslands
                        failCount++;
                    }
                }
            }

            if (logProgress)
            {
                ArchonLogger.Log($"TerrainImageLoader: Read {successCount} pixels, {failCount} failed, {uniqueColors.Count} unique colors", "map_initialization");

                // Log unique colors found
                ArchonLogger.Log($"TerrainImageLoader: Unique terrain colors found:", "map_initialization");
                foreach (int packed in uniqueColors)
                {
                    byte r = (byte)((packed >> 16) & 0xFF);
                    byte g = (byte)((packed >> 8) & 0xFF);
                    byte b = (byte)(packed & 0xFF);
                    ArchonLogger.Log($"  RGB({r},{g},{b})", "map_initialization");
                }
            }

            // Apply to texture
            terrainTexture.SetPixels32(pixels);
            terrainTexture.Apply(false);
            GL.Flush();

            // Generate terrain type texture from colors
            textureManager.GenerateTerrainTypeTexture();

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
}
