using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Core.Data;
using UnityEngine;

namespace Core.Jobs
{
    /// <summary>
    /// Burst-compiled job for processing country data
    /// Phase 2 of the hybrid JSON5 + Burst architecture for countries
    /// Converts RawCountryData to final CountryData
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct CountryProcessingJob : IJobParallelFor
    {
        // Input data from JSON5 loading
        [ReadOnly] public NativeArray<RawCountryData> rawData;

        // Output data for game state (only hot data - cold data handled on main thread)
        [WriteOnly] public NativeArray<CountryHotData> hotResults;

        // Processing options
        [ReadOnly] public bool validateData;
        [ReadOnly] public bool applyDefaults;

        public void Execute(int index)
        {
            var raw = rawData[index];
            var hotData = ProcessSingleCountryHotData(raw);
            hotResults[index] = hotData;
        }

        /// <summary>
        /// Process a single country's raw data into hot data only (burst-compatible)
        /// Cold data is handled on the main thread
        /// </summary>
        private CountryHotData ProcessSingleCountryHotData(RawCountryData raw)
        {
            // Create hot data only (value type, burst-compatible)
            var hotData = new CountryHotData
            {
                tagHash = ComputeTagHash(raw.tag),
                graphicalCultureId = 0, // Will be resolved later during linking phase
                flags = 0
            };

            // Set color
            var color = raw.GetColor();
            hotData.SetColor(color);

            // Apply validation if enabled
            if (validateData)
            {
                ValidateCountryHotData(ref hotData);
            }

            // Apply defaults if enabled
            if (applyDefaults)
            {
                ApplyDefaultHotValues(ref hotData, raw);
            }

            return hotData;
        }

        /// <summary>
        /// Validate country hot data and fix common issues
        /// </summary>
        private void ValidateCountryHotData(ref CountryHotData hotData)
        {
            // Ensure tag hash is valid
            if (hotData.tagHash == 0)
            {
                hotData.tagHash = 1; // Default to 1 instead of 0 (which is invalid)
            }
        }

        /// <summary>
        /// Apply default values for missing hot data
        /// </summary>
        private void ApplyDefaultHotValues(ref CountryHotData hotData, RawCountryData raw)
        {
            // Set flags based on available data
            hotData.SetFlag(CountryHotData.FLAG_HAS_HISTORICAL_IDEAS, raw.hasGraphicalCulture);
            hotData.SetFlag(CountryHotData.FLAG_HAS_PREFERRED_RELIGION, raw.hasPreferredReligion);
            hotData.SetFlag(CountryHotData.FLAG_HAS_REVOLUTIONARY_COLORS, raw.hasRevolutionaryColors);
        }

        /// <summary>
        /// Compute a hash for the country tag (same algorithm as used elsewhere)
        /// Burst-compatible version using FixedString64Bytes
        /// </summary>
        private ushort ComputeTagHash(FixedString64Bytes tag)
        {
            if (tag.Length == 0)
                return 0;

            uint hash = 2166136261u;
            for (int i = 0; i < tag.Length; i++)
            {
                hash ^= tag[i];
                hash *= 16777619u;
            }
            return (ushort)(hash & 0xFFFF);
        }
    }

    /// <summary>
    /// Job result containing processed country hot data
    /// Cold data is handled separately on the main thread
    /// </summary>
    public struct CountryProcessingResult
    {
        public bool success;
        public NativeArray<CountryHotData> hotData;
        public int processedCount;
        public string errorMessage;

        public static CountryProcessingResult Success(NativeArray<CountryHotData> hot, int count)
        {
            return new CountryProcessingResult
            {
                success = true,
                hotData = hot,
                processedCount = count,
                errorMessage = ""
            };
        }

        public static CountryProcessingResult Failure(string error)
        {
            return new CountryProcessingResult
            {
                success = false,
                hotData = new NativeArray<CountryHotData>(),
                processedCount = 0,
                errorMessage = error
            };
        }

        public void Dispose()
        {
            if (hotData.IsCreated)
                hotData.Dispose();
        }
    }
}