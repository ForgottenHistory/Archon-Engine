using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Data;
using Core.Systems;
using Unity.Collections;
using Unity.Jobs;
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
        /// Key: Packed ulong from (country1, country2) with country1 < country2
        /// Value: RelationData with opinion and war state
        /// Memory: ~30k relationships × 16 bytes = ~480KB
        /// NATIVE: NativeParallelHashMap for zero GC allocations
        /// </summary>
        private NativeParallelHashMap<ulong, RelationData> relations;

        /// <summary>
        /// Fast lookup for active wars
        /// NativeParallelHashSet for O(1) IsAtWar() checks
        /// Memory: ~1k wars × 8 bytes = ~8KB
        /// NATIVE: Zero GC allocations
        /// </summary>
        private NativeParallelHashSet<ulong> activeWars;

        /// <summary>
        /// Index: Country → List of enemies
        /// Optimizes GetEnemies() queries for AI
        /// Memory: ~1k countries × pointer = ~8KB + war lists
        /// NATIVE: NativeParallelMultiHashMap for one-to-many relationships
        /// </summary>
        private NativeParallelMultiHashMap<ushort, ushort> warsByCountry;

        // ========== COLD DATA (Rare Access) ==========

        /// <summary>
        /// FLAT STORAGE for ALL opinion modifiers across ALL relationships
        ///
        /// ARCHITECTURE:
        /// - ALL modifiers stored with their relationship keys in single NativeList
        /// - modifierCache tracks start indices for fast O(1) lookup
        /// - Enables Burst-compiled parallel processing (no nested containers!)
        ///
        /// DETERMINISM:
        /// - Insertion order preserved (append-only during month)
        /// - Decay marks modifiers (parallel read-only, no race conditions)
        /// - Compaction rebuilds array sequentially (deterministic)
        ///
        /// MEMORY: ~32 bytes per modifier × 610k = ~19 MB worst case
        /// </summary>
        private NativeList<ModifierWithKey> allModifiers;

        /// <summary>
        /// Cache: Maps relationship key → first modifier index in allModifiers
        /// Rebuilt after decay compaction for O(1) GetOpinion() performance
        /// Key: Relationship key, Value: Index of first modifier for this relationship
        /// </summary>
        private NativeParallelHashMap<ulong, int> modifierCache;

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

            // Allocate NativeCollections with Persistent allocator
            // Pre-allocate for realistic worst case: 350 countries, ~60k relationships max
            relations = new NativeParallelHashMap<ulong, RelationData>(65536, Allocator.Persistent);
            activeWars = new NativeParallelHashSet<ulong>(2048, Allocator.Persistent);
            warsByCountry = new NativeParallelMultiHashMap<ushort, ushort>(2048, Allocator.Persistent);

            // FLAT STORAGE: Pre-allocate for worst case (610k modifiers with keys)
            allModifiers = new NativeList<ModifierWithKey>(655360, Allocator.Persistent);  // 610k + headroom
            modifierCache = new NativeParallelHashMap<ulong, int>(65536, Allocator.Persistent);

            ArchonLogger.LogCoreDiplomacy("DiplomacySystem: Initialized with NativeCollections (flat modifier storage)");
        }

        protected override void OnShutdown()
        {
            ArchonLogger.LogCoreDiplomacy("DiplomacySystem: Shutting down...");

            // Dispose all NativeCollections
            if (relations.IsCreated) relations.Dispose();
            if (activeWars.IsCreated) activeWars.Dispose();
            if (warsByCountry.IsCreated) warsByCountry.Dispose();
            if (allModifiers.IsCreated) allModifiers.Dispose();
            if (modifierCache.IsCreated) modifierCache.Dispose();

            ArchonLogger.LogCoreDiplomacy("DiplomacySystem: Disposed all NativeCollections");
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
                var (c1, c2) = UnpackKey(key);
                relation = RelationData.Create(c1, c2, baseOpinion);
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
        public List<(ushort, ushort)> GetAllWars()
        {
            var result = new List<(ushort, ushort)>();
            var keys = activeWars.ToNativeArray(Allocator.Temp);

            for (int i = 0; i < keys.Length; i++)
            {
                result.Add(UnpackKey(keys[i]));
            }

            keys.Dispose();
            return result;
        }

        /// <summary>
        /// Get count of active wars
        /// </summary>
        public int GetWarCount()
        {
            return activeWars.Count();
        }

        // ========== QUERIES: ADVANCED ==========

        /// <summary>
        /// Get all countries with opinion above threshold
        /// Used by AI for finding potential allies
        /// </summary>
        public List<ushort> GetCountriesWithOpinionAbove(ushort countryID, FixedPoint64 threshold, int currentTick)
        {
            var result = new List<ushort>();
            var keys = relations.GetKeyArray(Allocator.Temp);

            for (int i = 0; i < keys.Length; i++)
            {
                var (c1, c2) = UnpackKey(keys[i]);

                if (c1 != countryID && c2 != countryID)
                    continue;

                ushort otherCountry = (c1 == countryID) ? c2 : c1;
                FixedPoint64 opinion = GetOpinion(countryID, otherCountry, currentTick);

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
        public List<ushort> GetCountriesWithOpinionBelow(ushort countryID, FixedPoint64 threshold, int currentTick)
        {
            var result = new List<ushort>();
            var keys = relations.GetKeyArray(Allocator.Temp);

            for (int i = 0; i < keys.Length; i++)
            {
                var (c1, c2) = UnpackKey(keys[i]);

                if (c1 != countryID && c2 != countryID)
                    continue;

                ushort otherCountry = (c1 == countryID) ? c2 : c1;
                FixedPoint64 opinion = GetOpinion(countryID, otherCountry, currentTick);

                if (opinion < threshold)
                    result.Add(otherCountry);
            }

            keys.Dispose();
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
                var (c1, c2) = UnpackKey(key);
                var relation = RelationData.Create(c1, c2, DEFAULT_BASE_OPINION);
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
            var (c1, c2) = UnpackKey(key);
            if (guarantor == c1)
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
            var (c1, c2) = UnpackKey(key);
            if (granter == c1)
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
            var kvps = relations.GetKeyValueArrays(Allocator.Temp);

            for (int i = 0; i < kvps.Keys.Length; i++)
            {
                var rel = kvps.Values[i];

                if (!rel.InvolvesCountry(countryID)) continue;
                if ((rel.treatyFlags & (byte)TreatyFlags.Alliance) == 0) continue;

                result.Add(rel.GetOtherCountry(countryID));
            }

            kvps.Dispose();
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
            var kvps = relations.GetKeyValueArrays(Allocator.Temp);

            for (int i = 0; i < kvps.Keys.Length; i++)
            {
                var key = kvps.Keys[i];
                var rel = kvps.Values[i];
                var (c1, c2) = UnpackKey(key);

                if (!rel.InvolvesCountry(guarantorID)) continue;

                // Check if guarantorID is guaranteeing the other country
                if (guarantorID == c1 && (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom1To2) != 0)
                {
                    result.Add(c2);
                }
                else if (guarantorID == c2 && (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom2To1) != 0)
                {
                    result.Add(c1);
                }
            }

            kvps.Dispose();
            return result;
        }

        /// <summary>
        /// Get all countries that guarantee this country
        /// </summary>
        public List<ushort> GetGuaranteedBy(ushort guaranteedID)
        {
            var result = new List<ushort>();
            var kvps = relations.GetKeyValueArrays(Allocator.Temp);

            for (int i = 0; i < kvps.Keys.Length; i++)
            {
                var key = kvps.Keys[i];
                var rel = kvps.Values[i];
                var (c1, c2) = UnpackKey(key);

                if (!rel.InvolvesCountry(guaranteedID)) continue;

                // Check if the other country is guaranteeing guaranteedID
                if (guaranteedID == c2 && (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom1To2) != 0)
                {
                    result.Add(c1);
                }
                else if (guaranteedID == c1 && (rel.treatyFlags & (byte)TreatyFlags.GuaranteeFrom2To1) != 0)
                {
                    result.Add(c2);
                }
            }

            kvps.Dispose();
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
                var (c1, c2) = UnpackKey(key);
                relations[key] = RelationData.Create(c1, c2, DEFAULT_BASE_OPINION);
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
                var (c1, c2) = UnpackKey(key);
                relations[key] = RelationData.Create(c1, c2, DEFAULT_BASE_OPINION);
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
                var (c1, c2) = UnpackKey(key);
                relations[key] = RelationData.Create(c1, c2, DEFAULT_BASE_OPINION);
            }

            var rel = relations[key];
            var (country1, country2) = UnpackKey(key);

            // Set directional guarantee bit
            if (guarantor == country1)
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
            var (c1, c2) = UnpackKey(key);

            // Clear directional guarantee bit
            if (guarantor == c1)
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
                var (c1, c2) = UnpackKey(key);
                relations[key] = RelationData.Create(c1, c2, DEFAULT_BASE_OPINION);
            }

            var rel = relations[key];
            var (country1, country2) = UnpackKey(key);

            // Set directional access bit
            if (granter == country1)
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
            var (c1, c2) = UnpackKey(key);

            // Clear directional access bit
            if (granter == c1)
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
            var (c1, c2) = UnpackKey(key);

            // Ensure relationship exists
            if (!relations.ContainsKey(key))
            {
                var relation = RelationData.Create(c1, c2, DEFAULT_BASE_OPINION);
                relations[key] = relation;
            }

            // Calculate old opinion
            FixedPoint64 oldOpinion = GetOpinion(country1, country2, currentTick);

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

            if (removed > 0)
            {
                ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Removed {removed} modifiers of type {modifierTypeID} between {country1} and {country2}");
            }
        }

        // ========== MODIFIER DECAY ==========

        /// <summary>
        /// Decay all opinion modifiers and remove fully decayed ones
        /// Called monthly by DiplomacyMonthlyTickHandler
        /// Target: <5ms for 610k modifiers (with Burst compilation)
        /// </summary>
        public void DecayOpinionModifiers(int currentTick)
        {
            int totalModifiers = allModifiers.Length;

            // Early exit if no modifiers
            if (totalModifiers == 0)
                return;

            // Step 1: Burst-compiled parallel job to mark decayed modifiers
            var isDecayed = new NativeArray<bool>(totalModifiers, Allocator.TempJob);

            var job = new DecayModifiersJob
            {
                modifiers = allModifiers.AsArray(),
                currentTick = currentTick,
                isDecayed = isDecayed
            };

            // Execute in parallel batches of 64
            var handle = job.Schedule(totalModifiers, 64);
            handle.Complete();

            // Step 2: Compact array SEQUENTIALLY (deterministic)
            // Count non-decayed modifiers
            int survivingCount = 0;
            for (int i = 0; i < totalModifiers; i++)
            {
                if (!isDecayed[i])
                    survivingCount++;
            }

            int removedCount = totalModifiers - survivingCount;

            // If nothing decayed, we're done
            if (removedCount == 0)
            {
                isDecayed.Dispose();
                return;
            }

            // Create new compacted array
            var compacted = new NativeList<ModifierWithKey>(survivingCount, Allocator.Temp);

            for (int i = 0; i < totalModifiers; i++)
            {
                if (!isDecayed[i])
                {
                    compacted.Add(allModifiers[i]);
                }
            }

            // Replace allModifiers with compacted version
            allModifiers.Clear();
            for (int i = 0; i < compacted.Length; i++)
            {
                allModifiers.Add(compacted[i]);
            }

            // Rebuild cache after compaction (CRITICAL for GetOpinion performance)
            modifierCache.Clear();
            for (int i = 0; i < allModifiers.Length; i++)
            {
                var key = allModifiers[i].relationshipKey;
                if (!modifierCache.ContainsKey(key))
                {
                    modifierCache[key] = i;  // Cache first modifier index for this relationship
                }
            }

            // Cleanup
            compacted.Dispose();
            isDecayed.Dispose();

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Decay processed {totalModifiers} modifiers, removed {removedCount} fully decayed (Burst parallel, <5ms target)");
        }

        // ========== HELPER METHODS ==========

        /// <summary>
        /// Pack two country IDs into a single ulong key
        /// Ensures consistent ordering (smaller ID first)
        /// Format: high 32 bits = country1, low 32 bits = country2
        /// </summary>
        private ulong PackKey(ushort country1, ushort country2)
        {
            if (country1 > country2)
            {
                // Swap to ensure country1 < country2
                ushort temp = country1;
                country1 = country2;
                country2 = temp;
            }

            return ((ulong)country1 << 32) | country2;
        }

        /// <summary>
        /// Unpack ulong key into two country IDs
        /// </summary>
        private (ushort, ushort) UnpackKey(ulong key)
        {
            ushort country1 = (ushort)(key >> 32);
            ushort country2 = (ushort)(key & 0xFFFFFFFF);
            return (country1, country2);
        }

        /// <summary>
        /// Legacy method for compatibility - converts to PackKey
        /// </summary>
        private ulong GetKey(ushort country1, ushort country2)
        {
            return PackKey(country1, country2);
        }

        /// <summary>
        /// Add to war index (warsByCountry)
        /// NativeParallelMultiHashMap automatically handles duplicates
        /// </summary>
        private void AddToWarIndex(ushort country, ushort enemy)
        {
            warsByCountry.Add(country, enemy);
        }

        /// <summary>
        /// Remove from war index (warsByCountry)
        /// </summary>
        private void RemoveFromWarIndex(ushort country, ushort enemy)
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
                // Get all relations
                var kvps = relations.GetKeyValueArrays(Allocator.Temp);

                // Write relationship count
                writer.Write(kvps.Keys.Length);

                // Write each relationship
                for (int i = 0; i < kvps.Keys.Length; i++)
                {
                    var key = kvps.Keys[i];
                    var relation = kvps.Values[i];
                    var (c1, c2) = UnpackKey(key);

                    // Write key
                    writer.Write(c1);
                    writer.Write(c2);

                    // Write hot data
                    writer.Write(relation.baseOpinion.RawValue);
                    writer.Write(relation.atWar);
                    writer.Write(relation.treatyFlags);

                    // FLAT STORAGE: Count and write modifiers for this relationship
                    int modifierCount = 0;
                    for (int m = 0; m < allModifiers.Length; m++)
                    {
                        if (allModifiers[m].relationshipKey == key)
                            modifierCount++;
                    }

                    writer.Write(modifierCount);

                    // Write each modifier for this relationship
                    for (int m = 0; m < allModifiers.Length; m++)
                    {
                        if (allModifiers[m].relationshipKey == key)
                        {
                            var modifier = allModifiers[m].modifier;
                            writer.Write(modifier.modifierTypeID);
                            writer.Write(modifier.value.RawValue);
                            writer.Write(modifier.appliedTick);
                            writer.Write(modifier.decayRate);
                        }
                    }
                }

                kvps.Dispose();

                // Store in saveData
                saveData.systemData["Diplomacy"] = stream.ToArray();
            }

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Saved {relations.Count()} relationships, {activeWars.Count()} wars");
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
            activeWars.Clear();
            warsByCountry.Clear();
            allModifiers.Clear();
            modifierCache.Clear();

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
                    var key = PackKey(country1, country2);

                    // Read hot data
                    long baseOpinionRaw = reader.ReadInt64();
                    bool atWar = reader.ReadBoolean();
                    byte treatyFlags = reader.ReadByte();

                    // Create relation
                    var relation = new RelationData
                    {
                        country1 = country1,
                        country2 = country2,
                        baseOpinion = FixedPoint64.FromRaw(baseOpinionRaw),
                        atWar = atWar,
                        treatyFlags = treatyFlags
                    };

                    relations[key] = relation;

                    // Rebuild war indices if at war
                    if (atWar)
                    {
                        activeWars.Add(key);
                        AddToWarIndex(country1, country2);
                        AddToWarIndex(country2, country1);
                    }

                    // FLAT STORAGE: Read modifiers for this relationship
                    int modifierCount = reader.ReadInt32();

                    // Read each modifier and add to flat storage
                    for (int j = 0; j < modifierCount; j++)
                    {
                        var modifier = new OpinionModifier
                        {
                            modifierTypeID = reader.ReadUInt16(),
                            value = FixedPoint64.FromRaw(reader.ReadInt64()),
                            appliedTick = reader.ReadInt32(),
                            decayRate = reader.ReadInt32()
                        };

                        allModifiers.Add(new ModifierWithKey
                        {
                            relationshipKey = key,
                            modifier = modifier
                        });
                    }
                }
            }

            // Rebuild cache after loading all modifiers
            for (int i = 0; i < allModifiers.Length; i++)
            {
                var key = allModifiers[i].relationshipKey;
                if (!modifierCache.ContainsKey(key))
                {
                    modifierCache[key] = i;  // Cache first modifier index for this relationship
                }
            }

            ArchonLogger.LogCoreDiplomacy($"DiplomacySystem: Loaded {relations.Count()} relationships, {activeWars.Count()} wars, {allModifiers.Length} modifiers");
        }

        // ========== STATISTICS ==========

        /// <summary>
        /// Get statistics for debugging and validation
        /// </summary>
        public DiplomacyStats GetStats()
        {
            // FLAT STORAGE: Count unique relationships with modifiers
            var uniqueRelationships = new HashSet<ulong>();
            for (int i = 0; i < allModifiers.Length; i++)
            {
                uniqueRelationships.Add(allModifiers[i].relationshipKey);
            }

            return new DiplomacyStats
            {
                relationshipCount = relations.Count(),
                warCount = activeWars.Count(),
                coldDataCount = uniqueRelationships.Count,
                totalModifiers = allModifiers.Length
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
