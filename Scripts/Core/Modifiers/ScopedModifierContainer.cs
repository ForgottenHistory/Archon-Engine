using System;
using Unity.Collections;
using Core.Data;

namespace Core.Modifiers
{
    /// <summary>
    /// ENGINE: Container for modifiers with scope inheritance
    /// Pattern used by: EU4 (province ← country ← global), CK3 (character ← dynasty ← culture)
    ///
    /// Scope Hierarchy:
    /// - Province modifiers (local only)
    /// - Country modifiers (inherited by all provinces)
    /// - Global modifiers (inherited by everyone)
    ///
    /// Design:
    /// - ActiveModifierList for local modifiers
    /// - Reference to parent scope (optional)
    /// - Cached ModifierSet (dirty flag optimization)
    /// - Rebuild only when modifiers change
    ///
    /// Performance: O(n) rebuild where n = local + inherited modifiers
    /// Cached lookups: O(1) after rebuild
    /// </summary>
    public struct ScopedModifierContainer : IDisposable
    {
        private ActiveModifierList localModifiers;
        private ModifierSet cachedModifierSet;
        private bool isDirty;

        /// <summary>
        /// Create a new scoped modifier container
        /// </summary>
        public static ScopedModifierContainer Create(int capacity = ActiveModifierList.DEFAULT_CAPACITY, Allocator allocator = Allocator.Persistent)
        {
            return new ScopedModifierContainer
            {
                localModifiers = ActiveModifierList.Create(capacity, allocator),
                cachedModifierSet = new ModifierSet(),
                isDirty = true // Force initial rebuild
            };
        }

        /// <summary>
        /// Add a modifier to this scope
        /// </summary>
        public bool Add(ModifierSource source)
        {
            bool added = localModifiers.Add(source);
            if (added)
                isDirty = true; // Mark for rebuild
            return added;
        }

        /// <summary>
        /// Remove modifiers from a specific source
        /// </summary>
        public int RemoveBySource(ModifierSource.SourceType sourceType, uint sourceId)
        {
            int removed = localModifiers.RemoveBySource(sourceType, sourceId);
            if (removed > 0)
                isDirty = true; // Mark for rebuild
            return removed;
        }

        /// <summary>
        /// Remove modifiers of a specific type from a specific source
        /// </summary>
        public int RemoveBySourceAndType(ModifierSource.SourceType sourceType, uint sourceId, ushort modifierTypeId)
        {
            int removed = localModifiers.RemoveBySourceAndType(sourceType, sourceId, modifierTypeId);
            if (removed > 0)
                isDirty = true; // Mark for rebuild
            return removed;
        }

        /// <summary>
        /// Expire temporary modifiers
        /// </summary>
        public int ExpireModifiers(int currentTick)
        {
            int expired = localModifiers.ExpireModifiers(currentTick);
            if (expired > 0)
                isDirty = true; // Mark for rebuild
            return expired;
        }

        /// <summary>
        /// Clear all local modifiers
        /// </summary>
        public void Clear()
        {
            localModifiers.Clear();
            isDirty = true; // Mark for rebuild
        }

        /// <summary>
        /// Rebuild modifier set from local + inherited modifiers
        /// </summary>
        public void RebuildIfDirty(ScopedModifierContainer? parentScope = null)
        {
            if (!isDirty)
                return;

            // Clear cached set
            cachedModifierSet.Clear();

            // Apply parent scope modifiers first (if any)
            if (parentScope.HasValue)
            {
                parentScope.Value.RebuildIfDirty(); // Ensure parent is up to date
                var parentSet = parentScope.Value.GetModifierSet();

                // Copy only active parent modifiers using bitmask (typically 2-5 types vs 512)
                parentSet.CopyActiveToSet(ref cachedModifierSet);
            }

            // Apply local modifiers on top
            localModifiers.ApplyTo(ref cachedModifierSet);

            isDirty = false;
        }

        /// <summary>
        /// Get the final modifier set (rebuilds if dirty)
        /// </summary>
        public ModifierSet GetModifierSet(ScopedModifierContainer? parentScope = null)
        {
            RebuildIfDirty(parentScope);
            return cachedModifierSet;
        }

        /// <summary>
        /// Apply modifier to a base value (rebuilds if dirty)
        /// </summary>
        public FixedPoint64 ApplyModifier(ushort modifierTypeId, FixedPoint64 baseValue, ScopedModifierContainer? parentScope = null)
        {
            RebuildIfDirty(parentScope);
            return cachedModifierSet.ApplyModifier(modifierTypeId, baseValue);
        }

        /// <summary>
        /// Apply modifier from cached set without rebuild check.
        /// Only call when you know the scope is NOT dirty (check IsDirty first).
        /// </summary>
        public FixedPoint64 ApplyModifierFromCache(ushort modifierTypeId, FixedPoint64 baseValue)
        {
            return cachedModifierSet.ApplyModifier(modifierTypeId, baseValue);
        }

        /// <summary>
        /// Whether this scope needs rebuilding
        /// </summary>
        public bool IsDirty => isDirty;

        /// <summary>
        /// Check if this container has any local modifiers
        /// </summary>
        public bool HasLocalModifiers => localModifiers.HasAnyModifiers;

        /// <summary>
        /// Get count of local modifiers
        /// </summary>
        public int LocalModifierCount => localModifiers.ActiveCount;

        /// <summary>
        /// Mark this container as dirty (forces rebuild on next access)
        /// Use this when parent scope changes
        /// </summary>
        public void MarkDirty()
        {
            isDirty = true;
        }

        /// <summary>
        /// Iterate over local modifiers (for debugging/tooltips)
        /// </summary>
        public void ForEachLocalModifier(Action<ModifierSource> action)
        {
            localModifiers.ForEach(action);
        }

        /// <summary>
        /// Iterate over local modifiers from a specific source
        /// </summary>
        public void ForEachLocalModifierBySource(ModifierSource.SourceType sourceType, uint sourceId, Action<ModifierSource> action)
        {
            localModifiers.ForEachBySource(sourceType, sourceId, action);
        }

        /// <summary>
        /// Iterate over local modifiers of a specific modifier type
        /// </summary>
        public void ForEachLocalModifierByType(ushort modifierTypeId, Action<ModifierSource> action)
        {
            localModifiers.ForEachByModifierType(modifierTypeId, action);
        }

        /// <summary>
        /// Count local modifiers from a specific source
        /// </summary>
        public int CountLocalModifiersBySource(ModifierSource.SourceType sourceType, uint sourceId)
        {
            return localModifiers.CountBySource(sourceType, sourceId);
        }

        /// <summary>
        /// Dispose native collections
        /// </summary>
        public void Dispose()
        {
            localModifiers.Dispose();
        }
    }
}
