using System.Collections.Generic;
using Core.Data;
using Core.Systems;
using Unity.Collections;

namespace Core.Diplomacy
{
    /// <summary>
    /// ENGINE LAYER - Manages war state and war-related queries
    ///
    /// RESPONSIBILITY:
    /// - War state queries (IsAtWar, GetEnemies, GetAllWars)
    /// - War declarations and peace treaties
    /// - War index management (warsByCountry for fast lookups)
    ///
    /// PATTERN: Stateless manager (receives data references from DiplomacySystem)
    /// - Does NOT own NativeCollections (passed as parameters)
    /// - Manages activeWars HashSet and warsByCountry index
    /// - Emits events through GameState.EventBus
    ///
    /// PERFORMANCE:
    /// - IsAtWar: O(1) HashSet lookup
    /// - GetEnemies: O(k) where k = enemies for this country
    /// - DeclareWar: O(1) + event emission
    /// </summary>
    public static class DiplomacyWarManager
    {
        // ========== WAR STATE QUERIES ==========

        /// <summary>
        /// Check if two countries are at war
        /// O(1) HashSet lookup
        /// </summary>
        public static bool IsAtWar(
            ushort country1,
            ushort country2,
            NativeParallelHashSet<ulong> activeWars)
        {
            var key = DiplomacyKeyHelper.GetKey(country1, country2);
            return activeWars.Contains(key);
        }

        /// <summary>
        /// Get all countries at war with the given country
        /// Optimized with warsByCountry index
        /// </summary>
        public static List<ushort> GetEnemies(
            ushort countryID,
            NativeParallelMultiHashMap<ushort, ushort> warsByCountry)
        {
            var result = new List<ushort>();

            if (warsByCountry.ContainsKey(countryID))
            {
                var iterator = warsByCountry.GetValuesForKey(countryID);
                foreach (var enemy in iterator)
                {
                    result.Add(enemy);
                }
            }

            return result;
        }

        /// <summary>
        /// Get all active wars as country pairs
        /// Used for debugging and UI
        /// </summary>
        public static List<(ushort, ushort)> GetAllWars(
            NativeParallelHashSet<ulong> activeWars)
        {
            var result = new List<(ushort, ushort)>();
            var keys = activeWars.ToNativeArray(Allocator.Temp);

            for (int i = 0; i < keys.Length; i++)
            {
                result.Add(DiplomacyKeyHelper.UnpackKey(keys[i]));
            }

            keys.Dispose();
            return result;
        }

        /// <summary>
        /// Get count of active wars
        /// </summary>
        public static int GetWarCount(
            NativeParallelHashSet<ulong> activeWars)
        {
            return activeWars.Count();
        }

        // ========== WAR STATE CHANGES ==========

        /// <summary>
        /// Declare war between two countries
        /// Called by DeclareWarCommand after validation
        /// </summary>
        public static void DeclareWar(
            ushort attackerID,
            ushort defenderID,
            int currentTick,
            NativeParallelHashMap<ulong, RelationData> relations,
            NativeParallelHashSet<ulong> activeWars,
            NativeParallelMultiHashMap<ushort, ushort> warsByCountry,
            GameState gameState)
        {
            var key = DiplomacyKeyHelper.GetKey(attackerID, defenderID);

            // Ensure relationship exists
            if (!relations.ContainsKey(key))
            {
                var (c1, c2) = DiplomacyKeyHelper.UnpackKey(key);
                var relation = RelationData.Create(c1, c2, DiplomacyRelationManager.DEFAULT_BASE_OPINION);
                relations[key] = relation;
            }

            // Update war state
            var rel = relations[key];
            rel.atWar = true;
            relations[key] = rel;

            // Add to activeWars set
            activeWars.Add(key);

            // Update warsByCountry index
            AddToWarIndex(attackerID, defenderID, warsByCountry);
            AddToWarIndex(defenderID, attackerID, warsByCountry);

            ArchonLogger.LogCoreDiplomacy($"War declared: {attackerID} vs {defenderID}");

            // Emit event
            var evt = new DiplomacyWarDeclaredEvent(attackerID, defenderID, currentTick);
            gameState.EventBus.Emit(evt);
        }

        /// <summary>
        /// Make peace between two countries
        /// Called by MakePeaceCommand after validation
        /// </summary>
        public static void MakePeace(
            ushort country1,
            ushort country2,
            int currentTick,
            NativeParallelHashMap<ulong, RelationData> relations,
            NativeParallelHashSet<ulong> activeWars,
            NativeParallelMultiHashMap<ushort, ushort> warsByCountry,
            GameState gameState)
        {
            var key = DiplomacyKeyHelper.GetKey(country1, country2);

            if (!relations.ContainsKey(key))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"Cannot make peace - no relationship exists between {country1} and {country2}");
                return;
            }

            // Update war state
            var rel = relations[key];
            rel.atWar = false;
            relations[key] = rel;

            // Remove from activeWars set
            activeWars.Remove(key);

            // Update warsByCountry index
            RemoveFromWarIndex(country1, country2, warsByCountry);
            RemoveFromWarIndex(country2, country1, warsByCountry);

            ArchonLogger.LogCoreDiplomacy($"Peace made: {country1} and {country2}");

            // Emit event
            var evt = new DiplomacyPeaceMadeEvent(country1, country2, currentTick);
            gameState.EventBus.Emit(evt);
        }

        // ========== WAR INDEX MANAGEMENT ==========

        /// <summary>
        /// Add to war index (warsByCountry)
        /// NativeParallelMultiHashMap automatically handles duplicates
        /// </summary>
        private static void AddToWarIndex(
            ushort country,
            ushort enemy,
            NativeParallelMultiHashMap<ushort, ushort> warsByCountry)
        {
            warsByCountry.Add(country, enemy);
        }

        /// <summary>
        /// Remove from war index (warsByCountry)
        /// </summary>
        private static void RemoveFromWarIndex(
            ushort country,
            ushort enemy,
            NativeParallelMultiHashMap<ushort, ushort> warsByCountry)
        {
            // NativeParallelMultiHashMap doesn't have direct Remove(key, value)
            // We need to iterate and remove matching entries
            if (warsByCountry.ContainsKey(country))
            {
                var iterator = warsByCountry.GetValuesForKey(country);
                var tempList = new NativeList<ushort>(Allocator.Temp);

                // Collect all values except the one we want to remove
                foreach (var val in iterator)
                {
                    if (val != enemy)
                        tempList.Add(val);
                }

                // Remove all entries for this country
                warsByCountry.Remove(country);

                // Re-add filtered entries
                for (int i = 0; i < tempList.Length; i++)
                {
                    warsByCountry.Add(country, tempList[i]);
                }

                tempList.Dispose();
            }
        }
    }
}
