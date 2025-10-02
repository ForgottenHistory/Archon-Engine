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
    /// Single file parsing job for Unity Job System
    /// Designed to replace async/await approach with proper Unity threading
    /// Note: Burst disabled temporarily due to try/catch in dependencies
    /// </summary>
    // [BurstCompile(OptimizeFor = OptimizeFor.Performance)] // Disabled due to ErrorAccumulator/TokenStream try/catch
    public struct ParseJob : IJob
    {
        [ReadOnly] public NativeArray<byte> FileData;
        [ReadOnly] public NativeStringPool StringPool;
        [ReadOnly] public ParseJobOptions Options;

        [WriteOnly] public NativeReference<ParseResult> Result;
        [WriteOnly] public NativeList<ParsedKeyValue> OutputKeyValues;
        [WriteOnly] public NativeList<ParsedKeyValue> OutputBlocks;

        // Error tracking
        public ErrorAccumulator ErrorAccumulator;

        public void Execute()
        {
            // Clear error accumulator
            ErrorAccumulator.Clear();

            // Create tokenizer with Job allocators
            var tokenizer = new Tokenizer(FileData, StringPool, ErrorAccumulator);
            var tokenStream = tokenizer.Tokenize(Allocator.Temp);

            // Parse with Job allocators
            var parseResult = ParadoxParser.Core.ParadoxParser.Parse(
                tokenStream.GetRemainingSlice(),
                OutputKeyValues,
                OutputBlocks,
                FileData,
                Allocator.Temp
            );

            Result.Value = parseResult;

            // Cleanup allocated resources (dispose in reverse order of creation)
            if (tokenStream.IsCreated) tokenStream.Dispose();
            if (tokenizer.IsCreated) tokenizer.Dispose();
        }
    }

    /// <summary>
    /// Helper methods for creating and scheduling ParseJobs
    /// </summary>
    public static class ParseJobHelpers
    {
        /// <summary>
        /// Create and schedule a ParseJob for a single file
        /// </summary>
        public static JobHandle ScheduleParseJob(
            NativeArray<byte> fileData,
            NativeStringPool stringPool,
            NativeList<ParsedKeyValue> outputKeyValues,
            NativeList<ParsedKeyValue> outputBlocks,
            NativeReference<ParseResult> result,
            ErrorAccumulator errorAccumulator,
            ParseJobOptions options = default,
            JobHandle dependency = default)
        {
            var job = new ParseJob
            {
                FileData = fileData,
                StringPool = stringPool,
                Options = options.Equals(default) ? ParseJobOptions.Default : options,
                Result = result,
                OutputKeyValues = outputKeyValues,
                OutputBlocks = outputBlocks,
                ErrorAccumulator = errorAccumulator
            };

            return job.Schedule(dependency);
        }

        /// <summary>
        /// Synchronously parse a file using Job System (for testing/simple cases)
        /// </summary>
        public static ParseResult ParseFileSync(
            NativeArray<byte> fileData,
            NativeStringPool stringPool,
            NativeList<ParsedKeyValue> outputKeyValues,
            NativeList<ParsedKeyValue> outputBlocks,
            ErrorAccumulator errorAccumulator,
            ParseJobOptions options = default)
        {
            using var result = new NativeReference<ParseResult>(Allocator.TempJob);

            var jobHandle = ScheduleParseJob(
                fileData, stringPool, outputKeyValues, outputBlocks,
                result, errorAccumulator, options
            );

            jobHandle.Complete();
            return result.Value;
        }
    }

    /// <summary>
    /// Single file parse result with metadata
    /// </summary>
    public struct SingleFileParseResult
    {
        public bool Success;
        public string FilePath;
        public int KeyValueCount;
        public int BlockCount;
        public int ErrorCount;
        public float ParseTimeMs;
        public long MemoryUsedBytes;

        public static SingleFileParseResult CreateSuccess(
            string filePath,
            int keyValueCount,
            int blockCount,
            float parseTime,
            long memoryUsed)
        {
            return new SingleFileParseResult
            {
                Success = true,
                FilePath = filePath,
                KeyValueCount = keyValueCount,
                BlockCount = blockCount,
                ErrorCount = 0,
                ParseTimeMs = parseTime,
                MemoryUsedBytes = memoryUsed
            };
        }

        public static SingleFileParseResult CreateFailure(
            string filePath,
            int errorCount,
            float parseTime)
        {
            return new SingleFileParseResult
            {
                Success = false,
                FilePath = filePath,
                KeyValueCount = 0,
                BlockCount = 0,
                ErrorCount = errorCount,
                ParseTimeMs = parseTime,
                MemoryUsedBytes = 0
            };
        }
    }
}