using System.Threading.Tasks;
using Unity.Collections;
using Map.Loading.Bitmaps;
using ParadoxParser.CSV;
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

                byte[] imageBytes = File.ReadAllBytes(actualImagePath);
                var imageData = new NativeArray<byte>(imageBytes, Allocator.Persistent);

                // Detect image format
                var format = ImageParser.DetectFormat(imageData);
                if (format == ImageParser.ImageFormat.Unknown)
                {
                    imageData.Dispose();
                    return new ProvinceMapResult
                    {
                        IsSuccess = false,
                        ErrorMessage = $"Unknown image format: {actualImagePath}"
                    };
                }

                ArchonLogger.Log($"ProvinceMapProcessor: Loading {format} image from {actualImagePath}", "map_rendering");

                // Read CSV file if provided
                NativeArray<byte> csvData = default;
                bool hasDefinitions = false;

                if (!string.IsNullOrEmpty(csvPath) && File.Exists(csvPath))
                {
                    byte[] csvBytes = File.ReadAllBytes(csvPath);
                    csvData = new NativeArray<byte>(csvBytes, Allocator.Persistent);
                    hasDefinitions = true;
                }

                try
                {
                    if (hasDefinitions)
                    {
                        // Parse with definitions using unified parser
                        var parseResult = ProvinceMapParser.ParseProvinceMapUnified(imageData, csvData, Allocator.Persistent);

                        if (!parseResult.IsSuccess)
                        {
                            return new ProvinceMapResult
                            {
                                IsSuccess = false,
                                ErrorMessage = "Failed to parse province map with definitions"
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
                            SourceCsvData = csvData
                        };
                    }
                    else
                    {
                        // Parse image only without definitions using unified parser
                        var parseResult = ProvinceMapParser.ParseProvinceMapImageOnly(imageData, Allocator.Persistent);

                        if (!parseResult.IsSuccess)
                        {
                            return new ProvinceMapResult
                            {
                                IsSuccess = false,
                                ErrorMessage = $"Failed to parse {format} image"
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
                            HasDefinitions = false,
                            ErrorMessage = string.Empty,
                            SourceBmpData = imageData
                        };
                    }
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
