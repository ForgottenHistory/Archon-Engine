# Lua Scripting System
**Date**: 2026-01-16
**Session**: 08
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Add deterministic Lua scripting system to Archon Engine using MoonSharp

**Secondary Objectives:**
- Keep MoonSharp as optional dependency (not required for Core)
- Provide LuaFixed type wrapping FixedPoint64 for multiplayer-safe math
- Create trigger system for event-driven script execution

**Success Criteria:**
- Scripting assembly compiles and is optional
- Core has no MoonSharp dependency
- LuaFixed wraps FixedPoint64 for determinism

---

## Context & Background

**Previous Work:**
- See: [07-ui-infrastructure-refactor.md](07-ui-infrastructure-refactor.md)
- Plan created in plan mode for Lua scripting architecture

**Current State:**
- No scripting system existed
- Game logic hardcoded in C#

**Why Now:**
- Enable moddable events, AI logic, and game rules
- Scripts declare intent, C# executes deterministically

---

## What We Did

### 1. Created Scripting Assembly (Separate from Core)
**Files Changed:** `Scripting/Scripting.asmdef`

Created new assembly with references:
- Core (for GameState, IDs, FixedPoint64)
- Utils (for ArchonLogger)
- MoonSharp

Added `defineConstraints: ["MOONSHARP_ENABLED"]` so assembly only compiles when define is present.

### 2. Core Runtime Files
**Files Created:**
- `Scripting/ScriptEngine.cs` - Central MoonSharp coordinator
- `Scripting/ScriptContext.cs` - Per-execution environment with scope
- `Scripting/ScriptSandbox.cs` - Security configuration (blocks io/os/debug)

### 3. LuaFixed Type for Deterministic Math
**Files Created:** `Scripting/Types/LuaFixed.cs`

Wraps FixedPoint64 with:
- Factory methods: `FromInt()`, `FromFraction()`, `FromFixedPoint()`
- All operators: `+`, `-`, `*`, `/`, `%`, comparisons
- Math functions: `Abs`, `Min`, `Max`, `Clamp`, `Sqrt`, `Lerp`, `Floor`, `Ceiling`, `Round`
- Properties: `IsZero`, `IsPositive`, `IsNegative`, `Sign`

### 4. ID Type Wrappers
**Files Created:**
- `Scripting/Types/LuaProvinceId.cs` - ProvinceId wrapper
- `Scripting/Types/LuaCountryId.cs` - CountryId wrapper

Both provide `Create(int)` factory and `IsValid()`, `IsNone()` methods.

### 5. Binding Interface and Core Bindings
**Files Created:**
- `Scripting/Bindings/IScriptBinding.cs` - Interface for registering Lua functions
- `Scripting/Bindings/CoreBindings.cs` - ENGINE bindings

Core bindings provide:
- `province_owner(province)` → CountryId
- `province_controller(province)` → CountryId
- `province_is_valid(province)` → bool
- `country_is_valid(country)` → bool
- `country_province_count(country)` → int
- `this_province`, `this_country` → scope accessors
- `current_tick` → game time

### 6. Trigger System
**Files Created:**
- `Scripting/Triggers/IScriptTrigger.cs` - Interface for trigger definitions
- `Scripting/Triggers/ScriptTriggerRegistry.cs` - Central registry for triggers/handlers

Supports:
- Registering trigger types
- Registering handlers with conditions and priorities
- Firing triggers with context
- `FireTriggerForEach<T>()` for iterating collections

### 7. Removed MoonSharp from Core.asmdef
**Files Changed:** `Core/Core.asmdef`

Removed MoonSharp reference - Core now has no external dependencies beyond Unity packages.

---

## Decisions Made

### Decision 1: Separate Scripting Assembly
**Context:** MoonSharp could be a hard dependency or optional
**Options Considered:**
1. Hard dependency in Core - simpler but forces MoonSharp on all users
2. Conditional compilation (#if) - messy, hard to maintain
3. Separate assembly with defineConstraints - clean separation

**Decision:** Option 3 - Separate assembly
**Rationale:**
- Core stays pure with no external dependencies
- Games can opt-in to scripting by adding MOONSHARP_ENABLED define
- Clear architectural boundary

### Decision 2: Namespace Change from Core.Scripting to Scripting
**Context:** When moving to separate assembly, namespace needed update
**Decision:** Use `Scripting` namespace (not `Core.Scripting`)
**Rationale:** Matches assembly name, clear it's not part of Core

---

## What Worked ✅

1. **defineConstraints for Optional Compilation**
   - Assembly only compiles when MOONSHARP_ENABLED is defined
   - Clean way to make MoonSharp truly optional

2. **LuaFixed Wrapping FixedPoint64**
   - All operators work via MoonSharpUserData attribute
   - Deterministic math preserved for multiplayer

---

## Problems Encountered & Solutions

### Problem 1: MoonSharp Assembly Name
**Symptom:** `MoonSharp.Interpreter` not found in asmdef references
**Root Cause:** The UPM package uses assembly name `MoonSharp`, not `MoonSharp.Interpreter`
**Solution:** Changed reference from `MoonSharp.Interpreter` to `MoonSharp`

### Problem 2: Missing API (InstructionLimit)
**Symptom:** `ScriptOptions.InstructionLimit` not found
**Root Cause:** The MoonSharp UPM version doesn't have this API
**Solution:** Removed instruction limit code (sandbox still provides security)

### Problem 3: ArchonLogger Not Found
**Symptom:** After moving to Scripting assembly, ArchonLogger errors
**Root Cause:** ArchonLogger is in Utils assembly, not Core
**Solution:** Added Utils to Scripting.asmdef references

### Problem 4: ProvinceState Field Name
**Symptom:** `state.controllerId` not found
**Root Cause:** Field is named `controllerID` (capital ID)
**Solution:** Fixed to `state.controllerID`

---

## Architecture Impact

### New Assembly Structure
```
Archon-Engine/Scripts/
├── Core/           # No MoonSharp dependency
├── Utils/          # ArchonLogger, etc.
└── Scripting/      # Optional, requires MOONSHARP_ENABLED
    ├── ScriptEngine.cs
    ├── ScriptContext.cs
    ├── ScriptSandbox.cs
    ├── Types/
    │   ├── LuaFixed.cs
    │   ├── LuaProvinceId.cs
    │   └── LuaCountryId.cs
    ├── Bindings/
    │   ├── IScriptBinding.cs
    │   └── CoreBindings.cs
    └── Triggers/
        ├── IScriptTrigger.cs
        └── ScriptTriggerRegistry.cs
```

### To Enable Scripting
1. Install MoonSharp via UPM: `https://github.com/k0dep/MoonSharp.git#2.0.0`
2. Add `MOONSHARP_ENABLED` to Player Settings → Scripting Define Symbols
3. Reference `Scripting` assembly from Game layer

---

## Session Statistics

**Files Created:** 10
- Scripting.asmdef
- ScriptEngine.cs
- ScriptContext.cs
- ScriptSandbox.cs
- LuaFixed.cs
- LuaProvinceId.cs
- LuaCountryId.cs
- IScriptBinding.cs
- CoreBindings.cs
- IScriptTrigger.cs
- ScriptTriggerRegistry.cs

**Files Modified:** 2
- Core/Core.asmdef (removed MoonSharp)
- Game/Game.asmdef (removed MoonSharp - reverted during cleanup)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Scripting is OPTIONAL - only compiles with MOONSHARP_ENABLED
- Core has NO MoonSharp dependency
- All script math must use LuaFixed for determinism
- Scripts declare intent, C# executes via commands

**Key Files:**
- Entry point: `Scripting/ScriptEngine.cs`
- Deterministic math: `Scripting/Types/LuaFixed.cs`
- Context/scope: `Scripting/ScriptContext.cs`
- Bindings interface: `Scripting/Bindings/IScriptBinding.cs`

**To Add Game-Layer Bindings:**
```csharp
public class EconomyBindings : IScriptBinding
{
    public string BindingName => "Game.Economy";

    public void Register(Script luaScript, ScriptContext context)
    {
        luaScript.Globals["country_gold"] = (Func<LuaCountryId, LuaFixed>)(id =>
            GetGold(context, id));
    }
}
```

**Gotchas for Next Session:**
- MoonSharp UPM package assembly name is `MoonSharp`, not `MoonSharp.Interpreter`
- ProvinceState fields use capital ID suffix (ownerID, controllerID)
- IL2CPP builds may need link.xml to prevent stripping

---

## Links & References

### Code References
- ScriptEngine: `Scripting/ScriptEngine.cs`
- LuaFixed: `Scripting/Types/LuaFixed.cs`
- CoreBindings: `Scripting/Bindings/CoreBindings.cs`
- TriggerRegistry: `Scripting/Triggers/ScriptTriggerRegistry.cs`

### Related Sessions
- Previous: [07-ui-infrastructure-refactor.md](07-ui-infrastructure-refactor.md)

### External Resources
- MoonSharp UPM: `https://github.com/k0dep/MoonSharp.git#2.0.0`
- MoonSharp docs: http://www.moonsharp.org/

---

*Session Duration: ~45 minutes*
