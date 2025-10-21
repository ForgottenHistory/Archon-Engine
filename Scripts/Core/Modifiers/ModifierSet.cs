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

        // Separate arrays for additive and multiplicative modifiers
        // Using fixed-size arrays for cache locality and zero allocations
        // Store as long (FixedPoint64.RawValue) for determinism
        private fixed long additive[MAX_MODIFIER_TYPES];
        private fixed long multiplicative[MAX_MODIFIER_TYPES];

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
        }

        /// <summary>
        /// Clear all modifiers (reset to zero)
        /// Vectorized for performance
        /// </summary>
        public void Clear()
        {
            fixed (long* add = additive, mult = multiplicative)
            {
                // Zero out both arrays
                for (int i = 0; i < MAX_MODIFIER_TYPES; i++)
                {
                    add[i] = 0L;
                    mult[i] = 0L;
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
    }
}
