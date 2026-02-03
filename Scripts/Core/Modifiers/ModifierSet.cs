using System.Runtime.InteropServices;
using Core.Data;

namespace Core.Modifiers
{
    /// <summary>
    /// ENGINE: Fixed-size modifier storage (cache-friendly, deterministic)
    /// Uses fixed arrays for zero allocations and cache locality
    ///
    /// Performance: O(1) lookup, 8KB per instance (512 types × 2 longs × 8 bytes)
    /// Memory layout: Contiguous arrays for additive and multiplicative values
    ///
    /// DETERMINISM: Stores FixedPoint64.RawValue (long) for cross-platform compatibility
    /// Pattern used by: EU4 (modifier system), CK3 (character modifiers), Stellaris (empire modifiers)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ModifierSet
    {
        public const int MAX_MODIFIER_TYPES = 512;
        private const int BITMASK_LONGS = MAX_MODIFIER_TYPES / 64; // 8 longs = 512 bits

        // Separate arrays for additive and multiplicative modifiers
        // Using fixed-size arrays for cache locality and zero allocations
        // Store as long (FixedPoint64.RawValue) for determinism
        private fixed long additive[MAX_MODIFIER_TYPES];
        private fixed long multiplicative[MAX_MODIFIER_TYPES];

        // Bitmask tracking which modifier types have non-zero values (64 bytes)
        // Used to skip empty slots during RebuildIfDirty parent copy
        private fixed long activeTypeMask[BITMASK_LONGS];

        /// <summary>
        /// Get accumulated modifier value
        /// </summary>
        public ModifierValue Get(ushort modifierTypeId)
        {
            if (modifierTypeId >= MAX_MODIFIER_TYPES)
                return new ModifierValue();

            return new ModifierValue
            {
                Additive = FixedPoint64.FromRaw(additive[modifierTypeId]),
                Multiplicative = FixedPoint64.FromRaw(multiplicative[modifierTypeId])
            };
        }

        /// <summary>
        /// Add a modifier (stacks with existing)
        /// </summary>
        public void Add(ushort modifierTypeId, FixedPoint64 value, bool isMultiplicative)
        {
            if (modifierTypeId >= MAX_MODIFIER_TYPES)
                return;

            if (isMultiplicative)
                multiplicative[modifierTypeId] += value.RawValue;
            else
                additive[modifierTypeId] += value.RawValue;

            // Mark type as active in bitmask
            int wordIndex = modifierTypeId >> 6;  // / 64
            int bitIndex = modifierTypeId & 63;   // % 64
            activeTypeMask[wordIndex] |= (1L << bitIndex);
        }

        /// <summary>
        /// Remove a modifier (for temporary effects)
        /// </summary>
        public void Remove(ushort modifierTypeId, FixedPoint64 value, bool isMultiplicative)
        {
            if (modifierTypeId >= MAX_MODIFIER_TYPES)
                return;

            if (isMultiplicative)
                multiplicative[modifierTypeId] -= value.RawValue;
            else
                additive[modifierTypeId] -= value.RawValue;

            // Note: don't clear bitmask on remove — value may still be non-zero
            // Bitmask is cleared on Clear() and rebuilt from scratch
        }

        /// <summary>
        /// Set modifier to exact value (replaces existing)
        /// </summary>
        public void Set(ushort modifierTypeId, FixedPoint64 value, bool isMultiplicative)
        {
            if (modifierTypeId >= MAX_MODIFIER_TYPES)
                return;

            if (isMultiplicative)
                multiplicative[modifierTypeId] = value.RawValue;
            else
                additive[modifierTypeId] = value.RawValue;

            // Mark type as active in bitmask
            int wordIndex = modifierTypeId >> 6;
            int bitIndex = modifierTypeId & 63;
            activeTypeMask[wordIndex] |= (1L << bitIndex);
        }

        /// <summary>
        /// Clear all modifiers (reset to zero)
        /// Vectorized for performance
        /// </summary>
        public void Clear()
        {
            fixed (long* add = additive, mult = multiplicative, mask = activeTypeMask)
            {
                // Zero out both arrays
                for (int i = 0; i < MAX_MODIFIER_TYPES; i++)
                {
                    add[i] = 0L;
                    mult[i] = 0L;
                }
                // Zero out bitmask
                for (int i = 0; i < BITMASK_LONGS; i++)
                {
                    mask[i] = 0L;
                }
            }
        }

        /// <summary>
        /// Apply modifier to base value
        /// Formula: (base + additive) * (1 + multiplicative)
        /// </summary>
        public FixedPoint64 ApplyModifier(ushort modifierTypeId, FixedPoint64 baseValue)
        {
            var mod = Get(modifierTypeId);
            return mod.Apply(baseValue);
        }

        /// <summary>
        /// Check if any modifier type is active (has non-zero values)
        /// </summary>
        public bool HasActiveTypes
        {
            get
            {
                fixed (long* mask = activeTypeMask)
                {
                    for (int i = 0; i < BITMASK_LONGS; i++)
                    {
                        if (mask[i] != 0L) return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Copy only active (non-zero) modifier values from this set to target set.
        /// Uses bitmask to skip empty modifier types — typically iterates 2-5 types vs 512.
        /// </summary>
        public void CopyActiveToSet(ref ModifierSet target)
        {
            fixed (long* mask = activeTypeMask)
            {
                for (int wordIdx = 0; wordIdx < BITMASK_LONGS; wordIdx++)
                {
                    long word = mask[wordIdx];
                    if (word == 0L) continue; // Skip entire 64-type block

                    while (word != 0)
                    {
                        // Find lowest set bit
                        int bitIdx = BitCount.TrailingZeroCount(word);
                        ushort typeId = (ushort)((wordIdx << 6) | bitIdx);

                        var mod = Get(typeId);
                        if (mod.Additive != FixedPoint64.Zero)
                            target.Add(typeId, mod.Additive, false);
                        if (mod.Multiplicative != FixedPoint64.Zero)
                            target.Add(typeId, mod.Multiplicative, true);

                        // Clear the bit we just processed
                        word &= word - 1;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Bit manipulation helpers (trailing zero count for bitmask iteration)
    /// </summary>
    internal static class BitCount
    {
        /// <summary>
        /// Count trailing zeros in a long (find index of lowest set bit).
        /// Uses de Bruijn sequence for O(1) lookup.
        /// </summary>
        public static int TrailingZeroCount(long value)
        {
            if (value == 0) return 64;

            // Use .NET's built-in if available, otherwise manual
            int count = 0;
            long v = value;
            if ((v & 0xFFFFFFFFL) == 0) { count += 32; v >>= 32; }
            if ((v & 0xFFFFL) == 0) { count += 16; v >>= 16; }
            if ((v & 0xFFL) == 0) { count += 8; v >>= 8; }
            if ((v & 0xFL) == 0) { count += 4; v >>= 4; }
            if ((v & 0x3L) == 0) { count += 2; v >>= 2; }
            if ((v & 0x1L) == 0) { count += 1; }
            return count;
        }
    }
}
