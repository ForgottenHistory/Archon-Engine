using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Utils;

namespace Map.Loading
{
    /// <summary>
    /// Loads terrain detail textures into Texture2DArray for GPU splatting
    /// Scans Assets/Data/textures/terrain_detail/ for {index}_{name}.png files
    ///
    /// Architecture:
    /// - File naming: {index}_{name}.png (e.g., "0_grasslands.png", "3_desert.png")
    /// - Index range: 0-255 (matches TerrainColorMapper terrain type indices)
    /// - Format: RGBA32 sRGB with mipmaps for performance
    /// - Missing indices: Filled with neutral gray (128,128,128,255) for multiply blend
    ///
    /// Moddability:
    /// - Drop PNG files in folder to add/replace detail textures
    /// - File names must match terrain type indices from TerrainColorMapper
    /// - Supports hot-reload (rebuild array when files change)
    /// </summary>
    public static class DetailTextureArrayLoader
    {
        private const string DetailTextureFolder = "Assets/Data/textures/terrain_detail";
        private const int DefaultTextureSize = 512;
        private const int MaxTerrainTypes = 256;

        /// <summary>
        /// Neutral gray detail texture (no effect when multiplied)
        /// Used for missing terrain type indices
        /// </summary>
        private static readonly Color32 NeutralGray = new Color32(128, 128, 128, 255);

        /// <summary>
        /// Load all detail textures into Texture2DArray
        /// </summary>
        /// <param name="textureSize">Resolution per texture (default 512x512)</param>
        /// <param name="logProgress">Enable progress logging</param>
        /// <returns>Texture2DArray with terrain detail textures</returns>
        public static Texture2DArray LoadDetailTextureArray(int textureSize = DefaultTextureSize, bool logProgress = true)
        {
            if (logProgress)
            {
                ArchonLogger.Log($"DetailTextureArrayLoader: Starting load from {DetailTextureFolder}", "map_initialization");
            }

            // Scan folder for detail texture files
            var detailFiles = ScanDetailTextureFiles();

            if (logProgress)
            {
                ArchonLogger.Log($"DetailTextureArrayLoader: Found {detailFiles.Count} detail texture files", "map_initialization");
            }

            // Determine layer count (max terrain type index + 1)
            int layerCount = DetermineLayerCount(detailFiles);

            if (logProgress)
            {
                ArchonLogger.Log($"DetailTextureArrayLoader: Creating Texture2DArray with {layerCount} layers at {textureSize}x{textureSize}", "map_initialization");
            }

            // Create Texture2DArray with explicit GraphicsFormat (follow explicit-graphics-format.md pattern)
            var textureArray = new Texture2DArray(
                textureSize,
                textureSize,
                layerCount,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB,  // sRGB for proper color
                UnityEngine.Experimental.Rendering.TextureCreationFlags.MipChain  // Mipmaps for performance
            );

            textureArray.name = "TerrainDetailArray";
            textureArray.filterMode = FilterMode.Bilinear;  // Smooth filtering
            textureArray.wrapMode = TextureWrapMode.Repeat;  // Tiled detail
            textureArray.anisoLevel = 8;  // High anisotropic filtering for sharp detail at angles

            // Populate texture array layers
            int loadedCount = 0;
            int missingCount = 0;

            for (int i = 0; i < layerCount; i++)
            {
                if (detailFiles.TryGetValue(i, out string filePath))
                {
                    // Load texture from file
                    LoadTextureIntoArray(textureArray, i, filePath, textureSize, logProgress);
                    loadedCount++;
                }
                else
                {
                    // Missing texture - fill with neutral gray
                    FillLayerWithNeutralGray(textureArray, i, textureSize);
                    missingCount++;
                }
            }

            // Apply changes (upload to GPU)
            textureArray.Apply(true, false);  // updateMipmaps=true, makeNoLongerReadable=false (keep CPU copy for hot-reload)

            if (logProgress)
            {
                ArchonLogger.Log($"DetailTextureArrayLoader: Complete - Loaded: {loadedCount}, Missing (neutral gray): {missingCount}", "map_initialization");
            }

            return textureArray;
        }

        /// <summary>
        /// Scan terrain_detail folder for {index}_{name}.png/jpg files
        /// Returns dictionary: terrain type index → file path
        /// </summary>
        private static Dictionary<int, string> ScanDetailTextureFiles()
        {
            var files = new Dictionary<int, string>();

            if (!Directory.Exists(DetailTextureFolder))
            {
                ArchonLogger.LogWarning($"DetailTextureArrayLoader: Folder not found: {DetailTextureFolder}", "map_initialization");
                return files;
            }

            // Support both PNG and JPG files
            var imageFiles = new List<string>();
            imageFiles.AddRange(Directory.GetFiles(DetailTextureFolder, "*.png"));
            imageFiles.AddRange(Directory.GetFiles(DetailTextureFolder, "*.jpg"));
            imageFiles.AddRange(Directory.GetFiles(DetailTextureFolder, "*.jpeg"));

            foreach (var filePath in imageFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);

                // Parse {index}_{name} pattern
                if (TryParseTerrainIndex(fileName, out int index))
                {
                    if (index >= 0 && index < MaxTerrainTypes)
                    {
                        files[index] = filePath;
                    }
                    else
                    {
                        ArchonLogger.LogWarning($"DetailTextureArrayLoader: Invalid terrain index {index} in file: {fileName} (must be 0-255)", "map_initialization");
                    }
                }
                else
                {
                    ArchonLogger.LogWarning($"DetailTextureArrayLoader: Skipping file with invalid naming: {fileName} (expected format: {{index}}_{{name}}.png)", "map_initialization");
                }
            }

            return files;
        }

        /// <summary>
        /// Parse terrain type index from filename (e.g., "3_desert" → 3)
        /// </summary>
        private static bool TryParseTerrainIndex(string fileName, out int index)
        {
            index = 0;

            int underscorePos = fileName.IndexOf('_');
            if (underscorePos <= 0)
            {
                return false;  // No underscore or underscore at start
            }

            string indexPart = fileName.Substring(0, underscorePos);
            return int.TryParse(indexPart, out index);
        }

        /// <summary>
        /// Determine layer count for Texture2DArray
        /// Uses max terrain type index + 1, with minimum of 8 layers
        /// </summary>
        private static int DetermineLayerCount(Dictionary<int, string> detailFiles)
        {
            if (detailFiles.Count == 0)
            {
                return 8;  // Minimum 8 layers for common terrain types
            }

            int maxIndex = detailFiles.Keys.Max();
            return Mathf.Max(maxIndex + 1, 8);  // At least 8 layers
        }

        /// <summary>
        /// Load PNG texture into Texture2DArray layer
        /// </summary>
        private static void LoadTextureIntoArray(Texture2DArray array, int layer, string filePath, int textureSize, bool logProgress)
        {
            // Load PNG file
            byte[] fileData = File.ReadAllBytes(filePath);
            var tempTexture = new Texture2D(2, 2);  // Dummy size, LoadImage will resize
            tempTexture.LoadImage(fileData);

            // Resize if needed
            if (tempTexture.width != textureSize || tempTexture.height != textureSize)
            {
                tempTexture = ResizeTexture(tempTexture, textureSize, textureSize);
            }

            // Copy pixels to array layer
            Color32[] pixels = tempTexture.GetPixels32();
            array.SetPixels32(pixels, layer);

            if (logProgress)
            {
                string fileName = Path.GetFileName(filePath);
                ArchonLogger.Log($"DetailTextureArrayLoader: Loaded layer {layer} from {fileName}", "map_initialization");
            }

            // Cleanup temp texture
            Object.DestroyImmediate(tempTexture);
        }

        /// <summary>
        /// Fill array layer with neutral gray (for missing terrain types)
        /// </summary>
        private static void FillLayerWithNeutralGray(Texture2DArray array, int layer, int textureSize)
        {
            var pixels = new Color32[textureSize * textureSize];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = NeutralGray;
            }

            array.SetPixels32(pixels, layer);
        }

        /// <summary>
        /// Resize texture to target dimensions
        /// Uses bilinear filtering for smooth results
        /// </summary>
        private static Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            var result = new Texture2D(targetWidth, targetHeight, source.format, false);
            Color[] pixels = new Color[targetWidth * targetHeight];

            float xRatio = (float)source.width / targetWidth;
            float yRatio = (float)source.height / targetHeight;

            for (int y = 0; y < targetHeight; y++)
            {
                for (int x = 0; x < targetWidth; x++)
                {
                    float u = x * xRatio;
                    float v = y * yRatio;
                    pixels[y * targetWidth + x] = source.GetPixelBilinear(u / source.width, v / source.height);
                }
            }

            result.SetPixels(pixels);
            result.Apply();

            return result;
        }

        /// <summary>
        /// Get list of loaded terrain type indices
        /// Useful for debugging and validation
        /// </summary>
        public static List<int> GetLoadedTerrainTypes()
        {
            var files = ScanDetailTextureFiles();
            return files.Keys.OrderBy(k => k).ToList();
        }

        /// <summary>
        /// Check if terrain type has detail texture available
        /// </summary>
        public static bool HasDetailTexture(int terrainTypeIndex)
        {
            var files = ScanDetailTextureFiles();
            return files.ContainsKey(terrainTypeIndex);
        }
    }
}
