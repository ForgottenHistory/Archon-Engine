# Decision: Map Shader Architecture v2 (Engine Pass + Globals + Contract)
**Date:** 2026-10-21
**Status:** ✅ Implemented
**Impact:** Breaking change (game shader layout, renderer interfaces)
**Supersedes:** `shader-copy-and-customize-architecture.md`
**Source:** `Docs/Log/learnings/hoi4-shader-system-notes.md`, plan `Docs/Planning/map-shader-architecture-v2.md`

---

## Decision Summary

**Changed:** GAME map shaders are thin shells (Properties + render state + 3 includes). ENGINE owns the render pass, publishes its values as shader globals, and defines the engine/shader contract in a C# parameter registry.
**Reason:** Copy-and-customize made GAME own ~300 lines of ENGINE code per shader (only one include differed), and the contract lived in three hand-synced places with a silent failure mode.
**Trade-off:** Fixed stage order and one shared set of globals, in exchange for automatic ENGINE updates, explicit validation, and moddable styles.

---

## Context

Copy-and-customize (previous decision) achieved ENGINE/GAME separation but:
- GAME copies held tessellation, vertex, fragment and effect order — ENGINE fixes never reached them
- Contract = `UnityPerMaterial` CBUFFER + every copied Properties block + hand-written `material.SetX` calls; a missing property silently read 0
- Visual styles (ScriptableObjects) could not change in shipped builds

HOI4 (Clausewitz) comparison: name-based engine/shader contract, engine-uploaded constants, effects = shared code + defines, small overridable include files, data-driven tuning. Runtime shader compilation does not carry over to Unity players.

---

## Architecture

**Engine globals.** Textures, buffers, map mode state and style values are `Shader.SetGlobal*`. Shaders declare them; they must never appear in a Properties block (a material property shadows the global). Material swaps need no rebinding; any shader can read them.

**Per-material values.** Only values describing the material: tessellation (read back by selection and border mesh code to detect displacement), values never set from C#, GAME tweakables (`ARCHON_GAME_MATERIAL_PROPERTIES` hook into the CBUFFER).

**Engine pass.** Setup include → GAME map mode dispatcher (`RenderMapMode`) → ENGINE pass via `#include_with_pragmas`. Shared fragment composition: debug → ocean → map mode → lighting → borders → highlights → fog → overlay → post composite.

**Stage overrides.** Compile-time `ARCHON_OVERRIDE_*` macros replace single stages; `ARCHON_POST_COMPOSITE` adds one. Unused hooks cost nothing. Context struct keeps signatures stable as inputs grow.

**Contract registry.** One C# list: name, type, Global/Material scope, default, required, style-settable. Drives global defaults, generic style application, and validation on every style apply (shadowed globals, missing required properties — errors name shader + property). GAME registers its own parameters.

**Data-driven styles.** JSON5 file per style, resolved override-first, keyed by registry names, applied last. Engine-managed state (simulation textures, map mode, border mode, fog toggle) is refused so shader and C# cannot desync. Editor hot reload is optional via settings.

---

## Considered Alternatives

- **Keep copy-and-customize:** rejected — duplication and silent contract failures grow with every ENGINE change.
- **Material properties + Properties sync tooling:** rejected — fixes symptoms; still per-material binding and rebinding.
- **Runtime-configurable compositor (blend modes per layer):** rejected — was never wired to a shader; compile-time hooks give more freedom at zero runtime cost.
- **Runtime shader compilation / AssetBundle shaders:** out of scope; data-driven parameters cover most map mods.

---

## Constraints

- Never declare an engine global in a map shader's Properties block (validation catches it)
- Engine textures have no `_ST` tiling; don't use `TRANSFORM_TEX` on them
- One set of globals per process: a second simultaneous map with different data needs per-material overrides
- Different stage order requires a GAME-owned pass (Level 3)

---

## Related

- `Docs/Engine/visual-styles-architecture.md`
- `Docs/Wiki/Shaders.md`
- Pattern 1 (Engine-Game Separation), Pattern 17 (Single Source of Truth)
