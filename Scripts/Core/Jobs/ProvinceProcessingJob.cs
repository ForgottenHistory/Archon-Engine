using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Core.Data;

namespace Core.Jobs
{
    /// <summary>
    /// Burst-compiled job for processing province data
    /// Phase 2 of the hybrid JSON5 + Burst architecture
    /// Converts RawProvinceData to final ProvinceInitialState
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ProvinceProcessingJob : IJobParallelFor
    {
        // Input data from JSON5 loading
        [ReadOnly] public NativeArray<RawProvinceData> rawData;

        // Output data for game state
        [WriteOnly] public NativeArray<ProvinceInitialState> results;

        // Processing options
        [ReadOnly] public bool validateData;
        [ReadOnly] public bool applyDefaults;

        public void Execute(int index)
        {
            var raw = rawData[index];
            var result = ProcessSingleProvince(raw);
            results[index] = result;
        }

        /// <summary>
        /// Process a single province's raw data into final game state
        /// ENGINE layer only processes generic fields (provinceID, owner, controller)
        /// Game-specific fields (culture, religion, development, etc.) should be
        /// processed by game layer jobs/loaders
        /// </summary>
        private ProvinceInitialState ProcessSingleProvince(RawProvinceData raw)
        {
            // Create initial state
            var state = ProvinceInitialState.Create(raw.provinceID);

            // Copy ownership data (generic - all games have ownership)
            state.OwnerTag = raw.owner;
            state.ControllerTag = raw.controller;

            // Set default terrain - will be overwritten by ProvinceTerrainAnalyzer (Map layer)
            // Use terrain ID 1 (grasslands) instead of 0 (ocean)
            state.Terrain = 1;

            // Apply validation if enabled
            if (validateData)
            {
                ValidateProvinceData(ref state);
            }

            // Apply defaults if enabled
            if (applyDefaults)
            {
                ApplyDefaultValues(ref state);
            }

            return state;
        }

        /// <summary>
        /// Validate province data and fix common issues
        /// ENGINE layer only validates generic fields
        /// </summary>
        private void ValidateProvinceData(ref ProvinceInitialState state)
        {
            // ENGINE layer has minimal validation for generic fields
            // Game-specific validation should be done by game layer
        }

        /// <summary>
        /// Apply default values for missing data
        /// ENGINE layer only sets defaults for generic fields
        /// </summary>
        private void ApplyDefaultValues(ref ProvinceInitialState state)
        {
            // Check if owner tag is empty or default
            if (state.OwnerTag.Length == 0)
            {
                // Set default values - use empty default() constructor which is Burst-compatible
                state.OwnerTag = default;
                state.ControllerTag = default;
            }
        }
    }

    /// <summary>
    /// Job result containing processed province data
    /// </summary>
    public struct ProvinceProcessingResult
    {
        public bool success;
        public NativeArray<ProvinceInitialState> provinces;
        public int processedCount;
        public string errorMessage;

        public static ProvinceProcessingResult Success(NativeArray<ProvinceInitialState> data, int count)
        {
            return new ProvinceProcessingResult
            {
                success = true,
                provinces = data,
                processedCount = count,
                errorMessage = ""
            };
        }

        public static ProvinceProcessingResult Failure(string error)
        {
            return new ProvinceProcessingResult
            {
                success = false,
                provinces = new NativeArray<ProvinceInitialState>(),
                processedCount = 0,
                errorMessage = error
            };
        }

        public void Dispose()
        {
            if (provinces.IsCreated)
                provinces.Dispose();
        }
    }
}