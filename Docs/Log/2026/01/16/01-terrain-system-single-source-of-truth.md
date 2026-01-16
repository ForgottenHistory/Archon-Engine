# Terrain System: Single Source of Truth Fix
**Date**: 2026-01-16
**Session**: 01
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix "unknown [T21]" terrain types appearing in UI despite terrain.json5 only defining 15 terrain types (T0-T14)

**Secondary Objectives:**
- Switch terrain image format from BMP to PNG
- Establish terrain.json5 as single source of truth for all terrain definitions
- Remove unused/conflicting terrain loading code

**Success Criteria:**
- No unknown terrain types in UI
- All terrain-related code reads from same source (terrain.json5 via settings path)

---

## Context & Background

**Previous Work:**
- See: [12-adjacency-system-improvements.md](../14/12-adjacency-system-improvements.md)

**Current State:**
- UI showing "unknown [T21]", "unknown [T24]" etc. for many provinces
- Multiple terrain loading paths with conflicting index assignments

**Why Now:**
- Terrain types were being assigned wrong indices causing UI display bugs

---

## What We Did

### 1. Removed Unused WaterProvinceLoader
**Files Changed:**
- `Core/Loaders/WaterProvinceLoader.cs` - DELETED
- `Core/Loaders/ReferenceResolver.cs:~150` - Removed reference

WaterProvinceLoader had hardcoded terrain mappings that weren't being used but could cause confusion. Removed entirely.

**ReferenceResolver fix:**
```csharp
// Before: Called WaterProvinceLoader.ClassifyProvince()
// After: Default terrain = 1, set later by ProvinceTerrainAnalyzer
provinceData.Terrain = 1;
```

### 2. Switched Terrain Image from BMP to PNG
**Files Changed:**
- `Map/Loading/Bitmaps/TerrainBitmapLoader.cs` → Renamed to `TerrainImageLoader`
- `Scripts/generate_terrain.py` - Output PNG instead of BMP

**Implementation:**
```csharp
public class TerrainImageLoader
{
    public void LoadAndPopulate(string mapDirectory)
    {
        // Try PNG first, then BMP for backwards compatibility
        string pngPath = Path.Combine(mapDirectory, "terrain.png");
        string bmpPath = Path.Combine(mapDirectory, "terrain.bmp");

        // Uses ImageParser for unified BMP/PNG support
        var pixelData = ImageParser.Parse(fileData, Allocator.Temp);
    }
}
```

**Rationale:**
- PNG is simpler (no palette confusion)
- ImageParser already supports both formats
- RGB values read directly without palette lookup issues

### 3. Fixed TerrainOverrideApplicator - THE ROOT CAUSE
**Files Changed:**
- `Map/Rendering/Terrain/TerrainOverrideApplicator.cs:19-21, 51, 137-175`
- `Map/Rendering/ProvinceTerrainAnalyzer.cs:63`

**The Bug:** TerrainOverrideApplicator was reading from `terrain_rgb.json5` (Assets/Data/map/) instead of `terrain.json5` (Template-Data/map/). These files had different orders, causing terrain indices to be wrong.

**Evidence from logs:**
```
TerrainOverrideApplicator: Loaded 16 terrain type mappings from terrain_rgb.json5
MapDataLoader: Terrain type distribution - T24:268 T12:158 T0:1603 T21:161...
```

**Fix 1 - Accept data directory parameter:**
```csharp
public TerrainOverrideApplicator(string dataDirectory = null, bool logProgress = true)
{
    this.dataDirectory = dataDirectory ?? Path.Combine(Application.dataPath, "Data");
    this.logProgress = logProgress;
}
```

**Fix 2 - Read from terrain.json5 (same as TerrainRGBLookup):**
```csharp
private Dictionary<string, uint> BuildCategoryToIndexMapping()
{
    string terrainPath = Path.Combine(dataDirectory, "map", "terrain.json5");
    JObject terrainData = Json5Loader.LoadJson5File(terrainPath);
    JObject categories = terrainData["categories"] as JObject;

    // Build category name → index mapping (ORDER in categories = index)
    uint terrainTypeIndex = 0;
    foreach (var categoryProperty in categories.Properties())
    {
        categoryToIndex[categoryProperty.Name] = terrainTypeIndex;
        terrainTypeIndex++;
    }
}
```

**Fix 3 - Pass correct dataDirectory in ProvinceTerrainAnalyzer:**
```csharp
// Before:
overrideApplicator = new TerrainOverrideApplicator(logAnalysis);

// After:
overrideApplicator = new TerrainOverrideApplicator(dataDirectory, logAnalysis);
```

---

## Decisions Made

### Decision 1: Delete terrain_rgb.json5 dependency entirely
**Context:** Two separate JSON files defining terrain with conflicting indices
**Options Considered:**
1. Keep both files synchronized - Error-prone, maintenance burden
2. Merge into single file - Could break existing references
3. Make terrain.json5 the single source of truth - Clean, follows Pattern 17

**Decision:** Option 3 - terrain.json5 as single source of truth
**Rationale:** Pattern 17 (Single Source of Truth) - ONE authoritative place for each piece of data
**Trade-offs:** terrain_rgb.json5 in Assets/Data/map/ is now orphaned (can be deleted)

---

## What Worked ✅

1. **Reading logs folder**
   - What: Checked Logs/map_rendering.log for clues
   - Why it worked: Revealed exact file being loaded and terrain distribution
   - Reusable pattern: Yes - always check game logs for runtime behavior

2. **Tracing data flow UI → Source**
   - What: Traced GetTerrain() from UI → ProvinceState → TerrainAnalyzer → OverrideApplicator
   - Why it worked: Found exact point where wrong indices were being assigned

---

## What Didn't Work ❌

1. **Initial focus on TerrainRGBLookup**
   - What we tried: Assumed TerrainRGBLookup was assigning wrong indices
   - Why it failed: TerrainRGBLookup correctly reads terrain.json5; problem was in TerrainOverrideApplicator
   - Lesson learned: Follow the ENTIRE data path, not just the obvious files

2. **Looking at terrain.png generation**
   - What we tried: Thought terrain.png colors might be wrong
   - Why it failed: Colors were correct; override applicator was overwriting with wrong indices
   - Lesson learned: terrain_override in terrain.json5 overrides image-based terrain detection

---

## Problems Encountered & Solutions

### Problem 1: Unknown Terrain Types [T21] in UI
**Symptom:** UI displayed "unknown [T21]" for many provinces
**Root Cause:** TerrainOverrideApplicator used wrong file (terrain_rgb.json5) and wrong data path

**Investigation:**
- Checked terrain.json5 - only 15 terrain types defined
- Traced UI → ProvinceQueries.GetTerrain() → ProvinceState.terrainType
- Found map_rendering.log showing terrain_rgb.json5 being loaded
- terrain_rgb.json5 existed in Assets/Data/map/ but NOT in Template-Data

**Solution:** Fixed TerrainOverrideApplicator to:
1. Accept dataDirectory parameter from settings
2. Read from terrain.json5 instead of terrain_rgb.json5
3. Build indices by ORDER in categories section (matches TerrainRGBLookup)

**Why This Works:** All terrain-related code now reads from same source using same index assignment logic

**Pattern for Future:** When indices don't match, check ALL files that assign indices and ensure they use same source/logic

---

## Architecture Impact

### New Patterns Discovered
**Pattern:** Single Source of Truth for Game Data
- When to use: Any game data referenced by multiple systems (terrain, buildings, units)
- Benefits: No sync bugs, consistent indices, single update point
- Add to: CLAUDE.md Pattern 17 already covers this

### Anti-Pattern Discovered
**Anti-Pattern:** Hardcoded data paths ignoring settings
- What not to do: `Path.Combine(Application.dataPath, "Data", ...)` instead of using configured dataDirectory
- Why it's bad: Ignores mod support, template data system, causes file not found or wrong file loaded
- Add warning to: Any loader/reader class documentation

---

## Code Quality Notes

### Technical Debt
- **Created:** None
- **Paid Down:** Removed conflicting terrain loading paths
- **TODOs:** Consider deleting orphaned `Assets/Data/map/terrain_rgb.json5`

---

## Next Session

### Immediate Next Steps
1. Delete `Assets/Data/map/terrain_rgb.json5` if confirmed unused
2. Update FILE_REGISTRY.md for Map layer changes

### Questions to Resolve
1. Should terrain_rgb.json5 be deleted or kept for reference?

---

## Session Statistics

**Files Changed:** 4
- TerrainOverrideApplicator.cs (major rewrite of BuildCategoryToIndexMapping)
- ProvinceTerrainAnalyzer.cs (pass dataDirectory)
- TerrainBitmapLoader.cs → TerrainImageLoader.cs (PNG support)
- WaterProvinceLoader.cs (deleted)

**Bugs Fixed:** 1 (unknown terrain types)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `TerrainOverrideApplicator.cs:137-175` - category→index mapping
- Critical decision: All terrain code must use terrain.json5 via settings dataDirectory
- Active pattern: Single Source of Truth (Pattern 17)
- Current status: Working correctly, terrain types display properly

**Gotchas for Next Session:**
- Watch out for: Any hardcoded paths like `Application.dataPath + "Data"` - should use settings
- Don't forget: Terrain indices = ORDER in terrain.json5 categories section
- Remember: terrain_rgb.json5 exists but should NOT be used

---

## Links & References

### Code References
- TerrainOverrideApplicator fix: `Map/Rendering/Terrain/TerrainOverrideApplicator.cs:137-175`
- TerrainRGBLookup (correct pattern): `Map/Rendering/Terrain/TerrainRGBLookup.cs:67-120`
- ProvinceTerrainAnalyzer initialization: `Map/Rendering/ProvinceTerrainAnalyzer.cs:52-63`

---

## Notes & Observations

- terrain.json5 `categories` section defines terrain types - ORDER determines index
- terrain.json5 `terrain` section maps palette indices to terrain types (different purpose)
- terrain_override arrays in categories force specific provinces to specific terrain regardless of terrain.bmp
- Log files are invaluable for debugging - showed exact file paths and data being loaded

---

*Session Duration: ~1 hour*
