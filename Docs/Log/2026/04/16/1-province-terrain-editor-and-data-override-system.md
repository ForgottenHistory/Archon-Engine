# Province Terrain Editor & Data Override System
**Date**: 2026-04-16
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Build a runtime terrain editor tool in StarterKit for painting terrain types onto provinces and editing terrain colors

**Secondary Objectives:**
- Implement Paradox-style data file override system (base data + override layer)
- Fix graceful loading for missing/unknown scenario data (country refs, terrain.json5)

**Success Criteria:**
- Terrain editor accessible from StarterKit main menu
- Province terrain painting works with live GPU feedback
- Terrain color editing works with live preview
- Changes persist across play mode sessions via override system
- No gray tile rendering corruption on alt-tab

---

## Context & Background

**Previous Work:**
- ~619 Hegemon provinces had `terrain: "impassable_terrain"` needing reclassification
- Auto-classified ~285 by name heuristics, ~334 needed manual editing

**Why Now:**
- Manual province data editing needed tooling, not script hacks
- Opportunity to build reusable editor infrastructure for the engine

---

## What We Did

### 1. Graceful Loading for Missing Data
**Files Changed:**
- `Scripts/Core/Linking/ReferenceResolver.cs:108` — Unknown country tag → warning + unowned province (was error)
- `Scripts/Map/Rendering/Terrain/TerrainRGBLookup.cs:50` — Missing terrain.json5 → warning (was error)
- `Scripts/Map/Rendering/ProvinceTerrainAnalyzer.cs:69` — Failed lookup → warning via ArchonLogger (was Debug.LogError)
- `Scripts/Map/Rendering/Terrain/TerrainOverrideApplicator.cs:39` — Uninitialized lookup → warning (was error)

### 2. TerrainData.Key Property
**Files Changed:**
- `Scripts/Core/Registries/GameRegistries.cs:73` — Added `Key` property to `TerrainData`
- `Scripts/Core/Loaders/TerrainLoader.cs:79,155` — Populate `Key` in both json5 and default loading paths

**Rationale:** Needed raw registry key (e.g., "mountain") for writing back to json5 files. `Name` only had formatted "Mountain".

### 3. Runtime Terrain Update Pipeline
**Files Changed:**
- `Scripts/Map/Loading/MapDataLoader.cs` — Added `UpdateProvinceTerrain(ushort, uint)` and `RegenerateBlendMaps()`
- `Scripts/Map/Core/MapSystemCoordinator.cs` — Exposed `UpdateProvinceTerrain` and `RegenerateTerrainBlendMaps`
- `Scripts/Engine/ArchonEngine.cs` — Exposed `MapSystemCoordinator` publicly
- `Scripts/Map/MapModes/MapModeManager.cs` — Exposed `DataTextures` publicly

**Rationale:** Terrain was load-time-only data. Editor needs to update `provinceTerrainBuffer` (ComputeBuffer) and regenerate blend maps at runtime for live visual feedback.

### 4. Province Terrain Editor UI
**Files Changed:**
- `Scripts/StarterKit/UI/ProvinceTerrainEditorUI.cs` (NEW) — Runtime StarterKitPanel with terrain palette, province info, paint mode, color editor, save-to-disk
- `Scripts/StarterKit/UI/ProvinceTerrainFilePatcher.cs` (NEW) — Comment-preserving json5 regex patcher, creates new province files if missing
- `Scripts/StarterKit/UI/TerrainColorPatcher.cs` (NEW) — Live terrain color preview + deferred terrain.png patching
- `Scripts/StarterKit/UI/LobbyUI.cs` — Added "Map Editor" button to main menu mode selection
- `Scripts/StarterKit/Initializer.cs` — Added `terrainEditorUI` serialized field and initialization

### 5. Terrain Visual Map Mode
**Files Changed:**
- `Scripts/StarterKit/MapModes/TerrainVisualMapMode.cs` (NEW) — ShaderModeID=1, triggers RenderTerrain shader path
- `Scripts/StarterKit/Initializer.cs` — Registered on `MapMode.Terrain`, moved `TerrainCostMapMode` to `StrategicView` slot

**Rationale:** StarterKit's `MapMode.Terrain` was occupied by `TerrainCostMapMode` (movement cost gradient). Needed actual terrain colors for the editor.

### 6. Data File Override System (Paradox-Style)
**Files Changed:**
- `Scripts/Core/Modding/DataFileResolver.cs` (NEW) — Override-first file resolution. Read: check override dir first, fall back to base. Write: always to override dir.
- `Scripts/Engine/ArchonEngine.cs` — Initialize `DataFileResolver` at startup
- `Scripts/Core/Loaders/TerrainLoader.cs` — Uses `DataFileResolver.Resolve()` for terrain.json5
- `Scripts/Core/Loaders/Json5ProvinceConverter.cs` — Uses `DataFileResolver.ListFiles()` for province directory merge
- `Scripts/Map/Loading/Images/TerrainBitmapLoader.cs` — Uses `DataFileResolver.Resolve()` for terrain.png
- `Scripts/Map/Loading/Data/TerrainColorMapper.cs` — Uses `DataFileResolver.Resolve()` for terrain.json5

**Override directory:** `Application.persistentDataPath/DataOverrides/` (outside Unity Assets/ tree)

---

## Decisions Made

### Decision 1: Runtime UI Panel vs EditorWindow
**Context:** Where to put the terrain editor
**Options Considered:**
1. Unity EditorWindow (IMGUI) — editor-only, needs Editor assembly
2. Runtime StarterKitPanel (UI Toolkit) — accessible from main menu, works in builds
**Decision:** Runtime StarterKitPanel
**Rationale:** Fits the StarterKit's existing UI flow (main menu → editor). No separate Editor assembly needed. Could ship in builds.

### Decision 2: Override Directory Location
**Context:** File writes during play mode caused gray tiles
**Options Considered:**
1. `Assets/StreamingAssets/Data/` — still inside Assets/, Unity watches it
2. `Application.persistentDataPath/DataOverrides/` — completely outside Unity project
3. Sibling folder next to project
**Decision:** `Application.persistentDataPath`
**Rationale:** Unity's file watcher monitors everything inside `Assets/`. Any file change triggers `AssetDatabase.Refresh()` on focus regain, which corrupts GPU resources during play mode. `persistentDataPath` is completely outside the project tree.

### Decision 3: Deferred terrain.png Patching
**Context:** `Texture2D.LoadImage` + `EncodeToPNG` during play mode allocated GPU memory that corrupted active render textures
**Decision:** Defer terrain.png writes to after play mode exits; provide "Rebuild Terrain GPU" button for manual refresh
**Rationale:** terrain.json5 writes are safe (text file). terrain.png writes require Texture2D GPU operations. Separating them avoids the corruption.

---

## What Worked

1. **Live GPU palette updates** — `TerrainColorPalette.SetPixel()` + `Apply()` is instant and non-disruptive
2. **ComputeBuffer.SetData partial update** — Single-entry terrain buffer updates work correctly
3. **DataFileResolver pattern** — Clean separation of read (resolve) and write (override dir) paths
4. **Comment-preserving regex patching** — Json5 files with comments round-trip safely

---

## What Didn't Work

1. **Writing files inside Assets/ during play mode**
   - What we tried: Writing to Template-Data/, then StreamingAssets/Data/
   - Why it failed: Unity's file watcher detects changes on alt-tab, triggers AssetDatabase.Refresh(), corrupts GPU textures
   - Lesson learned: NEVER write to anything inside Assets/ during play mode
   - Don't try this again because: The file watcher cannot be disabled per-directory

2. **Multi-line regex for terrain.json5 patching**
   - What we tried: `$"$1{newColor.r}, {newColor.g}, {newColor.b}$2"` replacement
   - Why it failed: `$1` followed by digits in C# string interpolation gets interpreted as regex backreference `$1XX`
   - Lesson learned: Avoid capture group backreferences in replacements with interpolated numeric values. Use simpler patterns that replace the whole match.

3. **Texture2D operations during play mode for PNG patching**
   - What we tried: `new Texture2D()` → `LoadImage()` → `GetPixels32()` → `SetPixels32()` → `EncodeToPNG()`
   - Why it failed: Allocates ~46MB GPU memory, displaces/corrupts active render textures
   - Lesson learned: Heavy GPU allocations during play mode are dangerous. Defer to after play mode exits.

---

## Problems Encountered & Solutions

### Problem 1: Gray Tiles on Alt-Tab
**Symptom:** Map renders as gray tiles after alt-tabbing back into Unity
**Root Cause:** Two separate causes discovered:
1. File writes inside `Assets/` trigger `AssetDatabase.Refresh()` on focus regain → GPU texture invalidation
2. `Texture2D.LoadImage()` for PNG patching allocates GPU memory → corrupts active render textures

**Solution:**
- Move override directory to `Application.persistentDataPath` (outside Assets/)
- Defer terrain.png patching to after play mode exits
- Provide "Rebuild Terrain GPU" button for manual palette + blend map refresh

### Problem 2: Regex Corruption of terrain.json5
**Symptom:** `$199, 56, 23]` appearing instead of `color: [199, 56, 23]`
**Root Cause:** C# regex `Replace()` interprets `$1` + digits as backreference
**Solution:** Line-by-line parsing with `colorValueRegex.Replace()` matching only `[R, G, B]` — no capture group backreferences in replacement string

---

## Architecture Impact

### New Systems
- **DataFileResolver** (`Core.Modding`) — Paradox-style override-first file resolution. Loaders check override dir first, fall back to base. Editor writes only to override.
- **Runtime Terrain Updates** — `MapDataLoader.UpdateProvinceTerrain()` + `RegenerateBlendMaps()` exposed through `MapSystemCoordinator`

### Loaders Wired to DataFileResolver
- TerrainLoader, Json5ProvinceConverter, TerrainBitmapLoader, TerrainColorMapper
- Remaining loaders (DefinitionLoader, MapConfigLoader, country loaders) still use base path only

---

## Next Session

### Immediate Next Steps
1. Wire remaining loaders to DataFileResolver (DefinitionLoader, MapConfigLoader, country loaders)
2. Apply auto-classified terrain changes to Hegemon's 285 provinces
3. Use editor tool to manually classify remaining 334 unknown provinces

### Questions to Resolve
1. Should DataFileResolver support multiple override layers (mod stacking)?
2. Should the editor tool support undo/redo?

---

## Session Statistics

**Files Changed:** ~16
**Files Created:** 5 (ProvinceTerrainEditorUI, ProvinceTerrainFilePatcher, TerrainColorPatcher, DataFileResolver, TerrainVisualMapMode)
**Bugs Fixed:** 2 (graceful loading for unknown countries, missing terrain.json5)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- DataFileResolver override dir: `Application.persistentDataPath/DataOverrides/`
- Terrain editor: `StarterKit/UI/ProvinceTerrainEditorUI.cs` — requires scene GameObject with UIDocument
- NEVER write files inside Assets/ during play mode — causes gray tiles via AssetDatabase.Refresh
- Texture2D GPU operations during play mode corrupt render textures — defer heavy image work
- terrain.json5 regex: avoid `$1` backreferences with interpolated numbers, use whole-match replacement

**Gotchas for Next Session:**
- terrain.png deferred patches flush on OnDestroy — verify this fires reliably
- ProvinceTerrainEditorUI needs manual scene setup (GameObject + UIDocument + PanelSettings)
- TerrainCostMapMode moved from MapMode.Terrain to MapMode.StrategicView

---

*Session logged: 2026-04-16*
