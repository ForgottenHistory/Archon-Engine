# Grand Strategy Game - AI Architecture

## Executive Summary
**Challenge**: AI for 200+ nations making strategic decisions without killing performance  
**Solution**: Hierarchical goal system with bucketed processing and shared calculations  
**Performance Target**: <10ms total AI processing per frame with 200+ active nations  
**Key Innovation**: AI "thinks" at different frequencies - strategic monthly, tactical daily, operational hourly

## Core Architecture Principles

### The Three-Layer AI Brain
```
STRATEGIC LAYER (Monthly/Yearly)
├── Long-term goals (form empire, dominate trade node)
├── Diplomatic strategy (who to ally, who to attack)
├── Economic focus (military, trade, development)
└── Updates: Every 30 days or when major events occur

TACTICAL LAYER (Weekly/Monthly)  
├── War planning (where to attack, what to siege)
├── Resource allocation (spend on army or buildings?)
├── Diplomatic actions (improve relations, claims)
└── Updates: Every 7 days or when situation changes

OPERATIONAL LAYER (Daily/Hourly)
├── Army movement (pathfinding, retreat)
├── Combat decisions (engage, withdraw)
├── Emergency responses (rebels, bankruptcy)
└── Updates: Every day or immediately for combat
```

## Performance-First Design

### The Bucketing Strategy

The key insight: Not all AI nations need to think at the same time. With 200 nations:
- Each day, only ~7 nations do strategic planning (200/30)
- Each hour, only ~8 nations do tactical updates (200/24)
- Only nations at war or in crisis do operational updates

**Note**: This AI bucketing strategy integrates with the [Time System Architecture](time-system-architecture.md)'s update frequency hierarchy. AI strategic layer aligns with Monthly updates, tactical with Weekly/Daily updates, and operational with Hourly updates.

```csharp
public class AIScheduler {
    private int dayCounter = 0;
    private int hourCounter = 0;
    
    public void DailyTick() {
        // Spread strategic updates across 30 days
        int strategicBucket = dayCounter % 30;
        int nationsPerBucket = nationCount / 30;
        
        for (int i = strategicBucket * nationsPerBucket; 
             i < (strategicBucket + 1) * nationsPerBucket; i++) {
            
            if (NeedsStrategicUpdate(i)) {
                UpdateStrategicAI(i);  // ~0.1ms per nation
            }
        }
        
        // Only war participants get daily operational updates
        foreach (var nation in nationsAtWar) {
            UpdateOperationalAI(nation);  // ~0.05ms per nation
        }
        
        dayCounter++;
    }
}
```

### Shared Calculations

Many AI decisions need the same expensive calculations. Calculate once, use many times:

```csharp
public class SharedAIData {
    // Calculated once per month, used by all AI
    public float[,] regionStrengths;      // Military power by region
    public float[,] tradeNodeValues;      // Value of each trade node
    public byte[,] diplomaticWeb;         // Who allies/rivals whom
    public float[] nationThreatLevels;    // How dangerous each nation is
    public ushort[] strategicProvinces;   // Important provinces to take
    
    // Update monthly or when major changes occur
    public void RecalculateMonthly() {
        CalculateRegionStrengths();      // ~5ms
        CalculateTradeValues();           // ~2ms
        CalculateThreatLevels();          // ~3ms
        UpdateDiplomaticWeb();            // ~1ms
        // Total: ~11ms, once per month
    }
}
```

## Goal-Oriented Architecture

### AI Personality & Goals

Each nation has persistent goals that drive all decisions:

```csharp
public struct AIPersonality {
    // Base personality (set at game start)
    public float aggression;        // 0-1, how likely to start wars
    public float tradeFocus;        // 0-1, prioritize trade over military
    public float diplomaticFocus;   // 0-1, seek allies vs go alone
    public float riskTolerance;     // 0-1, conservative vs ambitious
    
    // Modified by government, religion, national ideas
    public float colonialDesire;
    public float religiousZeal;
    public float economicFocus;
}

public class AIGoal {
    public GoalType type;
    public int priority;          // 0-1000
    public uint targetId;         // Province, nation, or trade node
    public float progress;        // 0-1 completion
    public float desireScore;     // How much AI wants this
    
    public abstract float Evaluate(NationState nation);
    public abstract ActionPlan GeneratePlan(NationState nation);
}
```

### Goal Types & Evaluation

```csharp
public enum GoalType {
    // Expansion Goals
    ConquerProvince,      // Want specific province
    ConquerRegion,        // Want entire region
    FormNation,           // Form Spain, Germany, etc.
    
    // Economic Goals  
    DominateTradeNode,    // Control Venice trade
    DevelopProvince,      // Build up capital
    BuildManufactory,     // Industrialize
    
    // Diplomatic Goals
    FormAlliance,         // Ally with France
    BreakAlliance,        // Split rivals
    GetRival,             // Declare rivalry
    
    // Internal Goals
    ConvertReligion,      // Religious unity
    StabilizeCountry,     // Reduce unrest
    BuildArmy,            // Reach force limit
}

// Example: Conquer Province Goal
public class ConquerProvinceGoal : AIGoal {
    public override float Evaluate(NationState nation) {
        float score = 0;
        
        // Base desire from proximity and value
        score += GetProvinceValue(targetId) * 10f;
        score += GetProximityScore(nation, targetId) * 5f;
        
        // Personality modifiers
        score *= (1f + nation.ai.personality.aggression);
        
        // Situational modifiers
        if (HasClaim(nation, targetId)) score *= 1.5f;
        if (IsCoreProvince(nation, targetId)) score *= 2f;
        if (IsHolyLand(nation, targetId)) score *= 1.5f;
        
        // Difficulty modifiers
        float difficulty = GetConquestDifficulty(nation, targetId);
        score /= (1f + difficulty);
        
        return score;
    }
}
```

## Decision Making System

### The Decision Pipeline

AI makes decisions in priority order, spending "decision points" (representing attention/resources):

```csharp
public class AIDecisionMaker {
    private const int DECISION_POINTS_PER_UPDATE = 100;
    
    public void MakeDecisions(NationAI ai) {
        int pointsRemaining = DECISION_POINTS_PER_UPDATE;
        
        // Sort goals by priority
        ai.goals.Sort((a, b) => b.desireScore.CompareTo(a.desireScore));
        
        foreach (var goal in ai.goals) {
            if (pointsRemaining <= 0) break;
            
            // Generate possible actions for this goal
            var actions = goal.GeneratePossibleActions();
            
            foreach (var action in actions) {
                if (action.cost > pointsRemaining) continue;
                
                if (ShouldTakeAction(ai, action)) {
                    ExecuteAction(action);
                    pointsRemaining -= action.cost;
                }
            }
        }
    }
}
```

### Action Types & Costs

```csharp
public struct AIAction {
    public ActionType type;
    public uint targetId;
    public int cost;          // Decision points required
    public float value;       // Expected benefit
    
    // Common action costs
    public static class Costs {
        public const int DeclareWar = 50;
        public const int BuildBuilding = 10;
        public const int MoveArmy = 5;
        public const int FormAlliance = 20;
        public const int FabricateClaim = 15;
        public const int DevelopProvince = 10;
    }
}
```

## Military AI

### Strategic War Planning

AI evaluates wars strategically before committing:

```csharp
public class WarPlanner {
    public struct WarPlan {
        public byte target;
        public byte[] allies;
        public byte[] enemies;
        public float winProbability;
        public float expectedGains;
        public ushort[] targetProvinces;
        public int expectedDuration;
    }
    
    public WarPlan EvaluateWar(byte nation, byte target) {
        var plan = new WarPlan { target = target };
        
        // Calculate military strengths
        float ourStrength = CalculateMilitaryPower(nation);
        float theirStrength = CalculateMilitaryPower(target);
        
        // Add allies to calculation
        foreach (var ally in GetLikelyAllies(nation)) {
            ourStrength += CalculateMilitaryPower(ally) * 0.8f; // Allies less reliable
            plan.allies.Add(ally);
        }
        
        foreach (var enemy in GetLikelyAllies(target)) {
            theirStrength += CalculateMilitaryPower(enemy) * 0.8f;
            plan.enemies.Add(enemy);
        }
        
        // Calculate win probability
        plan.winProbability = ourStrength / (ourStrength + theirStrength);
        
        // Identify target provinces
        plan.targetProvinces = SelectWarGoals(nation, target);
        
        // Expected duration based on force ratio
        plan.expectedDuration = CalculateWarDuration(ourStrength / theirStrength);
        
        return plan;
    }
}
```

### Tactical Army Control

During war, AI controls armies with simple but effective rules:

```csharp
public class ArmyAI {
    public enum ArmyRole {
        MainForce,      // Seek battles
        SiegeForce,     // Siege provinces
        Defender,       // Defend homeland
        Raider,         // Carpet siege
        Support         // Reinforce others
    }
    
    public struct ArmyOrder {
        public ArmyRole role;
        public ushort targetProvince;
        public byte targetArmy;
        public float priority;
    }
    
    public void UpdateArmyOrders(NationAI ai) {
        foreach (var army in ai.armies) {
            // Emergency overrides
            if (army.morale < 0.3f) {
                OrderRetreat(army);
                continue;
            }
            
            if (NearbyHostileArmy(army) && ShouldEngage(army)) {
                OrderEngagement(army);
                continue;
            }
            
            // Follow role assignment
            switch (army.order.role) {
                case ArmyRole.MainForce:
                    SeekDecisiveBattle(army);
                    break;
                    
                case ArmyRole.SiegeForce:
                    ContinueSiegeOperations(army);
                    break;
                    
                case ArmyRole.Defender:
                    DefendKeyProvinces(army);
                    break;
            }
        }
    }
}
```

## Economic AI

### Budget Allocation

AI divides income between competing needs:

```csharp
public class EconomicAI {
    public struct Budget {
        public float armyMaintenance;
        public float navyMaintenance;
        public float fortMaintenance;
        public float advisors;
        public float buildings;
        public float development;
        public float savings;
    }
    
    public Budget AllocateBudget(NationAI ai) {
        float income = GetMonthlyIncome(ai.nation);
        var budget = new Budget();
        
        // Fixed costs first
        budget.armyMaintenance = GetArmyMaintenanceCost(ai.nation);
        budget.fortMaintenance = GetFortMaintenanceCost(ai.nation);
        
        float remaining = income - budget.armyMaintenance - budget.fortMaintenance;
        
        // Allocate based on situation and personality
        if (ai.IsAtWar()) {
            budget.armyMaintenance *= 1.5f;  // Full maintenance
            budget.savings = remaining * 0.8f;  // Save for war
        }
        else if (ai.personality.economicFocus > 0.7f) {
            budget.buildings = remaining * 0.4f;
            budget.development = remaining * 0.3f;
            budget.advisors = remaining * 0.2f;
            budget.savings = remaining * 0.1f;
        }
        else {
            // Balanced approach
            budget.buildings = remaining * 0.25f;
            budget.advisors = remaining * 0.25f;
            budget.savings = remaining * 0.5f;
        }
        
        return budget;
    }
}
```

### Building Priorities

```csharp
public class BuildingAI {
    public struct BuildingScore {
        public ushort province;
        public BuildingType building;
        public float score;
        public float roi;  // Return on investment in years
    }
    
    public void SelectBuildings(NationAI ai) {
        var scores = new List<BuildingScore>();
        
        foreach (var province in ai.ownedProvinces) {
            foreach (var building in GetAvailableBuildings(province)) {
                var score = EvaluateBuilding(province, building, ai);
                scores.Add(score);
            }
        }
        
        // Sort by score
        scores.Sort((a, b) => b.score.CompareTo(a.score));
        
        // Build until out of money
        float treasury = GetTreasury(ai.nation);
        foreach (var score in scores) {
            float cost = GetBuildingCost(score.building);
            if (cost > treasury) break;
            
            if (score.roi < 50) {  // Less than 50 years ROI
                QueueBuilding(score.province, score.building);
                treasury -= cost;
            }
        }
    }
    
    private float EvaluateBuilding(ushort province, BuildingType type, NationAI ai) {
        float score = 0;
        
        // Base economic value
        float yearlyIncome = GetBuildingIncome(type, province);
        float cost = GetBuildingCost(type);
        float roi = cost / yearlyIncome;
        
        score = 100f / roi;  // Better ROI = higher score
        
        // Personality adjustments
        if (type == BuildingType.Barracks) {
            score *= (1f + ai.personality.aggression);
        }
        else if (type == BuildingType.Marketplace) {
            score *= (1f + ai.personality.tradeFocus);
        }
        
        // Strategic adjustments
        if (IsBorderProvince(province)) {
            if (type == BuildingType.Fort) score *= 2f;
        }
        
        return score;
    }
}
```

## Diplomatic AI

### Relationship Management

```csharp
public class DiplomaticAI {
    public struct RelationshipGoal {
        public byte targetNation;
        public RelationType desiredRelation;  // Allied, Neutral, Hostile
        public float currentOpinion;
        public float targetOpinion;
        public int priority;
    }
    
    public void UpdateDiplomacy(NationAI ai) {
        // Categorize all nations
        foreach (byte other in allNations) {
            if (other == ai.nation) continue;
            
            float threat = EvaluateThreat(ai.nation, other);
            float opportunity = EvaluateOpportunity(ai.nation, other);
            
            if (threat > 0.7f) {
                // Major threat - seek allies against them
                ai.relationshipGoals[other] = RelationType.Hostile;
                SeekAlliesAgainst(ai, other);
            }
            else if (opportunity > 0.7f && threat < 0.3f) {
                // Good ally candidate
                ai.relationshipGoals[other] = RelationType.Allied;
                ImproveRelations(ai, other);
            }
            else {
                // Neutral - don't waste diplomatic resources
                ai.relationshipGoals[other] = RelationType.Neutral;
            }
        }
    }
    
    private float EvaluateThreat(byte us, byte them) {
        float threat = 0;
        
        // Military threat
        float powerRatio = GetMilitaryPower(them) / GetMilitaryPower(us);
        threat += Mathf.Clamp01(powerRatio - 1f) * 0.4f;
        
        // Proximity threat
        float distance = GetBorderDistance(us, them);
        threat += (10f - distance) / 10f * 0.3f;
        
        // Historical threat (have they attacked us?)
        if (HasRecentWar(us, them)) threat += 0.2f;
        
        // Aggressive expansion
        threat += GetAggressiveExpansion(them) / 100f * 0.1f;
        
        return threat;
    }
}
```

## AI Optimization Techniques

### Caching & Memoization

```csharp
public class AICache {
    // Expensive calculations cached
    private Dictionary<uint, float> pathDistanceCache;
    private Dictionary<uint, float> provinceValueCache;
    private Dictionary<ulong, float> warEvaluationCache;
    
    private uint cacheVersion = 0;  // Invalidate when major changes occur
    
    public float GetPathDistance(ushort from, ushort to) {
        uint key = ((uint)from << 16) | to;
        
        if (!pathDistanceCache.TryGetValue(key, out float distance)) {
            distance = CalculatePathDistance(from, to);
            pathDistanceCache[key] = distance;
        }
        
        return distance;
    }
    
    public void InvalidateCache() {
        cacheVersion++;
        // Clear caches that are invalidated
        pathDistanceCache.Clear();
        warEvaluationCache.Clear();
        // Keep some caches that change rarely
    }
}
```

### Decision Pruning

```csharp
public class DecisionPruner {
    // Don't evaluate obviously bad decisions
    public bool ShouldConsider(AIAction action, NationAI ai) {
        switch (action.type) {
            case ActionType.DeclareWar:
                // Don't even consider if:
                if (GetTreasury(ai.nation) < 0) return false;  // Bankrupt
                if (GetManpower(ai.nation) < 1000) return false;  // No manpower
                if (GetWarExhaustion(ai.nation) > 15) return false;  // Exhausted
                if (CountCurrentWars(ai.nation) >= 2) return false;  // Too many wars
                break;
                
            case ActionType.BuildBuilding:
                if (GetTreasury(ai.nation) < action.cost * 2) return false;  // Need buffer
                break;
                
            case ActionType.FormAlliance:
                if (GetDiplomaticRelations(ai.nation) >= GetMaxRelations(ai.nation)) return false;
                break;
        }
        
        return true;
    }
}
```

### Parallel Processing

```csharp
public class ParallelAI {
    // Process independent AI decisions in parallel
    public void ProcessAllAI() {
        // Group nations that won't interact this frame
        var independentGroups = GroupNonInteracting(allNations);
        
        Parallel.ForEach(independentGroups, group => {
            foreach (var nation in group) {
                // Safe to process in parallel - no shared state
                ProcessStrategicAI(nation);
            }
        });
    }
    
    private List<List<byte>> GroupNonInteracting(byte[] nations) {
        // Nations at war with each other can't be in same group
        // Nations in same trade node can't be in same group
        // Otherwise safe to process in parallel
        var groups = new List<List<byte>>();
        // ... grouping logic
        return groups;
    }
}
```

## Performance Metrics

### Processing Time Budget
```
Per Frame (60 FPS = 16.67ms total):
├── Strategic AI: 2ms (6-7 nations × 0.3ms)
├── Tactical AI: 1ms (8 nations × 0.125ms)
├── Operational AI: 2ms (20 war nations × 0.1ms)
├── Shared calculations: 0.5ms (amortized)
└── Total AI Budget: 5.5ms (~33% of frame)

Monthly Full Recalculation:
├── All strategic goals: 30ms (spread over 30 days = 1ms/day)
├── Diplomatic web: 10ms (once per month)
├── Trade evaluation: 15ms (once per month)
└── Averaged per frame: <2ms
```

### Memory Usage
```
Per Nation AI:
├── Goals (10 × 32 bytes): 320 bytes
├── Personality: 32 bytes
├── Cached decisions: ~1KB
├── Relationship matrix: 256 bytes
└── Total: ~2KB per nation

Global AI Data:
├── Shared calculations: ~100KB
├── Decision cache: ~500KB
├── Path cache: ~200KB
└── Total: <1MB for 256 nations
```

## Difficulty Scaling

### AI Bonuses by Difficulty
```csharp
public struct DifficultyModifiers {
    // Easy (AI is handicapped)
    public static readonly DifficultyModifiers Easy = new() {
        decisionPointsMultiplier = 0.7f,   // Fewer decisions
        incomeMultiplier = 0.9f,            // Less money
        moraleMultiplier = 0.9f,            // Weaker armies
        aggressionMultiplier = 0.5f         // Less aggressive
    };
    
    // Normal (Fair play)
    public static readonly DifficultyModifiers Normal = new() {
        decisionPointsMultiplier = 1.0f,
        incomeMultiplier = 1.0f,
        moraleMultiplier = 1.0f,
        aggressionMultiplier = 1.0f
    };
    
    // Hard (AI gets help)
    public static readonly DifficultyModifiers Hard = new() {
        decisionPointsMultiplier = 1.3f,   // More decisions
        incomeMultiplier = 1.1f,            // Bonus income
        moraleMultiplier = 1.1f,            // Stronger armies
        aggressionMultiplier = 1.2f,        // More aggressive
        perfectInformation = true           // Knows player strength exactly
    };
}
```

## AI Personality Examples

### The Merchant
```
aggression: 0.2
tradeFocus: 0.9
diplomaticFocus: 0.7
riskTolerance: 0.3

Behavior: Avoids wars, focuses on trade nodes, builds marketplaces, 
seeks trade agreements, maintains large navy for trade protection
```

### The Conqueror
```
aggression: 0.9
tradeFocus: 0.3
diplomaticFocus: 0.4
riskTolerance: 0.8

Behavior: Constantly at war, prioritizes military buildings,
breaks alliances for expansion, high risk tolerance
```

### The Diplomat
```
aggression: 0.4
tradeFocus: 0.5
diplomaticFocus: 0.9
riskTolerance: 0.4

Behavior: Builds alliance networks, uses allies for wars,
focuses on defensive play, expands through diplomacy
```

## Debugging & Tuning Tools

### AI Inspector
```csharp
#if UNITY_EDITOR
public class AIDebugger {
    // Visualize AI thinking
    public void DrawAIDebug(NationAI ai) {
        // Show current goals
        foreach (var goal in ai.goals) {
            DominionLogger.Log($"Goal: {goal.type} Target: {goal.targetId} Score: {goal.desireScore}");
        }
        
        // Show decision process
        DominionLogger.Log($"Decision points remaining: {ai.decisionPoints}");
        DominionLogger.Log($"Treasury: {GetTreasury(ai.nation)}");
        DominionLogger.Log($"Current action: {ai.currentAction}");
        
        // Visualize on map
        HighlightTargetProvinces(ai);
        DrawPlannedPaths(ai);
        ShowThreatMap(ai);
    }
}
#endif
```

## Best Practices

1. **Bucket everything** - Spread AI processing across frames
2. **Share expensive calculations** - Calculate once, use many times  
3. **Cache aggressively** - Path distances, evaluations, etc.
4. **Prune early** - Don't evaluate obviously bad decisions
5. **Think at appropriate frequency** - Strategic = monthly, tactical = daily
6. **Keep goals persistent** - Don't recalculate everything every time
7. **Use simple heuristics** - Perfect is the enemy of good enough
8. **Make AI explainable** - Players should understand why AI did something

## Summary

This AI architecture achieves:
- **200+ AI nations** thinking independently
- **<10ms per frame** total AI processing
- **Strategic depth** through goal-oriented behavior
- **Personality variety** making each nation feel different
- **Performance scalability** through bucketing and caching
- **Maintainable code** through clear separation of concerns

The key insight: AI doesn't need to be smart every frame - it needs to be smart enough, often enough, without killing performance.