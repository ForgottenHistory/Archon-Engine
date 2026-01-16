# Scripting Assembly File Registry
**Namespace:** `Scripting.*`
**Assembly:** `Scripting` (optional, requires `MOONSHARP_ENABLED` define)
**Dependencies:** Core, Utils, MoonSharp

---

## Overview
Deterministic Lua scripting via MoonSharp. Scripts declare intent, C# executes.

**To Enable:**
1. Install MoonSharp: `https://github.com/k0dep/MoonSharp.git#2.0.0`
2. Add `MOONSHARP_ENABLED` to Player Settings → Scripting Define Symbols

---

## Root/
- **Scripting.ScriptEngine** - Central MoonSharp coordinator; manages execution, binding registration, sandboxing
- **Scripting.ScriptContext** - Per-execution environment with scope (province/country), GameState access, ICommandSubmitter
- **Scripting.ScriptSandbox** - Security configuration; whitelists safe modules, blocks io/os/debug/load
- **Scripting.ScriptResult** - Execution result struct with success/error and DynValue
- **Scripting.ICommandSubmitter** - Interface for submitting commands from scripts (implemented in GAME layer)

---

## Types/
- **Scripting.Types.LuaFixed** - [MoonSharpUserData] FixedPoint64 wrapper for deterministic Lua math
- **Scripting.Types.LuaProvinceId** - [MoonSharpUserData] ProvinceId wrapper for type-safe province IDs
- **Scripting.Types.LuaCountryId** - [MoonSharpUserData] CountryId wrapper for type-safe country IDs

---

## Bindings/
- **Scripting.Bindings.IScriptBinding** - Interface for registering Lua functions with script engine
- **Scripting.Bindings.CoreBindings** - ENGINE bindings: province_owner, province_controller, country_province_count, this_province, this_country, current_tick

---

## Triggers/
- **Scripting.Triggers.IScriptTrigger** - Interface for trigger definitions (ShouldFire, GetExecutionContext)
- **Scripting.Triggers.ScriptTriggerRegistry** - Central registry for triggers and handlers; fires triggers, manages priorities
- **Scripting.Triggers.ScriptHandler** - Registered handler struct: Script, Condition, Priority, Source

---

## Lua API (CoreBindings)

| Function | Returns | Description |
|----------|---------|-------------|
| `province_owner(province)` | CountryId | Get province owner |
| `province_controller(province)` | CountryId | Get province controller |
| `province_is_valid(province)` | bool | Check if province exists |
| `country_is_valid(country)` | bool | Check if country exists |
| `country_province_count(country)` | int | Count provinces owned |
| `this_province` | ProvinceId | Current scope province |
| `this_country` | CountryId | Current scope country |
| `current_tick` | number | Current game tick |

## Lua Types

| Type | Factory | Methods |
|------|---------|---------|
| `Fixed` | `FromInt(n)`, `FromFraction(num, denom)` | All operators, `Abs`, `Min`, `Max`, `Sqrt`, `Lerp`, etc. |
| `ProvinceId` | `Create(n)`, `None` | `IsValid()`, `IsNone()` |
| `CountryId` | `Create(n)`, `None` | `IsValid()`, `IsNone()` |

---

*Updated: 2026-01-16*
*Created: Scripting assembly with MoonSharp Lua integration*
