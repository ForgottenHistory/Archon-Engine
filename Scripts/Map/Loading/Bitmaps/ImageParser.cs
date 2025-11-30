using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine;

namespace Map.Loading.Bitmaps
{
    /// <summary>
    /// Unified image parser that automatically detects and parses BMP or PNG files
    /// Provides a common interface for both formats
    /// </summary>
    public static class ImageParser
    {
        /// <summary>
        /// Image format enumeration
        /// </summary>
        public enum ImageFormat
        {
            Unknown,
            BMP,
            PNG
        }

        /// <summary>
        /// Unified image header information
        /// </summary>
        public struct ImageHeader
        {
            public int Width;
            public int Height;
            public int BitsPerPixel;
            public ImageFormat Format;
            public bool Success;
            public bool HasPalette;

            // Original format-specific headers (only one is valid)
            public BMPParser.BMPHeader BMPHeader;
            public PNGParser.PNGHeader PNGHeader;

            public bool IsValid => Success && Width > 0 && Height > 0;
        }

        /// <summary>
        /// Unified pixel data result
        /// </summary>
        public struct ImagePixelData
        {
            public ImageFormat Format;
            public ImageHeader Header;
            public bool Success;

            // Only one of these is valid, depending on Format
            public BMPParser.BMPPixelData BMPData;
            public PNGParser.PNGPixelData PNGData;

            public int Width => Header.Width;
            public int Height => Header.Height;

            public void Dispose()
            {
                if (Format == ImageFormat.BMP)
                {
                    BMPData.Dispose();
                }
                else if (Format == ImageFormat.PNG)
                {
                    PNGData.Dispose();
                }
            }
        }

        /// <summary>
        /// Detect image format from file data
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ImageFormat DetectFormat(NativeArray<byte> fileData)
        {
            if (fileData.Length < 8) return ImageFormat.Unknown;

            // Check for PNG signature: 0x89 0x50 0x4E 0x47 0x0D 0x0A 0x1A 0x0A
            if (fileData[0] == 0x89 && fileData[1] == 0x50 && fileData[2] == 0x4E && fileData[3] == 0x47 &&
                fileData[4] == 0x0D && fileData[5] == 0x0A && fileData[6] == 0x1A && fileData[7] == 0x0A)
            {
                return ImageFormat.PNG;
            }

            // Check for BMP signature: 'BM' (0x42 0x4D)
            if (fileData[0] == 0x42 && fileData[1] == 0x4D)
            {
                return ImageFormat.BMP;
            }

            return ImageFormat.Unknown;
        }

        /// <summary>
        /// Parse image header (auto-detects format)
        /// </summary>
        public static ImageHeader ParseHeader(NativeArray<byte> fileData)
        {
            var format = DetectFormat(fileData);

            switch (format)
            {
                case ImageFormat.BMP:
                    var bmpHeader = BMPParser.ParseHeader(fileData);
                    return new ImageHeader
                    {
                        Width = bmpHeader.Width,
                        Height = bmpHeader.Height,
                        BitsPerPixel = bmpHeader.BitsPerPixel,
                        Format = ImageFormat.BMP,
                        Success = bmpHeader.IsValid,
                        HasPalette = bmpHeader.BitsPerPixel == 8,
                        BMPHeader = bmpHeader
                    };

                case ImageFormat.PNG:
                    var pngHeader = PNGParser.ParseHeader(fileData);
                    return new ImageHeader
                    {
                        Width = pngHeader.Width,
                        Height = pngHeader.Height,
                        BitsPerPixel = pngHeader.BitDepth * (pngHeader.ColorType == 2 ? 3 : pngHeader.ColorType == 6 ? 4 : 1),
                        Format = ImageFormat.PNG,
                        Success = pngHeader.IsValid && pngHeader.IsSupported,
                        HasPalette = pngHeader.HasPalette,
                        PNGHeader = pngHeader
                    };

                default:
                    return new ImageHeader { Success = false, Format = ImageFormat.Unknown };
            }
        }

        /// <summary>
        /// Parse entire image file (auto-detects format)
        /// </summary>
        public static ImagePixelData Parse(NativeArray<byte> fileData, Allocator allocator)
        {
            var format = DetectFormat(fileData);

            switch (format)
            {
                case ImageFormat.BMP:
                    var bmpHeader = BMPParser.ParseHeader(fileData);
                    if (!bmpHeader.IsValid)
                    {
                        return new ImagePixelData { Success = false, Format = ImageFormat.BMP };
                    }

                    var bmpPixelData = BMPParser.GetPixelData(fileData, bmpHeader);
                    return new ImagePixelData
                    {
                        Format = ImageFormat.BMP,
                        Header = new ImageHeader
                        {
                            Width = bmpHeader.Width,
                            Height = bmpHeader.Height,
                            BitsPerPixel = bmpHeader.BitsPerPixel,
                            Format = ImageFormat.BMP,
                            Success = bmpPixelData.Success,
                            HasPalette = bmpHeader.BitsPerPixel == 8,
                            BMPHeader = bmpHeader
                        },
                        Success = bmpPixelData.Success,
                        BMPData = bmpPixelData
                    };

                case ImageFormat.PNG:
                    var pngData = PNGParser.Parse(fileData, allocator);
                    return new ImagePixelData
                    {
                        Format = ImageFormat.PNG,
                        Header = new ImageHeader
                        {
                            Width = pngData.Header.Width,
                            Height = pngData.Header.Height,
                            BitsPerPixel = pngData.Header.BitDepth * (pngData.Header.ColorType == 2 ? 3 : pngData.Header.ColorType == 6 ? 4 : 1),
                            Format = ImageFormat.PNG,
                            Success = pngData.Success,
                            HasPalette = pngData.Header.HasPalette,
                            PNGHeader = pngData.Header
                        },
                        Success = pngData.Success,
                        PNGData = pngData
                    };

                default:
                    return new ImagePixelData { Success = false, Format = ImageFormat.Unknown };
            }
        }

        /// <summary>
        /// Get RGB color at specific pixel coordinates (works for both BMP and PNG)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetPixelRGB(ImagePixelData pixelData, int x, int y, out byte r, out byte g, out byte b)
        {
            switch (pixelData.Format)
            {
                case ImageFormat.BMP:
                    return BMPParser.TryGetPixelRGB(pixelData.BMPData, x, y, out r, out g, out b);

                case ImageFormat.PNG:
                    return PNGParser.TryGetPixelRGB(pixelData.PNGData, x, y, out r, out g, out b);

                default:
                    r = g = b = 0;
                    return false;
            }
        }

        /// <summary>
        /// Get packed RGB color (0xRRGGBB) at specific coordinates
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetPixelRGBPacked(ImagePixelData pixelData, int x, int y, out int rgb)
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
        public static NativeHashSet<int> CollectUniqueColors(ImagePixelData pixelData, Allocator allocator)
        {
            switch (pixelData.Format)
            {
                case ImageFormat.BMP:
                    return BMPParser.CollectUniqueColors(pixelData.BMPData, allocator);

                case ImageFormat.PNG:
                    return PNGParser.CollectUniqueColors(pixelData.PNGData, allocator);

                default:
                    return new NativeHashSet<int>(0, allocator);
            }
        }

        /// <summary>
        /// Find all pixels with a specific RGB color
        /// </summary>
        public static NativeList<PixelCoord> FindPixelsWithColor(ImagePixelData pixelData, int targetRGB, Allocator allocator)
        {
            switch (pixelData.Format)
            {
                case ImageFormat.BMP:
                    return BMPParser.FindPixelsWithColor(pixelData.BMPData, targetRGB, allocator);

                case ImageFormat.PNG:
                    return PNGParser.FindPixelsWithColor(pixelData.PNGData, targetRGB, allocator);

                default:
                    return new NativeList<PixelCoord>(0, allocator);
            }
        }

        /// <summary>
        /// Get the palette from an indexed image (8-bit BMP or indexed PNG)
        /// Returns null if image doesn't have a palette
        /// </summary>
        public static Color32[] GetPalette(NativeArray<byte> fileData, ImageHeader header)
        {
            switch (header.Format)
            {
                case ImageFormat.BMP:
                    return BMPParser.ExtractPalette(fileData, header.BMPHeader);

                case ImageFormat.PNG:
                    // Palette is already extracted during PNG parsing
                    // This method is mainly for consistency with BMP workflow
                    return null;

                default:
                    return null;
            }
        }
    }
}
