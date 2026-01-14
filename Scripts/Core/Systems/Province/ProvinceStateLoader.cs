using Unity.Collections;
using Core.Data;
using Core.Loaders;
using System;

namespace Core.Systems.Province
{
    /// <summary>
    /// Handles loading and applying province initial states
    /// Extracted from ProvinceSystem.cs for better separation of concerns
    /// </summary>
    public class ProvinceStateLoader
    {
        private readonly ProvinceDataManager dataManager;
        private readonly EventBus eventBus;
        private readonly ProvinceHistoryDatabase historyDatabase;

        private const ushort UNOWNED_COUNTRY = 0;

        public ProvinceStateLoader(ProvinceDataManager dataManager, EventBus eventBus, ProvinceHistoryDatabase historyDatabase)
        {
            this.dataManager = dataManager;
            this.eventBus = eventBus;
            this.historyDatabase = historyDatabase;
        }

        /// <summary>
        /// Load province initial states using Burst-compiled parallel jobs
        /// Architecture-compliant: hot/cold separation, bounded data, parallel processing
        /// </summary>
        public void LoadProvinceInitialStates(string dataDirectory)
        {
            ArchonLogger.Log($"Loading province initial states from {dataDirectory} using Burst jobs", "core_simulation");

            var result = BurstProvinceHistoryLoader.LoadProvinceInitialStates(dataDirectory);

            if (!result.IsSuccess)
            {
                ArchonLogger.LogError($"Failed to load province initial states: {result.ErrorMessage}", "core_simulation");
                return;
            }

            ArchonLogger.Log($"Province initial states loaded: {result.LoadedCount} successful, {result.FailedCount} failed", "core_simulation");

            ApplyInitialStates(result.InitialStates);

            result.Dispose();

            eventBus?.Emit(new ProvinceInitialStatesLoadedEvent
            {
                LoadedCount = result.LoadedCount,
                FailedCount = result.FailedCount
            });
        }

        /// <summary>
        /// Load province initial states but return them for reference resolution before applying
        /// Used by data linking architecture to resolve string references to IDs
        /// </summary>
        public ProvinceInitialStateLoadResult LoadProvinceInitialStatesForLinking(string dataDirectory)
        {
            ArchonLogger.Log($"Loading province initial states for reference linking from {dataDirectory}", "core_simulation");

            var result = BurstProvinceHistoryLoader.LoadProvinceInitialStates(dataDirectory);

            if (!result.IsSuccess)
            {
                ArchonLogger.LogError($"Failed to load province initial states: {result.ErrorMessage}", "core_simulation");
                return result;
            }

            ArchonLogger.Log($"Province initial states loaded for linking: {result.LoadedCount} successful, {result.FailedCount} failed", "core_simulation");

            // Return the raw data WITHOUT applying it - caller will resolve references first
            return result;
        }

        /// <summary>
        /// Apply resolved initial states to hot province data after reference resolution
        /// </summary>
        public void ApplyResolvedInitialStates(NativeArray<ProvinceInitialState> initialStates)
        {
            ApplyInitialStates(initialStates);

            eventBus?.Emit(new ProvinceInitialStatesLoadedEvent
            {
                LoadedCount = initialStates.Length,
                FailedCount = 0 // Already filtered during resolution
            });
        }

        /// <summary>
        /// Apply initial states to hot province data
        /// Only touches hot data needed for simulation
        /// </summary>
        private void ApplyInitialStates(NativeArray<ProvinceInitialState> initialStates)
        {
            int appliedCount = 0;

            for (int i = 0; i < initialStates.Length; i++)
            {
                var initialState = initialStates[i];
                if (!initialState.IsValid)
                    continue;

                var provinceId = (ushort)initialState.ProvinceID;

                if (!dataManager.HasProvince(provinceId))
                {
                    ArchonLogger.LogWarning($"Province {initialState.ProvinceID} has initial state but doesn't exist in map data", "core_simulation");
                    continue;
                }

                ApplyInitialStateToProvince(provinceId, initialState);
                appliedCount++;
            }

            ArchonLogger.Log($"Applied initial state to {appliedCount} provinces", "core_simulation");
        }

        /// <summary>
        /// Apply initial state to hot province data only
        /// </summary>
        private void ApplyInitialStateToProvince(ushort provinceId, ProvinceInitialState initialState)
        {
            // Convert to hot ProvinceState and store (8 bytes)
            var state = initialState.ToProvinceState();
            dataManager.SetProvinceState(provinceId, state);

            // Add initial ownership event to cold data (history database)
            if (initialState.OwnerID != UNOWNED_COUNTRY)
            {
                var ownershipEvent = HistoricalEvent.CreateOwnershipChange(
                    new DateTime(1444, 11, 11), // EU4 start date
                    UNOWNED_COUNTRY,
                    initialState.OwnerID
                );
                historyDatabase.AddEvent(initialState.ProvinceID, ownershipEvent);
            }
        }
    }
}
