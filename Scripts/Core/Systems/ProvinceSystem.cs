using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;
using Core.Data;
using ParadoxParser.Jobs;
using Core.Systems.Province;
using Utils;

namespace Core.Systems
{
    /// <summary>
    /// Single source of truth for all province data
    /// Owns the 8-byte ProvinceState array for deterministic simulation
    /// Manages province ownership, development, terrain, and flags
    /// Performance: Structure of Arrays design, Burst-compatible, zero allocations
    /// </summary>
    public class ProvinceSystem : MonoBehaviour, System.IDisposable
    {
        [Header("Configuration")]
        [SerializeField] private int initialCapacity = 10000;

        // Core components (refactored for better separation)
        private ProvinceDataManager dataManager;
        private ProvinceStateLoader stateLoader;
        private ProvinceHistoryDatabase historyDatabase;

        // Native arrays (owned by this class, passed to components)
        private NativeArray<ProvinceState> provinceStates;
        private NativeHashMap<ushort, int> idToIndex;
        private NativeList<ushort> activeProvinceIds;

        // State
        private bool isInitialized;
        private EventBus eventBus;

        // Properties
        public int ProvinceCount => dataManager?.ProvinceCount ?? 0;
        public int Capacity => provinceStates.IsCreated ? provinceStates.Length : 0;
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Initialize the province system with event bus
        /// </summary>
        public void Initialize(EventBus eventBus)
        {
            this.eventBus = eventBus;

            // Allocate native arrays with initial capacity (8 bytes per province)
            provinceStates = new NativeArray<ProvinceState>(initialCapacity, Allocator.Persistent);
            idToIndex = new NativeHashMap<ushort, int>(initialCapacity, Allocator.Persistent);
            activeProvinceIds = new NativeList<ushort>(initialCapacity, Allocator.Persistent);

            historyDatabase = new ProvinceHistoryDatabase();

            // Initialize components with references to native arrays
            dataManager = new ProvinceDataManager(provinceStates, idToIndex, activeProvinceIds, eventBus);
            stateLoader = new ProvinceStateLoader(dataManager, eventBus, historyDatabase);

            isInitialized = true;
            ArchonLogger.Log($"ProvinceSystem initialized with capacity {initialCapacity} (8 bytes per province = {initialCapacity * 8 / 1024}KB total)");

            // Validate ProvinceState is exactly 8 bytes
            ValidateProvinceStateSize();
        }

        /// <summary>
        /// Initialize provinces from JSON5 + Burst loaded province states
        /// </summary>
        public void InitializeFromProvinceStates(ProvinceInitialStateLoadResult loadResult)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem not initialized - call Initialize() first");
                return;
            }

            if (!loadResult.Success)
            {
                ArchonLogger.LogError($"Cannot initialize from failed load result: {loadResult.ErrorMessage}");
                return;
            }

            ArchonLogger.Log($"Initializing {loadResult.LoadedCount} provinces from JSON5 + Burst data");

            // Clear existing data
            dataManager.Clear();

            // Process each loaded province
            for (int i = 0; i < loadResult.InitialStates.Length; i++)
            {
                var initialState = loadResult.InitialStates[i];

                if (!initialState.IsValid)
                    continue;

                ushort provinceId = (ushort)initialState.ProvinceID;

                // Add province to system with basic initialization
                dataManager.AddProvince(provinceId, initialState.Terrain);

                // Store the initial state for later reference linking
                // The actual ownership and other data will be applied after reference resolution
            }

            ArchonLogger.Log($"ProvinceSystem initialized with {ProvinceCount} provinces (ownership will be resolved in linking phase)");

            // Emit initialization complete event
            eventBus?.Emit(new ProvinceSystemInitializedEvent
            {
                ProvinceCount = ProvinceCount
            });
        }


        // ===== DATA ACCESS OPERATIONS (delegated to ProvinceDataManager) =====

        public ushort GetProvinceOwner(ushort provinceId) => dataManager.GetProvinceOwner(provinceId);
        public void SetProvinceOwner(ushort provinceId, ushort newOwner) => dataManager.SetProvinceOwner(provinceId, newOwner);
        public byte GetProvinceDevelopment(ushort provinceId) => dataManager.GetProvinceDevelopment(provinceId);
        public void SetProvinceDevelopment(ushort provinceId, byte development) => dataManager.SetProvinceDevelopment(provinceId, development);
        public byte GetProvinceTerrain(ushort provinceId) => dataManager.GetProvinceTerrain(provinceId);
        public void SetProvinceTerrain(ushort provinceId, byte terrain) => dataManager.SetProvinceTerrain(provinceId, terrain);
        public ProvinceState GetProvinceState(ushort provinceId) => dataManager.GetProvinceState(provinceId);
        public NativeArray<ushort> GetCountryProvinces(ushort countryId, Allocator allocator = Allocator.TempJob) => dataManager.GetCountryProvinces(countryId, allocator);
        public NativeArray<ushort> GetAllProvinceIds(Allocator allocator = Allocator.TempJob) => dataManager.GetAllProvinceIds(allocator);
        public bool HasProvince(ushort provinceId) => dataManager.HasProvince(provinceId);

        // ===== LOADING OPERATIONS (delegated to ProvinceStateLoader) =====

        public void LoadProvinceInitialStates(string dataDirectory)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem not initialized - call Initialize() first");
                return;
            }
            stateLoader.LoadProvinceInitialStates(dataDirectory);
        }

        public ProvinceInitialStateLoadResult LoadProvinceInitialStatesForLinking(string dataDirectory)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem not initialized - call Initialize() first");
                return ProvinceInitialStateLoadResult.Failed("ProvinceSystem not initialized");
            }
            return stateLoader.LoadProvinceInitialStatesForLinking(dataDirectory);
        }

        public void ApplyResolvedInitialStates(NativeArray<ProvinceInitialState> initialStates)
        {
            stateLoader.ApplyResolvedInitialStates(initialStates);
        }

        // ===== HISTORY OPERATIONS (delegated to ProvinceHistoryDatabase) =====

        public List<HistoricalEvent> GetRecentHistory(ushort provinceId, int maxEvents = 10) => historyDatabase.GetRecentEvents(provinceId, maxEvents);
        public ProvinceHistorySummary GetHistorySummary(ushort provinceId) => historyDatabase.GetHistorySummary(provinceId);
        public void AddHistoricalEvent(ushort provinceId, HistoricalEvent evt) => historyDatabase.AddEvent(provinceId, evt);
        public HistoryDatabaseStats GetHistoryStats() => historyDatabase.GetStats();

        /// <summary>
        /// Validate that ProvinceState is exactly 8 bytes
        /// </summary>
        private void ValidateProvinceStateSize()
        {
            int actualSize = UnsafeUtility.SizeOf<ProvinceState>();
            if (actualSize != 8)
            {
                ArchonLogger.LogError($"ProvinceState size validation failed: expected 8 bytes, got {actualSize} bytes");
            }
            else
            {
                ArchonLogger.Log("ProvinceState size validation passed: 8 bytes");
            }
        }

        public void Dispose()
        {
            if (provinceStates.IsCreated) provinceStates.Dispose();
            if (idToIndex.IsCreated) idToIndex.Dispose();
            if (activeProvinceIds.IsCreated) activeProvinceIds.Dispose();

            historyDatabase?.Dispose();

            isInitialized = false;
            ArchonLogger.Log("ProvinceSystem disposed");
        }

        void OnDestroy()
        {
            Dispose();
        }

        #if UNITY_EDITOR
        [Header("Debug Info")]
        [SerializeField, ReadOnly] private int debugProvinceCount;
        [SerializeField, ReadOnly] private int debugMemoryUsageKB;
        [SerializeField, ReadOnly] private bool debugIsInitialized;

        void Update()
        {
            if (isInitialized)
            {
                debugProvinceCount = ProvinceCount;
                debugMemoryUsageKB = (ProvinceCount * 8) / 1024; // 8 bytes per province
                debugIsInitialized = isInitialized;
            }
        }
        #endif
    }

    // Province-related events moved to Province/ProvinceEvents.cs
}