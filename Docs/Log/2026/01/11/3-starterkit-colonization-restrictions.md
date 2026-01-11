# StarterKit Colonization Restrictions
**Date**: 2026-01-11
**Session**: 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Add restrictions to colonization: land only (no ocean), neighbors only

**Success Criteria:**
- Buy Land button only shows for land provinces adjacent to player territory
- Data-driven terrain ownable flag (not hardcoded ocean check)

---

## Context & Background

**Previous Work:**
- See: [2-starterkit-ai-colonization.md](2-starterkit-ai-colonization.md)

**Current State:**
- Colonization worked but allowed buying any unowned province including ocean

---

## What We Did

### 1. Added Adjacency Scanning to StarterKit
**Files Changed:** `StarterKit/Initializer.cs`

StarterKit uses `EngineMapInitializer`, not `HegemonInitializer`, so adjacencies were never populated. Added `ScanProvinceAdjacencies()` method.

```csharp
// In InitializeSequence(), after engine init completes:
yield return ScanProvinceAdjacencies(gameState);

// New method scans adjacencies using FastAdjacencyScanner
private IEnumerator ScanProvinceAdjacencies(GameState gameState)
{
    var mapSystemCoordinator = FindFirstObjectByType<MapSystemCoordinator>();
    var provinceMapTexture = mapSystemCoordinator.TextureManager.ProvinceColorTexture;

    GameObject scannerObj = new GameObject("FastAdjacencyScanner_Temp");
    var scanner = scannerObj.AddComponent<FastAdjacencyScanner>();
    // ... scan and populate gameState.Adjacencies
}
```

### 2. Added Data-Driven Terrain Ownable Flag
**Files Changed:** `terrain_rgb.json5`, `TerrainRGBLookup.cs`

Instead of hardcoding ocean terrain IDs, added `ownable` field to terrain definitions:

**terrain_rgb.json5:**
```json5
ocean: { type: "ocean", color: [8, 31, 130], ownable: false },
inland_ocean_17: { type: "inland_ocean", color: [55, 90, 220], ownable: false },
```

**TerrainRGBLookup.cs:**
- Added `terrainTypeOwnable` dictionary
- Added `IsTerrainOwnable(uint terrainIndex)` method
- Updated `Initialize()` to accept `dataDirectory` parameter

### 3. Updated ProvinceInfoUI with Restrictions
**Files Changed:** `StarterKit/ProvinceInfoUI.cs`

Added terrain and neighbor checks:
```csharp
// Check if province terrain is ownable
ushort terrainType = gameState.ProvinceQueries.GetTerrain(currentProvinceID);
if (terrainLookup != null && !terrainLookup.IsTerrainOwnable(terrainType))
{
    colonizeContainer.style.display = DisplayStyle.None;
    return;
}

// Check if province borders player territory
if (!IsAdjacentToPlayerTerritory(currentProvinceID))
{
    colonizeContainer.style.display = DisplayStyle.None;
    return;
}
```

Added `IsAdjacentToPlayerTerritory()` helper that checks if any neighbor is owned by player.

### 4. Added Terrain Display to Province Info UI
**Files Changed:** `StarterKit/ProvinceInfoUI.cs`

Added terrain label showing: `Terrain: ocean [T15] (unownable)`

### 5. Fixed DataDirectory Path Handling
**Files Changed:** `MapInitializer.cs`, `ProvinceTerrainAnalyzer.cs`, `ProvinceInfoUI.cs`

Added `DataDirectory` property to `MapInitializer` exposing `GameSettings.DataDirectory`. Both terrain analyzer and UI now use the same path from GameSettings.

```csharp
// MapInitializer.cs
public string DataDirectory => gameSettings != null ? gameSettings.DataDirectory : null;

// ProvinceTerrainAnalyzer.cs & ProvinceInfoUI.cs
var mapInitializer = FindFirstObjectByType<MapInitializer>();
string dataDirectory = mapInitializer?.DataDirectory;
terrainLookup.Initialize(dataDirectory, false);
```

---

## Problems Encountered & Solutions

### Problem 1: Buy Button Never Showed
**Symptom:** After adding ocean check, button never appeared
**Root Cause:** `ProvinceQueries.IsOcean()` checked terrain == 0, but template data has ocean as T15

**Solution:** Data-driven approach with `ownable: false` in terrain_rgb.json5 instead of hardcoded terrain IDs

### Problem 2: Adjacencies Not Populated
**Symptom:** Neighbor check always returned false
**Root Cause:** StarterKit uses `EngineMapInitializer` which doesn't scan adjacencies (only `HegemonInitializer` does)

**Solution:** Added `ScanProvinceAdjacencies()` to StarterKit Initializer

### Problem 3: Wrong terrain_rgb.json5 Loaded
**Symptom:** Still could buy ocean after adding ownable flag
**Root Cause:** Edited Template-Data file but game was loading from Assets/Data

**Solution:** Made all terrain loading use `DataDirectory` from `GameSettings` via `MapInitializer`

---

## Architecture Impact

### New Pattern: Data-Driven Terrain Flags
- `ownable` field in terrain_rgb.json5 marks which terrains can be owned
- Extensible for other terrain flags (passable, buildable, etc.)
- Single source of truth in terrain data

### Key Insight: DataDirectory Consistency
All systems loading terrain data must use the same `DataDirectory` from `GameSettings`:
- `ProvinceTerrainAnalyzer` - determines terrain indices
- `ProvinceInfoUI` / any UI checking terrain - must match indices

---

## Quick Reference for Future Claude

**Key Files:**
- `StarterKit/Initializer.cs:262-326` - Adjacency scanning for StarterKit
- `StarterKit/ProvinceInfoUI.cs:415-426` - UpdateTerrainLabel
- `StarterKit/ProvinceInfoUI.cs:428-460` - UpdateColonizeButton with restrictions
- `StarterKit/ProvinceInfoUI.cs:462-478` - IsAdjacentToPlayerTerritory helper
- `Map/Rendering/Terrain/TerrainRGBLookup.cs:170-195` - IsTerrainOwnable
- `Map/Core/MapInitializer.cs:58` - DataDirectory property

**Critical Pattern:**
Terrain ownable check uses data-driven flags, not hardcoded terrain IDs:
1. Add `ownable: false` to terrain in `terrain_rgb.json5`
2. Use `TerrainRGBLookup.IsTerrainOwnable(terrainType)` to check

**Gotchas:**
- StarterKit needs its own adjacency scanning (doesn't use HegemonInitializer)
- All terrain loading must use same `DataDirectory` from `GameSettings`
- `ProvinceQueries.IsOcean()` is ENGINE convention (terrain 0), not reliable for game data

---

## Files Changed Summary

**Modified Files:** 7 files
- `Map/Core/MapInitializer.cs` - Added DataDirectory property
- `Map/Rendering/Terrain/TerrainRGBLookup.cs` - Added ownable flag parsing and checking
- `Map/Rendering/ProvinceTerrainAnalyzer.cs` - Use DataDirectory from GameSettings
- `StarterKit/Initializer.cs` - Added ScanProvinceAdjacencies
- `StarterKit/ProvinceInfoUI.cs` - Added terrain display, ownable check, neighbor check
- `Assets/Data/map/terrain_rgb.json5` - Added ownable: false to ocean terrains
- `Assets/Archon-Engine/Template-Data/map/terrain_rgb.json5` - Added ownable: false to ocean terrains

---

*Session focused on data-driven colonization restrictions with proper GameSettings integration*
