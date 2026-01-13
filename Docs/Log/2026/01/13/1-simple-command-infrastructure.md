# SimpleCommand Infrastructure
**Date**: 2026-01-13
**Session**: 1
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Reduce command boilerplate for game developers

**Secondary Objectives:**
- Identify Core layer usability improvements
- Create simplified command pattern that coexists with BaseCommand

**Success Criteria:**
- Single-class commands with auto-generated factories
- 40%+ code reduction for typical commands

---

## Context & Background

**Previous Work:**
- See: [8-starterkit-commands-and-docs.md](../12/8-starterkit-commands-and-docs.md)
- StarterKit commands created with BaseCommand pattern

**Current State:**
- Creating a command required 2 classes (~100+ lines)
- Manual serialization, factory, argument parsing

**Why Now:**
- Preparing ENGINE for external developers
- Commands are most frequently created thing - high impact improvement

---

## What We Did

### 1. Identified Core Usability Improvements

Explored Core namespace extension points and identified friction:
- Command boilerplate (2 classes per command)
- Manual registration everywhere
- Event subscription lifecycle management
- Inconsistent service access patterns

Prioritized: **SimpleCommand** as highest impact improvement.

### 2. Created SimpleCommand Infrastructure

**Files Created in `Core/Commands/`:**

| File | Purpose |
|------|---------|
| `CommandAttribute.cs` | `[Command]` attribute with name, aliases, description |
| `ArgAttribute.cs` | `[Arg]` attribute for property-based arguments |
| `SimpleCommand.cs` | Base class with auto-serialization |
| `SimpleCommandFactory.cs` | Reflection-based factory generation |

**Key Features:**
- Auto-serialization of `[Arg]` properties
- Auto-generated argument parsing
- Auto-generated usage/help text
- Optional Undo (default no-op)
- Coexists with BaseCommand for complex cases

### 3. Converted StarterKit Commands

All 5 commands converted to SimpleCommand:

| Command | Before | After | Reduction |
|---------|--------|-------|-----------|
| AddGoldCommand | 124 | 73 | 41% |
| CreateUnitCommand | 113 | 69 | 39% |
| MoveUnitCommand | 109 | 67 | 39% |
| DisbandUnitCommand | 100 | 63 | 37% |
| ConstructBuildingCommand | 99 | 54 | 45% |
| **Total** | **545** | **326** | **40%** |

---

## Decisions Made

### Decision 1: Two-Tier Command System
**Context:** Need simple commands but also full control for complex cases
**Decision:** Keep both SimpleCommand and BaseCommand
**Rationale:**
- SimpleCommand: 80% of commands (straightforward state changes)
- BaseCommand: 20% (custom serialization, complex undo, networking edge cases)

### Decision 2: Reflection-Based Factory Generation
**Context:** Could use source generators or manual registration
**Decision:** Runtime reflection with caching
**Trade-offs:**
- Pro: No build complexity, works immediately
- Con: Slight startup cost (acceptable for command discovery)

### Decision 3: Property-Based Arguments
**Context:** Could use constructor parameters or fields
**Decision:** Public properties with `[Arg]` attribute
**Rationale:**
- Clear intent via attribute
- Auto-serialization natural fit
- IDE autocomplete works well

---

## What Worked ✅

1. **Attribute-based design**
   - Clean, declarative syntax
   - Self-documenting commands
   - Easy to extend

2. **Property caching**
   - Reflection cached per type
   - No repeated reflection cost

---

## Problems Encountered & Solutions

### Problem 1: Func<> with out parameters
**Symptom:** C# doesn't support `Func<string, out object, out string, bool>`
**Solution:** Created custom `ArgParser` delegate

### Problem 2: Missing FixedPoint64 using
**Symptom:** Compile errors for FixedPoint64 type
**Solution:** Added `using Core.Data;` to SimpleCommand and SimpleCommandFactory

---

## Architecture Impact

### New Pattern: SimpleCommand
**When to use:**
- Standard state-changing commands
- Commands with 0-4 simple arguments
- When auto-serialization is sufficient

**When to use BaseCommand instead:**
- Custom binary serialization format
- Complex undo with multiple state captures
- Network-optimized commands
- Maximum performance (no reflection)

---

## Code Quality Notes

### Testing
- Manual test: All 5 StarterKit commands compile and work

### Technical Debt
- None created
- Reduced: Less boilerplate to maintain

---

## Next Session

### Potential Future Improvements
1. Fluent validation helpers
2. Scoped event subscriptions (auto-cleanup)
3. Auto-discovery for systems and goals (like commands)

---

## Quick Reference for Future Claude

**What Changed:**
- New `SimpleCommand` base class in ENGINE
- All StarterKit commands now use SimpleCommand
- 40% less code for typical commands

**Key Files:**
- `Core/Commands/SimpleCommand.cs` - Base class
- `Core/Commands/CommandAttribute.cs` - Class-level metadata
- `Core/Commands/ArgAttribute.cs` - Property arguments
- `Core/Commands/SimpleCommandFactory.cs` - Auto-factory generation

**Usage Pattern:**
```csharp
[Command("my_cmd", Description = "Does something")]
public class MyCommand : SimpleCommand
{
    [Arg(0, "amount")] public int Amount { get; set; }

    public override bool Validate(GameState gs) => /* ... */;
    public override void Execute(GameState gs) => /* ... */;
}
```

**Registration:**
```csharp
SimpleCommandFactory.DiscoverAndRegister(registry, assembly);
```

---

## Links & References

### Related Documentation
- [Command System Architecture](../../Engine/command-system-architecture.md)

### Related Sessions
- [Previous: StarterKit Commands](../12/8-starterkit-commands-and-docs.md)

---

*SimpleCommand reduces boilerplate by 40% while preserving full control via BaseCommand.*
