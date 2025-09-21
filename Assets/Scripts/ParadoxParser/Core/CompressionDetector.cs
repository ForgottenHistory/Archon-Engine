using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace ParadoxParser.Core
{
    public static class CompressionDetector
    {
        public static CompressionInfo DetectCompression(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return new CompressionInfo
                    {
                        IsValid = false,
                        ErrorMessage = "File not found"
                    };
                }

                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fileStream.Length < 16) // Need at least 16 bytes for magic number detection
                    {
                        return new CompressionInfo
                        {
                            IsValid = true,
                            CompressionType = CompressionType.None,
                            OriginalSize = fileStream.Length,
                            CompressedSize = fileStream.Length
                        };
                    }

                    // Read first 16 bytes for magic number detection
                    byte[] header = new byte[16];
                    fileStream.Read(header, 0, 16);

                    var compressionType = DetectCompressionFromHeader(header);
                    var info = new CompressionInfo
                    {
                        IsValid = true,
                        CompressionType = compressionType,
                        CompressedSize = fileStream.Length,
                        FileName = Path.GetFileName(filePath)
                    };

                    // Try to get original size for known formats
                    if (compressionType != CompressionType.None)
                    {
                        info.OriginalSize = EstimateOriginalSize(fileStream, compressionType);
                    }
                    else
                    {
                        info.OriginalSize = fileStream.Length;
                    }

                    return info;
                }
            }
            catch (Exception ex)
            {
                return new CompressionInfo
                {
                    IsValid = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public static CompressionInfo DetectCompressionFromBytes(NativeArray<byte> data)
        {
            if (!data.IsCreated || data.Length < 16)
            {
                return new CompressionInfo
                {
                    IsValid = false,
                    ErrorMessage = "Insufficient data"
                };
            }

            unsafe
            {
                byte* dataPtr = (byte*)data.GetUnsafeReadOnlyPtr();
                byte[] header = new byte[16];

                for (int i = 0; i < 16; i++)
                {
                    header[i] = dataPtr[i];
                }

                var compressionType = DetectCompressionFromHeader(header);
                return new CompressionInfo
                {
                    IsValid = true,
                    CompressionType = compressionType,
                    CompressedSize = data.Length,
                    OriginalSize = compressionType == CompressionType.None ? data.Length : -1
                };
            }
        }

        private static CompressionType DetectCompressionFromHeader(byte[] header)
        {
            // ZIP/PKZIP files
            if (header[0] == 0x50 && header[1] == 0x4B &&
                (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07))
            {
                return CompressionType.Zip;
            }

            // GZIP files
            if (header[0] == 0x1F && header[1] == 0x8B)
            {
                return CompressionType.GZip;
            }

            // RAR files
            if (header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72 && header[3] == 0x21)
            {
                return CompressionType.Rar;
            }

            // 7-Zip files
            if (header[0] == 0x37 && header[1] == 0x7A && header[2] == 0xBC && header[3] == 0xAF)
            {
                return CompressionType.SevenZip;
            }

            // BZip2 files
            if (header[0] == 0x42 && header[1] == 0x5A && header[2] == 0x68)
            {
                return CompressionType.BZip2;
            }

            // LZ4 files
            if (header[0] == 0x04 && header[1] == 0x22 && header[2] == 0x4D && header[3] == 0x18)
            {
                return CompressionType.LZ4;
            }

            // XZ files
            if (header[0] == 0xFD && header[1] == 0x37 && header[2] == 0x7A &&
                header[3] == 0x58 && header[4] == 0x5A && header[5] == 0x00)
            {
                return CompressionType.XZ;
            }

            // Zstandard files
            if (header[0] == 0x28 && header[1] == 0xB5 && header[2] == 0x2F && header[3] == 0xFD)
            {
                return CompressionType.Zstandard;
            }

            // Check for common Paradox file signatures (uncompressed)
            if (IsParadoxTextFile(header))
            {
                return CompressionType.None;
            }

            return CompressionType.Unknown;
        }

        private static bool IsParadoxTextFile(byte[] header)
        {
            // Check if it's likely a text file (not binary)
            // Check for common ASCII/UTF-8 text characters
            for (int i = 0; i < Math.Min(16, header.Length); i++)
            {
                byte b = header[i];
                // Allow printable ASCII, tabs, newlines, carriage returns
                if (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D)
                {
                    // Non-text character found (except for allowed whitespace)
                    if (b != 0x00) // Allow null terminators
                        return false;
                }
                // Reject high control characters that aren't valid UTF-8
                if (b > 0x7E && b < 0x80)
                {
                    return false;
                }
            }

            // If we get here, it's likely a text file
            // Check for common Paradox file patterns
            string headerText = System.Text.Encoding.UTF8.GetString(header, 0, Math.Min(16, header.Length));

            // Common Paradox file starts
            string[] paradoxPatterns = {
                "# ", "# Generated", "# Checksum",
                "version=", "checksum=", "date=",
                "country", "province", "culture",
                "localisation", "localization",
                "tag", "name", "id", "owner"  // Added common Paradox keywords
            };

            foreach (string pattern in paradoxPatterns)
            {
                if (headerText.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // If it looks like text but doesn't match specific patterns,
            // still consider it as uncompressed text (Paradox files are text-based)
            return true;
        }

        private static long EstimateOriginalSize(FileStream stream, CompressionType compressionType)
        {
            try
            {
                switch (compressionType)
                {
                    case CompressionType.GZip:
                        return GetGZipOriginalSize(stream);
                    case CompressionType.Zip:
                        return EstimateZipOriginalSize(stream);
                    default:
                        // For unknown compression types, estimate 2-4x compression ratio
                        return stream.Length * 3;
                }
            }
            catch
            {
                // If we can't determine original size, estimate
                return stream.Length * 2;
            }
        }

        private static long GetGZipOriginalSize(FileStream stream)
        {
            // GZIP stores original size in last 4 bytes
            if (stream.Length < 8) return stream.Length;

            stream.Seek(-4, SeekOrigin.End);
            byte[] sizeBytes = new byte[4];
            stream.Read(sizeBytes, 0, 4);

            return BitConverter.ToUInt32(sizeBytes, 0);
        }

        private static long EstimateZipOriginalSize(FileStream stream)
        {
            // Simple estimation for ZIP files (would need proper ZIP parsing for accuracy)
            return stream.Length * 3; // Assume ~3:1 compression ratio
        }

        public static bool IsCompressed(string filePath)
        {
            var info = DetectCompression(filePath);
            return info.IsValid && info.CompressionType != CompressionType.None;
        }

        public static bool SupportsDecompression(CompressionType compressionType)
        {
            return compressionType switch
            {
                CompressionType.GZip => true,
                CompressionType.Zip => true,
                CompressionType.BZip2 => false, // Would need external library
                CompressionType.LZ4 => false,   // Would need external library
                CompressionType.XZ => false,    // Would need external library
                CompressionType.Zstandard => false, // Would need external library
                CompressionType.Rar => false,   // Proprietary format
                CompressionType.SevenZip => false, // Would need external library
                _ => false
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CompressionInfo
    {
        public bool IsValid;
        public string ErrorMessage;
        public CompressionType CompressionType;
        public long CompressedSize;
        public long OriginalSize;
        public string FileName;

        public bool IsCompressed => CompressionType != CompressionType.None && CompressionType != CompressionType.Unknown;
        public float CompressionRatio => OriginalSize > 0 ? (float)CompressedSize / OriginalSize : 1.0f;
        public long SizeSaved => OriginalSize - CompressedSize;

        public override string ToString()
        {
            if (!IsValid)
                return $"Invalid: {ErrorMessage}";

            if (!IsCompressed)
                return $"Uncompressed: {CompressedSize} bytes";

            return $"{CompressionType}: {CompressedSize} bytes (from {OriginalSize} bytes, {CompressionRatio:P1} ratio)";
        }
    }

    public enum CompressionType : byte
    {
        None = 0,
        Unknown = 1,
        Zip = 2,
        GZip = 3,
        Rar = 4,
        SevenZip = 5,
        BZip2 = 6,
        LZ4 = 7,
        XZ = 8,
        Zstandard = 9
    }

    public static class CompressionUtilities
    {
        public static readonly string[] CompressedExtensions = {
            ".zip", ".gz", ".rar", ".7z", ".bz2", ".lz4", ".xz", ".zst"
        };

        public static bool HasCompressedExtension(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            return Array.Exists(CompressedExtensions, ext => ext == extension);
        }

        public static string GetCompressionName(CompressionType type)
        {
            return type switch
            {
                CompressionType.Zip => "ZIP",
                CompressionType.GZip => "GZIP",
                CompressionType.Rar => "RAR",
                CompressionType.SevenZip => "7-Zip",
                CompressionType.BZip2 => "BZip2",
                CompressionType.LZ4 => "LZ4",
                CompressionType.XZ => "XZ",
                CompressionType.Zstandard => "Zstandard",
                CompressionType.None => "None",
                _ => "Unknown"
            };
        }

        public static bool ShouldDecompress(CompressionInfo info)
        {
            return info.IsValid &&
                   info.IsCompressed &&
                   CompressionDetector.SupportsDecompression(info.CompressionType);
        }
    }
}