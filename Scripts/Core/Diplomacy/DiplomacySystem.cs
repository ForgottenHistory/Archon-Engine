using System.Collections.Generic;
using Core.Data;
using Core.SaveLoad;
using Core.Systems;
using Unity.Collections;

namespace Core.Diplomacy
{
    /// <summary>
    /// ENGINE LAYER - Facade for diplomatic relations between countries
    ///
    /// PATTERN 6: Facade Pattern
    /// - Owns all NativeCollections (data ownership)
    /// - Delegates operations to specialized managers (stateless processors)
    /// - Provides unified API for external systems
    ///
    /// ARCHITECTURE:
    /// - DiplomacyRelationManager: Opinion calculations and modifiers
    /// - DiplomacyWarManager: War state and declarations
    /// - DiplomacyTreatyManager: Treaties (Alliance, NAP, Guarantee, Military Access)
    /// - DiplomacyModifierProcessor: Burst-optimized modifier decay
    /// - DiplomacySaveLoadHandler: Serialization/deserialization
    ///
    /// Performance Targets (Paradox Scale):
    /// - 1000 countries, 30k active relationships
    /// - GetOpinion() <0.1ms (O(1) cache + O(m) modifiers)
    /// - IsAtWar() <0.01ms (HashSet O(1))
    /// - DecayOpinionModifiers() <5ms for 610k modifiers (Burst parallel)
    ///
    /// Pattern Compliance:
    /// - Pattern 6: Facade (delegates to specialized managers)
    /// - Pattern 8: Sparse Collections (store active only)
    /// - Pattern 4: Hot/Cold Separation (RelationData hot, modifiers cold)
    /// - Pattern 5: Fixed-Point Determinism (FixedPoint64 opinions)
    /// - Pattern 17: Single Source of Truth (owns all diplomatic state)
    /// </summary>
    public class DiplomacySystem : GameSystem
    {
        public override string SystemName => "Diplomacy";

        // Reference to GameState (for EventBus access)
        private GameState gameState;

        // ========== DATA OWNERSHIP (Facade owns NativeCollections) ==========

        private NativeParallelHashMap<ulong, RelationData> relations;
        private NativeParallelHashSet<ulong> activeWars;
        private NativeParallelMultiHashMap<ushort, ushort> warsByCountry;
        private NativeList<ModifierWithKey> allModifiers;
        private NativeParallelHashMap<ulong, int> modifierCache;

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

            ArchonLogger.LogCoreDiplomacy("DiplomacySystem: Initialized (facade pattern with specialized managers)");
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

        // ========== OPINION QUERIES (Delegate to DiplomacyRelationManager) ==========

        public FixedPoint64 GetOpinion(ushort country1, ushort country2, int currentTick)
        {
            return DiplomacyRelationManager.GetOpinion(country1, country2, currentTick, relations, allModifiers, modifierCache);
        }

        public FixedPoint64 GetBaseOpinion(ushort country1, ushort country2)
        {
            return DiplomacyRelationManager.GetBaseOpinion(country1, country2, relations);
        }

        public void SetBaseOpinion(ushort country1, ushort country2, FixedPoint64 baseOpinion)
        {
            DiplomacyRelationManager.SetBaseOpinion(country1, country2, baseOpinion, relations);
        }

        public List<ushort> GetCountriesWithOpinionAbove(ushort countryID, FixedPoint64 threshold, int currentTick)
        {
            return DiplomacyRelationManager.GetCountriesWithOpinionAbove(countryID, threshold, currentTick, relations, allModifiers, modifierCache);
        }

        public List<ushort> GetCountriesWithOpinionBelow(ushort countryID, FixedPoint64 threshold, int currentTick)
        {
            return DiplomacyRelationManager.GetCountriesWithOpinionBelow(countryID, threshold, currentTick, relations, allModifiers, modifierCache);
        }

        // ========== MODIFIER MANAGEMENT (Delegate to DiplomacyRelationManager) ==========

        public void AddOpinionModifier(ushort country1, ushort country2, OpinionModifier modifier, int currentTick)
        {
            DiplomacyRelationManager.AddOpinionModifier(country1, country2, modifier, relations, allModifiers, modifierCache);
        }

        public void RemoveOpinionModifier(ushort country1, ushort country2, ushort modifierTypeID)
        {
            DiplomacyRelationManager.RemoveOpinionModifier(country1, country2, modifierTypeID, allModifiers, modifierCache);
        }

        public void DecayOpinionModifiers(int currentTick)
        {
            DiplomacyModifierProcessor.DecayOpinionModifiers(currentTick, allModifiers, modifierCache);
        }

        // ========== WAR QUERIES (Delegate to DiplomacyWarManager) ==========

        public bool IsAtWar(ushort country1, ushort country2)
        {
            return DiplomacyWarManager.IsAtWar(country1, country2, activeWars);
        }

        public List<ushort> GetEnemies(ushort countryID)
        {
            return DiplomacyWarManager.GetEnemies(countryID, warsByCountry);
        }

        public List<(ushort, ushort)> GetAllWars()
        {
            return DiplomacyWarManager.GetAllWars(activeWars);
        }

        public int GetWarCount()
        {
            return DiplomacyWarManager.GetWarCount(activeWars);
        }

        // ========== WAR STATE CHANGES (Delegate to DiplomacyWarManager) ==========

        public void DeclareWar(ushort attackerID, ushort defenderID, int currentTick)
        {
            DiplomacyWarManager.DeclareWar(attackerID, defenderID, currentTick, relations, activeWars, warsByCountry, gameState);
        }

        public void MakePeace(ushort country1, ushort country2, int currentTick)
        {
            DiplomacyWarManager.MakePeace(country1, country2, currentTick, relations, activeWars, warsByCountry, gameState);
        }

        // ========== TREATY QUERIES (Delegate to DiplomacyTreatyManager) ==========

        public bool AreAllied(ushort country1, ushort country2)
        {
            return DiplomacyTreatyManager.AreAllied(country1, country2, relations);
        }

        public bool HasNonAggressionPact(ushort country1, ushort country2)
        {
            return DiplomacyTreatyManager.HasNonAggressionPact(country1, country2, relations);
        }

        public bool IsGuaranteeing(ushort guarantor, ushort guaranteed)
        {
            return DiplomacyTreatyManager.IsGuaranteeing(guarantor, guaranteed, relations);
        }

        public bool HasMilitaryAccess(ushort granter, ushort recipient)
        {
            return DiplomacyTreatyManager.HasMilitaryAccess(granter, recipient, relations);
        }

        public List<ushort> GetAllies(ushort countryID)
        {
            return DiplomacyTreatyManager.GetAllies(countryID, relations);
        }

        public HashSet<ushort> GetAlliesRecursive(ushort countryID)
        {
            return DiplomacyTreatyManager.GetAlliesRecursive(countryID, relations);
        }

        public List<ushort> GetGuaranteeing(ushort guarantorID)
        {
            return DiplomacyTreatyManager.GetGuaranteeing(guarantorID, relations);
        }

        public List<ushort> GetGuaranteedBy(ushort guaranteedID)
        {
            return DiplomacyTreatyManager.GetGuaranteedBy(guaranteedID, relations);
        }

        // ========== TREATY STATE CHANGES (Delegate to DiplomacyTreatyManager) ==========

        public void FormAlliance(ushort country1, ushort country2, int currentTick)
        {
            DiplomacyTreatyManager.FormAlliance(country1, country2, relations);
        }

        public void BreakAlliance(ushort country1, ushort country2, int currentTick)
        {
            DiplomacyTreatyManager.BreakAlliance(country1, country2, relations);
        }

        public void FormNonAggressionPact(ushort country1, ushort country2, int currentTick)
        {
            DiplomacyTreatyManager.FormNonAggressionPact(country1, country2, relations);
        }

        public void BreakNonAggressionPact(ushort country1, ushort country2, int currentTick)
        {
            DiplomacyTreatyManager.BreakNonAggressionPact(country1, country2, relations);
        }

        public void GuaranteeIndependence(ushort guarantor, ushort guaranteed, int currentTick)
        {
            DiplomacyTreatyManager.GuaranteeIndependence(guarantor, guaranteed, relations);
        }

        public void RevokeGuarantee(ushort guarantor, ushort guaranteed, int currentTick)
        {
            DiplomacyTreatyManager.RevokeGuarantee(guarantor, guaranteed, relations);
        }

        public void GrantMilitaryAccess(ushort granter, ushort recipient, int currentTick)
        {
            DiplomacyTreatyManager.GrantMilitaryAccess(granter, recipient, relations);
        }

        public void RevokeMilitaryAccess(ushort granter, ushort recipient, int currentTick)
        {
            DiplomacyTreatyManager.RevokeMilitaryAccess(granter, recipient, relations);
        }

        // ========== SAVE/LOAD (Delegate to DiplomacySaveLoadHandler) ==========

        protected override void OnSave(SaveGameData saveData)
        {
            DiplomacySaveLoadHandler.OnSave(saveData, relations, allModifiers, activeWars);
        }

        protected override void OnLoad(SaveGameData saveData)
        {
            DiplomacySaveLoadHandler.OnLoad(saveData, relations, activeWars, warsByCountry, allModifiers, modifierCache);
        }

        // ========== STATISTICS ==========

        public DiplomacyStats GetStats()
        {
            return new DiplomacyStats
            {
                relationshipCount = relations.Count(),
                warCount = activeWars.Count(),
                totalModifiers = allModifiers.Length
            };
        }
    }

    /// <summary>
    /// Statistics for debugging and validation
    /// </summary>
    public struct DiplomacyStats
    {
        public int relationshipCount;
        public int warCount;
        public int totalModifiers;
    }
}
