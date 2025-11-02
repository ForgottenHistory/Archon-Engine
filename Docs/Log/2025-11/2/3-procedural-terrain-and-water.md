# Procedural Terrain Type Assignment and Heightmap-Based Water
**Date**: 2025-11-02
**Session**: 3
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Replace pixelated terrain boundaries with smooth, resolution-independent transitions
- Implement heightmap-based water detection for smooth coastlines

**Secondary Objectives:**
- Remove dependency on low-resolution terrain.bmp for terrain assignment
- Match Paradox quality for terrain/water blending

**Success Criteria:**
- Smooth terrain transitions (grassland → mountain → snow)
- Smooth coastlines (not following pixelated province boundaries)
- Resolution-independent (scales to any zoom level)

---

## Context & Background

**Previous Work:**
- See: [2-terrain-detail-mapping-implementation.md](../1/2-terrain-detail-mapping-implementation.md)
- Terrain detail mapping implemented but had sharp, pixelated boundaries
- All blending approaches (bilinear, multi-sampling, height-based) amplified pixelation

**Current State:**
- Terrain detail textures working
- Water/coastlines following pixelated province boundaries
- Attempted bilinear/height-based blending - all failed due to low-res source data

**Why Now:**
- Cannot create smooth detail from low-resolution source textures
- Need procedural approach like tessellation uses for geometry

---

## What We Did

### 1. Procedural Terrain Type Assignment (Height-Based)
**Files Changed:** `Assets/Game/Shaders/MapModes/MapModeTerrain.hlsl:76-151`

**Implementation:**
```hlsl
// Sample heightmap at current world position
float height = SAMPLE_TEXTURE2D(_HeightmapTexture, sampler_HeightmapTexture, correctedUV).r;

// PROCEDURAL TERRAIN RULES (height-based)
if (height > 0.25)
    proceduralTerrainType = 16; // Snow
else if (height > 0.15)
    proceduralTerrainType = 6; // Mountain
else
    proceduralTerrainType = 0; // Grassland

// SMOOTH BLENDING between terrain types
if (height > 0.23 && height < 0.27)
{
    // Blend zone between mountain and snow
    float blendFactor = (height - 0.23) / 0.04;
    blendFactor = smoothstep(blendFactor);
    blendedDetail = lerp(mountainDetail, snowDetail, blendFactor);
}
```

**Rationale:**
- Heightmap has bilinear filtering enabled (unlike terrain.bmp)
- Continuous height values create smooth gradients
- Same principle as tessellation: generate detail from continuous coordinates

**Key Insight:**
Like tessellation creates geometry detail from heightmap, we create texture detail from heightmap. Both are resolution-independent because they use continuous sampled values, not discrete texture pixels.

### 2. Heightmap-Based Water Detection
**Files Changed:** `Assets/Game/Shaders/MapModes/MapModeTerrain.hlsl:62-85`

**Implementation:**
```hlsl
// WATER DETECTION (height-based, like EU4)
float waterThreshold = 0.09; // Sea level
float beachThreshold = 0.12; // Coastal blend zone

if (height < waterThreshold)
{
    // Pure water - return ocean color
    macroColor.rgb = lerp(macroColor.rgb, _OceanColor.rgb, _DetailStrength);
}
else if (height < beachThreshold)
{
    // Beach/coastal blend zone - smooth transition
    float blendFactor = smoothstep((height - waterThreshold) / (beachThreshold - waterThreshold));
    float4 coastalBlend = lerp(_OceanColor, beachDetail, blendFactor);
    macroColor.rgb = lerp(macroColor.rgb, coastalBlend.rgb, _DetailStrength);
}
```

**Rationale:**
- EU4 uses heightmap-based water: "any values below (94, 94, 94) will be submerged"
- Heightmap has bilinear filtering → smooth coastlines
- Water detection runs for ALL pixels, not just water provinces

**Architecture Compliance:**
- ✅ Follows dual-layer architecture (GPU presentation independent of CPU simulation)
- ✅ Zero allocations (all procedural in shader)
- ✅ Scale-independent (world-space coordinates)

### 3. Removed Terrain Type Texture Dependency
**Files Changed:** `Assets/Game/Shaders/MapModes/MapModeTerrain.hlsl:51-67`

**Before:**
```hlsl
// Sample terrain type from low-res texture
uint terrainType = SAMPLE_TEXTURE2D(_TerrainTypeTexture, ...).r * 255.0;
if (terrainType == 15 || terrainType == 17 || ...) // Skip water
```

**After:**
```hlsl
// All pixels use procedural assignment
// Heightmap determines both terrain type AND water
```

**Impact:**
- Terrain type texture still exists but no longer drives visual rendering
- Could be removed entirely or repurposed for province-based overrides later

---

## Decisions Made

### Decision 1: Height-Based vs Province-Based Terrain Assignment
**Context:** Need smooth transitions but low-res source data (terrain.bmp, provinces.bmp)

**Options Considered:**
1. **Bilinear terrain type blending** - Interpolate between pixelated terrain types
   - Pros: Uses existing terrain.bmp data
   - Cons: Can't create detail from low-res source (tried, failed)

2. **Multi-texture splatting** - Sample multiple nearby terrain types, blend by distance
   - Pros: Could create smoother boundaries
   - Cons: Still constrained by pixelated source, creates blurry mess

3. **Height-based procedural** - Assign terrain types from continuous heightmap
   - Pros: Resolution-independent, smooth, matches tessellation approach
   - Cons: Can't distinguish Sahara from European plains (both flat)

4. **Province-based with world-space blending** - Store terrain per province, blend smoothly
   - Pros: Geographically accurate, smooth if done right
   - Cons: Complex, needs further investigation (deferred)

**Decision:** Chose Option 3 (Height-based procedural)

**Rationale:**
- Immediate solution that works (matches tessellation principle)
- Simple implementation (single heightmap sample)
- Can layer province-based overrides later if needed
- Proven approach (EU4 uses heightmap for water)

**Trade-offs:**
- Giving up: Geographic accuracy (desert placement)
- Gaining: Smooth visuals, simplicity, proven approach

**Future Path:**
Province-based terrain with world-space blending remains option for geographic accuracy. Would require sampling multiple provinces and blending by distance in world space.

### Decision 2: Water Thresholds
**Context:** Need to find correct sea level threshold

**Investigation:**
- EU4 wiki: sea level at RGB(94, 94, 94) = 0.369 in 0-1 range
- Our heightmap: Normalized differently, mountain peaks at ~0.25
- Through iteration: water at 0.09, beach zone 0.09-0.12

**Decision:** Sea level = 0.09, beach blend zone = 0.03

**Rationale:**
- Empirically determined by visual feedback
- Sensitive to small changes (0.01 makes visible difference)
- Matches actual water provinces in current map

---

## What Worked ✅

1. **Heightmap as Source of Truth**
   - What: Using continuous heightmap instead of discrete terrain texture
   - Why it worked: Bilinear filtering provides smooth interpolated values
   - Reusable pattern: Yes - same principle as tessellation

2. **Smoothstep for Blend Zones**
   - What: `smoothstep()` for natural falloff between terrain types
   - Impact: Eliminated visible hard edges in transition zones
   - Pattern: Use smoothstep for any gradual transition (0.04 range works well)

3. **Iterative Threshold Tuning**
   - What: Adjusted thresholds by 0.01 increments based on visual feedback
   - Why it worked: User could immediately see changes and provide feedback
   - Pattern: For visual thresholds, iterate with user in loop

---

## What Didn't Work ❌

1. **Bilinear Terrain Type Sampling**
   - What we tried: Sample terrain.bmp with bilinear filtering, blend types
   - Why it failed: Creates blurry, undefined boundaries - worse than sharp
   - Lesson learned: Bilinear filtering works on continuous data (heightmap), not categorical data (terrain indices)
   - Don't try this again because: Can't interpolate between "grassland" and "desert" meaningfully

2. **Multi-Texture Splatting with Pixel-Space Offsets**
   - What we tried: Sample 8 nearby pixels, blend by distance
   - Why it failed: Still sampling pixelated source, just averaging pixels
   - Lesson learned: Sampling more of pixelated data doesn't make it smooth
   - Pattern: Need continuous source data for smooth results

3. **Height-Based Blending for Geographic Accuracy**
   - What we tried: Use height + latitude for climate zones
   - Why incomplete: Need additional data (climate map, province terrain assignments)
   - Deferred: Province-based approach better for geographic accuracy

---

## Problems Encountered & Solutions

### Problem 1: Pixelated Coastlines Despite Bilinear Heightmap
**Symptom:** Hard, stair-stepped coastline edges even with bilinear-filtered heightmap

**Root Cause:** Water detection was inside `if (terrainType != water)` block, only running for land provinces. Ocean provinces bypassed procedural detection.

**Investigation:**
- User: "You clearly see a hard pixel perfect coastline. WHY"
- Checked: Heightmap has bilinear filtering ✓
- Checked: Water detection code ✓
- Found: Code only ran for non-water provinces ✗

**Solution:**
```hlsl
// BEFORE: Water detection inside terrain type check
if (terrainType != 15 && terrainType != 17 && ...)
{
    float height = SAMPLE_HEIGHTMAP(...);
    if (height < waterThreshold) { ... }
}

// AFTER: Water detection runs for ALL pixels
float height = SAMPLE_HEIGHTMAP(...);
if (height < waterThreshold)
{
    // Apply water regardless of province type
}
```

**Why This Works:** Heightmap-based water detection applies universally, creating smooth coastlines across all province types.

**Pattern for Future:** When implementing procedural rules, ensure they run for ALL relevant pixels, not gated by discrete categorizations.

### Problem 2: Syntax Error from Control Flow Restructuring
**Symptom:** `unexpected token 'return'` after moving water detection

**Root Cause:** Removed terrain type check but left orphaned closing brace

**Solution:** Properly closed control flow blocks after restructuring

**Pattern:** When refactoring nested if/else, trace all opening/closing braces

### Problem 3: Terrain Threshold Sensitivity
**Symptom:** User: "too much" / "too little" with small threshold changes

**Root Cause:** Heightmap values compressed in narrow range (0.09-0.25 for all terrain)

**Solution:**
- Small adjustments: 0.01 increments
- Iterative tuning with user feedback
- Final values: water 0.09, beach 0.12, grass 0.15, mountain 0.25

**Pattern:** For compressed value ranges, use fine-grained tuning (0.01 steps)

---

## Architecture Impact

### Documentation Updates Required
- [ ] Update terrain-detail-mapping architecture doc - Add procedural assignment approach
- [ ] Document height-based water detection pattern
- [ ] Note: Terrain type texture now optional (visual rendering doesn't use it)

### New Patterns Discovered
**Pattern: Procedural Assignment from Continuous Data**
- When to use: Need smooth transitions but only have low-res categorical data
- How: Use continuous data source (heightmap) with bilinear filtering
- Benefits: Resolution-independent, smooth, scales to any zoom level
- Example: Tessellation (geometry), terrain types (textures), water (blending)
- Add to: Rendering patterns architecture doc

**Pattern: Dual-Purpose Data**
- What: Heightmap serves both geometry (tessellation) and textures (terrain/water)
- When: Single continuous dataset can drive multiple systems
- Benefits: Memory efficient, consistent, single source of truth
- Add to: Data architecture patterns

### Anti-Pattern Discovered
**Anti-Pattern: Blending Categorical Data**
- What not to do: Apply bilinear filtering to terrain type indices
- Why it's bad: Can't meaningfully interpolate between "desert" and "ocean"
- Result: Blurry, undefined boundaries worse than sharp edges
- Add warning to: Texture sampling guidelines

---

## Code Quality Notes

### Performance
- **Measured:** Single heightmap sample per pixel (low cost)
- **Target:** Zero allocations, GPU-only
- **Status:** ✅ Meets target (all procedural in shader)

### Testing
- **Manual Tests:**
  - Zoom in/out - transitions remain smooth
  - Check coastlines - no pixelated province boundaries
  - Verify terrain distribution - snow on peaks, water in low areas

### Technical Debt
- **Created:**
  - Terrain type texture still generated but unused (could remove)
  - Hard-coded terrain indices (16=snow, 6=mountain, 0=grass)
  - No geographic accuracy (can't place desert in Sahara)

- **Future Work:**
  - Province-based terrain storage for geographic accuracy
  - World-space blending between province terrain types
  - Make thresholds configurable (not hard-coded)

---

## Next Session

### Immediate Next Steps
1. **Re-enable borders** - Disabled for testing, need them back
2. **Investigate province-based terrain** - How does Imperator achieve smooth blending from pixelated provinces?
3. **Make thresholds configurable** - Expose water/terrain thresholds to VisualStyleConfiguration

### Questions to Resolve
1. **How does Imperator blend terrain smoothly from pixelated province data?**
   - Theory: Sample multiple provinces, blend by world-space distance
   - Theory: Voronoi-like blending based on province centers
   - Theory: Pre-calculated distance fields
   - Need: Further investigation of Imperator's approach

2. **Should we keep province-based terrain data?**
   - Current: Height-only (simple but geographically inaccurate)
   - Future: Province-based (accurate but complex)
   - Question: Is height-based sufficient for game needs?

### Docs to Read Before Next Session
- Imperator map data analysis (terrain-investigation folder)
- Province data structures (for province-based terrain implementation)

---

## Session Statistics

**Files Changed:** 2
- `Assets/Game/Shaders/MapModes/MapModeTerrain.hlsl`
- `Assets/Game/VisualStyles/EU3Classic/EU3MapShaderTessellated.shader`

**Lines Added/Removed:** +64/-52
**Commits:** 2
- "Implement procedural terrain type assignment with height-based blending"
- "Add heightmap-based water detection with smooth coastlines"

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `MapModeTerrain.hlsl:62-151`
- Critical decision: Height-based procedural (NOT province-based yet)
- Active pattern: Procedural assignment from continuous data (like tessellation)
- Current status: Smooth terrain/water working, but geographically inaccurate

**What Changed Since Last Doc Read:**
- Architecture: Terrain type texture no longer drives rendering
- Implementation: Fully procedural terrain assignment from heightmap
- Constraints: Can't distinguish flat desert from flat plains (height-only)

**Gotchas for Next Session:**
- Watch out for: Heightmap has bilinear filtering, terrain type texture has point filtering
- Don't forget: Water threshold is very sensitive (0.01 changes visible)
- Remember: User wants Imperator-quality (province-based + smooth blending)

**Investigation Required:**
Imperator achieves smooth terrain transitions from pixelated province data. Files examined:
- `default.map` - Province-based climate/terrain definitions
- `climate.txt` - Province ID lists for climate types
- `imperator_terrain1.png` - Shows perfectly smooth blending

**Theory:** They sample multiple provinces and blend by world-space distance, not texture pixels. Need to implement similar approach for geographic accuracy.

---

## Links & References

### Related Documentation
- [terrain-detail-mapping.md](../../Planning/terrain-detail-mapping.md) - Architecture planning
- [master-architecture-document.md](../../Engine/master-architecture-document.md) - Dual-layer system

### Related Sessions
- [2-terrain-detail-mapping-implementation.md](../1/2-terrain-detail-mapping-implementation.md) - Previous session (blocked)

### Code References
- Procedural terrain: `MapModeTerrain.hlsl:76-151`
- Water detection: `MapModeTerrain.hlsl:62-85`
- Heightmap creation: `VisualTextureSet.cs:93-116` (bilinear filtering enabled)

---

## Notes & Observations

**Key Realization:**
Can't create smooth detail from low-resolution source data. Need continuous data source (heightmap) or sophisticated blending algorithm (province-based with world-space weights).

**Imperator Analysis:**
- No terrain.bmp texture found
- Uses province-based terrain assignment (climate.txt, definition files)
- Achieves smooth blending despite pixelated provinces
- **Hypothesis:** World-space distance-based blending between province terrain types

**User Insight:**
"Province bitmaps are completely straight pixels" - The challenge is achieving smooth blending FROM pixelated source data. This is only possible by using world-space coordinates for blend weights, not texture pixel lookups.

**Pattern Connection:**
Tessellation (geometry) and terrain (textures) both use same principle:
- Sample continuous data (heightmap)
- Generate detail procedurally
- Resolution-independent results
This pattern is fundamental to scale-independent rendering.

---

*Session completed: 2025-11-02*
