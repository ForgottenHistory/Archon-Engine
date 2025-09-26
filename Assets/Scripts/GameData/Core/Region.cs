namespace GameData.Core
{
    /// <summary>
    /// Region data structure - grouping of areas for larger strategic purposes
    /// Used for region borders, trade regions, and administrative divisions
    /// </summary>
    [System.Serializable]
    public struct Region
    {
        public ushort id;               // Numeric region ID
        public string name;             // Region name (france_region, etc.)
        public ushort[] areas;          // Area IDs in this region
        public ushort superregion;      // Parent superregion ID
        public byte continent;          // Continent this region belongs to

        /// <summary>
        /// Create a region
        /// </summary>
        public static Region Create(ushort id, string name, ushort[] areas, ushort superregion = 0, byte continent = 1)
        {
            return new Region
            {
                id = id,
                name = name ?? string.Empty,
                areas = areas ?? new ushort[0],
                superregion = superregion,
                continent = continent
            };
        }

        /// <summary>
        /// Check if this region contains a specific area
        /// </summary>
        public bool ContainsArea(ushort areaId)
        {
            if (areas == null) return false;

            for (int i = 0; i < areas.Length; i++)
            {
                if (areas[i] == areaId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Get number of areas in this region
        /// </summary>
        public int AreaCount => areas?.Length ?? 0;

        /// <summary>
        /// Check if this is a valid region
        /// </summary>
        public bool IsValid => id > 0 && !string.IsNullOrEmpty(name);

        /// <summary>
        /// Get displayable string for debugging
        /// </summary>
        public override string ToString()
        {
            return $"{name} (Region) [ID: {id}, Areas: {AreaCount}]";
        }
    }
}