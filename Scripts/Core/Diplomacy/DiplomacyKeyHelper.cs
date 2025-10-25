namespace Core.Diplomacy
{
    /// <summary>
    /// ENGINE LAYER - Helper for packing/unpacking country pairs into relationship keys
    ///
    /// PATTERN: Static utility class (no state)
    /// - Ensures consistent key generation (always smaller ID first)
    /// - Prevents duplicate relationships (A→B and B→A use same key)
    ///
    /// KEY FORMAT:
    /// - ulong: 64 bits = (uint32 country1 << 32) | uint32 country2
    /// - country1 always < country2 (normalized order)
    /// - Example: (France=5, Spain=10) → 0x0000000500000010
    /// </summary>
    public static class DiplomacyKeyHelper
    {
        /// <summary>
        /// Pack two country IDs into a single ulong key
        /// Ensures country1 < country2 for consistent ordering
        /// </summary>
        public static ulong GetKey(ushort country1, ushort country2)
        {
            // Normalize order (smaller ID first)
            if (country1 > country2)
            {
                var temp = country1;
                country1 = country2;
                country2 = temp;
            }

            // Pack: (country1 << 32) | country2
            return ((ulong)country1 << 32) | country2;
        }

        /// <summary>
        /// Unpack a relationship key into two country IDs
        /// Returns (country1, country2) where country1 < country2
        /// </summary>
        public static (ushort, ushort) UnpackKey(ulong key)
        {
            ushort country1 = (ushort)(key >> 32);
            ushort country2 = (ushort)(key & 0xFFFFFFFF);
            return (country1, country2);
        }
    }
}
