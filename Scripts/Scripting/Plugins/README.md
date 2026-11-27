# Scripting plugins

Precompiled assemblies the `Scripting` assembly needs. All are MIT licensed and taken unchanged from NuGet
(the `netstandard2.1` build, or `netstandard2.0` where there is no 2.1 one).

| File | NuGet package | Version | Why |
|---|---|---|---|
| `Lua.dll` | [LuaCSharp](https://www.nuget.org/packages/LuaCSharp) ([source](https://github.com/nuskey8/Lua-CSharp)) | 0.5.8 | The Lua 5.2 interpreter |
| `Microsoft.Bcl.TimeProvider.dll` | Microsoft.Bcl.TimeProvider | 8.0.0 | Referenced by `Lua.dll` |
| `Microsoft.Bcl.AsyncInterfaces.dll` | Microsoft.Bcl.AsyncInterfaces | 6.0.0 | Referenced by `Microsoft.Bcl.TimeProvider.dll` |

`Lua.dll` also references `System.Runtime.CompilerServices.Unsafe` 6.0.0, which Unity's Collections package
(a dependency of the engine) already brings.

To update: download the package from nuget.org (a `.nupkg` is a zip), replace the DLL from its `lib/` folder and
check the package's dependencies for changes.
