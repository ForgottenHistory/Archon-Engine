# AISystem Improvements
**Date**: 2026-01-14
**Session**: 11
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Enhance AISystem with IGoalSelector interface, goal constraints, query methods, timeout, and debug support

**Success Criteria:**
- Custom goal selection strategies possible
- Declarative constraints for goals
- Query methods for active goals and tier info
- Execution timeout for runaway goals
- Statistics and debug info

---

## What We Did

### 1. IGoalSelector Interface

**File:** `Core/AI/IGoalSelector.cs`

Allows GAME layer to customize how goals are selected (not just highest score).

**Components:**
- `IGoalSelector` interface with `SelectGoal()` method
- `GoalEvaluation` struct (goal + score pair)
- `HighestScoreSelector` default implementation

**Use Cases:**
- Weighted random selection (personality variance)
- Goal cooldowns (don't repeat same goal)
- Priority overrides (emergency goals always win)

### 2. Goal Constraints System

**File:** `Core/AI/GoalConstraint.cs`

Declarative preconditions for goals. If any constraint fails, goal is skipped.

**Built-in Constraints:**
- `MinProvincesConstraint` - "only if country has >N provinces"
- `MinResourceConstraint` - "only if has resource amount"
- `AtWarConstraint` - "must be at war / at peace"
- `AtWarWithConstraint` - "must be at war with specific country"
- `DelegateConstraint` - custom one-off constraints

**Benefits:**
- Self-documenting (declarative)
- Debuggable (`GetFailedConstraints()` lists why skipped)
- Reusable across multiple goals

### 3. AIGoal Constraint Support

**File:** `Core/AI/AIGoal.cs`

Added constraint support to base class:
- `protected List<IGoalConstraint> constraints`
- `AddConstraint(IGoalConstraint)` - add during Initialize
- `CheckConstraints(countryID, gameState)` - returns true if all pass
- `GetFailedConstraints(countryID, gameState)` - for debugging
- `Constraints` property - readonly list for UI

### 4. AIStatistics & Debug

**File:** `Core/AI/AIStatistics.cs`

**AIStatistics:**
- `TotalProcessed`, `TotalSkipped`, `TotalTimeouts`
- `AverageProcessingTimeMs`
- `GetProcessedByTier(tier)`
- `GetSummary()` for debug output

**AIDebugInfo struct:**
- CountryID, Tier, IsActive, ActiveGoalID, ActiveGoalName
- LastProcessedHour, HoursSinceProcessed
- FailedConstraints array

### 5. AIScheduler Updates

**File:** `Core/AI/AIScheduler.cs`

- Added `IGoalSelector` integration
- Constraint checking before evaluation
- Pre-allocated `evaluationBuffer` (zero allocation)
- Execution timeout with `Stopwatch`
- Statistics recording per AI tick

### 6. AISystem Query Methods

**File:** `Core/AI/AISystem.cs`

**New Methods:**
```csharp
AIGoal GetActiveGoal(ushort countryID);
List<ushort> GetCountriesByTier(byte tier);
int GetCountryCountByTier(byte tier);
int GetActiveAICount();
void SetGoalSelector(IGoalSelector selector);
void SetExecutionTimeout(long timeoutMs);
AIDebugInfo GetDebugInfo(ushort countryID, int currentHourOfYear);
AIStatistics GetStatistics();
void ResetStatistics();
```

### 7. Core System Gaps Fixed

**GameState.cs:**
- Added `Diplomacy` property (was missing)

**ProvinceSystem.cs / ProvinceDataManager.cs:**
- Added `GetProvinceCountForCountry(ushort countryId)` - O(n) count, no allocation

**DiplomacySystem.cs / DiplomacyWarManager.cs:**
- Added `IsAtWar(ushort countryID)` - check if at war with anyone
- Added `HasAnyWar()` helper - O(1) using warsByCountry index

---

## Files Changed

| File | Changes |
|------|---------|
| `Core/AI/IGoalSelector.cs` | NEW - interface + HighestScoreSelector |
| `Core/AI/GoalConstraint.cs` | NEW - IGoalConstraint + built-in constraints |
| `Core/AI/AIStatistics.cs` | NEW - statistics + AIDebugInfo |
| `Core/AI/AIGoal.cs` | Added constraint support |
| `Core/AI/AIScheduler.cs` | IGoalSelector, constraints, timeout, stats |
| `Core/AI/AISystem.cs` | Query methods, debug support |
| `Core/GameState.cs` | Added Diplomacy property |
| `Core/Systems/ProvinceSystem.cs` | Added GetProvinceCountForCountry |
| `Core/Systems/Province/ProvinceDataManager.cs` | Added GetProvinceCountForCountry |
| `Core/Diplomacy/DiplomacySystem.cs` | Added IsAtWar(countryID) overload |
| `Core/Diplomacy/DiplomacyWarManager.cs` | Added HasAnyWar |

---

## Quick Reference for Future Claude

**Custom Goal Selector:**
```csharp
public class PersonalitySelector : IGoalSelector
{
    public AIGoal SelectGoal(ushort countryID, IReadOnlyList<GoalEvaluation> goals,
                             GameState gs, AIState state)
    {
        // Weighted random based on personality
        return goals[WeightedRandom(goals)].Goal;
    }
}
aiSystem.SetGoalSelector(new PersonalitySelector());
```

**Goal with Constraints:**
```csharp
public class ExpansionGoal : AIGoal
{
    public override void Initialize(EventBus eventBus)
    {
        AddConstraint(new MinProvincesConstraint(3));
        AddConstraint(new AtWarConstraint(mustBeAtWar: false));
        AddConstraint(new MinResourceConstraint(goldId, FixedPoint64.FromInt(500), "Gold"));
    }
}
```

**Debug AI State:**
```csharp
var info = aiSystem.GetDebugInfo(countryID, currentHour);
Debug.Log($"Country {info.CountryID}: Tier {info.Tier}, Goal: {info.ActiveGoalName}");

var stats = aiSystem.GetStatistics();
Debug.Log(stats.GetSummary());
```

---

## Links & References

### Related Sessions
- [Previous: PathfindingSystem Improvements](10-pathfinding-system-improvements.md)
- [CORE Namespace Improvements](9-core-namespace-improvements.md)

### Code References
- IGoalSelector: `Core/AI/IGoalSelector.cs`
- GoalConstraint: `Core/AI/GoalConstraint.cs`
- AIStatistics: `Core/AI/AIStatistics.cs`
- AISystem queries: `Core/AI/AISystem.cs:255-407`

### Planning
- [CORE Improvements Roadmap](../../Planning/core-namespace-improvements.md)

---

*AISystem enhanced with IGoalSelector for custom goal selection, declarative GoalConstraint system, query methods, execution timeout, and statistics. Also fixed gaps in GameState.Diplomacy, ProvinceSystem.GetProvinceCountForCountry, and DiplomacySystem.IsAtWar.*
