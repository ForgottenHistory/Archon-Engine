using Core.Data;

namespace Core.Diplomacy
{
    /// <summary>
    /// Single modifier entry in the flattened modifier array
    /// Stores both the modifier data AND which relationship it belongs to
    ///
    /// This enables Burst-compiled parallel processing of ALL modifiers at once
    /// instead of iterating relationships sequentially
    /// </summary>
    public struct ModifierEntry
    {
        /// <summary>
        /// Relationship key (packed country pair)
        /// </summary>
        public ulong relationshipKey;

        /// <summary>
        /// The opinion modifier data
        /// </summary>
        public OpinionModifier modifier;

        /// <summary>
        /// Index in coldDataStorage for this relationship
        /// Used to mark relationships as empty after decay
        /// </summary>
        public int coldDataIndex;
    }
}
