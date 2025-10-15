using System;

namespace Core.Data.SparseData
{
    /// <summary>
    /// Non-generic interface for sparse collections (type-erased)
    ///
    /// Purpose: Enables polymorphic management of different collection types
    /// - Memory monitoring across all collections
    /// - Unified disposal at shutdown
    /// - Capacity warnings for all collections
    ///
    /// Pattern: Interface with generic implementation
    /// - ISparseCollection provides type-erased contract
    /// - SparseCollectionManager<TKey, TValue> implements typed operations
    ///
    /// Use case: Central management in game systems
    /// - Track memory usage across all sparse collections
    /// - Dispose all collections at shutdown
    /// - Monitor capacity warnings globally
    /// </summary>
    public interface ISparseCollection : IDisposable
    {
        /// <summary>
        /// Name for debugging and logging
        /// Example: "ProvinceBuildings", "CountryModifiers"
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Is the collection initialized and ready for use?
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Pre-allocated capacity (from Principle 4)
        /// - Set at initialization
        /// - Fixed during gameplay (zero allocations)
        /// </summary>
        int Capacity { get; }

        /// <summary>
        /// Current number of entries in collection
        /// - Actual items stored (not capacity)
        /// - Used for memory monitoring
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Percentage of capacity used (0.0 to 1.0)
        /// - Warns when approaching capacity
        /// - Triggers logging at thresholds (80%, 90%, 95%)
        /// </summary>
        float CapacityUsage { get; }

        /// <summary>
        /// Clear all entries without deallocating
        /// - Resets count to 0
        /// - Keeps pre-allocated capacity (Principle 4)
        /// - Used for scenario reload or game reset
        /// </summary>
        void Clear();

        /// <summary>
        /// Get memory statistics for profiling
        /// </summary>
        SparseCollectionStats GetStats();
    }

    /// <summary>
    /// Memory statistics for sparse collection monitoring
    /// </summary>
    public struct SparseCollectionStats
    {
        /// <summary>
        /// Collection name
        /// </summary>
        public string Name;

        /// <summary>
        /// Pre-allocated capacity
        /// </summary>
        public int Capacity;

        /// <summary>
        /// Current entry count
        /// </summary>
        public int Count;

        /// <summary>
        /// Capacity usage percentage (0.0 to 1.0)
        /// </summary>
        public float CapacityUsage;

        /// <summary>
        /// Estimated memory usage in bytes
        /// - Capacity × entry size (key + value)
        /// - Does not include NativeContainer overhead
        /// </summary>
        public int EstimatedMemoryBytes;

        /// <summary>
        /// Has capacity warning been triggered? (>80%)
        /// </summary>
        public bool HasCapacityWarning;
    }
}
