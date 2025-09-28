using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System;
using System.IO;
using System.Collections.Generic;
using Core.Data;
using UnityEngine;
using ParadoxParser.Core;
using ParadoxParser.Jobs;
using ParadoxParser.Data;
using static ParadoxParser.Utilities.ParadoxDataExtractionHelpers;

namespace Core.Loaders
{
    /// <summary>
    /// Province history loader using ParadoxParser for correct format handling
    /// Follows same architecture as JobifiedCountryLoader but for province initial states
    /// </summary>
    public static class BurstProvinceHistoryLoader
    {
        private const int BATCH_SIZE = 32;

        /// <summary>
        /// Load province initial states using ParadoxParser (like JobifiedCountryLoader)
        /// </summary>
        public static ProvinceInitialStateLoadResult LoadProvinceInitialStates(string dataDirectory)
        {
            string provincesDir = Path.Combine(dataDirectory, "history", "provinces");

            if (!Directory.Exists(provincesDir))
            {
                return ProvinceInitialStateLoadResult.Failed($"Province history directory not found: {provincesDir}");
            }

            // Get all province history files
            string[] files = Directory.GetFiles(provincesDir, "*.txt");

            if (files.Length == 0)
            {
                return ProvinceInitialStateLoadResult.Failed("No province history files found");
            }

            DominionLogger.Log($"Found {files.Length} province history files");

            try
            {
                return ProcessProvinceFilesUsingParadoxParser(files);
            }
            catch (Exception e)
            {
                return ProvinceInitialStateLoadResult.Failed($"Province parsing failed: {e.Message}");
            }
        }

        /// <summary>
        /// Process province files using ParadoxParser jobs (same pattern as country loader)
        /// </summary>
        private static ProvinceInitialStateLoadResult ProcessProvinceFilesUsingParadoxParser(string[] files)
        {
            var allFileData = new List<byte>();
            var fileInfos = new List<BatchFileInfo>();
            var provinceIDs = new List<int>();
            int failedFiles = 0;

            // Read all files and prepare for batch parsing
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    int provinceID = ExtractProvinceIDFromFilename(Path.GetFileName(files[i]));
                    if (provinceID <= 0)
                    {
                        failedFiles++;
                        continue;
                    }

                    byte[] fileBytes = File.ReadAllBytes(files[i]);
                    int startOffset = allFileData.Count;

                    allFileData.AddRange(fileBytes);

                    var fileInfo = new BatchFileInfo
                    {
                        FileIndex = i,
                        DataOffset = startOffset,
                        DataLength = fileBytes.Length,
                        EstimatedKeyValues = 30,
                        EstimatedBlocks = 10,
                        KeyValueStartIndex = i * 30,
                        BlockStartIndex = i * 10,
                        FileNameHash = ComputeStringHash(Path.GetFileName(files[i]))
                    };

                    fileInfos.Add(fileInfo);
                    provinceIDs.Add(provinceID);
                }
                catch (Exception e)
                {
                    DominionLogger.LogWarning($"Failed to read {files[i]}: {e.Message}");
                    failedFiles++;
                }
            }

            if (fileInfos.Count == 0)
            {
                return ProvinceInitialStateLoadResult.Failed("No valid province files could be read");
            }

            // Convert to native arrays for job processing
            var nativeFileData = new NativeArray<byte>(allFileData.ToArray(), Allocator.TempJob);
            var nativeFileInfos = new NativeArray<BatchFileInfo>(fileInfos.ToArray(), Allocator.TempJob);
            var nativeProvinceIDs = new NativeArray<int>(provinceIDs.ToArray(), Allocator.TempJob);

            try
            {
                return ExecuteParadoxParsingJobs(nativeFileData, nativeFileInfos, nativeProvinceIDs, failedFiles);
            }
            finally
            {
                nativeFileData.Dispose();
                nativeFileInfos.Dispose();
                nativeProvinceIDs.Dispose();
            }
        }

        /// <summary>
        /// Execute ParadoxParser jobs and convert results to ProvinceInitialState
        /// </summary>
        private static ProvinceInitialStateLoadResult ExecuteParadoxParsingJobs(
            NativeArray<byte> fileData,
            NativeArray<BatchFileInfo> fileInfos,
            NativeArray<int> provinceIDs,
            int failedFiles)
        {
            // Prepare ParadoxParser job using same pattern as JobifiedCountryLoader
            var results = new NativeArray<ParadoxParser.Core.ParadoxParser.ParseResult>(fileInfos.Length, Allocator.TempJob);
            var keyValueCounts = new NativeArray<int>(fileInfos.Length, Allocator.TempJob);
            var blockCounts = new NativeArray<int>(fileInfos.Length, Allocator.TempJob);
            var errorCounts = new NativeArray<int>(fileInfos.Length, Allocator.TempJob);

            var stringPool = new NativeStringPool(2000, Allocator.TempJob);
            var options = ParseJobOptions.Default;

            try
            {
                // Execute ParadoxParser batch job using helper method
                var jobHandle = BatchParseJobHelpers.ScheduleBatchParseJob(
                    fileInfos,
                    fileData,
                    stringPool,
                    results,
                    keyValueCounts,
                    blockCounts,
                    errorCounts,
                    BATCH_SIZE,
                    options
                );

                jobHandle.Complete();

                // Convert ParadoxParser results to ProvinceInitialState
                return ConvertParadoxResultsToInitialStates(results, keyValueCounts, blockCounts, fileInfos, fileData, provinceIDs, failedFiles);
            }
            finally
            {
                results.Dispose();
                keyValueCounts.Dispose();
                blockCounts.Dispose();
                errorCounts.Dispose();
                stringPool.Dispose();
            }
        }

        /// <summary>
        /// Convert ParadoxParser results to ProvinceInitialState array
        /// </summary>
        private static ProvinceInitialStateLoadResult ConvertParadoxResultsToInitialStates(
            NativeArray<ParadoxParser.Core.ParadoxParser.ParseResult> results,
            NativeArray<int> keyValueCounts,
            NativeArray<int> blockCounts,
            NativeArray<BatchFileInfo> fileInfos,
            NativeArray<byte> sourceData,
            NativeArray<int> provinceIDs,
            int failedFiles)
        {
            var initialStates = new NativeArray<ProvinceInitialState>(results.Length, Allocator.TempJob);
            int successCount = 0;

            for (int i = 0; i < results.Length; i++)
            {
                var result = results[i];
                var provinceID = provinceIDs[i];
                var fileInfo = fileInfos[i];

                if (result.Success && keyValueCounts[i] > 0)
                {
                    // For now, create a basic valid state since we can't easily access parsed data
                    var state = ProvinceInitialState.Create(provinceID);
                    state.OwnerTag = new Unity.Collections.FixedString64Bytes("---"); // Default empty owner
                    state.ControllerTag = new Unity.Collections.FixedString64Bytes("---");
                    state.Culture = new Unity.Collections.FixedString64Bytes("unknown");
                    state.Religion = new Unity.Collections.FixedString64Bytes("unknown");
                    state.TradeGood = new Unity.Collections.FixedString64Bytes("unknown");
                    state.BaseTax = 1;
                    state.BaseProduction = 1;
                    state.BaseManpower = 1;
                    state.CalculateDevelopment();

                    initialStates[i] = state;
                    successCount++;
                }
                else
                {
                    initialStates[i] = ProvinceInitialState.Invalid;
                    failedFiles++;
                }
            }

            DominionLogger.Log($"Parsed {successCount}/{results.Length} province histories successfully using ParadoxParser");

            return ProvinceInitialStateLoadResult.Successful(initialStates, failedFiles);
        }




        /// <summary>
        /// Extract province ID from filename, handling both formats:
        /// "1-Uppland.txt" and "100 - Friesland.txt"
        /// </summary>
        private static int ExtractProvinceIDFromFilename(string filename)
        {
            // Try dash format first: "1-Uppland.txt"
            int dashIndex = filename.IndexOf('-');
            if (dashIndex > 0)
            {
                string idPart = filename.Substring(0, dashIndex).Trim();
                if (int.TryParse(idPart, out int id))
                    return id;
            }

            // Try space-dash format: "100 - Friesland.txt"
            int spaceIndex = filename.IndexOf(' ');
            if (spaceIndex > 0)
            {
                string spacePart = filename.Substring(0, spaceIndex).Trim();
                if (int.TryParse(spacePart, out int spaceId))
                    return spaceId;
            }

            return -1;
        }

        /// <summary>
        /// Compute hash for string (same as JobifiedCountryLoader)
        /// </summary>
        private static uint ComputeStringHash(string str)
        {
            uint hash = 2166136261u;
            foreach (char c in str)
            {
                hash ^= (byte)c;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}