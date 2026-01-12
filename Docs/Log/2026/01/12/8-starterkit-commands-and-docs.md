# StarterKit Commands & Documentation
**Date**: 2026-01-12
**Session**: 8
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Implement Commands system in StarterKit following GAME layer patterns

**Secondary Objectives:**
- Move shared command infrastructure from GAME to ENGINE
- Create architecture documentation for commands
- Create documentation for StarterKit

**Success Criteria:**
- StarterKit commands compile and follow ENGINE patterns
- Command infrastructure reusable by both GAME and StarterKit

---

## Context & Background

**Previous Work:**
- See: [7-pluggable-shader-compositor.md](7-pluggable-shader-compositor.md)
- Earlier session standardized architecture docs (removed code examples, made timeless)

**Current State:**
- StarterKit had systems (Economy, Units, Buildings) but no command pattern
- GAME layer had command infrastructure that should be shared

**Why Now:**
- Commands are Pattern 2 - core architectural pattern
- StarterKit should demonstrate proper ENGINE patterns

---

## What We Did

### 1. Moved Command Infrastructure to ENGINE

**Files Created:**
- `Core/Commands/CommandMetadataAttribute.cs` - Attribute for auto-discovery
- `Core/Commands/ICommandFactory.cs` - Factory interface
- `Core/Commands/CommandRegistry.cs` - Multi-assembly discovery registry

**Files Deleted from GAME:**
- `Game/Commands/CommandMetadataAttribute.cs`
- `Game/Commands/ICommandFactory.cs`
- `Game/Commands/CommandRegistry.cs`

**Rationale:**
- Shared infrastructure enables both GAME and StarterKit to use same pattern
- Registry now supports scanning multiple assemblies
- Configurable log subsystem per registry instance

### 2. Created StarterKit Commands

**Files Created in `StarterKit/Commands/`:**
- `AddGoldCommand.cs` + factory - Add/remove gold
- `CreateUnitCommand.cs` + factory - Spawn units
- `MoveUnitCommand.cs` + factory - Move units
- `DisbandUnitCommand.cs` + factory - Remove units
- `ConstructBuildingCommand.cs` + factory - Build buildings

**Pattern Used:**
- Commands extend `Core.Commands.BaseCommand`
- Factories implement `Core.Commands.ICommandFactory`
- Use `[CommandMetadata]` attribute for auto-discovery
- Access StarterKit systems via `FindFirstObjectByType<Initializer>()`

### 3. Created Architecture Documentation

**Files Created:**
- `Docs/Engine/command-system-architecture.md` - Command pattern architecture
- `Docs/Engine/architecture-doc-standards.md` - Meta-guide for writing docs

### 4. Created StarterKit Documentation

**Files Created:**
- `Scripts/StarterKit/README.md` - Comprehensive StarterKit guide
- `Scenes/README.md` - Scene overview pointing to StarterKit

---

## Decisions Made

### Decision 1: Keep Factory Interface Simple
**Context:** StarterKit needs Initializer access, GAME uses GetGameSystem<T>()
**Options Considered:**
1. Generic factory interface with TContext
2. Object context parameter
3. Keep GameState only, find other context via FindFirstObjectByType

**Decision:** Option 3 - Keep interface simple
**Rationale:** Both layers have GameState, can find additional context when needed
**Trade-offs:** Slight overhead from FindFirstObjectByType in factories

### Decision 2: Multi-Assembly Registry
**Context:** Commands split across ENGINE, GAME, StarterKit assemblies
**Decision:** CommandRegistry accepts Assembly[] in DiscoverCommands()
**Rationale:** Each layer can have its own commands discovered together

---

## Problems Encountered & Solutions

### Problem 1: UnitState Property Names
**Symptom:** Compile errors - `UnitState` has no `UnitID`, `ProvinceID`
**Root Cause:** UnitState uses lowercase field names (`provinceID`, `unitTypeID`)
**Solution:** Fixed property references to use lowercase names
**Pattern for Future:** Check actual struct definitions before using

### Problem 2: Unit Existence Check
**Symptom:** No `HasUnit()` method in StarterKit's UnitSystem
**Solution:** Check `unit.strength > 0` (default struct has strength=0)
**Pattern for Future:** Default struct values can indicate non-existence

---

## Architecture Impact

### Documentation Updates Made
- ✅ Created `command-system-architecture.md`
- ✅ Created `architecture-doc-standards.md`
- ✅ Updated `Core/FILE_REGISTRY.md` with new command files
- ✅ Created `StarterKit/README.md`
- ✅ Created `Scenes/README.md`

### Files Changed Summary
**ENGINE (Core/Commands/):**
- `CommandMetadataAttribute.cs` (new)
- `ICommandFactory.cs` (new)
- `CommandRegistry.cs` (new)

**StarterKit/Commands/:**
- `AddGoldCommand.cs` (new)
- `CreateUnitCommand.cs` (new)
- `MoveUnitCommand.cs` (new)
- `DisbandUnitCommand.cs` (new)
- `ConstructBuildingCommand.cs` (new)

---

## Next Session

### Immediate Next Steps
1. Update GAME layer to use ENGINE command infrastructure (add `using Core.Commands;`)
2. Hook up CommandRegistry in StarterKit Initializer for console support
3. Test StarterKit commands in-game

### Deferred
- GAME layer namespace updates (user chose to skip for now)

---

## Quick Reference for Future Claude

**What Changed:**
- Command infrastructure moved from GAME to ENGINE
- StarterKit now has 5 commands following Pattern 2
- Two new architecture docs created

**Key Files:**
- Command infrastructure: `Core/Commands/`
- StarterKit commands: `StarterKit/Commands/`
- Architecture docs: `Docs/Engine/command-system-architecture.md`, `architecture-doc-standards.md`

**Gotchas:**
- UnitState fields are lowercase (`provinceID` not `ProvinceID`)
- Check `strength > 0` to verify unit exists
- GAME layer still needs `using Core.Commands;` added to DebugCommandExecutor

---

## Links & References

### Related Documentation
- [Command System Architecture](../../Engine/command-system-architecture.md)
- [Architecture Doc Standards](../../Engine/architecture-doc-standards.md)
- [StarterKit README](../../../Scripts/StarterKit/README.md)

### Related Sessions
- [Previous: Pluggable Shader Compositor](7-pluggable-shader-compositor.md)

---

*Session focused on bringing Pattern 2 (Commands) to StarterKit with shared ENGINE infrastructure.*
