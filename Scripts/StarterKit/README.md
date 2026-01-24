# StarterKit

**Purpose:** Minimal working game template demonstrating Archon-Engine patterns.

---

## What Is StarterKit?

A lightweight implementation showing how to build a game on Archon-Engine. Use it as:
- Learning reference for ENGINE patterns
- Starting point for new games
- Test bed for ENGINE features

---

## Folder Structure

```
StarterKit/
├── Initializer.cs          # Entry point, coordinates all systems
├── Commands/               # Commands for state changes (network-synced)
├── MapModes/               # Custom map modes (extends ENGINE map system)
├── Network/                # Multiplayer (NetworkInitializer, LobbyUI)
├── State/                  # Player state and events
├── Systems/                # Game systems (economy, units, buildings, AI)
├── UI/                     # All UI components
├── Validation/             # GAME-layer validation extensions
└── Visualization/          # Visual representation (unit sprites, etc.)
```

---

## Systems (`Systems/`)

| System | Purpose |
|--------|---------|
| **EconomySystem** | Gold economy (1 gold/province/month + building bonuses) |
| **UnitSystem** | Military units with movement and combat stats |
| **BuildingSystem** | Province buildings that provide bonuses |
| **AISystem** | Basic AI that builds and expands |

### ProvinceQueryBuilder Pattern

AISystem demonstrates using ENGINE's fluent query builders with GAME-layer post-filtering:

```csharp
// ENGINE query: find unowned provinces bordering our country
using var query = new ProvinceQueryBuilder(provinceSystem, adjacencySystem);
using var candidates = query
    .BorderingCountry(countryId)  // Adjacent to our provinces
    .IsUnowned()                   // Not owned by anyone
    .Execute(Allocator.Temp);

// GAME-layer filter: only ownable terrain (ENGINE doesn't know this concept)
for (int i = 0; i < candidates.Length; i++)
{
    if (terrainLookup.IsTerrainOwnable(provinceSystem.GetProvinceTerrain(candidates[i])))
        colonizeCandidates.Add(candidates[i]);
}
```

Available ENGINE query filters:
- `.OwnedBy(countryId)` / `.ControlledBy(countryId)`
- `.IsOwned()` / `.IsUnowned()`
- `.IsLand()` / `.BorderingCountry(countryId)`
- `.AdjacentTo(provinceId)` / `.WithTerrain(terrainType)`

Terminal operations: `.Execute()`, `.Count()`, `.Any()`, `.FirstOrDefault()`

---

## State (`State/`)

| File | Purpose |
|------|---------|
| **PlayerState** | Tracks player's selected country |
| **PlayerEvents** | Player-specific events (country selected) |
| **StarterKitEvents** | Game events (GoldChanged, BuildingConstructed) |

---

## Commands (`Commands/`)

All state changes go through commands (Pattern 2). Commands are auto-registered for network sync.

| Command | Description |
|---------|-------------|
| `add_gold <amount>` | Add/remove gold from treasury |
| `create_unit <type> <province>` | Spawn a unit |
| `queue_movement <unitId> <path>` | Queue unit movement along path |
| `disband_unit <unitId>` | Remove a unit |
| `build <type> <province>` | Construct a building |
| `colonize <province>` | Colonize an unowned province |

Commands use ENGINE infrastructure (`Core.Commands`) and sync via `CommandProcessor`.

### Fluent Validation Pattern

Commands use ENGINE's fluent validation with GAME-layer extensions:

```csharp
public override bool Validate(GameState gameState)
{
    return Core.Validation.Validate.For(gameState)
        .Province(ProvinceId)              // ENGINE validator
        .UnitTypeExists(UnitTypeId)        // GAME extension
        .ProvinceOwnedByPlayer(ProvinceId) // GAME extension
        .Result(out validationError);
}
```

GAME-layer validators in `Validation/StarterKitValidationExtensions.cs`:
- `UnitExists(unitId)` - Unit is alive
- `UnitTypeExists(typeId)` - Unit type defined
- `ProvinceOwnedByPlayer(provinceId)` - Player owns province
- `BuildingTypeExists(typeId)` - Building type defined
- `CanConstructBuilding(provinceId, typeId)` - Construction allowed
- `HasGold(amount)` - Player has sufficient gold

### Type-Safe ID Wrappers

Commands use `ProvinceId` instead of raw `ushort` for compile-time safety:

```csharp
[Arg(1, "provinceId")]
public ProvinceId ProvinceId { get; set; }  // Not ushort!
```

Implicit conversions mean this is backward compatible with existing code.

---

## UI Components (`UI/`)

| Component | Purpose |
|-----------|---------|
| **CountrySelectionUI** | Initial country picker |
| **ResourceBarUI** | Gold display with income |
| **TimeUI** | Date and speed controls |
| **ProvinceInfoUI** | Selected province details + colonization |
| **ProvinceInfoPresenter** | Data formatting for province panel |
| **UnitInfoUI** | Unit list, creation, and movement |
| **BuildingInfoUI** | Building list and construction |
| **DiplomacyPanel** | War/peace management with other countries (D key) |
| **LedgerUI** | Country statistics table (L key) |
| **ToolbarUI** | Top-right buttons (Ledger, Map Mode, Save, Load) |

### Event-Driven UI Pattern

All UI components subscribe to relevant events and only refresh when necessary:

```csharp
// Subscribe in Initialize()
subscriptions.Add(gameState.EventBus.Subscribe<GoldChangedEvent>(HandleGoldChanged));

// Only refresh if visible
private void HandleGoldChanged(GoldChangedEvent evt)
{
    if (!isVisible) return;
    if (evt.CountryId != playerState.PlayerCountryId) return;
    RefreshDisplay();
}
```

This avoids polling in `Update()` and unnecessary refreshes.

---

## Visualization (`Visualization/`)

| Component | Purpose |
|-----------|---------|
| **UnitVisualization** | Renders unit sprites on map |

---

## Map Modes (`MapModes/`)

Custom map modes extend ENGINE's `GradientMapMode` to visualize GAME data:

| Component | Purpose |
|-----------|---------|
| **FarmDensityMapMode** | Heatmap showing farms built per province |

### Using Map Modes

- **M key** or **toolbar button** - Toggle between Political and Farm Density modes
- Farm Density shows: white (no farms) → yellow → orange (max farms)

### Creating Custom Map Modes

1. Create class extending `GradientMapMode`
2. Override abstract methods:
   - `GetGradient()` - Define color stops
   - `GetValueForProvince()` - Return data value for each province
3. Register in `Initializer.RegisterMapModes()`

Example (FarmDensityMapMode):
```csharp
public class FarmDensityMapMode : GradientMapMode
{
    protected override ColorGradient GetGradient()
    {
        return new ColorGradient(
            new Color32(240, 240, 220, 255),  // No farms
            new Color32(255, 200, 50, 255),   // Some farms
            new Color32(200, 80, 0, 255)      // Max farms
        );
    }

    protected override float GetValueForProvince(ushort provinceId, ...)
    {
        return buildingSystem.GetBuildingCount(provinceId, farmTypeId);
    }
}
```

This demonstrates **Pattern 1 (Engine-Game Separation)**: ENGINE provides the gradient map mode mechanism, GAME provides the policy (what data to visualize).

---

## Data Files

Located in `Assets/Archon-Engine/Template-Data/`:

```
units/          - Unit type definitions (*.json5)
buildings/      - Building type definitions (*.json5)
```

---

## Multiplayer

StarterKit includes full multiplayer support using lockstep synchronization.

### Quick Start
1. Launch game → Select "Host Game" or "Join Game" from lobby
2. Host selects country, clients join and select their countries
3. All players click "Ready", host clicks "Start Game"

### Architecture

**Lockstep Pattern:** All state changes go through commands. Host validates and broadcasts, clients execute identically.

```
Client Action → Command → Send to Host
                              ↓
                    Host validates & executes
                              ↓
                    Broadcast to all clients
                              ↓
                    Clients execute (identical state)
```

### Key Components

| Component | Purpose |
|-----------|---------|
| **NetworkInitializer** | Setup host/client, manage lobby state |
| **LobbyUI** | Host/Join/Ready UI |
| **CommandProcessor** | Routes commands through network |

### Command Sync

All StarterKit commands extend `BaseCommand` with serialization:

```csharp
public class CreateUnitCommand : BaseCommand
{
    public ushort CountryId { get; set; }  // Explicit - never use playerState

    public override void Serialize(BinaryWriter writer)
    {
        writer.Write(CountryId);
        writer.Write(ProvinceId.Value);
        // ...
    }
}
```

**Critical Rules:**
- Commands MUST include explicit `CountryId` (not from playerState)
- All state changes MUST go through commands
- AI runs ONLY on host (`NetworkInitializer.IsHost`)

### Time Synchronization

`NetworkTimeSync` keeps game time aligned across clients. Host controls time, clients follow.

---

## Save/Load

StarterKit integrates with ENGINE's SaveManager:
- **F6** - Quick save
- **F7** - Quick load
- Toolbar buttons also available

Serialized data: PlayerState, EconomySystem (gold), BuildingSystem (buildings)

---

## Getting Started

1. Open scene: `Assets/Archon-Engine/Scenes/StarterKit.unity`
2. Press Play
3. Select a country
4. Use UI to build, create units, manage economy

---

## Extending StarterKit

### Add New Unit Type
Create `Template-Data/units/myunit.json5`:
```json5
{
  id: "myunit",
  name: "My Unit",
  cost: { gold: 50 },
  stats: { attack: 5, defense: 3 }
}
```

### Add New Building Type
Create `Template-Data/buildings/mybuilding.json5`:
```json5
{
  id: "mybuilding",
  name: "My Building",
  cost: { gold: 100 },
  modifiers: { gold_output: 2 },
  max_per_province: 1
}
```

### Add New Command
1. Create command class extending `BaseCommand`
2. Create factory with `[CommandMetadata]` attribute
3. Registry auto-discovers on startup

### Add New UI Panel
1. Create class with `[RequireComponent(typeof(UIDocument))]`
2. Subscribe to relevant events via `EventBus`
3. Only refresh when visible (event-driven pattern)
4. Initialize from `Initializer.cs`

---

## Architecture Patterns Used

- **Pattern 1 (Engine-Game Separation)** - ENGINE mechanism + GAME policy (map modes, validation extensions, query post-filtering)
- **Pattern 2 (Command)** - All state changes through commands with fluent validation
- **Pattern 3 (Event-Driven)** - EventBus subscriptions, zero-allocation events
- **Pattern 7 (Registry)** - Type-safe ID wrappers (`ProvinceId`, `CountryId`)
- **Pattern 14 (Hybrid Save/Load)** - Binary serialization with callbacks
- **Pattern 15 (Phase-Based Init)** - Coroutine-based initialization
- **Pattern 19 (UI Presenter)** - Separated view/presenter components

### ENGINE Features Demonstrated

- **Fluent Validation** - `Core.Validation.Validate.For(gs).Province(id).Result()` with GAME extensions
- **Query Builders** - `ProvinceQueryBuilder`, `CountryQueryBuilder`, `UnitQueryBuilder` for fluent filtering

---

## Intentionally Omitted

### Combat System

Combat is **intentionally not included** in StarterKit. Every grand strategy game handles combat differently:
- EU4: Stack-based with dice rolls and morale
- HOI4: Front lines with division combat width
- Victoria 3: General-based with battle conditions
- CK3: Knight duels and army composition

Combat is pure **GAME-layer policy** - ENGINE provides the building blocks:

```csharp
// Find enemy units in a province
using var enemies = Query.Units(unitSystem)
    .InProvince(provinceId)
    .NotOwnedBy(myCountryId)
    .Execute(Allocator.Temp);

// GAME layer decides resolution (dice? morale? terrain?)
if (enemies.Length > 0)
    ResolveCombat(myUnitId, enemies);  // Your implementation
```

ENGINE provides: `UnitSystem`, `UnitQueryBuilder`, `EventBus` for combat events, `Commands` for state changes.
GAME decides: Combat resolution, morale, terrain bonuses, retreats, casualties.
