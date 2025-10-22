using System.Threading.Tasks;
using Unity.Collections;
using ParadoxParser.Bitmap;
using ParadoxParser.CSV;
using System.IO;

namespace Map.Loading
{
    /// <summary>
    /// Wraps ProvinceMapParser to provide async loading interface for Map layer
    /// Bridges between ParadoxParser.Bitmap and Map layer expectations
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
            public bool Success;
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
        /// BMP data wrapper
        /// </summary>
        public struct BMPData
        {
            public int Width;
            public int Height;
            private BMPParser.BMPPixelData pixelData;

            public BMPPixelDataWrapper GetPixelData() => new BMPPixelDataWrapper { data = pixelData };

            public void Dispose()
            {
                if (pixelData.Success)
                    pixelData.Dispose();
            }

            public static BMPData FromPixelData(BMPParser.BMPPixelData data)
            {
                return new BMPData
                {
                    Width = data.Header.Width,
                    Height = data.Header.Height,
                    pixelData = data
                };
            }
        }

        /// <summary>
        /// Wrapper for pixel data access
        /// </summary>
        public struct BMPPixelDataWrapper
        {
            internal BMPParser.BMPPixelData data;

            /// <summary>
            /// Try to get RGB values at pixel coordinates
            /// </summary>
            public bool TryGetPixelRGB(int x, int y, out byte r, out byte g, out byte b)
            {
                return BMPParser.TryGetPixelRGB(data, x, y, out r, out g, out b);
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
        /// </summary>
        private ProvinceMapResult LoadProvinceMapSync(string bmpPath, string csvPath)
        {
            try
            {
                // Read BMP file
                if (!File.Exists(bmpPath))
                {
                    return new ProvinceMapResult
                    {
                        Success = false,
                        ErrorMessage = $"BMP file not found: {bmpPath}"
                    };
                }

                byte[] bmpBytes = File.ReadAllBytes(bmpPath);
                var bmpData = new NativeArray<byte>(bmpBytes, Allocator.Persistent);

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
                        // Parse with definitions
                        var parseResult = ProvinceMapParser.ParseProvinceMap(bmpData, csvData, Allocator.Persistent);

                        if (!parseResult.Success)
                        {
                            return new ProvinceMapResult
                            {
                                Success = false,
                                ErrorMessage = "Failed to parse province map with definitions"
                            };
                        }

                        return new ProvinceMapResult
                        {
                            Success = true,
                            BMPData = BMPData.FromPixelData(parseResult.PixelData),
                            ProvinceMappings = new ProvinceMappings
                            {
                                ColorToProvinceID = parseResult.ColorToProvinceID,
                                ProvinceIDToColor = parseResult.ProvinceIDToColor
                            },
                            Definitions = ConvertDefinitions(parseResult),
                            HasDefinitions = true,
                            ErrorMessage = string.Empty,
                            SourceBmpData = bmpData,
                            SourceCsvData = csvData
                        };
                    }
                    else
                    {
                        // Parse BMP only without definitions
                        var bmpHeader = BMPParser.ParseHeader(bmpData);
                        if (!bmpHeader.IsValid)
                        {
                            return new ProvinceMapResult
                            {
                                Success = false,
                                ErrorMessage = "Invalid BMP header"
                            };
                        }

                        var pixelData = BMPParser.GetPixelData(bmpData, bmpHeader);
                        if (!pixelData.Success)
                        {
                            return new ProvinceMapResult
                            {
                                Success = false,
                                ErrorMessage = "Failed to get BMP pixel data"
                            };
                        }

                        return new ProvinceMapResult
                        {
                            Success = true,
                            BMPData = BMPData.FromPixelData(pixelData),
                            ProvinceMappings = new ProvinceMappings
                            {
                                ColorToProvinceID = new NativeHashMap<int, int>(0, Allocator.Persistent),
                                ProvinceIDToColor = new NativeHashMap<int, int>(0, Allocator.Persistent)
                            },
                            HasDefinitions = false,
                            ErrorMessage = string.Empty,
                            SourceBmpData = bmpData
                        };
                    }
                }
                finally
                {
                    // NOTE: Do NOT dispose bmpData/csvData here!
                    // The returned ProvinceMapResult contains views into this memory.
                    // Caller must dispose the ProvinceMapResult which will clean up properly.
                }
            }
            catch (System.Exception ex)
            {
                return new ProvinceMapResult
                {
                    Success = false,
                    ErrorMessage = $"Exception during province map loading: {ex.Message}"
                };
            }
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
    }
}
