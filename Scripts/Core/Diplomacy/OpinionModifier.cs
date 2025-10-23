using System;
using Core.Data;

namespace Core.Diplomacy
{
    /// <summary>
    /// ENGINE LAYER - Individual opinion modifier affecting relations between countries
    ///
    /// Architecture:
    /// - Fixed-size struct for deterministic serialization
    /// - Decays linearly over time (value * (1 - elapsed/decayRate))
    /// - Fully decayed modifiers removed automatically
    ///
    /// Memory: ~20 bytes per modifier (aligned)
    ///
    /// Example Usage:
    /// var modifier = new OpinionModifier {
    ///     modifierTypeID = OpinionModifierTypes.DeclaredWar,
    ///     value = FixedPoint64.FromInt(-50),
    ///     appliedTick = currentTick,
    ///     decayRate = 3600  // 10 years (360 ticks/year)
    /// };
    ///
    /// // Calculate current value after 5 years:
    /// FixedPoint64 current = modifier.CalculateCurrentValue(currentTick);
    /// // Returns -25 (50% decay after half the decay period)
    /// </summary>
    [Serializable]
    public struct OpinionModifier
    {
        /// <summary>
        /// Type of modifier (game-specific, e.g., DeclaredWar, StoleProvince)
        /// Maps to OpinionModifierTypes enum in GAME layer
        /// </summary>
        public ushort modifierTypeID;

        /// <summary>
        /// Base opinion value change (-200 to +200)
        /// Negative = worsens opinion, Positive = improves opinion
        /// </summary>
        public FixedPoint64 value;

        /// <summary>
        /// Tick when this modifier was applied
        /// Used to calculate decay over time
        /// </summary>
        public int appliedTick;

        /// <summary>
        /// Ticks until full decay (0 = permanent modifier)
        /// Example: 3600 ticks = 10 years (360 days/year)
        /// </summary>
        public int decayRate;

        /// <summary>
        /// Calculate current value with decay applied
        /// Linear decay: value * (1 - timeElapsed / decayRate)
        /// </summary>
        public FixedPoint64 CalculateCurrentValue(int currentTick)
        {
            // Permanent modifier (no decay)
            if (decayRate == 0)
                return value;

            int elapsed = currentTick - appliedTick;

            // Fully decayed
            if (elapsed >= decayRate)
                return FixedPoint64.Zero;

            // Linear decay
            FixedPoint64 decayFactor = FixedPoint64.One -
                (FixedPoint64.FromInt(elapsed) / FixedPoint64.FromInt(decayRate));

            return value * decayFactor;
        }

        /// <summary>
        /// Check if this modifier is fully decayed
        /// </summary>
        public bool IsFullyDecayed(int currentTick)
        {
            if (decayRate == 0) return false;  // Permanent
            return (currentTick - appliedTick) >= decayRate;
        }
    }
}
