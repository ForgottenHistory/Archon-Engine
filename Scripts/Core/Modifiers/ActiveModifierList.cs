using System;
using Unity.Collections;

namespace Core.Modifiers
{
    /// <summary>
    /// ENGINE: Pool-based list of active modifiers with add/remove/expire logic
    /// Pattern used by: EU4 (timed modifiers), CK3 (character effects), Stellaris (empire modifiers)
    ///
    /// Design:
    /// - Fixed-size array (zero allocations)
    /// - Lazy deletion (mark as empty, compact on rebuild)
    /// - Efficient iteration (skip empty slots)
    /// - Track active count for quick checks
    ///
    /// Performance: O(n) iteration where n = active modifiers (not max capacity)
    /// </summary>
    public struct ActiveModifierList : IDisposable
    {
        public const int DEFAULT_CAPACITY = 64; // Should be enough for most provinces/countries

        private NativeArray<ModifierSource> modifiers;
        private NativeArray<bool> isActive;  // Track which slots are in use
        private int activeCount;             // Quick check if any modifiers exist
        private int capacity;

        /// <summary>
        /// Create a new active modifier list with specified capacity
        /// </summary>
        public static ActiveModifierList Create(int capacity = DEFAULT_CAPACITY, Allocator allocator = Allocator.Persistent)
        {
            return new ActiveModifierList
            {
                modifiers = new NativeArray<ModifierSource>(capacity, allocator),
                isActive = new NativeArray<bool>(capacity, allocator),
                activeCount = 0,
                capacity = capacity
            };
        }

        /// <summary>
        /// Add a new modifier to the list
        /// Returns false if capacity exceeded
        /// </summary>
        public bool Add(ModifierSource source)
        {
            // Find first empty slot
            for (int i = 0; i < capacity; i++)
            {
                if (!isActive[i])
                {
                    modifiers[i] = source;
                    isActive[i] = true;
                    activeCount++;
                    return true;
                }
            }

            // No empty slots found - capacity exceeded
            UnityEngine.Debug.LogWarning($"ActiveModifierList capacity exceeded ({capacity}). Consider increasing capacity.");
            return false;
        }

        /// <summary>
        /// Remove modifiers from a specific source
        /// Returns count of modifiers removed
        /// </summary>
        public int RemoveBySource(ModifierSource.SourceType sourceType, uint sourceId)
        {
            int removed = 0;

            for (int i = 0; i < capacity; i++)
            {
                if (isActive[i] &&
                    modifiers[i].Type == sourceType &&
                    modifiers[i].SourceID == sourceId)
                {
                    isActive[i] = false;
                    activeCount--;
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// Remove modifiers of a specific type from a specific source
        /// Returns count of modifiers removed
        /// </summary>
        public int RemoveBySourceAndType(ModifierSource.SourceType sourceType, uint sourceId, ushort modifierTypeId)
        {
            int removed = 0;

            for (int i = 0; i < capacity; i++)
            {
                if (isActive[i] &&
                    modifiers[i].Type == sourceType &&
                    modifiers[i].SourceID == sourceId &&
                    modifiers[i].ModifierTypeId == modifierTypeId)
                {
                    isActive[i] = false;
                    activeCount--;
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// Expire temporary modifiers that have passed their expiration tick
        /// Returns count of modifiers expired
        /// </summary>
        public int ExpireModifiers(int currentTick)
        {
            int expired = 0;

            for (int i = 0; i < capacity; i++)
            {
                if (isActive[i] && modifiers[i].HasExpired(currentTick))
                {
                    isActive[i] = false;
                    activeCount--;
                    expired++;
                }
            }

            return expired;
        }

        /// <summary>
        /// Apply all active modifiers to a ModifierSet
        /// </summary>
        public void ApplyTo(ref ModifierSet modifierSet)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (isActive[i])
                {
                    var source = modifiers[i];
                    modifierSet.Add(source.ModifierTypeId, source.Value, source.IsMultiplicative);
                }
            }
        }

        /// <summary>
        /// Clear all modifiers
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < capacity; i++)
            {
                isActive[i] = false;
            }
            activeCount = 0;
        }

        /// <summary>
        /// Check if any modifiers are active
        /// </summary>
        public bool HasAnyModifiers => activeCount > 0;

        /// <summary>
        /// Get count of active modifiers
        /// </summary>
        public int ActiveCount => activeCount;

        /// <summary>
        /// Iterate over all active modifiers (for debugging/tooltips)
        /// </summary>
        public void ForEach(Action<ModifierSource> action)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (isActive[i])
                {
                    action(modifiers[i]);
                }
            }
        }

        /// <summary>
        /// Iterate over modifiers from a specific source
        /// </summary>
        public void ForEachBySource(ModifierSource.SourceType sourceType, uint sourceId, Action<ModifierSource> action)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (isActive[i] &&
                    modifiers[i].Type == sourceType &&
                    modifiers[i].SourceID == sourceId)
                {
                    action(modifiers[i]);
                }
            }
        }

        /// <summary>
        /// Iterate over modifiers of a specific modifier type
        /// </summary>
        public void ForEachByModifierType(ushort modifierTypeId, Action<ModifierSource> action)
        {
            for (int i = 0; i < capacity; i++)
            {
                if (isActive[i] && modifiers[i].ModifierTypeId == modifierTypeId)
                {
                    action(modifiers[i]);
                }
            }
        }

        /// <summary>
        /// Count modifiers from a specific source
        /// </summary>
        public int CountBySource(ModifierSource.SourceType sourceType, uint sourceId)
        {
            int count = 0;
            for (int i = 0; i < capacity; i++)
            {
                if (isActive[i] &&
                    modifiers[i].Type == sourceType &&
                    modifiers[i].SourceID == sourceId)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Dispose native collections
        /// </summary>
        public void Dispose()
        {
            if (modifiers.IsCreated)
                modifiers.Dispose();
            if (isActive.IsCreated)
                isActive.Dispose();
        }
    }
}
