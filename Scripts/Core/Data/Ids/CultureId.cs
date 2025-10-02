using System;

namespace Core.Data.Ids
{
    /// <summary>
    /// Type-safe culture identifier
    /// Prevents mixing up culture IDs with other entity IDs at compile time
    /// Following data-linking-architecture.md specifications
    /// </summary>
    [Serializable]
    public readonly struct CultureId : IEquatable<CultureId>
    {
        public readonly ushort Value;

        public CultureId(ushort value) => Value = value;

        /// <summary>
        /// Special constant for no culture/unknown
        /// </summary>
        public static readonly CultureId None = new(0);

        /// <summary>
        /// Check if this is a valid culture ID (not none/zero)
        /// </summary>
        public bool IsValid => Value != 0;

        // Equality operations
        public bool Equals(CultureId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CultureId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        // Comparison operations
        public static bool operator ==(CultureId left, CultureId right) => left.Equals(right);
        public static bool operator !=(CultureId left, CultureId right) => !left.Equals(right);

        // Implicit conversions for ease of use
        public static implicit operator ushort(CultureId id) => id.Value;
        public static implicit operator CultureId(ushort value) => new(value);

        // String representation
        public override string ToString() => Value == 0 ? "None" : $"Culture#{Value}";
    }
}