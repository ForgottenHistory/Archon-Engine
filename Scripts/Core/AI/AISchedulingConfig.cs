namespace Core.AI
{
    /// <summary>
    /// Configuration for a single AI priority tier.
    /// Countries are assigned to tiers based on distance from player.
    /// </summary>
    public struct AITierConfig
    {
        /// <summary>
        /// Maximum distance (in province hops) for this tier.
        /// Countries at distance <= maxDistance are assigned to this tier.
        /// Use 255 for "everything else" tier.
        /// </summary>
        public byte maxDistance;

        /// <summary>
        /// How often to process AI in this tier (in game hours).
        /// Lower = more frequent processing.
        /// </summary>
        public ushort intervalHours;

        public AITierConfig(byte maxDistance, ushort intervalHours)
        {
            this.maxDistance = maxDistance;
            this.intervalHours = intervalHours;
        }
    }

    /// <summary>
    /// ENGINE LAYER - Configuration for AI scheduling system.
    ///
    /// Provides mechanism for tier-based AI processing.
    /// GAME layer provides policy (specific tier definitions).
    ///
    /// Design:
    /// - Tiers ordered by distance (closest first)
    /// - Each tier has distance threshold and processing interval
    /// - Countries assigned to first tier where distance <= maxDistance
    ///
    /// Default tiers (can be overridden by GAME):
    /// - Tier 0: Neighbors (distance 0-1), every 1 hour
    /// - Tier 1: Near (distance 2-4), every 6 hours
    /// - Tier 2: Medium (distance 5-8), every 24 hours
    /// - Tier 3: Far (distance 9+), every 72 hours
    /// </summary>
    public class AISchedulingConfig
    {
        public const int MAX_TIERS = 8;
        public const byte MAX_DISTANCE = 255;

        private AITierConfig[] tiers;

        public int TierCount => tiers.Length;

        public AISchedulingConfig(AITierConfig[] tiers)
        {
            this.tiers = tiers;
        }

        /// <summary>
        /// Get tier configuration by index.
        /// </summary>
        public AITierConfig GetTier(int tierIndex)
        {
            if (tierIndex < 0 || tierIndex >= tiers.Length)
                return tiers[tiers.Length - 1]; // Return last tier as fallback

            return tiers[tierIndex];
        }

        /// <summary>
        /// Get tier index for a given distance.
        /// Returns first tier where distance <= maxDistance.
        /// </summary>
        public byte GetTierForDistance(byte distance)
        {
            for (byte i = 0; i < tiers.Length; i++)
            {
                if (distance <= tiers[i].maxDistance)
                    return i;
            }

            // Fallback to last tier
            return (byte)(tiers.Length - 1);
        }

        /// <summary>
        /// Get processing interval (hours) for a tier.
        /// </summary>
        public ushort GetIntervalForTier(byte tier)
        {
            if (tier >= tiers.Length)
                return tiers[tiers.Length - 1].intervalHours;

            return tiers[tier].intervalHours;
        }

        /// <summary>
        /// Create default configuration.
        /// GAME layer can override with custom config.
        /// </summary>
        public static AISchedulingConfig CreateDefault()
        {
            return new AISchedulingConfig(new AITierConfig[]
            {
                new AITierConfig(1, 1),      // Tier 0: Neighbors, every hour
                new AITierConfig(4, 6),      // Tier 1: Near, every 6 hours
                new AITierConfig(8, 24),     // Tier 2: Medium, every day
                new AITierConfig(255, 72)    // Tier 3: Far, every 3 days
            });
        }
    }
}
