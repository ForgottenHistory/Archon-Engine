# Economy System

The Economy system tracks resources (gold) for all countries and collects income monthly. It demonstrates key Archon patterns: FixedPoint64 for determinism, EventBus for updates, and dirty-flag caching.

## Basic Setup

```csharp
public class MyInitializer : MonoBehaviour
{
    private EconomySystem economySystem;

    IEnumerator Start()
    {
        // Wait for ENGINE
        while (!ArchonEngine.Instance.IsInitialized)
            yield return null;

        var gameState = ArchonEngine.Instance.GameState;
        var playerState = new PlayerState(gameState);
        var modifierSystem = new ModifierSystem();

        // Create economy system
        economySystem = new EconomySystem(gameState, playerState, modifierSystem);
    }
}
```

## Core Concepts

### FixedPoint64 for Determinism

**Never use `float` for economy calculations.** Different CPUs produce different float results, breaking multiplayer sync.

```csharp
// ❌ WRONG - Float breaks multiplayer
float income = provinces * 1.5f;

// ✅ CORRECT - FixedPoint64 is deterministic
FixedPoint64 income = provinceCount * FixedPoint64.FromFraction(3, 2); // 1.5
```

### Monthly Income Collection

EconomySystem subscribes to `MonthlyTickEvent` and collects income automatically:

```csharp
public EconomySystem(GameState gameState, ...)
{
    // Subscribe to monthly tick
    subscriptions.Add(
        gameState.EventBus.Subscribe<MonthlyTickEvent>(OnMonthlyTick)
    );
}

private void OnMonthlyTick(MonthlyTickEvent evt)
{
    CollectIncomeForAllCountries();
}
```

### Income Formula

The StarterKit uses this formula per province:

```
baseIncome = 1 gold
localModified = (baseIncome + additiveBonus) * (1 + localModifier)
finalIncome = localModified * (1 + countryModifier)
```

- **additiveBonus** - Flat bonus from buildings (e.g., market +1 gold)
- **localModifier** - Percentage bonus from province buildings
- **countryModifier** - Percentage bonus from country-wide effects

## API Reference

### Reading Gold

```csharp
// Player's gold (convenience)
int gold = economySystem.Gold;

// Specific country
int countryGold = economySystem.GetCountryGoldInt(countryId);

// Precise value (for calculations)
FixedPoint64 precise = economySystem.GetCountryGold(countryId);
```

### Modifying Gold

```csharp
// Add to player
economySystem.AddGold(100);

// Add to specific country
economySystem.AddGoldToCountry(countryId, 50);

// Remove (returns false if insufficient)
bool success = economySystem.RemoveGold(25);
bool success = economySystem.RemoveGoldFromCountry(countryId, 25);
```

### Income Queries

```csharp
// Player's monthly income
int income = economySystem.GetMonthlyIncomeInt();

// Specific country
int countryIncome = economySystem.GetMonthlyIncomeInt(countryId);

// Precise value
FixedPoint64 precise = economySystem.GetMonthlyIncome(countryId);
```

## Income Caching (Dirty Flag Pattern)

Income is cached and only recalculated when something changes:

```csharp
// Cache is invalidated when:
// - Province ownership changes
// - Building is constructed
// - Modifiers change

// Manual invalidation (after loading, etc.)
economySystem.InvalidateCountryIncome(countryId);
economySystem.InvalidateAllIncome();
```

## Events

### Emitting Gold Changes

```csharp
private void EmitGoldChanged(ushort countryId, FixedPoint64 oldGold, FixedPoint64 newGold)
{
    gameState.EventBus.Emit(new GoldChangedEvent
    {
        CountryId = countryId,
        OldValue = oldGold.ToInt(),
        NewValue = newGold.ToInt()
    });
}
```

### Subscribing to Gold Changes (UI)

```csharp
public class ResourceBarUI : StarterKitPanel
{
    public void Initialize(GameState gameState)
    {
        Subscribe<GoldChangedEvent>(OnGoldChanged);
    }

    private void OnGoldChanged(GoldChangedEvent evt)
    {
        if (evt.CountryId == playerState.PlayerCountryId)
            UpdateDisplay();
    }
}
```

## Serialization (Save/Load)

```csharp
// Save
byte[] data = economySystem.Serialize();

// Load
economySystem.Deserialize(data);
economySystem.InvalidateAllIncome(); // Recalculate caches
```

FixedPoint64 is serialized as raw `long` values for perfect determinism.

## Integration with ModifierSystem

Buildings provide income bonuses through modifiers:

```csharp
// In BuildingSystem, when constructing a building:
modifierSystem.AddProvinceModifier(
    provinceId,
    (ushort)ModifierType.LocalIncomeAdditive,
    FixedPoint64.One  // +1 gold
);

// Economy reads modifiers when calculating income:
FixedPoint64 bonus = modifierSystem.GetProvinceModifier(
    provinceId,
    countryId,
    (ushort)ModifierType.LocalIncomeAdditive,
    FixedPoint64.Zero
);
```

## Creating a Custom Economy

To extend or replace the StarterKit economy:

```csharp
public class MyEconomySystem : IDisposable
{
    private readonly GameState gameState;
    private readonly CompositeDisposable subscriptions = new();
    private Dictionary<ushort, FixedPoint64> countryGold = new();

    public MyEconomySystem(GameState gameState)
    {
        this.gameState = gameState;

        // Subscribe to time events
        subscriptions.Add(
            gameState.EventBus.Subscribe<MonthlyTickEvent>(OnMonthlyTick)
        );
    }

    private void OnMonthlyTick(MonthlyTickEvent evt)
    {
        // Your income formula here
        // Use FixedPoint64 for all calculations!
    }

    public void Dispose()
    {
        subscriptions.Dispose();
    }
}
```

## Best Practices

1. **Always use FixedPoint64** for money calculations
2. **Emit events** when gold changes so UI can update
3. **Cache income** with dirty flags - don't recalculate every frame
4. **Serialize as raw values** - `FixedPoint64.RawValue` is a `long`
5. **Invalidate caches** after loading or major changes
6. **Track all countries** - not just player, for AI and ledger

## StarterKit Files

- `Systems/EconomySystem.cs` - Full economy implementation
- `UI/ResourceBarUI.cs` - Gold display UI
- `Commands/AddGoldCommand.cs` - Debug command
- `State/StarterKitEvents.cs` - GoldChangedEvent definition
