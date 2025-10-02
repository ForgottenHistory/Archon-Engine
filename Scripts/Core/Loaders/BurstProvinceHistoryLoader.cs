using Unity.Collections;
using Unity.Jobs;
using System;
using Core.Data;
using Core.Jobs;
using UnityEngine;
using Utils;

namespace Core.Loaders
{
    /// <summary>
    /// Province history loader using hybrid JSON5 + Burst architecture
    /// Phase 1: Load JSON5 files to structs (main thread)
    /// Phase 2: Process structs with burst jobs (multi-threaded)
    /// </summary>
    public static class BurstProvinceHistoryLoader
    {
        /// <summary>
        /// Load province initial states using hybrid JSON5 + Burst approach
        /// </summary>
        public static ProvinceInitialStateLoadResult LoadProvinceInitialStates(string dataDirectory)
        {
            DominionLogger.Log("Starting hybrid JSON5 + Burst province loading...");

            try
            {
                // Phase 1: Load JSON5 files to burst-compatible structs (main thread)
                var json5Result = Json5ProvinceConverter.LoadProvinceJson5Files(dataDirectory);

                if (!json5Result.success)
                {
                    return ProvinceInitialStateLoadResult.Failed($"JSON5 loading failed: {json5Result.errorMessage}");
                }

                DominionLogger.Log($"JSON5 loading complete: {json5Result.loadedCount} provinces loaded");

                // Phase 2: Process structs with burst jobs (multi-threaded)
                var burstResult = ProcessProvincesWithBurstJobs(json5Result);

                // Clean up JSON5 result
                json5Result.Dispose();

                if (!burstResult.success)
                {
                    return ProvinceInitialStateLoadResult.Failed($"Burst processing failed: {burstResult.errorMessage}");
                }

                DominionLogger.Log($"Burst processing complete: {burstResult.processedCount} provinces processed");

                // Convert to final result format
                return ProvinceInitialStateLoadResult.Successful(burstResult.provinces, json5Result.failedCount);
            }
            catch (Exception e)
            {
                return ProvinceInitialStateLoadResult.Failed($"Province loading failed: {e.Message}");
            }
        }

        /// <summary>
        /// Process raw province data using burst jobs
        /// Phase 2 of the hybrid architecture
        /// </summary>
        private static ProvinceProcessingResult ProcessProvincesWithBurstJobs(Json5ProvinceLoadResult json5Result)
        {
            if (!json5Result.rawData.IsCreated || json5Result.rawData.Length == 0)
            {
                return ProvinceProcessingResult.Failed("No raw province data to process");
            }

            // Prepare output array for processed provinces
            // Use Allocator.Persistent because data survives >4 frames in coroutine processing
            var processedProvinces = new NativeArray<ProvinceInitialState>(json5Result.rawData.Length, Allocator.Persistent);

            try
            {
                // Create and schedule the burst job
                var processingJob = new ProvinceProcessingJob
                {
                    rawData = json5Result.rawData,
                    results = processedProvinces,
                    validateData = true,
                    applyDefaults = true
                };

                // Execute the job with parallelization
                var jobHandle = processingJob.Schedule(json5Result.rawData.Length, 32);
                jobHandle.Complete();

                DominionLogger.Log($"Burst job completed: processed {json5Result.rawData.Length} provinces");

                return ProvinceProcessingResult.Success(processedProvinces, json5Result.rawData.Length);
            }
            catch (Exception e)
            {
                // Clean up on error
                if (processedProvinces.IsCreated)
                    processedProvinces.Dispose();

                return ProvinceProcessingResult.Failed($"Burst processing failed: {e.Message}");
            }
        }
    }
}