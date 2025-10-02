using Unity.Collections;
using Unity.Jobs;
using System;
using Core.Data;
using Core.Jobs;
using UnityEngine;
using Utils;
using System.Collections.Generic;

namespace Core.Loaders
{
    /// <summary>
    /// Country loader using hybrid JSON5 + Burst architecture
    /// Phase 1: Load JSON5 files to structs (main thread)
    /// Phase 2: Process structs with burst jobs (multi-threaded)
    /// </summary>
    public static class BurstCountryLoader
    {
        /// <summary>
        /// Load country data using hybrid JSON5 + Burst approach
        /// </summary>
        /// <param name="dataDirectory">Directory containing country data</param>
        /// <param name="tagMapping">Optional mapping of filenames to country tags from 00_countries.txt</param>
        public static CountryDataLoadResult LoadAllCountries(string dataDirectory, Dictionary<string, string> tagMapping = null)
        {
            DominionLogger.Log("Starting hybrid JSON5 + Burst country loading...");

            try
            {
                // Phase 1: Load JSON5 files to burst-compatible structs (main thread)
                var json5Result = Json5CountryConverter.LoadCountryJson5Files(dataDirectory, tagMapping);

                if (!json5Result.success)
                {
                    return CountryDataLoadResult.CreateFailure($"JSON5 loading failed: {json5Result.errorMessage}");
                }

                DominionLogger.Log($"JSON5 loading complete: {json5Result.loadedCount} countries loaded");

                // Phase 2: Process structs with burst jobs (multi-threaded)
                var burstResult = ProcessCountriesWithBurstJobs(json5Result);

                if (!burstResult.success)
                {
                    json5Result.Dispose();
                    return CountryDataLoadResult.CreateFailure($"Burst processing failed: {burstResult.errorMessage}");
                }

                DominionLogger.Log($"Burst processing complete: {burstResult.processedCount} countries processed");

                // Convert to final result format (needs both burst result and JSON5 data)
                var countryCollection = CreateCountryCollectionFromResults(burstResult, json5Result);

                // Clean up resources
                burstResult.Dispose();
                json5Result.Dispose();

                // Create loading statistics
                var stats = new LoadingStatistics
                {
                    LoadingTimeMs = 0, // TODO: Add timing
                    FilesProcessed = json5Result.loadedCount,
                    FilesSkipped = json5Result.failedCount,
                    ParseErrors = json5Result.failedCount,
                    MemoryUsedBytes = countryCollection?.GetMemoryUsage() ?? 0,
                    Warnings = new System.Collections.Generic.List<string>()
                };

                if (countryCollection != null)
                {
                    return CountryDataLoadResult.CreateSuccess(countryCollection, stats);
                }
                else
                {
                    return CountryDataLoadResult.CreateFailure("Failed to create country collection");
                }
            }
            catch (Exception e)
            {
                return CountryDataLoadResult.CreateFailure($"Country loading failed: {e.Message}");
            }
        }

        /// <summary>
        /// Process raw country data using hybrid approach:
        /// - Hot data: Processed with burst jobs (performance-critical)
        /// - Cold data: Processed on main thread (reference types)
        /// </summary>
        private static CountryProcessingResult ProcessCountriesWithBurstJobs(Json5CountryLoadResult json5Result)
        {
            if (!json5Result.rawData.IsCreated || json5Result.rawData.Length == 0)
            {
                return CountryProcessingResult.Failed("No raw country data to process");
            }

            // Prepare output array for hot data only (burst-compatible)
            // Use Allocator.Persistent because data survives >4 frames in coroutine processing
            var processedHotData = new NativeArray<CountryHotData>(json5Result.rawData.Length, Allocator.Persistent);

            try
            {
                // Create and schedule the burst job for hot data
                var processingJob = new CountryProcessingJob
                {
                    rawData = json5Result.rawData,
                    hotResults = processedHotData,
                    validateData = true,
                    applyDefaults = true
                };

                // Execute the job with parallelization
                var jobHandle = processingJob.Schedule(json5Result.rawData.Length, 32);
                jobHandle.Complete();

                DominionLogger.Log($"Burst job completed: processed {json5Result.rawData.Length} countries");

                return CountryProcessingResult.Success(processedHotData, json5Result.rawData.Length);
            }
            catch (Exception e)
            {
                // Clean up on error
                if (processedHotData.IsCreated)
                    processedHotData.Dispose();

                return CountryProcessingResult.Failed($"Burst processing failed: {e.Message}");
            }
        }

        /// <summary>
        /// Create CountryDataCollection from burst job results and JSON5 data
        /// Hot data comes from burst jobs, cold data is created on main thread
        /// </summary>
        private static CountryDataCollection CreateCountryCollectionFromResults(CountryProcessingResult burstResult, Json5CountryLoadResult json5Result)
        {
            if (!burstResult.success || !burstResult.hotData.IsCreated)
                return null;

            var collection = new CountryDataCollection(burstResult.hotData.Length, Allocator.Persistent);

            // Add all countries to the collection
            for (int i = 0; i < burstResult.hotData.Length; i++)
            {
                var hotData = burstResult.hotData[i];
                var rawData = json5Result.rawData[i];

                // Create cold data on main thread (can use reference types)
                var coldData = CreateColdDataFromRaw(rawData);

                if (!string.IsNullOrEmpty(coldData.tag) && coldData.tag != "---")
                {
                    var countryData = new CountryData(hotData, coldData);
                    collection.AddCountry(countryData);
                }
            }

            return collection;
        }

        /// <summary>
        /// Create cold data from raw JSON5 data (main thread only - can use reference types)
        /// </summary>
        private static CountryColdData CreateColdDataFromRaw(RawCountryData raw)
        {
            var coldData = new CountryColdData
            {
                tag = raw.tag.ToString(),
                displayName = raw.tag.ToString(), // Use tag as display name for now
                graphicalCulture = raw.hasGraphicalCulture ? raw.graphicalCulture.ToString() : "westerngfx",
                preferredReligion = raw.hasPreferredReligion ? raw.preferredReligion.ToString() : "",
                color = raw.GetColor(), // Always store main color
                revolutionaryColors = raw.hasRevolutionaryColors ? raw.GetRevolutionaryColor() : new Color32(0, 0, 0, 0) // Revolutionary colors only if present
            };

            return coldData;
        }
    }
}