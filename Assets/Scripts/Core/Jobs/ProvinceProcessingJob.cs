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
        /// </summary>
        private ProvinceInitialState ProcessSingleProvince(RawProvinceData raw)
        {
            // Create initial state
            var state = ProvinceInitialState.Create(raw.provinceID);

            // Copy string data
            state.OwnerTag = raw.owner;
            state.ControllerTag = raw.controller;
            state.Culture = raw.culture;
            state.Religion = raw.religion;
            state.TradeGood = raw.tradeGood;

            // Copy numeric data (need explicit casts to byte with bounds checking)
            state.BaseTax = (byte)Unity.Mathematics.math.clamp(raw.baseTax, 0, 255);
            state.BaseProduction = (byte)Unity.Mathematics.math.clamp(raw.baseProduction, 0, 255);
            state.BaseManpower = (byte)Unity.Mathematics.math.clamp(raw.baseManpower, 0, 255);
            state.CenterOfTrade = (byte)Unity.Mathematics.math.clamp(raw.centerOfTrade, 0, 255);

            // Pack boolean flags into Flags byte
            state.PackFlags(raw.isCity, raw.hre);

            // Set default terrain (terrain.bmp not loaded yet)
            // Use terrain ID 1 (likely grassland/plains) instead of 0 (likely ocean)
            state.Terrain = 1;

            // Note: extraCost from raw data is not stored in ProvinceInitialState
            // It's part of the detailed province data, not the hot simulation state
            // TODO: Load terrain data from terrain.bmp and map to provinces

            // Calculate development
            state.CalculateDevelopment();

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
        /// </summary>
        private void ValidateProvinceData(ref ProvinceInitialState state)
        {
            // Ensure minimum values
            if (state.BaseTax < 1) state.BaseTax = 1;
            if (state.BaseProduction < 1) state.BaseProduction = 1;
            if (state.BaseManpower < 1) state.BaseManpower = 1;

            // Validate center of trade levels
            if (state.CenterOfTrade > 3) state.CenterOfTrade = 3;
        }

        /// <summary>
        /// Apply default values for missing data
        /// </summary>
        private void ApplyDefaultValues(ref ProvinceInitialState state)
        {
            // If no owner, mark as uncolonized
            if (state.OwnerTag.ToString() == "---" || state.OwnerTag.Length == 0)
            {
                state.OwnerTag = new FixedString64Bytes("---");
                state.ControllerTag = new FixedString64Bytes("---");
            }

            // Default trade good
            if (state.TradeGood.ToString() == "unknown" || state.TradeGood.Length == 0)
            {
                state.TradeGood = new FixedString64Bytes("grain");
            }

            // Default culture and religion for uncolonized
            if (state.OwnerTag.ToString() == "---")
            {
                state.Culture = new FixedString64Bytes("unknown");
                state.Religion = new FixedString64Bytes("unknown");
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

        public static ProvinceProcessingResult Failed(string error)
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