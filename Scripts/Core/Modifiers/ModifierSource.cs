using System;
using System.Runtime.InteropServices;
using Core.Data;

namespace Core.Modifiers
{
    /// <summary>
    /// ENGINE: Tracks the source of a modifier for tooltips and removal
    /// Pattern used by: EU4 (modifier tooltips), CK3 (effect stacking), Stellaris (modifier tracking)
    ///
    /// Examples:
    /// - Building: Farm in Province #42 gives +5 production
    /// - Tech: "Advanced Agriculture" gives +20% production (permanent)
    /// - Event: "Harvest Festival" gives +10% production for 12 months (temporary)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ModifierSource
    {
        public enum SourceType : byte
        {
            Building = 0,      // From constructed buildings
            Technology = 1,    // From unlocked tech
            Event = 2,         // From triggered events (usually temporary)
            Government = 3,    // From government type
            Trait = 4,         // From character traits
            Policy = 5,        // From active policies
            Custom = 255       // Custom game-specific sources
        }

        public SourceType Type;          // What kind of source (1 byte)
        public uint SourceID;            // ID of the source (building ID, tech ID, etc.) (4 bytes)
        public ushort ModifierTypeId;    // Which modifier type this applies to (2 bytes)
        public FixedPoint64 Value;       // Modifier value (8 bytes, deterministic)
        public bool IsMultiplicative;    // Additive or multiplicative (1 byte)

        // For temporary modifiers
        public bool IsTemporary;         // Does this modifier expire? (1 byte)
        public int ExpirationTick;       // Game tick when this expires (4 bytes, 0 = permanent)

        // Total size: 21 bytes (will pad to 24 bytes for alignment)

        /// <summary>
        /// Create a permanent modifier source
        /// </summary>
        public static ModifierSource CreatePermanent(
            SourceType type,
            uint sourceId,
            ushort modifierTypeId,
            FixedPoint64 value,
            bool isMultiplicative)
        {
            return new ModifierSource
            {
                Type = type,
                SourceID = sourceId,
                ModifierTypeId = modifierTypeId,
                Value = value,
                IsMultiplicative = isMultiplicative,
                IsTemporary = false,
                ExpirationTick = 0
            };
        }

        /// <summary>
        /// Create a temporary modifier source
        /// </summary>
        public static ModifierSource CreateTemporary(
            SourceType type,
            uint sourceId,
            ushort modifierTypeId,
            FixedPoint64 value,
            bool isMultiplicative,
            int expirationTick)
        {
            return new ModifierSource
            {
                Type = type,
                SourceID = sourceId,
                ModifierTypeId = modifierTypeId,
                Value = value,
                IsMultiplicative = isMultiplicative,
                IsTemporary = true,
                ExpirationTick = expirationTick
            };
        }

        /// <summary>
        /// Check if this modifier has expired
        /// </summary>
        public bool HasExpired(int currentTick)
        {
            return IsTemporary && currentTick >= ExpirationTick;
        }

        public override string ToString()
        {
            string prefix = IsMultiplicative ? "×" : "+";
            string duration = IsTemporary ? $" (expires tick {ExpirationTick})" : " (permanent)";
            return $"{Type}[{SourceID}]: {prefix}{Value}{duration}";
        }
    }
}
