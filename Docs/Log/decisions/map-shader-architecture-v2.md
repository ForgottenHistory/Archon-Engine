# Decision: Map Shader Architecture v2 (Engine Pass + Globals + Contract)
**Date:** 2026-10-21
**Status:** ✅ Implemented
**Impact:** Breaking change (game shader layout, renderer interfaces)
**Supersedes:** shader copy-and-customize (GAME copied the Default shaders and swapped the map mode include; removed, see git history)
**Source:** `Docs/Log/learnings/hoi4-shader-system-notes.md`

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

## Implementation Map

- Globals: `MapShaderGlobals` (IDs, engine parameter registration, style setters); fallbacks replace Properties defaults
- Shader includes: `MapShaderSetup.hlsl`, `MapContext.hlsl`, `MapFragment.hlsl` (stages + hooks), `MapPassFlat.hlsl`, `MapPassTerrain.hlsl`; `DefaultCommon.hlsl` (globals + small material CBUFFER)
- Contract: `MapShaderParameterRegistry`, `MapShaderParameter`/`ShaderValue`, `ShaderParameterOverride`
- Styles: `VisualStyleManager` (apply, custom params, data file, validation, hot reload, export), `VisualStyleDataFile`, `VisualStyleConfiguration.styleDataFile`
- Settings: `GameSettings.WatchStyleDataFile`, `WatchStyleDataFileInDevelopmentBuilds`
- Removed: legacy MapCore shaders, unwired compositor stack, `MAP_MODE_*` keywords, renderer `ApplyToMaterial` + `*StyleParams`

---

## Notes and Known Gaps

- Materials keep stale serialized values for removed properties. Unity ignores them (not in Properties); reset the material if a value looks stuck.
- `MapModeDataTextures` binds culture/religion textures that no ENGINE shader reads; kept for GAME shaders.
- Development gradient (`_Dev*`) still lives in the ENGINE material CBUFFER; it is GAME policy and should move to GAME shaders via `ARCHON_GAME_MATERIAL_PROPERTIES`.
- Structural style settings (border rendering mode, pixel-perfect thickness, tessellation on/off) are asset-only: they drive C# systems, not just shader values.

---

## Future: Large Maps (EU5 scale, deferred)

Generated-data caching was deferred. At 5632x2048 (11.5M px) generation is cheap: terrain blend maps ~46 ms, terrain type ~68 ms, neighbor detection ~105 ms; image decodes and adjacency are already cached (4-28 ms).

Estimates at 16384x8192 (~134M px, ~11.6x):
- CPU hotspot: province ID lookup + pixel index (`MapTexturePopulator`), 542 ms → ~6 s. Uncached and CPU-bound: first cache candidate (key = hash of inputs + generator version; store a full input fingerprint to guard collisions, as HOI4's shader cache does; cache under `persistentDataPath`).
- GPU-generated textures are usually cheaper to regenerate than cache: one full-res RGBA texture is ~512 MB to read back, store and re-upload.
- VRAM is the real limit: ~a dozen full-res map textures → several GB.
- Conclusion: EU5 scale needs an opt-in large-map system (streaming / tiled or virtual textures, lower-res derived textures), not a cache on the current pipeline. The default path stays for games that don't need it.

---

## Related

- `Docs/Engine/visual-styles-architecture.md`
- `Docs/Wiki/Shaders.md`
- Pattern 1 (Engine-Game Separation), Pattern 17 (Single Source of Truth)
