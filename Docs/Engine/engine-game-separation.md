# Engine-Game Separation

**Status:** Production Standard

---

## Core Principle

**ENGINE provides mechanisms (HOW), GAME defines policy (WHAT).**

ENGINE is a reusable foundation. GAME is the specific implementation. The same ENGINE should support different games (space strategy, fantasy conquest, modern warfare) with different policies.

---

## The Separation

### ENGINE Layer (Mechanism)
- Generic primitives and data structures
- Rendering infrastructure
- Input handling
- Networking infrastructure
- Save/load framework
- Command execution

**ENGINE asks:** "How do I store province state?" "How do I render a map?" "How do I process commands?"

### GAME Layer (Policy)
- Game-specific formulas and rules
- Colors, visuals, UI content
- AI behavior and goals
- Victory conditions
- Balance values

**GAME asks:** "What is the tax formula?" "What color is France?" "When does a country surrender?"

---

## Why This Matters

### Reusability
Same ENGINE can power different games:
- Grand strategy (EU4-like)
- Space 4X
- Fantasy conquest
- Modern political simulation

Each game implements different policies using the same mechanisms.

### Maintainability
Changes to game balance don't touch ENGINE code. Changes to rendering don't affect game rules. Clear boundaries reduce bugs.

### Testability
ENGINE mechanisms can be tested in isolation. GAME policies can be tested with mock ENGINE.

---

## Extension Points

ENGINE provides interfaces that GAME implements:

| Category | ENGINE Provides | GAME Implements |
|----------|-----------------|-----------------|
| Game Systems | IGameSystem lifecycle | Economy, Buildings, Military |
| Map Modes | IMapModeHandler contract | Political, Economic, Diplomatic modes |
| Commands | ICommand interface | DeclareWar, BuildBuilding, etc. |
| Definitions | IDefinition loading | Building types, unit types, etc. |
| Rendering | IRenderer interfaces | Custom borders, fog, effects |

---

## Import Rules

Strict hierarchy prevents circular dependencies:

**CORE (Simulation):**
- Cannot import Map or Game
- Pure simulation logic
- Deterministic operations only

**MAP (Presentation):**
- Can import Core
- Cannot import Game
- Reads simulation state for rendering

**GAME (Policy):**
- Can import Core and Map
- Defines all game-specific behavior
- Owns initialization order

**Rule:** Dependencies flow downward only. CORE → MAP → GAME.

---

## Data Ownership

### ENGINE Owns
- Province state primitives (owner, controller, terrain)
- Country state primitives (exists, tag)
- Command processing
- Event bus
- Time management

### GAME Owns
- Game-specific province data (development, buildings)
- Game-specific country data (treasury, relations)
- All formulas and balance values
- UI content and styling
- AI decision making

---

## Initialization Pattern

GAME layer controls all initialization order:
- ENGINE systems initialize through GAME coordinator
- Avoids rogue Start/Awake methods
- Clear dependency chain
- Predictable load order

---

## Common Mistakes

**Putting policy in ENGINE:**
- Tax formula in ENGINE → Should be in GAME
- Country colors in ENGINE → Should be in GAME
- Building definitions in ENGINE → Should be in GAME

**Putting mechanism in GAME:**
- Province storage reimplemented → Use ENGINE's ProvinceSystem
- Custom event system → Use ENGINE's EventBus
- Duplicate rendering logic → Use ENGINE's pluggable renderers

---

## Design Principles

### Mechanisms, Not Policy
ENGINE provides generic state access. GAME defines what to do with state.

### Flexible, But Opinionated
- Opinionated: "Use FixedPoint64 for determinism"
- Flexible: "But you define the formulas"

### Abstract Hard Problems
ENGINE solves: determinism, performance, state management, events, persistence.
GAME focuses on: gameplay, content, balance.

### Zero Game Logic in ENGINE
No game-specific concepts (farms, armies, trade) in ENGINE code. Only generic concepts: provinces, countries, commands, events.

---

## Trade-offs

| Aspect | Benefit | Cost |
|--------|---------|------|
| Strict separation | Reusable ENGINE | More interfaces to implement |
| Interface contracts | Clear boundaries | Indirection overhead |
| Layer hierarchy | No circular deps | Can't call "up" the hierarchy |

---

## Success Metric

**Can you build a different game in one week using the same ENGINE?**

If yes: Separation is working.
If no: Too much policy leaked into ENGINE.

---

## Related Patterns

- **Pattern 1 (Engine-Game Separation):** This document
- **Pattern 20 (Pluggable Implementation):** How GAME extends ENGINE rendering
- **Pattern 2 (Command Pattern):** How state changes flow through layers

---

*Mechanism belongs in ENGINE. Policy belongs in GAME. Never mix them.*
