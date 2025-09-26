using UnityEngine;
using System.Runtime.InteropServices;

namespace GameData.Core
{
    /// <summary>
    /// Country data structure for Dominion game
    /// Represents a playable nation with its properties
    /// </summary>
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Country
    {
        public ushort id;           // Numeric country ID (1-65535, 0 = unowned)
        public string tag;          // 3-letter country tag (FRA, ENG, etc.)
        public string name;         // Display name (France, England, etc.)
        public Color color;         // Country color for map display
        public ushort capital;      // Capital province ID
        public byte government;     // Government type ID
        public byte technology;     // Technology group ID
        public byte culture;        // Primary culture ID
        public byte religion;       // Primary religion ID

        /// <summary>
        /// Create a country with basic information
        /// </summary>
        public static Country Create(ushort id, string tag, string name, Color color, ushort capital = 0)
        {
            return new Country
            {
                id = id,
                tag = tag ?? string.Empty,
                name = name ?? string.Empty,
                color = color,
                capital = capital,
                government = 1,   // Default government
                technology = 1,   // Default tech group
                culture = 1,      // Default culture
                religion = 1      // Default religion
            };
        }

        /// <summary>
        /// Check if this is a valid country (has ID > 0)
        /// </summary>
        public bool IsValid => id > 0;

        /// <summary>
        /// Check if this country is unowned/neutral (ID = 0)
        /// </summary>
        public bool IsUnowned => id == 0;

        /// <summary>
        /// Get a displayable string for debugging
        /// </summary>
        public override string ToString()
        {
            return $"{tag} ({name}) [ID: {id}]";
        }

        /// <summary>
        /// Unowned/neutral country constant
        /// </summary>
        public static readonly Country Unowned = new Country
        {
            id = 0,
            tag = "---",
            name = "Unowned",
            color = new Color(0.5f, 0.5f, 0.5f, 1.0f), // Gray
            capital = 0
        };
    }
}