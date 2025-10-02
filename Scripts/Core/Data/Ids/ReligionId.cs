using System;

namespace Core.Data.Ids
{
    /// <summary>
    /// Type-safe religion identifier
    /// Prevents mixing up religion IDs with other entity IDs at compile time
    /// Following data-linking-architecture.md specifications
    /// </summary>
    [Serializable]
    public readonly struct ReligionId : IEquatable<ReligionId>
    {
        public readonly ushort Value;

        public ReligionId(ushort value) => Value = value;

        /// <summary>
        /// Special constant for no religion/unreligious
        /// </summary>
        public static readonly ReligionId None = new(0);

        /// <summary>
        /// Check if this is a valid religion ID (not none/zero)
        /// </summary>
        public bool IsValid => Value != 0;

        // Equality operations
        public bool Equals(ReligionId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ReligionId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        // Comparison operations
        public static bool operator ==(ReligionId left, ReligionId right) => left.Equals(right);
        public static bool operator !=(ReligionId left, ReligionId right) => !left.Equals(right);

        // Implicit conversions for ease of use
        public static implicit operator ushort(ReligionId id) => id.Value;
        public static implicit operator ReligionId(ushort value) => new(value);

        // String representation
        public override string ToString() => Value == 0 ? "None" : $"Religion#{Value}";
    }
}