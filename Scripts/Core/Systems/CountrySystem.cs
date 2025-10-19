using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;
using Core.Data;
using Core.Loaders;
using Core.Systems.Country;
using Utils;

namespace Core.Systems
{
    /// <summary>
    /// Single source of truth for all country/nation data
    /// Manages CountryHotData (8-byte) array for frequently accessed data
    /// Lazy-loads CountryColdData for detailed information
    /// Performance: Structure of Arrays, hot/cold separation, zero allocations
    /// </summary>
    public class CountrySystem : MonoBehaviour, System.IDisposable
    {
        [Header("Configuration")]
        [SerializeField] private int initialCapacity = 1000;  // Max countries supported
        [SerializeField] private bool enableColdDataCaching = true;
        [SerializeField] private int coldDataCacheSize = 64;

        // Core components (refactored for better separation)
        private CountryDataManager dataManager;
        private CountryStateLoader stateLoader;

        // Native arrays (owned by this class, passed to components)
        private NativeArray<CountryHotData> countryHotData;
        private NativeArray<Color32> countryColors;
        private NativeArray<ushort> countryTagHashes;
        private NativeArray<byte> countryGraphicalCultures;
        private NativeArray<byte> countryFlags;
        private NativeHashMap<ushort, ushort> tagHashToId;
        private NativeHashMap<ushort, ushort> idToTagHash;
        private NativeList<ushort> activeCountryIds;

        // Cold data (managed collections)
        private Dictionary<ushort, CountryColdData> coldDataCache;
        private Dictionary<ushort, string> countryTags;
        private HashSet<string> usedTags;

        // State
        private bool isInitialized;
        private EventBus eventBus;

        // Properties
        public int CountryCount => dataManager?.CountryCount ?? 0;
        public int Capacity => countryHotData.IsCreated ? countryHotData.Length : 0;
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// Initialize the country system with event bus
        /// </summary>
        public void Initialize(EventBus eventBus)
        {
            this.eventBus = eventBus;

            // Allocate native arrays for hot data
            countryHotData = new NativeArray<CountryHotData>(initialCapacity, Allocator.Persistent);
            countryColors = new NativeArray<Color32>(initialCapacity, Allocator.Persistent);
            countryTagHashes = new NativeArray<ushort>(initialCapacity, Allocator.Persistent);
            countryGraphicalCultures = new NativeArray<byte>(initialCapacity, Allocator.Persistent);
            countryFlags = new NativeArray<byte>(initialCapacity, Allocator.Persistent);

            // ID management
            tagHashToId = new NativeHashMap<ushort, ushort>(initialCapacity, Allocator.Persistent);
            idToTagHash = new NativeHashMap<ushort, ushort>(initialCapacity, Allocator.Persistent);
            activeCountryIds = new NativeList<ushort>(initialCapacity, Allocator.Persistent);

            // Cold data management
            coldDataCache = new Dictionary<ushort, CountryColdData>(coldDataCacheSize);
            countryTags = new Dictionary<ushort, string>(initialCapacity);
            usedTags = new HashSet<string>(initialCapacity);

            // Initialize components with references to native arrays
            dataManager = new CountryDataManager(
                countryHotData, countryColors, countryTagHashes,
                countryGraphicalCultures, countryFlags,
                tagHashToId, idToTagHash, activeCountryIds,
                coldDataCache, countryTags, usedTags,
                eventBus, enableColdDataCaching
            );

            stateLoader = new CountryStateLoader(dataManager, eventBus, initialCapacity);

            isInitialized = true;
            ArchonLogger.Log($"CountrySystem initialized with capacity {initialCapacity}");

            // Validate CountryHotData size
            ValidateCountryHotDataSize();
        }

        // ===== LOADING OPERATIONS (delegated to CountryStateLoader) =====

        public void InitializeFromCountryData(CountryDataLoadResult countryDataResult)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("CountrySystem not initialized - call Initialize() first");
                return;
            }
            stateLoader.InitializeFromCountryData(countryDataResult);
        }

        // ===== DATA ACCESS OPERATIONS (delegated to CountryDataManager) =====

        public Color32 GetCountryColor(ushort countryId) => dataManager.GetCountryColor(countryId);
        public string GetCountryTag(ushort countryId) => dataManager.GetCountryTag(countryId);
        public ushort GetCountryIdFromTag(string tag) => dataManager.GetCountryIdFromTag(tag);
        public CountryHotData GetCountryHotData(ushort countryId) => dataManager.GetCountryHotData(countryId);
        public CountryColdData GetCountryColdData(ushort countryId) => dataManager.GetCountryColdData(countryId);
        public byte GetCountryGraphicalCulture(ushort countryId) => dataManager.GetCountryGraphicalCulture(countryId);
        public bool HasCountryFlag(ushort countryId, byte flag) => dataManager.HasCountryFlag(countryId, flag);
        public NativeArray<ushort> GetAllCountryIds(Allocator allocator = Allocator.TempJob) => dataManager.GetAllCountryIds(allocator);
        public bool HasCountry(ushort countryId) => dataManager.HasCountry(countryId);
        public void SetCountryColor(ushort countryId, Color32 newColor) => dataManager.SetCountryColor(countryId, newColor);

        // Old code removed - now handled by CountryDataManager and CountryStateLoader:
        // - AddDefaultUnownedCountry()
        // - AddCountry()
        // - CreateHotDataFromCold()
        // - GenerateColorFromTag()
        // - GetGraphicalCultureId()
        // - CalculateTagHash()


        /// <summary>
        /// Validate that CountryHotData is exactly 8 bytes
        /// </summary>
        private void ValidateCountryHotDataSize()
        {
            int actualSize = UnsafeUtility.SizeOf<CountryHotData>();
            if (actualSize != 8)
            {
                ArchonLogger.LogError($"CountryHotData size validation failed: expected 8 bytes, got {actualSize} bytes");
            }
            else
            {
                ArchonLogger.Log("CountryHotData size validation passed: 8 bytes");
            }
        }

        // ====================================================================
        // SAVE/LOAD SUPPORT
        // ====================================================================

        /// <summary>
        /// Save CountrySystem state to binary writer
        /// Serializes: capacity, hot data arrays, id mappings, tags, cold data cache
        /// </summary>
        public void SaveState(System.IO.BinaryWriter writer)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("CountrySystem: Cannot save state - not initialized");
                return;
            }

            // Write capacity
            writer.Write(initialCapacity);

            // Write country count
            writer.Write(CountryCount);

            // Write hot data arrays
            Core.SaveLoad.SerializationHelper.WriteNativeArray(writer, countryHotData);
            Core.SaveLoad.SerializationHelper.WriteNativeArray(writer, countryColors);
            Core.SaveLoad.SerializationHelper.WriteNativeArray(writer, countryTagHashes);
            Core.SaveLoad.SerializationHelper.WriteNativeArray(writer, countryGraphicalCultures);
            Core.SaveLoad.SerializationHelper.WriteNativeArray(writer, countryFlags);

            // Write tagHashToId mapping
            writer.Write(tagHashToId.Count);
            foreach (var kvp in tagHashToId)
            {
                writer.Write(kvp.Key);   // tagHash (ushort)
                writer.Write(kvp.Value); // countryId (ushort)
            }

            // Write idToTagHash mapping
            writer.Write(idToTagHash.Count);
            foreach (var kvp in idToTagHash)
            {
                writer.Write(kvp.Key);   // countryId (ushort)
                writer.Write(kvp.Value); // tagHash (ushort)
            }

            // Write activeCountryIds list
            writer.Write(activeCountryIds.Length);
            for (int i = 0; i < activeCountryIds.Length; i++)
            {
                writer.Write(activeCountryIds[i]);
            }

            // Write countryTags dictionary
            writer.Write(countryTags.Count);
            foreach (var kvp in countryTags)
            {
                writer.Write(kvp.Key);   // countryId (ushort)
                Core.SaveLoad.SerializationHelper.WriteString(writer, kvp.Value); // tag (string)
            }

            // Write usedTags set
            writer.Write(usedTags.Count);
            foreach (var tag in usedTags)
            {
                Core.SaveLoad.SerializationHelper.WriteString(writer, tag);
            }

            // Write coldDataCache (complex data)
            writer.Write(coldDataCache.Count);
            foreach (var kvp in coldDataCache)
            {
                writer.Write(kvp.Key);   // countryId (ushort)
                SaveCountryColdData(writer, kvp.Value);
            }

            ArchonLogger.Log($"CountrySystem: Saved {CountryCount} countries ({initialCapacity} capacity)");
        }

        /// <summary>
        /// Load CountrySystem state from binary reader
        /// Restores: capacity, hot data arrays, id mappings, tags, cold data cache
        /// Note: Must be called AFTER Initialize() but BEFORE any country operations
        /// </summary>
        public void LoadState(System.IO.BinaryReader reader)
        {
            if (!isInitialized)
            {
                ArchonLogger.LogError("CountrySystem: Cannot load state - not initialized");
                return;
            }

            // Read capacity
            int savedCapacity = reader.ReadInt32();

            // Verify capacity matches
            if (savedCapacity != initialCapacity)
            {
                ArchonLogger.LogWarning($"CountrySystem: Capacity mismatch (saved: {savedCapacity}, current: {initialCapacity})");
            }

            // Read country count
            int savedCountryCount = reader.ReadInt32();

            // Clear existing data
            dataManager.Clear();

            // Read hot data arrays
            Core.SaveLoad.SerializationHelper.ReadNativeArray(reader, countryHotData);
            Core.SaveLoad.SerializationHelper.ReadNativeArray(reader, countryColors);
            Core.SaveLoad.SerializationHelper.ReadNativeArray(reader, countryTagHashes);
            Core.SaveLoad.SerializationHelper.ReadNativeArray(reader, countryGraphicalCultures);
            Core.SaveLoad.SerializationHelper.ReadNativeArray(reader, countryFlags);

            // Read tagHashToId mapping
            int tagHashToIdCount = reader.ReadInt32();
            for (int i = 0; i < tagHashToIdCount; i++)
            {
                ushort tagHash = reader.ReadUInt16();
                ushort countryId = reader.ReadUInt16();
                tagHashToId.TryAdd(tagHash, countryId);
            }

            // Read idToTagHash mapping
            int idToTagHashCount = reader.ReadInt32();
            for (int i = 0; i < idToTagHashCount; i++)
            {
                ushort countryId = reader.ReadUInt16();
                ushort tagHash = reader.ReadUInt16();
                idToTagHash.TryAdd(countryId, tagHash);
            }

            // Read activeCountryIds list
            int activeCount = reader.ReadInt32();
            for (int i = 0; i < activeCount; i++)
            {
                ushort countryId = reader.ReadUInt16();
                activeCountryIds.Add(countryId);
            }

            // Read countryTags dictionary
            int countryTagsCount = reader.ReadInt32();
            for (int i = 0; i < countryTagsCount; i++)
            {
                ushort countryId = reader.ReadUInt16();
                string tag = Core.SaveLoad.SerializationHelper.ReadString(reader);
                countryTags[countryId] = tag;
            }

            // Read usedTags set
            int usedTagsCount = reader.ReadInt32();
            for (int i = 0; i < usedTagsCount; i++)
            {
                string tag = Core.SaveLoad.SerializationHelper.ReadString(reader);
                usedTags.Add(tag);
            }

            // Read coldDataCache
            int coldDataCount = reader.ReadInt32();
            for (int i = 0; i < coldDataCount; i++)
            {
                ushort countryId = reader.ReadUInt16();
                CountryColdData coldData = LoadCountryColdData(reader);
                coldDataCache[countryId] = coldData;
            }

            // CRITICAL: Restore countryCount (dataManager.Clear() set it to 0)
            dataManager.RestoreCountryCount(savedCountryCount);

            ArchonLogger.Log($"CountrySystem: Loaded {savedCountryCount} countries (capacity: {savedCapacity})");
        }

        /// <summary>
        /// Save CountryColdData to binary writer
        /// </summary>
        private void SaveCountryColdData(System.IO.BinaryWriter writer, CountryColdData data)
        {
            Core.SaveLoad.SerializationHelper.WriteString(writer, data.tag ?? "");
            Core.SaveLoad.SerializationHelper.WriteString(writer, data.displayName ?? "");
            Core.SaveLoad.SerializationHelper.WriteString(writer, data.graphicalCulture ?? "");

            // Color32 (4 bytes)
            writer.Write(data.color.r);
            writer.Write(data.color.g);
            writer.Write(data.color.b);
            writer.Write(data.color.a);

            // Revolutionary colors (4 bytes)
            writer.Write(data.revolutionaryColors.r);
            writer.Write(data.revolutionaryColors.g);
            writer.Write(data.revolutionaryColors.b);
            writer.Write(data.revolutionaryColors.a);

            Core.SaveLoad.SerializationHelper.WriteString(writer, data.preferredReligion ?? "");

            // Historical idea groups
            writer.Write(data.historicalIdeaGroups?.Count ?? 0);
            if (data.historicalIdeaGroups != null)
            {
                foreach (var idea in data.historicalIdeaGroups)
                    Core.SaveLoad.SerializationHelper.WriteString(writer, idea);
            }

            // Historical units
            writer.Write(data.historicalUnits?.Count ?? 0);
            if (data.historicalUnits != null)
            {
                foreach (var unit in data.historicalUnits)
                    Core.SaveLoad.SerializationHelper.WriteString(writer, unit);
            }

            // Monarch names
            writer.Write(data.monarchNames?.Count ?? 0);
            if (data.monarchNames != null)
            {
                foreach (var kvp in data.monarchNames)
                {
                    Core.SaveLoad.SerializationHelper.WriteString(writer, kvp.Key);
                    writer.Write(kvp.Value);
                }
            }

            // Metadata (skip - can be reconstructed)
            // lastParsed, parseTimeMs, hasParseErrors, parseErrors
        }

        /// <summary>
        /// Load CountryColdData from binary reader
        /// </summary>
        private CountryColdData LoadCountryColdData(System.IO.BinaryReader reader)
        {
            var data = new CountryColdData();

            data.tag = Core.SaveLoad.SerializationHelper.ReadString(reader);
            data.displayName = Core.SaveLoad.SerializationHelper.ReadString(reader);
            data.graphicalCulture = Core.SaveLoad.SerializationHelper.ReadString(reader);

            // Color32
            data.color = new Color32(
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte()
            );

            // Revolutionary colors
            data.revolutionaryColors = new Color32(
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte()
            );

            data.preferredReligion = Core.SaveLoad.SerializationHelper.ReadString(reader);

            // Historical idea groups
            int ideaCount = reader.ReadInt32();
            data.historicalIdeaGroups = new List<string>(ideaCount);
            for (int i = 0; i < ideaCount; i++)
                data.historicalIdeaGroups.Add(Core.SaveLoad.SerializationHelper.ReadString(reader));

            // Historical units
            int unitCount = reader.ReadInt32();
            data.historicalUnits = new List<string>(unitCount);
            for (int i = 0; i < unitCount; i++)
                data.historicalUnits.Add(Core.SaveLoad.SerializationHelper.ReadString(reader));

            // Monarch names
            int monarchCount = reader.ReadInt32();
            data.monarchNames = new Dictionary<string, int>(monarchCount);
            for (int i = 0; i < monarchCount; i++)
            {
                string name = Core.SaveLoad.SerializationHelper.ReadString(reader);
                int weight = reader.ReadInt32();
                data.monarchNames[name] = weight;
            }

            return data;
        }

        public void Dispose()
        {
            if (countryHotData.IsCreated) countryHotData.Dispose();
            if (countryColors.IsCreated) countryColors.Dispose();
            if (countryTagHashes.IsCreated) countryTagHashes.Dispose();
            if (countryGraphicalCultures.IsCreated) countryGraphicalCultures.Dispose();
            if (countryFlags.IsCreated) countryFlags.Dispose();
            if (tagHashToId.IsCreated) tagHashToId.Dispose();
            if (idToTagHash.IsCreated) idToTagHash.Dispose();
            if (activeCountryIds.IsCreated) activeCountryIds.Dispose();

            coldDataCache?.Clear();
            countryTags?.Clear();
            usedTags?.Clear();

            isInitialized = false;
            ArchonLogger.Log("CountrySystem disposed");
        }

        void OnDestroy()
        {
            Dispose();
        }

        #if UNITY_EDITOR
        [Header("Debug Info")]
        [SerializeField, ReadOnly] private int debugCountryCount;
        [SerializeField, ReadOnly] private int debugCachedColdData;
        [SerializeField, ReadOnly] private int debugMemoryUsageKB;

        void Update()
        {
            if (isInitialized)
            {
                debugCountryCount = CountryCount;
                debugCachedColdData = coldDataCache?.Count ?? 0;
                debugMemoryUsageKB = (CountryCount * 8) / 1024; // 8 bytes per country hot data
            }
        }
        #endif
    }

    // Country-related events moved to Country/CountryEvents.cs
}