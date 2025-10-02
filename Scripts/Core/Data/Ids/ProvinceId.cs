using System;

namespace Core.Data.Ids
{
    /// <summary>
    /// Type-safe province identifier (runtime ID)
    /// Prevents mixing up province IDs with other entity IDs at compile time
    /// Following data-linking-architecture.md specifications
    /// </summary>
    [Serializable]
    public readonly struct ProvinceId : IEquatable<ProvinceId>
    {
        public readonly ushort Value;

        public ProvinceId(ushort value) => Value = value;

        /// <summary>
        /// Special constant for no province/invalid
        /// </summary>
        public static readonly ProvinceId None = new(0);

        /// <summary>
        /// Check if this is a valid province ID (not none/zero)
        /// </summary>
        public bool IsValid => Value != 0;

        // Equality operations
        public bool Equals(ProvinceId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ProvinceId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        // Comparison operations
        public static bool operator ==(ProvinceId left, ProvinceId right) => left.Equals(right);
        public static bool operator !=(ProvinceId left, ProvinceId right) => !left.Equals(right);

        // Implicit conversions for ease of use
        public static implicit operator ushort(ProvinceId id) => id.Value;
        public static implicit operator ProvinceId(ushort value) => new(value);

        // String representation
        public override string ToString() => Value == 0 ? "None" : $"Province#{Value}";
    }
}