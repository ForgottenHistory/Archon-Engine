using System.Collections.Generic;
using Unity.Collections;

namespace GameData.Core
{
    /// <summary>
    /// Area data structure - grouping of provinces for strategic purposes
    /// Used for area borders, strategic regions, and gameplay mechanics
    /// </summary>
    [System.Serializable]
    public struct Area
    {
        public ushort id;               // Numeric area ID
        public string name;             // Area name (champagne_area, etc.)
        public ushort[] provinces;      // Province IDs in this area
        public bool isSeaArea;          // Land area vs sea area
        public byte continent;          // Continent ID this area belongs to
        public ushort region;           // Parent region ID

        /// <summary>
        /// Create a land area
        /// </summary>
        public static Area CreateLandArea(ushort id, string name, ushort[] provinces, ushort region = 0)
        {
            return new Area
            {
                id = id,
                name = name ?? string.Empty,
                provinces = provinces ?? new ushort[0],
                isSeaArea = false,
                continent = 1,  // Default continent
                region = region
            };
        }

        /// <summary>
        /// Create a sea area
        /// </summary>
        public static Area CreateSeaArea(ushort id, string name, ushort[] provinces)
        {
            return new Area
            {
                id = id,
                name = name ?? string.Empty,
                provinces = provinces ?? new ushort[0],
                isSeaArea = true,
                continent = 0,  // Sea areas don't belong to continents
                region = 0      // Sea areas don't belong to regions
            };
        }

        /// <summary>
        /// Check if this area contains a specific province
        /// </summary>
        public bool ContainsProvince(ushort provinceId)
        {
            if (provinces == null) return false;

            for (int i = 0; i < provinces.Length; i++)
            {
                if (provinces[i] == provinceId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Get number of provinces in this area
        /// </summary>
        public int ProvinceCount => provinces?.Length ?? 0;

        /// <summary>
        /// Check if this is a valid area
        /// </summary>
        public bool IsValid => id > 0 && !string.IsNullOrEmpty(name);

        /// <summary>
        /// Get displayable string for debugging
        /// </summary>
        public override string ToString()
        {
            string type = isSeaArea ? "Sea" : "Land";
            return $"{name} ({type} Area) [ID: {id}, Provinces: {ProvinceCount}]";
        }

        /// <summary>
        /// Convert to NativeArray for Burst compilation
        /// </summary>
        public NativeArray<ushort> GetProvincesNative(Allocator allocator)
        {
            if (provinces == null || provinces.Length == 0)
                return new NativeArray<ushort>(0, allocator);

            var native = new NativeArray<ushort>(provinces.Length, allocator);
            for (int i = 0; i < provinces.Length; i++)
            {
                native[i] = provinces[i];
            }
            return native;
        }
    }
}