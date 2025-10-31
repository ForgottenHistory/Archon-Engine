# Border Rendering System Cleanup Plan
**Date**: 2025-10-31
**Context**: After 5 days of experimentation, consolidate to three proven approaches
**Goal**: Clean, maintainable codebase with reusable patterns for generic engine

---

## Cleanup Philosophy

**Keep**:
- Proven, working approaches
- Reusable patterns for generic engine
- Infrastructure with clear use cases

**Remove**:
- Failed experiments (anti-patterns documented)
- Circular/redundant logic
- Code wrapped in `#if FALSE` (dead code)

**Refactor**:
- Simplify to three rendering modes: Distance Field (smooth 3D) + Mesh (smooth flat) + Pixelated (retro/performance)
- Remove Bézier conversion overhead
- Consolidate configuration

---

## Files to Delete Entirely

### 1. BorderCurveRenderer.cs
**Location**: `Assets/Archon-Engine/Scripts/Map/Rendering/BorderCurveRenderer.cs`
**Reason**: GPU curve rasterization (anti-pattern - rasterizes smooth curves back to texture)
**Status**: Already wrapped in `#if FALSE` (lines 1-402)
**Action**: Delete file + .meta

**Dependencies to Check**:
- BorderComputeDispatcher.cs (references removed, wrapped in `#if FALSE`)
- No other references expected

### 2. BorderSDFRenderer.cs
**Location**: `Assets/Archon-Engine/Scripts/Map/Rendering/BorderSDFRenderer.cs`
**Reason**: Redundant with distance field approach, never fully worked
**Status**: Already wrapped in `#if FALSE` (lines 1-228)
**Action**: Delete file + .meta

**Dependencies to Check**:
- BorderComputeDispatcher.cs (references removed, wrapped in `#if FALSE`)
- No other references expected

---

## Files to Refactor (Major Changes)

### 3. BorderCurveExtractor.cs
**Location**: `Assets/Archon-Engine/Scripts/Map/Rendering/BorderCurveExtractor.cs`

**Keep (Core Pipeline)**:
- Border pixel extraction using ProvinceMapping (lines ~100-310)
- RDP simplification `SimplifyPolyline()` (lines ~940-1002)
- Chaikin smoothing `SmoothCurve()` (lines ~1030-1100)
- Median filter `ApplyMedianFilterToProvinceIDs()` (lines ~1330-1410)
- Junction detection `DetectJunctionPixels()` (lines ~1343-1432)
- Polyline output: `Dictionary<(ushort, ushort), List<Vector2>>`

**Remove**:
- Bézier fitting logic (if any remains)
- Curve conversion functions
- Debug Bézier-related logging

**Refactor**:
- Single output type: `List<Vector2>` polylines (no Bézier variants)
- Remove `ExtractAndSmoothBordersAsBezier()` method (if exists)
- Simplify to: `ExtractAndSmoothBordersAsPolylines()` only

**Expected Changes**:
- Lines removed: ~200-300 (Bézier fitting, conversions)
- Lines kept: ~1200 (core pipeline)
- Complexity: Moderate (remove dead branches)

### 4. BorderCurveCache.cs
**Location**: `Assets/Archon-Engine/Scripts/Map/Rendering/BorderCurveCache.cs`

**Current State**: Stores `List<BezierSegment>` (lines 59-220)

**Refactor To**:
- Store `List<Vector2>` polylines only
- Remove `BezierSegment` class/struct
- Remove Bézier-related storage and accessors

**Changes**:
```csharp
// BEFORE
private Dictionary<(ushort, ushort), List<BezierSegment>> borderCurves;

// AFTER
private Dictionary<(ushort, ushort), List<Vector2>> borderPolylines;
```

**Expected Changes**:
- Lines removed: ~100 (Bézier storage, conversions)
- Lines simplified: ~50 (accessors now return polylines)
- Complexity: Low (straightforward type change)

### 5. BorderComputeDispatcher.cs
**Location**: `Assets/Archon-Engine/Scripts/Map/Rendering/BorderComputeDispatcher.cs`

**Current State**: Three rendering modes, two wrapped in `#if FALSE`

**Refactor To**:
- Two rendering modes: `DistanceField` (default) and `Mesh` (fallback)
- Remove all `#if FALSE` wrapped code (delete, don't just disable)
- Clean up configuration enum

**Changes**:
```csharp
// BEFORE
public enum BorderRenderingMode
{
    Rasterization,    // Delete
    SDF,              // Delete
    Mesh              // Keep
}

// AFTER
public enum BorderRenderingMode
{
    DistanceField,    // Smooth 3D (for tessellation, JFA-based)
    Mesh,             // Smooth flat (triangle strips, sub-pixel)
    Pixelated         // Pixel-perfect (BorderMask, retro aesthetic)
}
```

**Remove Lines**:
- Lines 98-101: Legacy rendering initialization (wrapped in `#if FALSE`)
- Lines 288-325: Legacy rendering calls (wrapped in `#if FALSE`)
- All references to BorderCurveRenderer
- All references to BorderSDFRenderer

**Expected Changes**:
- Lines removed: ~300 (legacy code + `#if FALSE` wrappers)
- Lines kept: ~200 (distance field + mesh modes)
- Complexity: Moderate (remove dead branches, simplify enum)

### 6. BorderMeshGenerator.cs
**Location**: `Assets/Archon-Engine/Scripts/Map/Rendering/BorderMeshGenerator.cs`

**Current State**: Works correctly (generates triangle strips from polylines)

**Minor Cleanup**:
- Remove any Bézier-related code (if exists)
- Remove hardcoded debug width override (already done)
- Ensure only accepts `List<Vector2>` polylines

**Expected Changes**:
- Lines removed: ~10-20 (minor cleanup)
- Complexity: Low (working code, just polish)

---

## Files to Keep As-Is (Working)

### 7. BorderDistanceFieldGenerator.cs
**Location**: `Assets/Archon-Engine/Scripts/Map/Rendering/BorderDistanceFieldGenerator.cs`
**Reason**: JFA distance field generation (proven, reusable)
**Action**: Keep unchanged
**Use Case**: DistanceField rendering mode (smooth 3D terrain)

### 8. BorderMask Generation
**Location**: Part of existing border pipeline (compute shader edge detection)
**Reason**: Pixel-perfect border rendering (retro aesthetic, performance)
**Action**: Keep unchanged
**Use Case**: Pixelated rendering mode (1-pixel sharp borders)
**Note**: Already implemented, just needs to be exposed as rendering mode

### 9. ProvinceMapping.cs
**Location**: Already in codebase
**Reason**: O(n) border extraction using adjacency
**Action**: Keep unchanged
**Use Case**: Core engine infrastructure (used by all modes)

---

## Files to Verify/Update (Dependencies)

### 9. VisualStyleManager.cs
**Location**: `Assets/Archon-Engine/Scripts/Map/Rendering/VisualStyleManager.cs`

**Current State**: Disabled vector curve buffer binding (lines 206-292)

**Action**:
- Remove Bézier/curve buffer references entirely
- Keep only distance field + mesh buffer management
- Verify no broken references after deletions

**Expected Changes**:
- Lines removed: ~100 (vector curve buffer code)
- Complexity: Low (cleanup only)

---

## Documentation Updates Required

### 10. Architecture Docs
**Files to Update**:
- `master-architecture-document.md` - Update border rendering section
- `FILE_REGISTRY.md` - Remove deleted files, update descriptions

**New Sections to Add**:
- Border rendering modes (Distance Field vs Mesh)
- When to use each mode
- Reusable patterns (RDP + Chaikin, median filter)

### 11. Session Logs
**Action**: Add reference to cleanup in latest session log
**Note**: Keep all session logs (document what was tried, even failures)

---

## Testing After Cleanup

### Verification Checklist
- [ ] Project compiles with no errors
- [ ] Distance field borders render correctly
- [ ] Mesh borders render correctly (if mode switched)
- [ ] No broken references to deleted files
- [ ] BorderComputeDispatcher enum works
- [ ] BorderCurveCache accepts polylines only
- [ ] No `#if FALSE` code remains

### Performance Verification
- [ ] Load time unchanged (~8-10s)
- [ ] Render time unchanged (~1ms)
- [ ] Memory usage reduced (removed Bézier storage)

### Visual Verification
- [ ] Borders appear smooth
- [ ] No regressions in junction quality
- [ ] U-turn elimination still works (95%)

---

## Execution Order

### Phase 1: Delete Dead Code (Low Risk)
1. Delete `BorderCurveRenderer.cs` + .meta
2. Delete `BorderSDFRenderer.cs` + .meta
3. Remove `#if FALSE` sections from `BorderComputeDispatcher.cs`
4. **Test**: Project compiles

### Phase 2: Refactor Storage (Medium Risk)
1. Refactor `BorderCurveCache.cs` - polylines only
2. Update `BorderCurveExtractor.cs` - remove Bézier fitting
3. Update `BorderComputeDispatcher.cs` - simplify enum
4. **Test**: Project compiles, distance field mode works

### Phase 3: Cleanup Dependencies (Low Risk)
1. Update `VisualStyleManager.cs` - remove curve buffer references
2. Update `BorderMeshGenerator.cs` - minor cleanup
3. **Test**: Both rendering modes work

### Phase 4: Documentation (No Risk)
1. Update `FILE_REGISTRY.md`
2. Update `master-architecture-document.md`
3. Create reference to cleanup in session log

---

## Estimated Time

**Phase 1** (Delete): 30 minutes
**Phase 2** (Refactor): 2-3 hours
**Phase 3** (Cleanup): 1 hour
**Phase 4** (Docs): 1 hour

**Total**: 4-6 hours

---

## Code Size Reduction

**Before Cleanup**:
- BorderCurveRenderer.cs: ~400 lines
- BorderSDFRenderer.cs: ~230 lines
- BorderComputeDispatcher.cs: ~500 lines (with `#if FALSE`)
- BorderCurveCache.cs: ~220 lines
- BorderCurveExtractor.cs: ~1500 lines
- **Total**: ~2850 lines

**After Cleanup**:
- BorderCurveRenderer.cs: DELETED
- BorderSDFRenderer.cs: DELETED
- BorderComputeDispatcher.cs: ~200 lines (simplified)
- BorderCurveCache.cs: ~120 lines (polylines only)
- BorderCurveExtractor.cs: ~1200 lines (remove Bézier)
- **Total**: ~1520 lines

**Reduction**: ~1330 lines removed (47% reduction)

---

## Benefits of Cleanup

### Code Quality
- Remove anti-patterns (documented in learnings doc)
- Simpler codebase (two modes vs five)
- No dead code (`#if FALSE` removed)
- Clear separation (distance field vs mesh)

### Maintainability
- Fewer files to maintain
- Clear purpose for each file
- Easier onboarding (less to understand)
- Better documented (updated architecture docs)

### Performance
- Reduced memory (no Bézier storage)
- No conversion overhead (polylines → Bézier → polylines)
- Simpler pipeline (fewer branches)

### Generic Engine Value
- Reusable patterns clearly identified
- Distance field + mesh approaches proven
- RDP + Chaikin pipeline documented
- Applicable to roads, rivers, terrain features

---

## Risks & Mitigation

### Risk 1: Breaking Existing Functionality
**Mitigation**: Test after each phase, keep git history for rollback

### Risk 2: Missing Dependencies
**Mitigation**: Search codebase for references before deleting files

### Risk 3: Regression in Visual Quality
**Mitigation**: Visual comparison before/after, keep screenshots

### Risk 4: Performance Regression
**Mitigation**: Profile before/after, measure load time + render time

---

## Post-Cleanup State

### Final Architecture

```
Border Rendering System (Three Modes)
├── Distance Field Mode (Smooth 3D)
│   ├── BorderDistanceFieldGenerator.cs (JFA, 1/4 resolution)
│   ├── Fragment shader sampling
│   └── Use case: Tessellated terrain (borders follow 3D surface)
│
├── Mesh Mode (Smooth Flat)
│   ├── BorderCurveExtractor.cs (RDP + Chaikin → polylines)
│   ├── BorderCurveCache.cs (stores polylines)
│   ├── BorderMeshGenerator.cs (triangle strips)
│   └── Use case: Non-tessellated, razor-thin flat borders
│
├── Pixelated Mode (Retro/Performance)
│   ├── BorderMask generation (edge detection compute shader)
│   ├── 1-pixel sharp borders (no smoothing)
│   └── Use case: Pixel art aesthetic, performance-critical, retro style
│
└── Shared Infrastructure
    ├── ProvinceMapping.cs (O(n) extraction)
    ├── Median filter (junction preservation, optional for modes)
    └── BorderComputeDispatcher.cs (mode selection)
```

### Clear Use Cases

**Distance Field Mode** (Smooth 3D):
- Default for tessellated terrain
- Borders follow 3D surface elevation
- Memory efficient (2MB texture)
- Proven by Imperator Rome
- **Quality**: Smooth, anti-aliased

**Mesh Mode** (Smooth Flat):
- Non-tessellated rendering
- Flat map, razor-thin borders
- More memory (9MB+ for vertices)
- Sub-pixel width (0.0002 world units)
- **Quality**: Smooth, vector-like

**Pixelated Mode** (Retro/Performance):
- Pixel-perfect borders (1px wide)
- No smoothing, pure edge detection
- Minimal memory (BorderMask texture)
- Fastest rendering (simple texture lookup)
- **Quality**: Sharp, pixel art aesthetic

---

## Success Criteria

- [ ] Code compiles with no errors/warnings
- [ ] ~1300 lines of code removed
- [ ] Three clear rendering modes remain (Distance Field, Mesh, Pixelated)
- [ ] All tests pass (visual + performance)
- [ ] Documentation updated
- [ ] No `#if FALSE` code remains
- [ ] Bézier logic completely removed
- [ ] Git history preserved (incremental commits)
- [ ] All three modes selectable via BorderComputeDispatcher enum

---

*Plan created: 2025-10-31*
*Estimated execution: 4-6 hours*
*Expected outcome: 47% code reduction, cleaner architecture*
