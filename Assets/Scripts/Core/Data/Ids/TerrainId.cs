using System;

namespace Core.Data.Ids
{
    /// <summary>
    /// Type-safe terrain identifier
    /// Prevents mixing up terrain IDs with other entity IDs at compile time
    /// Following data-linking-architecture.md specifications
    /// </summary>
    [Serializable]
    public readonly struct TerrainId : IEquatable<TerrainId>
    {
        public readonly byte Value;

        public TerrainId(byte value) => Value = value;

        /// <summary>
        /// Special constant for ocean terrain (always 0)
        /// </summary>
        public static readonly TerrainId Ocean = new(0);

        /// <summary>
        /// Check if this is a valid land terrain (not ocean)
        /// </summary>
        public bool IsLand => Value != 0;

        // Equality operations
        public bool Equals(TerrainId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TerrainId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        // Comparison operations
        public static bool operator ==(TerrainId left, TerrainId right) => left.Equals(right);
        public static bool operator !=(TerrainId left, TerrainId right) => !left.Equals(right);

        // Implicit conversions for ease of use
        public static implicit operator byte(TerrainId id) => id.Value;
        public static implicit operator TerrainId(byte value) => new(value);

        // String representation
        public override string ToString() => Value == 0 ? "Ocean" : $"Terrain#{Value}";
    }
}