using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;
using Core.Data;
using Core.Loaders;

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

        // Hot data - frequently accessed (8 bytes per country)
        private NativeArray<CountryHotData> countryHotData;

        // Structure of Arrays for most accessed data
        private NativeArray<Color32> countryColors;          // Most accessed for rendering
        private NativeArray<ushort> countryTagHashes;        // Second most accessed for identification
        private NativeArray<byte> countryGraphicalCultures;  // Used for unit graphics
        private NativeArray<byte> countryFlags;              // Least accessed hot data

        // Country ID management
        private NativeHashMap<ushort, ushort> tagHashToId;     // Tag hash -> Country ID lookup
        private NativeHashMap<ushort, ushort> idToTagHash;     // Country ID -> Tag hash lookup
        private NativeList<ushort> activeCountryIds;           // List of valid country IDs

        // Cold data management (lazy-loaded, cached)
        private Dictionary<ushort, CountryColdData> coldDataCache;
        private Dictionary<ushort, string> countryTags;        // Country ID -> 3-letter tag (e.g., "ENG")
        private HashSet<string> usedTags;                    // Track used tags for duplicate detection

        // Performance tracking
        private int countryCount;
        private bool isInitialized;
        private EventBus eventBus;

        // Properties
        public int CountryCount => countryCount;
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

            isInitialized = true;
            DominionLogger.Log($"CountrySystem initialized with capacity {initialCapacity}");

            // Validate CountryHotData size
            ValidateCountryHotDataSize();
        }

        /// <summary>
        /// Initialize countries from JobifiedCountryLoader result
        /// </summary>
        public void InitializeFromCountryData(CountryDataLoadResult countryDataResult)
        {
            if (!isInitialized)
            {
                DominionLogger.LogError("CountrySystem not initialized - call Initialize() first");
                return;
            }

            if (!countryDataResult.Success)
            {
                DominionLogger.LogError($"Cannot initialize from failed country data: {countryDataResult.ErrorMessage}");
                return;
            }

            var countryData = countryDataResult.Countries;
            DominionLogger.Log($"Initializing {countryData.Count} countries from data");

            // Clear existing data
            countryCount = 0;
            tagHashToId.Clear();
            idToTagHash.Clear();
            activeCountryIds.Clear();
            coldDataCache.Clear();
            countryTags.Clear();
            usedTags.Clear();

            // Add default "unowned" country at ID 0
            AddDefaultUnownedCountry();

            // Process each country from the loaded data
            ushort nextCountryId = 1; // Start from 1 (0 is reserved for unowned)

            for (int i = 0; i < countryData.Count; i++)
            {
                var country = countryData.GetCountryByIndex(i);
                if (country == null) continue;

                var tag = country.Tag;
                var hotData = country.hotData; // Use the hotData from Burst job (already has correct color!)
                var coldData = country.coldData;

                // Skip duplicates before assigning ID
                if (usedTags.Contains(tag))
                {
                    // Don't increment nextCountryId - reuse this slot for next non-duplicate
                    if (i < 50)
                    {
                        DominionLogger.Log($"CountrySystem: Skipping duplicate tag '{tag}' at index {i}");
                    }
                    continue;
                }

                // DEBUG: Log colors for first 20 countries
                if (nextCountryId < 50)
                {
                    var color = hotData.Color;
                    DominionLogger.Log($"CountrySystem: Country index {i} tag={tag} → ID {nextCountryId}, hotData color R={color.r} G={color.g} B={color.b}");
                }

                // Add country to system
                AddCountry(nextCountryId, tag, hotData, coldData);
                nextCountryId++;

                if (nextCountryId >= initialCapacity)
                {
                    DominionLogger.LogWarning($"Country capacity exceeded: {nextCountryId}/{initialCapacity}");
                    break;
                }
            }

            DominionLogger.Log($"CountrySystem initialized with {countryCount} countries");

            // Emit initialization complete event
            eventBus?.Emit(new CountrySystemInitializedEvent
            {
                CountryCount = countryCount
            });
        }

        /// <summary>
        /// Add the default "unowned" country at ID 0
        /// </summary>
        private void AddDefaultUnownedCountry()
        {
            var defaultHotData = new CountryHotData
            {
                tagHash = 0, // No tag
                graphicalCultureId = 0,
                flags = 0
            };
            defaultHotData.SetColor(Color.gray);

            var defaultColdData = new CountryColdData
            {
                tag = "---",
                displayName = "Unowned",
                graphicalCulture = "western",
                // ... other default values
            };

            AddCountry(0, "---", defaultHotData, defaultColdData);
        }

        /// <summary>
        /// Add a new country to the system
        /// </summary>
        private void AddCountry(ushort countryId, string tag, CountryHotData hotData, CountryColdData coldData)
        {
            if (countryId >= countryHotData.Length)
            {
                DominionLogger.LogError($"Country ID {countryId} exceeds capacity {countryHotData.Length}");
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
                UnityEngine.Debug.Log($"CountrySystem.RegisterCountry: Country {countryId} ({tag}) - Packed: 0x{hotData.colorRGB:X8}, Color property: R={color.r} G={color.g} B={color.b} A={color.a}");
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

        /// <summary>
        /// Create hot data from cold data
        /// </summary>
        private CountryHotData CreateHotDataFromCold(CountryColdData coldData)
        {
            // Use loaded color from EU4 data, fallback to generated color only if black (missing data)
            var color = coldData.color;
            if (color.r == 0 && color.g == 0 && color.b == 0)
            {
                // Only generate fallback color if loaded color is black (missing/invalid)
                color = GenerateColorFromTag(coldData.tag);
            }

            // Convert graphical culture string to ID (simplified mapping for now)
            byte graphicalCultureId = GetGraphicalCultureId(coldData.graphicalCulture);

            var hotData = new CountryHotData
            {
                graphicalCultureId = graphicalCultureId,
                flags = 0 // Will be set based on cold data properties
            };
            hotData.SetColor(color);

            // Set flags based on cold data
            if (coldData.historicalIdeaGroups != null && coldData.historicalIdeaGroups.Count > 0)
                hotData.SetFlag(CountryHotData.FLAG_HAS_HISTORICAL_IDEAS, true);

            if (coldData.historicalUnits != null && coldData.historicalUnits.Count > 0)
                hotData.SetFlag(CountryHotData.FLAG_HAS_HISTORICAL_UNITS, true);

            if (coldData.monarchNames != null && coldData.monarchNames.Count > 0)
                hotData.SetFlag(CountryHotData.FLAG_HAS_MONARCH_NAMES, true);

            if (coldData.revolutionaryColors.a > 0)
                hotData.SetFlag(CountryHotData.FLAG_HAS_REVOLUTIONARY_COLORS, true);

            if (!string.IsNullOrEmpty(coldData.preferredReligion))
                hotData.SetFlag(CountryHotData.FLAG_HAS_PREFERRED_RELIGION, true);

            return hotData;
        }

        /// <summary>
        /// Generate a consistent color from country tag
        /// </summary>
        private Color32 GenerateColorFromTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return new Color32(128, 128, 128, 255);

            // Generate a consistent color from tag hash
            uint hash = (uint)tag.GetHashCode();
            return new Color32(
                (byte)((hash >> 16) & 0xFF),
                (byte)((hash >> 8) & 0xFF),
                (byte)(hash & 0xFF),
                255
            );
        }

        /// <summary>
        /// Convert graphical culture string to ID (simplified mapping)
        /// </summary>
        private byte GetGraphicalCultureId(string graphicalCulture)
        {
            if (string.IsNullOrEmpty(graphicalCulture))
                return 0;

            // Simplified mapping - in a full implementation this would be a proper lookup table
            switch (graphicalCulture.ToLower())
            {
                case "western":
                case "westerneuropean":
                    return 1;
                case "eastern":
                case "easterneuropean":
                    return 2;
                case "muslim":
                case "middleeast":
                    return 3;
                case "indian":
                    return 4;
                case "chinese":
                    return 5;
                case "african":
                    return 6;
                default:
                    return 0; // Default/unknown
            }
        }

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
                UnityEngine.Debug.Log($"CountrySystem.GetCountryColor: Country {countryId} ({GetCountryTag(countryId)}) - Packed: 0x{hotData.colorRGB:X8}, Unpacked: R={color.r} G={color.g} B={color.b} A={color.a}");
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
            DominionLogger.LogWarning($"Cold data for country {countryId} not cached and lazy loading not implemented");
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

        /// <summary>
        /// Validate that CountryHotData is exactly 8 bytes
        /// </summary>
        private void ValidateCountryHotDataSize()
        {
            int actualSize = UnsafeUtility.SizeOf<CountryHotData>();
            if (actualSize != 8)
            {
                DominionLogger.LogError($"CountryHotData size validation failed: expected 8 bytes, got {actualSize} bytes");
            }
            else
            {
                DominionLogger.Log("CountryHotData size validation passed: 8 bytes");
            }
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
            DominionLogger.Log("CountrySystem disposed");
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
                debugCountryCount = countryCount;
                debugCachedColdData = coldDataCache?.Count ?? 0;
                debugMemoryUsageKB = (countryCount * 8) / 1024; // 8 bytes per country hot data
            }
        }
        #endif
    }

    // Country-related events
    public struct CountrySystemInitializedEvent : IGameEvent
    {
        public int CountryCount;
        public float TimeStamp { get; set; }
    }

    public struct CountryColorChangedEvent : IGameEvent
    {
        public ushort CountryId;
        public Color32 OldColor;
        public Color32 NewColor;
        public float TimeStamp { get; set; }
    }
}