# Building System

The Building system allows constructing buildings in provinces that provide bonuses through the ModifierSystem. Buildings are defined in JSON5 files and loaded at runtime.

## Basic Setup

```csharp
public class MyInitializer : MonoBehaviour
{
    private BuildingSystem buildingSystem;

    IEnumerator Start()
    {
        // Wait for ENGINE
        while (!ArchonEngine.Instance.IsInitialized)
            yield return null;

        var gameState = ArchonEngine.Instance.GameState;
        var playerState = new PlayerState(gameState);
        var economySystem = new EconomySystem(gameState, playerState, modifierSystem);
        var modifierSystem = new ModifierSystem();

        // Create building system
        buildingSystem = new BuildingSystem(
            gameState, playerState, economySystem, modifierSystem);

        // Load building definitions
        string buildingsPath = Path.Combine(dataDirectory, "buildings");
        buildingSystem.LoadBuildingTypes(buildingsPath);
    }
}
```

## Defining Building Types

Create JSON5 files in `Template-Data/buildings/`:

### Simple Building (market.json5)
```json5
{
    id: "market",
    name: "Market",
    cost: { gold: 50 },
    max_per_province: 1,
    modifiers: {
        local_income_additive: 1.0  // +1 gold per month
    }
}
```

### Building with Percentage Bonus (workshop.json5)
```json5
{
    id: "workshop",
    name: "Workshop",
    cost: { gold: 100 },
    max_per_province: 1,
    modifiers: {
        local_income_modifier: 0.25  // +25% local income
    }
}
```

### Country-Wide Building (capital.json5)
```json5
{
    id: "capital",
    name: "Capital",
    cost: { gold: 200 },
    max_per_province: 1,
    modifiers: {
        country_income_modifier: 0.10  // +10% income in ALL provinces
    }
}
```

## Modifier Types

Define your modifier types in an enum:

```csharp
public enum ModifierType : ushort
{
    None = 0,

    // Province-local modifiers
    LocalIncomeAdditive = 1,    // Flat +X gold
    LocalIncomeModifier = 2,    // +X% local income

    // Country-wide modifiers
    CountryIncomeModifier = 10, // +X% income everywhere
}
```

## Constructing Buildings

### Player Construction
```csharp
// Check if construction is possible
if (buildingSystem.CanConstruct(provinceId, "market", out string reason))
{
    // Construct (deducts gold, applies modifiers)
    buildingSystem.Construct(provinceId, "market");
}
else
{
    Debug.Log($"Cannot build: {reason}");
}
```

### AI Construction
```csharp
// AI bypasses gold cost and ownership check
buildingSystem.ConstructForAI(provinceId, "market");
```

### Via Command
```csharp
var cmd = new ConstructBuildingCommand
{
    BuildingTypeId = "market",
    ProvinceId = new ProvinceId(provinceId)
};
gameState.CommandProcessor.Execute(cmd);
```

## Querying Buildings

```csharp
// Count of specific building type
int markets = buildingSystem.GetBuildingCount(provinceId, marketTypeId);

// Total buildings in province
int total = buildingSystem.GetTotalBuildingCount(provinceId);

// All buildings in province (typeId -> count)
var buildings = buildingSystem.GetProvinceBuildings(provinceId);

// Get building type info
BuildingType type = buildingSystem.GetBuildingType("market");
BuildingType type = buildingSystem.GetBuildingType(typeId);

// All building types
foreach (var type in buildingSystem.GetAllBuildingTypes())
{
    Debug.Log($"{type.Name}: costs {type.Cost} gold");
}
```

## Integration with ModifierSystem

Buildings apply modifiers when constructed:

```csharp
// In BuildingSystem.Construct():
private void ApplyBuildingModifiers(ushort provinceId, ushort ownerId, BuildingType type)
{
    foreach (var modifier in type.Modifiers)
    {
        var source = ModifierSource.CreatePermanent(
            type: ModifierSource.SourceType.Building,
            sourceId: type.ID,
            modifierTypeId: (ushort)modifier.Type,
            value: modifier.Value,
            isMultiplicative: modifier.IsMultiplicative
        );

        if (modifier.IsCountryWide)
        {
            modifierSystem.AddCountryModifier(ownerId, source);
        }
        else
        {
            modifierSystem.AddProvinceModifier(provinceId, source);
        }
    }
}
```

EconomySystem reads these modifiers when calculating income.

## Events

### BuildingConstructedEvent

```csharp
public struct BuildingConstructedEvent : IGameEvent
{
    public ushort ProvinceId;
    public ushort BuildingTypeId;
    public ushort CountryId;
    public float TimeStamp { get; set; }
}
```

### Subscribing to Building Events

```csharp
public class BuildingInfoUI : StarterKitPanel
{
    public void Initialize(GameState gameState)
    {
        Subscribe<BuildingConstructedEvent>(OnBuildingConstructed);
    }

    private void OnBuildingConstructed(BuildingConstructedEvent evt)
    {
        if (evt.ProvinceId == selectedProvinceId)
            RefreshBuildingList();
    }
}
```

## Validation

Use fluent validation in commands:

```csharp
public override bool Validate(GameState gameState)
{
    return Validate.For(gameState)
        .Province(ProvinceId)
        .BuildingTypeExists(BuildingTypeId)
        .CanConstructBuilding(ProvinceId, BuildingTypeId)
        .Result(out validationError);
}
```

Custom validators (in your game layer):

```csharp
public static ValidationBuilder BuildingTypeExists(
    this ValidationBuilder v, string buildingTypeId)
{
    var buildings = MyInitializer.Instance?.BuildingSystem;
    if (buildings?.GetBuildingType(buildingTypeId) == null)
        return v.Fail($"Building type '{buildingTypeId}' does not exist");
    return v;
}

public static ValidationBuilder CanConstructBuilding(
    this ValidationBuilder v, ProvinceId provinceId, string buildingTypeId)
{
    var buildings = MyInitializer.Instance?.BuildingSystem;
    if (!buildings.CanConstruct(provinceId, buildingTypeId, out string reason))
        return v.Fail(reason);
    return v;
}
```

## Serialization

```csharp
// Save
byte[] data = buildingSystem.Serialize();

// Load
buildingSystem.Deserialize(data);
// Note: Modifiers need to be re-applied after load
```

## UI Example

```csharp
public class BuildingInfoUI : StarterKitPanel
{
    private BuildingSystem buildingSystem;
    private ushort selectedProvinceId;

    protected override void CreateUI()
    {
        foreach (var type in buildingSystem.GetAllBuildingTypes())
        {
            var button = new Button(() => OnBuildClicked(type.StringID));
            button.text = $"{type.Name} ({type.Cost}g)";
            panel.Add(button);
        }
    }

    private void OnBuildClicked(string buildingTypeId)
    {
        if (buildingSystem.CanConstruct(selectedProvinceId, buildingTypeId, out _))
        {
            buildingSystem.Construct(selectedProvinceId, buildingTypeId);
        }
    }

    private void RefreshBuildingList()
    {
        var buildings = buildingSystem.GetProvinceBuildings(selectedProvinceId);
        // Update UI to show current buildings
    }
}
```

## Best Practices

1. **Load building types early** - Before player can interact
2. **Use ModifierSystem** - Don't hardcode building effects
3. **Emit events** - Let UI and other systems react
4. **Validate before construct** - Use `CanConstruct()` to check
5. **JSON5 for definitions** - Easy to mod and extend
6. **Separate AI construction** - AI doesn't need gold checks

## StarterKit Files

- `Systems/BuildingSystem.cs` - Full implementation
- `Data/ModifierType.cs` - Modifier type enum
- `Commands/ConstructBuildingCommand.cs` - Build command
- `UI/BuildingInfoUI.cs` - Building UI panel
- `Validation/StarterKitValidationExtensions.cs` - Validators
