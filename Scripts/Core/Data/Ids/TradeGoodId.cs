using System;

namespace Core.Data.Ids
{
    /// <summary>
    /// Type-safe trade good identifier
    /// Prevents mixing up trade good IDs with other entity IDs at compile time
    /// Following data-linking-architecture.md specifications
    /// </summary>
    [Serializable]
    public readonly struct TradeGoodId : IEquatable<TradeGoodId>
    {
        public readonly ushort Value;

        public TradeGoodId(ushort value) => Value = value;

        /// <summary>
        /// Special constant for no trade good/unknown
        /// </summary>
        public static readonly TradeGoodId None = new(0);

        /// <summary>
        /// Check if this is a valid trade good ID (not none/zero)
        /// </summary>
        public bool IsValid => Value != 0;

        // Equality operations
        public bool Equals(TradeGoodId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TradeGoodId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        // Comparison operations
        public static bool operator ==(TradeGoodId left, TradeGoodId right) => left.Equals(right);
        public static bool operator !=(TradeGoodId left, TradeGoodId right) => !left.Equals(right);

        // Implicit conversions for ease of use
        public static implicit operator ushort(TradeGoodId id) => id.Value;
        public static implicit operator TradeGoodId(ushort value) => new(value);

        // String representation
        public override string ToString() => Value == 0 ? "None" : $"TradeGood#{Value}";
    }
}