# Decision: Lua Scripting on Lua-CSharp, Objects with Methods
**Date:** 2026-11-27
**Status:** ✅ Implemented
**Impact:** Breaking change (the January scripting API is removed)
**Replaces:** the MoonSharp layer of 2026-01-16 (`Docs/Log/2026/01/16/08-lua-scripting-system.md`)

---

## Decision Summary

**Changed:** Scripted content is Lua, run by the Lua-CSharp interpreter behind an API of our own (`ScriptEngine`). Scripts hold the GAME's objects and call methods on them; the GAME keeps the Lua functions scripts register and calls them later.
**Reason:** Lua is common (documented, tooled, written well by people and by AI assistants), and the first scripting layer was unused, unmaintained underneath, and shaped for one-line code strings.
**Trade-off:** A third-party interpreter in the engine (three DLLs), and scripts that are code, so they can be wrong in more ways than data.

---

## Context

**Problem:** A game needs thousands of scripted definitions (focuses, events, decisions), each with several functions the game calls at decision points, inside the simulation.
**Constraints:** Deterministic and synchronous (scripts run inside commands and ticks); no allocation worth mentioning per call; content may be converted from another game and so be far ahead of what the game implements.
**What existed:** A `Scripting` assembly on MoonSharp behind a `MOONSHARP_ENABLED` define that no project set. It had not compiled since it was written. It evaluated code strings (`Execute(code)`, `EvaluateCondition("...")`) against a context of one province and one country, and offered a fixed-point number type inside Lua.

---

## Options Considered

### Option 1: Interpret another game's script format directly
**Approach:** Keep converted content in its original declarative format and write an interpreter for it.
**Pros:** No translation step; data is easy to analyse (which effects does this have?) without running it.
**Cons:** A format with one user. No tooling, no documentation but ours, awkward to write by hand or by AI. Every construct (scopes, iterators, variables) is ours to design and maintain.
**Rejected:** The format is the long-term cost, and content gets written and edited far more often than the interpreter.

### Option 2: Keep MoonSharp
**Approach:** Enable the existing layer and extend it.
**Pros:** Already written.
**Cons:** MoonSharp's last stable release is 2.0.0; its Unity package fork was archived in 2022. The existing API compiled code on every call.
**Rejected:** Building thousands of scripts on an interpreter nobody maintains.

### Option 3: Lua-CSharp behind our own API (chosen)
**Approach:** Lua 5.2 in pure C# (MIT, maintained), hidden completely behind `ScriptEngine`, `ScriptCall`, `ScriptType`, `ScriptObject`, `ScriptValue`, `ScriptTable`, `ScriptFunctionRef`.
**Pros:** Fast (about a microsecond for a function with two C# calls, no allocation), works on netstandard2.1, and replaceable: no Lua-CSharp type is public.
**Cons:** Its API is async; three DLLs ship in the engine.

---

## Design Choices

- **Objects with methods, not a context.** A script function gets the object it is about (`function(c) ... end`) and calls `c:add_gold(5)`. Scope changes are ordinary values (`s:owner()`), where the old design had one implicit province and country.
- **Functions are compiled once and kept.** Scripts register tables; the GAME stores the functions in them (`ScriptFunctionRef`) and calls them when needed.
- **The engine defines no vocabulary.** Every function and method scripts can call is the GAME's. The old `CoreBindings` (province and country queries defined by the engine) are gone.
- **Arithmetic stays in C#.** Scripts pass numbers, mostly literals; C# converts to fixed point and computes. The old `LuaFixed` type moved simulation maths into scripts.
- **State stays in C#.** Lua variables are not saved or synchronised; flags and variables go through GAME functions.
- **Synchronous only.** Lua-CSharp returns `ValueTask`; a call whose C# functions never await completes at once, and the engine treats anything else as an error.
- **Failures never escape.** An error returns false with file and line; an endless loop is cut off by an instruction limit; the engine stays usable.
- **Missing methods are reported, not fatal.** The call returns false and the name is listed once. Content can be ahead of the game, and the list is the to-do list.

---

## Consequences

**Good:**
- Content is in a language with editors, linters, documentation and AI fluency.
- The interpreter can be swapped by changing one assembly.
- The assembly always compiles and is covered by tests (`ScriptEngineTests`).

**To live with:**
- Scripts cannot be analysed without running them. A tooltip that lists what an effect will do needs the GAME to run the effect in a "describe" mode where its functions record instead of act.
- A missing trigger answers false, so a negation of it answers true. `ScriptEngine.MissingCalls` lets a caller tell "false" from "can't tell" where that matters.
- Lua numbers are doubles: determinism depends on the GAME keeping its maths in C#.

---

## References

- `Docs/Engine/scripting.md` (architecture), `Docs/Wiki/Scripting.md` (how-to)
- `Docs/Log/2026/11/27/2-scripting-rebuilt-on-lua-csharp.md` (the session)
- `Scripts/Scripting/Plugins/README.md` (the DLLs, their versions and sources)
