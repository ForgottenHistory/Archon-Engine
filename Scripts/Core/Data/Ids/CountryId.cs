using System;

namespace Core.Data.Ids
{
    /// <summary>
    /// Type-safe country identifier
    /// Prevents mixing up country IDs with other entity IDs at compile time
    /// Following data-linking-architecture.md specifications
    /// </summary>
    [Serializable]
    public readonly struct CountryId : IEquatable<CountryId>
    {
        public readonly ushort Value;

        public CountryId(ushort value) => Value = value;

        /// <summary>
        /// Special constant for no country/unowned
        /// </summary>
        public static readonly CountryId None = new(0);

        /// <summary>
        /// Check if this is a valid country ID (not none/zero)
        /// </summary>
        public bool IsValid => Value != 0;

        // Equality operations
        public bool Equals(CountryId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CountryId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        // Comparison operations
        public static bool operator ==(CountryId left, CountryId right) => left.Equals(right);
        public static bool operator !=(CountryId left, CountryId right) => !left.Equals(right);

        // Implicit conversions for ease of use
        public static implicit operator ushort(CountryId id) => id.Value;
        public static implicit operator CountryId(ushort value) => new(value);

        // String representation
        public override string ToString() => Value == 0 ? "None" : $"Country#{Value}";
    }
}