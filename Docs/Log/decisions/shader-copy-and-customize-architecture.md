# Decision: Shader Copy-and-Customize Architecture
**Date:** 2026-04-05
**Status:** ✅ Implemented
**Impact:** Breaking change (shader workflow)
**Pattern:** Level 2 Visual Style Customization

---

## Decision Summary

**Changed:** GAME creates shaders by copying ENGINE Default shaders and replacing map mode includes with its own visual policy files
**Reason:** ENGINE cannot include GAME (separate git repos), and ENGINE shouldn't dictate visual identity
**Trade-off:** GAME must keep copies in sync with ENGINE infrastructure updates, but gains full visual control

---

## Context

**Problem:** Three conflicting approaches existed:
1. `MapCore.shader` (legacy) — ENGINE shader with hardcoded `#include "../../Game/Shaders/..."` paths, violating the engine-game separation (ENGINE importing GAME)
2. Default shaders — ENGINE includes its own map mode hlsl files, forcing one visual style on all games
3. GAME duplicating entire ENGINE hlsl files — leads to stale copies and missed infrastructure updates

**Constraint:** Archon-Engine is a separate git repository. It cannot reference paths outside `Assets/Archon-Engine/`.

**Goal:** GAME controls visual policy (how political/terrain modes look). ENGINE provides rendering infrastructure (textures, borders, fog, lighting).

---

## Considered Alternatives

### A: ENGINE includes GAME hlsl (MapCore.shader approach)
- ❌ Violates engine-game separation
- ❌ ENGINE repo contains paths to GAME
- ❌ Breaks if GAME folder structure changes

### B: GAME uses ENGINE Default shaders directly
- ❌ All games look the same
- ❌ No way to customize political/terrain rendering
- ❌ ENGINE becomes opinionated about visual style

### C: GAME copies entire shader + all hlsl files
- ❌ Duplicates ENGINE infrastructure (borders, fog, lighting)
- ❌ Stale copies miss ENGINE bug fixes and new features
- ❌ Maintenance burden

### D: GAME copies shader, replaces only map mode dispatcher ✅
- ✅ GAME controls visual policy (map mode hlsl files)
- ✅ ENGINE provides infrastructure via includes from engine path
- ✅ Minimal duplication — only .shader file and map mode hlsl
- ✅ ENGINE infrastructure updates flow through automatically
- Trade-off: Properties block must stay in sync with ENGINE's CBUFFER

---

## Architecture

```
GAME shader (.shader)
├── DefaultCommon.hlsl (ENGINE path) — textures, CBUFFER
├── GameMapModes.hlsl (GAME) — replaces DefaultMapModes.hlsl
│   ├── MapModeCommon.hlsl (ENGINE path) — utilities
│   ├── MapModePolitical.hlsl (GAME) — visual policy
│   ├── MapModeTerrain.hlsl (GAME) — visual policy
│   └── MapModeDevelopment.hlsl (GAME) — visual policy
├── DefaultLighting.hlsl (ENGINE path) — normal map lighting
├── DefaultEffects.hlsl (ENGINE path) — overlay effects
└── DefaultDebugModes.hlsl (ENGINE path) — debug modes
```

**Rule:** GAME includes ENGINE, never the reverse.

**What GAME copies:** `.shader` file + map mode `.hlsl` files (visual policy)
**What GAME includes from ENGINE:** `DefaultCommon.hlsl`, `MapModeCommon.hlsl`, `DefaultLighting.hlsl`, `DefaultEffects.hlsl`, `DefaultDebugModes.hlsl`

---

## Sync Requirements

Sync is **one-directional: ENGINE → GAME only.** GAME can add its own Properties, textures, and shader parameters freely without affecting ENGINE.

When ENGINE updates:
- **New CBUFFER parameters** → GAME must add matching Properties in `.shader` files. Unity's Properties block doesn't support `#include`, so this is manual. **Failure mode is silent** — no compile error, the new feature just defaults to 0/black. Check `DefaultCommon.hlsl` CBUFFER when updating Archon.
- **New texture declarations** → GAME gets them automatically (included from ENGINE path)
- **New border/fog/lighting features** → GAME gets them automatically (included from ENGINE path)
- **Map mode API changes** → GAME must update its map mode hlsl files (function signatures)

When GAME adds custom parameters:
- Add Properties to GAME's `.shader` files — no ENGINE changes needed
- Add to GAME's own CBUFFER extension or declare inline — ENGINE is unaffected

---

## Implementation (Hegemon)

```
Assets/Game/Shaders/
├── HegemonFlatMap.shader        — "Hegemon/FlatMap"
├── HegemonTerrainMap.shader     — "Hegemon/TerrainMap"
└── MapModes/
    ├── HegemonMapModes.hlsl     — Dispatcher
    ├── MapModePolitical.hlsl    — HSV grading, bilinear blending
    ├── MapModeTerrain.hlsl      — Imperator Rome 4-channel blending
    └── MapModeDevelopment.hlsl  — Gradient colorization
```

---

## Related

- Visual Styles Architecture: `Docs/Engine/visual-styles-architecture.md` (Level 2)
- Wiki: `Docs/Wiki/Shaders.md`
- Pattern 1: Engine-Game Separation
