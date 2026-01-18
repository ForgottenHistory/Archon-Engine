# AI System

The AI system uses Goal-Oriented Action Planning (GOAP) with tier-based scheduling. AI countries evaluate goals, select the best one, and execute actions using the same Command pattern as players.

## Architecture Overview

```
ENGINE Layer (Core.AI)          GAME Layer (Your Game)
├── AISystem                    ├── Custom Goals
├── AIGoal (abstract)          │   ├── BuildEconomyGoal
├── AIScheduler                │   ├── ExpandTerritoryGoal
├── AIGoalRegistry             │   └── DefendGoal
├── AIDistanceCalculator       ├── Custom Constraints
└── IGoalConstraint            └── Scheduling Config
```

**Key Principles:**
- ENGINE provides mechanisms (scheduling, goal evaluation, constraints)
- GAME provides policy (which goals, formulas, thresholds)
- AI uses same Commands as player (deterministic, network-safe)
- Zero allocations during gameplay (pre-allocated buffers)

## Creating Goals

Goals extend `AIGoal` and implement three methods:

```csharp
using Core.AI;
using Core.Data;

[Goal("expand_territory")]  // Auto-discovered via reflection
public class ExpandTerritoryGoal : AIGoal
{
    public override string GoalName => "Expand Territory";

    // Score how desirable this goal is (0-1000)
    public override FixedPoint64 Evaluate(ushort countryID, GameState gameState)
    {
        int ownedProvinces = gameState.Provinces.GetProvinceCountForCountry(countryID);

        // More urgent when we have few provinces
        if (ownedProvinces < 5)
            return FixedPoint64.FromInt(800);  // Critical priority
        if (ownedProvinces < 10)
            return FixedPoint64.FromInt(500);  // High priority

        return FixedPoint64.FromInt(200);      // Normal priority
    }

    // Perform actions to achieve this goal
    public override void Execute(ushort countryID, GameState gameState)
    {
        // Find target province, issue colonize/attack command
        var target = FindBestExpansionTarget(countryID, gameState);
        if (target.HasValue)
        {
            gameState.ExecuteCommand(new ColonizeCommand(countryID, target.Value));
        }
    }
}
```

### Score Ranges

| Range | Priority | Use Case |
|-------|----------|----------|
| 800-1000 | Critical | Bankruptcy, being invaded |
| 500-799 | High | Major opportunities, strategic goals |
| 200-499 | Medium | Normal priorities |
| 1-199 | Low | Nice-to-have improvements |
| 0 | Skip | Impossible or undesirable |

## Constraints

Constraints filter when a goal applies. If any constraint fails, the goal is skipped.

### Built-in Constraints

```csharp
// Must have minimum provinces
AddConstraint(new MinProvincesConstraint(3));

// Must have minimum gold
AddConstraint(new MinResourceConstraint(goldResourceID, FixedPoint64.FromInt(100), "Gold"));

// Must be at war
AddConstraint(new AtWarConstraint(mustBeAtWar: true));

// Must be at peace
AddConstraint(new AtWarConstraint(mustBeAtWar: false));

// Must be at war with specific country
AddConstraint(new AtWarWithConstraint(targetCountryID: 5, mustBeAtWar: true));
```

### Custom Constraints

```csharp
public class HasNavyConstraint : IGoalConstraint
{
    public string Name => "HasNavy";

    public bool IsSatisfied(ushort countryID, GameState gameState)
    {
        var units = gameState.GetComponent<UnitSystem>();
        return units.GetNavalUnitCount(countryID) > 0;
    }
}

// In your goal:
AddConstraint(new HasNavyConstraint());
```

### Delegate Constraints (Quick One-offs)

```csharp
AddConstraint(new DelegateConstraint(
    "HasCoastalProvince",
    (countryID, gameState) => gameState.Provinces.HasCoastalProvince(countryID)
));
```

## Tier-Based Scheduling

AI countries are assigned tiers based on distance from the player. Near countries are processed more frequently.

| Tier | Distance | Interval | Use Case |
|------|----------|----------|----------|
| 0 | Neighbors | Every hour | Direct threats/opportunities |
| 1 | Near | Every 6 hours | Regional powers |
| 2 | Medium | Every 24 hours | Distant countries |
| 3 | Far | Every 72 hours | Remote countries |

### Custom Scheduling Config

```csharp
var config = new AISchedulingConfig(
    tierCount: 4,
    tierIntervals: new ushort[] { 1, 6, 24, 72 },
    tierDistanceThresholds: new int[] { 2, 5, 10, int.MaxValue }
);
aiSystem.SetSchedulingConfig(config);
```

## Registering Goals

### Manual Registration

```csharp
// In your game initialization
aiSystem.RegisterGoal(new BuildEconomyGoal());
aiSystem.RegisterGoal(new ExpandTerritoryGoal());
aiSystem.RegisterGoal(new DefendGoal());
```

### Auto-Discovery (Recommended)

Goals with `[Goal]` attribute are discovered automatically:

```csharp
[Goal("build_economy")]
public class BuildEconomyGoal : AIGoal { ... }

// During init:
AIGoalDiscovery.RegisterAll(aiSystem);
```

## Full Setup Example

```csharp
public class MyGameInitializer
{
    public void InitializeAI(GameState gameState, ushort playerCountryID)
    {
        var aiSystem = new AISystem(gameState);

        // 1. Initialize (creates registry)
        aiSystem.Initialize();

        // 2. Register goals
        aiSystem.RegisterGoal(new BuildEconomyGoal());
        aiSystem.RegisterGoal(new ExpandTerritoryGoal());
        aiSystem.RegisterGoal(new DefendGoal());

        // 3. Custom scheduling (optional)
        aiSystem.SetSchedulingConfig(AISchedulingConfig.CreateDefault());

        // 4. Initialize country AI (after goals registered)
        int countryCount = gameState.GetComponent<CountrySystem>().CountryCount;
        int provinceCount = gameState.Provinces.ProvinceCount;
        aiSystem.InitializeCountryAI(countryCount, provinceCount, playerCountryID);

        // 5. Disable AI for player
        aiSystem.SetAIEnabled(playerCountryID, false);

        // 6. Subscribe to hourly tick
        gameState.EventBus.Subscribe<HourlyTickEvent>(evt => {
            aiSystem.ProcessHourlyAI(evt.Month, evt.Day, evt.Hour);
        });
    }
}
```

## StarterKit Simple AI

StarterKit includes a simpler AI for demonstration:

```csharp
// StarterKit.AISystem - Simple monthly-tick AI
// Priority: Colonize first (expand), then build farms (develop)

var aiSystem = new StarterKit.AISystem(
    gameState,
    playerState,
    buildingSystem,
    economySystem
);

// Uses ProvinceQueryBuilder to find targets
using var candidates = new ProvinceQueryBuilder(provinceSystem, adjacencies)
    .BorderingCountry(countryId)  // Adjacent to our provinces
    .IsUnowned()                   // Not owned by anyone
    .Execute(Allocator.Temp);
```

## Debugging

### Get AI State

```csharp
var state = aiSystem.GetAIState(countryID);
Debug.Log($"Tier: {state.tier}, Active: {state.IsActive}");

var goal = aiSystem.GetActiveGoal(countryID);
Debug.Log($"Current Goal: {goal?.GoalName ?? "None"}");
```

### Debug Info

```csharp
var info = aiSystem.GetDebugInfo(countryID, currentHourOfYear);
Debug.Log($"Country {info.CountryID}:");
Debug.Log($"  Tier: {info.Tier}");
Debug.Log($"  Active Goal: {info.ActiveGoalName}");
Debug.Log($"  Hours Since Processed: {info.HoursSinceProcessed}");
```

### Failed Constraints

```csharp
var goal = new ExpandTerritoryGoal();
var failed = goal.GetFailedConstraints(countryID, gameState);
foreach (var constraint in failed)
{
    Debug.Log($"Failed: {constraint}");
}
```

### Statistics

```csharp
var stats = aiSystem.GetStatistics();
Debug.Log($"Total Processed: {stats.TotalProcessed}");
Debug.Log($"Timeouts: {stats.TimeoutCount}");
Debug.Log($"Avg Processing Time: {stats.AverageProcessingTimeMs}ms");
```

## Performance Tips

1. **Return 0 early** - If goal is impossible, return score 0 immediately
2. **Use constraints** - Cheap to check, skips evaluation entirely
3. **Pre-allocate buffers** - Initialize() allocates, Execute() reuses
4. **Simple heuristics** - Quick approximations beat perfect calculations
5. **Tier configuration** - Process far AI less frequently

## API Reference

- [AISystem](~/api/Core.AI.AISystem.html) - Main AI coordinator
- [AIGoal](~/api/Core.AI.AIGoal.html) - Abstract goal base class
- [AIScheduler](~/api/Core.AI.AIScheduler.html) - Tier-based processing
- [IGoalConstraint](~/api/Core.AI.IGoalConstraint.html) - Constraint interface
- [AISchedulingConfig](~/api/Core.AI.AISchedulingConfig.html) - Tier configuration
