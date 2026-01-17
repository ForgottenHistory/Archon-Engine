using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;
using Core.Data;

namespace Core.Systems.Country
{
    /// <summary>
    /// Manages country hot/cold data operations
    /// Handles get/set operations, queries, and ID mapping
    /// Extracted from CountrySystem.cs for better separation of concerns
    /// </summary>
    public class CountryDataManager
    {
        // Hot data - frequently accessed (8 bytes per country)
        private NativeArray<CountryHotData> countryHotData;

        // Structure of Arrays for most accessed data
        private NativeArray<Color32> countryColors;
        private NativeArray<ushort> countryTagHashes;
        private NativeArray<byte> countryGraphicalCultures;
        private NativeArray<byte> countryFlags;

        // Country ID management
        private NativeHashMap<ushort, ushort> tagHashToId;
        private NativeHashMap<ushort, ushort> idToTagHash;
        private NativeList<ushort> activeCountryIds;

        // Cold data management (lazy-loaded, cached)
        private readonly Dictionary<ushort, CountryColdData> coldDataCache;
        private readonly Dictionary<ushort, string> countryTags;
        private readonly HashSet<string> usedTags;

        private readonly EventBus eventBus;
        private readonly bool enableColdDataCaching;

        private int countryCount;

        public int CountryCount => countryCount;

        public CountryDataManager(
            NativeArray<CountryHotData> countryHotData,
            NativeArray<Color32> countryColors,
            NativeArray<ushort> countryTagHashes,
            NativeArray<byte> countryGraphicalCultures,
            NativeArray<byte> countryFlags,
            NativeHashMap<ushort, ushort> tagHashToId,
            NativeHashMap<ushort, ushort> idToTagHash,
            NativeList<ushort> activeCountryIds,
            Dictionary<ushort, CountryColdData> coldDataCache,
            Dictionary<ushort, string> countryTags,
            HashSet<string> usedTags,
            EventBus eventBus,
            bool enableColdDataCaching)
        {
            this.countryHotData = countryHotData;
            this.countryColors = countryColors;
            this.countryTagHashes = countryTagHashes;
            this.countryGraphicalCultures = countryGraphicalCultures;
            this.countryFlags = countryFlags;
            this.tagHashToId = tagHashToId;
            this.idToTagHash = idToTagHash;
            this.activeCountryIds = activeCountryIds;
            this.coldDataCache = coldDataCache;
            this.countryTags = countryTags;
            this.usedTags = usedTags;
            this.eventBus = eventBus;
            this.enableColdDataCaching = enableColdDataCaching;
            this.countryCount = 0;
        }

        /// <summary>
        /// Clear all country data
        /// </summary>
        public void Clear()
        {
            countryCount = 0;
            tagHashToId.Clear();
            idToTagHash.Clear();
            activeCountryIds.Clear();
            coldDataCache.Clear();
            countryTags.Clear();
            usedTags.Clear();
        }

        /// <summary>
        /// Restore country count after loading from save (Clear() sets it to 0)
        /// CRITICAL for GetAllCountryIds() to work after load
        /// </summary>
        public void RestoreCountryCount(int count)
        {
            countryCount = count;
        }

        /// <summary>
        /// Check if a tag is already used
        /// </summary>
        public bool HasTag(string tag) => usedTags.Contains(tag);

        /// <summary>
        /// Add a new country to the system
        /// </summary>
        public void AddCountry(ushort countryId, string tag, CountryHotData hotData, CountryColdData coldData)
        {
            if (countryId >= countryHotData.Length)
            {
                ArchonLogger.LogError($"Country ID {countryId} exceeds capacity {countryHotData.Length}", "core_simulation");
                return;
            }

            // Calculate tag hash
            ushort tagHash = CalculateTagHash(tag);

            // Set hot data
            hotData.tagHash = tagHash;
            countryHotData[countryId] = hotData;

            // Set structure of arrays data
            var color = hotData.Color;
            countryColors[countryId] = color;
            countryTagHashes[countryId] = tagHash;
            countryGraphicalCultures[countryId] = hotData.graphicalCultureId;
            countryFlags[countryId] = hotData.flags;

            // Debug: Log color for first few countries
            if (countryId < 5)
            {
                UnityEngine.Debug.Log($"CountrySystem.RegisterCountry: Country {countryId} ({tag}) - Packed: 0x{hotData.colorRGBA:X8}, Color property: R={color.r} G={color.g} B={color.b} A={color.a}");
            }

            // Update lookup tables
            tagHashToId[tagHash] = countryId;
            idToTagHash[countryId] = tagHash;
            countryTags[countryId] = tag;
            usedTags.Add(tag);

            // Cache cold data if enabled
            if (enableColdDataCaching)
            {
                coldDataCache[countryId] = coldData;
            }

            // Update active list
            if (countryId >= activeCountryIds.Length)
            {
                activeCountryIds.Add(countryId);
            }
            else
            {
                activeCountryIds[countryId] = countryId;
            }

            if (countryId + 1 > countryCount)
            {
                countryCount = countryId + 1;
            }
        }

        #region Data Access

        /// <summary>
        /// Get country color - most common query (must be ultra-fast)
        /// </summary>
        public Color32 GetCountryColor(ushort countryId)
        {
            if (countryId >= countryCount)
                return Color.gray;

            var color = countryColors[countryId];

            // Debug: Log color for first few countries
            if (countryId < 5)
            {
                var hotData = countryHotData[countryId];
                UnityEngine.Debug.Log($"CountrySystem.GetCountryColor: Country {countryId} ({GetCountryTag(countryId)}) - Packed: 0x{hotData.colorRGBA:X8}, Unpacked: R={color.r} G={color.g} B={color.b} A={color.a}");
            }

            return color;
        }

        /// <summary>
        /// Get country tag (3-letter code)
        /// </summary>
        public string GetCountryTag(ushort countryId)
        {
            if (countryTags.TryGetValue(countryId, out string tag))
                return tag;

            return "---";
        }

        /// <summary>
        /// Get country ID from tag
        /// </summary>
        public ushort GetCountryIdFromTag(string tag)
        {
            ushort tagHash = CalculateTagHash(tag);
            if (tagHashToId.TryGetValue(tagHash, out ushort countryId))
                return countryId;

            return 0; // Unowned
        }

        /// <summary>
        /// Get country hot data (8-byte struct)
        /// </summary>
        public CountryHotData GetCountryHotData(ushort countryId)
        {
            if (countryId >= countryCount)
                return new CountryHotData();

            return countryHotData[countryId];
        }

        /// <summary>
        /// Get country cold data (lazy-loaded)
        /// </summary>
        public CountryColdData GetCountryColdData(ushort countryId)
        {
            // Check cache first
            if (coldDataCache.TryGetValue(countryId, out CountryColdData cachedData))
                return cachedData;

            // If not cached and caching is disabled, we need to load it
            // For now, return null - in full implementation, this would trigger loading
            ArchonLogger.LogWarning($"Cold data for country {countryId} not cached and lazy loading not implemented", "core_simulation");
            return null;
        }

        /// <summary>
        /// Get graphical culture ID for a country
        /// </summary>
        public byte GetCountryGraphicalCulture(ushort countryId)
        {
            if (countryId >= countryCount)
                return 0;

            return countryGraphicalCultures[countryId];
        }

        /// <summary>
        /// Check if country has specific flag
        /// </summary>
        public bool HasCountryFlag(ushort countryId, byte flag)
        {
            if (countryId >= countryCount)
                return false;

            return (countryFlags[countryId] & flag) != 0;
        }

        /// <summary>
        /// Get all active country IDs
        /// </summary>
        public NativeArray<ushort> GetAllCountryIds(Allocator allocator = Allocator.TempJob)
        {
            var result = new NativeArray<ushort>(countryCount, allocator);
            for (int i = 0; i < countryCount; i++)
            {
                result[i] = (ushort)i;
            }
            return result;
        }

        /// <summary>
        /// Check if country exists
        /// </summary>
        public bool HasCountry(ushort countryId)
        {
            return countryId < countryCount;
        }

        /// <summary>
        /// Set country color (for dynamic changes)
        /// </summary>
        public void SetCountryColor(ushort countryId, Color32 newColor)
        {
            if (countryId >= countryCount)
                return;

            Color32 oldColor = countryColors[countryId];
            if (oldColor.r == newColor.r && oldColor.g == newColor.g &&
                oldColor.b == newColor.b && oldColor.a == newColor.a)
                return; // No change

            // Update data
            countryColors[countryId] = newColor;

            // Update hot data
            var hotData = countryHotData[countryId];
            hotData.SetColor(newColor);
            countryHotData[countryId] = hotData;

            // Emit color change event
            eventBus?.Emit(new CountryColorChangedEvent
            {
                CountryId = countryId,
                OldColor = oldColor,
                NewColor = newColor
            });
        }

        #endregion

        /// <summary>
        /// Calculate hash for country tag (3-letter code)
        /// </summary>
        private ushort CalculateTagHash(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tag.Length != 3)
                return 0;

            // Simple hash: combine 3 characters into 16 bits
            return (ushort)((tag[0] << 10) + (tag[1] << 5) + tag[2]);
        }
    }
}
