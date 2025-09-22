using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ParadoxParser.Bitmap
{
    /// <summary>
    /// High-performance BMP file parser for Paradox map files
    /// Supports 24-bit and 32-bit RGB formats commonly used in game maps
    /// </summary>
    public static class BMPParser
    {
        /// <summary>
        /// BMP file header structure (14 bytes)
        /// </summary>
        public struct BMPFileHeader
        {
            public ushort Signature;      // 'BM' (0x4D42)
            public uint FileSize;         // Total file size in bytes
            public ushort Reserved1;      // Reserved (0)
            public ushort Reserved2;      // Reserved (0)
            public uint DataOffset;       // Offset to pixel data

            public bool IsValid => Signature == 0x4D42; // 'BM'
        }

        /// <summary>
        /// BMP info header structure (40 bytes for BITMAPINFOHEADER)
        /// </summary>
        public struct BMPInfoHeader
        {
            public uint HeaderSize;       // Size of this header (40)
            public int Width;             // Image width in pixels
            public int Height;            // Image height in pixels (positive = bottom-up)
            public ushort Planes;         // Number of color planes (1)
            public ushort BitsPerPixel;   // Bits per pixel (24 or 32)
            public uint Compression;      // Compression type (0 = none)
            public uint ImageSize;        // Size of image data (can be 0 for uncompressed)
            public int XPixelsPerMeter;   // Horizontal resolution
            public int YPixelsPerMeter;   // Vertical resolution
            public uint ColorsUsed;       // Number of colors in palette (0 = all)
            public uint ColorsImportant;  // Number of important colors (0 = all)

            public bool IsValid => HeaderSize >= 40 && Planes == 1 && (Compression == 0 || Compression == 3);
            public bool IsSupported => BitsPerPixel == 8 || BitsPerPixel == 24 || BitsPerPixel == 32;
            public int AbsoluteHeight => Math.Abs(Height);
            public bool IsBottomUp => Height > 0;
        }

        /// <summary>
        /// Complete BMP header information
        /// </summary>
        public struct BMPHeader
        {
            public BMPFileHeader FileHeader;
            public BMPInfoHeader InfoHeader;
            public bool Success;

            public bool IsValid => Success && FileHeader.IsValid && InfoHeader.IsValid && InfoHeader.IsSupported;
            public int Width => InfoHeader.Width;
            public int Height => InfoHeader.AbsoluteHeight;
            public int BitsPerPixel => InfoHeader.BitsPerPixel;
            public int BytesPerPixel => InfoHeader.BitsPerPixel / 8;
            public uint PixelDataOffset => FileHeader.DataOffset;
            public int RowStride => ((Width * InfoHeader.BitsPerPixel + 31) / 32) * 4; // 4-byte aligned
            public long PixelDataSize => (long)RowStride * Height;
        }

        /// <summary>
        /// Pixel data access result
        /// </summary>
        public struct BMPPixelData
        {
            public NativeSlice<byte> RawData;
            public BMPHeader Header;
            public bool Success;

            public void Dispose()
            {
                // RawData is a slice, no disposal needed
            }
        }

        /// <summary>
        /// Parse BMP header from file data
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BMPHeader ParseHeader(NativeArray<byte> fileData)
        {
            return ParseHeader(new NativeSlice<byte>(fileData));
        }

        /// <summary>
        /// Parse BMP header from file data
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BMPHeader ParseHeader(NativeSlice<byte> fileData)
        {
            if (fileData.Length < 54) // Minimum BMP size (14 + 40 bytes)
            {
                return new BMPHeader { Success = false };
            }

            unsafe
            {
                byte* dataPtr = (byte*)fileData.GetUnsafePtr();

                // Parse file header (14 bytes)
                    var fileHeader = new BMPFileHeader
                    {
                        Signature = *(ushort*)(dataPtr + 0),
                        FileSize = *(uint*)(dataPtr + 2),
                        Reserved1 = *(ushort*)(dataPtr + 6),
                        Reserved2 = *(ushort*)(dataPtr + 8),
                        DataOffset = *(uint*)(dataPtr + 10)
                    };

                    // Parse info header (40 bytes)
                    var infoHeader = new BMPInfoHeader
                    {
                        HeaderSize = *(uint*)(dataPtr + 14),
                        Width = *(int*)(dataPtr + 18),
                        Height = *(int*)(dataPtr + 22),
                        Planes = *(ushort*)(dataPtr + 26),
                        BitsPerPixel = *(ushort*)(dataPtr + 28),
                        Compression = *(uint*)(dataPtr + 30),
                        ImageSize = *(uint*)(dataPtr + 34),
                        XPixelsPerMeter = *(int*)(dataPtr + 38),
                        YPixelsPerMeter = *(int*)(dataPtr + 42),
                        ColorsUsed = *(uint*)(dataPtr + 46),
                        ColorsImportant = *(uint*)(dataPtr + 50)
                    };

                // Debug logging to understand actual header values
                UnityEngine.Debug.Log($"BMP Header Debug: Signature=0x{fileHeader.Signature:X4}, HeaderSize={infoHeader.HeaderSize}, Planes={infoHeader.Planes}, BitsPerPixel={infoHeader.BitsPerPixel}, Compression={infoHeader.Compression}");

                return new BMPHeader
                {
                    FileHeader = fileHeader,
                    InfoHeader = infoHeader,
                    Success = true
                };
            }
        }

        /// <summary>
        /// Get pixel data from BMP file
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BMPPixelData GetPixelData(NativeArray<byte> fileData, BMPHeader header)
        {
            return GetPixelData(new NativeSlice<byte>(fileData), header);
        }

        /// <summary>
        /// Get pixel data from BMP file
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BMPPixelData GetPixelData(NativeSlice<byte> fileData, BMPHeader header)
        {
            if (!header.IsValid)
            {
                return new BMPPixelData { Success = false };
            }

            if (fileData.Length < header.PixelDataOffset + header.PixelDataSize)
            {
                return new BMPPixelData { Success = false };
            }

            var pixelDataSlice = fileData.Slice((int)header.PixelDataOffset, (int)header.PixelDataSize);

            return new BMPPixelData
            {
                RawData = pixelDataSlice,
                Header = header,
                Success = true
            };
        }

        /// <summary>
        /// Get RGB color at specific pixel coordinates
        /// Handles 8-bit (grayscale), 24-bit and 32-bit formats
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetPixelRGB(BMPPixelData pixelData, int x, int y, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;

            if (!pixelData.Success || x < 0 || x >= pixelData.Header.Width || y < 0 || y >= pixelData.Header.Height)
                return false;

            var header = pixelData.Header;
            int bytesPerPixel = header.BytesPerPixel;

            // Calculate row (BMP can be bottom-up or top-down)
            int row = header.InfoHeader.IsBottomUp ? (header.Height - 1 - y) : y;

            // Calculate byte offset
            long byteOffset = (long)row * header.RowStride + (long)x * bytesPerPixel;

            if (byteOffset + bytesPerPixel > pixelData.RawData.Length)
                return false;

            unsafe
            {
                byte* dataPtr = (byte*)pixelData.RawData.GetUnsafePtr();
                byte* pixelPtr = dataPtr + byteOffset;

                if (bytesPerPixel == 1) // 8-bit grayscale
                {
                    // For 8-bit, treat as grayscale
                    r = g = b = pixelPtr[0];
                }
                else // 24-bit or 32-bit
                {
                    // BMP stores pixels as BGR (not RGB)
                    b = pixelPtr[0];
                    g = pixelPtr[1];
                    r = pixelPtr[2];
                    // Alpha channel (pixelPtr[3]) ignored for RGB
                }

                return true;
            }
        }

        /// <summary>
        /// Get packed RGB color (0xRRGGBB) at specific coordinates
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetPixelRGBPacked(BMPPixelData pixelData, int x, int y, out int rgb)
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
        /// Useful for building province ID mappings
        /// </summary>
        public static NativeHashSet<int> CollectUniqueColors(BMPPixelData pixelData, Allocator allocator)
        {
            var uniqueColors = new NativeHashSet<int>(1000, allocator);

            if (!pixelData.Success)
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
        /// Useful for finding province boundaries
        /// </summary>
        public static NativeList<PixelCoord> FindPixelsWithColor(BMPPixelData pixelData, int targetRGB, Allocator allocator)
        {
            var matchingPixels = new NativeList<PixelCoord>(100, allocator);

            if (!pixelData.Success)
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
    }

    /// <summary>
    /// Simple 2D integer vector for pixel coordinates
    /// </summary>
    public struct PixelCoord
    {
        public int x, y;

        public PixelCoord(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }
}