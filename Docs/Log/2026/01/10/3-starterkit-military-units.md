# StarterKit Military Units
**Date**: 2026-01-10
**Session**: 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement military units for StarterKit demo

**Success Criteria:**
- Unit creation via UI button
- Unit visualization on map at province centers
- Unit info panel showing units in selected province
- Hide create button for unowned provinces

---

## Context & Background

**Previous Work:**
- See: [2-starterkit-province-selection.md](2-starterkit-province-selection.md)

**Current State:**
- StarterKit had province selection, country selection, economy
- Core UnitSystem existed but wasn't exposed to StarterKit
- GAME layer had unit visualization but StarterKit couldn't use it

---

## What We Did

### 1. Created Unit Definition
**Files Created:** `Template-Data/units/infantry.json5`

Simple infantry unit for StarterKit demo with basic stats.

### 2. Created StarterKitUnitSystem
**Files Created:** `StarterKit/StarterKitUnitSystem.cs`

Wraps Core.Units.UnitSystem with:
- Unit type loading from Template-Data/units/
- Simple API for create/query/disband
- Events for UI updates
- `IsProvinceOwnedByPlayer()` helper for ownership checks

### 3. Created UnitInfoUI
**Files Created:** `StarterKit/UnitInfoUI.cs`

Unit info panel with:
- Shows units in selected province
- "+ Create Infantry" button (hidden for unowned provinces)
- "X" disband button per unit
- Auto-refresh on unit events

### 4. Created StarterKitUnitVisualization
**Files Created:** `StarterKit/StarterKitUnitVisualization.cs`

GPU instanced unit rendering:
- Horizontal quads at province centers
- Uses `ProvinceCenterLookup` for positioning
- Event-driven updates via Core UnitSystem events

### 5. Fixed ComputeBuffer Leak
**Files Changed:** `Map/Loading/MapDataLoader.cs`

**Problem:** `terrainBuffer` created but never disposed.

**Solution:**
- Added `provinceTerrainBuffer` field to store reference
- Added `OnDestroy()` to dispose buffer

### 6. Moved ProvinceCenterLookup to ENGINE
**Files Moved:** `Game/Utils/ProvinceCenterLookup.cs` → `Map/Utils/ProvinceCenterLookup.cs`

**Rationale:** Utility had no GAME dependencies, needed by both GAME and StarterKit.

**Updated 8 files** to use `Map.Utils` namespace.

### 7. Fixed Unit Positioning
**Files Changed:** `Map/Utils/ProvinceCenterLookup.cs`

**Problem:** Units spawned at wrong positions (reversed X and Y).

**Solution:** Changed coordinate mapping:
```csharp
// Removed X flip (was for 180° rotation that StarterKit doesn't have)
// uvX = 1.0f - uvX;
// Added Y flip for texture-to-world mapping
uvY = 1.0f - uvY;
```

---

## Decisions Made

### Decision 1: Flat Quads vs Billboard
**Context:** How should units appear on map?
**Decision:** Flat horizontal quads (lie on map surface)
**Rationale:** Top-down strategy view - flat markers work better than billboards

### Decision 2: Move ProvinceCenterLookup to ENGINE
**Context:** StarterKit needed the utility but couldn't reference GAME layer
**Decision:** Move to `Map.Utils` namespace
**Rationale:** No GAME dependencies, general-purpose utility

---

## Problems Encountered & Solutions

### Problem 1: Units Not Rendering
**Symptom:** Create button worked but no visual
**Root Cause:** Multiple issues - wrong shader, wrong mesh orientation
**Solution:** Used URP Unlit shader, horizontal quad mesh, proper coordinate mapping

### Problem 2: Wrong Positions
**Symptom:** Left→right, top→bottom reversed
**Root Cause:** Coordinate flip for 180° map rotation not needed in StarterKit
**Solution:** Removed X flip, added Y flip

### Problem 3: ComputeBuffer Leak
**Symptom:** Unity warning about leaked buffer
**Root Cause:** `terrainBuffer` local variable lost reference
**Solution:** Store in field, dispose in OnDestroy

---

## Architecture Impact

### New Files in ENGINE
- `Map/Utils/ProvinceCenterLookup.cs` - Province center world positions
- `StarterKit/StarterKitUnitSystem.cs` - Unit system wrapper
- `StarterKit/UnitInfoUI.cs` - Unit info panel
- `StarterKit/StarterKitUnitVisualization.cs` - GPU instanced rendering
- `Template-Data/units/infantry.json5` - Unit definition

---

## Quick Reference for Future Claude

**Key Files:**
- `StarterKit/StarterKitUnitSystem.cs` - Unit management for StarterKit
- `StarterKit/UnitInfoUI.cs` - Unit UI panel
- `StarterKit/StarterKitUnitVisualization.cs` - Unit rendering
- `Map/Utils/ProvinceCenterLookup.cs` - Province center positions (ENGINE utility)

**Gotchas:**
- ProvinceCenterLookup coordinate flips depend on map mesh rotation
- StarterKit map may have different rotation than GAME scene
- Use `gameState.Units` not `gameState.UnitSystem` (property name)

**Scene Setup:**
| GameObject | Components |
|------------|------------|
| UnitInfoUI | `UIDocument` + `UnitInfoUI` |
| UnitVisualization | `StarterKitUnitVisualization` |

---

## Files Changed Summary

**New Files:** 5 files
- `Template-Data/units/infantry.json5`
- `StarterKit/StarterKitUnitSystem.cs`
- `StarterKit/UnitInfoUI.cs`
- `StarterKit/StarterKitUnitVisualization.cs`
- `Map/Utils/ProvinceCenterLookup.cs` (moved from GAME)

**Modified Files:** 10 files
- `StarterKit/StarterKitInitializer.cs` - Unit system init
- `Map/Loading/MapDataLoader.cs` - ComputeBuffer leak fix
- `Game/UIInitializer.cs` - Updated import
- 7 other GAME files - Updated `Game.Utils` → `Map.Utils`

**Deleted Files:** 1 file
- `Game/Utils/ProvinceCenterLookup.cs` (moved to ENGINE)

---

*Session focused on military unit implementation for StarterKit demo*
