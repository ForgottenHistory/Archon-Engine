using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Core.Data
{
    /// <summary>
    /// ENGINE LAYER - Core province simulation state - EXACTLY 8 bytes
    /// This is the foundation of the dual-layer architecture
    ///
    /// ARCHITECTURE: Engine provides MECHANISM (how ownership works)
    /// Game layer provides POLICY (what game-specific data means)
    ///
    /// This struct contains ONLY generic engine primitives:
    /// - Ownership (who owns this province)
    /// - Control (who controls it militarily)
    /// - Terrain (what type of land)
    /// - Game data slot (index into game-specific data)
    ///
    /// Game-specific fields (development, forts, etc.) belong in Game layer.
    ///
    /// CRITICAL: Never change the size of this struct - it must remain exactly 8 bytes
    /// for performance targets (10,000 provinces × 8 bytes = 80KB total)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ProvinceState
    {
        public ushort ownerID;       // 2 bytes - country that owns this province (0 = unowned)
        public ushort controllerID;  // 2 bytes - country controlling militarily (different during occupation)
        public ushort terrainType;   // 2 bytes - terrain type ID (ocean=0, grassland=1, mountain=2, etc.)
        public ushort gameDataSlot;  // 2 bytes - index into game-specific data array

        // TOTAL: exactly 8 bytes

        // REMOVED (migrated to Game layer - HegemonProvinceData):
        // - public byte development;    → HegemonProvinceSystem.GetDevelopment()
        // - public byte fortLevel;      → HegemonProvinceSystem.GetFortLevel()
        // - public byte flags;          → Moved to separate ProvinceFlags system if needed

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
        public static ProvinceState CreateDefault(ushort terrainType = 1, ushort gameSlot = 0)
        {
            return new ProvinceState
            {
                ownerID = 0,           // Unowned
                controllerID = 0,      // Uncontrolled
                terrainType = terrainType, // Grassland by default
                gameDataSlot = gameSlot    // Default slot (usually provinceID for 1:1 mapping)
            };
        }

        /// <summary>
        /// Create province state with initial owner
        /// </summary>
        public static ProvinceState CreateOwned(ushort owner, ushort terrainType = 1, ushort gameSlot = 0)
        {
            return new ProvinceState
            {
                ownerID = owner,
                controllerID = owner,  // Owner controls initially
                terrainType = terrainType,
                gameDataSlot = gameSlot
            };
        }

        /// <summary>
        /// Create province state for ocean/water
        /// </summary>
        public static ProvinceState CreateOcean(ushort gameSlot = 0)
        {
            return new ProvinceState
            {
                ownerID = 0,           // Unowned
                controllerID = 0,      // Uncontrolled
                terrainType = 0,       // TerrainType.Ocean = 0
                gameDataSlot = gameSlot
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
        /// Check if province is ocean/water (terrain type 0)
        /// </summary>
        public bool IsOcean => terrainType == 0;

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
        /// Calculate hash for state validation/checksums (deterministic for multiplayer)
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
                       terrainType == other.terrainType &&
                       gameDataSlot == other.gameDataSlot;
            }
            return false;
        }

        /// <summary>
        /// Debug string representation
        /// </summary>
        public override string ToString()
        {
            return $"Province[Owner:{ownerID}, Controller:{controllerID}, Terrain:{terrainType}, GameSlot:{gameDataSlot}]";
        }
    }

    /// <summary>
    /// Terrain types for provinces (engine-defined generic types)
    /// Games can define their own terrain interpretations in game layer
    /// </summary>
    public enum TerrainType : ushort
    {
        Ocean = 0,      // Impassable water
        Grassland = 1,  // Default fertile land
        Forest = 2,     // Forested areas
        Hills = 3,      // Hilly terrain
        Mountain = 4,   // Mountainous (difficult)
        Desert = 5,     // Arid regions
        Marsh = 6,      // Swampy areas
        Tundra = 7,     // Cold northern regions

        // Engine supports up to 65,535 terrain types (ushort)
        // Games can define additional types as needed
    }

    // NOTE: ProvinceFlags enum REMOVED
    // Flags were mixing presentation concerns (IsSelected) with simulation
    // If flags are needed, create separate ProvinceFlags system in game layer
    // or use a dedicated flags byte in HegemonProvinceData
}
