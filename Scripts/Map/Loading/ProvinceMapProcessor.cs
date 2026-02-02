using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Map.Loading.Images;
using Core.Loaders;
using System.IO;
using Utils;

namespace Map.Loading
{
    /// <summary>
    /// Wraps ProvinceMapParser to provide async loading interface for Map layer
    /// Bridges between ParadoxParser.Bitmap and Map layer expectations
    /// Supports both BMP and PNG image formats via auto-detection
    /// </summary>
    public class ProvinceMapProcessor
    {
        /// <summary>
        /// Processing progress event data
        /// </summary>
        public struct ProcessingProgress
        {
            public int ProcessedProvinces;
            public int TotalProvinces;
            public float ProgressPercentage;
            public string CurrentOperation;
        }

        public event System.Action<ProcessingProgress> OnProgressUpdate;
        /// <summary>
        /// Province map processing result for Map layer
        /// </summary>
        public struct ProvinceMapResult
        {
            public BMPData BMPData;
            public ProvinceMappings ProvinceMappings;
            public ProvinceDefinitions Definitions;
            public bool HasDefinitions;
            public bool IsSuccess;
            public string ErrorMessage;

            // Track original data arrays for disposal
            internal NativeArray<byte> SourceBmpData;
            internal NativeArray<byte> SourceCsvData;

            public void Dispose()
            {
                BMPData.Dispose();
                ProvinceMappings.Dispose();
                if (HasDefinitions)
                    Definitions.Dispose();

                // Dispose source arrays
                if (SourceBmpData.IsCreated)
                    SourceBmpData.Dispose();
                if (SourceCsvData.IsCreated)
                    SourceCsvData.Dispose();
            }
        }

        /// <summary>
        /// Image data wrapper (supports both BMP and PNG)
        /// Named BMPData for backward compatibility
        /// </summary>
        public struct BMPData
        {
            public int Width;
            public int Height;
            private BMPParser.BMPPixelData bmpPixelData;
            private ImageParser.ImagePixelData unifiedPixelData;
            private bool isUnified;

            public BMPPixelDataWrapper GetPixelData() => new BMPPixelDataWrapper
            {
                bmpData = bmpPixelData,
                unifiedData = unifiedPixelData,
                isUnified = isUnified
            };

            /// <summary>
            /// Get raw decoded pixel bytes and bytes-per-pixel for GPU upload.
            /// Currently only supports PNG (unified) format.
            /// Returns false for BMP format (not currently used).
            /// </summary>
            public bool TryGetRawPixelBytes(out Unity.Collections.NativeArray<byte> rawBytes, out int bytesPerPixel)
            {
                if (isUnified && unifiedPixelData.IsSuccess && unifiedPixelData.Format == Images.ImageParser.ImageFormat.PNG)
                {
                    rawBytes = unifiedPixelData.PNGData.DecodedPixels;
                    bytesPerPixel = unifiedPixelData.PNGData.Header.BytesPerPixel;
                    return true;
                }

                // BMP format not supported for GPU upload - would need BGR→RGB conversion + row padding handling
                rawBytes = default;
                bytesPerPixel = 0;
                return false;
            }

            public void Dispose()
            {
                if (isUnified)
                {
                    if (unifiedPixelData.IsSuccess)
                        unifiedPixelData.Dispose();
                }
                else
                {
                    if (bmpPixelData.IsSuccess)
                        bmpPixelData.Dispose();
                }
            }

            public static BMPData FromPixelData(BMPParser.BMPPixelData data)
            {
                return new BMPData
                {
                    Width = data.Header.Width,
                    Height = data.Header.Height,
                    bmpPixelData = data,
                    isUnified = false
                };
            }

            public static BMPData FromUnifiedPixelData(ImageParser.ImagePixelData data)
            {
                return new BMPData
                {
                    Width = data.Width,
                    Height = data.Height,
                    unifiedPixelData = data,
                    isUnified = true
                };
            }
        }

        /// <summary>
        /// Wrapper for pixel data access (supports both BMP and PNG)
        /// </summary>
        public struct BMPPixelDataWrapper
        {
            internal BMPParser.BMPPixelData bmpData;
            internal ImageParser.ImagePixelData unifiedData;
            internal bool isUnified;

            /// <summary>
            /// Try to get RGB values at pixel coordinates
            /// </summary>
            public bool TryGetPixelRGB(int x, int y, out byte r, out byte g, out byte b)
            {
                if (isUnified)
                {
                    return ImageParser.TryGetPixelRGB(unifiedData, x, y, out r, out g, out b);
                }
                return BMPParser.TryGetPixelRGB(bmpData, x, y, out r, out g, out b);
            }
        }

        /// <summary>
        /// Province color-to-ID mappings
        /// </summary>
        public struct ProvinceMappings
        {
            public NativeHashMap<int, int> ColorToProvinceID;
            public NativeHashMap<int, int> ProvinceIDToColor;

            public void Dispose()
            {
                if (ColorToProvinceID.IsCreated)
                    ColorToProvinceID.Dispose();
                if (ProvinceIDToColor.IsCreated)
                    ProvinceIDToColor.Dispose();
            }
        }

        /// <summary>
        /// Province definitions from CSV
        /// </summary>
        public struct ProvinceDefinitions
        {
            public NativeArray<ProvinceDefinition> AllDefinitions;

            public void Dispose()
            {
                if (AllDefinitions.IsCreated)
                    AllDefinitions.Dispose();
            }
        }

        public struct ProvinceDefinition
        {
            public int ProvinceID;
            public int R, G, B;
        }

        /// <summary>
        /// Load province map asynchronously
        /// Note: Runs on main thread due to NativeCollection thread safety requirements
        /// </summary>
        public Task<ProvinceMapResult> LoadProvinceMapAsync(string bmpPath, string csvPath)
        {
            // NativeCollections can't be created on arbitrary background threads
            // Run synchronously on main thread instead
            var result = LoadProvinceMapSync(bmpPath, csvPath);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Synchronous loading implementation
        /// Supports both BMP and PNG formats via auto-detection
        /// Uses raw pixel cache to skip PNG decompression on subsequent loads
        /// </summary>
        private ProvinceMapResult LoadProvinceMapSync(string imagePath, string csvPath)
        {
            try
            {
                // Try to find image file (support both .bmp and .png)
                string actualImagePath = FindImageFile(imagePath);

                if (actualImagePath == null)
                {
                    return new ProvinceMapResult
                    {
                        IsSuccess = false,
                        ErrorMessage = $"Image file not found: {imagePath} (tried .bmp and .png)"
                    };
                }

                // Read CSV file if provided
                NativeArray<byte> csvData = default;
                bool hasDefinitions = false;

                if (!string.IsNullOrEmpty(csvPath) && File.Exists(csvPath))
                {
                    byte[] csvBytes = File.ReadAllBytes(csvPath);
                    csvData = new NativeArray<byte>(csvBytes, Allocator.Persistent);
                    hasDefinitions = true;
                }

                // Try raw pixel cache first (skips PNG decompression + unfiltering)
                string cachePath = actualImagePath + ".pixels";
                float t0 = UnityEngine.Time.realtimeSinceStartup;
                ImageParser.ImagePixelData? cachedPixelData = TryLoadPixelCache(actualImagePath, cachePath);
                float t1 = UnityEngine.Time.realtimeSinceStartup;

                if (cachedPixelData.HasValue)
                {
                    ArchonLogger.Log($"ProvinceMapProcessor: Cache hit — read {cachedPixelData.Value.Width}x{cachedPixelData.Value.Height} pixels in {(t1 - t0) * 1000f:F0}ms from {cachePath}", "map_rendering");
                    var cacheResult = BuildResultFromPixelData(cachedPixelData.Value, csvData, hasDefinitions, default, csvData);
                    float t2 = UnityEngine.Time.realtimeSinceStartup;
                    ArchonLogger.Log($"ProvinceMapProcessor: CSV + mappings built in {(t2 - t1) * 1000f:F0}ms", "map_rendering");
                    return cacheResult;
                }

                // Cache miss — full PNG parse
                ArchonLogger.Log("ProvinceMapProcessor: Cache miss — loading PNG from disk", "map_rendering");
                float tRead0 = UnityEngine.Time.realtimeSinceStartup;
                byte[] imageBytes = File.ReadAllBytes(actualImagePath);
                var imageData = new NativeArray<byte>(imageBytes, Allocator.Persistent);
                float tRead1 = UnityEngine.Time.realtimeSinceStartup;
                ArchonLogger.Log($"ProvinceMapProcessor: File read + NativeArray copy in {(tRead1 - tRead0) * 1000f:F0}ms ({imageBytes.Length / (1024 * 1024)}MB)", "map_rendering");

                // Detect image format
                var format = ImageParser.DetectFormat(imageData);
                if (format == ImageParser.ImageFormat.Unknown)
                {
                    imageData.Dispose();
                    if (csvData.IsCreated) csvData.Dispose();
                    return new ProvinceMapResult
                    {
                        IsSuccess = false,
                        ErrorMessage = $"Unknown image format: {actualImagePath}"
                    };
                }

                ArchonLogger.Log($"ProvinceMapProcessor: Loading {format} image from {actualImagePath}", "map_rendering");

                try
                {
                    ProvinceMapResult result;
                    if (hasDefinitions)
                    {
                        var parseResult = ProvinceMapParser.ParseProvinceMapUnified(imageData, csvData, Allocator.Persistent);

                        if (!parseResult.IsSuccess)
                        {
                            return new ProvinceMapResult
                            {
                                IsSuccess = false,
                                ErrorMessage = "Failed to parse province map with definitions"
                            };
                        }

                        // Save pixel cache for next load (PNG only)
                        if (format == ImageParser.ImageFormat.PNG)
                            SavePixelCache(cachePath, parseResult.PixelData);

                        result = new ProvinceMapResult
                        {
                            IsSuccess = true,
                            BMPData = BMPData.FromUnifiedPixelData(parseResult.PixelData),
                            ProvinceMappings = new ProvinceMappings
                            {
                                ColorToProvinceID = parseResult.ColorToProvinceID,
                                ProvinceIDToColor = parseResult.ProvinceIDToColor
                            },
                            Definitions = ConvertUnifiedDefinitions(parseResult),
                            HasDefinitions = true,
                            ErrorMessage = string.Empty,
                            SourceBmpData = imageData,
                            SourceCsvData = csvData
                        };
                    }
                    else
                    {
                        var parseResult = ProvinceMapParser.ParseProvinceMapImageOnly(imageData, Allocator.Persistent);

                        if (!parseResult.IsSuccess)
                        {
                            return new ProvinceMapResult
                            {
                                IsSuccess = false,
                                ErrorMessage = $"Failed to parse {format} image"
                            };
                        }

                        // Save pixel cache for next load (PNG only)
                        if (format == ImageParser.ImageFormat.PNG)
                            SavePixelCache(cachePath, parseResult.PixelData);

                        result = new ProvinceMapResult
                        {
                            IsSuccess = true,
                            BMPData = BMPData.FromUnifiedPixelData(parseResult.PixelData),
                            ProvinceMappings = new ProvinceMappings
                            {
                                ColorToProvinceID = parseResult.ColorToProvinceID,
                                ProvinceIDToColor = parseResult.ProvinceIDToColor
                            },
                            HasDefinitions = false,
                            ErrorMessage = string.Empty,
                            SourceBmpData = imageData
                        };
                    }

                    return result;
                }
                finally
                {
                    // NOTE: Do NOT dispose imageData/csvData here!
                    // The returned ProvinceMapResult contains views into this memory.
                    // Caller must dispose the ProvinceMapResult which will clean up properly.
                }
            }
            catch (System.Exception ex)
            {
                return new ProvinceMapResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Exception during province map loading: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Build ProvinceMapResult from pre-parsed pixel data (cache hit path).
        /// CSV parsing still happens normally via ParseProvinceMapWithPixelData.
        /// </summary>
        private ProvinceMapResult BuildResultFromPixelData(
            ImageParser.ImagePixelData pixelData,
            NativeArray<byte> csvData,
            bool hasDefinitions,
            NativeArray<byte> imageData,
            NativeArray<byte> sourceCsvData)
        {
            if (hasDefinitions)
            {
                var parseResult = ProvinceMapParser.ParseProvinceMapWithPixelData(pixelData, csvData, Allocator.Persistent);

                if (!parseResult.IsSuccess)
                {
                    return new ProvinceMapResult
                    {
                        IsSuccess = false,
                        ErrorMessage = "Failed to parse province map with definitions (cached pixels)"
                    };
                }

                return new ProvinceMapResult
                {
                    IsSuccess = true,
                    BMPData = BMPData.FromUnifiedPixelData(parseResult.PixelData),
                    ProvinceMappings = new ProvinceMappings
                    {
                        ColorToProvinceID = parseResult.ColorToProvinceID,
                        ProvinceIDToColor = parseResult.ProvinceIDToColor
                    },
                    Definitions = ConvertUnifiedDefinitions(parseResult),
                    HasDefinitions = true,
                    ErrorMessage = string.Empty,
                    SourceBmpData = imageData,
                    SourceCsvData = sourceCsvData
                };
            }
            else
            {
                // No definitions — collect unique colors from cached pixels
                using var uniqueColors = ImageParser.CollectUniqueColors(pixelData, Allocator.Temp);

                var colorToProvinceID = new NativeHashMap<int, int>(uniqueColors.Count, Allocator.Persistent);
                var provinceIDToColor = new NativeHashMap<int, int>(uniqueColors.Count, Allocator.Persistent);

                foreach (var rgb in uniqueColors)
                {
                    colorToProvinceID.TryAdd(rgb, rgb);
                    provinceIDToColor.TryAdd(rgb, rgb);
                }

                return new ProvinceMapResult
                {
                    IsSuccess = true,
                    BMPData = BMPData.FromUnifiedPixelData(pixelData),
                    ProvinceMappings = new ProvinceMappings
                    {
                        ColorToProvinceID = colorToProvinceID,
                        ProvinceIDToColor = provinceIDToColor
                    },
                    HasDefinitions = false,
                    ErrorMessage = string.Empty,
                    SourceBmpData = imageData
                };
            }
        }

        // Raw pixel cache format:
        // [0-3]   Magic: "RPXL" (0x52, 0x50, 0x58, 0x4C)
        // [4-7]   Width (int32 LE)
        // [8-11]  Height (int32 LE)
        // [12]    BytesPerPixel (byte)
        // [13]    ColorType (byte)
        // [14]    BitDepth (byte)
        // [15]    Reserved (byte)
        // [16..]  Raw pixel data (Width * Height * BytesPerPixel bytes)
        private const int CACHE_HEADER_SIZE = 16;
        private static readonly byte[] CACHE_MAGIC = { 0x52, 0x50, 0x58, 0x4C }; // "RPXL"

        /// <summary>
        /// Try to load raw pixel cache. Returns null if cache is missing or stale.
        /// </summary>
        private ImageParser.ImagePixelData? TryLoadPixelCache(string imagePath, string cachePath)
        {
            if (!File.Exists(cachePath))
                return null;

            // Invalidate if source image is newer than cache
            if (File.GetLastWriteTimeUtc(imagePath) > File.GetLastWriteTimeUtc(cachePath))
            {
                ArchonLogger.Log("ProvinceMapProcessor: Pixel cache stale, will regenerate", "map_rendering");
                return null;
            }

            try
            {
                byte[] cacheBytes = File.ReadAllBytes(cachePath);

                if (cacheBytes.Length < CACHE_HEADER_SIZE)
                    return null;

                // Validate magic
                if (cacheBytes[0] != CACHE_MAGIC[0] || cacheBytes[1] != CACHE_MAGIC[1] ||
                    cacheBytes[2] != CACHE_MAGIC[2] || cacheBytes[3] != CACHE_MAGIC[3])
                    return null;

                int width = cacheBytes[4] | (cacheBytes[5] << 8) | (cacheBytes[6] << 16) | (cacheBytes[7] << 24);
                int height = cacheBytes[8] | (cacheBytes[9] << 8) | (cacheBytes[10] << 16) | (cacheBytes[11] << 24);
                int bytesPerPixel = cacheBytes[12];
                byte colorType = cacheBytes[13];
                byte bitDepth = cacheBytes[14];

                int expectedDataSize = width * height * bytesPerPixel;
                if (cacheBytes.Length != CACHE_HEADER_SIZE + expectedDataSize)
                {
                    ArchonLogger.LogWarning($"ProvinceMapProcessor: Pixel cache size mismatch (expected {CACHE_HEADER_SIZE + expectedDataSize}, got {cacheBytes.Length})", "map_rendering");
                    return null;
                }

                // Single bulk copy from managed array to NativeArray
                var decodedPixels = new NativeArray<byte>(expectedDataSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                unsafe
                {
                    fixed (byte* src = &cacheBytes[CACHE_HEADER_SIZE])
                    {
                        UnsafeUtility.MemCpy(decodedPixels.GetUnsafePtr(), src, expectedDataSize);
                    }
                }

                var pngHeader = new PNGParser.PNGHeader
                {
                    Width = width,
                    Height = height,
                    BitDepth = bitDepth,
                    ColorType = colorType,
                    CompressionMethod = 0,
                    FilterMethod = 0,
                    InterlaceMethod = 0,
                    IsSuccess = true
                };

                var pngData = new PNGParser.PNGPixelData
                {
                    DecodedPixels = decodedPixels,
                    Header = pngHeader,
                    Palette = null,
                    IsSuccess = true
                };

                var pixelData = new ImageParser.ImagePixelData
                {
                    Format = ImageParser.ImageFormat.PNG,
                    Header = new ImageParser.ImageHeader
                    {
                        Width = width,
                        Height = height,
                        BitsPerPixel = bitDepth * (colorType == 2 ? 3 : colorType == 6 ? 4 : 1),
                        Format = ImageParser.ImageFormat.PNG,
                        IsSuccess = true,
                        HasPalette = colorType == 3,
                        PNGHeader = pngHeader
                    },
                    IsSuccess = true,
                    PNGData = pngData
                };

                return pixelData;
            }
            catch (System.Exception e)
            {
                ArchonLogger.LogWarning($"ProvinceMapProcessor: Failed to load pixel cache: {e.Message}", "map_rendering");
                return null;
            }
        }

        /// <summary>
        /// Save decoded pixel data to cache file for fast loading next time.
        /// </summary>
        private void SavePixelCache(string cachePath, ImageParser.ImagePixelData pixelData)
        {
            if (pixelData.Format != ImageParser.ImageFormat.PNG || !pixelData.IsSuccess)
                return;

            try
            {
                var pngData = pixelData.PNGData;
                var header = pngData.Header;
                int dataSize = pngData.DecodedPixels.Length;

                using var stream = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);

                // Write header
                byte[] headerBuf = new byte[CACHE_HEADER_SIZE];
                headerBuf[0] = CACHE_MAGIC[0];
                headerBuf[1] = CACHE_MAGIC[1];
                headerBuf[2] = CACHE_MAGIC[2];
                headerBuf[3] = CACHE_MAGIC[3];
                headerBuf[4] = (byte)(header.Width);
                headerBuf[5] = (byte)(header.Width >> 8);
                headerBuf[6] = (byte)(header.Width >> 16);
                headerBuf[7] = (byte)(header.Width >> 24);
                headerBuf[8] = (byte)(header.Height);
                headerBuf[9] = (byte)(header.Height >> 8);
                headerBuf[10] = (byte)(header.Height >> 16);
                headerBuf[11] = (byte)(header.Height >> 24);
                headerBuf[12] = (byte)header.BytesPerPixel;
                headerBuf[13] = header.ColorType;
                headerBuf[14] = header.BitDepth;
                headerBuf[15] = 0; // Reserved
                stream.Write(headerBuf, 0, CACHE_HEADER_SIZE);

                // Write pixel data in 1MB chunks — avoids allocating 292MB managed array
                unsafe
                {
                    byte* src = (byte*)pngData.DecodedPixels.GetUnsafeReadOnlyPtr();
                    byte[] writeBuffer = new byte[1024 * 1024];
                    int totalWritten = 0;
                    while (totalWritten < dataSize)
                    {
                        int toWrite = System.Math.Min(writeBuffer.Length, dataSize - totalWritten);
                        fixed (byte* dst = writeBuffer)
                        {
                            UnsafeUtility.MemCpy(dst, src + totalWritten, toWrite);
                        }
                        stream.Write(writeBuffer, 0, toWrite);
                        totalWritten += toWrite;
                    }
                }

                ArchonLogger.Log($"ProvinceMapProcessor: Saved pixel cache ({dataSize / (1024 * 1024)}MB) to {cachePath}", "map_rendering");
            }
            catch (System.Exception e)
            {
                // Cache save failure is non-fatal — next load will just decompress PNG again
                ArchonLogger.LogWarning($"ProvinceMapProcessor: Failed to save pixel cache: {e.Message}", "map_rendering");
            }
        }

        /// <summary>
        /// Find image file, trying both original path and alternate extensions
        /// </summary>
        private string FindImageFile(string imagePath)
        {
            // Try exact path first
            if (File.Exists(imagePath))
                return imagePath;

            // Get base path without extension
            string directory = Path.GetDirectoryName(imagePath);
            string baseName = Path.GetFileNameWithoutExtension(imagePath);

            // Try PNG
            string pngPath = Path.Combine(directory, baseName + ".png");
            if (File.Exists(pngPath))
                return pngPath;

            // Try BMP
            string bmpPath = Path.Combine(directory, baseName + ".bmp");
            if (File.Exists(bmpPath))
                return bmpPath;

            return null;
        }

        private ProvinceDefinitions ConvertDefinitions(ProvinceMapParser.ProvinceMapResult parseResult)
        {
            var definitions = new NativeArray<ProvinceDefinition>(parseResult.UniqueProvinceIDs.Length, Allocator.Persistent);

            for (int i = 0; i < parseResult.UniqueProvinceIDs.Length; i++)
            {
                int provinceID = parseResult.UniqueProvinceIDs[i];
                if (parseResult.ProvinceIDToColor.TryGetValue(provinceID, out int rgb))
                {
                    definitions[i] = new ProvinceDefinition
                    {
                        ProvinceID = provinceID,
                        R = (rgb >> 16) & 0xFF,
                        G = (rgb >> 8) & 0xFF,
                        B = rgb & 0xFF
                    };
                }
            }

            // Dispose temporary UniqueProvinceIDs array (data now copied to definitions)
            parseResult.UniqueProvinceIDs.Dispose();

            return new ProvinceDefinitions { AllDefinitions = definitions };
        }

        private ProvinceDefinitions ConvertUnifiedDefinitions(ProvinceMapParser.UnifiedProvinceMapResult parseResult)
        {
            var definitions = new NativeArray<ProvinceDefinition>(parseResult.UniqueProvinceIDs.Length, Allocator.Persistent);

            for (int i = 0; i < parseResult.UniqueProvinceIDs.Length; i++)
            {
                int provinceID = parseResult.UniqueProvinceIDs[i];
                if (parseResult.ProvinceIDToColor.TryGetValue(provinceID, out int rgb))
                {
                    definitions[i] = new ProvinceDefinition
                    {
                        ProvinceID = provinceID,
                        R = (rgb >> 16) & 0xFF,
                        G = (rgb >> 8) & 0xFF,
                        B = rgb & 0xFF
                    };
                }
            }

            // Dispose temporary UniqueProvinceIDs array (data now copied to definitions)
            parseResult.UniqueProvinceIDs.Dispose();

            return new ProvinceDefinitions { AllDefinitions = definitions };
        }
    }
}
