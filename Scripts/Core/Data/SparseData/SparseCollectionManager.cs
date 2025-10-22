using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System;

namespace Core.Data.SparseData
{
    /// <summary>
    /// Generic sparse collection for optional/rare data that scales with mods
    ///
    /// Problem: Dense arrays scale with POSSIBLE items (HOI4's 30→500 equipment disaster)
    /// Solution: Sparse storage scales with ACTUAL items (only what exists)
    ///
    /// Pattern: NativeParallelMultiHashMap for one-to-many relationships
    /// - One key (provinceID) → multiple values (buildingIDs)
    /// - Iteration only over actual items (not all possible)
    /// - Pre-allocated capacity (Principle 4: zero allocations during gameplay)
    ///
    /// Performance:
    /// - Has: O(m) where m = items per key (typically 3-5)
    /// - Get: O(m) where m = items per key
    /// - Add/Remove: O(1) average case
    /// - Iterate: O(actual items), not O(possible items)
    ///
    /// Memory: Scales with usage, not type count
    /// - Dense approach: 10k entities × 500 types = 5 MB
    /// - Sparse approach: 10k entities × 5 actual = 200 KB
    /// - Savings: 96% reduction at mod scale
    ///
    /// Example use cases:
    /// - Buildings: Province → BuildingIDs (most have 0-5 out of 100+)
    /// - Modifiers: Province → ModifierIDs (most have 0-3 out of 200+)
    /// - Trade Goods: Province → TradeGoodIDs (most produce 1-2 out of 50)
    ///
    /// Architecture:
    /// - TKey: Entity ID (ushort provinceId, ushort countryId)
    /// - TValue: Item ID (ushort buildingId, ushort modifierId)
    /// - Both must be unmanaged (value types, no references)
    /// </summary>
    public class SparseCollectionManager<TKey, TValue> : ISparseCollection
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged, IEquatable<TValue>
    {
        // Core storage
        private NativeParallelMultiHashMap<TKey, TValue> data;

        // Metadata
        private string name;
        private bool isInitialized;
        private int capacity;

        // Capacity monitoring (Principle 4 enforcement)
        private const float WARNING_THRESHOLD = 0.80f;  // Warn at 80%
        private const float CRITICAL_THRESHOLD = 0.95f; // Critical at 95%
        private bool hasLoggedWarning;
        private bool hasLoggedCritical;

        // ISparseCollection implementation
        public string Name => name;
        public bool IsInitialized => isInitialized;
        public int Capacity => capacity;
        public int Count => isInitialized ? data.Count() : 0;
        public float CapacityUsage => capacity > 0 ? (float)Count / capacity : 0f;

        /// <summary>
        /// Initialize sparse collection with pre-allocated capacity (Principle 4)
        ///
        /// Capacity estimation guidelines:
        /// - Entities × Average items per entity × Safety margin (2x)
        /// - Example: 10k provinces × 5 buildings × 2 = 100k capacity
        ///
        /// Safety margin accounts for:
        /// - Player builds more than average
        /// - Mods add more item types
        /// - Late-game province growth
        /// </summary>
        public void Initialize(string collectionName, int estimatedCapacity)
        {
            if (isInitialized)
            {
                ArchonLogger.LogCoreSimulationWarning($"SparseCollection '{collectionName}' already initialized");
                return;
            }

            name = collectionName;
            capacity = estimatedCapacity;

            // Pre-allocate with Allocator.Persistent (Principle 4)
            data = new NativeParallelMultiHashMap<TKey, TValue>(capacity, Allocator.Persistent);

            isInitialized = true;
            hasLoggedWarning = false;
            hasLoggedCritical = false;

            int entrySize = UnsafeUtility.SizeOf<TKey>() + UnsafeUtility.SizeOf<TValue>();
            int memoryKB = (capacity * entrySize) / 1024;

            ArchonLogger.LogCoreSimulation($"SparseCollection '{name}' initialized: {capacity} capacity, ~{memoryKB} KB pre-allocated");
        }

        #region Query APIs

        /// <summary>
        /// Check if key has specific value (existence check)
        ///
        /// Performance: O(m) where m = values per key (typically 3-5)
        /// Use case: "Does province 42 have Farm building?"
        /// </summary>
        public bool Has(TKey key, TValue value)
        {
            ValidateInitialized();

            if (!data.TryGetFirstValue(key, out TValue currentValue, out var iterator))
                return false;

            // Check first value
            if (currentValue.Equals(value))
                return true;

            // Check remaining values
            while (data.TryGetNextValue(out currentValue, ref iterator))
            {
                if (currentValue.Equals(value))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if key has any values
        ///
        /// Performance: O(1)
        /// Use case: "Does province have any buildings?"
        /// </summary>
        public bool HasAny(TKey key)
        {
            ValidateInitialized();
            return data.ContainsKey(key);
        }

        /// <summary>
        /// Get all values for a key
        ///
        /// Performance: O(m) where m = values per key
        /// Use case: "What buildings does province 42 have?"
        ///
        /// IMPORTANT: Caller must dispose returned NativeArray!
        /// </summary>
        public NativeArray<TValue> Get(TKey key, Allocator allocator = Allocator.TempJob)
        {
            ValidateInitialized();

            // Count values first
            int count = data.CountValuesForKey(key);
            if (count == 0)
                return new NativeArray<TValue>(0, allocator);

            // Allocate result array
            var result = new NativeArray<TValue>(count, allocator);
            int index = 0;

            // Collect all values
            if (data.TryGetFirstValue(key, out TValue value, out var iterator))
            {
                result[index++] = value;
                while (data.TryGetNextValue(out value, ref iterator))
                {
                    result[index++] = value;
                }
            }

            return result;
        }

        /// <summary>
        /// Get all values for a key (fills existing NativeList, zero-allocation)
        ///
        /// Performance: O(n) where n = values for this key
        /// Use case: Hot paths that need to avoid allocations (monthly tick, etc.)
        /// </summary>
        public void Get(TKey key, NativeList<TValue> resultBuffer)
        {
            ValidateInitialized();
            resultBuffer.Clear();

            // Collect all values
            if (data.TryGetFirstValue(key, out TValue value, out var iterator))
            {
                resultBuffer.Add(value);
                while (data.TryGetNextValue(out value, ref iterator))
                {
                    resultBuffer.Add(value);
                }
            }
        }

        /// <summary>
        /// Get count of values for a key
        ///
        /// Performance: O(1)
        /// Use case: "How many buildings does province have?"
        /// </summary>
        public int GetCount(TKey key)
        {
            ValidateInitialized();
            return data.CountValuesForKey(key);
        }

        #endregion

        #region Modification APIs

        /// <summary>
        /// Add value to key (allows duplicates!)
        ///
        /// Performance: O(1) average case
        /// Use case: "Province 42 builds Farm"
        ///
        /// Note: NativeMultiHashMap allows duplicate (key, value) pairs
        /// Call Has() first if you need to prevent duplicates
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            ValidateInitialized();
            data.Add(key, value);
            CheckCapacityWarnings();
        }

        /// <summary>
        /// Remove specific value from key
        ///
        /// Performance: O(m) where m = values per key
        /// Use case: "Province 42 destroys Farm"
        ///
        /// Returns: True if value was removed, false if not found
        /// </summary>
        public bool Remove(TKey key, TValue value)
        {
            ValidateInitialized();

            if (!data.TryGetFirstValue(key, out TValue currentValue, out var iterator))
                return false;

            // Check first value
            if (currentValue.Equals(value))
            {
                data.Remove(iterator);
                return true;
            }

            // Check remaining values
            while (data.TryGetNextValue(out currentValue, ref iterator))
            {
                if (currentValue.Equals(value))
                {
                    data.Remove(iterator);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Remove all values for a key
        ///
        /// Performance: O(m) where m = values per key
        /// Use case: "Clear all buildings from province 42"
        /// </summary>
        public void RemoveAll(TKey key)
        {
            ValidateInitialized();
            data.Remove(key);
        }

        #endregion

        #region Iteration APIs

        /// <summary>
        /// Process all values for a key with callback
        ///
        /// Performance: O(m) where m = values per key
        /// Use case: "Apply effects from all buildings in province"
        ///
        /// Pattern: Callback-based iteration (zero allocation)
        /// </summary>
        public void ProcessValues(TKey key, Action<TValue> processor)
        {
            ValidateInitialized();

            if (!data.TryGetFirstValue(key, out TValue value, out var iterator))
                return;

            processor(value);
            while (data.TryGetNextValue(out value, ref iterator))
            {
                processor(value);
            }
        }

        /// <summary>
        /// Get all unique keys in collection
        ///
        /// Performance: O(n) where n = entry count
        /// Use case: "Which provinces have any buildings?"
        ///
        /// IMPORTANT: Caller must dispose returned NativeArray!
        /// </summary>
        public NativeArray<TKey> GetKeys(Allocator allocator = Allocator.TempJob)
        {
            ValidateInitialized();
            return data.GetKeyArray(allocator);
        }

        /// <summary>
        /// Get all unique keys (fills existing NativeList, zero-allocation)
        ///
        /// Performance: O(n) where n = number of unique keys
        /// Use case: Hot paths that need to avoid allocations (monthly tick, etc.)
        /// </summary>
        public void GetKeys(NativeList<TKey> resultBuffer)
        {
            ValidateInitialized();
            resultBuffer.Clear();

            var keys = data.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++)
            {
                resultBuffer.Add(keys[i]);
            }
            keys.Dispose();
        }

        #endregion

        #region Memory Management

        /// <summary>
        /// Clear all entries without deallocating (Principle 4)
        ///
        /// Use case: Scenario reload, game reset
        /// Performance: O(1) - just resets internal count
        /// </summary>
        public void Clear()
        {
            if (!isInitialized)
                return;

            data.Clear();
            hasLoggedWarning = false;
            hasLoggedCritical = false;
        }

        /// <summary>
        /// Get memory statistics for profiling
        /// </summary>
        public SparseCollectionStats GetStats()
        {
            int entrySize = UnsafeUtility.SizeOf<TKey>() + UnsafeUtility.SizeOf<TValue>();
            float usage = CapacityUsage;

            return new SparseCollectionStats
            {
                Name = name,
                Capacity = capacity,
                Count = Count,
                CapacityUsage = usage,
                EstimatedMemoryBytes = capacity * entrySize,
                HasCapacityWarning = usage >= WARNING_THRESHOLD
            };
        }

        /// <summary>
        /// Dispose native collection (call at shutdown)
        /// </summary>
        public void Dispose()
        {
            if (!isInitialized)
                return;

            if (data.IsCreated)
                data.Dispose();

            isInitialized = false;
            ArchonLogger.LogCoreSimulation($"SparseCollection '{name}' disposed");
        }

        #endregion

        #region Validation & Monitoring

        private void ValidateInitialized()
        {
            if (!isInitialized)
            {
                throw new InvalidOperationException($"SparseCollection '{name ?? "unnamed"}' not initialized. Call Initialize() first.");
            }
        }

        private void CheckCapacityWarnings()
        {
            float usage = CapacityUsage;

            // Critical threshold (95%)
            if (usage >= CRITICAL_THRESHOLD && !hasLoggedCritical)
            {
                ArchonLogger.LogCoreSimulationWarning($"SparseCollection '{name}' CRITICAL: {usage:P1} capacity used ({Count}/{capacity}). Consider increasing capacity!");
                hasLoggedCritical = true;
            }
            // Warning threshold (80%)
            else if (usage >= WARNING_THRESHOLD && !hasLoggedWarning)
            {
                ArchonLogger.LogCoreSimulationWarning($"SparseCollection '{name}' WARNING: {usage:P1} capacity used ({Count}/{capacity})");
                hasLoggedWarning = true;
            }
        }

        #endregion
    }
}
