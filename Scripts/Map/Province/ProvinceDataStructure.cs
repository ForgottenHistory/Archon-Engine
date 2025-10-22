using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace Map.Province
{
    /// <summary>
    /// Optimized province data structure for texture-based rendering system
    /// Designed for performance with 10,000+ provinces
    /// </summary>
    [System.Serializable]
    public struct ProvinceData
    {
        public ushort id;                    // Province unique identifier (1-65535)
        public ushort ownerCountryID;        // Country that owns this province
        public Color32 identifierColor;     // Original bitmap color for identification
        public Color32 displayColor;        // Current display color (political, terrain, etc.)
        public float2 centerPoint;          // Center point for label placement
        public int pixelCount;               // Number of pixels this province occupies
        public float2 boundsMin;             // Bounding rectangle minimum
        public float2 boundsMax;             // Bounding rectangle maximum

        // Flags packed into single byte for memory efficiency
        public ProvinceFlags flags;

        /// <summary>
        /// Calculate center point from pixel list
        /// </summary>
        public static float2 CalculateCenterPoint(NativeArray<int2> pixels)
        {
            if (pixels.Length == 0)
                return float2.zero;

            long totalX = 0;
            long totalY = 0;

            for (int i = 0; i < pixels.Length; i++)
            {
                totalX += pixels[i].x;
                totalY += pixels[i].y;
            }

            return new float2(
                (float)totalX / pixels.Length,
                (float)totalY / pixels.Length
            );
        }

        /// <summary>
        /// Calculate bounding rectangle from pixels
        /// </summary>
        public static (float2 min, float2 max) CalculateBounds(NativeArray<int2> pixels)
        {
            if (pixels.Length == 0)
                return (float2.zero, float2.zero);

            float2 min = new float2(float.MaxValue);
            float2 max = new float2(float.MinValue);

            for (int i = 0; i < pixels.Length; i++)
            {
                float2 pixel = new float2(pixels[i].x, pixels[i].y);
                min = math.min(min, pixel);
                max = math.max(max, pixel);
            }

            return (min, max);
        }
    }

    /// <summary>
    /// Packed flags for province properties
    /// </summary>
    [System.Flags]
    public enum ProvinceFlags : byte
    {
        None = 0,
        IsCoastal = 1 << 0,          // Province touches water
        IsImpassable = 1 << 1,       // Mountains, lakes, etc.
        IsCapital = 1 << 2,          // Country capital
        IsOccupied = 1 << 3,         // Under foreign occupation
        HasReligiousCenter = 1 << 4, // Important religious site
        IsTradeCenter = 1 << 5,      // Major trade hub
        IsBorderProvince = 1 << 6,   // Touches foreign territory
        IsSelected = 1 << 7          // Currently selected in UI
    }

    /// <summary>
    /// Fast hash function for Color32 lookups
    /// Optimized for province color distributions
    /// </summary>
    public static class ProvinceColorHasher
    {
        /// <summary>
        /// Fast hash function for Color32 values
        /// Uses FNV-1a hash for good distribution
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint HashColor32(Color32 color)
        {
            const uint FNV_OFFSET_BASIS = 2166136261u;
            const uint FNV_PRIME = 16777619u;

            uint hash = FNV_OFFSET_BASIS;
            hash = (hash ^ color.r) * FNV_PRIME;
            hash = (hash ^ color.g) * FNV_PRIME;
            hash = (hash ^ color.b) * FNV_PRIME;
            // Skip alpha channel as it's typically 255 for province colors

            return hash;
        }

        /// <summary>
        /// Alternative hash using bit manipulation for very fast computation
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint FastHashColor32(Color32 color)
        {
            // Pack RGB into single 32-bit value, then hash
            uint packed = ((uint)color.r << 16) | ((uint)color.g << 8) | color.b;

            // Wang hash - very fast and good distribution
            packed = (packed ^ 61) ^ (packed >> 16);
            packed *= 9;
            packed ^= packed >> 4;
            packed *= 0x27d4eb2d;
            packed ^= packed >> 15;

            return packed;
        }
    }

    /// <summary>
    /// High-performance province data manager optimized for 10,000+ provinces
    /// Uses native arrays and efficient lookup structures
    /// </summary>
    public class ProvinceDataManager : System.IDisposable
    {
        private const int INITIAL_CAPACITY = 10000;
        private const float LOAD_FACTOR = 0.75f;

        // Core data storage
        private NativeArray<ProvinceData> provinces;
        private NativeHashMap<Color32, ushort> colorToID;
        private NativeHashMap<ushort, int> idToArrayIndex;
        private int provinceCount;

        public int ProvinceCount => provinceCount;
        public bool IsValid => provinces.IsCreated;

        public ProvinceDataManager(int capacity = INITIAL_CAPACITY)
        {
            provinces = new NativeArray<ProvinceData>(capacity, Allocator.Persistent);
            colorToID = new NativeHashMap<Color32, ushort>(capacity, Allocator.Persistent);
            idToArrayIndex = new NativeHashMap<ushort, int>(capacity, Allocator.Persistent);
            provinceCount = 0;

            ArchonLogger.LogMapTextures($"ProvinceDataManager initialized with capacity for {capacity} provinces");
        }

        /// <summary>
        /// Add a new province to the data structure
        /// </summary>
        public bool AddProvince(ushort id, Color32 identifierColor, NativeArray<int2> pixels)
        {
            if (provinceCount >= provinces.Length)
            {
                ArchonLogger.LogMapTexturesError("Province capacity exceeded!");
                return false;
            }

            if (colorToID.ContainsKey(identifierColor))
            {
                ArchonLogger.LogMapTexturesWarning($"Province color {identifierColor} already exists!");
                return false;
            }

            // Calculate province properties
            var (boundsMin, boundsMax) = ProvinceData.CalculateBounds(pixels);
            float2 centerPoint = ProvinceData.CalculateCenterPoint(pixels);

            // Create province data
            var provinceData = new ProvinceData
            {
                id = id,
                ownerCountryID = 0, // No owner initially
                identifierColor = identifierColor,
                displayColor = identifierColor, // Start with identifier color
                centerPoint = centerPoint,
                pixelCount = pixels.Length,
                boundsMin = boundsMin,
                boundsMax = boundsMax,
                flags = ProvinceFlags.None
            };

            // Store in arrays
            int arrayIndex = provinceCount;
            provinces[arrayIndex] = provinceData;

            // Update lookup tables
            colorToID.TryAdd(identifierColor, id);
            idToArrayIndex.TryAdd(id, arrayIndex);

            provinceCount++;
            return true;
        }

        /// <summary>
        /// Get province by ID
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ProvinceData GetProvinceByID(ushort id)
        {
            if (idToArrayIndex.TryGetValue(id, out int index))
            {
                return provinces[index];
            }
            return default;
        }

        /// <summary>
        /// Get province ID by color (fast lookup)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort GetProvinceIDByColor(Color32 color)
        {
            colorToID.TryGetValue(color, out ushort id);
            return id; // Returns 0 if not found
        }

        /// <summary>
        /// Update province owner
        /// </summary>
        public void SetProvinceOwner(ushort provinceID, ushort ownerCountryID)
        {
            if (idToArrayIndex.TryGetValue(provinceID, out int index))
            {
                var province = provinces[index];
                province.ownerCountryID = ownerCountryID;
                provinces[index] = province;
            }
        }

        /// <summary>
        /// Update province display color
        /// </summary>
        public void SetProvinceDisplayColor(ushort provinceID, Color32 newColor)
        {
            if (idToArrayIndex.TryGetValue(provinceID, out int index))
            {
                var province = provinces[index];
                province.displayColor = newColor;
                provinces[index] = province;
            }
        }

        /// <summary>
        /// Set province flags
        /// </summary>
        public void SetProvinceFlags(ushort provinceID, ProvinceFlags flags)
        {
            if (idToArrayIndex.TryGetValue(provinceID, out int index))
            {
                var province = provinces[index];
                province.flags = flags;
                provinces[index] = province;
            }
        }

        /// <summary>
        /// Add flag to province
        /// </summary>
        public void AddProvinceFlag(ushort provinceID, ProvinceFlags flag)
        {
            if (idToArrayIndex.TryGetValue(provinceID, out int index))
            {
                var province = provinces[index];
                province.flags |= flag;
                provinces[index] = province;
            }
        }

        /// <summary>
        /// Remove flag from province
        /// </summary>
        public void RemoveProvinceFlag(ushort provinceID, ProvinceFlags flag)
        {
            if (idToArrayIndex.TryGetValue(provinceID, out int index))
            {
                var province = provinces[index];
                province.flags &= ~flag;
                provinces[index] = province;
            }
        }

        /// <summary>
        /// Get all provinces as read-only array slice
        /// </summary>
        public NativeArray<ProvinceData>.ReadOnly GetAllProvinces()
        {
            return provinces.GetSubArray(0, provinceCount).AsReadOnly();
        }

        /// <summary>
        /// Get provinces owned by specific country
        /// </summary>
        public void GetProvincesByOwner(ushort ownerCountryID, NativeList<ushort> result)
        {
            result.Clear();

            for (int i = 0; i < provinceCount; i++)
            {
                if (provinces[i].ownerCountryID == ownerCountryID)
                {
                    result.Add(provinces[i].id);
                }
            }
        }

        /// <summary>
        /// Clear all province data
        /// </summary>
        public void Clear()
        {
            colorToID.Clear();
            idToArrayIndex.Clear();
            provinceCount = 0;
        }

        /// <summary>
        /// Get memory usage statistics
        /// </summary>
        public (int totalBytes, int provinceBytes, int lookupBytes) GetMemoryUsage()
        {
            int provinceBytes = provinces.Length * UnsafeUtility.SizeOf<ProvinceData>();
            int colorLookupBytes = colorToID.Capacity * (UnsafeUtility.SizeOf<Color32>() + sizeof(ushort));
            int idLookupBytes = idToArrayIndex.Capacity * (sizeof(ushort) + sizeof(int));
            int totalBytes = provinceBytes + colorLookupBytes + idLookupBytes;

            return (totalBytes, provinceBytes, colorLookupBytes + idLookupBytes);
        }

        public void Dispose()
        {
            if (provinces.IsCreated) provinces.Dispose();
            if (colorToID.IsCreated) colorToID.Dispose();
            if (idToArrayIndex.IsCreated) idToArrayIndex.Dispose();
        }

#if UNITY_EDITOR
        [ContextMenu("Log Province Statistics")]
        public void LogStatistics()
        {
            var (totalBytes, provinceBytes, lookupBytes) = GetMemoryUsage();

            ArchonLogger.LogMapTextures($"Province Data Manager Statistics:\n" +
                     $"Provinces: {provinceCount}/{provinces.Length}\n" +
                     $"Memory Usage: {totalBytes / 1024f:F1} KB total\n" +
                     $"  - Province Data: {provinceBytes / 1024f:F1} KB\n" +
                     $"  - Lookup Tables: {lookupBytes / 1024f:F1} KB\n" +
                     $"Average bytes per province: {(provinceCount > 0 ? totalBytes / provinceCount : 0)} bytes");
        }
#endif
    }
}