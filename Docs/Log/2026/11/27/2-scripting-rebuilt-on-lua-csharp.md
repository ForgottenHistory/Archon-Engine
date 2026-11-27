# Scripting Rebuilt on Lua-CSharp
**Date**: 2026-11-27
**Session**: 2
**Status**: ✅ Complete (imports and compiles in the editor; the 13 tests pass in the Test Runner)
**Priority**: High

---

## Goal

Make the engine's Lua scripting usable for real content: thousands of scripted definitions, each with functions
the game calls at decision points, inside the simulation.

## Problems Found

### The scripting layer had never been in use
The `Scripting` assembly (January, session 08) was built on MoonSharp behind a `MOONSHARP_ENABLED` define. No
project enabled it, so it had not compiled since, and nothing called it.

### MoonSharp looks unmaintained
Its last stable release is 2.0.0; there is a lone 3.0 beta, and the Unity package fork was archived in 2022.

### The old design did not fit content with many functions
It evaluated code strings (`Execute(code)`, `EvaluateCondition("...")`), which compiles on every call, and passed
scope through a context object with one province and one country. Content that defines thousands of functions
needs them compiled once and called with the object they are about. Its fixed-point Lua type (`LuaFixed`) moved
arithmetic into scripts; keeping arithmetic in C# is simpler and safer.

## What Changed

- **Interpreter**: [Lua-CSharp](https://github.com/nuskey8/Lua-CSharp) 0.5.8 (Lua 5.2 in pure C#, MIT), as DLLs
  in `Scripts/Scripting/Plugins/`. The define is gone: the assembly always compiles.
- **New API** (`Docs/Engine/scripting.md`): `ScriptEngine`, `ScriptCall`, `ScriptType`, `ScriptObject`,
  `ScriptValue`, `ScriptTable`, `ScriptFunctionRef`. Scripts call methods on game objects (`c:add_gold(5)`);
  the game keeps Lua functions from tables scripts register and calls them later.
- **Removed**: `ScriptContext`, `ICommandSubmitter`, `ScriptSandbox`, `ScriptResult`, `IScriptBinding`,
  `CoreBindings`, `IScriptTrigger`, `ScriptTriggerRegistry`, `ScriptHandler`, `LuaFixed`, `LuaProvinceId`,
  `LuaCountryId`. The engine no longer defines any script function about game state; that is the game's.
- **Docs**: `Docs/Engine/scripting.md` (architecture), `Docs/Wiki/Scripting.md` (how-to),
  `Docs/Log/decisions/lua-scripting-on-lua-csharp.md` (the decision); the January session log is marked superseded.
- **Tests**: `Scripts/Tests/EditMode/ScriptEngineTests.cs` (13), and `Scripting` added to the Tests assembly.

## Decisions

- **Lua, not a format of our own.** It is common: documented, tooled, and AI assistants write it well.
- **The interpreter is hidden.** No Lua-CSharp type is public, so a game never references `Lua.dll` and a later
  swap touches one assembly.
- **Synchronous only.** Lua-CSharp's API is async (`ValueTask`). A call whose C# functions never await
  completes immediately; the engine checks that and treats anything else as an error. Scripts can therefore run
  inside commands and ticks.
- **Missing methods are reported, not fatal.** Content can be written or converted ahead of the game's
  vocabulary; `ScriptEngine.Missing` lists what is still owed. `LogMissing = false` keeps them out of the log
  when there are hundreds (first real use: 95 distinct names from a handful of scripts).
- **`MissingCalls` tells "false" from "can't tell".** A missing trigger answers false, so a script's `not` of it
  answers true. A caller for whom a wrong yes does something compares the counter around the call.

## Learnings

- **After a failed call the Lua stack is left dirty.** Lua-CSharp unwinds its call frames when an exception
  passes through, but values pushed for the call stay on the stack. `ScriptEngine` records the stack height
  before a call and pops back to it on failure. Without that, every error leaks stack slots.
- **A C# exception inside a script function arrives wrapped** in `LuaRuntimeException` with the original as
  `InnerException`. Its `Message` is only the inner message; the script's file and line are in `LuaTraceback`,
  which the engine appends. Lua's own errors already carry `file:line:` in the message.
- **The instruction hook is free at a coarse interval.** `SetHook` with a count of 100 000 showed no measurable
  cost on a tight loop, and stops `while true do end`.
- **`math.random` is in the math library** and is not deterministic across machines: removed at start.
- **Dependencies in Unity**: `Lua.dll` (netstandard2.1) references `Microsoft.Bcl.TimeProvider` 8.0.0, which
  references `Microsoft.Bcl.AsyncInterfaces` 6.0.0; both are shipped. It also references
  `System.Runtime.CompilerServices.Unsafe` 6.0.0, which the Collections package already provides (an overridable
  plugin), so a second copy would be a duplicate.
- **A method called on the result of a missing method fails.** `c:missing():other()` indexes `false`. That is a
  normal script error with a line number, and the first miss is reported.

## Notes

- First editor import was clean: the three DLLs resolved with no reference errors, and the shipped
  `Microsoft.Bcl.AsyncInterfaces` did not clash with the copy Unity carries for editor assemblies.
- Measurements (on .NET 8, not Unity's Mono) are in `Docs/Engine/scripting.md`.
