# Cookbook

Quick recipes for common tasks. Each recipe shows the minimal code needed. See feature guides for deeper explanations.

## Province & Country Data

### Get province owner
```csharp
var state = gameState.Provinces.GetProvinceState(provinceId);
ushort ownerId = state.ownerID;
```

### Get all provinces owned by a country
```csharp
using var provinces = Query.Provinces(gameState)
    .OwnedBy(countryId)
    .Execute(Allocator.Temp);

foreach (ushort provinceId in provinces)
{
    // Process province
}
```

### Get province count for a country
```csharp
int count = gameState.ProvinceQueries.GetCountryProvinceCount(countryId);
```

### Get country tag (e.g., "FRA", "ENG")
```csharp
string tag = gameState.CountryQueries.GetTag(countryId);
```

### Get province name
```csharp
string name = LocalizationManager.Get($"PROV{provinceId}");
```

### Check if provinces are adjacent
```csharp
bool adjacent = gameState.Adjacencies.IsAdjacent(provinceA, provinceB);
```

### Get neighboring provinces
```csharp
using var neighbors = gameState.Adjacencies.GetNeighbors(provinceId, Allocator.Temp);
foreach (ushort neighborId in neighbors)
{
    // Process neighbor
}
```

---

## Commands

### Create and execute a command
```csharp
var cmd = new ChangeOwnerCommand
{
    ProvinceID = provinceId,
    NewOwnerID = targetCountryId
};

bool success = gameState.TryExecuteCommand(cmd);
```

### Create a custom command (using SimpleCommand)
```csharp
[Command("my_command", "Does something")]
public class MyCommand : SimpleCommand
{
    [Arg("target", "Target province")]
    public ushort TargetId { get; set; }

    [Arg("amount", "Amount to apply")]
    public int Amount { get; set; }

    public override bool Validate(GameState gameState)
    {
        return TargetId > 0 && Amount > 0;
    }

    public override void Execute(GameState gameState)
    {
        // Modify state here
    }
}
```

### Command with validation builder
```csharp
public override bool Validate(GameState gameState)
{
    return Validate.For(gameState)
        .Province(ProvinceId)
        .ProvinceOwnedBy(ProvinceId, IssuingCountryId)
        .Result();
}
```

---

## Events

### Subscribe to an event
```csharp
gameState.EventBus.Subscribe<ProvinceOwnerChangedEvent>(OnOwnerChanged);

void OnOwnerChanged(ProvinceOwnerChangedEvent evt)
{
    Debug.Log($"Province {evt.ProvinceId} now owned by {evt.NewOwner}");
}
```

### Emit an event
```csharp
gameState.EventBus.Emit(new MyCustomEvent
{
    Data = someValue
});
```

### Define a custom event
```csharp
public struct MyCustomEvent : IGameEvent
{
    public int Data;
    public float TimeStamp { get; set; }
}
```

### Unsubscribe from an event
```csharp
IDisposable subscription = gameState.EventBus.Subscribe<MyEvent>(handler);

// Later, to unsubscribe:
subscription.Dispose();
```

---

## Economy & Resources

### Add/remove gold from a country (GAME layer)
```csharp
// This is GAME layer - implement in your EconomySystem
economySystem.AddGold(countryId, FixedPoint64.FromInt(100));
economySystem.TrySpendGold(countryId, 50); // Returns false if insufficient
```

### Calculate with FixedPoint64 (deterministic)
```csharp
// Always use FixedPoint64 for simulation math
FixedPoint64 baseIncome = FixedPoint64.FromInt(provinceCount);
FixedPoint64 modifier = FixedPoint64.FromFraction(3, 2); // 1.5x
FixedPoint64 totalIncome = baseIncome * modifier;
int asInt = totalIncome.ToInt();
```

---

## Buildings (GAME Layer)

Building systems are GAME-specific. Here's the pattern:

```csharp
// In your BuildingSystem
public bool CanBuild(ushort provinceId, byte buildingTypeId, ushort countryId)
{
    var state = gameState.Provinces.GetProvinceState(provinceId);
    return state.ownerID == countryId;
}

// Execute via command
var cmd = new BuildCommand
{
    ProvinceId = provinceId,
    BuildingTypeId = buildingTypeId,
    IssuingCountryId = countryId
};
gameState.TryExecuteCommand(cmd);
```

---

## Units (ENGINE Layer)

### Create a unit
```csharp
var cmd = new CreateUnitCommand
{
    ProvinceId = provinceId,
    CountryId = countryId,
    UnitTypeId = unitTypeId,
    InitialCount = 1000
};
gameState.TryExecuteCommand(cmd);
```

### Move a unit
```csharp
var cmd = new MoveUnitCommand
{
    UnitId = unitId,
    TargetProvinceId = targetProvinceId
};
gameState.TryExecuteCommand(cmd);
```

### Get units in a province
```csharp
using var units = Query.Units(unitSystem)
    .InProvince(provinceId)
    .Execute(Allocator.Temp);
```

### Get all units owned by a country
```csharp
using var units = Query.Units(unitSystem)
    .OwnedBy(countryId)
    .Execute(Allocator.Temp);
```

---

## Time System

### Subscribe to monthly tick
```csharp
gameState.EventBus.Subscribe<MonthlyTickEvent>(OnMonthlyTick);

void OnMonthlyTick(MonthlyTickEvent evt)
{
    // Called once per game month
}
```

### Get current date
```csharp
int year = gameState.Time.CurrentYear;
int month = gameState.Time.CurrentMonth;
int day = gameState.Time.CurrentDay;
```

### Pause/resume time
```csharp
gameState.Time.PauseTime();
gameState.Time.StartTime();  // Resume
gameState.Time.TogglePause();
```

### Set game speed
```csharp
gameState.Time.SetSpeed(3); // 3x speed
```

---

## Map Modes

### Create a custom map mode handler
```csharp
public class MyMapMode : BaseMapModeHandler
{
    public override MapMode Mode => MapMode.Economic;
    public override string Name => "My Mode";
    public override int ShaderModeID => 8;

    public override UpdateFrequency GetUpdateFrequency() => UpdateFrequency.Monthly;

    public override void OnActivate(Material mapMaterial, MapModeDataTextures dataTextures)
    {
        DisableAllMapModeKeywords(mapMaterial);
        EnableMapModeKeyword(mapMaterial, "MAP_MODE_ECONOMIC");
        SetShaderMode(mapMaterial, ShaderModeID);
    }

    public override void OnDeactivate(Material mapMaterial) { }

    public override void UpdateTextures(MapModeDataTextures dataTextures,
        ProvinceQueries provinceQueries, CountryQueries countryQueries,
        ProvinceMapping provinceMapping, object gameProvinceSystem = null)
    {
        // Update textures based on your data
    }

    public override string GetProvinceTooltip(ushort provinceId,
        ProvinceQueries provinceQueries, CountryQueries countryQueries)
    {
        return $"Province {provinceId}";
    }
}

// Register during initialization
mapModeManager.RegisterHandler(MapMode.Economic, new MyMapMode());
```

### Switch map mode
```csharp
mapModeManager.SetMapMode(MapMode.Political);
```

---

## UI Patterns

### Block map clicks when over UI
```csharp
private bool IsPointerOverUI()
{
    return EventSystem.current != null &&
           EventSystem.current.IsPointerOverGameObject();
}

void Update()
{
    if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
    {
        HandleMapClick();
    }
}
```

### Update UI when game state changes
```csharp
void Initialize()
{
    gameState.EventBus.Subscribe<GoldChangedEvent>(OnGoldChanged);
}

void OnGoldChanged(GoldChangedEvent evt)
{
    if (evt.CountryId == playerCountryId)
        goldLabel.text = $"Gold: {evt.NewValue}";
}
```

### Scroll to bottom after adding content (UI Toolkit)
```csharp
contentLabel.text += newText;
scrollView.schedule.Execute(() =>
{
    float maxScroll = scrollView.contentContainer.layout.height -
                      scrollView.contentViewport.layout.height;
    scrollView.scrollOffset = new Vector2(0, Mathf.Max(0, maxScroll));
}).ExecuteLater(10);
```

---

## Diplomacy

### Declare war
```csharp
var cmd = new DeclareWarCommand
{
    AttackerID = myCountryId,
    DefenderID = targetCountryId
};
gameState.TryExecuteCommand(cmd);
```

### Check if countries are at war
```csharp
bool atWar = gameState.Diplomacy.IsAtWar(countryA, countryB);
```

### Get opinion between countries
```csharp
FixedPoint64 opinion = gameState.Diplomacy.GetOpinion(fromCountry, toCountry, currentTick);
int opinionInt = opinion.ToInt();
```

---

## Save/Load (GAME Layer)

Save/load is typically GAME-specific. Here's the pattern:

```csharp
// In your SaveManager
public void SaveGame(string saveName)
{
    var data = new SaveData
    {
        CurrentTick = gameState.Time.CurrentTick,
        // ... serialize your game state
    };
    // Write to file
}

public void LoadGame(string saveName)
{
    // Read from file
    // Restore game state
}
```

---

## Debugging

### Log with subsystem (goes to Logs/ folder)
```csharp
ArchonLogger.Log("Something happened", "game_systems");
ArchonLogger.LogWarning("Something suspicious", "game_systems");
ArchonLogger.LogError("Something broke", "game_systems");
```

### Check console for errors
Read `Logs/game_initialization.log`, `Logs/core_simulation.log`, etc.

### Verify province data at runtime
```csharp
var state = gameState.Provinces.GetProvinceState(provinceId);
ArchonLogger.Log($"Province {provinceId}: owner={state.ownerID}, terrain={state.terrainType}", "debug");
```

---

## Common Patterns

### Iterate with proper disposal
```csharp
// Always use 'using' with NativeCollections
using var results = Query.Provinces(gameState)
    .OwnedBy(countryId)
    .Execute(Allocator.Temp);

foreach (var id in results)
{
    // Process
}
// Automatically disposed
```

### Cache expensive calculations per frame
```csharp
private int cachedFrame = -1;
private int cachedValue;

public int GetValue()
{
    if (Time.frameCount != cachedFrame)
    {
        cachedValue = ExpensiveCalculation();
        cachedFrame = Time.frameCount;
    }
    return cachedValue;
}
```

### Initialize system after ENGINE ready
```csharp
IEnumerator Start()
{
    while (ArchonEngine.Instance == null || !ArchonEngine.Instance.IsInitialized)
        yield return null;

    var gameState = ArchonEngine.Instance.GameState;
    // Now safe to use gameState
}
```

### Deterministic math (multiplayer-safe)
```csharp
// ❌ WRONG - Float is non-deterministic
float result = value * 1.5f;

// ✅ CORRECT - FixedPoint64 is deterministic
FixedPoint64 result = value * FixedPoint64.FromFraction(3, 2);
```

### Validation with error message
```csharp
public override bool Validate(GameState gameState)
{
    bool isValid = Validate.For(gameState)
        .Country(CountryId)
        .Province(ProvinceId)
        .ProvinceOwnedBy(ProvinceId, CountryId)
        .Result(out string reason);

    if (!isValid)
        ArchonLogger.LogWarning($"Validation failed: {reason}", "game_commands");

    return isValid;
}
```
