using Unity.Collections;
using Core.Data;

namespace Core.Systems.Province
{
    /// <summary>
    /// Manages province hot data (double-buffered) operations
    /// Handles get/set operations, queries, and ID mapping
    /// Uses snapshot's WRITE buffer for all simulation operations
    /// Extracted from ProvinceSystem.cs for better separation of concerns
    ///
    /// DIRTY TRACKING: Tracks which provinces have been modified since last buffer swap.
    /// This enables efficient partial copies instead of full buffer copies on swap.
    /// </summary>
    public class ProvinceDataManager
    {
        private GameStateSnapshot snapshot;
        private NativeHashMap<ushort, int> idToIndex;
        private NativeList<ushort> activeProvinceIds;
        private EventBus eventBus;

        private int provinceCount;

        // Dirty tracking for efficient buffer swaps
        // Stores array INDICES (not province IDs) for direct buffer access
        private NativeHashSet<int> dirtyIndices;

        private const ushort OCEAN_TERRAIN = 0;
        private const ushort UNOWNED_COUNTRY = 0;
        private const int INITIAL_DIRTY_CAPACITY = 256;
        private const int SUPPORTED_PROVINCE_LIMIT = 100000;

        private bool hasShownUnsupportedWarning = false;

        public int ProvinceCount => provinceCount;
        public int DirtyCount => dirtyIndices.IsCreated ? dirtyIndices.Count : 0;

        public ProvinceDataManager(GameStateSnapshot snapshot,
                                   NativeHashMap<ushort, int> idToIndex,
                                   NativeList<ushort> activeProvinceIds,
                                   EventBus eventBus)
        {
            this.snapshot = snapshot;
            this.idToIndex = idToIndex;
            this.activeProvinceIds = activeProvinceIds;
            this.eventBus = eventBus;
            this.provinceCount = 0;

            // Initialize dirty tracking set
            dirtyIndices = new NativeHashSet<int>(INITIAL_DIRTY_CAPACITY, Allocator.Persistent);
        }

        /// <summary>
        /// Add a new province to the system
        /// </summary>
        public void AddProvince(ushort provinceId, ushort terrainType)
        {
            if (provinceCount >= snapshot.Capacity)
            {
                ArchonLogger.LogError($"Province capacity exceeded: {provinceCount}/{snapshot.Capacity}. Increase initialCapacity in ProvinceSystem.", "core_simulation");
                return;
            }

            // Warn once when exceeding officially supported limit
            if (provinceCount >= SUPPORTED_PROVINCE_LIMIT && !hasShownUnsupportedWarning)
            {
                ArchonLogger.LogWarning($"Province count ({provinceCount}) exceeds officially supported limit ({SUPPORTED_PROVINCE_LIMIT}). Performance may degrade.", "core_simulation");
                hasShownUnsupportedWarning = true;
            }

            // Check for duplicate province ID
            if (idToIndex.ContainsKey(provinceId))
            {
                ArchonLogger.LogWarning($"Province {provinceId} already exists, skipping", "core_simulation");
                return;
            }

            int arrayIndex = provinceCount;

            // Create and store province state (8 bytes)
            var provinceState = ProvinceState.CreateDefault(terrainType);
            var states = snapshot.GetProvinceWriteBuffer();
            states[arrayIndex] = provinceState;

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

        /// <summary>
        /// Restore province count after loading from save (Clear() sets it to 0)
        /// CRITICAL for GetAllProvinceIds() to work after load
        /// </summary>
        public void RestoreProvinceCount(int count)
        {
            provinceCount = count;
        }

        #region Hot Data Access

        /// <summary>
        /// Get province owner - most common query (must be ultra-fast)
        /// </summary>
        public ushort GetProvinceOwner(ushort provinceId)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return UNOWNED_COUNTRY;

            var states = snapshot.GetProvinceWriteBuffer();
            var ownerID = states[arrayIndex].ownerID;
            return ownerID;
        }

        /// <summary>
        /// Get province controller (who currently controls, may differ from owner during occupation)
        /// </summary>
        public ushort GetProvinceController(ushort provinceId)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return UNOWNED_COUNTRY;

            var states = snapshot.GetProvinceWriteBuffer();
            return states[arrayIndex].controllerID;
        }

        /// <summary>
        /// Set province owner and emit events
        /// </summary>
        public void SetProvinceOwner(ushort provinceId, ushort newOwner)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
            {
                ArchonLogger.LogWarning($"Cannot set owner for invalid province {provinceId}", "core_simulation");
                return;
            }

            var states = snapshot.GetProvinceWriteBuffer();
            var state = states[arrayIndex];
            ushort oldOwner = state.ownerID;
            if (oldOwner == newOwner)
                return; // No change

            // Update state and write back (structs are value types)
            state.ownerID = newOwner;
            states[arrayIndex] = state;

            // Mark as dirty for buffer swap
            MarkDirty(arrayIndex);

            // Emit ownership change event
            eventBus?.Emit(new ProvinceOwnershipChangedEvent
            {
                ProvinceId = provinceId,
                OldOwner = oldOwner,
                NewOwner = newOwner
            });
        }

        // REMOVED: GetProvinceDevelopment() and SetProvinceDevelopment()
        // Development is game-specific and moved to Game layer (HegemonProvinceSystem)
        // Old code: dataManager.GetProvinceDevelopment(provinceId)
        // New code: hegemonSystem.GetDevelopment(provinceId)

        /// <summary>
        /// Get province terrain type (now ushort instead of byte)
        /// </summary>
        public ushort GetProvinceTerrain(ushort provinceId)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return OCEAN_TERRAIN;

            var states = snapshot.GetProvinceWriteBuffer();
            return states[arrayIndex].terrainType;
        }

        /// <summary>
        /// Set province terrain type (now ushort instead of byte)
        /// </summary>
        public void SetProvinceTerrain(ushort provinceId, ushort terrain)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return;

            // Update state and write back (structs are value types)
            var states = snapshot.GetProvinceWriteBuffer();
            var state = states[arrayIndex];
            if (state.terrainType == terrain)
                return; // No change

            state.terrainType = terrain;
            states[arrayIndex] = state;

            // Mark as dirty for buffer swap
            MarkDirty(arrayIndex);
        }

        /// <summary>
        /// Get complete province state (8-byte struct)
        /// </summary>
        public ProvinceState GetProvinceState(ushort provinceId)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return ProvinceState.CreateDefault(OCEAN_TERRAIN);

            var states = snapshot.GetProvinceWriteBuffer();
            return states[arrayIndex];
        }

        /// <summary>
        /// Set complete province state (internal use)
        /// </summary>
        public void SetProvinceState(ushort provinceId, ProvinceState state)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
                return;

            var states = snapshot.GetProvinceWriteBuffer();
            states[arrayIndex] = state;

            // Mark as dirty for buffer swap
            MarkDirty(arrayIndex);
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
            var states = snapshot.GetProvinceWriteBuffer();

            for (int i = 0; i < provinceCount; i++)
            {
                if (states[i].ownerID == countryId)
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
        /// Get all provinces owned by country (fills existing NativeList, zero-allocation)
        /// </summary>
        public void GetCountryProvinces(ushort countryId, NativeList<ushort> resultBuffer)
        {
            resultBuffer.Clear();
            var states = snapshot.GetProvinceWriteBuffer();

            for (int i = 0; i < provinceCount; i++)
            {
                if (states[i].ownerID == countryId)
                {
                    resultBuffer.Add(activeProvinceIds[i]);
                }
            }
        }

        /// <summary>
        /// Get count of provinces owned by a country (no allocation).
        /// </summary>
        public int GetProvinceCountForCountry(ushort countryId)
        {
            int count = 0;
            var states = snapshot.GetProvinceWriteBuffer();

            for (int i = 0; i < provinceCount; i++)
            {
                if (states[i].ownerID == countryId)
                {
                    count++;
                }
            }

            return count;
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

        #region Dirty Tracking

        /// <summary>
        /// Mark a province as dirty (modified since last buffer swap)
        /// Uses array index for efficient buffer access during swap
        /// </summary>
        private void MarkDirty(int arrayIndex)
        {
            if (dirtyIndices.IsCreated)
            {
                dirtyIndices.Add(arrayIndex);
            }
        }

        /// <summary>
        /// Get all dirty indices for buffer swap
        /// Returns a NativeArray that must be disposed by caller
        /// </summary>
        public NativeArray<int> GetDirtyIndices(Allocator allocator = Allocator.Temp)
        {
            if (!dirtyIndices.IsCreated || dirtyIndices.Count == 0)
            {
                return new NativeArray<int>(0, allocator);
            }

            var result = dirtyIndices.ToNativeArray(allocator);
            return result;
        }

        /// <summary>
        /// Clear dirty tracking after buffer swap
        /// </summary>
        public void ClearDirty()
        {
            if (dirtyIndices.IsCreated)
            {
                dirtyIndices.Clear();
            }
        }

        /// <summary>
        /// Check if any provinces are dirty
        /// </summary>
        public bool HasDirtyProvinces()
        {
            return dirtyIndices.IsCreated && dirtyIndices.Count > 0;
        }

        /// <summary>
        /// Dispose native collections
        /// </summary>
        public void Dispose()
        {
            if (dirtyIndices.IsCreated)
            {
                dirtyIndices.Dispose();
            }
        }

        #endregion
    }
}
