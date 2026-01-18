using System.Collections.Generic;
using Core.Data;

namespace Core.Diplomacy
{
    /// <summary>
    /// ENGINE LAYER - Cold data for diplomatic relations (rarely accessed)
    ///
    /// Architecture:
    /// - Separate from hot RelationData struct
    /// - Only loaded when needed (UI, calculations, save/load)
    /// - Modifiers and history stored here
    /// - Managed heap (Dictionary) storage acceptable for cold data
    ///
    /// Memory: Variable (~200 bytes + modifiers)
    ///
    /// Storage Pattern:
    /// Dictionary{(ushort, ushort), DiplomacyColdData} coldData;
    /// - Same key as RelationData
    /// - Only created when modifiers exist
    /// </summary>
    public class DiplomacyColdData
    {
        /// <summary>
        /// Active opinion modifiers affecting this relationship
        /// List for efficient iteration during decay
        /// </summary>
        public List<OpinionModifier> modifiers;

        /// <summary>
        /// Last tick when this relationship changed
        /// Used for decay calculations and debugging
        /// </summary>
        public int lastInteractionTick;

        /// <summary>
        /// Game-specific extension data
        /// Allows GAME layer to attach additional data without modifying ENGINE
        /// Example: treaty information, trade agreement details, etc.
        /// </summary>
        public Dictionary<string, object> customData;

        public DiplomacyColdData()
        {
            modifiers = new List<OpinionModifier>();
            lastInteractionTick = 0;
            customData = new Dictionary<string, object>();
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
            int removed = modifiers.RemoveAll(m => m.IsFullyDecayed(currentTick));
            return removed;
        }

        /// <summary>
        /// Calculate total opinion from all active modifiers
        /// </summary>
        public FixedPoint64 CalculateModifierTotal(int currentTick)
        {
            FixedPoint64 total = FixedPoint64.Zero;

            foreach (var modifier in modifiers)
            {
                total += modifier.CalculateCurrentValue(currentTick);
            }

            return total;
        }

        /// <summary>
        /// Check if this cold data has any active modifiers
        /// Used to determine if cold data can be removed
        /// </summary>
        public bool HasActiveModifiers(int currentTick)
        {
            foreach (var modifier in modifiers)
            {
                if (!modifier.IsFullyDecayed(currentTick))
                    return true;
            }
            return false;
        }
    }
}
