using System.Collections.Generic;
using Core.Data;
using Unity.Collections;

namespace Core.Diplomacy
{
    /// <summary>
    /// ENGINE LAYER - Manages opinion calculations and modifiers for diplomatic relations
    ///
    /// RESPONSIBILITY:
    /// - Opinion queries (GetOpinion, GetBaseOpinion)
    /// - Opinion filtering (GetCountriesWithOpinionAbove/Below)
    /// - Opinion modifier management (Add/Remove)
    ///
    /// PATTERN: Stateless manager (receives data references from DiplomacySystem)
    /// - Does NOT own NativeCollections (passed as parameters)
    /// - Pure methods for opinion calculations
    /// - Zero allocations in hot paths
    ///
    /// PERFORMANCE:
    /// - GetOpinion: O(1) cache lookup + O(m) modifier scan (m = modifiers per relationship, ~10)
    /// - GetBaseOpinion: O(1) dictionary lookup
    /// - AddOpinionModifier: O(1) append to flat array
    /// </summary>
    public static class DiplomacyRelationManager
    {
        // ========== CONSTANTS ==========

        public static readonly FixedPoint64 MIN_OPINION = FixedPoint64.FromInt(-200);
        public static readonly FixedPoint64 MAX_OPINION = FixedPoint64.FromInt(200);
        public static readonly FixedPoint64 DEFAULT_BASE_OPINION = FixedPoint64.Zero;

        // ========== OPINION QUERIES ==========

        /// <summary>
        /// Get total opinion between two countries (base + modifiers)
        /// Returns 0 if no relationship exists (neutral)
        /// Clamped to [-200, +200]
        /// </summary>
        public static FixedPoint64 GetOpinion(
            ushort country1,
            ushort country2,
            int currentTick,
            NativeParallelHashMap<ulong, RelationData> relations,
            NativeList<ModifierWithKey> allModifiers,
            NativeParallelHashMap<ulong, int> modifierCache)
        {
            var key = DiplomacyKeyHelper.GetKey(country1, country2);

            // No relationship = neutral opinion
            if (!relations.TryGetValue(key, out var relation))
                return FixedPoint64.Zero;

            // Start with base opinion
            FixedPoint64 total = relation.baseOpinion;

            // Add all modifiers for this relationship (use cache for O(1) lookup)
            if (modifierCache.TryGetValue(key, out int startIndex))
            {
                // Scan from cached start index until we find a different key
                for (int i = startIndex; i < allModifiers.Length; i++)
                {
                    if (allModifiers[i].relationshipKey != key)
                        break;  // Modifiers for this relationship are contiguous

                    total += allModifiers[i].modifier.CalculateCurrentValue(currentTick);
                }
            }

            // Clamp to valid range
            return FixedPoint64.Clamp(total, MIN_OPINION, MAX_OPINION);
        }

        /// <summary>
        /// Get base opinion (without modifiers)
        /// Used for debugging and UI details
        /// </summary>
        public static FixedPoint64 GetBaseOpinion(
            ushort country1,
            ushort country2,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(country1, country2);
            if (!relations.TryGetValue(key, out var relation))
                return DEFAULT_BASE_OPINION;
            return relation.baseOpinion;
        }

        /// <summary>
        /// Set base opinion between two countries
        /// Creates relationship if it doesn't exist
        /// </summary>
        public static void SetBaseOpinion(
            ushort country1,
            ushort country2,
            FixedPoint64 baseOpinion,
            NativeParallelHashMap<ulong, RelationData> relations)
        {
            var key = DiplomacyKeyHelper.GetKey(country1, country2);

            if (relations.TryGetValue(key, out var relation))
            {
                relation.baseOpinion = baseOpinion;
                relations[key] = relation;
            }
            else
            {
                // Create new relationship
                var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);
                relation = RelationData.Create(c1, c2, baseOpinion);
                relations[key] = relation;
            }
        }

        // ========== OPINION FILTERING ==========

        /// <summary>
        /// Get all countries with opinion above threshold
        /// Used by AI for finding potential allies
        /// </summary>
        public static List<ushort> GetCountriesWithOpinionAbove(
            ushort countryID,
            FixedPoint64 threshold,
            int currentTick,
            NativeParallelHashMap<ulong, RelationData> relations,
            NativeList<ModifierWithKey> allModifiers,
            NativeParallelHashMap<ulong, int> modifierCache)
        {
            var result = new List<ushort>();
            var keys = relations.GetKeyArray(Allocator.Temp);

            for (int i = 0; i < keys.Length; i++)
            {
                var (c1, c2) = DiplomacyKeyHelper.UnpackKey(keys[i]);

                if (c1 != countryID && c2 != countryID)
                    continue;

                ushort otherCountry = (c1 == countryID) ? c2 : c1;
                FixedPoint64 opinion = GetOpinion(countryID, otherCountry, currentTick, relations, allModifiers, modifierCache);

                if (opinion > threshold)
                    result.Add(otherCountry);
            }

            keys.Dispose();
            return result;
        }

        /// <summary>
        /// Get all countries with opinion below threshold
        /// Used by AI for finding war targets
        /// </summary>
        public static List<ushort> GetCountriesWithOpinionBelow(
            ushort countryID,
            FixedPoint64 threshold,
            int currentTick,
            NativeParallelHashMap<ulong, RelationData> relations,
            NativeList<ModifierWithKey> allModifiers,
            NativeParallelHashMap<ulong, int> modifierCache)
        {
            var result = new List<ushort>();
            var keys = relations.GetKeyArray(Allocator.Temp);

            for (int i = 0; i < keys.Length; i++)
            {
                var (c1, c2) = DiplomacyKeyHelper.UnpackKey(keys[i]);

                if (c1 != countryID && c2 != countryID)
                    continue;

                ushort otherCountry = (c1 == countryID) ? c2 : c1;
                FixedPoint64 opinion = GetOpinion(countryID, otherCountry, currentTick, relations, allModifiers, modifierCache);

                if (opinion < threshold)
                    result.Add(otherCountry);
            }

            keys.Dispose();
            return result;
        }

        // ========== MODIFIER MANAGEMENT ==========

        /// <summary>
        /// Add opinion modifier to a relationship
        /// Modifiers decay over time and affect GetOpinion()
        /// </summary>
        public static void AddOpinionModifier(
            ushort country1,
            ushort country2,
            OpinionModifier modifier,
            NativeParallelHashMap<ulong, RelationData> relations,
            NativeList<ModifierWithKey> allModifiers,
            NativeParallelHashMap<ulong, int> modifierCache)
        {
            var key = DiplomacyKeyHelper.GetKey(country1, country2);

            // Ensure relationship exists
            if (!relations.ContainsKey(key))
            {
                var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);
                var relation = RelationData.Create(c1, c2, DEFAULT_BASE_OPINION);
                relations[key] = relation;
            }

            // FLAT STORAGE: Append modifier with key to end of allModifiers
            int newIndex = allModifiers.Length;
            allModifiers.Add(new ModifierWithKey
            {
                relationshipKey = key,
                modifier = modifier
            });

            // Update cache: if this is the first modifier for this relationship, cache the index
            if (!modifierCache.ContainsKey(key))
            {
                modifierCache[key] = newIndex;
            }
        }

        /// <summary>
        /// Remove all opinion modifiers of a specific type from a relationship
        /// Uses RemoveAtSwapBack for O(1) removal (order not guaranteed)
        /// </summary>
        public static void RemoveOpinionModifier(
            ushort country1,
            ushort country2,
            ushort modifierTypeID,
            NativeList<ModifierWithKey> allModifiers,
            NativeParallelHashMap<ulong, int> modifierCache)
        {
            var key = DiplomacyKeyHelper.GetKey(country1, country2);
            int removed = 0;

            // FLAT STORAGE: Remove matching modifiers from allModifiers
            for (int i = allModifiers.Length - 1; i >= 0; i--)
            {
                if (allModifiers[i].relationshipKey == key &&
                    allModifiers[i].modifier.modifierTypeID == modifierTypeID)
                {
                    allModifiers.RemoveAtSwapBack(i);
                    removed++;
                }
            }

            // If we removed modifiers, cache needs rebuild
            // (DiplomacySystem will call RebuildModifierCache after batch removals)
            if (removed > 0)
            {
                ArchonLogger.LogCoreDiplomacy($"Removed {removed} modifiers of type {modifierTypeID} from relationship {key}");
            }
        }
    }
}
