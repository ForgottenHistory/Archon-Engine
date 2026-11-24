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

        // Reverse index: owner countryId → province IDs (Pattern 16: Bidirectional Mapping)
        // Maintained incrementally in AddProvince/SetProvinceOwner for O(k) lookup
        private NativeParallelMultiHashMap<ushort, ushort> provincesByOwner;

        // Dirty tracking for efficient buffer swaps
        // Stores array INDICES (not province IDs) for direct buffer access
        private NativeHashSet<int> dirtyIndices;

        private const ushort OCEAN_TERRAIN = 0;
        private const ushort UNOWNED_COUNTRY = 0;
        private const int INITIAL_DIRTY_CAPACITY = 256;
        private const int SUPPORTED_PROVINCE_LIMIT = 100000;

        private bool hasShownUnsupportedWarning = false;

        // Tick at which each province's effective controller (controller, else owner) last changed, by array index.
        // 0: not since the game started. Changes at tick 0 are the starting setup and aren't stamped.
        private ulong[] controlChangeTick;

        /// <summary>The simulation clock, for stamping control changes (set by GameState once time exists).</summary>
        public System.Func<ulong> TickSource { get; set; }

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

            controlChangeTick = new ulong[snapshot.Capacity];

            // Initialize dirty tracking set
            dirtyIndices = new NativeHashSet<int>(INITIAL_DIRTY_CAPACITY, Allocator.Persistent);

            // Initialize reverse index with capacity = snapshot capacity (one entry per province)
            provincesByOwner = new NativeParallelMultiHashMap<ushort, ushort>(
                snapshot.Capacity, Allocator.Persistent);
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

            // Add to reverse index (skip unowned — owner 0 would be a 49k+ bucket)
            if (provinceState.ownerID != UNOWNED_COUNTRY)
                provincesByOwner.Add(provinceState.ownerID, provinceId);

            provinceCount++;
        }

        /// <summary>
        /// Clear all province data
        /// </summary>
        public void Clear()
        {
            provinceCount = 0;
            System.Array.Clear(controlChangeTick, 0, controlChangeTick.Length);
            idToIndex.Clear();
            activeProvinceIds.Clear();
            if (provincesByOwner.IsCreated)
                provincesByOwner.Clear();
        }

        /// <summary>
        /// Restore province count after loading from save (Clear() sets it to 0)
        /// CRITICAL for GetAllProvinceIds() to work after load
        /// </summary>
        public void RestoreProvinceCount(int count)
        {
            provinceCount = count;
        }

        /// <summary>
        /// Rebuild the provincesByOwner reverse index from current province state data.
        /// MUST be called after loading province states from a save file,
        /// since LoadState writes directly to the NativeArray without updating the reverse index.
        /// </summary>
        public void RebuildOwnerIndex()
        {
            provincesByOwner.Clear();

            var readBuffer = snapshot.GetProvinceReadBuffer();
            for (int i = 0; i < activeProvinceIds.Length; i++)
            {
                ushort provinceId = activeProvinceIds[i];
                if (idToIndex.TryGetValue(provinceId, out int arrayIndex))
                {
                    ushort owner = readBuffer[arrayIndex].ownerID;
                    if (owner != UNOWNED_COUNTRY)
                        provincesByOwner.Add(owner, provinceId);
                }
            }
        }

        #region Hot Data Access

        /// <summary>
        /// Get province owner - most common query (must be ultra-fast)
        /// </summary>
        /// <summary>Goes up on every owner change (and bulk load): caches of owner-derived data compare it.</summary>
        public int OwnershipVersion { get; private set; }
        /// <summary>Goes up on every controller change (and bulk load): caches of control-derived data compare it.</summary>
        public int ControlVersion { get; private set; }
        /// <summary>Bulk writes (initial load, save load) change owners and controllers at once: bumps both.</summary>
        public void BumpOwnershipVersion() { OwnershipVersion++; ControlVersion++; }

        /// <summary>
        /// The tick the province's effective controller (its controller, else its owner) last changed, false when it
        /// hasn't since the game started (or the save it was loaded from started). For rules that depend on how long
        /// a province has been held: conversion, integration, unrest after conquest.
        /// </summary>
        public bool TryGetControlChangeTick(ushort provinceId, out ulong tick)
        {
            tick = 0;
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex) || arrayIndex >= controlChangeTick.Length) return false;
            tick = controlChangeTick[arrayIndex];
            return tick != 0;
        }

        /// <summary>Every province's control change tick by array index (0: none), for save/load.</summary>
        public ulong[] ControlChangeTicks => controlChangeTick;

        private void StampControlChange(int arrayIndex)
        {
            ulong tick = TickSource != null ? TickSource() : 0;
            if (tick != 0 && arrayIndex < controlChangeTick.Length) controlChangeTick[arrayIndex] = tick;
        }

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
        /// <remarks>Raises OwnershipVersion when the owner actually changes.</remarks>
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
            OwnershipVersion++;
            if (state.controllerID == 0) StampControlChange(arrayIndex); // the owner is the effective controller

            // Update reverse index (skip unowned — owner 0 would be a 49k+ bucket)
            if (oldOwner != UNOWNED_COUNTRY)
                RemoveFromReverseIndex(oldOwner, provinceId);
            if (newOwner != UNOWNED_COUNTRY)
                provincesByOwner.Add(newOwner, provinceId);

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

        /// <summary>
        /// Set who controls a province militarily (occupation) and emit an event. Ownership is untouched.
        /// </summary>
        public void SetProvinceController(ushort provinceId, ushort newController)
        {
            if (!idToIndex.TryGetValue(provinceId, out int arrayIndex))
            {
                ArchonLogger.LogWarning($"Cannot set controller for invalid province {provinceId}", "core_simulation");
                return;
            }

            var states = snapshot.GetProvinceWriteBuffer();
            var state = states[arrayIndex];
            ushort oldController = state.controllerID;
            if (oldController == newController)
                return;

            state.controllerID = newController;
            states[arrayIndex] = state;
            ControlVersion++;
            ushort before = oldController != 0 ? oldController : state.ownerID;
            ushort after = newController != 0 ? newController : state.ownerID;
            if (before != after) StampControlChange(arrayIndex);
            MarkDirty(arrayIndex);

            eventBus?.Emit(new ProvinceControllerChangedEvent
            {
                ProvinceId = provinceId,
                OldController = oldController,
                NewController = newController
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
            ushort oldOwner = states[arrayIndex].ownerID;
            ushort oldController = states[arrayIndex].controllerID;
            states[arrayIndex] = state;
            if (oldOwner != state.ownerID) OwnershipVersion++;
            if (oldController != state.controllerID) ControlVersion++;
            if ((oldController != 0 ? oldController : oldOwner) != (state.controllerID != 0 ? state.controllerID : state.ownerID))
                StampControlChange(arrayIndex);

            // Update reverse index if owner changed (skip unowned — owner 0 would be a 49k+ bucket)
            if (oldOwner != state.ownerID)
            {
                if (oldOwner != UNOWNED_COUNTRY)
                    RemoveFromReverseIndex(oldOwner, provinceId);
                if (state.ownerID != UNOWNED_COUNTRY)
                    provincesByOwner.Add(state.ownerID, provinceId);
            }

            // Mark as dirty for buffer swap
            MarkDirty(arrayIndex);
        }

        #endregion

        #region Query Operations

        /// <summary>
        /// Get all provinces owned by a specific country
        /// Returns a native array that must be disposed by caller
        /// Uses reverse index for O(k) lookup where k = owned provinces
        /// </summary>
        public NativeArray<ushort> GetCountryProvinces(ushort countryId, Allocator allocator = Allocator.TempJob)
        {
            int count = provincesByOwner.CountValuesForKey(countryId);
            if (count == 0)
                return new NativeArray<ushort>(0, allocator);

            var resultArray = new NativeArray<ushort>(count, allocator);
            int idx = 0;

            foreach (var provinceId in provincesByOwner.GetValuesForKey(countryId))
            {
                resultArray[idx++] = provinceId;
            }

            return resultArray;
        }

        /// <summary>
        /// Get all provinces owned by country (fills existing NativeList, zero-allocation)
        /// Uses reverse index for O(k) lookup where k = owned provinces
        /// </summary>
        public void GetCountryProvinces(ushort countryId, NativeList<ushort> resultBuffer)
        {
            resultBuffer.Clear();

            foreach (var provinceId in provincesByOwner.GetValuesForKey(countryId))
            {
                resultBuffer.Add(provinceId);
            }
        }

        /// <summary>
        /// Get count of provinces owned by a country (no allocation).
        /// Uses reverse index for O(k) not O(n) where n = total provinces
        /// </summary>
        public int GetProvinceCountForCountry(ushort countryId)
        {
            return provincesByOwner.CountValuesForKey(countryId);
        }

        /// <summary>
        /// Bulk fill owner data into a pre-allocated array indexed by province ID.
        /// Avoids per-province hash lookups by iterating contiguous buffer directly.
        /// Used by GPU texture upload for O(n) linear scan instead of O(n) hash lookups.
        /// </summary>
        public void FillOwnerBuffer(uint[] ownerBuffer)
        {
            var states = snapshot.GetProvinceWriteBuffer();

            for (int i = 0; i < provinceCount; i++)
            {
                ushort provinceId = activeProvinceIds[i];
                if (provinceId < ownerBuffer.Length)
                {
                    ownerBuffer[provinceId] = states[i].ownerID;
                }
            }
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

        #region Reverse Index

        /// <summary>
        /// Remove a province from a specific owner's reverse index entry
        /// </summary>
        private void RemoveFromReverseIndex(ushort ownerID, ushort provinceId)
        {
            if (!provincesByOwner.TryGetFirstValue(ownerID, out ushort value, out var it))
                return;

            do
            {
                if (value == provinceId)
                {
                    provincesByOwner.Remove(it);
                    return;
                }
            } while (provincesByOwner.TryGetNextValue(out value, ref it));
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
            if (provincesByOwner.IsCreated)
            {
                provincesByOwner.Dispose();
            }
        }

        #endregion
    }
}
