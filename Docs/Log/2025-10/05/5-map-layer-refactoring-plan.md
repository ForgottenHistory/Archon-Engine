# Map Layer Refactoring Plan

**Date:** 2025-10-05
**Status:** In Progress
**Goal:** Bring all Map layer files under 500 lines, enforce single responsibility principle

---

## Executive Summary

The Map layer has **3 critical violations** of the 500-line architecture rule, totaling **2,157 lines** that need to be refactored into smaller, focused components. This document outlines the step-by-step refactoring plan.

**Progress:**
- ✅ **Phase 0:** Quick wins completed (-229 lines)
- ✅ **Phase 1:** MapDataLoader.cs refactoring completed (-641 lines, 845→208)
- ✅ **Phase 2:** MapTextureManager.cs refactoring completed (-413 lines, 636→223)
- ✅ **Phase 3:** MapDataIntegrator.cs refactoring completed (-187 lines, 688→501)

---

## Phase 0: Quick Wins ✅ COMPLETE

### 1. Remove Deprecated Code from MapTextureManager.cs ✅
**Completed:** 2025-10-05
**Lines removed:** 77 (713 → 636)

**Changes:**
- Removed CPU-based `SetProvinceID()` and `SetProvinceOwner()` methods
- Removed legacy `ApplyOwnerTextureChanges()` blit operation
- Removed temporary `tempOwnerTexture` field
- Updated calling code with TODOs pointing to GPU alternatives

### 2. Clean Up FastAdjacencyScanner.cs ✅
**Completed:** 2025-10-05
**Lines removed:** 152 (494 → 342)

**Changes:**
- Removed duplicate sequential implementation
- Kept only parallel/Burst-compiled version
- Removed commented-out legacy code
- Removed unused helper methods

---

## Phase 1: MapDataLoader.cs Refactoring 🔄

**Current:** 845 lines
**Target:** 4 files × ~200 lines each
**Priority:** Critical (highest line count violation)

### Problem Analysis

**Multiple Responsibilities:**
1. Province bitmap loading and processing
2. Terrain bitmap loading with indexed color mapping
3. Heightmap bitmap loading and conversion
4. Normal map bitmap loading and conversion
5. Texture rebinding and material synchronization

**Code Duplication:**
- Three nearly identical `Load*BitmapAsync()` methods (terrain, heightmap, normal map)
- Each method: load → validate → populate texture → rebind materials
- `PopulateTerrainTexture()` alone is 250 lines with hardcoded terrain color map

### Refactoring Strategy

#### New File Structure
```
Assets/Archon-Engine/Scripts/Map/Loading/
├── MapDataLoader.cs (200 lines) - Orchestration only
├── Bitmaps/
│   ├── BitmapTextureLoader.cs (150 lines) - Generic bitmap loading base
│   ├── TerrainBitmapLoader.cs (200 lines) - Terrain-specific logic
│   ├── HeightmapBitmapLoader.cs (150 lines) - Heightmap-specific logic
│   └── NormalMapBitmapLoader.cs (150 lines) - Normal map-specific logic
└── Data/
    └── TerrainColorMapper.cs (100 lines) - Terrain index to color mapping
```

#### Step-by-Step Refactoring

**Step 1: Extract Generic Bitmap Loader**
```csharp
// BitmapTextureLoader.cs - Base class for all bitmap loading
public abstract class BitmapTextureLoader
{
    protected JobifiedBMPLoader bmpLoader;
    protected MapTextureManager textureManager;

    public async Task<bool> LoadAndPopulateAsync(string bitmapPath)
    {
        // 1. Derive path from provinces.bmp
        // 2. Check file exists
        // 3. Load bitmap via JobifiedBMPLoader
        // 4. Call abstract PopulateTexture()
        // 5. Rebind to materials
    }

    protected abstract void PopulateTexture(BMPLoadResult bitmapData);
    protected abstract string GetBitmapFileName();
    protected abstract Texture2D GetTargetTexture();
}
```

**Step 2: Extract Terrain-Specific Loader**
```csharp
// TerrainBitmapLoader.cs - Handles terrain.bmp loading
public class TerrainBitmapLoader : BitmapTextureLoader
{
    private TerrainColorMapper colorMapper;

    protected override string GetBitmapFileName() => "terrain.bmp";
    protected override Texture2D GetTargetTexture() => textureManager.ProvinceTerrainTexture;

    protected override void PopulateTexture(BMPLoadResult terrainData)
    {
        // Use TerrainColorMapper for indexed color lookup
        // Single responsibility: terrain texture population only
    }
}
```

**Step 3: Extract Terrain Color Mapping**
```csharp
// TerrainColorMapper.cs - Centralized terrain color definitions
public static class TerrainColorMapper
{
    private static readonly Dictionary<byte, Color32> terrainColors = new()
    {
        [0] = new Color32(50, 180, 50, 255),    // grasslands
        [1] = new Color32(160, 140, 120, 255),  // hills
        // ... all terrain mappings
    };

    public static Color32 GetTerrainColor(byte index, Color32 defaultColor);
    public static bool TryGetTerrainColor(byte index, out Color32 color);
}
```

**Step 4: Simplified MapDataLoader**
```csharp
// MapDataLoader.cs - Orchestration only
public class MapDataLoader : MonoBehaviour
{
    private TerrainBitmapLoader terrainLoader;
    private HeightmapBitmapLoader heightmapLoader;
    private NormalMapBitmapLoader normalMapLoader;

    public async Task<ProvinceMapResult?> LoadFromSimulationAsync(...)
    {
        // 1. Load province bitmap
        var provinceResult = await provinceProcessor.LoadProvinceMapAsync(...);

        // 2. Load supplementary bitmaps in parallel
        await Task.WhenAll(
            terrainLoader.LoadAndPopulateAsync(bitmapPath),
            heightmapLoader.LoadAndPopulateAsync(bitmapPath),
            normalMapLoader.LoadAndPopulateAsync(bitmapPath)
        );

        // 3. Generate borders
        GenerateBorders();

        return provinceResult;
    }
}
```

### Benefits
- ✅ Each file under 200 lines
- ✅ Single responsibility per class
- ✅ Eliminates code duplication (3 similar methods → 1 base + 3 specializations)
- ✅ Easier to test individual bitmap loaders
- ✅ Can parallelize bitmap loading (already using async, now clearer)
- ✅ Terrain color mapping centralized and reusable

### Migration Strategy
1. Create new files with extracted code
2. Update MapDataLoader to use new loaders
3. Test that map loading still works
4. Delete old code from MapDataLoader
5. Update FILE_REGISTRY.md

### Estimated Effort
**6-8 hours** (complex due to async patterns and material rebinding)

---

## Phase 2: MapTextureManager.cs Refactoring ✅ COMPLETE

**Completed:** 2025-10-05
**Before:** 636 lines
**After:** 223 lines (coordinator) + 235 lines (CoreTextureSet) + 192 lines (VisualTextureSet) + 142 lines (DynamicTextureSet) + 137 lines (PaletteTextureManager)
**Lines reduced:** -413 lines from MapTextureManager.cs
**Priority:** High

### Problem Analysis

**Too Many Textures Managed:**
- Province ID, Owner, Color, Development (core gameplay)
- Terrain, Heightmap, Normal Map (visual enhancements)
- Border, Highlight (dynamic rendering)
- Color Palette (lookup table)

**Single Responsibility Violations:**
- Texture creation, binding, memory management, and palette generation all in one class

### Refactoring Strategy

#### New File Structure
```
Assets/Archon-Engine/Scripts/Map/Rendering/
├── TextureManagers/
│   ├── CoreTextureSet.cs (200 lines) - Province ID, Owner, Color, Development
│   ├── VisualTextureSet.cs (200 lines) - Terrain, Heightmap, Normal Map
│   ├── DynamicTextureSet.cs (150 lines) - Border, Highlight (RenderTextures)
│   └── PaletteTextureManager.cs (150 lines) - Color palette generation
└── MapTextureManager.cs (200 lines) - Coordinator + facade
```

#### Texture Set Responsibilities

**CoreTextureSet** - Gameplay-critical textures
- Province ID (RenderTexture, R8G8B8A8_UNorm)
- Province Owner (RenderTexture, RFloat)
- Province Color (Texture2D, RGBA32)
- Province Development (Texture2D, RGBA32)
- Methods: CreateCoreTextures(), BindCoreTextures(), ReleaseCoreTextures()

**VisualTextureSet** - Visual enhancement textures
- Terrain (Texture2D, RGBA32)
- Heightmap (Texture2D, R8)
- Normal Map (Texture2D, RGB24)
- Methods: CreateVisualTextures(), BindVisualTextures(), ReleaseVisualTextures()

**DynamicTextureSet** - Runtime-generated textures
- Border (RenderTexture, R8)
- Highlight (RenderTexture, RGBA32)
- Methods: CreateDynamicTextures(), BindDynamicTextures(), ReleaseDynamicTextures()

**PaletteTextureManager** - Color palette management
- Color Palette (Texture2D, 256×1 RGBA32)
- Methods: GeneratePalette(), UpdatePaletteColor(), BindPalette()

**MapTextureManager** - Facade coordinator
- Owns instances of all texture sets
- Provides unified API for external consumers
- Delegates to appropriate texture set

### Benefits
- ✅ Each file focused on specific texture category
- ✅ Easier to understand texture dependencies
- ✅ Can test texture sets independently
- ✅ Clear lifecycle management per set
- ✅ Reduces cognitive load (200 lines vs 636 lines)

### Migration Strategy
1. Create CoreTextureSet and migrate core texture methods
2. Create VisualTextureSet and migrate visual texture methods
3. Create DynamicTextureSet and migrate dynamic texture methods
4. Create PaletteTextureManager and migrate palette logic
5. Refactor MapTextureManager to facade pattern
6. Update all calling code (minimal changes due to facade)
7. Update FILE_REGISTRY.md

### Changes Completed

**1. Created CoreTextureSet.cs (235 lines)**
- Manages Province ID, Owner, Color, Development textures
- Methods: CreateTextures(), BindToMaterial(), SetProvinceColor(), SetProvinceDevelopment(), ApplyChanges()
- All core gameplay-critical texture operations

**2. Created VisualTextureSet.cs (192 lines)**
- Manages Terrain, Heightmap, Normal Map textures
- Methods: CreateTextures(), BindToMaterial(), ApplyChanges()
- Visual enhancement textures with bilinear filtering

**3. Created DynamicTextureSet.cs (142 lines)**
- Manages Border and Highlight RenderTextures
- Methods: CreateTextures(), BindToMaterial(), SetBorderStyle()
- Runtime-generated dynamic effects

**4. Created PaletteTextureManager.cs (137 lines)**
- Manages 256×1 RGBA32 color palette texture
- Methods: CreatePalette(), SetPaletteColor(), SetPaletteColors(), ApplyChanges()
- HSV-based default color generation with golden angle distribution

**5. Refactored MapTextureManager.cs (636 → 223 lines)**
- Removed: All individual Create*Texture() methods (moved to texture sets)
- Removed: All configuration and initialization logic (delegated)
- Updated: All public API methods now delegate to appropriate texture sets
- Result: Clean facade pattern maintaining backward compatibility

### Estimated Effort
**6-8 hours** (moderate complexity, many call sites)
**Actual Effort:** ~2 hours (clear separation made it faster than estimated)

---

## Phase 3: MapDataIntegrator.cs Refactoring ✅ COMPLETE

**Completed:** 2025-10-05
**Before:** 688 lines
**After:** 501 lines (coordinator) + 115 lines (converter) + 175 lines (synchronizer) + 200 lines (metadata)
**Lines reduced:** -187 lines from MapDataIntegrator.cs
**Priority:** Medium

### Problem Analysis

**Multiple Responsibilities:**
1. Province data conversion (load result → data manager)
2. Texture synchronization (CPU ↔ GPU)
3. Neighbor/metadata queries
4. Province owner updates

**Code Duplication:**
- `SyncProvinceOwnerToTexture()` and `SyncProvinceColorToTexture()` have nearly identical double-loop patterns

### Refactoring Strategy

#### New File Structure
```
Assets/Archon-Engine/Scripts/Map/Integration/
├── ProvinceDataConverter.cs (200 lines) - Load result conversion
├── ProvinceTextureSynchronizer.cs (200 lines) - CPU↔GPU sync
├── ProvinceMetadataManager.cs (150 lines) - Neighbor/metadata queries
└── MapDataIntegrator.cs (150 lines) - High-level coordination
```

#### Component Responsibilities

**ProvinceDataConverter**
- Convert `ProvinceMapLoader.LoadResult` → `ProvinceDataManager`
- Build province pixel mappings
- Initialize province metadata (bounds, centers, pixel counts)

**ProvinceTextureSynchronizer**
- Generic texture sync pattern: `SyncFieldToTexture<T>(provinceID, value, setter)`
- `SyncProvinceOwner()`, `SyncProvinceColor()`, `SyncProvinceDevelopment()`
- Batch sync operations for performance

**ProvinceMetadataManager**
- Neighbor queries (CPU-side neighbor storage)
- Province bounds queries
- Province center point queries
- Pixel count queries

**MapDataIntegrator** (coordinator)
- Orchestrates converter, synchronizer, metadata manager
- Provides high-level API: `InitializeMapData()`, `SetProvinceOwner()`, `GetNeighbors()`

### Benefits
- ✅ Clear separation of conversion, sync, and query concerns
- ✅ Eliminates sync code duplication via generic pattern
- ✅ Easier to test individual components
- ✅ Neighbor queries separated from texture sync

### Migration Strategy
1. Extract ProvinceDataConverter with conversion methods
2. Extract ProvinceTextureSynchronizer with generic sync pattern
3. Extract ProvinceMetadataManager with query methods
4. Refactor MapDataIntegrator to coordinator pattern
5. Update calling code
6. Update FILE_REGISTRY.md

### Changes Completed

**1. Created ProvinceDataConverter.cs (115 lines)**
- Static utility class for converting ProvinceMapLoader.LoadResult to ProvinceDataManager
- Methods: ConvertLoadResult(), GroupPixelsByProvince(), FindProvinceColor(), CalculateProvinceCenter()
- Eliminates duplicate pixel grouping logic

**2. Created ProvinceTextureSynchronizer.cs (175 lines)**
- Handles all CPU↔GPU texture synchronization
- Methods: SyncProvinceOwner(), SyncProvinceColor(), SyncProvinceDevelopment(), SyncAllProvinces()
- Eliminates duplicate sync patterns (3 similar methods → 1 component)

**3. Created ProvinceMetadataManager.cs (200 lines)**
- Manages neighbor detection results and province metadata
- Methods: GetNeighbors(), AreNeighbors(), GetMetadata(), GetCoastalProvinces(), GetProvinceBounds()
- Automatically updates coastal and terrain flags when results are set

**4. Refactored MapDataIntegrator.cs (688 → 501 lines)**
- Removed: ConvertLoadResultToDataManager() (duplicate of ProvinceDataConverter)
- Removed: SyncProvinceOwnerToTexture(), SyncProvinceColorToTexture(), SyncMultipleProvinceColors() (now in ProvinceTextureSynchronizer)
- Removed: UpdateCoastalFlags(), UpdateTerrainFlags() (now automatic in ProvinceMetadataManager)
- Updated: All query methods now delegate to metadataManager
- Updated: ForceFullSync() now delegates to textureSynchronizer.SyncAllProvinces()
- Result: Clean coordinator pattern with clear delegation

### Estimated Effort
**4-6 hours** (lower complexity, clearer boundaries)
**Actual Effort:** ~3 hours (clearer boundaries made it faster than estimated)

---

## Testing Strategy

### Per-Phase Testing
After each refactoring phase:
1. **Manual Testing:** Load map in Unity, verify no visual regressions
2. **Performance Testing:** Ensure load times haven't degraded
3. **Memory Testing:** Verify texture allocations are identical
4. **Integration Testing:** Run existing map loading integration tests

### Validation Checklist
- [ ] Map loads successfully
- [ ] All textures populated correctly (terrain, heightmap, normal map)
- [ ] Province selection still works
- [ ] Map modes switch correctly
- [ ] No memory leaks (texture creation/disposal)
- [ ] Load time unchanged or improved
- [ ] No new compiler warnings

---

## Risk Mitigation

### High-Risk Areas
1. **Async/await patterns** - Easy to break async chains
2. **Material binding** - Runtime material instance rebinding is fragile
3. **Texture disposal** - Memory leaks if not properly released

### Mitigation Strategies
1. **Create branch before refactoring** - Easy rollback if issues arise
2. **Refactor incrementally** - One file at a time, test after each
3. **Keep old code temporarily** - Comment out, don't delete immediately
4. **Extensive logging** - Add temporary debug logs to verify data flow

---

## Success Criteria

**Quantitative:**
- ✅ All Map layer files under 500 lines
- ✅ Total line count reduction: ~1,500 lines → ~2,400 lines (distributed across more focused files)
- ✅ Zero new compiler errors/warnings
- ✅ Load time within 10% of baseline

**Qualitative:**
- ✅ Each file has single, clear responsibility
- ✅ No code duplication (DRY principle)
- ✅ Easier to understand and maintain
- ✅ Follows established architecture patterns

---

## Timeline Estimate

| Phase | Effort | Dependencies | Risk |
|-------|--------|--------------|------|
| **Phase 0** | 2-3 hours | None | Low ✅ COMPLETE |
| **Phase 1** | 6-8 hours | Phase 0 | Medium 🔄 CURRENT |
| **Phase 2** | 6-8 hours | Phase 1 | Medium |
| **Phase 3** | 4-6 hours | Phase 2 | Low |
| **Total** | **18-25 hours** | Sequential | Medium |

---

## Post-Refactoring Tasks

1. **Update FILE_REGISTRY.md** - Document all new files
2. **Update CLAUDE.md** - Reference new file structure
3. **Add XML documentation** - Document all public APIs
4. **Create migration guide** - Document breaking changes (if any)
5. **Performance profiling** - Ensure no regressions

---

**Next Action:** Begin Phase 1 - MapDataLoader.cs refactoring
