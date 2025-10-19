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
    /// - Percentage strength/morale (0-100) - sufficient granularity, saves space
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

        /// <summary>Current strength (0-100%). 100 = full strength, 0 = destroyed</summary>
        public byte strength;

        /// <summary>Current morale (0-100%). Affects combat, breaks cause retreat</summary>
        public byte morale;

        // === Factory Methods ===

        /// <summary>Create a new unit at full strength and morale</summary>
        public static UnitState Create(ushort provinceID, ushort countryID, ushort unitTypeID)
        {
            return new UnitState
            {
                provinceID = provinceID,
                countryID = countryID,
                unitTypeID = unitTypeID,
                strength = 100,
                morale = 100
            };
        }

        /// <summary>Create a unit with custom strength/morale (for loading saves, reinforcements, etc.)</summary>
        public static UnitState CreateWithStats(ushort provinceID, ushort countryID, ushort unitTypeID, byte strength, byte morale)
        {
            return new UnitState
            {
                provinceID = provinceID,
                countryID = countryID,
                unitTypeID = unitTypeID,
                strength = strength,
                morale = morale
            };
        }

        // === Queries ===

        /// <summary>Is this unit destroyed (strength = 0)?</summary>
        public bool IsDestroyed => strength == 0;

        /// <summary>Is this unit at full strength?</summary>
        public bool IsFullStrength => strength == 100;

        /// <summary>Is morale broken (ready to retreat)?</summary>
        public bool IsMoraleBroken => morale < 20;  // Configurable threshold

        // === Serialization (for network/save) ===

        /// <summary>Serialize to 8 bytes for network transmission or save files</summary>
        public byte[] ToBytes()
        {
            byte[] bytes = new byte[8];
            BitConverter.GetBytes(provinceID).CopyTo(bytes, 0);
            BitConverter.GetBytes(countryID).CopyTo(bytes, 2);
            BitConverter.GetBytes(unitTypeID).CopyTo(bytes, 4);
            bytes[6] = strength;
            bytes[7] = morale;
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
                strength = bytes[6],
                morale = bytes[7]
            };
        }

        // === Equality ===

        public bool Equals(UnitState other)
        {
            return provinceID == other.provinceID &&
                   countryID == other.countryID &&
                   unitTypeID == other.unitTypeID &&
                   strength == other.strength &&
                   morale == other.morale;
        }

        public override bool Equals(object obj) => obj is UnitState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = provinceID.GetHashCode();
                hash = (hash * 397) ^ countryID.GetHashCode();
                hash = (hash * 397) ^ unitTypeID.GetHashCode();
                hash = (hash * 397) ^ strength.GetHashCode();
                hash = (hash * 397) ^ morale.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(UnitState left, UnitState right) => left.Equals(right);
        public static bool operator !=(UnitState left, UnitState right) => !left.Equals(right);

        public override string ToString()
        {
            return $"Unit(Province={provinceID}, Country={countryID}, Type={unitTypeID}, Str={strength}%, Mor={morale}%)";
        }
    }
}
