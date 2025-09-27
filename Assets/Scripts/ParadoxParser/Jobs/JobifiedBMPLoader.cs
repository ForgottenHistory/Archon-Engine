using System;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using ParadoxParser.Bitmap;
using ParadoxParser.Core;

namespace ParadoxParser.Jobs
{
    /// <summary>
    /// High-performance generic BMP loader using Unity's Burst job system
    /// Loads any BMP file (provinces, heightmaps, terrain, rivers, etc.) with optimal performance
    /// Use ProvinceMapProcessor for province-specific processing with definition.csv
    /// </summary>
    public class JobifiedBMPLoader
    {
        public struct LoadingProgress
        {
            public int FilesProcessed;
            public int TotalFiles;
            public float ProgressPercentage;
            public string CurrentOperation;
        }

        public event System.Action<LoadingProgress> OnProgressUpdate;

        private const int BATCH_SIZE = 1024; // Pixels per batch for parallel processing

        /// <summary>
        /// Load and process BMP file with Burst jobs for optimal performance
        /// Generic method that works with any BMP file type (provinces, heightmaps, terrain, etc.)
        /// </summary>
        public async Task<BMPLoadResult> LoadBMPAsync(string bmpFilePath)
        {
            ReportProgress(0, 1, "Loading BMP file...");

            // Load BMP file data
            var fileResult = await AsyncFileReader.ReadFileAsync(bmpFilePath, Allocator.TempJob);
            if (!fileResult.Success)
            {
                return new BMPLoadResult { Success = false, ErrorMessage = "Failed to load BMP file" };
            }

            try
            {
                ReportProgress(0, 1, "Parsing BMP header...");

                // Parse BMP header
                var header = BMPParser.ParseHeader(fileResult.Data);
                if (!header.IsValid)
                {
                    return new BMPLoadResult { Success = false, ErrorMessage = "Invalid BMP header" };
                }

                // Get pixel data
                var pixelData = BMPParser.GetPixelData(fileResult.Data, header);
                if (!pixelData.Success)
                {
                    return new BMPLoadResult { Success = false, ErrorMessage = "Failed to extract pixel data" };
                }

                ReportProgress(0, 1, "Processing pixels with Burst jobs...");

                // Collect unique colors for any BMP type
                var uniqueColors = CollectUniqueColorsWithJobs(pixelData);

                // Create persistent copy of pixel data before disposing file data
                var persistentPixelData = CopyPixelDataToPersistentMemory(pixelData);

                return new BMPLoadResult
                {
                    Success = true,
                    PixelData = persistentPixelData,
                    UniqueColors = uniqueColors,
                    Width = header.Width,
                    Height = header.Height,
                    BitsPerPixel = header.BitsPerPixel
                };
            }
            finally
            {
                fileResult.Dispose();
            }
        }


        /// <summary>
        /// Collect unique colors using optimized single-threaded approach
        /// For most BMP files, this is more efficient than job overhead
        /// </summary>
        private NativeHashSet<int> CollectUniqueColorsWithJobs(BMPParser.BMPPixelData pixelData)
        {
            // For BMP parsing, single-threaded is often faster due to job overhead
            // The main performance benefit comes from Burst compilation of BMPParser methods
            return BMPParser.CollectUniqueColors(pixelData, Allocator.TempJob);
        }


        /// <summary>
        /// Find pixels with specific color using Burst-optimized approach
        /// </summary>
        public NativeList<PixelCoord> FindPixelsWithColorJob(BMPParser.BMPPixelData pixelData, int targetRGB)
        {
            // Use existing Burst-optimized method which is already very fast
            return BMPParser.FindPixelsWithColor(pixelData, targetRGB, Allocator.TempJob);
        }

        /// <summary>
        /// Copy pixel data to persistent memory allocation to avoid disposal issues
        /// </summary>
        private PersistentBMPPixelData CopyPixelDataToPersistentMemory(BMPParser.BMPPixelData originalData)
        {
            if (!originalData.Success || originalData.RawData.Length == 0)
                return new PersistentBMPPixelData { Success = false };

            // Create a persistent copy of the pixel data
            var persistentData = new NativeArray<byte>(originalData.RawData.Length, Allocator.TempJob);

            // Copy slice data to the new array
            originalData.RawData.CopyTo(persistentData);

            return new PersistentBMPPixelData
            {
                RawData = persistentData,
                Header = originalData.Header,
                Success = true
            };
        }

        private void ReportProgress(int current, int total, string operation)
        {
            OnProgressUpdate?.Invoke(new LoadingProgress
            {
                FilesProcessed = current,
                TotalFiles = total,
                ProgressPercentage = total > 0 ? (float)current / total : 0f,
                CurrentOperation = operation
            });
        }
    }

    /// <summary>
    /// Generic result of BMP file loading (works for any BMP type)
    /// </summary>
    public struct BMPLoadResult
    {
        public bool Success;
        public string ErrorMessage;
        public PersistentBMPPixelData PixelData;
        public NativeHashSet<int> UniqueColors;
        public int Width;
        public int Height;
        public int BitsPerPixel;

        /// <summary>
        /// Get pixel data as BMPPixelData for processing
        /// </summary>
        public BMPParser.BMPPixelData GetPixelData()
        {
            if (Success && PixelData.Success)
            {
                return new BMPParser.BMPPixelData
                {
                    RawData = new NativeSlice<byte>(PixelData.RawData),
                    Header = PixelData.Header,
                    Success = true
                };
            }
            return new BMPParser.BMPPixelData { Success = false };
        }

        public void Dispose()
        {
            PixelData.Dispose();
            if (UniqueColors.IsCreated) UniqueColors.Dispose();
        }
    }

    /// <summary>
    /// Persistent pixel data that owns its memory allocation
    /// </summary>
    public struct PersistentBMPPixelData
    {
        public NativeArray<byte> RawData;
        public BMPParser.BMPHeader Header;
        public bool Success;

        public void Dispose()
        {
            if (RawData.IsCreated) RawData.Dispose();
        }
    }
}