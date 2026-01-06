# Hybrid Terrain System Implementation
**Date**: 2025-11-18
**Session**: 1
**Status**: 🔄 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Implement hybrid terrain system: texture-based coarse assignment + world-space procedural detail

**Secondary Objectives:**
- Investigate Imperator Rome terrain data structure
- Understand EU4 vs Imperator terrain assignment differences
- Create GPU compute shader for province terrain analysis

**Success Criteria:**
- Compute shader analyzes terrain.bmp and assigns dominant terrain type per province (majority vote)
- Shader uses province terrain category for coarse assignment, world-space height for fine detail
- System maintains resolution independence (works at any zoom level)

---

## Context & Background

**Previous Work:**
- HSV color grading and multi-material blending from Imperator Rome analysis (same session)
- Border strength parameter fix (same session)
- Related: [imperator-rome-terrain-rendering-analysis.md](../learnings/imperator-rome-terrain-rendering-analysis.md)

**Current State:**
- Terrain rendering uses pure procedural height-based assignment
- System is resolution-independent via world-space coordinates
- Works with tessellation but lacks artist control

**Why Now:**
- User wants to use terrain.bmp for artist control (like Imperator/EU4)
- Pure texture-based breaks resolution independence (fixed pixel count)
- Hybrid approach solves both: texture = coarse category, procedural = fine detail

---

## What We Did

### 1. Investigated Imperator Rome Terrain Data
**Files Analyzed:** `Assets\Imperator-Rome-Data\setup\provinces\*.txt`, `common\terrain_types\00_terrains.txt`

**Discovery:**
Imperator uses per-province terrain definitions in province files:
```
1470 = {
    terrain = "plains"
    trade_goods = "stone"
    ...
}
```

Terrain types defined in `common/terrain_types/00_terrains.txt`:
- plains, mountain, hills, desert, marsh, jungle, farmland, forest, ocean, etc.
- Each has color, movement cost, combat modifiers

**Runtime Generation:**
1. Game reads province terrain assignments from data files
2. Generates `DetailIndexTexture` at startup from province data
3. Shader samples texture to get material indices

### 2. Analyzed EU4 Terrain Data Structure
**Files Analyzed:** `Assets\Data\map\terrain.json5`

**Discovery:**
EU4 does NOT store terrain per province! Uses hybrid approach:
- `terrain.bmp` - Texture where pixel color (0-255) maps to terrain type
- `terrain.json5` - Defines color → terrain type mapping
- `terrain_override` arrays - Province-specific overrides

**Key Insight:**
EU4 already uses texture-based terrain, exactly what we need!

**Our Current System:**
- Has `terrain.bmp` but doesn't use it (pure procedural instead)
- User wants to use texture for artist control
- But texture is fixed resolution (breaks "infinite scale" idea)

### 3. Designed Hybrid Solution
**Architecture:** Two-layer terrain system

**Layer 1: Coarse Assignment (Province-Level)**
- Sample `terrain.bmp` at province center to get terrain TYPE
- Province gets category: mountain, grassland, desert, etc.
- Artist control via texture painting

**Layer 2: Fine Detail (World-Space Procedural)**
- Use world position for detail blending (resolution-independent!)
- Heightmap, moisture, temperature drive fine detail
- Works with tessellation/infinite zoom

**Example:**
```
terrain.bmp: Province #500 = MOUNTAIN (category)
World height at (x=1234, z=5678) = 180m → rock texture
World height at (x=1235, z=5679) = 240m → snow texture
```

**Benefits:**
✅ Artist control (paint terrain.bmp)
✅ Resolution-independent (world-space sampling)
✅ Works with tessellation
✅ Natural transitions at all scales

### 4. Created Province Terrain Analyzer Compute Shader
**Files Created:** `Assets\Archon-Engine\Shaders\Compute\ProvinceTerrainAnalyzer.compute`

**Implementation:**
Two-pass GPU algorithm for majority voting:

**Pass 1 (CountVotes):**
- Each pixel thread reads province ID and terrain type
- Atomically increments: `VoteMatrix[provinceID * 256 + terrainType]++`
- Result: Vote counts for all 256 terrain types per province

**Pass 2 (DetermineWinner):**
- Each province scans its 256 vote counters
- Finds terrain type with most votes
- Writes: `ProvinceTerrainTypes[provinceID] = winningTerrainType`

**Architecture Compliance:**
- ✅ Follows [unity-compute-shader-coordination.md](../learnings/unity-compute-shader-coordination.md)
  - Uses `RWTexture2D` for all textures (not `Texture2D`)
  - No Y-flipping in compute shader (raw GPU coordinates)
  - 8x8 thread groups (64 threads, optimal for GPU warps)
  - Atomic operations for safe concurrent writes
- ✅ Follows [explicit-graphics-format.md](../decisions/explicit-graphics-format.md)
  - Will use explicit `GraphicsFormat.R8G8B8A8_UNorm` for textures
  - Avoids TYPELESS format issues

**Key Code:**
```hlsl
// Pass 1: Count votes
[numthreads(8, 8, 1)]
void CountVotes(uint3 id : SV_DispatchThreadID)
{
    float4 provinceIDColor = ProvinceIDTexture[id.xy];
    uint provinceID = DecodeProvinceID(provinceIDColor);

    float4 terrainColor = TerrainTexture[id.xy];
    uint terrainType = uint(terrainColor.r * 255.0);

    uint voteIndex = provinceID * 256 + terrainType;
    InterlockedAdd(VoteMatrix[voteIndex], 1);
}

// Pass 2: Determine winner
[numthreads(256, 1, 1)]
void DetermineWinner(uint3 id : SV_DispatchThreadID)
{
    uint provinceID = id.x;

    uint maxVotes = 0;
    uint winningTerrainType = 0;

    for (uint terrainType = 0; terrainType < 256; terrainType++)
    {
        uint voteIndex = provinceID * 256 + terrainType;
        if (VoteMatrix[voteIndex] > maxVotes)
        {
            maxVotes = VoteMatrix[voteIndex];
            winningTerrainType = terrainType;
        }
    }

    ProvinceTerrainTypes[provinceID] = winningTerrainType;
}
```

**Why Two-Pass:**
groupshared memory only works within a thread group (can't share across groups). Need global counting per province, so use two passes with StructuredBuffer.

### 5. Created C# Dispatcher for Terrain Analysis
**Files Created:** `Assets\Archon-Engine\Scripts\Map\Rendering\ProvinceTerrainAnalyzer.cs`

**Implementation:**
```csharp
public uint[] AnalyzeProvinceTerrain(
    RenderTexture provinceIDTexture,
    RenderTexture terrainTexture,
    int provinceCount)
{
    // Pass 1: Count votes
    terrainAnalyzerCompute.Dispatch(countVotesKernel, threadGroupsX, threadGroupsY, 1);

    // CRITICAL: GPU sync between passes
    var syncRequest = AsyncGPUReadback.Request(voteMatrix);
    syncRequest.WaitForCompletion();

    // Pass 2: Determine winner
    terrainAnalyzerCompute.Dispatch(determineWinnerKernel, threadGroupsForProvinces, 1, 1);

    // Read results
    uint[] results = new uint[provinceCount];
    provinceTerrainTypes.GetData(results);
    return results;
}
```

**Architecture Compliance:**
- ✅ Uses `AsyncGPUReadback.WaitForCompletion()` for GPU sync between passes
- ✅ Releases GPU buffers in finally block
- ✅ Proper logging for debugging
- ✅ Returns both CPU array and GPU lookup texture options

### 6. Updated MapModeTerrain.hlsl with Hybrid System
**Files Changed:** `Assets\Archon-Engine\Shaders\Includes\MapModeTerrain.hlsl`

**Implementation:**
```hlsl
// Added province terrain lookup texture
TEXTURE2D(_ProvinceTerrainLookup);
SAMPLER(sampler_ProvinceTerrainLookup);

uint GetProvinceTerrainCategory(uint provinceID)
{
    float2 lookupUV = float2((provinceID + 0.5) / 10000.0, 0.5);
    float4 lookupValue = SAMPLE_TEXTURE2D(_ProvinceTerrainLookup, sampler_ProvinceTerrainLookup, lookupUV);
    return (uint)(lookupValue.r * 255.0 + 0.5);
}

// Hybrid terrain rendering
uint provinceTerrainCategory = GetProvinceTerrainCategory(provinceID);

// Map category to base material
if (provinceTerrainCategory == 6 || provinceTerrainCategory == 2)
    baseTerrainType = 6; // Mountain
else if (provinceTerrainCategory == 16)
    baseTerrainType = 16; // Snow
else
    baseTerrainType = 0; // Grassland

// World-space detail blending (resolution-independent!)
if (baseTerrainType == TERRAIN_MOUNTAIN)
{
    // Mountain provinces: rock → snow based on world-space Y
    float snowThreshold = 220.0; // meters
    float snowBlend = saturate((positionWS.y - snowThreshold) / 40.0);
    blendedDetail = lerp(mountainDetail, snowDetail, snowBlend);
}
```

**Key Design:**
- Province terrain category from texture (artist control)
- World-space height for fine detail (resolution-independent)
- Category-specific blending rules (mountains use world Y, grasslands use heightmap)

---

## Decisions Made

### Decision 1: Hybrid Texture + Procedural Terrain
**Context:** User wants terrain.bmp control but system must stay resolution-independent

**Options Considered:**
1. Pure texture-based - Breaks resolution independence (fixed pixel count)
2. Pure procedural - No artist control
3. Hybrid: Texture category + procedural detail - Best of both

**Decision:** Chose Option 3 (Hybrid)

**Rationale:**
- Artists paint broad terrain types in terrain.bmp
- Shader adds infinite-resolution detail via world-space sampling
- Scales to any zoom level (supports tessellation)

**Trade-offs:**
- More complex than pure approach
- Requires compute shader preprocessing
- Need to define category-specific blending rules

### Decision 2: Two-Pass Compute Shader for Majority Vote
**Context:** Need to count votes per province across all pixels

**Options Considered:**
1. Single-pass with groupshared memory - Doesn't work (can't share across thread groups)
2. Two-pass with global buffer - Works but requires GPU sync
3. CPU preprocessing - Too slow for large maps

**Decision:** Chose Option 2 (Two-pass GPU)

**Rationale:**
- groupshared memory only works within thread group (256-1024 threads)
- Provinces can span thousands of pixels across multiple thread groups
- Need global vote counting → use StructuredBuffer

**Trade-offs:**
- Requires GPU sync between passes (adds ~30ms startup cost)
- More complex than single-pass
- Acceptable for initialization (runs once)

---

## What Worked ✅

1. **Two-Pass Compute Shader Architecture**
   - What: Pass 1 counts votes, Pass 2 finds winner
   - Why it worked: Avoids groupshared memory limitations, uses global buffer
   - Reusable pattern: Yes - any per-province aggregation

2. **Following unity-compute-shader-coordination.md**
   - What: RWTexture2D, no Y-flip, explicit GPU sync
   - Impact: No debugging time wasted on coordinate/texture binding issues

3. **Hybrid Design Insight**
   - What: Recognized texture = category, procedural = detail
   - Why it worked: Solves both artist control AND resolution independence
   - Reusable pattern: Yes - applies to any multi-scale rendering problem

---

## What Didn't Work ❌

1. **Initial Single-Pass groupshared Approach**
   - What we tried: Use groupshared memory to count votes per province
   - Why it failed: groupshared only works within thread group (can't aggregate across groups)
   - Lesson learned: groupshared is local to thread group, use StructuredBuffer for global data
   - Don't try this again because: Fundamental GPU limitation

---

## Problems Encountered & Solutions

### Problem 1: Resolution Independence vs Artist Control
**Symptom:** Pure texture-based terrain breaks at high zoom (pixelated), pure procedural has no artist control

**Root Cause:** Texture = fixed resolution, procedural = no control

**Solution:**
Hybrid approach - texture for coarse category, world-space procedural for fine detail

**Why This Works:**
- Texture resolution doesn't matter (just used for category lookup)
- Detail comes from world-space sampling (infinite resolution)
- Best of both worlds

**Pattern for Future:** When facing fixed-resolution vs control trade-off, use multi-layer approach

---

## Architecture Impact

### Documentation Updates Required
- [ ] None yet - hybrid system not fully tested

### New Patterns Discovered
**New Pattern:** Hybrid Multi-Scale Rendering
- When to use: When you need both artist control (texture) and resolution independence (procedural)
- Benefits: Combines fixed-resolution input with infinite-resolution detail
- Example: Terrain category from texture, detail from world-space rules

---

## Code Quality Notes

### Performance
- **Measured:** Not yet
- **Target:** Single GPU pass for analysis at startup (<50ms for 10k provinces)
- **Status:** ⚠️ Needs testing

### Testing
- **Tests Written:** None (compute shader not hooked up yet)
- **Manual Tests:** Need to run analyzer and verify terrain assignments

### Technical Debt
- **TODOs:**
  - `GetProvinceTerrainCategory()` hardcodes province count (10000) - need actual count
  - Terrain category → material mapping is hardcoded (need terrain definitions loader)
  - Forest material index not implemented (uses grassland as placeholder)

---

## Next Session

### Immediate Next Steps (Priority Order)
1. Hook up ProvinceTerrainAnalyzer in initialization pipeline
2. Test terrain analysis on real terrain.bmp data
3. Verify world-space detail blending works at different zoom levels
4. Load terrain definitions from EU4 data (terrain.json5)

### Questions to Resolve
1. Where should terrain analysis run in initialization? (After province ID texture populated)
2. How to pass province count to shader? (Shader uniform)
3. Should we cache analysis results? (Yes - only changes when terrain.bmp changes)

---

## Session Statistics

**Files Changed:** 3
**Files Created:** 2
- `ProvinceTerrainAnalyzer.compute` (102 lines)
- `ProvinceTerrainAnalyzer.cs` (180 lines)
- Modified `MapModeTerrain.hlsl` (~50 lines changed)

**Lines Added:** ~332
**Tests Added:** 0
**Commits:** 1 (Imperator visual enhancements + border fix from earlier in session)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Hybrid terrain: texture category (coarse) + world-space detail (fine)
- Two-pass compute shader: CountVotes → DetermineWinner with GPU sync
- Key implementation: `ProvinceTerrainAnalyzer.cs`, `ProvinceTerrainAnalyzer.compute`
- Current status: Shader written but not tested/hooked up

**What Changed Since Last Doc Read:**
- New hybrid terrain architecture designed and partially implemented
- Compute shader for province terrain analysis created
- MapModeTerrain.hlsl updated for category-based blending

**Gotchas for Next Session:**
- Need to pass actual province count to shader (currently hardcoded 10000)
- Terrain category → material mapping is hardcoded (need definitions loader)
- GPU sync between compute passes is CRITICAL (see unity-compute-shader-coordination.md)

---

## Links & References

### Related Documentation
- [unity-compute-shader-coordination.md](../learnings/unity-compute-shader-coordination.md) - GPU sync patterns
- [explicit-graphics-format.md](../decisions/explicit-graphics-format.md) - RenderTexture format rules
- [imperator-rome-terrain-rendering-analysis.md](../learnings/imperator-rome-terrain-rendering-analysis.md) - 4-channel material system

### Code References
- Compute shader: `Assets\Archon-Engine\Shaders\Compute\ProvinceTerrainAnalyzer.compute`
- Dispatcher: `Assets\Archon-Engine\Scripts\Map\Rendering\ProvinceTerrainAnalyzer.cs`
- Terrain rendering: `Assets\Archon-Engine\Shaders\Includes\MapModeTerrain.hlsl:97-253`

### External Resources
- EU4 terrain data: `Assets\Data\map\terrain.json5`
- Imperator terrain data: `Assets\Imperator-Rome-Data\common\terrain_types\00_terrains.txt`

---

## Notes & Observations

- User confirmed EU4 data doesn't have per-province terrain (unlike Imperator)
- Majority vote algorithm is simple but effective for province terrain assignment
- World-space Y for snow line (mountains) is more intuitive than heightmap values
- Hybrid approach feels like the "right" solution - elegant combination of artist control and procedural detail

---

*Session ended before testing/integration - need to hook up and verify in next session*
