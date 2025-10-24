using System;
using Core.Data;

namespace Core.Diplomacy
{
    /// <summary>
    /// Treaty type bitfield flags
    /// Used in RelationData.treatyFlags to track active treaties
    /// </summary>
    [Flags]
    public enum TreatyFlags : byte
    {
        None = 0,
        Alliance = 1 << 0,                  // Bit 0: Defensive alliance (bidirectional)
        NonAggressionPact = 1 << 1,         // Bit 1: NAP (bidirectional)
        GuaranteeFrom1To2 = 1 << 2,         // Bit 2: country1 guarantees country2
        GuaranteeFrom2To1 = 1 << 3,         // Bit 3: country2 guarantees country1
        MilitaryAccessFrom1To2 = 1 << 4,    // Bit 4: country1 grants access to country2
        MilitaryAccessFrom2To1 = 1 << 5,    // Bit 5: country2 grants access to country1
        // Bits 6-7 reserved for future treaties
    }

    /// <summary>
    /// ENGINE LAYER - Hot data for diplomatic relations between two countries
    ///
    /// Architecture:
    /// - Fixed-size struct for cache efficiency
    /// - Only stores essential hot data (opinion, war state)
    /// - Cold data (modifiers, history) stored separately
    /// - Stored in sparse Dictionary (only active relationships)
    ///
    /// Memory: 16 bytes per relationship
    ///
    /// Storage Pattern:
    /// Dictionary<(ushort, ushort), RelationData> relations;
    /// - Key: (country1, country2) sorted pair
    /// - Value: this struct
    /// - Only store relationships that exist (sparse)
    ///
    /// Example:
    /// 1000 countries × 30% interaction = ~30k relationships × 16 bytes = ~480KB
    /// </summary>
    [Serializable]
    public struct RelationData
    {
        /// <summary>
        /// First country in the relationship (lower ID)
        /// </summary>
        public ushort country1;

        /// <summary>
        /// Second country in the relationship (higher ID)
        /// </summary>
        public ushort country2;

        /// <summary>
        /// Base opinion value before modifiers (-200 to +200)
        /// Cultural/religious similarity, historical relations, etc.
        /// </summary>
        public FixedPoint64 baseOpinion;

        /// <summary>
        /// Are these countries currently at war?
        /// </summary>
        public bool atWar;

        /// <summary>
        /// Treaty flags (bitfield for 8 treaty types)
        /// Phase 2: Alliance, NAP, Guarantee×2, MilitaryAccess×2
        /// </summary>
        public byte treatyFlags;

        /// <summary>
        /// Create relationship with base opinion
        /// </summary>
        public static RelationData Create(ushort countryA, ushort countryB, FixedPoint64 baseOpinion)
        {
            // Ensure consistent ordering (smaller ID first)
            if (countryA > countryB)
            {
                var temp = countryA;
                countryA = countryB;
                countryB = temp;
            }

            return new RelationData
            {
                country1 = countryA,
                country2 = countryB,
                baseOpinion = baseOpinion,
                atWar = false,
                treatyFlags = (byte)TreatyFlags.None
            };
        }

        /// <summary>
        /// Get the other country in this relationship
        /// </summary>
        public ushort GetOtherCountry(ushort countryID)
        {
            if (countryID == country1) return country2;
            if (countryID == country2) return country1;
            throw new ArgumentException($"Country {countryID} not in this relationship");
        }

        /// <summary>
        /// Check if this relationship involves the given country
        /// </summary>
        public bool InvolvesCountry(ushort countryID)
        {
            return country1 == countryID || country2 == countryID;
        }
    }
}
