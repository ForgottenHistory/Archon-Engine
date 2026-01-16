using System;
using System.Runtime.InteropServices;

namespace Core.Units
{
    /// <summary>
    /// 8-byte hot data for a single military unit.
    ///
    /// DESIGN:
    /// - Fixed size (8 bytes) for cache efficiency and network transmission
    /// - No visual data (positions, sprites) - presentation layer responsibility
    /// - provinceID instead of coordinates - simulation layer doesn't know positions
    /// - RISK-style: Simple unit count instead of percentage-based strength/morale
    ///
    /// MULTIPLAYER:
    /// - Deterministic layout (explicit struct layout)
    /// - No managed references (NativeArray compatible)
    /// - Serializable for network sync
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnitState : IEquatable<UnitState>
    {
        // === Location & Ownership (6 bytes) ===

        /// <summary>Current province location (0-65535 provinces)</summary>
        public ushort provinceID;

        /// <summary>Owning country (0-65535 countries)</summary>
        public ushort countryID;

        /// <summary>Unit type ID (infantry, cavalry, artillery, etc.)</summary>
        public ushort unitTypeID;

        // === Combat Stats (2 bytes) ===

        /// <summary>Number of troops in this unit</summary>
        public ushort unitCount;

        // === Factory Methods ===

        /// <summary>Create a new unit with specified troop count</summary>
        public static UnitState Create(ushort provinceID, ushort countryID, ushort unitTypeID, ushort unitCount = 1)
        {
            return new UnitState
            {
                provinceID = provinceID,
                countryID = countryID,
                unitTypeID = unitTypeID,
                unitCount = unitCount
            };
        }

        /// <summary>Create a unit with custom stats (for loading saves, reinforcements, etc.)</summary>
        public static UnitState CreateWithStats(ushort provinceID, ushort countryID, ushort unitTypeID, ushort unitCount)
        {
            return new UnitState
            {
                provinceID = provinceID,
                countryID = countryID,
                unitTypeID = unitTypeID,
                unitCount = unitCount
            };
        }

        // === Queries ===

        /// <summary>Is this unit destroyed (unitCount = 0)?</summary>
        public bool IsDestroyed => unitCount == 0;

        /// <summary>Does this unit have troops?</summary>
        public bool HasTroops => unitCount > 0;

        // === Serialization (for network/save) ===

        /// <summary>Serialize to 8 bytes for network transmission or save files</summary>
        public byte[] ToBytes()
        {
            byte[] bytes = new byte[8];
            BitConverter.GetBytes(provinceID).CopyTo(bytes, 0);
            BitConverter.GetBytes(countryID).CopyTo(bytes, 2);
            BitConverter.GetBytes(unitTypeID).CopyTo(bytes, 4);
            BitConverter.GetBytes(unitCount).CopyTo(bytes, 6);
            return bytes;
        }

        /// <summary>Deserialize from 8 bytes</summary>
        public static UnitState FromBytes(byte[] bytes)
        {
            if (bytes.Length != 8)
                throw new ArgumentException($"Expected 8 bytes, got {bytes.Length}");

            return new UnitState
            {
                provinceID = BitConverter.ToUInt16(bytes, 0),
                countryID = BitConverter.ToUInt16(bytes, 2),
                unitTypeID = BitConverter.ToUInt16(bytes, 4),
                unitCount = BitConverter.ToUInt16(bytes, 6)
            };
        }

        // === Equality ===

        public bool Equals(UnitState other)
        {
            return provinceID == other.provinceID &&
                   countryID == other.countryID &&
                   unitTypeID == other.unitTypeID &&
                   unitCount == other.unitCount;
        }

        public override bool Equals(object obj) => obj is UnitState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = provinceID.GetHashCode();
                hash = (hash * 397) ^ countryID.GetHashCode();
                hash = (hash * 397) ^ unitTypeID.GetHashCode();
                hash = (hash * 397) ^ unitCount.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(UnitState left, UnitState right) => left.Equals(right);
        public static bool operator !=(UnitState left, UnitState right) => !left.Equals(right);

        public override string ToString()
        {
            return $"Unit(Province={provinceID}, Country={countryID}, Type={unitTypeID}, Count={unitCount})";
        }
    }
}
