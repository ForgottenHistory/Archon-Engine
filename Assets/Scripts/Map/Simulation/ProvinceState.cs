using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Map.Simulation
{
    /// <summary>
    /// Core province simulation state - EXACTLY 8 bytes
    /// This is the foundation of the dual-layer architecture
    ///
    /// CRITICAL: Never change the size of this struct - it must remain exactly 8 bytes
    /// for performance targets (10,000 provinces × 8 bytes = 80KB total)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ProvinceState
    {
        public ushort ownerID;      // 2 bytes - country that owns this province (0 = unowned)
        public ushort controllerID; // 2 bytes - country that controls it (different during occupation)
        public byte development;    // 1 byte - development level 0-255
        public byte terrain;        // 1 byte - terrain type (ocean=0, grassland=1, mountain=2, etc.)
        public byte fortLevel;      // 1 byte - fortification level 0-255
        public byte flags;          // 1 byte - packed boolean flags (see ProvinceFlags)

        // TOTAL: exactly 8 bytes

        /// <summary>
        /// Validate that the struct is exactly 8 bytes at compile time
        /// </summary>
        static ProvinceState()
        {
            int actualSize = UnsafeUtility.SizeOf<ProvinceState>();
            if (actualSize != 8)
            {
                throw new System.InvalidOperationException(
                    $"ProvinceState must be exactly 8 bytes, but is {actualSize} bytes. " +
                    "This violates the dual-layer architecture requirements.");
            }
        }

        /// <summary>
        /// Create a default province state (unowned, undeveloped)
        /// </summary>
        public static ProvinceState CreateDefault(byte terrainType = 1)
        {
            return new ProvinceState
            {
                ownerID = 0,           // Unowned
                controllerID = 0,      // Uncontrolled
                development = (terrainType == (byte)TerrainType.Ocean) ? (byte)0 : (byte)1, // Ocean has no development
                terrain = terrainType, // Grassland by default
                fortLevel = 0,         // No fortifications
                flags = 0              // No flags set
            };
        }

        /// <summary>
        /// Create province state with initial owner
        /// </summary>
        public static ProvinceState CreateOwned(ushort owner, byte terrainType = 1, byte initialDevelopment = 10)
        {
            return new ProvinceState
            {
                ownerID = owner,
                controllerID = owner,  // Owner controls initially
                development = initialDevelopment,
                terrain = terrainType,
                fortLevel = 0,
                flags = 0
            };
        }

        /// <summary>
        /// Check if province is owned by anyone
        /// </summary>
        public bool IsOwned => ownerID != 0;

        /// <summary>
        /// Check if province is controlled by someone different than owner (occupied)
        /// </summary>
        public bool IsOccupied => controllerID != ownerID && ownerID != 0;

        /// <summary>
        /// Check if province has specific flag
        /// </summary>
        public bool HasFlag(ProvinceFlags flag) => (flags & (byte)flag) != 0;

        /// <summary>
        /// Set a specific flag
        /// </summary>
        public void SetFlag(ProvinceFlags flag) => flags |= (byte)flag;

        /// <summary>
        /// Clear a specific flag
        /// </summary>
        public void ClearFlag(ProvinceFlags flag) => flags &= (byte)~flag;

        /// <summary>
        /// Get flag value as boolean
        /// </summary>
        public bool GetFlag(ProvinceFlags flag) => HasFlag(flag);

        /// <summary>
        /// Serialize to bytes for networking (8 bytes exactly)
        /// </summary>
        public unsafe byte[] ToBytes()
        {
            byte[] bytes = new byte[8];
            fixed (byte* ptr = bytes)
            {
                *(ProvinceState*)ptr = this;
            }
            return bytes;
        }

        /// <summary>
        /// Deserialize from bytes for networking
        /// </summary>
        public static unsafe ProvinceState FromBytes(byte[] bytes)
        {
            if (bytes == null)
                throw new System.ArgumentException("Byte array cannot be null");

            if (bytes.Length != 8)
                throw new System.ArgumentException($"ProvinceState requires exactly 8 bytes, got {bytes.Length}");

            fixed (byte* ptr = bytes)
            {
                return *(ProvinceState*)ptr;
            }
        }

        /// <summary>
        /// Calculate hash for state validation/checksums
        /// </summary>
        public override int GetHashCode()
        {
            // Fast hash using all 8 bytes
            unsafe
            {
                fixed (ProvinceState* ptr = &this)
                {
                    byte* bytes = (byte*)ptr;
                    int hash = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        hash = hash * 31 + bytes[i];
                    }
                    return hash;
                }
            }
        }

        /// <summary>
        /// Equality comparison for deterministic validation
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj is ProvinceState other)
            {
                return ownerID == other.ownerID &&
                       controllerID == other.controllerID &&
                       development == other.development &&
                       terrain == other.terrain &&
                       fortLevel == other.fortLevel &&
                       flags == other.flags;
            }
            return false;
        }

        /// <summary>
        /// Debug string representation
        /// </summary>
        public override string ToString()
        {
            return $"Province[Owner:{ownerID}, Controller:{controllerID}, Dev:{development}, " +
                   $"Terrain:{terrain}, Fort:{fortLevel}, Flags:0x{flags:X2}]";
        }
    }

    /// <summary>
    /// Province flags packed into a single byte (8 flags maximum)
    /// </summary>
    [System.Flags]
    public enum ProvinceFlags : byte
    {
        None = 0,
        IsCoastal = 1 << 0,          // Touches water/ocean
        IsCapital = 1 << 1,          // Country capital
        HasReligiousCenter = 1 << 2, // Important religious site
        IsTradeCenter = 1 << 3,      // Major trade hub
        IsBorderProvince = 1 << 4,   // Touches foreign territory
        UnderSiege = 1 << 5,         // Currently being sieged
        HasSpecialBuilding = 1 << 6, // Has wonder/special building
        IsSelected = 1 << 7          // Currently selected (presentation layer)
    }

    /// <summary>
    /// Terrain types for provinces
    /// </summary>
    public enum TerrainType : byte
    {
        Ocean = 0,      // Impassable water
        Grassland = 1,  // Default fertile land
        Forest = 2,     // Forested areas
        Hills = 3,      // Hilly terrain
        Mountain = 4,   // Mountainous (difficult)
        Desert = 5,     // Arid regions
        Marsh = 6,      // Swampy areas
        Tundra = 7      // Cold northern regions
    }
}