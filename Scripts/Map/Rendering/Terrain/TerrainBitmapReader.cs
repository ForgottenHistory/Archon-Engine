using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Utils;

namespace Map.Rendering.Terrain
{
    /// <summary>
    /// ENGINE: Reads terrain.bmp and converts RGB pixels to terrain type indices
    /// Handles both direct BMP palette reading and Unity Texture2D RGB conversion
    /// </summary>
    public class TerrainBitmapReader
    {
        private bool logProgress;

        public TerrainBitmapReader(bool logProgress = true)
        {
            this.logProgress = logProgress;
        }

        /// <summary>
        /// Read raw palette indices from an 8-bit indexed BMP file
        /// Bypasses Unity's texture import which converts palette indices to RGB
        /// Returns array of palette indices [y * width + x] = paletteIndex
        /// </summary>
        public uint[] ReadBmpPaletteIndices(string bmpPath, int expectedWidth, int expectedHeight)
        {
            try
            {
                if (!System.IO.File.Exists(bmpPath))
                {
                    ArchonLogger.LogError($"TerrainBitmapReader: BMP file not found: {bmpPath}", "map_rendering");
                    return null;
                }

                using (System.IO.FileStream fs = new System.IO.FileStream(bmpPath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                using (System.IO.BinaryReader reader = new System.IO.BinaryReader(fs))
                {
                    // Read BMP header
                    ushort signature = reader.ReadUInt16();
                    if (signature != 0x4D42) // "BM" in little-endian
                    {
                        ArchonLogger.LogError("TerrainBitmapReader: Not a valid BMP file (wrong signature)", "map_rendering");
                        return null;
                    }

                    reader.ReadUInt32(); // File size
                    reader.ReadUInt32(); // Reserved
                    uint dataOffset = reader.ReadUInt32(); // Offset to pixel data

                    // Read DIB header
                    uint headerSize = reader.ReadUInt32();
                    int width = reader.ReadInt32();
                    int height = reader.ReadInt32();
                    reader.ReadUInt16(); // Planes
                    ushort bitsPerPixel = reader.ReadUInt16();

                    if (bitsPerPixel != 8)
                    {
                        ArchonLogger.LogError($"TerrainBitmapReader: BMP must be 8-bit indexed (got {bitsPerPixel}-bit)", "map_rendering");
                        return null;
                    }

                    if (logProgress)
                    {
                        ArchonLogger.Log($"TerrainBitmapReader: Reading 8-bit BMP - {width}x{height}, data offset: {dataOffset}", "map_rendering");
                    }

                    // Read palette (256 entries, 4 bytes each: B, G, R, Reserved)
                    // Palette starts after DIB header
                    fs.Seek(14 + headerSize, System.IO.SeekOrigin.Begin);

                    var palette = new List<(byte r, byte g, byte b)>();
                    for (int i = 0; i < 256; i++)
                    {
                        byte b = reader.ReadByte();
                        byte g = reader.ReadByte();
                        byte r = reader.ReadByte();
                        reader.ReadByte(); // Reserved
                        palette.Add((r, g, b));
                    }

                    if (logProgress)
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        sb.AppendLine("TerrainBitmapReader: BMP Palette (first 20 entries):");
                        for (int i = 0; i < System.Math.Min(20, palette.Count); i++)
                        {
                            var (r, g, b) = palette[i];
                            sb.AppendLine($"  Index {i}: RGB({r}, {g}, {b})");
                        }
                        ArchonLogger.Log(sb.ToString(), "map_rendering");
                    }

                    // Skip to pixel data
                    fs.Seek(dataOffset, System.IO.SeekOrigin.Begin);

                    // Read pixel data (palette indices)
                    // BMP rows are bottom-up and padded to 4-byte boundaries
                    int rowSize = ((width * bitsPerPixel + 31) / 32) * 4;
                    uint[] indices = new uint[width * height];

                    for (int y = 0; y < height; y++)
                    {
                        // BMP is bottom-up, so read from bottom to top
                        int targetY = height - 1 - y;

                        for (int x = 0; x < width; x++)
                        {
                            byte paletteIndex = reader.ReadByte();
                            indices[targetY * width + x] = paletteIndex;
                        }

                        // Skip row padding
                        int padding = rowSize - width;
                        if (padding > 0)
                        {
                            reader.ReadBytes(padding);
                        }
                    }

                    if (logProgress)
                    {
                        ArchonLogger.Log($"TerrainBitmapReader: Successfully read {indices.Length} palette indices from BMP", "map_rendering");

                        // Sample some values
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        sb.Append("TerrainBitmapReader: BMP palette index samples - ");
                        for (int i = 0; i < 10; i++)
                        {
                            int idx = (indices.Length * i) / 10;
                            sb.Append($"[{idx}]={indices[idx]} ");
                        }
                        ArchonLogger.Log(sb.ToString(), "map_rendering");
                    }

                    return indices;
                }
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogError($"TerrainBitmapReader: Failed to read BMP file: {e.Message}", "map_rendering");
                return null;
            }
        }

        /// <summary>
        /// Convert RGB pixels from terrain texture to terrain type indices
        /// Uses TerrainRGBLookup to map RGB colors to terrain indices
        /// Returns array of terrain indices [y * width + x] = terrainTypeIndex
        /// </summary>
        public uint[] ConvertRGBToTerrainTypes(
            Texture2D terrainTexture,
            TerrainRGBLookup rgbLookup,
            int width,
            int height)
        {
            if (!rgbLookup.IsInitialized)
            {
                ArchonLogger.LogError("TerrainBitmapReader: TerrainRGBLookup not initialized!", "map_rendering");
                return null;
            }

            Color[] pixels = terrainTexture.GetPixels();
            uint[] terrainTypes = new uint[pixels.Length];
            int unmappedCount = 0;
            Dictionary<(byte r, byte g, byte b), int> unmappedColors = new Dictionary<(byte r, byte g, byte b), int>();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color pixel = pixels[i];
                byte r = (byte)(pixel.r * 255f + 0.5f);
                byte g = (byte)(pixel.g * 255f + 0.5f);
                byte b = (byte)(pixel.b * 255f + 0.5f);

                uint terrainType = rgbLookup.GetTerrainTypeIndex(r, g, b);
                terrainTypes[i] = terrainType;

                // Check if it was unmapped (defaults to 0)
                if (terrainType == 0)
                {
                    // Could be legitimately terrain 0, or unmapped
                    // For tracking purposes, assume unmapped if not in lookup
                    var dict = rgbLookup.GetRGBToTerrainDictionary();
                    if (!dict.ContainsKey((r, g, b)))
                    {
                        unmappedCount++;

                        // Track unmapped colors
                        if (!unmappedColors.ContainsKey((r, g, b)))
                        {
                            unmappedColors[(r, g, b)] = 0;
                        }
                        unmappedColors[(r, g, b)]++;
                    }
                }
            }

            if (unmappedCount > 0 && logProgress)
            {
                ArchonLogger.LogWarning($"TerrainBitmapReader: {unmappedCount} pixels had unmapped RGB colors (defaulted to index 0)", "map_rendering");

                // Log the unmapped colors
                ArchonLogger.Log($"TerrainBitmapReader: Found {unmappedColors.Count} unique unmapped RGB values:", "map_rendering");
                foreach (var kvp in unmappedColors.OrderByDescending(x => x.Value).Take(10))
                {
                    byte r = kvp.Key.r;
                    byte g = kvp.Key.g;
                    byte b = kvp.Key.b;
                    int count = kvp.Value;
                    ArchonLogger.Log($"  RGB({r},{g},{b}) - {count} pixels", "map_rendering");
                }
            }

            return terrainTypes;
        }
    }
}
