using Unity.Collections;
using Unity.Mathematics;

namespace Core.AI
{
    /// <summary>
    /// Hot AI state data for a single country (8 bytes, blittable).
    /// Stored in flat NativeArray for cache-friendly access.
    ///
    /// Memory: 300 countries × 8 bytes = 2.4 KB
    ///
    /// Design: Minimal hot data only. Cold data (personality, caches) goes in separate structure.
    /// </summary>
    public struct AIState
    {
        /// <summary>
        /// Country ID this AI controls (ushort = 2 bytes)
        /// </summary>
        public ushort countryID;

        /// <summary>
        /// Which bucket this AI belongs to (0-29 for monthly cycle)
        /// Determines which day of month this AI processes strategic layer.
        /// (byte = 1 byte)
        /// </summary>
        public byte bucket;

        /// <summary>
        /// AI behavior flags (byte = 1 byte)
        /// Bit 0: IsActive (1 = AI enabled, 0 = player-controlled or disabled)
        /// Bit 1-7: Reserved for future use
        /// </summary>
        public byte flags;

        /// <summary>
        /// Current active goal ID (0 = no goal)
        /// Index into AIGoalRegistry.
        /// (ushort = 2 bytes)
        /// </summary>
        public ushort activeGoalID;

        /// <summary>
        /// Reserved for future use (2 bytes to reach 8-byte alignment)
        /// Could be used for: goal priority, last evaluation tick, etc.
        /// </summary>
        public ushort reserved;

        // Total size: 2 + 1 + 1 + 2 + 2 = 8 bytes ✅

        /// <summary>
        /// Is this AI currently active?
        /// </summary>
        public bool IsActive
        {
            get => (flags & 0x01) != 0;
            set
            {
                if (value)
                    flags |= 0x01;
                else
                    flags &= 0xFE;
            }
        }

        /// <summary>
        /// Create new AI state for a country.
        /// </summary>
        public static AIState Create(ushort countryID, byte bucket, bool isActive = true)
        {
            return new AIState
            {
                countryID = countryID,
                bucket = bucket,
                flags = (byte)(isActive ? 0x01 : 0x00),
                activeGoalID = 0,
                reserved = 0
            };
        }
    }
}
