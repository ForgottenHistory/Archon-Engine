# Image Loading Refactor & Fluent Validation
**Date**: 2026-01-17
**Session**: 06
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Refactor map image loading to prefer PNG over BMP
- Rename `Loading/Bitmaps` to `Loading/Images` for clarity

**Secondary Objectives:**
- Document custom data loading in Template-Data README
- Showcase fluent validation pattern in StarterKit commands

**Success Criteria:**
- PNG files loaded first, BMP fallback
- Namespace and folder structure reflects modern format support
- Validation pattern demonstrated with GAME-layer extensions

---

## Context & Background

**Previous Work:**
- See: [05-starterkit-diplomacy-ui.md](05-starterkit-diplomacy-ui.md)
- Template-Data README created but referenced heightmap.bmp

**Current State:**
- Image loaders mixed BMP and PNG under "Bitmaps" folder
- `generate_heightmap.py` output BMP format
- Validation system existed in ENGINE but unused

**Why Now:**
- PNG is better, more modern format
- Folder naming was confusing (Bitmaps containing PNG code)
- StarterKit should showcase ENGINE patterns

---

## What We Did

### 1. Refactored Image Loading to Prefer PNG
**Files Changed:**
- `Scripts/Map/Loading/Images/HeightmapBitmapLoader.cs` - Complete rewrite
- `Scripts/Map/Loading/Images/NormalMapBitmapLoader.cs` - Complete rewrite
- `Scripts/Map/Loading/MapDataLoader.cs` - Updated calls

**Implementation:**
```csharp
// Try PNG first, then BMP
string pngPath = System.IO.Path.Combine(mapDirectory, "heightmap.png");
string bmpPath = System.IO.Path.Combine(mapDirectory, "heightmap.bmp");

string imagePath = null;
if (System.IO.File.Exists(pngPath))
    imagePath = pngPath;
else if (System.IO.File.Exists(bmpPath))
    imagePath = bmpPath;
```

Now uses `ImageParser` (auto-detects format) like `TerrainImageLoader`.

### 2. Renamed Namespace and Folder
**Files Changed:** All files in `Scripts/Map/Loading/Images/`

- `Map.Loading.Bitmaps` → `Map.Loading.Images`
- Folder `Bitmaps/` → `Images/`
- Updated all `using` statements across Map layer

### 3. Updated Python Scripts for PNG
**Files Changed:**
- `Template-Data/utils/generate_heightmap.py:258-259`
- `Template-Data/utils/generate_terrain.py:370-371`

```python
# Before
heightmap_path = output_dir / "heightmap.bmp"
img.save(heightmap_path, "BMP")

# After
heightmap_path = output_dir / "heightmap.png"
img.save(heightmap_path, "PNG")
```

Generated new `heightmap.png`, deleted old `heightmap.bmp`.

### 4. Documented Custom Data Loading
**Files Changed:** `Template-Data/README.md`

Added comprehensive example showing how GAME layer adds custom data:
- Religion data example (JSON5 file structure)
- Loader with `LoaderMetadata` attribute
- Registry pattern for runtime access
- `LoaderContext` explanation
- Priority guidelines (10=core, 20=mechanics, 25-30=content, 40+=dependent)

### 5. Added Fluent Validation to StarterKit
**Files Changed:**
- `StarterKit/Validation/StarterKitValidationExtensions.cs` (NEW)
- `StarterKit/Commands/CreateUnitCommand.cs`
- `StarterKit/Commands/MoveUnitCommand.cs`

**New Extension Methods:**
```csharp
public static class StarterKitValidationExtensions
{
    public static ValidationBuilder UnitExists(this ValidationBuilder v, ushort unitId)
    public static ValidationBuilder UnitTypeExists(this ValidationBuilder v, string unitTypeId)
    public static ValidationBuilder ProvinceOwnedByPlayer(this ValidationBuilder v, ushort provinceId)
    public static ValidationBuilder BuildingTypeExists(this ValidationBuilder v, string buildingTypeId)
    public static ValidationBuilder CanConstructBuilding(this ValidationBuilder v, ushort provinceId, string buildingTypeId)
    public static ValidationBuilder HasGold(this ValidationBuilder v, int amount)
}
```

**Usage in Commands:**
```csharp
public override bool Validate(GameState gameState)
{
    return Core.Validation.Validate.For(gameState)
        .Province(ProvinceId)              // ENGINE validator
        .UnitTypeExists(UnitTypeId)        // GAME validator
        .ProvinceOwnedByPlayer(ProvinceId) // GAME validator
        .Result(out validationError);
}
```

---

## Decisions Made

### Decision 1: PNG-First with BMP Fallback
**Context:** Should we drop BMP support entirely?

**Decision:** Keep BMP as fallback for legacy compatibility.

**Rationale:** Some users may have existing BMP assets. Zero cost to check for both.

### Decision 2: Fully Qualified Validate.For()
**Context:** `Validate` method name in commands conflicts with `Core.Validation.Validate` static class.

**Decision:** Use `Core.Validation.Validate.For(gameState)` instead of importing namespace.

**Rationale:** Clearer, avoids naming collision, shows the ENGINE origin explicitly.

---

## What Worked ✅

1. **ImageParser Auto-Detection**
   - Existing unified parser made PNG support trivial
   - Just needed wrapper loaders to try PNG first

2. **Extension Method Pattern**
   - Clean separation of ENGINE vs GAME validators
   - GAME adds domain-specific checks without modifying ENGINE

---

## Problems Encountered & Solutions

### Problem 1: Validate Name Collision
**Symptom:** `CS0119: 'Validate(GameState)' is a method, which is not valid`

**Root Cause:** `using Core.Validation` imports `Validate` static class, which collides with the command's `Validate` method.

**Solution:** Use fully qualified name `Core.Validation.Validate.For()`

### Problem 2: Missing HasBuildingType Method
**Symptom:** `CS1061: 'BuildingSystem' does not contain 'HasBuildingType'`

**Solution:** Use `GetBuildingType(id) == null` instead.

---

## Architecture Impact

### New Files
- `StarterKit/Validation/StarterKitValidationExtensions.cs` - GAME-layer validation

### Namespace Change
- `Map.Loading.Bitmaps` → `Map.Loading.Images`

### Pattern Demonstrated
- ENGINE provides core validators (`Province`, `Country`, `ProvinceOwnedBy`)
- GAME extends via extension methods (`UnitExists`, `HasGold`, etc.)
- Fluent API with short-circuit on first failure

---

## Next Session

### StarterKit Feature Gaps (from analysis)
1. **War indicators** on province tooltips
2. **Notification toasts** for diplomatic events
3. **Advanced AI goals** (Expansion, Defense)
4. **Visual style selector** UI (pluggable renderers)

---

## Quick Reference for Future Claude

**Fluent Validation Pattern:**
```csharp
// In command's Validate method:
return Core.Validation.Validate.For(gameState)
    .Province(provinceId)           // ENGINE
    .UnitExists(unitId)             // GAME extension
    .Result(out validationError);
```

**Image Loading Order:**
1. Try `.png` first
2. Fall back to `.bmp`
3. Uses `ImageParser` for auto-detection

**Gotchas:**
- Use `Core.Validation.Validate.For()` not `Validate.For()` in commands
- Extension methods need `using StarterKit.Validation;`

---

## Files Changed Summary

| File | Changes |
|------|---------|
| `Scripts/Map/Loading/Images/*.cs` | Namespace change to `Map.Loading.Images` |
| `Scripts/Map/Loading/Images/HeightmapBitmapLoader.cs` | Rewritten for PNG-first, uses ImageParser |
| `Scripts/Map/Loading/Images/NormalMapBitmapLoader.cs` | Rewritten for PNG-first, uses ImageParser |
| `Scripts/Map/Loading/MapDataLoader.cs` | Updated loader calls |
| `Scripts/Map/FILE_REGISTRY.md` | Updated namespace references |
| `Template-Data/README.md` | Added custom loader documentation |
| `Template-Data/utils/generate_heightmap.py` | Output PNG instead of BMP |
| `Template-Data/utils/generate_terrain.py` | Expect PNG input |
| `Template-Data/map/heightmap.png` | **NEW** - Generated PNG heightmap |
| `StarterKit/Validation/StarterKitValidationExtensions.cs` | **NEW** - GAME-layer validators |
| `StarterKit/Commands/CreateUnitCommand.cs` | Uses fluent validation |
| `StarterKit/Commands/MoveUnitCommand.cs` | Uses fluent validation |

---

*Session Duration: ~60 minutes*
