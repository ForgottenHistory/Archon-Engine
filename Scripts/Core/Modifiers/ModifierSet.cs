using System.Runtime.InteropServices;

namespace Core.Modifiers
{
    /// <summary>
    /// ENGINE: Fixed-size modifier storage (cache-friendly, deterministic)
    /// Uses fixed arrays for zero allocations and cache locality
    ///
    /// Performance: O(1) lookup, 4KB per instance (512 types × 2 floats × 4 bytes)
    /// Memory layout: Contiguous arrays for additive and multiplicative values
    ///
    /// Pattern used by: EU4 (modifier system), CK3 (character modifiers), Stellaris (empire modifiers)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ModifierSet
    {
        public const int MAX_MODIFIER_TYPES = 512;

        // Separate arrays for additive and multiplicative modifiers
        // Using fixed-size arrays for cache locality and zero allocations
        private fixed float additive[MAX_MODIFIER_TYPES];
        private fixed float multiplicative[MAX_MODIFIER_TYPES];

        /// <summary>
        /// Get accumulated modifier value
        /// </summary>
        public ModifierValue Get(ushort modifierTypeId)
        {
            if (modifierTypeId >= MAX_MODIFIER_TYPES)
                return new ModifierValue();

            return new ModifierValue
            {
                Additive = additive[modifierTypeId],
                Multiplicative = multiplicative[modifierTypeId]
            };
        }

        /// <summary>
        /// Add a modifier (stacks with existing)
        /// </summary>
        public void Add(ushort modifierTypeId, float value, bool isMultiplicative)
        {
            if (modifierTypeId >= MAX_MODIFIER_TYPES)
                return;

            if (isMultiplicative)
                multiplicative[modifierTypeId] += value;
            else
                additive[modifierTypeId] += value;
        }

        /// <summary>
        /// Remove a modifier (for temporary effects)
        /// </summary>
        public void Remove(ushort modifierTypeId, float value, bool isMultiplicative)
        {
            if (modifierTypeId >= MAX_MODIFIER_TYPES)
                return;

            if (isMultiplicative)
                multiplicative[modifierTypeId] -= value;
            else
                additive[modifierTypeId] -= value;
        }

        /// <summary>
        /// Set modifier to exact value (replaces existing)
        /// </summary>
        public void Set(ushort modifierTypeId, float value, bool isMultiplicative)
        {
            if (modifierTypeId >= MAX_MODIFIER_TYPES)
                return;

            if (isMultiplicative)
                multiplicative[modifierTypeId] = value;
            else
                additive[modifierTypeId] = value;
        }

        /// <summary>
        /// Clear all modifiers (reset to zero)
        /// Vectorized for performance
        /// </summary>
        public void Clear()
        {
            fixed (float* add = additive, mult = multiplicative)
            {
                // Zero out both arrays
                for (int i = 0; i < MAX_MODIFIER_TYPES; i++)
                {
                    add[i] = 0f;
                    mult[i] = 0f;
                }
            }
        }

        /// <summary>
        /// Apply modifier to base value
        /// Formula: (base + additive) * (1 + multiplicative)
        /// </summary>
        public float ApplyModifier(ushort modifierTypeId, float baseValue)
        {
            var mod = Get(modifierTypeId);
            return mod.Apply(baseValue);
        }
    }
}
