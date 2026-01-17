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
    ///
    /// ENGINE layer - generic country data only.
    /// Game-specific data should be stored via customData dictionary in CountryColdData.
    /// </summary>
    [Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    public struct CountryHotData
    {
        // CRITICAL: This struct must be exactly 8 bytes for performance
        public ushort tagHash;           // 2 bytes - hash of country tag
        public uint colorRGBA;           // 4 bytes - packed RGBA color
        public byte graphicalCultureId;  // 1 byte - ID for graphical culture
        public byte flags;               // 1 byte - generic bit flags (game defines meanings)

        // Generic flag accessors - game layer defines what these mean
        public bool GetFlag(int index) => (flags & (1 << index)) != 0;
        public void SetFlag(int index, bool value)
        {
            if (value)
                flags |= (byte)(1 << index);
            else
                flags &= (byte)~(1 << index);
        }

        /// <summary>
        /// Convert packed RGBA color to Color32 for rendering
        /// </summary>
        public Color32 Color => new Color32(
            (byte)((colorRGBA >> 24) & 0xFF),  // Red
            (byte)((colorRGBA >> 16) & 0xFF),  // Green
            (byte)((colorRGBA >> 8) & 0xFF),   // Blue
            (byte)(colorRGBA & 0xFF)           // Alpha
        );

        /// <summary>
        /// Set color from Color32
        /// </summary>
        public void SetColor(Color32 color)
        {
            colorRGBA = ((uint)color.r << 24) | ((uint)color.g << 16) | ((uint)color.b << 8) | color.a;
        }
    }

    /// <summary>
    /// Cold data for countries - loaded on-demand, stored separately.
    /// Contains detailed information not needed for core simulation.
    ///
    /// ENGINE layer - generic country data only.
    /// Game-specific data should be stored in customData dictionary.
    /// </summary>
    [Serializable]
    public class CountryColdData
    {
        // Core identity
        public string tag;                          // Country tag/identifier
        public string displayName;                  // Display name for UI

        // Visual
        public string graphicalCulture;             // Full graphical culture name
        public Color32 color;                       // Main country color

        // Metadata
        public DateTime lastParsed;                 // When this data was last loaded
        public int parseTimeMs;                     // Time taken to parse
        public bool hasParseErrors;                 // Whether parsing had issues
        public List<string> parseErrors;            // Detailed error messages

        // Game-specific extension point
        // Games can store arbitrary data here (e.g., historicalIdeas, monarchNames, etc.)
        public Dictionary<string, object> customData;

        public CountryColdData()
        {
            parseErrors = new List<string>();
            customData = new Dictionary<string, object>();
            lastParsed = DateTime.Now;
        }

        /// <summary>
        /// Get custom data with type safety
        /// </summary>
        public T GetCustomData<T>(string key, T defaultValue = default)
        {
            if (customData != null && customData.TryGetValue(key, out var value) && value is T typedValue)
                return typedValue;
            return defaultValue;
        }

        /// <summary>
        /// Set custom data
        /// </summary>
        public void SetCustomData(string key, object value)
        {
            customData ??= new Dictionary<string, object>();
            customData[key] = value;
        }

        /// <summary>
        /// Calculate memory usage of this cold data instance
        /// </summary>
        public long GetMemoryUsage()
        {
            long memory = 0;

            // String sizes (approximate, 2 bytes per char in .NET)
            memory += (tag?.Length ?? 0) * 2;
            memory += (displayName?.Length ?? 0) * 2;
            memory += (graphicalCulture?.Length ?? 0) * 2;

            if (parseErrors != null)
            {
                foreach (var error in parseErrors)
                    memory += (error?.Length ?? 0) * 2;
            }

            // Custom data - rough estimate
            if (customData != null)
            {
                foreach (var kvp in customData)
                {
                    memory += (kvp.Key?.Length ?? 0) * 2;
                    // Estimate value size based on type
                    if (kvp.Value is string s)
                        memory += s.Length * 2;
                    else if (kvp.Value is ICollection<object> collection)
                        memory += collection.Count * 8; // rough estimate
                    else
                        memory += 8; // default object reference size
                }
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
        public static CountryHotData CreateHotData(CountryColdData coldData, byte graphicalCultureId, byte flags = 0)
        {
            var hotData = new CountryHotData
            {
                tagHash = ComputeTagHash(coldData.tag),
                graphicalCultureId = graphicalCultureId,
                flags = flags
            };

            // Set color - use cold data color if available, otherwise generate from tag
            if (coldData.color.a > 0)
                hotData.SetColor(coldData.color);
            else
                hotData.SetColor(GenerateColorFromTag(coldData.tag));

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
        /// Generate a consistent color from tag (fallback when no color specified)
        /// </summary>
        private static Color32 GenerateColorFromTag(string tag)
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

            if (string.IsNullOrEmpty(coldData.tag))
            {
                validationErrors.Add("Tag is empty");
                isValid = false;
            }

            if (string.IsNullOrEmpty(coldData.displayName))
            {
                validationErrors.Add("Display name is empty");
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