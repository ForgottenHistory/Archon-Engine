using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;

namespace Core.Data
{
    /// <summary>
    /// Country data following dual-layer architecture principles
    /// Hot data: Frequently accessed, performance-critical (8 bytes)
    /// Cold data: Rarely accessed, can be loaded on-demand
    /// </summary>
    [Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    public struct CountryHotData
    {
        // CRITICAL: This struct must be exactly 8 bytes for performance
        public ushort tagHash;           // 2 bytes - hash of 3-letter tag (e.g., "ENG")
        public uint colorRGB;            // 4 bytes - packed RGB color (0xRRGGBB00, alpha in lowest byte)
        public byte graphicalCultureId;  // 1 byte - ID for graphical culture
        public byte flags;               // 1 byte - bit flags for various properties

        // Bit flags for the flags field
        public const byte FLAG_HAS_HISTORICAL_IDEAS = 1 << 0;
        public const byte FLAG_HAS_HISTORICAL_UNITS = 1 << 1;
        public const byte FLAG_HAS_MONARCH_NAMES = 1 << 2;
        public const byte FLAG_HAS_REVOLUTIONARY_COLORS = 1 << 3;
        public const byte FLAG_HAS_PREFERRED_RELIGION = 1 << 4;
        // 3 bits remaining for future use

        public bool HasHistoricalIdeas => (flags & FLAG_HAS_HISTORICAL_IDEAS) != 0;
        public bool HasHistoricalUnits => (flags & FLAG_HAS_HISTORICAL_UNITS) != 0;
        public bool HasMonarchNames => (flags & FLAG_HAS_MONARCH_NAMES) != 0;
        public bool HasRevolutionaryColors => (flags & FLAG_HAS_REVOLUTIONARY_COLORS) != 0;
        public bool HasPreferredReligion => (flags & FLAG_HAS_PREFERRED_RELIGION) != 0;

        /// <summary>
        /// Convert packed RGB color to Color32 for rendering
        /// </summary>
        public Color32 Color => new Color32(
            (byte)((colorRGB >> 24) & 0xFF),  // Red
            (byte)((colorRGB >> 16) & 0xFF),  // Green
            (byte)((colorRGB >> 8) & 0xFF),   // Blue
            (byte)(colorRGB & 0xFF)           // Alpha
        );

        /// <summary>
        /// Set color from Color32
        /// </summary>
        public void SetColor(Color32 color)
        {
            colorRGB = ((uint)color.r << 24) | ((uint)color.g << 16) | ((uint)color.b << 8) | color.a;
        }

        public void SetFlag(byte flag, bool value)
        {
            if (value)
                flags |= flag;
            else
                flags &= (byte)~flag;
        }
    }

    /// <summary>
    /// Cold data for countries - loaded on-demand, stored separately
    /// Contains detailed information not needed for core simulation
    /// </summary>
    [Serializable]
    public class CountryColdData
    {
        public string tag;                          // 3-letter country tag (e.g., "ENG")
        public string displayName;                  // Extracted from filename
        public string graphicalCulture;             // Full graphical culture name
        public Color32 color;                       // Main country color (from EU4 data)
        public Color32 revolutionaryColors;         // Alternative color scheme
        public string preferredReligion;            // Religion preference

        // Historical data - can be large, rarely accessed
        public List<string> historicalIdeaGroups;   // Idea group progression
        public List<string> historicalUnits;        // Unit progression
        public Dictionary<string, int> monarchNames; // Name -> weight mapping

        // Metadata
        public DateTime lastParsed;                 // When this data was last loaded
        public int parseTimeMs;                     // Time taken to parse (for performance monitoring)
        public bool hasParseErrors;                 // Whether parsing had issues
        public List<string> parseErrors;            // Detailed error messages

        public CountryColdData()
        {
            historicalIdeaGroups = new List<string>();
            historicalUnits = new List<string>();
            monarchNames = new Dictionary<string, int>();
            parseErrors = new List<string>();
            lastParsed = DateTime.Now;
        }

        /// <summary>
        /// Calculate memory usage of this cold data instance
        /// </summary>
        public long GetMemoryUsage()
        {
            long memory = 0;

            // String sizes (approximate)
            memory += (tag?.Length ?? 0) * 2;
            memory += (displayName?.Length ?? 0) * 2;
            memory += (graphicalCulture?.Length ?? 0) * 2;
            memory += (preferredReligion?.Length ?? 0) * 2;

            // Collections
            if (historicalIdeaGroups != null)
            {
                foreach (var idea in historicalIdeaGroups)
                    memory += (idea?.Length ?? 0) * 2;
            }

            if (historicalUnits != null)
            {
                foreach (var unit in historicalUnits)
                    memory += (unit?.Length ?? 0) * 2;
            }

            if (monarchNames != null)
            {
                foreach (var kvp in monarchNames)
                    memory += (kvp.Key?.Length ?? 0) * 2 + 4; // string + int
            }

            if (parseErrors != null)
            {
                foreach (var error in parseErrors)
                    memory += (error?.Length ?? 0) * 2;
            }

            return memory;
        }
    }

    /// <summary>
    /// Complete country data combining hot and cold data
    /// Used for runtime access with performance considerations
    /// </summary>
    [Serializable]
    public class CountryData
    {
        public CountryHotData hotData;      // Always loaded, cache-friendly
        public CountryColdData coldData;    // Loaded on-demand or cached

        public CountryData()
        {
            coldData = new CountryColdData();
        }

        public CountryData(CountryHotData hot, CountryColdData cold)
        {
            hotData = hot;
            coldData = cold;
        }

        // Convenience accessors
        public string Tag => coldData?.tag ?? "";
        public string DisplayName => coldData?.displayName ?? "";
        public Color32 Color => hotData.Color;
        public byte GraphicalCultureId => hotData.graphicalCultureId;

        /// <summary>
        /// Create hot data from cold data for performance optimization
        /// </summary>
        public static CountryHotData CreateHotData(CountryColdData coldData, byte graphicalCultureId)
        {
            var hotData = new CountryHotData
            {
                tagHash = ComputeTagHash(coldData.tag),
                graphicalCultureId = graphicalCultureId,
                flags = 0
            };

            // Set color using the packed format
            var defaultColor = ParseColor(coldData.tag); // Will be overridden by actual color
            hotData.SetColor(defaultColor);

            // Set flags based on available data
            hotData.SetFlag(CountryHotData.FLAG_HAS_HISTORICAL_IDEAS,
                coldData.historicalIdeaGroups?.Count > 0);
            hotData.SetFlag(CountryHotData.FLAG_HAS_HISTORICAL_UNITS,
                coldData.historicalUnits?.Count > 0);
            hotData.SetFlag(CountryHotData.FLAG_HAS_MONARCH_NAMES,
                coldData.monarchNames?.Count > 0);
            hotData.SetFlag(CountryHotData.FLAG_HAS_REVOLUTIONARY_COLORS,
                coldData.revolutionaryColors.a > 0);
            hotData.SetFlag(CountryHotData.FLAG_HAS_PREFERRED_RELIGION,
                !string.IsNullOrEmpty(coldData.preferredReligion));

            return hotData;
        }

        /// <summary>
        /// Compute hash for country tag for fast lookups
        /// </summary>
        private static ushort ComputeTagHash(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tag.Length != 3)
                return 0;

            // Simple but effective hash for 3-character tags
            return (ushort)((tag[0] << 8) | (tag[1] << 4) | tag[2]);
        }

        /// <summary>
        /// Default color calculation from tag (fallback)
        /// </summary>
        private static Color32 ParseColor(string tag)
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
        /// Validate that the country data is consistent and complete
        /// </summary>
        public bool Validate(out List<string> validationErrors)
        {
            validationErrors = new List<string>();
            bool isValid = true;

            // Validate hot data
            if (hotData.tagHash == 0)
            {
                validationErrors.Add("Tag hash is zero");
                isValid = false;
            }

            // Validate cold data
            if (coldData == null)
            {
                validationErrors.Add("Cold data is null");
                return false;
            }

            if (string.IsNullOrEmpty(coldData.tag) || coldData.tag.Length != 3)
            {
                validationErrors.Add($"Invalid tag: '{coldData.tag}' (must be 3 characters)");
                isValid = false;
            }

            if (string.IsNullOrEmpty(coldData.displayName))
            {
                validationErrors.Add("Display name is empty");
                isValid = false;
            }

            // Check flag consistency
            if (hotData.HasHistoricalIdeas && (coldData.historicalIdeaGroups?.Count ?? 0) == 0)
            {
                validationErrors.Add("HasHistoricalIdeas flag set but no historical idea groups found");
                isValid = false;
            }

            if (hotData.HasHistoricalUnits && (coldData.historicalUnits?.Count ?? 0) == 0)
            {
                validationErrors.Add("HasHistoricalUnits flag set but no historical units found");
                isValid = false;
            }

            if (hotData.HasMonarchNames && (coldData.monarchNames?.Count ?? 0) == 0)
            {
                validationErrors.Add("HasMonarchNames flag set but no monarch names found");
                isValid = false;
            }

            return isValid;
        }

        public override string ToString()
        {
            return $"Country[{Tag}] {DisplayName} - Color: {Color}, Culture: {GraphicalCultureId}, Flags: {hotData.flags:X2}";
        }
    }

    /// <summary>
    /// Collection for managing multiple countries with performance optimization
    /// Implements structure-of-arrays for hot data
    /// </summary>
    public class CountryDataCollection : IDisposable
    {
        // Hot data arrays for cache-friendly access
        private NativeArray<CountryHotData> hotDataArray;
        private Dictionary<ushort, int> tagHashToIndex;

        // Cold data stored separately
        private Dictionary<int, CountryColdData> coldDataLookup;

        // Metadata
        private int count;
        private bool isDisposed;

        public int Count => count;
        public bool IsCreated => hotDataArray.IsCreated;

        public CountryDataCollection(int capacity, Allocator allocator = Allocator.Persistent)
        {
            hotDataArray = new NativeArray<CountryHotData>(capacity, allocator);
            tagHashToIndex = new Dictionary<ushort, int>(capacity);
            coldDataLookup = new Dictionary<int, CountryColdData>(capacity);
            count = 0;
        }

        public void AddCountry(CountryData country)
        {
            if (isDisposed)
                throw new InvalidOperationException("CountryDataCollection has been disposed");

            if (count >= hotDataArray.Length)
                throw new InvalidOperationException("CountryDataCollection capacity exceeded");

            int index = count++;
            hotDataArray[index] = country.hotData;
            tagHashToIndex[country.hotData.tagHash] = index;
            coldDataLookup[index] = country.coldData;
        }

        public CountryData GetCountryByTag(string tag)
        {
            ushort hash = ComputeTagHash(tag);
            return GetCountryByHash(hash);
        }

        public CountryData GetCountryByHash(ushort tagHash)
        {
            if (tagHashToIndex.TryGetValue(tagHash, out int index))
            {
                return new CountryData(hotDataArray[index], coldDataLookup[index]);
            }
            return null;
        }

        public CountryData GetCountryByIndex(int index)
        {
            if (index < 0 || index >= count)
                return null;

            return new CountryData(hotDataArray[index], coldDataLookup[index]);
        }

        public NativeSlice<CountryHotData> GetHotDataSlice()
        {
            return hotDataArray.Slice(0, count);
        }

        private static ushort ComputeTagHash(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tag.Length != 3)
                return 0;

            return (ushort)((tag[0] << 8) | (tag[1] << 4) | tag[2]);
        }

        public void Dispose()
        {
            if (!isDisposed && hotDataArray.IsCreated)
            {
                hotDataArray.Dispose();
                isDisposed = true;
            }
        }

        /// <summary>
        /// Get total memory usage of the collection
        /// </summary>
        public long GetMemoryUsage()
        {
            long memory = 0;

            // Hot data array
            if (hotDataArray.IsCreated)
                memory += hotDataArray.Length * 8; // 8 bytes per CountryHotData

            // Hash table overhead (approximate)
            memory += tagHashToIndex.Count * 6; // ushort + int

            // Cold data
            foreach (var coldData in coldDataLookup.Values)
            {
                memory += coldData.GetMemoryUsage();
            }

            return memory;
        }
    }
}