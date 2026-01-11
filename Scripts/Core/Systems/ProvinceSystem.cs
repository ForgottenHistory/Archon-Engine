using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;
using Core.Data;
using Core.Systems.Province;
using Utils;

namespace Core.Systems
{
    /// <summary>
    /// Read-only native province data for Burst jobs.
    /// Use this struct in IJob implementations for parallel algorithms.
    /// </summary>
    public struct NativeProvinceData
    {
        [ReadOnly] public NativeArray<ProvinceState> provinceStates;
        [ReadOnly] public NativeHashMap<ushort, int> idToIndex;
        [ReadOnly] public NativeList<ushort> activeProvinceIds;
        public int provinceCount;

        public bool IsCreated => provinceStates.IsCreated;

        /// <summary>
        /// Get province owner by province ID. Use in Burst jobs.
        /// Returns 0 (unowned) if province not found.
        /// </summary>
        public ushort GetProvinceOwner(ushort provinceId)
        {
            if (!idToIndex.TryGetValue(provinceId, out int index))
                return 0;
            return provinceStates[index].ownerID;
        }

        /// <summary>
        /// Get province ID at array index. Use in Burst jobs.
        /// </summary>
        public ushort GetProvinceIdAtIndex(int index)
        {
            if (index < 0 || index >= activeProvinceIds.Length)
                return 0;
            return activeProvinceIds[index];
        }
    }

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

        // Double-buffer snapshot for zero-blocking UI reads (Victoria 3 pattern)
        private GameStateSnapshot snapshot;

        // Native arrays (owned by this class, passed to components)
        private NativeHashMap<ushort, int> idToIndex;
        private NativeList<ushort> activeProvinceIds;

        // State
        private bool isInitialized;
        private EventBus eventBus;

        // Properties
        public int ProvinceCount => dataManager?.ProvinceCount ?? 0;
        public int Capacity => snapshot?.Capacity ?? 0;
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Initialize the province system with event bus
        /// </summary>
        public void Initialize(EventBus eventBus)
        {
            this.eventBus = eventBus;

            // Initialize double-buffer snapshot (2x 8 bytes per province)
            snapshot = new GameStateSnapshot();
            snapshot.Initialize(initialCapacity);

            // Allocate native arrays with initial capacity
            idToIndex = new NativeHashMap<ushort, int>(initialCapacity, Allocator.Persistent);
            activeProvinceIds = new NativeList<ushort>(initialCapacity, Allocator.Persistent);

            historyDatabase = new ProvinceHistoryDatabase();

            // Initialize components with references to snapshot's write buffer
            // ProvinceDataManager writes to simulation buffer, UI reads from UI buffer
            dataManager = new ProvinceDataManager(snapshot, idToIndex, activeProvinceIds, eventBus);
            stateLoader = new ProvinceStateLoader(dataManager, eventBus, historyDatabase);

            isInitialized = true;
            ArchonLogger.Log($"ProvinceSystem initialized with capacity {initialCapacity} (double-buffered: {initialCapacity * 8 * 2 / 1024}KB total)", "core_simulation");

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
                ArchonLogger.LogError("ProvinceSystem not initialized - call Initialize() first", "core_simulation");
                return;
            }

            if (!loadResult.Success)
            {
                ArchonLogger.LogError($"Cannot initialize from failed load result: {loadResult.ErrorMessage}", "core_simulation");
                return;
            }

            ArchonLogger.Log($"Initializing {loadResult.LoadedCount} provinces from JSON5 + Burst data", "core_simulation");

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

            ArchonLogger.Log($"ProvinceSystem initialized with {ProvinceCount} provinces (ownership will be resolved in linking phase)", "core_simulation");

            // Emit initialization complete event
            eventBus?.Emit(new ProvinceSystemInitializedEvent
            {
                ProvinceCount = ProvinceCount
            });
        }

        /// <summary>
        /// Initialize empty province system for map-only mode
        /// No province data loaded - just renders the map texture
        /// </summary>
        public void InitializeEmpty()
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem not initialized - call Initialize() first", "core_simulation");
                return;
            }

            // Clear any existing data
            dataManager.Clear();

            ArchonLogger.Log("ProvinceSystem initialized empty (map-only mode)", "core_simulation");

            // Emit initialization complete event
            eventBus?.Emit(new ProvinceSystemInitializedEvent
            {
                ProvinceCount = 0
            });
        }

        /// <summary>
        /// Initialize provinces from definition.csv only (no history data)
        /// Creates provinces with default state (no owner, no development)
        /// </summary>
        public void InitializeFromDefinitions(System.Collections.Generic.List<Core.Loaders.DefinitionLoader.DefinitionEntry> definitions)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem not initialized - call Initialize() first", "core_simulation");
                return;
            }

            ArchonLogger.Log($"Initializing {definitions.Count} provinces from definitions (no history)", "core_simulation");

            // Clear existing data
            dataManager.Clear();

            // Add each province with default state
            foreach (var def in definitions)
            {
                if (def.ProvinceID <= 0 || def.ProvinceID > ushort.MaxValue)
                    continue;

                ushort provinceId = (ushort)def.ProvinceID;

                // Add province with terrain 0 (grasslands) and no owner
                dataManager.AddProvince(provinceId, 0);
            }

            ArchonLogger.Log($"ProvinceSystem initialized with {ProvinceCount} provinces from definitions", "core_simulation");

            // Emit initialization complete event
            eventBus?.Emit(new ProvinceSystemInitializedEvent
            {
                ProvinceCount = ProvinceCount
            });
        }

        /// <summary>
        /// Apply initial states (ownership, terrain) from JSON5 data to already-registered provinces
        /// Called after InitializeFromDefinitions to set ownership for provinces that have history files
        /// </summary>
        public void ApplyInitialStates(ProvinceInitialStateLoadResult loadResult)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem not initialized - call Initialize() first", "core_simulation");
                return;
            }

            if (!loadResult.Success)
            {
                ArchonLogger.LogWarning($"Cannot apply states from failed load result: {loadResult.ErrorMessage}", "core_simulation");
                return;
            }

            int appliedCount = 0;
            for (int i = 0; i < loadResult.InitialStates.Length; i++)
            {
                var initialState = loadResult.InitialStates[i];

                if (!initialState.IsValid)
                    continue;

                ushort provinceId = (ushort)initialState.ProvinceID;

                // Only apply to provinces that exist (were loaded from definition.csv)
                if (!dataManager.HasProvince(provinceId))
                {
                    ArchonLogger.LogWarning($"Cannot apply state to unregistered province {provinceId}", "core_simulation");
                    continue;
                }

                // Update terrain if specified
                if (initialState.Terrain != 0)
                {
                    dataManager.SetProvinceTerrain(provinceId, initialState.Terrain);
                }

                appliedCount++;
            }

            ArchonLogger.Log($"Applied initial state to {appliedCount} provinces", "core_simulation");
        }

        /// <summary>
        /// Add a single province to the system (for runtime colonization)
        /// </summary>
        public void AddProvince(ushort provinceId, ushort terrainType)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem not initialized", "core_simulation");
                return;
            }

            dataManager.AddProvince(provinceId, terrainType);
        }


        // ===== DATA ACCESS OPERATIONS (delegated to ProvinceDataManager) =====

        public ushort GetProvinceOwner(ushort provinceId) => dataManager.GetProvinceOwner(provinceId);
        public void SetProvinceOwner(ushort provinceId, ushort newOwner) => dataManager.SetProvinceOwner(provinceId, newOwner);
        // REMOVED: GetProvinceDevelopment/SetProvinceDevelopment (game-specific - use HegemonProvinceSystem)
        public ushort GetProvinceTerrain(ushort provinceId) => dataManager.GetProvinceTerrain(provinceId);
        public void SetProvinceTerrain(ushort provinceId, ushort terrain) => dataManager.SetProvinceTerrain(provinceId, terrain);
        public ProvinceState GetProvinceState(ushort provinceId) => dataManager.GetProvinceState(provinceId);
        public NativeArray<ushort> GetCountryProvinces(ushort countryId, Allocator allocator = Allocator.TempJob) => dataManager.GetCountryProvinces(countryId, allocator);
        public void GetCountryProvinces(ushort countryId, NativeList<ushort> resultBuffer) => dataManager.GetCountryProvinces(countryId, resultBuffer);
        public NativeArray<ushort> GetAllProvinceIds(Allocator allocator = Allocator.TempJob) => dataManager.GetAllProvinceIds(allocator);
        public bool HasProvince(ushort provinceId) => dataManager.HasProvince(provinceId);

        // ===== LOADING OPERATIONS (delegated to ProvinceStateLoader) =====

        public void LoadProvinceInitialStates(string dataDirectory)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem not initialized - call Initialize() first", "core_simulation");
                return;
            }
            stateLoader.LoadProvinceInitialStates(dataDirectory);
        }

        public ProvinceInitialStateLoadResult LoadProvinceInitialStatesForLinking(string dataDirectory)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem not initialized - call Initialize() first", "core_simulation");
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
                ArchonLogger.LogError($"ProvinceState size validation failed: expected 8 bytes, got {actualSize} bytes", "core_simulation");
            }
            else
            {
                ArchonLogger.Log("ProvinceState size validation passed: 8 bytes", "core_simulation");
            }
        }

        /// <summary>
        /// Get read-only snapshot buffer for UI access (zero-blocking reads)
        /// UI should NEVER write to this buffer - it's one frame behind simulation
        /// </summary>
        public NativeArray<ProvinceState> GetUIReadBuffer()
        {
            if (!isInitialized || snapshot == null)
            {
                throw new System.InvalidOperationException("ProvinceSystem not initialized");
            }
            return snapshot.GetProvinceReadBuffer();
        }

        /// <summary>
        /// Get read-only native province data for Burst jobs.
        /// Returns a struct with read-only views of province data.
        /// </summary>
        public NativeProvinceData GetNativeData()
        {
            if (!isInitialized || snapshot == null)
            {
                throw new System.InvalidOperationException("ProvinceSystem not initialized");
            }
            return new NativeProvinceData
            {
                provinceStates = snapshot.GetProvinceWriteBuffer(),
                idToIndex = idToIndex,
                activeProvinceIds = activeProvinceIds,
                provinceCount = ProvinceCount
            };
        }

        /// <summary>
        /// Called by TimeManager after simulation tick completes
        /// Swaps write/read buffers so UI sees latest completed tick
        ///
        /// Uses DIRTY TRACKING for efficient partial copies:
        /// - Only copies provinces that were modified since last swap
        /// - O(dirty count) instead of O(total provinces)
        /// - Scales to 100k+ provinces without performance issues
        /// </summary>
        public void SwapBuffers()
        {
            if (!isInitialized || snapshot == null || dataManager == null)
            {
                return;
            }

            // Get dirty indices BEFORE swap (while they still reference the write buffer)
            using (var dirtyIndices = dataManager.GetDirtyIndices(Unity.Collections.Allocator.Temp))
            {
                // Swap buffer pointers (O(1))
                snapshot.SwapBuffers();

                // Copy only dirty entries from new read buffer (was write) to new write buffer (was read)
                if (dirtyIndices.Length > 0)
                {
                    var readBuffer = snapshot.GetProvinceReadBuffer();   // Has fresh data (was old write)
                    var writeBuffer = snapshot.GetProvinceWriteBuffer(); // Has stale data (was old read)

                    for (int i = 0; i < dirtyIndices.Length; i++)
                    {
                        int index = dirtyIndices[i];
                        writeBuffer[index] = readBuffer[index];
                    }
                }

                // Clear dirty tracking for next tick
                dataManager.ClearDirty();
            }
        }

        /// <summary>
        /// Synchronize buffers after scenario loading
        /// Ensures both buffers have identical data to prevent first-tick empty buffer bug
        /// </summary>
        public void SyncBuffersAfterLoad()
        {
            if (!isInitialized || snapshot == null)
            {
                ArchonLogger.LogWarning("ProvinceSystem: Cannot sync buffers, not initialized", "core_simulation");
                return;
            }
            snapshot.SyncBuffersAfterLoad();
        }

        // ====================================================================
        // SAVE/LOAD SUPPORT
        // ====================================================================

        /// <summary>
        /// Save ProvinceSystem state to binary writer
        /// Serializes: capacity, province states, id mappings
        /// </summary>
        public void SaveState(System.IO.BinaryWriter writer)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem: Cannot save state - not initialized", "core_simulation");
                return;
            }

            // Write capacity
            writer.Write(snapshot.Capacity);

            // Write province count
            writer.Write(ProvinceCount);

            // Write province states from write buffer (authoritative state)
            var writeBuffer = snapshot.GetProvinceWriteBuffer();
            Core.SaveLoad.SerializationHelper.WriteNativeArray(writer, writeBuffer);

            // Write idToIndex mapping
            writer.Write(idToIndex.Count);
            foreach (var kvp in idToIndex)
            {
                writer.Write(kvp.Key);   // provinceId (ushort)
                writer.Write(kvp.Value); // index (int)
            }

            // Write activeProvinceIds list
            writer.Write(activeProvinceIds.Length);
            for (int i = 0; i < activeProvinceIds.Length; i++)
            {
                writer.Write(activeProvinceIds[i]);
            }

            ArchonLogger.Log($"ProvinceSystem: Saved {ProvinceCount} provinces ({snapshot.Capacity} capacity)", "core_simulation");
        }

        /// <summary>
        /// Load ProvinceSystem state from binary reader
        /// Restores: capacity, province states, id mappings
        /// Note: Must be called AFTER Initialize() but BEFORE any province operations
        /// </summary>
        public void LoadState(System.IO.BinaryReader reader)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("ProvinceSystem: Cannot load state - not initialized", "core_simulation");
                return;
            }

            // Read capacity
            int savedCapacity = reader.ReadInt32();

            // Verify capacity matches (should match since we initialized with same capacity)
            if (savedCapacity != snapshot.Capacity)
            {
                ArchonLogger.LogWarning($"ProvinceSystem: Capacity mismatch (saved: {savedCapacity}, current: {snapshot.Capacity})", "core_simulation");
            }

            // Read province count
            int savedProvinceCount = reader.ReadInt32();

            // Clear existing data
            dataManager.Clear();
            idToIndex.Clear();
            activeProvinceIds.Clear();

            // Read province states into write buffer
            var writeBuffer = snapshot.GetProvinceWriteBuffer();
            Core.SaveLoad.SerializationHelper.ReadNativeArray(reader, writeBuffer);

            // Read idToIndex mapping
            int mappingCount = reader.ReadInt32();
            for (int i = 0; i < mappingCount; i++)
            {
                ushort provinceId = reader.ReadUInt16();
                int index = reader.ReadInt32();
                idToIndex.TryAdd(provinceId, index);
            }

            // Read activeProvinceIds list
            int activeCount = reader.ReadInt32();
            for (int i = 0; i < activeCount; i++)
            {
                ushort provinceId = reader.ReadUInt16();
                activeProvinceIds.Add(provinceId);
            }

            // CRITICAL: Restore provinceCount (dataManager.Clear() set it to 0)
            // This must match activeProvinceIds.Length for GetAllProvinceIds() to work
            dataManager.RestoreProvinceCount(activeCount);

            // Sync both buffers so UI doesn't read stale data on first tick
            snapshot.SyncBuffersAfterLoad();

            ArchonLogger.Log($"ProvinceSystem: Loaded {savedProvinceCount} provinces (capacity: {savedCapacity})", "core_simulation");
        }

        public void Dispose()
        {
            snapshot?.Dispose();
            dataManager?.Dispose();
            if (idToIndex.IsCreated) idToIndex.Dispose();
            if (activeProvinceIds.IsCreated) activeProvinceIds.Dispose();

            historyDatabase?.Dispose();

            isInitialized = false;
            ArchonLogger.Log("ProvinceSystem disposed", "core_simulation");
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