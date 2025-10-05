using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using Core.Data;
using Core.Loaders;
using ParadoxParser.Jobs;
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

        // Core simulation data - EXACTLY 8 bytes per province (dual-layer architecture)
        // Uses Array of Structures (AoS) pattern for grand strategy access patterns
        // All fields accessed together (owner + development + terrain) for province queries
        private NativeArray<ProvinceState> provinceStates;

        // Province ID management
        private NativeHashMap<ushort, int> idToIndex;    // Province ID -> Array index lookup
        private NativeList<ushort> activeProvinceIds;    // List of valid province IDs

        // Performance tracking
        private int provinceCount;
        private bool isInitialized;
        private EventBus eventBus;

        // Cold data: Historical events and detailed province information
        private ProvinceHistoryDatabase historyDatabase;

        // Constants
        private const byte OCEAN_TERRAIN = 0;
        private const ushort UNOWNED_COUNTRY = 0;

        // Properties
        public int ProvinceCount => provinceCount;
        public int Capacity => provinceStates.IsCreated ? provinceStates.Length : 0;
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Initialize the province system with event bus
        /// </summary>
        public void Initialize(EventBus eventBus)
        {
            this.eventBus = eventBus;

            // Allocate native array with initial capacity (8 bytes per province)
            provinceStates = new NativeArray<ProvinceState>(initialCapacity, Allocator.Persistent);

            idToIndex = new NativeHashMap<ushort, int>(initialCapacity, Allocator.Persistent);
            activeProvinceIds = new NativeList<ushort>(initialCapacity, Allocator.Persistent);

            historyDatabase = new ProvinceHistoryDatabase();

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
            provinceCount = 0;
            idToIndex.Clear();
            activeProvinceIds.Clear();

            // Process each loaded province
            for (int i = 0; i < loadResult.InitialStates.Length; i++)
            {
                var initialState = loadResult.InitialStates[i];

                if (!initialState.IsValid)
                    continue;

                ushort provinceId = (ushort)initialState.ProvinceID;

                // Add province to system with basic initialization
                AddProvince(provinceId, initialState.Terrain);

                // Store the initial state for later reference linking
                // The actual ownership and other data will be applied after reference resolution
            }

            ArchonLogger.Log($"ProvinceSystem initialized with {provinceCount} provinces (ownership will be resolved in linking phase)");

            // Emit initialization complete event
            eventBus?.Emit(new ProvinceSystemInitializedEvent
            {
                ProvinceCount = provinceCount
            });
        }

        /// <summary>
        /// Add a new province to the system
        /// </summary>
        private void AddProvince(ushort provinceId, byte terrainType)
        {
            if (provinceCount >= provinceStates.Length)
            {
                ArchonLogger.LogError($"Province capacity exceeded: {provinceCount}/{provinceStates.Length}");
                return;
            }

            // Check for duplicate province ID
            if (idToIndex.ContainsKey(provinceId))
            {
                ArchonLogger.LogWarning($"Province {provinceId} already exists, skipping");
                return;
            }

            int arrayIndex = provinceCount;

            // Create and store province state (8 bytes)
            var provinceState = ProvinceState.CreateDefault(terrainType);
            provinceStates[arrayIndex] = provinceState;

            // Update lookup tables
            idToIndex[provinceId] = arrayIndex;
            activeProvinceIds.Add(provinceId);

            provinceCount++;
        }


        /// <summary>
        /// Get province owner - most common query (must be ultra-fast)
        /// </summary>
        public ushort GetProvinceOwner(ushort provinceId)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return UNOWNED_COUNTRY;

            var ownerID = provinceStates[arrayIndex].ownerID;

            // DEBUG: Log first few queries for Cuenca provinces
            if ((provinceId == 2751 || provinceId == 817) && queryCount < 5)
            {
                ArchonLogger.Log($"ProvinceSystem.GetProvinceOwner: Province {provinceId} → ownerID={ownerID}, arrayIndex={arrayIndex}");
                queryCount++;
            }

            return ownerID;
        }

        private static int queryCount = 0; // DEBUG counter

        /// <summary>
        /// Set province owner and emit events
        /// </summary>
        public void SetProvinceOwner(ushort provinceId, ushort newOwner)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
            {
                ArchonLogger.LogWarning($"Cannot set owner for invalid province {provinceId}");
                return;
            }

            var state = provinceStates[arrayIndex];
            ushort oldOwner = state.ownerID;
            if (oldOwner == newOwner)
                return; // No change

            // Update state and write back (structs are value types)
            state.ownerID = newOwner;
            provinceStates[arrayIndex] = state;

            // Emit ownership change event
            eventBus?.Emit(new ProvinceOwnershipChangedEvent
            {
                ProvinceId = provinceId,
                OldOwner = oldOwner,
                NewOwner = newOwner
            });
        }

        /// <summary>
        /// Get province development level
        /// </summary>
        public byte GetProvinceDevelopment(ushort provinceId)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return 0;

            return provinceStates[arrayIndex].development;
        }

        /// <summary>
        /// Set province development level
        /// </summary>
        public void SetProvinceDevelopment(ushort provinceId, byte development)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return;

            var state = provinceStates[arrayIndex];
            byte oldDevelopment = state.development;
            if (oldDevelopment == development)
                return;

            // Update state and write back (structs are value types)
            state.development = development;
            provinceStates[arrayIndex] = state;

            // Emit development change event
            eventBus?.Emit(new ProvinceDevelopmentChangedEvent
            {
                ProvinceId = provinceId,
                OldDevelopment = oldDevelopment,
                NewDevelopment = development
            });
        }

        /// <summary>
        /// Get province terrain type
        /// </summary>
        public byte GetProvinceTerrain(ushort provinceId)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return OCEAN_TERRAIN;

            return provinceStates[arrayIndex].terrain;
        }

        /// <summary>
        /// Set province terrain type
        /// </summary>
        public void SetProvinceTerrain(ushort provinceId, byte terrain)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return;

            // Update state and write back (structs are value types)
            var state = provinceStates[arrayIndex];
            state.terrain = terrain;
            provinceStates[arrayIndex] = state;
        }

        /// <summary>
        /// Get complete province state (8-byte struct)
        /// </summary>
        public ProvinceState GetProvinceState(ushort provinceId)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return ProvinceState.CreateDefault(OCEAN_TERRAIN);

            return provinceStates[arrayIndex];
        }

        /// <summary>
        /// Get all provinces owned by a specific country
        /// Returns a native array that must be disposed by caller
        /// </summary>
        public NativeArray<ushort> GetCountryProvinces(ushort countryId, Allocator allocator = Allocator.TempJob)
        {
            var result = new NativeList<ushort>(provinceCount / 10, Allocator.Temp);

            for (int i = 0; i < provinceCount; i++)
            {
                if (provinceStates[i].ownerID == countryId)
                {
                    result.Add(activeProvinceIds[i]);
                }
            }

            var resultArray = new NativeArray<ushort>(result.Length, allocator);
            result.AsArray().CopyTo(resultArray);
            result.Dispose();

            return resultArray;
        }

        /// <summary>
        /// Get all active province IDs
        /// </summary>
        public NativeArray<ushort> GetAllProvinceIds(Allocator allocator = Allocator.TempJob)
        {
            var result = new NativeArray<ushort>(provinceCount, allocator);
            for (int i = 0; i < provinceCount; i++)
            {
                result[i] = activeProvinceIds[i];
            }
            return result;
        }

        /// <summary>
        /// Check if province exists
        /// </summary>
        public bool HasProvince(ushort provinceId)
        {
            return idToIndex.ContainsKey(provinceId);
        }

        /// <summary>
        /// Load province initial states using Burst-compiled parallel jobs
        /// Architecture-compliant: hot/cold separation, bounded data, parallel processing
        /// </summary>
        public void LoadProvinceInitialStates(string dataDirectory)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem not initialized - call Initialize() first");
                return;
            }

            ArchonLogger.Log($"Loading province initial states from {dataDirectory} using Burst jobs");

            var result = BurstProvinceHistoryLoader.LoadProvinceInitialStates(dataDirectory);

            if (!result.Success)
            {
                ArchonLogger.LogError($"Failed to load province initial states: {result.ErrorMessage}");
                return;
            }

            ArchonLogger.Log($"Province initial states loaded: {result.LoadedCount} successful, {result.FailedCount} failed");

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
            if (!isInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem not initialized - call Initialize() first");
                return ProvinceInitialStateLoadResult.Failed("ProvinceSystem not initialized");
            }

            ArchonLogger.Log($"Loading province initial states for reference linking from {dataDirectory}");

            var result = BurstProvinceHistoryLoader.LoadProvinceInitialStates(dataDirectory);

            if (!result.Success)
            {
                ArchonLogger.LogError($"Failed to load province initial states: {result.ErrorMessage}");
                return result;
            }

            ArchonLogger.Log($"Province initial states loaded for linking: {result.LoadedCount} successful, {result.FailedCount} failed");

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

                if (!HasProvince(provinceId))
                {
                    ArchonLogger.LogWarning($"Province {initialState.ProvinceID} has initial state but doesn't exist in map data");
                    continue;
                }

                ApplyInitialStateToProvince(provinceId, initialState);
                appliedCount++;
            }

            ArchonLogger.Log($"Applied initial state to {appliedCount} provinces");
        }

        /// <summary>
        /// Apply initial state to hot province data only
        /// </summary>
        private void ApplyInitialStateToProvince(ushort provinceId, ProvinceInitialState initialState)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return;

            // DEBUG: Log Spanish Cuenca and Incan Cuenca
            if (provinceId == 2751 || provinceId == 817)
            {
                ArchonLogger.Log($"ProvinceSystem.ApplyInitialState: Province {provinceId} BEFORE ToProvinceState() → initialState.OwnerID={initialState.OwnerID}");
            }

            // Convert to hot ProvinceState and store (8 bytes)
            var state = initialState.ToProvinceState();
            provinceStates[arrayIndex] = state;

            // DEBUG: Log Spanish Cuenca and Incan Cuenca
            if (provinceId == 2751 || provinceId == 817)
            {
                ArchonLogger.Log($"ProvinceSystem.ApplyInitialState: Province {provinceId} AFTER ToProvinceState() → state.ownerID={state.ownerID}, arrayIndex={arrayIndex}");
            }

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

        /// <summary>
        /// Get recent historical events for province (cold data access)
        /// </summary>
        public List<HistoricalEvent> GetRecentHistory(ushort provinceId, int maxEvents = 10)
        {
            return historyDatabase.GetRecentEvents(provinceId, maxEvents);
        }

        /// <summary>
        /// Get compressed historical summary for province (cold data access)
        /// </summary>
        public ProvinceHistorySummary GetHistorySummary(ushort provinceId)
        {
            return historyDatabase.GetHistorySummary(provinceId);
        }

        /// <summary>
        /// Add historical event (for ongoing simulation)
        /// </summary>
        public void AddHistoricalEvent(ushort provinceId, HistoricalEvent evt)
        {
            historyDatabase.AddEvent(provinceId, evt);
        }

        /// <summary>
        /// Get history database statistics
        /// </summary>
        public HistoryDatabaseStats GetHistoryStats()
        {
            return historyDatabase.GetStats();
        }

        /// <summary>
        /// Determine terrain type from RGB color (basic heuristic)
        /// </summary>
        private byte DetermineTerrainFromColor(int packedRGB)
        {
            // Extract RGB components
            int r = (packedRGB >> 16) & 0xFF;
            int g = (packedRGB >> 8) & 0xFF;
            int b = packedRGB & 0xFF;

            // Simple terrain detection based on color
            if (r < 50 && g < 50 && b > 150) return 0; // Ocean (dark blue)
            if (g > r && g > b) return 1;              // Grassland (green)
            if (r > 150 && g > 150 && b < 100) return 2; // Desert (yellow)
            if (r > 100 && g < 100 && b < 100) return 3; // Mountain (brown)

            return 1; // Default to grassland
        }

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
                debugProvinceCount = provinceCount;
                debugMemoryUsageKB = (provinceCount * 8) / 1024; // 8 bytes per province
                debugIsInitialized = isInitialized;
            }
        }
        #endif
    }

    // Province-related events
    public struct ProvinceSystemInitializedEvent : IGameEvent
    {
        public int ProvinceCount;
        public bool HasDefinitions;
        public float TimeStamp { get; set; }
    }

    public struct ProvinceOwnershipChangedEvent : IGameEvent
    {
        public ushort ProvinceId;
        public ushort OldOwner;
        public ushort NewOwner;
        public float TimeStamp { get; set; }
    }

    public struct ProvinceDevelopmentChangedEvent : IGameEvent
    {
        public ushort ProvinceId;
        public byte OldDevelopment;
        public byte NewDevelopment;
        public float TimeStamp { get; set; }
    }

    public struct ProvinceInitialStatesLoadedEvent : IGameEvent
    {
        public int LoadedCount;
        public int FailedCount;
        public float TimeStamp { get; set; }
    }
}