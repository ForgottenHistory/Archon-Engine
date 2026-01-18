# Unit System

The Unit system manages military units - creating, moving, and disbanding them. It wraps the ENGINE's `Core.Units.UnitSystem` and adds GAME-layer features like unit type loading from JSON5.

## Architecture

```
StarterKit.UnitSystem (GAME layer)
    ↓ wraps
Core.Units.UnitSystem (ENGINE layer)
    ↓ manages
UnitState structs (16-byte fixed data)
```

The GAME layer handles:
- Unit type definitions (from JSON5)
- Player-specific operations
- Cost validation

The ENGINE layer handles:
- Unit storage and queries
- Movement processing
- Event emission

## Basic Setup

```csharp
public class MyInitializer : MonoBehaviour
{
    private UnitSystem unitSystem;

    IEnumerator Start()
    {
        while (!ArchonEngine.Instance.IsInitialized)
            yield return null;

        var gameState = ArchonEngine.Instance.GameState;
        var playerState = new PlayerState(gameState);

        // Create unit system
        unitSystem = new UnitSystem(gameState, playerState);

        // Load unit type definitions
        string unitsPath = Path.Combine(dataDirectory, "units");
        unitSystem.LoadUnitTypes(unitsPath);
    }
}
```

## Defining Unit Types

Create JSON5 files in `Template-Data/units/`:

### infantry.json5
```json5
{
    id: "infantry",
    name: "Infantry",
    cost: { gold: 10 },
    maintenance: { gold: 1 },
    stats: {
        attack: 3,
        defense: 2,
        speed: 2  // Days per province
    }
}
```

### cavalry.json5
```json5
{
    id: "cavalry",
    name: "Cavalry",
    cost: { gold: 25 },
    maintenance: { gold: 2 },
    stats: {
        attack: 5,
        defense: 1,
        speed: 1  // Faster movement
    }
}
```

## Creating Units

```csharp
// By string ID
ushort unitId = unitSystem.CreateUnit(provinceId, "infantry");

// By numeric ID
ushort unitId = unitSystem.CreateUnit(provinceId, unitTypeId);

// Via command
var cmd = new CreateUnitCommand
{
    ProvinceId = new ProvinceId(provinceId),
    UnitTypeId = "infantry"
};
gameState.CommandProcessor.Execute(cmd);
```

## Moving Units

```csharp
// Direct move
unitSystem.MoveUnit(unitId, targetProvinceId);

// Via command
var cmd = new MoveUnitCommand
{
    UnitId = unitId,
    TargetProvinceId = new ProvinceId(targetProvinceId)
};
gameState.CommandProcessor.Execute(cmd);
```

## Disbanding Units

```csharp
// Direct
unitSystem.DisbandUnit(unitId);

// Via command
var cmd = new DisbandUnitCommand { UnitId = unitId };
gameState.CommandProcessor.Execute(cmd);
```

## Querying Units

```csharp
// Get unit state
UnitState unit = unitSystem.GetUnit(unitId);

// Units in a province
List<ushort> units = unitSystem.GetUnitsInProvince(provinceId);
int count = unitSystem.GetUnitCountInProvince(provinceId);

// Player's units
List<ushort> myUnits = unitSystem.GetPlayerUnits();

// Unit type info
UnitType type = unitSystem.GetUnitType("infantry");
UnitType type = unitSystem.GetUnitType(unitTypeId);

// All unit types
foreach (var type in unitSystem.GetAllUnitTypes())
{
    Debug.Log($"{type.Name}: {type.Attack} ATK / {type.Defense} DEF");
}
```

## UnitState Structure (ENGINE)

The ENGINE stores units as fixed-size structs

```csharp
public struct UnitState  // 8 bytes
{
    public ushort provinceID;   // Current location
    public ushort countryID;    // Owning country
    public ushort unitTypeID;   // Unit type (infantry, cavalry, etc.)
    public ushort unitCount;    // 0 = dead/disbanded
}
```

## Events

Unit events are emitted by the ENGINE. Subscribe via EventBus:

```csharp
// In your UI or system
gameState.EventBus.Subscribe<UnitCreatedEvent>(OnUnitCreated);
gameState.EventBus.Subscribe<UnitMovedEvent>(OnUnitMoved);
gameState.EventBus.Subscribe<UnitDestroyedEvent>(OnUnitDestroyed);

void OnUnitCreated(UnitCreatedEvent evt)
{
    Debug.Log($"Unit {evt.UnitID} created in province {evt.ProvinceID}");
}

void OnUnitMoved(UnitMovedEvent evt)
{
    Debug.Log($"Unit {evt.UnitID} moved from {evt.OldProvinceID} to {evt.NewProvinceID}");
}
```

## Unit Visualization

StarterKit includes `UnitVisualization` for rendering unit badges:

```csharp
public class UnitVisualization : MonoBehaviour
{
    public void Initialize(GameState gameState, UnitSystem unitSystem)
    {
        // Uses GPU instancing to render unit count badges
        // at province centers
    }
}
```

Features:
- GPU instanced rendering (efficient for many units)
- Number badges showing unit count per province
- Path lines for moving units
- Uses `ProvinceCenterLookup` for positioning

## Validation

Use fluent validation in commands:

```csharp
public override bool Validate(GameState gameState)
{
    return Validate.For(gameState)
        .Province(TargetProvinceId)
        .UnitExists(UnitId)
        .Result(out validationError);
}
```

Custom validators:

```csharp
public static ValidationBuilder UnitExists(
    this ValidationBuilder v, ushort unitId)
{
    var units = MyInitializer.Instance?.UnitSystem;
    if (units == null)
        return v.Fail("UnitSystem not available");

    var unit = units.GetUnit(unitId);
    if (unit.unitCount == 0)
        return v.Fail($"Unit {unitId} does not exist");

    return v;
}

public static ValidationBuilder UnitTypeExists(
    this ValidationBuilder v, string unitTypeId)
{
    var units = MyInitializer.Instance?.UnitSystem;
    if (units?.GetUnitType(unitTypeId) == null)
        return v.Fail($"Unit type '{unitTypeId}' does not exist");

    return v;
}
```

## Movement Handler (UI)

For click-to-move UI:

```csharp
public class UnitMoveHandler : MonoBehaviour
{
    private ushort selectedUnitId;

    void OnProvinceClicked(ushort provinceId)
    {
        if (selectedUnitId != 0)
        {
            // Move selected unit to clicked province
            unitSystem.MoveUnit(selectedUnitId, provinceId);
            selectedUnitId = 0;
        }
        else
        {
            // Select a unit in this province
            var units = unitSystem.GetUnitsInProvince(provinceId);
            if (units.Count > 0)
                selectedUnitId = units[0];
        }
    }
}
```

## Integration with Pathfinding

The ENGINE provides A* pathfinding:

```csharp
// Get path from unit's province to target
var path = gameState.PathfindingSystem.FindPath(
    fromProvinceId,
    toProvinceId
);

// Move unit along path
foreach (var province in path)
{
    unitSystem.MoveUnit(unitId, province);
    // In real game, wait for movement to complete
}
```

## Best Practices

1. **Use commands** for player actions (enables undo, network sync)
2. **Subscribe to ENGINE events** for unit state changes
3. **Query via GAME layer** when you need unit type info
4. **Query via ENGINE layer** for raw unit state
5. **JSON5 for definitions** - easy to mod
6. **GPU instancing** for visualization (see UnitVisualization)

## StarterKit Files

- `Systems/UnitSystem.cs` - Unit type loading, player operations
- `Visualization/UnitVisualization.cs` - GPU instanced rendering
- `Commands/CreateUnitCommand.cs` - Create command
- `Commands/MoveUnitCommand.cs` - Move command
- `Commands/DisbandUnitCommand.cs` - Disband command
- `UI/UnitMoveHandler.cs` - Click-to-move UI
- `UI/UnitInfoUI.cs` - Unit info panel
