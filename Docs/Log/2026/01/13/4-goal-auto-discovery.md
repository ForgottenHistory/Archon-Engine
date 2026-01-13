# Goal Auto-Discovery
**Date**: 2026-01-13
**Session**: 4
**Status**: Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Add auto-discovery for AI goals (same pattern as Commands)
- Prepare engine for larger-scale game development

**Success Criteria:**
- Goals marked with `[Goal]` attribute
- `AIGoalDiscovery.DiscoverAndRegister()` scans assemblies
- GAME layer no longer manually registers each goal

---

## Context & Background

**Previous Work:**
- See: [3-fluent-validation-and-initializer-instance.md](3-fluent-validation-and-initializer-instance.md)
- Commands already have auto-discovery via `[Command]` attribute

**Current State:**
- Only 2 AI goals, but engine designed for reuse
- Manual registration doesn't scale as goal count grows

---

## What We Did

### 1. Created GoalAttribute

**File: `Core/AI/GoalAttribute.cs`**

```csharp
[Goal(Description = "...", Category = "Economy")]
public class BuildEconomyGoal : AIGoal { ... }
```

| Property | Purpose |
|----------|---------|
| `Description` | What the goal does (for debugging) |
| `Category` | Grouping (Economy, Military, Diplomacy) |

### 2. Created AIGoalDiscovery

**File: `Core/AI/AIGoalDiscovery.cs`**

```csharp
// Scan assemblies for [Goal] classes and register them
AIGoalDiscovery.DiscoverAndRegister(
    aiSystem.GoalRegistry,
    typeof(BuildEconomyGoal).Assembly  // GAME assembly
);
```

Follows same pattern as `SimpleCommandFactory.DiscoverAndRegister()`.

### 3. Updated Goals with Attributes

| Goal | Attribute |
|------|-----------|
| `BuildEconomyGoal` | `[Goal(Description = "Economic development via building construction", Category = "Economy")]` |
| `ExpandTerritoryGoal` | `[Goal(Description = "Territorial expansion via war", Category = "Military")]` |

### 4. Updated AITickHandler

**Before:**
```csharp
aiSystem.RegisterGoal(new BuildEconomyGoal());
aiSystem.RegisterGoal(new ExpandTerritoryGoal());
```

**After:**
```csharp
AIGoalDiscovery.DiscoverAndRegister(
    aiSystem.GoalRegistry,
    typeof(BuildEconomyGoal).Assembly
);
```

### 5. Added AISystem.GoalRegistry Property

Changed `GetGoalRegistry()` method to `GoalRegistry` property for cleaner access.

---

## Quick Reference for Future Claude

**Adding New AI Goals:**
1. Create class extending `AIGoal` in GAME layer
2. Add `[Goal]` attribute (optionally with Description/Category)
3. Implement `GoalName`, `Evaluate()`, `Execute()`
4. Goal auto-discovered at startup

**Key Files:**
- `Core/AI/GoalAttribute.cs` - Attribute for marking goals
- `Core/AI/AIGoalDiscovery.cs` - Assembly scanning and registration
- `Core/AI/AISystem.cs` - `GoalRegistry` property
- `Game/AI/AITickHandler.cs` - Uses auto-discovery

**Pattern Consistency:**
| Type | Attribute | Discovery Method |
|------|-----------|------------------|
| Commands | `[Command]` | `SimpleCommandFactory.DiscoverAndRegister()` |
| Goals | `[Goal]` | `AIGoalDiscovery.DiscoverAndRegister()` |

---

## Links & References

### Related Sessions
- [Previous: Fluent Validation](3-fluent-validation-and-initializer-instance.md)
- [Session 1: SimpleCommand Infrastructure](1-simple-command-infrastructure.md) - Same pattern for commands

---

*Auto-discovery pattern ensures engine scales: add [Goal] attribute, goal is registered automatically.*
