using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Data;
using Core.Systems;
using UnityEngine;

namespace Core.Diplomacy
{
    /// <summary>
    /// ENGINE LAYER - Central manager for all diplomatic relations between countries
    ///
    /// Architecture:
    /// - Sparse storage (only active relationships, not all possible pairs)
    /// - Hot/cold data separation (opinion/war in hot, modifiers in cold)
    /// - Fixed-point determinism (FixedPoint64 for all calculations)
    /// - Command pattern integration (all state changes via commands)
    /// - Event-driven (emits events for AI/UI reactions)
    ///
    /// Performance Targets (Paradox Scale):
    /// - 1000 countries, 30k active relationships
    /// - GetOpinion() <0.1ms (Dictionary O(1))
    /// - IsAtWar() <0.01ms (HashSet O(1))
    /// - DecayOpinionModifiers() <20ms for 100k modifiers (monthly tick)
    ///
    /// Pattern Compliance:
    /// - Pattern 8: Sparse Collections (store active only)
    /// - Pattern 4: Hot/Cold Separation (RelationData hot, DiplomacyColdData cold)
    /// - Pattern 5: Fixed-Point Determinism (FixedPoint64 opinions)
    /// - Pattern 17: Single Source of Truth (owns all diplomatic state)
    /// </summary>
    public class DiplomacySystem : GameSystem
    {
        public override string SystemName => "Diplomacy";

        // Reference to GameState (for EventBus access)
        private GameState gameState;

        // ========== HOT DATA (Frequent Access) ==========

        /// <summary>
        /// Sparse storage for active relationships
        /// Key: (country1, country2) with country1 < country2
        /// Value: RelationData with opinion and war state
        /// Memory: ~30k relationships × 16 bytes = ~480KB
        /// </summary>
        private Dictionary<(ushort, ushort), RelationData> relations = new Dictionary<(ushort, ushort), RelationData>();

        /// <summary>
        /// Fast lookup for active wars
        /// HashSet for O(1) IsAtWar() checks
        /// Memory: ~1k wars × 8 bytes = ~8KB
        /// </summary>
        private HashSet<(ushort, ushort)> activeWars = new HashSet<(ushort, ushort)>();

        /// <summary>
        /// Index: Country → List of enemies
        /// Optimizes GetEnemies() queries for AI
        /// Memory: ~1k countries × pointer = ~8KB + war lists
        /// </summary>
        private Dictionary<ushort, List<ushort>> warsByCountry = new Dictionary<ushort, List<ushort>>();

        // ========== COLD DATA (Rare Access) ==========

        /// <summary>
        /// Cold data storage (modifiers, history)
        /// Only created when modifiers exist
        /// Key: Same as relations dictionary
        /// Value: DiplomacyColdData with modifier list
        /// </summary>
        private Dictionary<(ushort, ushort), DiplomacyColdData> coldData = new Dictionary<(ushort, ushort), DiplomacyColdData>();

        // ========== CONFIGURATION ==========

        /// <summary>
        /// Minimum opinion value (-200)
        /// </summary>
        public static readonly FixedPoint64 MIN_OPINION = FixedPoint64.FromInt(-200);

        /// <summary>
        /// Maximum opinion value (+200)
        /// </summary>
        public static readonly FixedPoint64 MAX_OPINION = FixedPoint64.FromInt(200);

        /// <summary>
        /// Default base opinion for new relationships (neutral)
        /// </summary>
        public static readonly FixedPoint64 DEFAULT_BASE_OPINION = FixedPoint64.Zero;

        // ========== LIFECYCLE ==========

        protected override void OnInitialize()
        {
            ArchonLogger.LogCoreDiplomacy("DiplomacySystem: Initializing...");

            // Get GameState reference for EventBus access
            gameState = GetComponent<GameState>();

            // Clear all data structures
            relations.Clear();
            coldData.Clear();
            activeWars.Clear();
            warsByCountry.Clear();

            ArchonLogger.LogCoreDiplomacy("DiplomacySystem: Initialized");
        }

        protected override void OnShutdown()
        {
            ArchonLogger.LogCoreDiplomacy("DiplomacySystem: Shutting down...");

            relations.Clear();
            coldData.Clear();
            activeWars.Clear();
            warsByCountry.Clear();
        }

        // ========== QUERIES: OPINION ==========

        /// <summary>
        /// Get total opinion between two countries (base + modifiers)
        /// Returns 0 if no relationship exists (neutral)
        /// Clamped to [-200, +200]
        /// </summary>
        public FixedPoint64 GetOpinion(ushort country1, ushort country2, int currentTick)
        {
            var key = GetKey(country1, country2);

            // No relationship = neutral opinion
            if (!relations.TryGetValue(key, out var relation))
                return FixedPoint64.Zero;

            // Start with base opinion
            FixedPoint64 total = relation.baseOpinion;

            // Add modifiers if they exist
            if (coldData.TryGetValue(key, out var cold))
            {
                total += cold.CalculateModifierTotal(currentTick);
            }

            // Clamp to valid range
            return FixedPoint64.Clamp(total, MIN_OPINION, MAX_OPINION);
        }

        /// <summary>
        /// Get base opinion (without modifiers)
        /// Used for debugging and UI details
        /// </summary>
        public FixedPoint64 GetBaseOpinion(ushort country1, ushort country2)
        {
            var key = GetKey(country1, country2);
            if (!relations.TryGetValue(key, out var relation))
                return DEFAULT_BASE_OPINION;
            return relation.baseOpinion;
        }

        /// <summary>
        /// Set base opinion between two countries
        /// Creates relationship if it doesn't exist
        /// </summary>
        public void SetBaseOpinion(ushort country1, ushort country2, FixedPoint64 baseOpinion)
        {
            var key = GetKey(country1, country2);

            if (relations.TryGetValue(key, out var relation))
            {
                relation.baseOpinion = baseOpinion;
                relations[key] = relation;
            }
            else
            {
                // Create new relationship
                relation = RelationData.Create(key.Item1, key.Item2, baseOpinion);
                relations[key] = relation;
            }
        }

        // ========== QUERIES: WAR STATE ==========

        /// <summary>
        /// Check if two countries are at war
        /// O(1) HashSet lookup
        /// </summary>
        public bool IsAtWar(ushort country1, ushort country2)
        {
            var key = GetKey(country1, country2);
            return activeWars.Contains(key);
        }

        /// <summary>
        /// Get all countries at war with the given country
        /// Optimized with warsByCountry index
        /// </summary>
        public List<ushort> GetEnemies(ushort countryID)
        {
            if (warsByCountry.TryGetValue(countryID, out var enemies))
                return new List<ushort>(enemies);  // Return copy
            return new List<ushort>();
        }

        /// <summary>
        /// Get all active wars as country pairs
        /// Used for debugging and UI
        /// </summary>
        public List<(ushort, ushort)> GetAllWars()
        {
            return new List<(ushort, ushort)>(activeWars);
        }

        /// <summary>
        /// Get count of active wars
        /// </summary>
        public int GetWarCount()
        {
            return activeWars.Count;
        }

        // ========== QUERIES: ADVANCED ==========

        /// <summary>
        /// Get all countries with opinion above threshold
        /// Used by AI for finding potential allies
        /// </summary>
        public List<ushort> GetCountriesWithOpinionAbove(ushort countryID, FixedPoint64 threshold, int currentTick)
        {
            var result = new List<ushort>();

            foreach (var kvp in relations)
            {
                var key = kvp.Key;
                if (key.Item1 != countryID && key.Item2 != countryID)
                    continue;

                ushort otherCountry = (key.Item1 == countryID) ? key.Item2 : key.Item1;
                FixedPoint64 opinion = GetOpinion(countryID, otherCountry, currentTick);

                if (opinion > threshold)
                    result.Add(otherCountry);
            }

            return result;
        }

        /// <summary>
        /// Get all countries with opinion below threshold
        /// Used by AI for finding war targets
        /// </summary>
        public List<ushort> GetCountriesWithOpinionBelow(ushort countryID, FixedPoint64 threshold, int currentTick)
        {
            var result = new List<ushort>();

            foreach (var kvp in relations)
            {
                var key = kvp.Key;
                if (key.Item1 != countryID && key.Item2 != countryID)
                    continue;

                ushort otherCountry = (key.Item1 == countryID) ? key.Item2 : key.Item1;
                FixedPoint64 opinion = GetOpinion(countryID, otherCountry, currentTick);

                if (opinion < threshold)
                    result.Add(otherCountry);
            }

            return result;
        }

        // ========== STATE CHANGES: WAR/PEACE ==========

        /// <summary>
        /// Declare war between two countries
        /// Called by DeclareWarCommand after validation
        /// </summary>
        public void DeclareWar(ushort attackerID, ushort defenderID, int currentTick)
        {
            var key = GetKey(attackerID, defenderID);

            // Ensure relationship exists
            if (!relations.ContainsKey(key))
            {
                var relation = RelationData.Create(key.Item1, key.Item2, DEFAULT_BASE_OPINION);
                relations[key] = relation;
            }

            // Update war state
            var rel = relations[key];
            rel.atWar = true;
            relations[key] = rel;

            // Add to activeWars set
            activeWars.Add(key);

            // Update warsByCountry index
            AddToWarIndex(attackerID, defenderID);
            AddToWarIndex(defenderID, attackerID);

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Country {attackerID} declared war on {defenderID}");

            // Emit event (Phase 2)
            var evt = new DiplomacyWarDeclaredEvent(attackerID, defenderID, currentTick);
            gameState.EventBus.Emit(evt);
        }

        /// <summary>
        /// Make peace between two countries
        /// Called by MakePeaceCommand after validation
        /// </summary>
        public void MakePeace(ushort country1, ushort country2, int currentTick)
        {
            var key = GetKey(country1, country2);

            if (!relations.ContainsKey(key))
            {
                ArchonLogger.LogCoreDiplomacyWarning($"DiplomacySystem: Cannot make peace - no relationship exists between {country1} and {country2}");
                return;
            }

            // Update war state
            var rel = relations[key];
            rel.atWar = false;
            relations[key] = rel;

            // Remove from activeWars set
            activeWars.Remove(key);

            // Update warsByCountry index
            RemoveFromWarIndex(country1, country2);
            RemoveFromWarIndex(country2, country1);

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Peace made between {country1} and {country2}");

            // Emit event (Phase 2)
            var evt = new DiplomacyPeaceMadeEvent(country1, country2, currentTick);
            gameState.EventBus.Emit(evt);
        }

        // ========== QUERIES: TREATIES ==========

        /// <summary>
        /// Check if two countries have an alliance
        /// </summary>
        public bool AreAllied(ushort country1, ushort country2)
        {
            var key = GetKey(country1, country2);
            if (!relations.TryGetValue(key, out var rel)) return false;
            return (rel.treatyFlags & (byte)TreatyFlags.Alliance) != 0;
        }

        /// <summary>
        /// Check if two countries have a non-aggression pact
        /// </summary>
        public bool HasNonAggressionPact(ushort country1, ushort country2)
        {
            var key = GetKey(country1, country2);
            if (!relations.TryGetValue(key, out var rel)) return false;
            return (rel.treatyFlags & (byte)TreatyFlags.NonAggressionPact) != 0;
        }

        /// <summary>
        /// Check if guarantor country guarantees guaranteed country's independence
        /// Directional check
        /// </summary>
        public bool IsGuaranteeing(ushort guarantor, ushort guaranteed)
        {
            var key = GetKey(guarantor, guaranteed);
            if (!relations.TryGetValue(key, out var rel)) return false;

            // Check direction
            if (guarantor == key.Item1)
                return (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom1To2) != 0;
            else
                return (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom2To1) != 0;
        }

        /// <summary>
        /// Check if granter grants military access to recipient
        /// Directional check
        /// </summary>
        public bool HasMilitaryAccess(ushort granter, ushort recipient)
        {
            var key = GetKey(granter, recipient);
            if (!relations.TryGetValue(key, out var rel)) return false;

            // Check direction
            if (granter == key.Item1)
                return (rel.treatyFlags & (byte)TreatyFlags.MilitaryAccessFrom1To2) != 0;
            else
                return (rel.treatyFlags & (byte)TreatyFlags.MilitaryAccessFrom2To1) != 0;
        }

        /// <summary>
        /// Get all allies of a country
        /// Returns list of country IDs that have alliance with given country
        /// </summary>
        public List<ushort> GetAllies(ushort countryID)
        {
            var result = new List<ushort>();

            foreach (var kvp in relations)
            {
                var key = kvp.Key;
                var rel = kvp.Value;

                if (!rel.InvolvesCountry(countryID)) continue;
                if ((rel.treatyFlags & (byte)TreatyFlags.Alliance) == 0) continue;

                result.Add(rel.GetOtherCountry(countryID));
            }

            return result;
        }

        /// <summary>
        /// Get all allies recursively (alliance chain A→B→C)
        /// Uses BFS traversal to find all connected allies
        /// CRITICAL for war declaration validation and auto-join
        /// </summary>
        public HashSet<ushort> GetAlliesRecursive(ushort countryID)
        {
            var visited = new HashSet<ushort>();
            var toVisit = new Queue<ushort>();

            toVisit.Enqueue(countryID);
            visited.Add(countryID);

            while (toVisit.Count > 0)
            {
                ushort current = toVisit.Dequeue();
                var allies = GetAllies(current);

                foreach (var ally in allies)
                {
                    if (!visited.Contains(ally))
                    {
                        visited.Add(ally);
                        toVisit.Enqueue(ally);
                    }
                }
            }

            visited.Remove(countryID);  // Don't include self
            return visited;
        }

        /// <summary>
        /// Get all countries that this country guarantees
        /// </summary>
        public List<ushort> GetGuaranteeing(ushort guarantorID)
        {
            var result = new List<ushort>();

            foreach (var kvp in relations)
            {
                var key = kvp.Key;
                var rel = kvp.Value;

                if (!rel.InvolvesCountry(guarantorID)) continue;

                // Check if guarantorID is guaranteeing the other country
                if (guarantorID == key.Item1 && (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom1To2) != 0)
                {
                    result.Add(key.Item2);
                }
                else if (guarantorID == key.Item2 && (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom2To1) != 0)
                {
                    result.Add(key.Item1);
                }
            }

            return result;
        }

        /// <summary>
        /// Get all countries that guarantee this country
        /// </summary>
        public List<ushort> GetGuaranteedBy(ushort guaranteedID)
        {
            var result = new List<ushort>();

            foreach (var kvp in relations)
            {
                var key = kvp.Key;
                var rel = kvp.Value;

                if (!rel.InvolvesCountry(guaranteedID)) continue;

                // Check if the other country is guaranteeing guaranteedID
                if (guaranteedID == key.Item2 && (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom1To2) != 0)
                {
                    result.Add(key.Item1);
                }
                else if (guaranteedID == key.Item1 && (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom2To1) != 0)
                {
                    result.Add(key.Item2);
                }
            }

            return result;
        }

        // ========== STATE CHANGES: TREATIES ==========

        /// <summary>
        /// Form alliance between two countries
        /// Called by FormAllianceCommand after validation
        /// </summary>
        public void FormAlliance(ushort country1, ushort country2, int currentTick)
        {
            var key = GetKey(country1, country2);

            // Ensure relationship exists
            if (!relations.ContainsKey(key))
            {
                relations[key] = RelationData.Create(key.Item1, key.Item2, DEFAULT_BASE_OPINION);
            }

            // Set alliance flag
            var rel = relations[key];
            rel.treatyFlags |= (byte)TreatyFlags.Alliance;
            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Alliance formed between {country1} and {country2}");
        }

        /// <summary>
        /// Break alliance between two countries
        /// </summary>
        public void BreakAlliance(ushort country1, ushort country2, int currentTick)
        {
            var key = GetKey(country1, country2);

            if (!relations.ContainsKey(key)) return;

            var rel = relations[key];
            rel.treatyFlags &= (byte)~TreatyFlags.Alliance;  // Clear alliance bit
            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Alliance broken between {country1} and {country2}");
        }

        /// <summary>
        /// Form non-aggression pact between two countries
        /// </summary>
        public void FormNonAggressionPact(ushort country1, ushort country2, int currentTick)
        {
            var key = GetKey(country1, country2);

            if (!relations.ContainsKey(key))
            {
                relations[key] = RelationData.Create(key.Item1, key.Item2, DEFAULT_BASE_OPINION);
            }

            var rel = relations[key];
            rel.treatyFlags |= (byte)TreatyFlags.NonAggressionPact;
            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Non-aggression pact formed between {country1} and {country2}");
        }

        /// <summary>
        /// Break non-aggression pact between two countries
        /// </summary>
        public void BreakNonAggressionPact(ushort country1, ushort country2, int currentTick)
        {
            var key = GetKey(country1, country2);

            if (!relations.ContainsKey(key)) return;

            var rel = relations[key];
            rel.treatyFlags &= (byte)~TreatyFlags.NonAggressionPact;
            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Non-aggression pact broken between {country1} and {country2}");
        }

        /// <summary>
        /// Guarantor guarantees guaranteed country's independence (directional)
        /// </summary>
        public void GuaranteeIndependence(ushort guarantor, ushort guaranteed, int currentTick)
        {
            var key = GetKey(guarantor, guaranteed);

            if (!relations.ContainsKey(key))
            {
                relations[key] = RelationData.Create(key.Item1, key.Item2, DEFAULT_BASE_OPINION);
            }

            var rel = relations[key];

            // Set directional guarantee bit
            if (guarantor == key.Item1)
                rel.treatyFlags |= (byte)TreatyFlags.GuaranteeFrom1To2;
            else
                rel.treatyFlags |= (byte)TreatyFlags.GuaranteeFrom2To1;

            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: {guarantor} now guarantees {guaranteed}'s independence");
        }

        /// <summary>
        /// Revoke guarantee of independence
        /// </summary>
        public void RevokeGuarantee(ushort guarantor, ushort guaranteed, int currentTick)
        {
            var key = GetKey(guarantor, guaranteed);

            if (!relations.ContainsKey(key)) return;

            var rel = relations[key];

            // Clear directional guarantee bit
            if (guarantor == key.Item1)
                rel.treatyFlags &= (byte)~TreatyFlags.GuaranteeFrom1To2;
            else
                rel.treatyFlags &= (byte)~TreatyFlags.GuaranteeFrom2To1;

            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: {guarantor} revoked guarantee of {guaranteed}");
        }

        /// <summary>
        /// Grant military access (directional)
        /// </summary>
        public void GrantMilitaryAccess(ushort granter, ushort recipient, int currentTick)
        {
            var key = GetKey(granter, recipient);

            if (!relations.ContainsKey(key))
            {
                relations[key] = RelationData.Create(key.Item1, key.Item2, DEFAULT_BASE_OPINION);
            }

            var rel = relations[key];

            // Set directional access bit
            if (granter == key.Item1)
                rel.treatyFlags |= (byte)TreatyFlags.MilitaryAccessFrom1To2;
            else
                rel.treatyFlags |= (byte)TreatyFlags.MilitaryAccessFrom2To1;

            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: {granter} granted military access to {recipient}");
        }

        /// <summary>
        /// Revoke military access
        /// </summary>
        public void RevokeMilitaryAccess(ushort granter, ushort recipient, int currentTick)
        {
            var key = GetKey(granter, recipient);

            if (!relations.ContainsKey(key)) return;

            var rel = relations[key];

            // Clear directional access bit
            if (granter == key.Item1)
                rel.treatyFlags &= (byte)~TreatyFlags.MilitaryAccessFrom1To2;
            else
                rel.treatyFlags &= (byte)~TreatyFlags.MilitaryAccessFrom2To1;

            relations[key] = rel;

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: {granter} revoked military access from {recipient}");
        }

        // ========== STATE CHANGES: OPINION MODIFIERS ==========

        /// <summary>
        /// Add an opinion modifier between two countries
        /// Creates relationship and cold data if needed
        /// </summary>
        public void AddOpinionModifier(ushort country1, ushort country2, OpinionModifier modifier, int currentTick)
        {
            var key = GetKey(country1, country2);

            // Ensure relationship exists
            if (!relations.ContainsKey(key))
            {
                var relation = RelationData.Create(key.Item1, key.Item2, DEFAULT_BASE_OPINION);
                relations[key] = relation;
            }

            // Ensure cold data exists
            if (!coldData.ContainsKey(key))
            {
                coldData[key] = new DiplomacyColdData();
            }

            // Calculate old opinion
            FixedPoint64 oldOpinion = GetOpinion(country1, country2, currentTick);

            // Add modifier
            coldData[key].AddModifier(modifier);
            coldData[key].lastInteractionTick = currentTick;

            // Calculate new opinion
            FixedPoint64 newOpinion = GetOpinion(country1, country2, currentTick);

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Added modifier {modifier.modifierTypeID} ({modifier.value}) between {country1} and {country2} (opinion {oldOpinion} → {newOpinion})");
        }

        /// <summary>
        /// Remove all opinion modifiers of a specific type
        /// Used when canceling actions (e.g., trade agreement ends)
        /// </summary>
        public void RemoveOpinionModifier(ushort country1, ushort country2, ushort modifierTypeID)
        {
            var key = GetKey(country1, country2);

            if (!coldData.ContainsKey(key))
                return;

            var cold = coldData[key];
            int removed = cold.modifiers.RemoveAll(m => m.modifierTypeID == modifierTypeID);

            if (removed > 0)
            {
                ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Removed {removed} modifiers of type {modifierTypeID} between {country1} and {country2}");
            }

            // Clean up cold data if empty
            if (cold.modifiers.Count == 0)
            {
                coldData.Remove(key);
            }
        }

        // ========== MODIFIER DECAY ==========

        /// <summary>
        /// Decay all opinion modifiers and remove fully decayed ones
        /// Called monthly by DiplomacyMonthlyTickHandler
        /// Target: <20ms for 100k modifiers
        /// </summary>
        public void DecayOpinionModifiers(int currentTick)
        {
            int totalModifiers = 0;
            int removedModifiers = 0;
            var keysToRemove = new List<(ushort, ushort)>();

            foreach (var kvp in coldData)
            {
                var key = kvp.Key;
                var cold = kvp.Value;

                totalModifiers += cold.modifiers.Count;

                // Remove fully decayed modifiers
                int removed = cold.RemoveDecayedModifiers(currentTick);
                removedModifiers += removed;

                // Mark for removal if no active modifiers
                if (!cold.HasActiveModifiers(currentTick))
                {
                    keysToRemove.Add(key);
                }
            }

            // Clean up empty cold data
            foreach (var key in keysToRemove)
            {
                coldData.Remove(key);
            }

            if (removedModifiers > 0)
            {
                ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Decay processed {totalModifiers} modifiers, removed {removedModifiers} fully decayed");
            }
        }

        // ========== HELPER METHODS ==========

        /// <summary>
        /// Get sorted key for dictionary lookups
        /// Ensures consistent ordering (smaller ID first)
        /// </summary>
        private (ushort, ushort) GetKey(ushort country1, ushort country2)
        {
            if (country1 < country2)
                return (country1, country2);
            else
                return (country2, country1);
        }

        /// <summary>
        /// Add to war index (warsByCountry)
        /// </summary>
        private void AddToWarIndex(ushort country, ushort enemy)
        {
            if (!warsByCountry.ContainsKey(country))
                warsByCountry[country] = new List<ushort>();

            if (!warsByCountry[country].Contains(enemy))
                warsByCountry[country].Add(enemy);
        }

        /// <summary>
        /// Remove from war index (warsByCountry)
        /// </summary>
        private void RemoveFromWarIndex(ushort country, ushort enemy)
        {
            if (warsByCountry.ContainsKey(country))
            {
                warsByCountry[country].Remove(enemy);

                // Clean up empty lists
                if (warsByCountry[country].Count == 0)
                    warsByCountry.Remove(country);
            }
        }

        // ========== SAVE/LOAD ==========

        /// <summary>
        /// Save diplomatic state to save data
        /// Pattern 14: Hybrid Save/Load (state snapshot for speed)
        /// </summary>
        protected override void OnSave(Core.SaveLoad.SaveGameData saveData)
        {
            ArchonLogger.LogCoreDiplomacy("DiplomacySystem: Saving state...");

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                // Write relationship count
                writer.Write(relations.Count);

                // Write each relationship
                foreach (var kvp in relations)
                {
                    var key = kvp.Key;
                    var relation = kvp.Value;

                    // Write key
                    writer.Write(key.Item1);
                    writer.Write(key.Item2);

                    // Write hot data
                    writer.Write(relation.baseOpinion.RawValue);
                    writer.Write(relation.atWar);

                    // Write cold data if it exists
                    bool hasColdData = coldData.ContainsKey(key);
                    writer.Write(hasColdData);

                    if (hasColdData)
                    {
                        var cold = coldData[key];

                        // Write last interaction tick
                        writer.Write(cold.lastInteractionTick);

                        // Write modifier count
                        writer.Write(cold.modifiers.Count);

                        // Write each modifier
                        foreach (var modifier in cold.modifiers)
                        {
                            writer.Write(modifier.modifierTypeID);
                            writer.Write(modifier.value.RawValue);
                            writer.Write(modifier.appliedTick);
                            writer.Write(modifier.decayRate);
                        }
                    }
                }

                // Store in saveData
                saveData.systemData["Diplomacy"] = stream.ToArray();
            }

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Saved {relations.Count} relationships, {activeWars.Count} wars");
        }

        /// <summary>
        /// Load diplomatic state from save data
        /// Rebuilds derived indices (activeWars, warsByCountry)
        /// </summary>
        protected override void OnLoad(Core.SaveLoad.SaveGameData saveData)
        {
            ArchonLogger.LogCoreDiplomacy("DiplomacySystem: Loading state...");

            // Clear existing data
            relations.Clear();
            coldData.Clear();
            activeWars.Clear();
            warsByCountry.Clear();

            // Get saved data
            if (!saveData.systemData.ContainsKey("Diplomacy"))
            {
                ArchonLogger.LogCoreDiplomacyWarning("DiplomacySystem: No save data found - starting fresh");
                return;
            }

            byte[] data = (byte[])saveData.systemData["Diplomacy"];
            using (var stream = new MemoryStream(data))
            using (var reader = new BinaryReader(stream))
            {
                // Read relationship count
                int relationCount = reader.ReadInt32();

                // Read each relationship
                for (int i = 0; i < relationCount; i++)
                {
                    // Read key
                    ushort country1 = reader.ReadUInt16();
                    ushort country2 = reader.ReadUInt16();
                    var key = (country1, country2);

                    // Read hot data
                    long baseOpinionRaw = reader.ReadInt64();
                    bool atWar = reader.ReadBoolean();

                    // Create relation
                    var relation = new RelationData
                    {
                        country1 = country1,
                        country2 = country2,
                        baseOpinion = FixedPoint64.FromRaw(baseOpinionRaw),
                        atWar = atWar
                    };

                    relations[key] = relation;

                    // Rebuild war indices if at war
                    if (atWar)
                    {
                        activeWars.Add(key);
                        AddToWarIndex(country1, country2);
                        AddToWarIndex(country2, country1);
                    }

                    // Read cold data if it exists
                    bool hasColdData = reader.ReadBoolean();

                    if (hasColdData)
                    {
                        var cold = new DiplomacyColdData();

                        // Read last interaction tick
                        cold.lastInteractionTick = reader.ReadInt32();

                        // Read modifier count
                        int modifierCount = reader.ReadInt32();

                        // Read each modifier
                        for (int j = 0; j < modifierCount; j++)
                        {
                            var modifier = new OpinionModifier
                            {
                                modifierTypeID = reader.ReadUInt16(),
                                value = FixedPoint64.FromRaw(reader.ReadInt64()),
                                appliedTick = reader.ReadInt32(),
                                decayRate = reader.ReadInt32()
                            };

                            cold.modifiers.Add(modifier);
                        }

                        coldData[key] = cold;
                    }
                }
            }

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Loaded {relations.Count} relationships, {activeWars.Count} wars");
        }

        // ========== STATISTICS ==========

        /// <summary>
        /// Get statistics for debugging and validation
        /// </summary>
        public DiplomacyStats GetStats()
        {
            return new DiplomacyStats
            {
                relationshipCount = relations.Count,
                warCount = activeWars.Count,
                coldDataCount = coldData.Count,
                totalModifiers = coldData.Values.Sum(c => c.modifiers.Count)
            };
        }
    }

    /// <summary>
    /// Statistics for DiplomacySystem (debugging/validation)
    /// </summary>
    public struct DiplomacyStats
    {
        public int relationshipCount;
        public int warCount;
        public int coldDataCount;
        public int totalModifiers;
    }
}
