using System;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using ParadoxParser.Bitmap;

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
        /// Note: Runs synchronously on main thread due to NativeCollection thread safety
        /// </summary>
        public Task<BMPLoadResult> LoadBMPAsync(string bmpFilePath)
        {
            ReportProgress(0, 1, "Loading BMP file...");

            // Load BMP file data (synchronously to avoid NativeArray thread issues)
            byte[] fileBytes;
            try
            {
                fileBytes = File.ReadAllBytes(bmpFilePath);
            }
            catch (Exception ex)
            {
                return Task.FromResult(new BMPLoadResult { Success = false, ErrorMessage = $"Failed to load BMP file: {ex.Message}" });
            }

            // Use Allocator.Persistent because data survives >4 frames in async processing
            var fileData = new NativeArray<byte>(fileBytes, Allocator.Persistent);

            try
            {
                ReportProgress(0, 1, "Parsing BMP header...");

                // Parse BMP header
                var header = BMPParser.ParseHeader(fileData);
                if (!header.IsValid)
                {
                    return Task.FromResult(new BMPLoadResult { Success = false, ErrorMessage = "Invalid BMP header" });
                }

                // Get pixel data
                var pixelData = BMPParser.GetPixelData(fileData, header);
                if (!pixelData.Success)
                {
                    return Task.FromResult(new BMPLoadResult { Success = false, ErrorMessage = "Failed to extract pixel data" });
                }

                ReportProgress(0, 1, "Processing pixels with Burst jobs...");

                // Collect unique colors for any BMP type
                var uniqueColors = CollectUniqueColorsWithJobs(pixelData);

                // Extract palette for 8-bit indexed BMPs
                UnityEngine.Color32[] palette = null;
                if (header.BitsPerPixel == 8)
                {
                    palette = BMPParser.ExtractPalette(fileData, header);
                }

                // Create persistent copy of pixel data before disposing file data
                var persistentPixelData = CopyPixelDataToPersistentMemory(pixelData);

                var result = new BMPLoadResult
                {
                    Success = true,
                    PixelData = persistentPixelData,
                    UniqueColors = uniqueColors,
                    Width = header.Width,
                    Height = header.Height,
                    BitsPerPixel = header.BitsPerPixel,
                    Palette = palette
                };

                return Task.FromResult(result);
            }
            finally
            {
                if (fileData.IsCreated)
                    fileData.Dispose();
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
            // Use Allocator.Persistent because this data survives >4 frames in coroutine processing
            return BMPParser.CollectUniqueColors(pixelData, Allocator.Persistent);
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
            // Use Allocator.Persistent because data survives >4 frames in coroutine processing
            var persistentData = new NativeArray<byte>(originalData.RawData.Length, Allocator.Persistent);

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
        public UnityEngine.Color32[] Palette; // Palette for 8-bit indexed BMPs (null for 24/32-bit)

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