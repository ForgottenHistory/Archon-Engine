# Wiki Documentation Plan

## Current State
- Home.md - Landing page
- Getting-Started.md - Setup + architecture basics
- Your-First-Game.md - Step-by-step tutorial

## Proposed Structure

### Core Feature Guides
| Page | Covers | StarterKit Reference |
|------|--------|---------------------|
| Commands.md | Command pattern, validation, creating commands | AddGoldCommand, ConstructBuildingCommand, MoveUnitCommand |
| Events.md | EventBus, subscribing, custom events | StarterKitEvents, PlayerEvents |
| Economy.md | Resources, gold, income | EconomySystem, ResourceBarUI |
| Buildings.md | Building system, construction | BuildingSystem, BuildingInfoUI |
| Units.md | Unit system, movement, combat | UnitSystem, UnitVisualization, UnitInfoUI |
| AI.md | AI goals, scheduling, evaluation | AISystem |
| Diplomacy.md | Relations, wars, treaties | DiplomacyPanel |
| Map-Modes.md | Custom map modes, colorization | FarmDensityMapMode, TerrainCostMapMode |
| Map-Rendering.md | Textures, borders, visual styles | ENGINE level |
| Save-Load.md | Saving, loading, serialization | ENGINE level |
| Modifiers.md | Modifier system, bonuses/penalties | ModifierType |
| Time-System.md | Ticks, game speed, calendar | TimeUI |
| UI-Patterns.md | UI toolkit, presenter pattern | ProvinceInfoUI, CountrySelectionUI |
| Localization.md | Multi-language, YAML parsing | ENGINE level |
| Pathfinding.md | A* pathfinding, movement | UnitMoveHandler |
| Queries.md | Fluent query API | ENGINE level |

### Reference Pages
| Page | Purpose |
|------|---------|
| Architecture-Overview.md | Dual-layer, ENGINE vs GAME separation |
| Troubleshooting-Common.md | Setup issues, initialization |
| Troubleshooting-Performance.md | Performance optimization |
| Troubleshooting-Rendering.md | Map/texture issues |

## Principles
- One feature = one page
- Each page links to StarterKit examples
- Troubleshooting split by domain
- No Hegemon-specific content in ENGINE wiki

## Priority Order
1. Commands.md - Foundation for all state changes
2. Events.md - Foundation for system communication
3. Economy.md - Most common first feature
4. Buildings.md - Extends economy
5. Units.md - Core gameplay
6. Map-Modes.md - Visual customization
7. AI.md - Advanced feature
8. Rest as needed
