# Core Pillars Implementation Plan
**Date:** 2025-10-19
**Type:** ENGINE Feature Implementation
**Scope:** Complete the four pillars of grand strategy
**Status:** 📋 Planning

---

## OVERVIEW

Grand strategy games have **four core pillars:**
1. ✅ **Economy** - Resource management, production, trade
2. ❌ **Military** - Units, movement, combat
3. ❌ **Diplomacy** - Relations, treaties, alliances
4. ❌ **AI** - Decision-making, opponents, challenge

**Current Status:** Economy pillar complete. Need to implement Military, Diplomacy, AI.

**Goal:** Validate Archon-Engine architecture with all four pillars working together.

---

## PILLAR 1: ECONOMY ✅ COMPLETE

**What Works:**
- Multi-resource system (gold, manpower)
- Building construction with costs and effects
- Income calculation with modifier stacking
- Tax collection and treasury management
- Resource events (OnResourceChanged)

**Validated Architecture:**
- ResourceSystem (generic storage)
- ModifierSystem (bonuses/penalties)
- Command pattern (all state changes)
- Save/Load (full serialization)

---

## PILLAR 2: MILITARY (Next Priority)

### 2.1 Unit System (Week 1: 20-25 hours)

**ENGINE Components:**
- UnitSystem - Manages all units (NativeArray<UnitState>)
- UnitState - 8-byte struct (provinceID, countryID, type, strength, morale)
- UnitRegistry - Unit type definitions (infantry, cavalry, artillery)
- UnitCommands - CreateUnit, MoveUnit, DisbandUnit, MergeUnits

**GAME Components:**
- UnitDefinition - JSON5 definitions (cost, speed, combat stats)
- UnitType enum - Infantry, Cavalry, Artillery

**Data Structure:**
```csharp
// 8 bytes - fits in cache line
struct UnitState {
    ushort provinceID;     // Current location
    ushort countryID;      // Owner
    ushort unitTypeID;     // Infantry/Cavalry/etc
    byte strength;         // 0-100 (percentage)
    byte morale;           // 0-100
}
```

**Validation:**
- Create 10k units, verify <100ms creation time
- Move 1k units simultaneously, verify deterministic
- Save/Load with units, verify round-trip

### 2.2 Movement System (Week 2: 15-20 hours)

**ENGINE Components:**
- MovementSystem - Processes move orders each tick
- PathfindingCache - Pre-computed adjacency graph
- MovementQueue - Orders executed in deterministic order

**Movement Rules:**
- Units move 1 province per day (configurable by terrain/unit type)
- Multiple units can stack in same province
- Movement costs morale (long marches = tired troops)

**Pathfinding:**
- A* pathfinding using province adjacency
- Cached paths (recalculate only when provinces change owner)
- Terrain movement costs (plains fast, mountains slow)

**Validation:**
- 100 units moving across map, verify pathfinding <5ms
- Save/Load mid-movement, verify units continue correctly
- Deterministic movement (same seed = same paths)

### 2.3 Combat System (Week 3: 15-20 hours)

**ENGINE Components:**
- CombatSystem - Resolves battles each tick
- BattleResolver - Deterministic damage calculation
- CombatModifierTypes - Attack, Defense, Discipline, Morale

**Combat Resolution:**
- When opposing units in same province → battle
- Damage formula: (attacker strength × attack mods) vs (defender strength × defense mods)
- Morale damage → units retreat when morale breaks
- Casualties calculated with FixedPoint64 (deterministic)

**Modifiers Apply:**
- Terrain bonuses (defender in mountains +25% defense)
- Building bonuses (fort gives +50% defense)
- Tech bonuses (military tech gives +10% discipline)
- General traits (if we add them later)

**Validation:**
- 100 simultaneous battles, verify <10ms resolution
- Save/Load mid-battle, verify outcome unchanged
- Deterministic combat (same inputs = same casualties)

**Total Effort: 50-65 hours (Military Pillar Complete)**

---

## PILLAR 3: DIPLOMACY (Week 4-5: 30-40 hours)

### 3.1 Relations System (Week 4: 15-20 hours)

**ENGINE Components:**
- DiplomacySystem - Manages relations between countries
- RelationState - Opinion values (-200 to +200)
- RelationModifiers - Recent events affecting opinion
- OpinionCalculator - Computes total opinion from modifiers

**Relations Storage:**
```csharp
// Sparse storage: Dictionary<(ushort, ushort), RelationData>
struct RelationData {
    FixedPoint64 baseOpinion;
    List<OpinionModifier> modifiers; // "Stole province", "Alliance", etc
}
```

**Opinion Modifiers:**
- Base relations (cultural similarity, religion, etc.)
- Recent actions (declared war = -100, trade = +10)
- Time decay (modifiers fade over time)

**Validation:**
- 100 countries with relations, verify <5ms opinion calculation
- Save/Load relations, verify modifiers persist
- Deterministic opinion changes

### 3.2 Treaty System (Week 5: 15-20 hours)

**ENGINE Components:**
- TreatySystem - Active treaties between countries
- TreatyDefinition - Alliance, Trade Agreement, Non-Aggression Pact
- TreatyCommands - ProposeAlliance, AcceptTreaty, BreakTreaty

**Treaty Types:**
- Alliance (join defensive wars, opinion bonus)
- Trade Agreement (economic bonuses, opinion bonus)
- Non-Aggression Pact (cannot declare war, opinion bonus)
- Vassalization (future - subject pays tribute)

**Treaty Storage:**
```csharp
struct Treaty {
    ushort country1;
    ushort country2;
    ushort treatyType;
    int startTick;
    int expirationTick; // 0 = permanent
}
```

**Validation:**
- 50 active alliances, verify war declarations respect treaties
- Save/Load treaties, verify expiration dates correct
- Break treaty, verify opinion penalties apply

**Total Effort: 30-40 hours (Diplomacy Pillar Complete)**

---

## PILLAR 4: AI (Week 6-8: 40-60 hours)

### 4.1 AI Framework (Week 6: 15-20 hours)

**ENGINE Components:**
- AISystem - Manages AI decision-making for all AI countries
- AIGoal - Base class for goals (Conquer, Build Economy, Defend)
- AIEvaluator - Scores potential actions (build building, declare war, etc.)
- AIDecisionQueue - Orders AI decisions by priority

**AI Architecture:**
- **Goal-Oriented Action Planning (GOAP)** pattern
- Each AI country has active goals (prioritized list)
- Goals generate actions (build farm, recruit unit, declare war)
- Actions scored by evaluators (which action best achieves goal?)
- Top-scoring action executed each tick

**Example Goals:**
- Economic Goal: "Increase income to 100 gold/month"
- Military Goal: "Have 20 units on border with enemy"
- Expansion Goal: "Conquer 5 more provinces"

**Validation:**
- 20 AI countries making decisions, verify <10ms per tick
- AI builds buildings, recruits units, declares wars
- Deterministic AI (same seed = same decisions)

### 4.2 AI Evaluators (Week 7: 15-20 hours)

**Economic Evaluators:**
- BuildBuildingEvaluator - Which province should build what?
- TaxRateEvaluator - Should we raise/lower taxes?
- ResourcePriorityEvaluator - Spend gold on buildings or units?

**Military Evaluators:**
- RecruitUnitEvaluator - Where to recruit units?
- WarTargetEvaluator - Who should we attack? (weak neighbor with high value provinces)
- DefensePriorityEvaluator - Defend home or push offensive?

**Diplomatic Evaluators:**
- AllianceEvaluator - Who should we ally with? (strong neighbors, shared enemies)
- TreatyEvaluator - Accept/reject treaty proposals
- TrustEvaluator - Can we trust this ally?

**Scoring Formula:**
```csharp
// Example: BuildBuildingEvaluator
float score =
    (expectedIncomeIncrease * 10f) +  // Economic value
    (provinceImportance * 5f) +       // Strategic value
    (-buildTime * 0.1f) +             // Opportunity cost
    (urgency * 20f);                  // How badly we need this
```

**Validation:**
- AI makes sensible decisions (doesn't bankrupt itself)
- AI adapts to player actions (defends when attacked)
- AI difficulty scales (different evaluator weights)

### 4.3 AI Personality (Week 8: 10-20 hours)

**ENGINE Components:**
- AIPersonality - ScriptableObject defining AI behavior
- PersonalityTraits - Aggressive, Defensive, Economic, Diplomatic

**Personality Modifiers:**
- Aggressive: +50% war target scores, -50% peace scores
- Economic: +100% building scores, -50% military scores
- Diplomatic: +100% alliance scores, prefers treaties over war

**JSON5 Definitions:**
```json5
{
  id: "aggressive_expansionist",
  name: "Aggressive Expansionist",
  war_desire: 1.5,          // 150% war target scores
  building_desire: 0.5,     // 50% building scores
  alliance_threshold: -50   // Only allies with opinion > -50
}
```

**Validation:**
- Aggressive AI declares wars frequently
- Economic AI builds tall (many buildings, few wars)
- Diplomatic AI forms alliances, avoids conflicts

**Total Effort: 40-60 hours (AI Pillar Complete)**

---

## INTEGRATION & POLISH (Week 9: 15-20 hours)

### 9.1 All Pillars Working Together

**Scenarios:**
1. AI declares war → units move to border → battles resolve → winner gains provinces
2. Player builds economy → AI sees player growing → AI forms defensive alliance
3. Player attacks AI ally → AI ally joins war (treaty obligation)

**Validation:**
- 10 AI countries, all 4 pillars active, verify game runs 100 ticks
- Save/Load mid-war, verify battles continue correctly
- Deterministic full game (same seed = same outcome)

### 9.2 Performance Validation

**Targets:**
- 100 countries, 10k provinces, 5k units → 60 FPS
- AI decision-making → <10ms per country per tick
- Combat resolution → <20ms for 100 simultaneous battles
- Full tick (all systems) → <50ms

### 9.3 Save/Load All Systems

**Validation:**
- Save game with units mid-movement
- Save game mid-battle
- Save game with active treaties
- Save game with AI goals/evaluators
- Load all saves, verify everything continues correctly

---

## TIMELINE

| Week | Pillar | Feature | Hours | Status |
|------|--------|---------|-------|--------|
| 1 | Military | Unit System | 20-25 | 📋 Planned |
| 2 | Military | Movement System | 15-20 | 📋 Planned |
| 3 | Military | Combat System | 15-20 | 📋 Planned |
| 4 | Diplomacy | Relations System | 15-20 | 📋 Planned |
| 5 | Diplomacy | Treaty System | 15-20 | 📋 Planned |
| 6 | AI | AI Framework | 15-20 | 📋 Planned |
| 7 | AI | AI Evaluators | 15-20 | 📋 Planned |
| 8 | AI | AI Personality | 10-20 | 📋 Planned |
| 9 | Integration | All Pillars Together | 15-20 | 📋 Planned |

**Total Estimated Effort:** 130-175 hours (6-9 weeks at 20h/week)

---

## SUCCESS METRICS

**Military Pillar:**
- ✅ Can create 10k units in <100ms
- ✅ Can move 1k units simultaneously
- ✅ Combat resolves 100 battles in <20ms
- ✅ Deterministic (same seed = same battles)
- ✅ Save/Load with units mid-movement/battle

**Diplomacy Pillar:**
- ✅ 100 countries with relations in <5ms opinion calculation
- ✅ Treaties respected (allies join wars)
- ✅ Opinion modifiers decay over time
- ✅ Deterministic diplomatic actions
- ✅ Save/Load with active treaties

**AI Pillar:**
- ✅ 20 AI countries making decisions in <10ms
- ✅ AI builds economy, recruits units, declares wars
- ✅ AI personalities behave distinctly (aggressive vs economic)
- ✅ Deterministic AI (same seed = same decisions)
- ✅ Save/Load with AI goals/state

**Integration:**
- ✅ All 4 pillars working together
- ✅ 100 countries, 10k provinces, 5k units → 60 FPS
- ✅ Full game save/load works
- ✅ Deterministic full simulation

---

## ARCHITECTURE VALIDATION

This plan validates that Archon-Engine can handle:

**Scale:**
- ✅ 10k provinces (already validated)
- ✅ 100 countries (already validated)
- ✅ 5k-10k units (new)
- ✅ 100+ simultaneous battles (new)
- ✅ Complex AI decision-making for 20+ countries (new)

**Performance:**
- ✅ Sub-50ms ticks with all systems active
- ✅ Burst-compiled hot paths
- ✅ Zero-allocation loops
- ✅ Sparse collections for variable data (units, treaties)

**Determinism:**
- ✅ FixedPoint64 for all calculations
- ✅ Deterministic random (seeded)
- ✅ Deterministic command ordering
- ✅ Multiplayer-ready (command pattern)

**Persistence:**
- ✅ All systems serialize/deserialize
- ✅ Save/Load mid-simulation
- ✅ No state loss on round-trip

---

## NEXT IMMEDIATE STEPS

1. **Approve this plan** - Confirm priority: Military → Diplomacy → AI
2. **Create Unit System spec** - Detail UnitState struct, commands, validation
3. **Implement Week 1** - UnitSystem foundation
4. **Session log** - Document implementation (like previous refactor logs)

---

*Planning Document Created: 2025-10-19*
*Estimated Duration: 6-9 weeks (130-175 hours)*
*Priority: ENGINE validation - complete the four pillars*
