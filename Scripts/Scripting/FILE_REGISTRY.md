# Scripting Assembly File Registry
**Namespace:** `Scripting`
**Assembly:** `Scripting`
**Dependencies:** Utils, `Lua.dll` (Lua-CSharp, in `Plugins/`)

---

## Overview
Lua 5.2 scripting. The ENGINE runs scripts and passes values; the GAME provides every function and object
scripts can use. See `Docs/Engine/scripting.md`.

No type of the Lua library is public here: game code does not reference `Lua.dll`.

---

## Root/
- **Scripting.ScriptEngine** - One sandboxed Lua world: global functions and values, object types, running files, calling kept Lua functions synchronously; errors as false + `LastError`; instruction limit; missing methods/globals reported (`MissingMember`, `Missing`, optional log) and stubbed
- **Scripting.ScriptFunction** - Delegate of a C# function scripts call (`ref ScriptCall`)
- **Scripting.ScriptException** - Thrown by `ScriptCall.Fail`; becomes the script's error
- **Scripting.ScriptCall** - One call from a script: arguments by index with fallbacks, `Self<T>`, `Return`, `ReturnList`, `Fail`
- **Scripting.ScriptType** - A kind of object with methods (`SetMethod`, `HasMethod`)
- **Scripting.ScriptObject** - Base class of GAME objects scripts hold; one C# object is one Lua identity
- **Scripting.ScriptValue** - nil / boolean / number / string / function / table / object
- **Scripting.ScriptTable** - A Lua table read from C#: named fields, list part, `FieldNames` in ordinal order
- **Scripting.ScriptFunctionRef** - A Lua function kept to call later

## Plugins/
- **Lua.dll**, **Microsoft.Bcl.TimeProvider.dll**, **Microsoft.Bcl.AsyncInterfaces.dll** - The interpreter and what it references; versions and sources in `Plugins/README.md`

---

*Updated: 2026-11-27*
*Changed: rebuilt on Lua-CSharp (was MoonSharp behind a define); the assembly is always compiled*
