using Unity.Collections;
using Core.Data;

namespace Core.Systems.Province
{
    /// <summary>
    /// Manages province hot data (NativeArray) operations
    /// Handles get/set operations, queries, and ID mapping
    /// Extracted from ProvinceSystem.cs for better separation of concerns
    /// </summary>
    public class ProvinceDataManager
    {
        private NativeArray<ProvinceState> provinceStates;
        private NativeHashMap<ushort, int> idToIndex;
        private NativeList<ushort> activeProvinceIds;
        private EventBus eventBus;

        private int provinceCount;

        private const byte OCEAN_TERRAIN = 0;
        private const ushort UNOWNED_COUNTRY = 0;

        public int ProvinceCount => provinceCount;

        public ProvinceDataManager(NativeArray<ProvinceState> provinceStates,
                                   NativeHashMap<ushort, int> idToIndex,
                                   NativeList<ushort> activeProvinceIds,
                                   EventBus eventBus)
        {
            this.provinceStates = provinceStates;
            this.idToIndex = idToIndex;
            this.activeProvinceIds = activeProvinceIds;
            this.eventBus = eventBus;
            this.provinceCount = 0;
        }

        /// <summary>
        /// Add a new province to the system
        /// </summary>
        public void AddProvince(ushort provinceId, byte terrainType)
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
        /// Clear all province data
        /// </summary>
        public void Clear()
        {
            provinceCount = 0;
            idToIndex.Clear();
            activeProvinceIds.Clear();
        }

        #region Hot Data Access

        /// <summary>
        /// Get province owner - most common query (must be ultra-fast)
        /// </summary>
        public ushort GetProvinceOwner(ushort provinceId)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return UNOWNED_COUNTRY;

            var ownerID = provinceStates[arrayIndex].ownerID;
            return ownerID;
        }

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
        /// Set complete province state (internal use)
        /// </summary>
        public void SetProvinceState(ushort provinceId, ProvinceState state)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return;

            provinceStates[arrayIndex] = state;
        }

        #endregion

        #region Query Operations

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

        #endregion
    }
}
