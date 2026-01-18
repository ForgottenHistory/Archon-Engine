# Events

The EventBus enables decoupled communication between systems. Events are zero-allocation structs that get queued and processed once per frame.

## Why Events?

```
❌ WRONG - Tight coupling
economySystem.OnGoldChanged += uiPanel.Refresh;  // Direct reference

✅ CORRECT - Decoupled via EventBus
gameState.EventBus.Emit(new GoldChangedEvent { ... });
// UI subscribes separately, no direct reference needed
```

Events provide:
- **Loose coupling** - Systems don't need references to each other
- **Zero allocations** - Struct events, no boxing
- **Frame-coherent** - Events processed once per frame in batch
- **Multiple listeners** - Many systems can react to one event

## Creating an Event

Events must be **structs** implementing `IGameEvent`:

```csharp
using Core;

namespace MyGame
{
    public struct GoldChangedEvent : IGameEvent
    {
        public ushort CountryId;
        public int OldValue;
        public int NewValue;

        // Required by IGameEvent (set automatically)
        public float TimeStamp { get; set; }
    }

    public struct BuildingConstructedEvent : IGameEvent
    {
        public ushort ProvinceId;
        public ushort BuildingTypeId;
        public ushort CountryId;
        public float TimeStamp { get; set; }
    }

    public struct PlayerCountrySelectedEvent : IGameEvent
    {
        public ushort CountryId;
        public float TimeStamp { get; set; }
    }
}
```

**Key rules:**
- Must be `struct`, not `class` (avoids heap allocation)
- Must implement `IGameEvent`
- Keep events small (only include necessary data)
- `TimeStamp` is set automatically when emitted

## Emitting Events

Emit events when something noteworthy happens:

```csharp
public void AddGold(ushort countryId, int amount)
{
    int oldValue = GetGold(countryId);
    // ... update gold ...
    int newValue = GetGold(countryId);

    // Notify listeners
    gameState.EventBus.Emit(new GoldChangedEvent
    {
        CountryId = countryId,
        OldValue = oldValue,
        NewValue = newValue
    });
}
```

## Subscribing to Events

### In UI Panels (Recommended Pattern)

Use the `StarterKitPanel` base class which auto-manages subscriptions:

```csharp
public class ResourceBarUI : StarterKitPanel
{
    public void Initialize(GameState gameStateRef)
    {
        base.Initialize(gameStateRef);

        // Subscribe - auto-disposed on destroy
        Subscribe<GoldChangedEvent>(OnGoldChanged);
        Subscribe<PlayerCountrySelectedEvent>(OnCountrySelected);
    }

    private void OnGoldChanged(GoldChangedEvent evt)
    {
        if (evt.CountryId == playerState.PlayerCountryId)
        {
            UpdateDisplay();
        }
    }

    private void OnCountrySelected(PlayerCountrySelectedEvent evt)
    {
        Show();
    }

    protected override void OnDestroy()
    {
        // Subscriptions auto-disposed by base class
        base.OnDestroy();
    }
}
```

### Direct Subscription

For non-panel classes, manage subscriptions manually:

```csharp
public class MySystem : IDisposable
{
    private CompositeDisposable subscriptions = new();

    public void Initialize(GameState gameState)
    {
        // Subscribe and track for disposal
        subscriptions.Add(
            gameState.EventBus.Subscribe<GoldChangedEvent>(OnGoldChanged)
        );

        subscriptions.Add(
            gameState.EventBus.Subscribe<MonthlyTickEvent>(OnMonthlyTick)
        );
    }

    private void OnGoldChanged(GoldChangedEvent evt) { /* ... */ }
    private void OnMonthlyTick(MonthlyTickEvent evt) { /* ... */ }

    public void Dispose()
    {
        subscriptions.Dispose();
    }
}
```

### One-Time Subscription

```csharp
IDisposable sub = gameState.EventBus.Subscribe<SomeEvent>(evt =>
{
    // Handle event
    sub.Dispose(); // Unsubscribe after first event
});
```

## ENGINE Events

The ENGINE provides these events (subscribe via `gameState.EventBus`):

### Time Events (Core.Events)
- `HourlyTickEvent` - Every game hour
- `DailyTickEvent` - Every game day
- `WeeklyTickEvent` - Every game week
- `MonthlyTickEvent` - Every game month
- `YearlyTickEvent` - Every game year

### Province Events
- `ProvinceOwnershipChangedEvent` - Province changed owner
- `ProvinceDevelopmentChangedEvent` - Development changed

### Country Events
- `CountryColorChangedEvent` - Country color changed

### Unit Events (Core.Units)
- `UnitCreatedEvent` - Unit created
- `UnitMovedEvent` - Unit moved
- `UnitDisbandedEvent` - Unit disbanded

### Diplomacy Events
- `DiplomacyWarDeclaredEvent` - War declared
- `DiplomacyPeaceMadeEvent` - Peace made

## When to Use Events vs Direct Calls

| Use Events When | Use Direct Calls When |
|-----------------|----------------------|
| Multiple systems need to react | Only one system needs the data |
| Systems shouldn't know about each other | Performance-critical path |
| UI needs to update from simulation | Same-system internal operations |
| Cross-layer communication | Required dependency (can't work without it) |

## Best Practices

1. **Events are notifications, not requests** - Don't use events to ask systems to do things; use commands for that

2. **Keep events small** - Only include data listeners actually need

3. **Filter in handlers** - Check if the event is relevant before processing:
   ```csharp
   void OnGoldChanged(GoldChangedEvent evt)
   {
       if (evt.CountryId != playerState.PlayerCountryId)
           return; // Not our country, ignore
       // ...
   }
   ```

4. **Always dispose subscriptions** - Use `CompositeDisposable` or base class management

5. **Don't emit events in constructors** - Systems may not be ready yet

6. **Avoid event chains** - Don't emit events from event handlers (can cause infinite loops)

## StarterKit Examples

- `StarterKitEvents.cs` - GoldChangedEvent, BuildingConstructedEvent
- `PlayerEvents.cs` - PlayerCountrySelectedEvent
- `ResourceBarUI.cs` - Subscribing to gold and player events
- `BuildingInfoUI.cs` - Subscribing to building events

See `Scripts/StarterKit/State/` for event definitions and `Scripts/StarterKit/UI/` for subscription patterns.
