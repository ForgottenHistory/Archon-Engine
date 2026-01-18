using Unity.Collections;
using Core.Data;

namespace Core.Diplomacy
{
    /// <summary>
    /// ENGINE LAYER - Cold data for diplomatic relations using NativeCollections
    ///
    /// Architecture:
    /// - Struct (not class) for Burst compatibility
    /// - NativeList for modifiers (dynamic growth, zero GC)
    /// - Stored in NativeList{DiplomacyColdDataNative} in DiplomacySystem
    ///
    /// Memory: ~24 bytes + modifiers
    /// </summary>
    public struct DiplomacyColdDataNative
    {
        /// <summary>
        /// Active opinion modifiers affecting this relationship
        /// NativeList for efficient iteration and zero allocations
        /// </summary>
        public NativeList<OpinionModifier> modifiers;

        /// <summary>
        /// Last tick when this relationship changed
        /// Used for decay calculations and debugging
        /// </summary>
        public int lastInteractionTick;

        /// <summary>
        /// Create new cold data with allocated modifier list
        /// </summary>
        public static DiplomacyColdDataNative Create(Allocator allocator)
        {
            return new DiplomacyColdDataNative
            {
                modifiers = new NativeList<OpinionModifier>(4, allocator),  // Start with 4 slots
                lastInteractionTick = 0
            };
        }

        /// <summary>
        /// Add a modifier to this relationship
        /// </summary>
        public void AddModifier(OpinionModifier modifier)
        {
            modifiers.Add(modifier);
        }

        /// <summary>
        /// Remove all fully decayed modifiers
        /// Returns number of modifiers removed
        /// </summary>
        public int RemoveDecayedModifiers(int currentTick)
        {
            int removed = 0;
            for (int i = modifiers.Length - 1; i >= 0; i--)
            {
                if (modifiers[i].IsFullyDecayed(currentTick))
                {
                    modifiers.RemoveAtSwapBack(i);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>
        /// Calculate total opinion from all active modifiers
        /// </summary>
        public FixedPoint64 CalculateModifierTotal(int currentTick)
        {
            FixedPoint64 total = FixedPoint64.Zero;

            for (int i = 0; i < modifiers.Length; i++)
            {
                total += modifiers[i].CalculateCurrentValue(currentTick);
            }

            return total;
        }

        /// <summary>
        /// Check if this cold data has any active modifiers
        /// Used to determine if cold data can be removed
        /// </summary>
        public bool HasActiveModifiers(int currentTick)
        {
            for (int i = 0; i < modifiers.Length; i++)
            {
                if (!modifiers[i].IsFullyDecayed(currentTick))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Dispose of NativeList to prevent memory leaks
        /// </summary>
        public void Dispose()
        {
            if (modifiers.IsCreated)
                modifiers.Dispose();
        }
    }
}
