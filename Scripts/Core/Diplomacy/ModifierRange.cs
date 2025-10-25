namespace Core.Diplomacy
{
    /// <summary>
    /// Tracks the range of modifiers for a single relationship in the flat modifier array
    ///
    /// DETERMINISM GUARANTEE:
    /// - Modifiers for each relationship stored in STABLE order (insertion order preserved)
    /// - Compaction is SEQUENTIAL on main thread (deterministic)
    /// - Parallel Burst job ONLY marks decayed (read-only, no order dependency)
    ///
    /// Architecture:
    /// allModifiers[startIndex ... startIndex+count-1] = this relationship's modifiers
    /// </summary>
    public struct ModifierRange
    {
        /// <summary>
        /// Index in allModifiers where this relationship's modifiers start
        /// </summary>
        public int startIndex;

        /// <summary>
        /// Number of active modifiers for this relationship
        /// </summary>
        public int count;

        /// <summary>
        /// Empty range (no modifiers)
        /// </summary>
        public static ModifierRange Empty => new ModifierRange { startIndex = -1, count = 0 };

        /// <summary>
        /// Check if this range is empty
        /// </summary>
        public bool IsEmpty => count == 0;
    }
}
