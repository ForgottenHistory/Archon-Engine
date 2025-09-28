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
        [SerializeField] private bool enablePerformanceMonitoring = true;

        // Core simulation data - EXACTLY 8 bytes per province (dual-layer architecture)
        private NativeArray<ProvinceState> provinceStates;

        // Structure of Arrays for cache-friendly access
        private NativeArray<ushort> provinceOwners;      // Most accessed - separate for performance (supports 65535 countries)
        private NativeArray<ushort> provinceControllers; // Second most accessed - separate for occupation mechanics
        private NativeArray<byte> provinceDevelopment;   // Third most accessed
        private NativeArray<byte> provinceTerrain;       // Used for pathfinding, visuals
        private NativeArray<byte> provinceFlags;         // Least accessed

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

            // Allocate native arrays with initial capacity
            provinceStates = new NativeArray<ProvinceState>(initialCapacity, Allocator.Persistent);
            provinceOwners = new NativeArray<ushort>(initialCapacity, Allocator.Persistent);
            provinceControllers = new NativeArray<ushort>(initialCapacity, Allocator.Persistent);
            provinceDevelopment = new NativeArray<byte>(initialCapacity, Allocator.Persistent);
            provinceTerrain = new NativeArray<byte>(initialCapacity, Allocator.Persistent);
            provinceFlags = new NativeArray<byte>(initialCapacity, Allocator.Persistent);

            idToIndex = new NativeHashMap<ushort, int>(initialCapacity, Allocator.Persistent);
            activeProvinceIds = new NativeList<ushort>(initialCapacity, Allocator.Persistent);

            historyDatabase = new ProvinceHistoryDatabase();

            isInitialized = true;
            DominionLogger.Log($"ProvinceSystem initialized with capacity {initialCapacity}");

            // Validate ProvinceState is exactly 8 bytes
            ValidateProvinceStateSize();
        }

        /// <summary>
        /// Initialize provinces from ProvinceMapProcessor result
        /// </summary>
        public void InitializeFromMapData(ProvinceMapResult mapResult)
        {
            if (!isInitialized)
            {
                DominionLogger.LogError("ProvinceSystem not initialized - call Initialize() first");
                return;
            }

            if (!mapResult.Success)
            {
                DominionLogger.LogError($"Cannot initialize from failed map result: {mapResult.ErrorMessage}");
                return;
            }

            DominionLogger.Log($"Initializing {mapResult.ProvinceMappings.ColorToProvinceID.Count} provinces from bitmap data (unique colors found)");

            // Clear existing data
            provinceCount = 0;
            idToIndex.Clear();
            activeProvinceIds.Clear();

            // Process each province from the map data
            var colorEnumerator = mapResult.ProvinceMappings.ColorToProvinceID.GetEnumerator();
            while (colorEnumerator.MoveNext())
            {
                var provinceId = (ushort)colorEnumerator.Current.Value;
                var colorRGB = colorEnumerator.Current.Key;

                // Add province to system
                AddProvince(provinceId, DetermineTerrainFromColor(colorRGB));
            }

            // Apply province definitions if available
            if (mapResult.HasDefinitions)
            {
                DominionLogger.Log($"Applying province definitions from definition.csv ({mapResult.Definitions.AllDefinitions.Length} definitions available)");
                ApplyProvinceDefinitions(mapResult.Definitions);
            }
            else
            {
                DominionLogger.Log("No province definitions available - provinces will use color-based terrain detection");
            }

            DominionLogger.Log($"ProvinceSystem initialized with {provinceCount} provinces (bitmap data + definitions applied)");

            // Emit initialization complete event
            eventBus?.Emit(new ProvinceSystemInitializedEvent
            {
                ProvinceCount = provinceCount,
                HasDefinitions = mapResult.HasDefinitions
            });
        }

        /// <summary>
        /// Add a new province to the system
        /// </summary>
        private void AddProvince(ushort provinceId, byte terrainType)
        {
            if (provinceCount >= provinceStates.Length)
            {
                DominionLogger.LogError($"Province capacity exceeded: {provinceCount}/{provinceStates.Length}");
                return;
            }

            // Check for duplicate province ID
            if (idToIndex.ContainsKey(provinceId))
            {
                DominionLogger.LogWarning($"Province {provinceId} already exists, skipping");
                return;
            }

            int arrayIndex = provinceCount;

            // Create province state
            var provinceState = ProvinceState.CreateDefault(terrainType);

            // Set data in structure of arrays
            provinceStates[arrayIndex] = provinceState;
            provinceOwners[arrayIndex] = provinceState.ownerID;
            provinceControllers[arrayIndex] = provinceState.controllerID;
            provinceDevelopment[arrayIndex] = provinceState.development;
            provinceTerrain[arrayIndex] = provinceState.terrain;
            provinceFlags[arrayIndex] = provinceState.flags;

            // Update lookup tables
            idToIndex[provinceId] = arrayIndex;
            activeProvinceIds.Add(provinceId);

            provinceCount++;
        }

        /// <summary>
        /// Apply province definitions from definition.csv
        /// </summary>
        private void ApplyProvinceDefinitions(ProvinceDefinitionMappings definitions)
        {
            if (!definitions.Success)
                return;

            DominionLogger.Log($"Applying definitions to {definitions.AllDefinitions.Length} provinces");

            int updatedCount = 0;

            for (int i = 0; i < definitions.AllDefinitions.Length; i++)
            {
                var definition = definitions.AllDefinitions[i];
                if (!definition.IsValid)
                    continue;

                var provinceId = (ushort)definition.ID;

                // Find province in our system
                if (idToIndex.TryGetValue(provinceId, out int arrayIndex))
                {
                    // Update terrain based on definition (if needed)
                    var terrainType = DetermineTerrainFromDefinition(definition);
                    if (terrainType != provinceTerrain[arrayIndex])
                    {
                        SetProvinceTerrain(provinceId, terrainType);
                        updatedCount++;
                    }
                }
            }

            DominionLogger.Log($"Province definitions applied: {updatedCount} terrain updates from {definitions.AllDefinitions.Length} definitions");
        }

        /// <summary>
        /// Get province owner - most common query (must be ultra-fast)
        /// </summary>
        public ushort GetProvinceOwner(ushort provinceId)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return UNOWNED_COUNTRY;

            return provinceOwners[arrayIndex];
        }

        /// <summary>
        /// Set province owner and emit events
        /// </summary>
        public void SetProvinceOwner(ushort provinceId, ushort newOwner)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
            {
                DominionLogger.LogWarning($"Cannot set owner for invalid province {provinceId}");
                return;
            }

            ushort oldOwner = provinceOwners[arrayIndex];
            if (oldOwner == newOwner)
                return; // No change

            // Update data
            provinceOwners[arrayIndex] = newOwner;

            // Update the full ProvinceState for consistency
            var state = provinceStates[arrayIndex];
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

            return provinceDevelopment[arrayIndex];
        }

        /// <summary>
        /// Set province development level
        /// </summary>
        public void SetProvinceDevelopment(ushort provinceId, byte development)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return;

            byte oldDevelopment = provinceDevelopment[arrayIndex];
            if (oldDevelopment == development)
                return;

            // Update data
            provinceDevelopment[arrayIndex] = development;

            // Update the full ProvinceState for consistency
            var state = provinceStates[arrayIndex];
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

            return provinceTerrain[arrayIndex];
        }

        /// <summary>
        /// Set province terrain type
        /// </summary>
        public void SetProvinceTerrain(ushort provinceId, byte terrain)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return;

            provinceTerrain[arrayIndex] = terrain;

            // Update the full ProvinceState for consistency
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
                if (provinceOwners[i] == countryId)
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
                DominionLogger.LogError("ProvinceSystem not initialized - call Initialize() first");
                return;
            }

            DominionLogger.Log($"Loading province initial states from {dataDirectory} using Burst jobs");

            var result = BurstProvinceHistoryLoader.LoadProvinceInitialStates(dataDirectory);

            if (!result.Success)
            {
                DominionLogger.LogError($"Failed to load province initial states: {result.ErrorMessage}");
                return;
            }

            DominionLogger.Log($"Province initial states loaded: {result.LoadedCount} successful, {result.FailedCount} failed");

            ApplyInitialStates(result.InitialStates);

            result.Dispose();

            eventBus?.Emit(new ProvinceInitialStatesLoadedEvent
            {
                LoadedCount = result.LoadedCount,
                FailedCount = result.FailedCount
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
                    DominionLogger.LogWarning($"Province {initialState.ProvinceID} has initial state but doesn't exist in map data");
                    continue;
                }

                ApplyInitialStateToProvince(provinceId, initialState);
                appliedCount++;
            }

            DominionLogger.Log($"Applied initial state to {appliedCount} provinces");
        }

        /// <summary>
        /// Apply initial state to hot province data only
        /// </summary>
        private void ApplyInitialStateToProvince(ushort provinceId, ProvinceInitialState initialState)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return;

            // Convert to hot ProvinceState
            var state = initialState.ToProvinceState();

            // Update hot data arrays
            provinceStates[arrayIndex] = state;
            provinceOwners[arrayIndex] = state.ownerID;
            provinceControllers[arrayIndex] = state.controllerID;
            provinceDevelopment[arrayIndex] = state.development;
            provinceTerrain[arrayIndex] = state.terrain;
            provinceFlags[arrayIndex] = state.flags;

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
        /// Determine terrain type from province definition
        /// </summary>
        private byte DetermineTerrainFromDefinition(ProvinceDefinition definition)
        {
            // For now, use color-based detection
            // In the future, this could use definition metadata
            int packedRGB = definition.PackedRGB;
            return DetermineTerrainFromColor(packedRGB);
        }

        /// <summary>
        /// Validate that ProvinceState is exactly 8 bytes
        /// </summary>
        private void ValidateProvinceStateSize()
        {
            int actualSize = UnsafeUtility.SizeOf<ProvinceState>();
            if (actualSize != 8)
            {
                DominionLogger.LogError($"ProvinceState size validation failed: expected 8 bytes, got {actualSize} bytes");
            }
            else
            {
                DominionLogger.Log("ProvinceState size validation passed: 8 bytes");
            }
        }

        public void Dispose()
        {
            if (provinceStates.IsCreated) provinceStates.Dispose();
            if (provinceOwners.IsCreated) provinceOwners.Dispose();
            if (provinceControllers.IsCreated) provinceControllers.Dispose();
            if (provinceDevelopment.IsCreated) provinceDevelopment.Dispose();
            if (provinceTerrain.IsCreated) provinceTerrain.Dispose();
            if (provinceFlags.IsCreated) provinceFlags.Dispose();
            if (idToIndex.IsCreated) idToIndex.Dispose();
            if (activeProvinceIds.IsCreated) activeProvinceIds.Dispose();

            historyDatabase?.Dispose();

            isInitialized = false;
            DominionLogger.Log("ProvinceSystem disposed");
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