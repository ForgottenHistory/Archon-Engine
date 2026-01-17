using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Utils;

namespace Map.Loading.Images
{
    /// <summary>
    /// High-performance PNG file parser for map files
    /// Supports 8-bit indexed, 24-bit RGB, and 32-bit RGBA formats
    /// Uses managed decompression but NativeArray for pixel storage
    /// </summary>
    public static class PNGParser
    {
        // PNG signature bytes
        private static readonly byte[] PNG_SIGNATURE = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>
        /// PNG header information
        /// </summary>
        public struct PNGHeader
        {
            public int Width;
            public int Height;
            public byte BitDepth;        // 1, 2, 4, 8, or 16
            public byte ColorType;       // 0=grayscale, 2=RGB, 3=indexed, 4=grayscale+alpha, 6=RGBA
            public byte CompressionMethod;
            public byte FilterMethod;
            public byte InterlaceMethod;
            public bool IsSuccess;

            public bool IsValid => IsSuccess && Width > 0 && Height > 0 && CompressionMethod == 0;
            public bool IsSupported => BitDepth == 8 && (ColorType == 0 || ColorType == 2 || ColorType == 3 || ColorType == 6);
            public int BytesPerPixel => ColorType switch
            {
                0 => 1,  // Grayscale
                2 => 3,  // RGB
                3 => 1,  // Indexed (palette lookup)
                4 => 2,  // Grayscale + Alpha
                6 => 4,  // RGBA
                _ => 0
            };
            public bool HasPalette => ColorType == 3;
            public bool HasAlpha => ColorType == 4 || ColorType == 6;
        }

        /// <summary>
        /// PNG pixel data result - mirrors BMPParser.BMPPixelData for compatibility
        /// </summary>
        public struct PNGPixelData
        {
            public NativeArray<byte> DecodedPixels;  // Decompressed and unfiltered pixel data
            public PNGHeader Header;
            public Color32[] Palette;    // For indexed color images
            public bool IsSuccess;

            public void Dispose()
            {
                if (DecodedPixels.IsCreated) DecodedPixels.Dispose();
            }
        }

        /// <summary>
        /// Check if file data is a valid PNG
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPNG(NativeArray<byte> fileData)
        {
            if (fileData.Length < 8) return false;

            for (int i = 0; i < 8; i++)
            {
                if (fileData[i] != PNG_SIGNATURE[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Parse PNG header from file data
        /// </summary>
        public static PNGHeader ParseHeader(NativeArray<byte> fileData)
        {
            if (fileData.Length < 33 || !IsPNG(fileData)) // 8 sig + 25 minimum IHDR chunk
            {
                return new PNGHeader { IsSuccess = false };
            }

            // First chunk should be IHDR at offset 8
            // Chunk format: 4 bytes length, 4 bytes type, data, 4 bytes CRC
            int chunkLength = ReadInt32BE(fileData, 8);
            uint chunkType = ReadUInt32BE(fileData, 12);

            // IHDR = 0x49484452
            if (chunkType != 0x49484452 || chunkLength != 13)
            {
                return new PNGHeader { IsSuccess = false };
            }

            // IHDR data starts at offset 16
            return new PNGHeader
            {
                Width = ReadInt32BE(fileData, 16),
                Height = ReadInt32BE(fileData, 20),
                BitDepth = fileData[24],
                ColorType = fileData[25],
                CompressionMethod = fileData[26],
                FilterMethod = fileData[27],
                InterlaceMethod = fileData[28],
                IsSuccess = true
            };
        }

        /// <summary>
        /// Parse and decode entire PNG file
        /// </summary>
        public static PNGPixelData Parse(NativeArray<byte> fileData, Allocator allocator)
        {
            var header = ParseHeader(fileData);
            if (!header.IsValid || !header.IsSupported)
            {
                return new PNGPixelData { IsSuccess = false };
            }

            if (header.InterlaceMethod != 0)
            {
                // Interlaced PNGs not supported yet
                ArchonLogger.LogWarning("Interlaced PNG not supported", "map_rendering");
                return new PNGPixelData { IsSuccess = false };
            }

            try
            {
                // Collect all IDAT chunks and palette
                using var compressedData = CollectIDATChunks(fileData, Allocator.Temp);
                var palette = ExtractPalette(fileData, header);

                if (compressedData.Length == 0)
                {
                    return new PNGPixelData { IsSuccess = false };
                }

                // Decompress using zlib (skip 2-byte zlib header)
                byte[] decompressed = DecompressZlib(compressedData);
                if (decompressed == null)
                {
                    return new PNGPixelData { IsSuccess = false };
                }

                // Calculate expected size (includes filter byte per row)
                int bytesPerPixel = header.BytesPerPixel;
                int rowBytes = header.Width * bytesPerPixel;
                int expectedSize = header.Height * (1 + rowBytes); // +1 for filter byte

                if (decompressed.Length < expectedSize)
                {
                    return new PNGPixelData { IsSuccess = false };
                }

                // Unfilter and store in NativeArray
                var pixels = new NativeArray<byte>(header.Width * header.Height * bytesPerPixel, allocator);
                UnfilterImageData(decompressed, pixels, header);

                return new PNGPixelData
                {
                    DecodedPixels = pixels,
                    Header = header,
                    Palette = palette,
                    IsSuccess = true
                };
            }
            catch (Exception e)
            {
                ArchonLogger.LogError($"PNG parse error: {e.Message}", "map_rendering");
                return new PNGPixelData { IsSuccess = false };
            }
        }

        /// <summary>
        /// Get RGB color at specific pixel coordinates
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetPixelRGB(PNGPixelData pixelData, int x, int y, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;

            if (!pixelData.IsSuccess || x < 0 || x >= pixelData.Header.Width || y < 0 || y >= pixelData.Header.Height)
                return false;

            int bytesPerPixel = pixelData.Header.BytesPerPixel;
            int offset = (y * pixelData.Header.Width + x) * bytesPerPixel;

            if (offset + bytesPerPixel > pixelData.DecodedPixels.Length)
                return false;

            switch (pixelData.Header.ColorType)
            {
                case 0: // Grayscale
                    r = g = b = pixelData.DecodedPixels[offset];
                    break;

                case 2: // RGB
                    r = pixelData.DecodedPixels[offset];
                    g = pixelData.DecodedPixels[offset + 1];
                    b = pixelData.DecodedPixels[offset + 2];
                    break;

                case 3: // Indexed
                    if (pixelData.Palette != null)
                    {
                        int paletteIndex = pixelData.DecodedPixels[offset];
                        if (paletteIndex < pixelData.Palette.Length)
                        {
                            var color = pixelData.Palette[paletteIndex];
                            r = color.r;
                            g = color.g;
                            b = color.b;
                        }
                    }
                    break;

                case 6: // RGBA
                    r = pixelData.DecodedPixels[offset];
                    g = pixelData.DecodedPixels[offset + 1];
                    b = pixelData.DecodedPixels[offset + 2];
                    // Alpha at offset + 3 ignored for RGB
                    break;

                default:
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Get packed RGB color (0xRRGGBB) at specific coordinates
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetPixelRGBPacked(PNGPixelData pixelData, int x, int y, out int rgb)
        {
            if (TryGetPixelRGB(pixelData, x, y, out byte r, out byte g, out byte b))
            {
                rgb = (r << 16) | (g << 8) | b;
                return true;
            }

            rgb = 0;
            return false;
        }

        /// <summary>
        /// Scan all pixels and collect unique RGB values
        /// </summary>
        public static NativeHashSet<int> CollectUniqueColors(PNGPixelData pixelData, Allocator allocator)
        {
            var uniqueColors = new NativeHashSet<int>(1000, allocator);

            if (!pixelData.IsSuccess)
                return uniqueColors;

            int width = pixelData.Header.Width;
            int height = pixelData.Header.Height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (TryGetPixelRGBPacked(pixelData, x, y, out int rgb))
                    {
                        uniqueColors.Add(rgb);
                    }
                }
            }

            return uniqueColors;
        }

        /// <summary>
        /// Find all pixels with a specific RGB color
        /// </summary>
        public static NativeList<PixelCoord> FindPixelsWithColor(PNGPixelData pixelData, int targetRGB, Allocator allocator)
        {
            var matchingPixels = new NativeList<PixelCoord>(100, allocator);

            if (!pixelData.IsSuccess)
                return matchingPixels;

            int width = pixelData.Header.Width;
            int height = pixelData.Header.Height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (TryGetPixelRGBPacked(pixelData, x, y, out int rgb) && rgb == targetRGB)
                    {
                        matchingPixels.Add(new PixelCoord(x, y));
                    }
                }
            }

            return matchingPixels;
        }

        #region Private Helper Methods

        private static int ReadInt32BE(NativeArray<byte> data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        private static uint ReadUInt32BE(NativeArray<byte> data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static NativeList<byte> CollectIDATChunks(NativeArray<byte> fileData, Allocator allocator)
        {
            var result = new NativeList<byte>(fileData.Length / 2, allocator);
            int offset = 8; // Skip signature

            while (offset + 12 <= fileData.Length)
            {
                int chunkLength = ReadInt32BE(fileData, offset);
                uint chunkType = ReadUInt32BE(fileData, offset + 4);

                if (chunkLength < 0 || offset + 12 + chunkLength > fileData.Length)
                    break;

                // IDAT = 0x49444154
                if (chunkType == 0x49444154)
                {
                    for (int i = 0; i < chunkLength; i++)
                    {
                        result.Add(fileData[offset + 8 + i]);
                    }
                }

                // IEND = 0x49454E44
                if (chunkType == 0x49454E44)
                    break;

                offset += 12 + chunkLength; // 4 len + 4 type + data + 4 CRC
            }

            return result;
        }

        private static Color32[] ExtractPalette(NativeArray<byte> fileData, PNGHeader header)
        {
            if (!header.HasPalette) return null;

            int offset = 8; // Skip signature

            while (offset + 12 <= fileData.Length)
            {
                int chunkLength = ReadInt32BE(fileData, offset);
                uint chunkType = ReadUInt32BE(fileData, offset + 4);

                if (chunkLength < 0 || offset + 12 + chunkLength > fileData.Length)
                    break;

                // PLTE = 0x504C5445
                if (chunkType == 0x504C5445)
                {
                    int colorCount = chunkLength / 3;
                    var palette = new Color32[colorCount];

                    for (int i = 0; i < colorCount; i++)
                    {
                        int dataOffset = offset + 8 + i * 3;
                        palette[i] = new Color32(
                            fileData[dataOffset],
                            fileData[dataOffset + 1],
                            fileData[dataOffset + 2],
                            255
                        );
                    }

                    return palette;
                }

                // IEND = 0x49454E44
                if (chunkType == 0x49454E44)
                    break;

                offset += 12 + chunkLength;
            }

            return null;
        }

        private static byte[] DecompressZlib(NativeList<byte> compressedData)
        {
            if (compressedData.Length < 2) return null;

            // Convert to managed array for decompression
            byte[] compressed = new byte[compressedData.Length];
            for (int i = 0; i < compressedData.Length; i++)
            {
                compressed[i] = compressedData[i];
            }

            try
            {
                // Skip 2-byte zlib header
                using var inputStream = new MemoryStream(compressed, 2, compressed.Length - 2);
                using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
                using var outputStream = new MemoryStream();

                deflateStream.CopyTo(outputStream);
                return outputStream.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static void UnfilterImageData(byte[] filtered, NativeArray<byte> output, PNGHeader header)
        {
            int bytesPerPixel = header.BytesPerPixel;
            int rowBytes = header.Width * bytesPerPixel;
            int filteredRowBytes = rowBytes + 1; // +1 for filter type byte

            byte[] prevRow = new byte[rowBytes];
            byte[] currentRow = new byte[rowBytes];

            for (int y = 0; y < header.Height; y++)
            {
                int filteredOffset = y * filteredRowBytes;
                byte filterType = filtered[filteredOffset];

                // Copy current row data (excluding filter byte)
                for (int i = 0; i < rowBytes; i++)
                {
                    currentRow[i] = filtered[filteredOffset + 1 + i];
                }

                // Apply filter
                switch (filterType)
                {
                    case 0: // None
                        break;

                    case 1: // Sub
                        for (int i = bytesPerPixel; i < rowBytes; i++)
                        {
                            currentRow[i] = (byte)(currentRow[i] + currentRow[i - bytesPerPixel]);
                        }
                        break;

                    case 2: // Up
                        for (int i = 0; i < rowBytes; i++)
                        {
                            currentRow[i] = (byte)(currentRow[i] + prevRow[i]);
                        }
                        break;

                    case 3: // Average
                        for (int i = 0; i < rowBytes; i++)
                        {
                            int a = i >= bytesPerPixel ? currentRow[i - bytesPerPixel] : 0;
                            int b = prevRow[i];
                            currentRow[i] = (byte)(currentRow[i] + ((a + b) / 2));
                        }
                        break;

                    case 4: // Paeth
                        for (int i = 0; i < rowBytes; i++)
                        {
                            int a = i >= bytesPerPixel ? currentRow[i - bytesPerPixel] : 0;
                            int b = prevRow[i];
                            int c = i >= bytesPerPixel ? prevRow[i - bytesPerPixel] : 0;
                            currentRow[i] = (byte)(currentRow[i] + PaethPredictor(a, b, c));
                        }
                        break;
                }

                // Copy to output
                int outputOffset = y * rowBytes;
                for (int i = 0; i < rowBytes; i++)
                {
                    output[outputOffset + i] = currentRow[i];
                }

                // Swap rows
                var temp = prevRow;
                prevRow = currentRow;
                currentRow = temp;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int PaethPredictor(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);

            if (pa <= pb && pa <= pc) return a;
            if (pb <= pc) return b;
            return c;
        }

        #endregion
    }
}
