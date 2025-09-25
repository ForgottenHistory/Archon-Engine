using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Map.Simulation
{
    /// <summary>
    /// Core province simulation system using dual-layer architecture
    /// Hot simulation data: exactly 80KB for 10,000 provinces
    /// Cold presentation data: loaded on-demand
    /// </summary>
    public class ProvinceSimulation : IDisposable
    {
        private const int MAX_PROVINCES = 65535; // ushort limit
        private const int DEFAULT_CAPACITY = 10000;

        // HOT DATA - Simulation Layer (CPU, deterministic, networked)
        private NativeArray<ProvinceState> provinces;
        private NativeHashMap<ushort, int> idToIndex;  // Province ID -> Array Index lookup
        private int provinceCount;
        private bool isInitialized;

        // COLD DATA - Presentation Layer (loaded on-demand)
        private Dictionary<ushort, ProvinceColdData> coldData;

        // State management
        private uint stateVersion;          // Incremented on each change
        private bool isDirty;               // Has state changed since last GPU update
        private HashSet<int> dirtyIndices;  // Which specific provinces changed

        public int ProvinceCount => provinceCount;
        public uint StateVersion => stateVersion;
        public bool IsDirty => isDirty;
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Initialize the simulation system
        /// </summary>
        public ProvinceSimulation(int capacity = DEFAULT_CAPACITY)
        {
            if (capacity > MAX_PROVINCES)
                throw new ArgumentException($"Capacity cannot exceed {MAX_PROVINCES} provinces");

            provinces = new NativeArray<ProvinceState>(capacity, Allocator.Persistent);
            idToIndex = new NativeHashMap<ushort, int>(capacity, Allocator.Persistent);
            coldData = new Dictionary<ushort, ProvinceColdData>();
            dirtyIndices = new HashSet<int>();

            provinceCount = 0;
            stateVersion = 0;
            isDirty = false;
            isInitialized = true;

            // Initialize all provinces to default state
            var defaultState = ProvinceState.CreateDefault();
            for (int i = 0; i < capacity; i++)
            {
                provinces[i] = defaultState;
            }

            Debug.Log($"ProvinceSimulation initialized with capacity for {capacity} provinces " +
                     $"({capacity * 8} bytes hot data)");
        }

        /// <summary>
        /// Add a new province to the simulation
        /// </summary>
        public bool AddProvince(ushort provinceID, TerrainType terrain = TerrainType.Grassland)
        {
            if (!isInitialized)
            {
                Debug.LogError("ProvinceSimulation not initialized");
                return false;
            }

            if (provinceID == 0)
            {
                Debug.LogError("Province ID 0 is reserved for ocean");
                return false;
            }

            if (provinceCount >= provinces.Length)
            {
                Debug.LogError($"Province capacity exceeded ({provinces.Length})");
                return false;
            }

            if (idToIndex.ContainsKey(provinceID))
            {
                Debug.LogWarning($"Province {provinceID} already exists");
                return false;
            }

            // Add to simulation
            int index = provinceCount;
            provinces[index] = ProvinceState.CreateDefault((byte)terrain);
            idToIndex.TryAdd(provinceID, index);
            provinceCount++;

            // Mark as dirty
            MarkDirty(index);

            return true;
        }

        /// <summary>
        /// Check if a province exists in the simulation
        /// </summary>
        public bool HasProvince(ushort provinceID)
        {
            return isInitialized && idToIndex.ContainsKey(provinceID);
        }

        /// <summary>
        /// Get province state by ID (fast O(1) lookup)
        /// </summary>
        public ProvinceState GetProvinceState(ushort provinceID)
        {
            if (idToIndex.TryGetValue(provinceID, out int index))
            {
                return provinces[index];
            }
            return ProvinceState.CreateDefault(); // Return default if not found
        }

        /// <summary>
        /// Set province state by ID
        /// </summary>
        public bool SetProvinceState(ushort provinceID, ProvinceState newState)
        {
            if (idToIndex.TryGetValue(provinceID, out int index))
            {
                provinces[index] = newState;
                MarkDirty(index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Set province owner
        /// </summary>
        public bool SetProvinceOwner(ushort provinceID, ushort ownerID)
        {
            if (idToIndex.TryGetValue(provinceID, out int index))
            {
                var state = provinces[index];
                state.ownerID = ownerID;
                state.controllerID = ownerID; // Owner controls by default
                provinces[index] = state;
                MarkDirty(index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Set province controller (for occupation scenarios)
        /// </summary>
        public bool SetProvinceController(ushort provinceID, ushort controllerID)
        {
            if (idToIndex.TryGetValue(provinceID, out int index))
            {
                var state = provinces[index];
                state.controllerID = controllerID;
                provinces[index] = state;
                MarkDirty(index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Set province development level
        /// </summary>
        public bool SetProvinceDevelopment(ushort provinceID, byte development)
        {
            if (idToIndex.TryGetValue(provinceID, out int index))
            {
                var state = provinces[index];
                state.development = development;
                provinces[index] = state;
                MarkDirty(index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Set province flag
        /// </summary>
        public bool SetProvinceFlag(ushort provinceID, ProvinceFlags flag, bool value)
        {
            if (idToIndex.TryGetValue(provinceID, out int index))
            {
                var state = provinces[index];
                if (value)
                    state.SetFlag(flag);
                else
                    state.ClearFlag(flag);
                provinces[index] = state;
                MarkDirty(index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get all provinces owned by a country
        /// </summary>
        public void GetProvincesByOwner(ushort ownerID, NativeList<ushort> result)
        {
            result.Clear();

            foreach (var kvp in idToIndex)
            {
                ushort provinceID = kvp.Key;
                int index = kvp.Value;

                if (provinces[index].ownerID == ownerID)
                {
                    result.Add(provinceID);
                }
            }
        }

        /// <summary>
        /// Get read-only access to all provinces (hot data only)
        /// </summary>
        public NativeArray<ProvinceState>.ReadOnly GetAllProvinces()
        {
            return provinces.GetSubArray(0, provinceCount).AsReadOnly();
        }

        /// <summary>
        /// Get provinces that have changed since last clear
        /// </summary>
        public IReadOnlyCollection<int> GetDirtyIndices()
        {
            return dirtyIndices;
        }

        /// <summary>
        /// Clear dirty flags (call after updating GPU textures)
        /// </summary>
        public void ClearDirtyFlags()
        {
            dirtyIndices.Clear();
            isDirty = false;
        }

        /// <summary>
        /// Mark specific province as dirty
        /// </summary>
        private void MarkDirty(int index)
        {
            dirtyIndices.Add(index);
            isDirty = true;
            stateVersion++;
        }

        /// <summary>
        /// Get cold data for a province (loaded on-demand)
        /// </summary>
        public ProvinceColdData GetColdData(ushort provinceID)
        {
            if (!coldData.TryGetValue(provinceID, out var data))
            {
                // Create default cold data on first access
                data = new ProvinceColdData(provinceID);
                coldData[provinceID] = data;
            }
            return data;
        }

        /// <summary>
        /// Set cold data for a province
        /// </summary>
        public void SetColdData(ushort provinceID, ProvinceColdData data)
        {
            coldData[provinceID] = data;
        }

        /// <summary>
        /// Calculate checksum of entire simulation state for validation
        /// </summary>
        public unsafe uint CalculateStateChecksum()
        {
            uint checksum = 0;

            // Hash all province states
            for (int i = 0; i < provinceCount; i++)
            {
                checksum = checksum * 31 + (uint)provinces[i].GetHashCode();
            }

            // Include province count and version
            checksum = checksum * 31 + (uint)provinceCount;
            checksum = checksum * 31 + stateVersion;

            return checksum;
        }

        /// <summary>
        /// Get memory usage statistics
        /// </summary>
        public (int totalBytes, int hotBytes, int coldBytes) GetMemoryUsage()
        {
            int hotBytes = provinceCount * UnsafeUtility.SizeOf<ProvinceState>(); // Only count used provinces
            int lookupBytes = idToIndex.Capacity * (sizeof(ushort) + sizeof(int));
            int coldBytes = coldData.Count * 1024; // Estimate 1KB per cold data entry

            return (hotBytes + lookupBytes + coldBytes, hotBytes, coldBytes);
        }

        /// <summary>
        /// Validate simulation state integrity
        /// </summary>
        public bool ValidateState(out string errorMessage)
        {
            errorMessage = null;

            // Check structure size
            int actualSize = UnsafeUtility.SizeOf<ProvinceState>();
            if (actualSize != 8)
            {
                errorMessage = $"ProvinceState size violation: expected 8 bytes, got {actualSize}";
                return false;
            }

            // Check province count
            if (provinceCount < 0 || provinceCount > provinces.Length)
            {
                errorMessage = $"Invalid province count: {provinceCount}";
                return false;
            }

            // Check lookup table consistency
            if (idToIndex.Count != provinceCount)
            {
                errorMessage = $"Lookup table inconsistency: {idToIndex.Count} vs {provinceCount}";
                return false;
            }

            // Validate province data
            foreach (var kvp in idToIndex)
            {
                int index = kvp.Value;
                if (index < 0 || index >= provinceCount)
                {
                    errorMessage = $"Invalid index in lookup table: {index}";
                    return false;
                }
            }

            return true;
        }

        public void Dispose()
        {
            if (provinces.IsCreated) provinces.Dispose();
            if (idToIndex.IsCreated) idToIndex.Dispose();

            coldData?.Clear();
            dirtyIndices?.Clear();

            isInitialized = false;
        }

#if UNITY_EDITOR
        [ContextMenu("Log Simulation Statistics")]
        public void LogStatistics()
        {
            var (totalBytes, hotBytes, coldBytes) = GetMemoryUsage();

            Debug.Log($"Province Simulation Statistics:\n" +
                     $"Provinces: {provinceCount}/{provinces.Length}\n" +
                     $"Memory Usage: {totalBytes / 1024f:F1} KB total\n" +
                     $"  - Hot Data: {hotBytes / 1024f:F1} KB ({hotBytes} bytes)\n" +
                     $"  - Cold Data: {coldBytes / 1024f:F1} KB (estimated)\n" +
                     $"State Version: {stateVersion}\n" +
                     $"Dirty Provinces: {dirtyIndices.Count}\n" +
                     $"Hot bytes per province: {hotBytes / math.max(provinceCount, 1)} bytes");
        }
#endif
    }
}