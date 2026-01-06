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
        /// Priority tier based on distance from player (0 = closest, higher = farther).
        /// Determines processing frequency via AISchedulingConfig.
        /// (byte = 1 byte)
        /// </summary>
        public byte tier;

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
        /// Hour-of-year when this AI was last processed (0-8639).
        /// Used to determine if enough time has passed based on tier interval.
        /// 360 days × 24 hours = 8640 hours per year, wraps around.
        /// (ushort = 2 bytes)
        /// </summary>
        public ushort lastProcessedHour;

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
        /// Tier 255 = unassigned (will be set by AIDistanceCalculator).
        /// </summary>
        public static AIState Create(ushort countryID, bool isActive = true)
        {
            return new AIState
            {
                countryID = countryID,
                tier = 255, // Unassigned, will be calculated
                flags = (byte)(isActive ? 0x01 : 0x00),
                activeGoalID = 0,
                lastProcessedHour = 0
            };
        }

        /// <summary>
        /// Create new AI state with explicit tier.
        /// </summary>
        public static AIState Create(ushort countryID, byte tier, bool isActive = true)
        {
            return new AIState
            {
                countryID = countryID,
                tier = tier,
                flags = (byte)(isActive ? 0x01 : 0x00),
                activeGoalID = 0,
                lastProcessedHour = 0
            };
        }
    }
}
