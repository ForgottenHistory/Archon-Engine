using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using ParadoxParser.Bitmap;

namespace Map.Rendering
{
    /// <summary>
    /// Loads province bitmap data and converts it to texture format for GPU rendering
    /// Handles province ID assignment and color mapping
    /// </summary>
    public static class ProvinceTextureLoader
    {
        /// <summary>
        /// Load province bitmap and populate texture manager with province data
        /// </summary>
        /// <param name="textureManager">Target texture manager</param>
        /// <param name="bitmapPath">Path to provinces.bmp file</param>
        /// <returns>Province mapping data</returns>
        public static ProvinceMapping LoadProvinceBitmap(MapTextureManager textureManager, string bitmapPath)
        {
            if (!File.Exists(bitmapPath))
            {
                Debug.LogError($"Province bitmap not found: {bitmapPath}");
                return null;
            }

            // Load the bitmap using BMPParser
            byte[] fileData = File.ReadAllBytes(bitmapPath);
            var nativeFileData = new NativeArray<byte>(fileData, Allocator.Temp);

            var bmpHeader = BMPParser.ParseHeader(nativeFileData);
            if (!bmpHeader.IsValid)
            {
                Debug.LogError($"Invalid BMP file: {bitmapPath}");
                nativeFileData.Dispose();
                return null;
            }

            var pixelData = BMPParser.GetPixelData(nativeFileData, bmpHeader);
            if (!pixelData.Success)
            {
                Debug.LogError($"Failed to read BMP pixel data: {bitmapPath}");
                nativeFileData.Dispose();
                return null;
            }

            // Create Unity texture from BMP data
            Texture2D tempTexture = new Texture2D(bmpHeader.Width, bmpHeader.Height, TextureFormat.RGB24, false);
            var colors = new Color32[bmpHeader.Width * bmpHeader.Height];

            for (int y = 0; y < bmpHeader.Height; y++)
            {
                for (int x = 0; x < bmpHeader.Width; x++)
                {
                    if (BMPParser.TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
                    {
                        colors[y * bmpHeader.Width + x] = new Color32(r, g, b, 255);
                    }
                }
            }

            tempTexture.SetPixels32(colors);
            tempTexture.Apply();

            nativeFileData.Dispose();

            // Validate dimensions
            if (tempTexture.width != textureManager.MapWidth || tempTexture.height != textureManager.MapHeight)
            {
                Debug.LogWarning($"Bitmap size ({tempTexture.width}x{tempTexture.height}) doesn't match texture manager ({textureManager.MapWidth}x{textureManager.MapHeight})");
                textureManager.ResizeTextures(tempTexture.width, tempTexture.height);
            }

            var mapping = ProcessBitmap(textureManager, tempTexture);

            Object.DestroyImmediate(tempTexture);
            return mapping;
        }

        /// <summary>
        /// Process the bitmap and create province ID mapping
        /// </summary>
        private static ProvinceMapping ProcessBitmap(MapTextureManager textureManager, Texture2D bitmap)
        {
            var mapping = new ProvinceMapping();
            var colorToID = new Dictionary<Color32, ushort>();
            var pixels = bitmap.GetPixels32();

            int width = bitmap.width;
            int height = bitmap.height;

            Debug.Log($"Processing bitmap: {width}x{height} ({pixels.Length} pixels)");

            // First pass: identify unique colors and assign IDs
            ushort nextProvinceID = 1; // Start from 1, reserve 0 for "no province"

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixelColor = pixels[i];

                // Skip if already processed
                if (colorToID.ContainsKey(pixelColor))
                    continue;

                // Assign new province ID
                colorToID[pixelColor] = nextProvinceID;
                mapping.AddProvince(nextProvinceID, pixelColor);

                nextProvinceID++;

                // Check for province limit (65,535 max for 16-bit)
                if (nextProvinceID >= 65535)
                {
                    Debug.LogError("Too many provinces! Maximum is 65,534.");
                    break;
                }
            }

            Debug.Log($"Found {colorToID.Count} unique provinces");

            // Debug: Show some sample province IDs and colors
            int sampleCount = 0;
            foreach (var kvp in colorToID)
            {
                if (sampleCount < 5)
                {
                    Debug.Log($"Province ID {kvp.Value}: Color RGB({kvp.Key.r}, {kvp.Key.g}, {kvp.Key.b})");
                    sampleCount++;
                }
            }

            // Second pass: populate textures
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = y * width + x;
                    Color32 pixelColor = pixels[pixelIndex];

                    if (colorToID.TryGetValue(pixelColor, out ushort provinceID))
                    {
                        // Set province ID in texture
                        textureManager.SetProvinceID(x, y, provinceID);

                        // ALWAYS set the display color to the actual RGB from the bitmap
                        // This is what we'll see in terrain mode - the actual province colors
                        textureManager.SetProvinceColor(x, y, pixelColor);

                        // For the palette, we can only fit 256 entries, so we'll use modulo
                        // But this is only for political mode - terrain mode uses ProvinceColorTexture directly
                        byte paletteIndex = (byte)(provinceID % 256);
                        if (paletteIndex > 0) // Skip index 0 as it might be reserved
                        {
                            textureManager.SetPaletteColor(paletteIndex, pixelColor);
                        }

                        // For owner texture, we'll use a simplified owner ID that fits in the palette
                        // But keep it non-zero so provinces show as "owned"
                        ushort ownerForPalette = (ushort)((provinceID % 255) + 1); // 1-255 range
                        textureManager.SetProvinceOwner(x, y, ownerForPalette);

                        // Update province pixel data
                        mapping.AddPixelToProvince(provinceID, x, y);
                    }
                }

                // Progress update for large maps
                if (y % 100 == 0)
                {
                    float progress = (float)y / height;
                    Debug.Log($"Processing bitmap: {progress * 100f:F1}% complete");
                }
            }

            // Apply all texture changes including palette
            textureManager.ApplyTextureChanges();
            textureManager.ApplyPaletteChanges();

            Debug.Log($"Bitmap processing complete: {mapping.ProvinceCount} provinces loaded");

            // Debug: Sample some pixels to verify province IDs were written
            Debug.Log("Sampling province IDs at various points:");
            for (int testY = 100; testY < height && testY < 500; testY += 100)
            {
                for (int testX = 100; testX < width && testX < 500; testX += 100)
                {
                    ushort testID = textureManager.GetProvinceID(testX, testY);
                    Color32 testColor = textureManager.ProvinceColorTexture.GetPixel(testX, testY);
                    Debug.Log($"  Pixel ({testX}, {testY}): Province ID = {testID}, Color = RGB({testColor.r}, {testColor.g}, {testColor.b})");
                    if (testID > 0) break; // Found a non-ocean province
                }
            }
            return mapping;
        }

        /// <summary>
        /// Update province colors for political map mode
        /// </summary>
        /// <param name="textureManager">Texture manager to update</param>
        /// <param name="mapping">Province mapping data</param>
        /// <param name="provinceColors">New colors for provinces</param>
        public static void UpdateProvinceColors(MapTextureManager textureManager, ProvinceMapping mapping, Dictionary<ushort, Color32> provinceColors)
        {
            foreach (var kvp in provinceColors)
            {
                ushort provinceID = kvp.Key;
                Color32 newColor = kvp.Value;

                if (!mapping.HasProvince(provinceID))
                    continue;

                // Update all pixels for this province
                var pixels = mapping.GetProvincePixels(provinceID);
                foreach (var pixel in pixels)
                {
                    textureManager.SetProvinceColor(pixel.x, pixel.y, newColor);
                }
            }

            textureManager.ApplyTextureChanges();
        }

        /// <summary>
        /// Update province owners
        /// </summary>
        public static void UpdateProvinceOwners(MapTextureManager textureManager, ProvinceMapping mapping, Dictionary<ushort, ushort> provinceOwners)
        {
            foreach (var kvp in provinceOwners)
            {
                ushort provinceID = kvp.Key;
                ushort ownerID = kvp.Value;

                if (!mapping.HasProvince(provinceID))
                    continue;

                // Update all pixels for this province
                var pixels = mapping.GetProvincePixels(provinceID);
                foreach (var pixel in pixels)
                {
                    textureManager.SetProvinceOwner(pixel.x, pixel.y, ownerID);
                }
            }

            textureManager.ApplyTextureChanges();
        }
    }

    /// <summary>
    /// Stores the mapping between province IDs, colors, and pixel coordinates
    /// </summary>
    public class ProvinceMapping
    {
        private Dictionary<ushort, ProvinceInfo> provinces = new Dictionary<ushort, ProvinceInfo>();
        private Dictionary<Color32, ushort> colorToID = new Dictionary<Color32, ushort>();

        public int ProvinceCount => provinces.Count;

        public void AddProvince(ushort id, Color32 identifierColor)
        {
            provinces[id] = new ProvinceInfo
            {
                ID = id,
                IdentifierColor = identifierColor,
                Pixels = new List<Vector2Int>()
            };

            colorToID[identifierColor] = id;
        }

        public void AddPixelToProvince(ushort provinceID, int x, int y)
        {
            if (provinces.TryGetValue(provinceID, out var province))
            {
                province.Pixels.Add(new Vector2Int(x, y));
            }
        }

        public bool HasProvince(ushort provinceID)
        {
            return provinces.ContainsKey(provinceID);
        }

        public List<Vector2Int> GetProvincePixels(ushort provinceID)
        {
            return provinces.TryGetValue(provinceID, out var province) ? province.Pixels : new List<Vector2Int>();
        }

        public Color32 GetProvinceIdentifierColor(ushort provinceID)
        {
            return provinces.TryGetValue(provinceID, out var province) ? province.IdentifierColor : Color.black;
        }

        public ushort GetProvinceByColor(Color32 color)
        {
            return colorToID.TryGetValue(color, out ushort id) ? id : (ushort)0;
        }

        public Dictionary<ushort, ProvinceInfo> GetAllProvinces()
        {
            return new Dictionary<ushort, ProvinceInfo>(provinces);
        }

        [System.Serializable]
        public class ProvinceInfo
        {
            public ushort ID;
            public Color32 IdentifierColor;
            public List<Vector2Int> Pixels;
            public int PixelCount => Pixels?.Count ?? 0;
        }
    }
}