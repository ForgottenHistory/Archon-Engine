# Command System Architecture

**Status:** Production Standard

---

## Core Principle

**All state modifications flow through commands for validation, networking, replay, and undo support.**

---

## The Problem

Direct state modification causes:
- No validation before changes
- No networking support (can't sync state changes)
- No replay capability (can't record/playback games)
- No undo support (can't revert mistakes)
- Hidden side effects (changes scattered across codebase)

---

## The Solution: Command Pattern

Every state change is encapsulated in a command object:
- **Validate** before execution (fast, no side effects)
- **Execute** with deterministic behavior
- **Undo** to revert changes
- **Serialize** for network transmission and save files

Commands are the single point of entry for all game state modifications.

---

## Architecture Layers

### ENGINE Layer (Mechanism)
Provides the infrastructure:
- **ICommand** - Base interface for all commands
- **BaseCommand** - Abstract base with common utilities
- **ICommandFactory** - Creates commands from string input
- **CommandMetadataAttribute** - Marks factories for auto-discovery
- **CommandRegistry** - Discovers and indexes command factories

### GAME/StarterKit Layer (Policy)
Implements concrete commands:
- Specific command classes (AddGoldCommand, CreateUnitCommand, etc.)
- Factory implementations with argument parsing
- Business logic and validation rules

---

## Component Responsibilities

### ICommand
- `Validate(GameState)` - Check if command can execute (< 1ms, no side effects)
- `Execute(GameState)` - Perform the state change
- `Undo(GameState)` - Revert the state change
- `Serialize/Deserialize` - Binary representation for network/save

### ICommandFactory
- Parse string arguments into command instances
- Provide user-friendly error messages
- Handle optional parameters and defaults

### CommandRegistry
- Auto-discover factories via reflection
- Index by command name and aliases
- Generate help text for console

### CommandMetadataAttribute
- Define command name, aliases, description
- Provide usage examples for help system
- Enable auto-registration

---

## Command Lifecycle

1. **Creation** - Factory parses input, creates command instance
2. **Validation** - Command checks preconditions against game state
3. **Execution** - Command modifies state, emits events
4. **Recording** - Command serialized for replay/network
5. **Undo** (optional) - Command reverts its changes

---

## Validation Rules

Commands must validate:
- Entity existence (province, country, unit IDs)
- Ownership and permissions
- Resource availability
- Game rules and constraints

**Validation must be:**
- Fast (< 1ms)
- Side-effect free
- Deterministic

---

## Determinism Requirements

For multiplayer and replay:
- Same command + same state = same result
- No floating-point in simulation logic
- No random without seeded RNG
- No external state dependencies

---

## When to Use Commands

**Use Commands For:**
- Any persistent state change
- Player actions
- AI decisions
- Anything that needs network sync

**Don't Use Commands For:**
- UI state (selection, hover)
- Presentation-only changes
- Queries (use Query layer)
- Transient calculations

---

## Factory Pattern Benefits

Factories separate:
- **Parsing** - String to typed parameters
- **Validation** - Parameter-level checks
- **Construction** - Command instantiation

This enables:
- Console/debug integration
- Scripting support
- Network command reconstruction

---

## Anti-Patterns

| Don't | Do Instead |
|-------|------------|
| Modify state directly | Create and execute command |
| Skip validation | Always validate before execute |
| Non-deterministic logic | Use fixed-point math, seeded RNG |
| Heavy validation | Keep validation fast (< 1ms) |
| Side effects in validation | Validation is read-only |
| Forget undo support | Store previous state for reversal |

---

## Key Trade-offs

| Aspect | Benefit | Cost |
|--------|---------|------|
| Command abstraction | Networking, replay, undo | Boilerplate per command |
| Factory pattern | Console integration | Additional classes |
| Auto-discovery | Zero registration code | Reflection at startup |
| Serialization | Save/load, network | Serialize/deserialize methods |

---

## Multi-Assembly Support

CommandRegistry can scan multiple assemblies:
- ENGINE commands (if any)
- GAME-specific commands
- Mod/plugin commands

Each layer provides its own commands using shared ENGINE infrastructure.

---

## Integration Points

### Console/Debug
- Registry provides command lookup
- Factories parse user input
- Results displayed to user

### Network
- Commands serialized and transmitted
- Remote execution with same validation
- Deterministic results across clients

### Replay
- Commands recorded with timestamps
- Playback recreates game state
- Useful for debugging and spectating

### Save/Load
- Command log can supplement state snapshots
- Enables verification of save integrity

---

## Summary

1. **All state changes through commands** - Single point of modification
2. **Validate before execute** - Fast, side-effect free checks
3. **Deterministic execution** - Same input = same output
4. **Serializable** - Network and save support
5. **Undo support** - Revert capability
6. **Factory pattern** - Console and scripting integration
7. **Auto-discovery** - Zero-config registration

---

## Related Patterns

- **Pattern 2 (Command Pattern)** - Core architecture principle
- **Pattern 3 (Event-Driven)** - Commands emit events after execution
- **Pattern 5 (Fixed-Point)** - Deterministic math in commands
- **Pattern 17 (Single Source of Truth)** - Commands are the truth for state changes

---

*Commands are the gateway to state. No modification bypasses them.*
