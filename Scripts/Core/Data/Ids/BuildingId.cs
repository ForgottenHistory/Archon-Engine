using System;

namespace Core.Data.Ids
{
    /// <summary>
    /// Type-safe building identifier
    /// Prevents mixing up building IDs with other entity IDs at compile time
    /// Following data-linking-architecture.md specifications
    /// </summary>
    [Serializable]
    public readonly struct BuildingId : IEquatable<BuildingId>
    {
        public readonly ushort Value;

        public BuildingId(ushort value) => Value = value;

        /// <summary>
        /// Special constant for no building/empty slot
        /// </summary>
        public static readonly BuildingId None = new(0);

        /// <summary>
        /// Check if this is a valid building ID (not none/zero)
        /// </summary>
        public bool IsValid => Value != 0;

        // Equality operations
        public bool Equals(BuildingId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is BuildingId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        // Comparison operations
        public static bool operator ==(BuildingId left, BuildingId right) => left.Equals(right);
        public static bool operator !=(BuildingId left, BuildingId right) => !left.Equals(right);

        // Implicit conversions for ease of use
        public static implicit operator ushort(BuildingId id) => id.Value;
        public static implicit operator BuildingId(ushort value) => new(value);

        // String representation
        public override string ToString() => Value == 0 ? "None" : $"Building#{Value}";
    }
}