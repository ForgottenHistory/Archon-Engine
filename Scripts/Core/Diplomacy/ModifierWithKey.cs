namespace Core.Diplomacy
{
    /// <summary>
    /// Opinion modifier tagged with its relationship key
    ///
    /// ARCHITECTURE:
    /// - Enables flat storage without range tracking
    /// - allModifiers = NativeList{ModifierWithKey} (all modifiers from all relationships)
    /// - GetOpinion filters by relationshipKey
    /// - Burst job processes entire array in parallel
    ///
    /// DETERMINISM:
    /// - Insertion order preserved (append-only)
    /// - Decay marks modifiers for removal (parallel read-only)
    /// - Compaction rebuilds array sequentially (deterministic)
    ///
    /// Memory: ~32 bytes per modifier (8 bytes key + 24 bytes modifier)
    /// </summary>
    public struct ModifierWithKey
    {
        /// <summary>
        /// Relationship key (packed country pair)
        /// </summary>
        public ulong relationshipKey;

        /// <summary>
        /// The opinion modifier
        /// </summary>
        public OpinionModifier modifier;
    }
}
