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
├── Commands/               # Console commands for state changes
├── State/                  # Player state and events
├── Systems/                # Game systems (economy, units, buildings, AI)
├── UI/                     # All UI components
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

---

## State (`State/`)

| File | Purpose |
|------|---------|
| **PlayerState** | Tracks player's selected country |
| **PlayerEvents** | Player-specific events (country selected) |
| **StarterKitEvents** | Game events (GoldChanged, BuildingConstructed) |

---

## Commands (`Commands/`)

All state changes go through commands (Pattern 2):

| Command | Description |
|---------|-------------|
| `add_gold <amount>` | Add/remove gold from treasury |
| `create_unit <type> <province>` | Spawn a unit |
| `move_unit <unitId> <province>` | Move unit to province |
| `disband_unit <unitId>` | Remove a unit |
| `build <type> <province>` | Construct a building |

Commands use ENGINE infrastructure (`Core.Commands`).

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
| **LedgerUI** | Country statistics table (L key) |
| **ToolbarUI** | Top-right buttons (Ledger, Save, Load) |

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

## Data Files

Located in `Assets/Archon-Engine/Template-Data/`:

```
units/          - Unit type definitions (*.json5)
buildings/      - Building type definitions (*.json5)
```

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

- **Pattern 2 (Command)** - All state changes through commands
- **Pattern 3 (Event-Driven)** - EventBus subscriptions, zero-allocation events
- **Pattern 14 (Save/Load)** - Binary serialization with callbacks
- **Pattern 15 (Phase Init)** - Coroutine-based initialization
- **Pattern 19 (UI Presenter)** - Separated view/presenter components
