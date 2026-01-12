# StarterKit

**Purpose:** Minimal working game template demonstrating Archon-Engine patterns.

---

## What Is StarterKit?

A lightweight implementation showing how to build a game on Archon-Engine. Use it as:
- Learning reference for ENGINE patterns
- Starting point for new games
- Test bed for ENGINE features

---

## Core Systems

| System | Purpose |
|--------|---------|
| **Initializer** | Entry point, coordinates all StarterKit systems |
| **PlayerState** | Tracks player's selected country |
| **EconomySystem** | Simple gold economy (1 gold/province/month + building bonuses) |
| **UnitSystem** | Military units with movement and combat stats |
| **BuildingSystem** | Province buildings that provide bonuses |
| **AISystem** | Basic AI that builds in provinces |

---

## Commands

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

## UI Components

| Component | Purpose |
|-----------|---------|
| **CountrySelectionUI** | Initial country picker |
| **ResourceBarUI** | Gold display with income |
| **TimeUI** | Date and speed controls |
| **ProvinceInfoUI** | Selected province details |
| **UnitInfoUI** | Unit creation and info |
| **BuildingInfoUI** | Building construction |

---

## Data Files

Located in `Assets/Archon-Engine/Template-Data/`:

```
units/          - Unit type definitions (*.json5)
buildings/      - Building type definitions (*.json5)
```

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

---

## Architecture Patterns Used

- **Pattern 2 (Command)** - All state through commands
- **Pattern 3 (Event-Driven)** - EventBus subscriptions
- **Pattern 15 (Phase Init)** - Coroutine-based initialization
- **Pattern 19 (UI Presenter)** - Separated UI components
