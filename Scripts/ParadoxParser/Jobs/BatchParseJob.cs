using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using ParadoxParser.Core;
using ParadoxParser.Tokenization;
using ParadoxParser.Data;
using static ParadoxParser.Core.ParadoxParser;

namespace ParadoxParser.Jobs
{
    /// <summary>
    /// Batch file parsing job for processing multiple files in parallel
    /// Uses IJobParallelForBatch for optimal performance with Unity Job System
    /// Note: Burst disabled temporarily due to try/catch in dependencies
    /// </summary>
    // [BurstCompile(OptimizeFor = OptimizeFor.Performance)] // Disabled due to ErrorAccumulator/TokenStream try/catch
    public struct BatchParseJob : IJobParallelForBatch
    {
        // Input data
        [ReadOnly] public NativeArray<BatchFileInfo> FilesInfo;
        [ReadOnly] public NativeArray<byte> CombinedFileData;
        [ReadOnly] public NativeStringPool StringPool;
        [ReadOnly] public ParseJobOptions Options;

        // Output arrays (one per file)
        [WriteOnly] public NativeArray<ParseResult> Results;
        [WriteOnly] public NativeArray<int> KeyValueCounts;
        [WriteOnly] public NativeArray<int> BlockCounts;

        // Error tracking per file
        [NativeDisableParallelForRestriction]
        public NativeArray<int> ErrorCounts;

        // Note: Removed shared output arrays to avoid "nested native containers" error
        // Data extraction will be handled after job completion

        public void Execute(int startIndex, int count)
        {
            for (int i = startIndex; i < startIndex + count; i++)
            {
                ProcessSingleFile(i);
            }
        }

        private void ProcessSingleFile(int fileIndex)
        {
            var fileInfo = FilesInfo[fileIndex];

            // Extract file data slice
            var fileDataSlice = CombinedFileData.Slice(fileInfo.DataOffset, fileInfo.DataLength);
            var fileData = new NativeArray<byte>(fileDataSlice.ToArray(), Allocator.Temp);

            // Create temporary collections for this file
            var keyValues = new NativeList<ParsedKeyValue>(fileInfo.EstimatedKeyValues, Allocator.Temp);
            var blocks = new NativeList<ParsedKeyValue>(fileInfo.EstimatedBlocks, Allocator.Temp);
            var errorAccumulator = new ErrorAccumulator(Allocator.Temp);

            // Parse the file
            var tokenizer = new Tokenizer(fileData, StringPool, errorAccumulator);
            var tokenStream = tokenizer.Tokenize(Allocator.Temp);

            var parseResult = ParadoxParser.Core.ParadoxParser.Parse(
                tokenStream.GetRemainingSlice(),
                keyValues,
                blocks,
                fileData,
                Allocator.Temp
            );

            // Store results with additional error info for debugging
            Results[fileIndex] = parseResult;
            KeyValueCounts[fileIndex] = keyValues.Length;
            BlockCounts[fileIndex] = blocks.Length;
            ErrorCounts[fileIndex] = errorAccumulator.ErrorCount;

            // Note: Data copying removed - will be handled after job completion

            // Manual cleanup of collections (dispose in reverse order of creation)
            if (tokenStream.IsCreated) tokenStream.Dispose();
            if (tokenizer.IsCreated) tokenizer.Dispose();
            if (errorAccumulator.IsCreated) errorAccumulator.Dispose();
            if (blocks.IsCreated) blocks.Dispose();
            if (keyValues.IsCreated) keyValues.Dispose();
            if (fileData.IsCreated) fileData.Dispose();
        }

        // CopyToSharedArrays method removed - data extraction handled after job completion
    }

    /// <summary>
    /// Information about a file in a batch
    /// </summary>
    public struct BatchFileInfo
    {
        public int FileIndex;
        public int DataOffset;           // Offset in combined data array
        public int DataLength;           // Length of file data
        public int EstimatedKeyValues;   // Estimated number of key-value pairs
        public int EstimatedBlocks;      // Estimated number of blocks
        public int KeyValueStartIndex;   // Starting index in shared KeyValues array
        public int BlockStartIndex;      // Starting index in shared Blocks array
        public uint FileNameHash;        // Hash of filename for identification
    }

    /// <summary>
    /// Helper methods for creating and scheduling BatchParseJobs
    /// </summary>
    public static class BatchParseJobHelpers
    {
        /// <summary>
        /// Create and schedule a BatchParseJob for multiple files
        /// </summary>
        public static JobHandle ScheduleBatchParseJob(
            NativeArray<BatchFileInfo> filesInfo,
            NativeArray<byte> combinedFileData,
            NativeStringPool stringPool,
            NativeArray<ParseResult> results,
            NativeArray<int> keyValueCounts,
            NativeArray<int> blockCounts,
            NativeArray<int> errorCounts,
            int batchSize = 4,
            ParseJobOptions options = default,
            JobHandle dependency = default)
        {
            var job = new BatchParseJob
            {
                FilesInfo = filesInfo,
                CombinedFileData = combinedFileData,
                StringPool = stringPool,
                Options = options.Equals(default) ? ParseJobOptions.Default : options,
                Results = results,
                KeyValueCounts = keyValueCounts,
                BlockCounts = blockCounts,
                ErrorCounts = errorCounts
            };

            return job.ScheduleBatch(filesInfo.Length, batchSize, dependency);
        }

        /// <summary>
        /// Calculate memory requirements for batch processing
        /// </summary>
        public static BatchMemoryRequirements CalculateMemoryRequirements(
            string[] filePaths,
            int estimatedKeyValuesPerFile = 30,
            int estimatedBlocksPerFile = 60)
        {
            long totalFileSize = 0;
            foreach (var path in filePaths)
            {
                if (System.IO.File.Exists(path))
                {
                    totalFileSize += new System.IO.FileInfo(path).Length;
                }
            }

            return new BatchMemoryRequirements
            {
                FileCount = filePaths.Length,
                TotalFileDataBytes = totalFileSize,
                EstimatedKeyValues = filePaths.Length * estimatedKeyValuesPerFile,
                EstimatedBlocks = filePaths.Length * estimatedBlocksPerFile,
                EstimatedTotalMemoryMB = (totalFileSize +
                    (filePaths.Length * estimatedKeyValuesPerFile * 64) +
                    (filePaths.Length * estimatedBlocksPerFile * 64)) / (1024 * 1024)
            };
        }
    }

    /// <summary>
    /// Memory requirements calculation result
    /// </summary>
    public struct BatchMemoryRequirements
    {
        public int FileCount;
        public long TotalFileDataBytes;
        public int EstimatedKeyValues;
        public int EstimatedBlocks;
        public long EstimatedTotalMemoryMB;

        public bool IsMemoryReasonable => EstimatedTotalMemoryMB < 500; // Under 500MB

        public override string ToString()
        {
            return $"Files: {FileCount}, Data: {TotalFileDataBytes / 1024}KB, " +
                   $"KV: {EstimatedKeyValues}, Blocks: {EstimatedBlocks}, " +
                   $"Memory: {EstimatedTotalMemoryMB}MB";
        }
    }

    /// <summary>
    /// Batch processing result summary
    /// </summary>
    public struct BatchProcessResult
    {
        public int FilesProcessed;
        public int SuccessfulFiles;
        public int FailedFiles;
        public int TotalKeyValues;
        public int TotalBlocks;
        public int TotalErrors;
        public float ProcessingTimeMs;
        public long MemoryUsedBytes;

        public float SuccessRate => FilesProcessed > 0 ? (float)SuccessfulFiles / FilesProcessed : 0f;
        public float AverageTimePerFile => FilesProcessed > 0 ? ProcessingTimeMs / FilesProcessed : 0f;

        public override string ToString()
        {
            return $"Processed: {FilesProcessed} files ({SuccessRate:P1} success), " +
                   $"Data: {TotalKeyValues} KV + {TotalBlocks} blocks, " +
                   $"Time: {ProcessingTimeMs:F1}ms ({AverageTimePerFile:F2}ms/file)";
        }
    }
}