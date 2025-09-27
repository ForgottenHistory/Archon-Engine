using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using System.Runtime.InteropServices;
using Map.Simulation;
using ParadoxParser.Jobs;

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

            isInitialized = true;
            Debug.Log($"ProvinceSystem initialized with capacity {initialCapacity}");

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
                Debug.LogError("ProvinceSystem not initialized - call Initialize() first");
                return;
            }

            if (!mapResult.Success)
            {
                Debug.LogError($"Cannot initialize from failed map result: {mapResult.ErrorMessage}");
                return;
            }

            Debug.Log($"Initializing {mapResult.ProvinceMappings.ColorToProvinceID.Count} provinces from map data");

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
                ApplyProvinceDefinitions(mapResult.Definitions);
            }

            Debug.Log($"ProvinceSystem initialized with {provinceCount} provinces");

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
                Debug.LogError($"Province capacity exceeded: {provinceCount}/{provinceStates.Length}");
                return;
            }

            // Check for duplicate province ID
            if (idToIndex.ContainsKey(provinceId))
            {
                Debug.LogWarning($"Province {provinceId} already exists, skipping");
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

            Debug.Log($"Applying definitions to {definitions.AllDefinitions.Length} provinces");

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
                    }
                }
                else
                {
                    Debug.LogWarning($"Definition found for province {provinceId} but province not in map data");
                }
            }
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
                Debug.LogWarning($"Cannot set owner for invalid province {provinceId}");
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
                Debug.LogError($"ProvinceState size validation failed: expected 8 bytes, got {actualSize} bytes");
            }
            else
            {
                Debug.Log("ProvinceState size validation passed: 8 bytes");
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

            isInitialized = false;
            Debug.Log("ProvinceSystem disposed");
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
}