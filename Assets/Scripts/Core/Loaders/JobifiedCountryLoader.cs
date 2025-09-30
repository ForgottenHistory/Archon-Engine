using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using UnityEngine;
using Unity.Collections;
using ParadoxParser.Data;
using ParadoxParser.Jobs;
using Core.Data;
using static ParadoxParser.Utilities.ParadoxDataExtractionHelpers;

namespace Core.Loaders
{
    /// <summary>
    /// High-performance country loader using Unity Job System
    /// Replaces async/await with proper Unity threading for massive performance gains
    /// </summary>
    public class JobifiedCountryLoader
    {
        // Configuration constants
        private const int BATCH_SIZE = 15;
        private const int FILES_PER_JOB_BATCH = 4;  // How many files per parallel batch
        private const int ESTIMATED_KV_PER_FILE = 30;
        private const int ESTIMATED_BLOCKS_PER_FILE = 60;
        private const int PROGRESS_INTERVAL = 50;

        // Performance tracking
        private readonly Stopwatch globalStopwatch = new();
        private readonly List<string> globalErrors = new();

        /// <summary>
        /// Event for progress reporting during loading
        /// </summary>
        public event Action<LoadingProgress> OnProgressUpdate;

        /// <summary>
        /// Progress information structure
        /// </summary>
        public struct LoadingProgress
        {
            public int FilesProcessed;
            public int TotalFiles;
            public int BatchesCompleted;
            public int TotalBatches;
            public long ElapsedMs;
            public long MemoryUsageMB;
            public int ErrorCount;
            public string CurrentOperation;
            public float ProgressPercentage => TotalFiles > 0 ? (float)FilesProcessed / TotalFiles : 0f;
        }

        /// <summary>
        /// Load all country files using hybrid JSON5 + Burst architecture
        /// </summary>
        public CountryDataLoadResult LoadAllCountriesJob(string countriesDirectory = "Assets/Data")
        {
            globalStopwatch.Restart();
            globalErrors.Clear();

            ReportProgress("Initializing hybrid JSON5 + Burst country loader...");

            try
            {
                // Use the new hybrid JSON5 + Burst loader
                var result = BurstCountryLoader.LoadAllCountries(countriesDirectory);

                globalStopwatch.Stop();

                // Update the result with timing information
                if (result.Success && result.Statistics != null)
                {
                    var updatedStats = new LoadingStatistics
                    {
                        LoadingTimeMs = globalStopwatch.ElapsedMilliseconds,
                        FilesProcessed = result.Statistics.FilesProcessed,
                        FilesSkipped = result.Statistics.FilesSkipped,
                        ParseErrors = result.Statistics.ParseErrors,
                        MemoryUsedBytes = result.Statistics.MemoryUsedBytes,
                        Warnings = result.Statistics.Warnings
                    };

                    return CountryDataLoadResult.CreateSuccess(result.Countries, updatedStats);
                }

                return result;
            }
            catch (System.Exception e)
            {
                var errorMsg = $"Critical error in JobifiedCountryLoader: {e.Message}";
                globalErrors.Add(errorMsg);
                DominionLogger.LogError($"JobifiedCountryLoader failed: {e.Message}\n{e.StackTrace}");
                return CountryDataLoadResult.CreateFailure(errorMsg);
            }
        }

        /// <summary>
        /// Process country files using Unity Job System
        /// </summary>
        private CountryDataCollection ProcessCountryFilesWithJobs(string[] countryFiles)
        {
            // Initialize result collection
            var countryCollection = new CountryDataCollection(countryFiles.Length, Allocator.Persistent);

            // Create shared parser components
            using var stringPool = new NativeStringPool(2000, Allocator.TempJob);

            // Calculate total batches
            int totalBatches = (countryFiles.Length + BATCH_SIZE - 1) / BATCH_SIZE;
            int processedFiles = 0;

            for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
            {
                int batchStart = batchIndex * BATCH_SIZE;
                int batchEnd = Math.Min(batchStart + BATCH_SIZE, countryFiles.Length);
                int batchSize = batchEnd - batchStart;

                ReportProgress($"Processing batch {batchIndex + 1}/{totalBatches} ({batchSize} files)...",
                    processedFiles, countryFiles.Length, batchIndex, totalBatches);

                // Process batch with jobs
                var batchResult = ProcessBatchWithJobs(
                    countryFiles, batchStart, batchSize, stringPool);

                // Extract data from batch results
                ExtractCountryDataFromBatch(batchResult, countryCollection, batchStart);

                // Dispose batch resources
                DisposeBatchResources(batchResult);

                processedFiles += batchSize;

                // Report progress
                ReportProgress($"Completed batch {batchIndex + 1}/{totalBatches}",
                    processedFiles, countryFiles.Length, batchIndex + 1, totalBatches);
            }

            return countryCollection;
        }

        /// <summary>
        /// Process a single batch using Unity Job System
        /// </summary>
        private BatchJobResult ProcessBatchWithJobs(string[] allFiles, int batchStart, int batchSize, NativeStringPool stringPool)
        {
            // Read all files in batch
            var batchFiles = new string[batchSize];
            Array.Copy(allFiles, batchStart, batchFiles, 0, batchSize);

            // Load file data
            var fileDataResult = LoadBatchFileData(batchFiles);
            if (!fileDataResult.Success)
            {
                return new BatchJobResult { Success = false };
            }

            // Allocate output arrays
            // Use Allocator.Persistent because data survives >4 frames in coroutine processing
            var results = new NativeArray<ParadoxParser.Core.ParadoxParser.ParseResult>(batchSize, Allocator.Persistent);
            var keyValueCounts = new NativeArray<int>(batchSize, Allocator.Persistent);
            var blockCounts = new NativeArray<int>(batchSize, Allocator.Persistent);
            var errorCounts = new NativeArray<int>(batchSize, Allocator.Persistent);

            // Schedule the batch job (simplified without shared arrays)
            var jobHandle = BatchParseJobHelpers.ScheduleBatchParseJob(
                fileDataResult.FilesInfo,
                fileDataResult.CombinedData,
                stringPool,
                results,
                keyValueCounts,
                blockCounts,
                errorCounts,
                FILES_PER_JOB_BATCH,
                ParseJobOptions.Default
            );

            // Wait for completion
            jobHandle.Complete();

            return new BatchJobResult
            {
                Success = true,
                Results = results,
                KeyValueCounts = keyValueCounts,
                BlockCounts = blockCounts,
                ErrorCounts = errorCounts,
                FilesInfo = fileDataResult.FilesInfo,
                CombinedData = fileDataResult.CombinedData
            };
        }

        /// <summary>
        /// Load file data for batch processing
        /// </summary>
        private FileDataResult LoadBatchFileData(string[] filePaths)
        {
            var fileDataList = new List<byte[]>();
            var filesInfo = new List<BatchFileInfo>();
            int totalSize = 0;
            int dataOffset = 0;

            for (int i = 0; i < filePaths.Length; i++)
            {
                try
                {
                    var fileData = File.ReadAllBytes(filePaths[i]);
                    fileDataList.Add(fileData);

                    var fileInfo = new BatchFileInfo
                    {
                        FileIndex = i,
                        DataOffset = dataOffset,
                        DataLength = fileData.Length,
                        EstimatedKeyValues = ESTIMATED_KV_PER_FILE,
                        EstimatedBlocks = ESTIMATED_BLOCKS_PER_FILE,
                        KeyValueStartIndex = i * ESTIMATED_KV_PER_FILE,
                        BlockStartIndex = i * ESTIMATED_BLOCKS_PER_FILE,
                        FileNameHash = ComputeStringHash(Path.GetFileName(filePaths[i]))
                    };

                    filesInfo.Add(fileInfo);
                    dataOffset += fileData.Length;
                    totalSize += fileData.Length;
                }
                catch (System.Exception e)
                {
                    globalErrors.Add($"Failed to read file {filePaths[i]}: {e.Message}");
                    return new FileDataResult { Success = false };
                }
            }

            // Combine all file data
            // Use Allocator.Persistent because data survives >4 frames in coroutine processing
            var combinedData = new NativeArray<byte>(totalSize, Allocator.Persistent);
            int offset = 0;
            for (int i = 0; i < fileDataList.Count; i++)
            {
                var fileData = fileDataList[i];
                var slice = combinedData.Slice(offset, fileData.Length);
                slice.CopyFrom(fileData);
                offset += fileData.Length;
            }

            return new FileDataResult
            {
                Success = true,
                CombinedData = combinedData,
                // Use Allocator.Persistent because data survives >4 frames in coroutine processing
                FilesInfo = new NativeArray<BatchFileInfo>(filesInfo.ToArray(), Allocator.Persistent)
            };
        }

        /// <summary>
        /// Extract country data from batch job results
        /// </summary>
        private void ExtractCountryDataFromBatch(BatchJobResult batchResult, CountryDataCollection collection, int batchStartIndex)
        {
            if (!batchResult.Success) return;

            for (int i = 0; i < batchResult.Results.Length; i++)
            {
                var result = batchResult.Results[i];
                var fileInfo = batchResult.FilesInfo[i];

                if (!result.Success)
                {
                    globalErrors.Add($"Parse failed for file at index {batchStartIndex + i}: " +
                        $"ErrorType={result.ErrorType}, Line={result.ErrorLine}, Column={result.ErrorColumn}, " +
                        $"TokensProcessed={result.TokensProcessed}, FileName={GetFileNameFromHash(fileInfo.FileNameHash)}");
                    continue;
                }

                // For now, create a simple country data entry without full parsing
                // TODO: Re-parse individual files to extract detailed data
                var countryData = CreateBasicCountryData(fileInfo.FileNameHash);
                if (countryData != null)
                {
                    collection.AddCountry(countryData);
                }
            }
        }

        /// <summary>
        /// Create basic country data from file hash (simplified for now)
        /// </summary>
        private CountryData CreateBasicCountryData(uint fileNameHash)
        {
            var coldData = new CountryColdData
            {
                tag = fileNameHash.ToString().Substring(0, Math.Min(3, fileNameHash.ToString().Length)),
                displayName = $"Country_{fileNameHash}"
            };

            var hotData = new CountryHotData
            {
                tagHash = (ushort)(fileNameHash & 0xFFFF),
                graphicalCultureId = 0,
                flags = 0
            };

            // Set color using the SetColor method
            var defaultColor = new Color32((byte)(fileNameHash >> 16), (byte)(fileNameHash >> 8), (byte)fileNameHash, 255);
            hotData.SetColor(defaultColor);

            return new CountryData(hotData, coldData);
        }

        /// <summary>
        /// Extract CountryData from parsed results
        /// </summary>
        private CountryData ExtractCountryDataFromParsedResults(
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> keyValues,
            NativeSlice<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> blocks,
            NativeSlice<byte> sourceData,
            uint fileNameHash)
        {
            try
            {
                // Create cold data first
                var coldData = new CountryColdData();
                var extractedColor = new Color32(128, 128, 128, 255); // default color

                // Convert slices to arrays for helper methods
                var keyValuesArray = keyValues.ToArray();
                var blocksArray = blocks.ToArray();
                var sourceArray = sourceData.ToArray();

                using var keyValuesList = new NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue>(keyValuesArray.Length, Allocator.TempJob);
                using var blocksList = new NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue>(blocksArray.Length, Allocator.TempJob);
                using var sourceNativeArray = new NativeArray<byte>(sourceArray, Allocator.TempJob);

                for (int i = 0; i < keyValuesArray.Length; i++)
                    keyValuesList.Add(keyValuesArray[i]);
                for (int i = 0; i < blocksArray.Length; i++)
                    blocksList.Add(blocksArray[i]);

                // Extract basic country information
                foreach (var kvp in keyValuesArray)
                {
                    if (kvp.KeyHash == CommonKeyHashes.COLOR_HASH && kvp.Value.IsBlock)
                    {
                        // Find the color block in blocks array
                        var colorBlock = FindBlockByHash(kvp, blocksList);
                        if (colorBlock.HasValue)
                        {
                            extractedColor = ExtractColorValue(colorBlock.Value, blocksList, sourceNativeArray);
                        }
                    }
                    else if (kvp.KeyHash == CommonKeyHashes.GRAPHICAL_CULTURE_HASH)
                    {
                        string graphicalCulture = ExtractStringValue(kvp.Value, sourceNativeArray);
                        coldData.graphicalCulture = graphicalCulture;
                    }
                    // Add more extractions as needed...
                }

                // Set tag from filename hash (simplified)
                coldData.tag = fileNameHash.ToString();

                // Create hot data
                var hotData = new CountryHotData
                {
                    tagHash = (ushort)(fileNameHash & 0xFFFF),
                    graphicalCultureId = 0, // Will be set based on graphicalCulture string
                    flags = 0
                };
                hotData.SetColor(extractedColor);

                return new CountryData(hotData, coldData);
            }
            catch (System.Exception e)
            {
                globalErrors.Add($"Failed to extract country data: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find a block by its hash in the blocks array
        /// </summary>
        private ParadoxParser.Core.ParadoxParser.ParsedKeyValue? FindBlockByHash(
            ParadoxParser.Core.ParadoxParser.ParsedKeyValue parentKvp,
            NativeList<ParadoxParser.Core.ParadoxParser.ParsedKeyValue> blocks)
        {
            if (!parentKvp.Value.IsBlock) return null;

            int startIndex = parentKvp.Value.BlockStartIndex;
            int length = parentKvp.Value.BlockLength;

            if (startIndex >= 0 && startIndex < blocks.Length && length > 0)
            {
                return blocks[startIndex];
            }

            return null;
        }

        /// <summary>
        /// Compute hash for string (simple implementation)
        /// </summary>
        private uint ComputeStringHash(string str)
        {
            uint hash = 2166136261u;
            foreach (char c in str)
            {
                hash ^= (byte)c;
                hash *= 16777619u;
            }
            return hash;
        }

        /// <summary>
        /// Get country files from directory
        /// </summary>
        private string[] GetCountryFiles(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return new string[0];
            }

            return Directory.GetFiles(directory, "*.txt", SearchOption.TopDirectoryOnly);
        }

        /// <summary>
        /// Report progress to listeners
        /// </summary>
        private void ReportProgress(string operation, int processed = 0, int total = 0, int batches = 0, int totalBatches = 0)
        {
            var progress = new LoadingProgress
            {
                FilesProcessed = processed,
                TotalFiles = total,
                BatchesCompleted = batches,
                TotalBatches = totalBatches,
                ElapsedMs = globalStopwatch.ElapsedMilliseconds,
                MemoryUsageMB = GC.GetTotalMemory(false) / (1024 * 1024),
                ErrorCount = globalErrors.Count,
                CurrentOperation = operation
            };

            OnProgressUpdate?.Invoke(progress);
        }

        /// <summary>
        /// Dispose batch resources
        /// </summary>
        private void DisposeBatchResources(BatchJobResult batchResult)
        {
            if (batchResult.Results.IsCreated) batchResult.Results.Dispose();
            if (batchResult.KeyValueCounts.IsCreated) batchResult.KeyValueCounts.Dispose();
            if (batchResult.BlockCounts.IsCreated) batchResult.BlockCounts.Dispose();
            if (batchResult.ErrorCounts.IsCreated) batchResult.ErrorCounts.Dispose();
            if (batchResult.FilesInfo.IsCreated) batchResult.FilesInfo.Dispose();
            if (batchResult.CombinedData.IsCreated) batchResult.CombinedData.Dispose();
        }

        /// <summary>
        /// Get filename from hash for debugging (simplified)
        /// </summary>
        private string GetFileNameFromHash(uint hash)
        {
            return $"File_{hash}"; // Simplified - in real implementation would reverse lookup
        }

        /// <summary>
        /// Log final statistics
        /// </summary>
        private void LogFinalStatistics(CountryDataCollection collection, int totalFiles)
        {
            var totalTime = globalStopwatch.ElapsedMilliseconds;
            var avgTimePerFile = totalFiles > 0 ? (float)totalTime / totalFiles : 0f;

            DominionLogger.LogSeparator("JobifiedCountryLoader Results");
            DominionLogger.LogFormat("Files: {0}", totalFiles);
            DominionLogger.LogFormat("Countries loaded: {0}", collection?.Count ?? 0);
            DominionLogger.LogFormat("Total time: {0}ms", totalTime);
            DominionLogger.LogFormat("Avg time per file: {0:F2}ms", avgTimePerFile);
            DominionLogger.LogFormat("Errors: {0}", globalErrors.Count);

            if (globalErrors.Count > 0)
            {
                DominionLogger.LogWarning($"Errors during loading:\n{string.Join("\n", globalErrors)}");
            }
        }

        // Helper structures for batch processing
        private struct BatchJobResult
        {
            public bool Success;
            public NativeArray<ParadoxParser.Core.ParadoxParser.ParseResult> Results;
            public NativeArray<int> KeyValueCounts;
            public NativeArray<int> BlockCounts;
            public NativeArray<int> ErrorCounts;
            public NativeArray<BatchFileInfo> FilesInfo;
            public NativeArray<byte> CombinedData;
        }

        private struct FileDataResult
        {
            public bool Success;
            public NativeArray<byte> CombinedData;
            public NativeArray<BatchFileInfo> FilesInfo;
        }
    }
}