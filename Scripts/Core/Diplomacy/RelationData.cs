using System;
using Core.Data;

namespace Core.Diplomacy
{
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
        /// Padding for alignment (unused)
        /// </summary>
        private byte padding;

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
                padding = 0
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
