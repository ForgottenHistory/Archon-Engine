# Terrain Shader Simplification: Direct Texture Sampling
**Date**: 2026-01-16
**Session**: 02
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Fix terrain colors not displaying correctly in shader after terrain system fixes from session 01

**Secondary Objectives:**
- Simplify terrain rendering by removing unnecessary indirection
- Clean up unused palette texture code

**Success Criteria:**
- Terrain colors display correctly for unowned provinces
- Code is simpler with no unused abstractions

---

## Context & Background

**Previous Work:**
- See: [01-terrain-system-single-source-of-truth.md](01-terrain-system-single-source-of-truth.md)
- Fixed TerrainOverrideApplicator to use correct data path
- Terrain types (T0-T14) now assigned correctly

**Current State:**
- Terrain types correct but shader showing wrong colors (only green and ocean blue)
- Overly complex terrain color lookup via palette texture

**Why Now:**
- Session 01 fixed terrain type assignment but colors still broken

---

## What We Did

### 1. Investigated Shader Color Issue
**Root Cause Analysis:**
- Shader sampled `_TerrainTypeTexture` for terrain index
- Then looked up color from `_TerrainColorPalette` (256x1 texture)
- `TerrainTypeTextureGenerator` was mapping ALL pixels to index 0
- Log showed: `Samples - Type [0] [0] [0]` despite 14 unique colors in terrain.png

**Key Insight from User:**
> "isn't it as simple as using the terrain.png? why are you doing lookups and whatever"

### 2. Simplified Shader to Direct Texture Sampling
**Files Changed:**
- `Shaders/Includes/MapModeTerrainSimple.hlsl:111-124`

**Before (convoluted):**
```hlsl
// Sample terrain index from _TerrainTypeTexture
float terrainIndexRaw = SAMPLE_TEXTURE2D(_TerrainTypeTexture, ...).r;
uint terrainTypeIndex = (uint)(terrainIndexRaw * 255.0 + 0.5);

// Look up color from palette
float2 paletteUV = float2(((float)terrainTypeIndex + 0.5) / 256.0, 0.5);
float3 terrainColor = SAMPLE_TEXTURE2D(_TerrainColorPalette, ..., paletteUV).rgb;
```

**After (simple):**
```hlsl
// Sample terrain color directly from terrain.png
float3 terrainColor = SAMPLE_TEXTURE2D(_ProvinceTerrainTexture, ..., correctedUV).rgb;
```

### 3. Removed Unused Palette Code
**Files Changed:**
- `Map/Rendering/Terrain/TerrainRGBLookup.cs` - Removed palette generation
- `Map/Rendering/VisualTextureSet.cs` - Removed palette field and methods
- `Map/MapTextureManager.cs` - Removed `SetTerrainColorPalette()`
- `Map/Rendering/ProvinceTerrainAnalyzer.cs` - Removed `GetTerrainColorPalette()`
- `Map/Loading/MapDataLoader.cs` - Removed palette creation/binding
- `Shaders/DefaultTerrainMapShader.shader` - Removed `_TerrainColorPalette` property

---

## Decisions Made

### Decision 1: Direct Texture Sampling vs Palette Lookup
**Context:** Terrain colors needed to be displayed in shader
**Options Considered:**
1. Fix palette lookup system (debug why all indices = 0)
2. Sample terrain.png directly (already has the colors we want)

**Decision:** Option 2 - Direct sampling
**Rationale:**
- terrain.png already contains the RGB colors
- No need for index→color indirection
- Simpler code, fewer failure points
- `_ProvinceTerrainTexture` is already bound to material

**Trade-offs:**
- Slightly less flexible (can't remap colors at runtime without regenerating texture)
- But we don't need that flexibility - terrain.json5 defines colors

---

## What Worked ✅

1. **User insight: "just use terrain.png"**
   - What: User pointed out unnecessary complexity
   - Why it worked: Fresh perspective on overengineered solution
   - Reusable pattern: Question complexity before debugging it

2. **Reading GPU debugging docs**
   - What: Found `unity-gpu-debugging-guide.md` and `explicit-graphics-format.md`
   - Why it worked: Provided context on texture handling patterns
   - Location: `Docs/Log/learnings/` and `Docs/Log/decisions/`

---

## What Didn't Work ❌

1. **Palette texture approach**
   - What we tried: Create 256x1 palette, shader lookups terrain index → color
   - Why it failed: TerrainTypeTextureGenerator mapped all colors to index 0
   - Root cause: TerrainColorMapper wasn't finding color matches properly
   - Lesson learned: Don't add indirection when direct access works

2. **Multiple rebind attempts**
   - What we tried: Rebinding palette texture to material after creation
   - Why it failed: The texture itself had wrong data, not a binding issue
   - Lesson learned: Verify data correctness before suspecting binding

---

## Architecture Impact

### Pattern Reinforced
**Pattern:** KISS (Keep It Simple)
- When to use: Always question if abstraction is necessary
- What we learned: Direct texture sampling >> index lookup >> palette lookup
- Anti-pattern avoided: Unnecessary indirection layers

### Code Removed
- ~100 lines of palette generation/binding code
- 1 shader property
- Multiple method chains across 6 files

---

## Code Quality Notes

### Technical Debt
- **Paid Down:** Removed unused TerrainColorMapper→TerrainTypeTextureGenerator→palette pipeline
- **Note:** `_TerrainTypeTexture` and `TerrainTypeTextureGenerator` still exist for terrain detail blending (different use case)

---

## Session Statistics

**Files Changed:** 7
- MapModeTerrainSimple.hlsl (shader fix)
- TerrainRGBLookup.cs (removed ~60 lines)
- VisualTextureSet.cs (removed ~25 lines)
- MapTextureManager.cs (removed method)
- ProvinceTerrainAnalyzer.cs (removed method)
- MapDataLoader.cs (removed ~25 lines)
- DefaultTerrainMapShader.shader (removed property)

**Lines Removed:** ~120
**Lines Added:** 3 (shader)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Terrain colors come directly from `_ProvinceTerrainTexture` (terrain.png)
- No palette lookup needed for terrain visualization
- `_TerrainTypeTexture` still used for terrain detail blending (different system)

**Gotchas for Next Session:**
- Don't recreate palette system - direct sampling is intentional
- `TerrainColorMapper` still exists for other purposes (detail texture selection)

---

## Links & References

### Code References
- Shader fix: `Shaders/Includes/MapModeTerrainSimple.hlsl:111-124`
- Terrain texture binding: `Map/Rendering/VisualTextureSet.cs:249`

### Related Docs
- GPU debugging: `Docs/Log/learnings/unity-gpu-debugging-guide.md`
- Imperator terrain analysis: `Docs/Log/learnings/imperator-rome-terrain-rendering-analysis.md`

---

## Notes & Observations

- The palette approach would have worked if TerrainColorMapper indices matched TerrainRGBLookup indices
- But simpler solution was available all along
- terrain.png is the single source of truth for terrain COLORS
- terrain.json5 is the single source of truth for terrain TYPES and PROPERTIES

---

*Session Duration: ~45 minutes*
