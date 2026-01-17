# GPU-Accelerated Gradient Map Modes
**Date**: 2026-01-17
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix FarmDensityMapMode to display correctly and update efficiently

**Secondary Objectives:**
- Implement texture array system for instant map mode switching
- Optimize performance to eliminate monthly tick lag

**Success Criteria:**
- Map mode displays correctly (not flipped, aligned with borders)
- Updates smoothly without noticeable frame drops
- Instant switching between map modes

---

## Context & Background

**Previous Work:**
- See: [08-lua-scripting-system.md](../16/08-lua-scripting-system.md)
- Related: [unity-compute-shader-coordination.md](../../learnings/unity-compute-shader-coordination.md)

**Current State:**
- FarmDensityMapMode existed but wasn't displaying correctly
- Map mode switching required full texture recomputation
- Shader had hardcoded map mode keywords (violated ENGINE/GAME separation)

**Why Now:**
- StarterKit needs working map mode example for showcasing patterns

---

## What We Did

### 1. Texture Array System for Instant Switching
**Files Changed:**
- `Assets/Archon-Engine/Scripts/Map/MapModes/MapModeManager.cs`
- `Assets/Archon-Engine/Shaders/Includes/DefaultCommon.hlsl`
- `Assets/Archon-Engine/Shaders/Includes/DefaultMapModes.hlsl`

**Implementation:**
- Added `_MapModeTextureArray` (Texture2DArray) with 16 pre-allocated slots
- Each GAME map mode registers and gets its own texture slice
- Switching modes = changing `_CustomMapModeIndex` (instant, GPU-side)

**Rationale:**
- Paradox games have instant map mode switching - this is the golden standard
- Pre-computing textures means no recomputation on switch

### 2. GPU Compute Shader Integration
**Files Changed:** `Assets/Archon-Engine/Scripts/Map/MapModes/GradientMapMode.cs`

**Implementation:**
- Rewrote GradientMapMode to use existing `GradientMapMode.compute` shader
- CPU calculates province values (~3000 provinces, fast)
- GPU compute shader colorizes all 11.5M pixels (~1ms)
- `Graphics.CopyTexture` copies result to texture array (GPU-to-GPU, ~0.1ms)

**Key Code:**
```csharp
// Phase 1: Calculate province values (CPU - fast)
var stats = CalculateProvinceValues(allProvinces, provinceQueries, gameProvinceSystem);

// Phase 2: Run GPU compute shader (GPU - ~1ms)
RunGPUColorization(stats);

// Phase 3: Copy to texture array (GPU-to-GPU, no CPU roundtrip)
Graphics.CopyTexture(outputTexture, 0, 0, mapModeTextureArray, arrayIndex, 0);
```

### 3. Event-Driven Updates with Batching
**Files Changed:** `Assets/Archon-Engine/Scripts/Map/MapModes/MapModeManager.cs`

**Implementation:**
- `MarkDirty()` requests deferred update via `RequestDeferredUpdate()`
- Multiple events in same frame are batched (single update in LateUpdate)
- Only updates if map mode is currently active

**Rationale:**
- Monthly tick fires many ownership change events (AI colonization)
- Without batching: N events = N full texture rebuilds = massive lag
- With batching: N events = 1 texture rebuild = smooth

### 4. Y-Flip Coordination Fix
**Files Changed:** `Assets/Archon-Engine/Shaders/Includes/DefaultMapModes.hlsl`

**Implementation:**
```hlsl
float4 RenderCustomMapMode(uint provinceID, float2 uv)
{
    // Y-flip UV for RenderTexture sampling
    // Fragment UVs: (0,0)=bottom-left, RenderTexture storage: (0,0)=top-left
    float2 flippedUV = float2(uv.x, 1.0 - uv.y);

    float4 color = SAMPLE_TEXTURE2D_ARRAY(_MapModeTextureArray, sampler_MapModeTextureArray, flippedUV, _CustomMapModeIndex);
    // ...
}
```

**Architecture Compliance:**
- ✅ Follows [unity-compute-shader-coordination.md](../../learnings/unity-compute-shader-coordination.md)
- Y-flip ONLY in fragment shader, NOT in compute shaders

---

## Decisions Made

### Decision 1: GPU Compute vs CPU Pixel Iteration
**Context:** Initial implementation iterated all pixels on CPU (125-170ms per update)
**Options Considered:**
1. CPU pixel iteration - simple but 125ms+ per update
2. GPU compute shader - fast (~1ms) but more complex setup
3. Job System parallelization - medium complexity, still ~30ms

**Decision:** GPU compute shader
**Rationale:**
- Existing `GradientMapMode.compute` already written and tested
- 100x+ speedup (1ms vs 125ms)
- Follows dual-layer architecture (GPU for presentation)
**Trade-offs:** More complex initialization, requires compute shader support

### Decision 2: Graphics.CopyTexture vs ReadPixels
**Context:** Need to copy compute output to texture array
**Options Considered:**
1. ReadPixels → SetPixels32 → Apply (CPU roundtrip, 170ms)
2. Graphics.CopyTexture (GPU-to-GPU, <1ms)
3. Direct RenderTexture binding (breaks instant switching)

**Decision:** Graphics.CopyTexture
**Rationale:**
- Stays entirely on GPU, no CPU sync stall
- Preserves texture array for instant switching
- Documented in unity-compute-shader-coordination.md
**Trade-offs:** None - this is the correct approach

### Decision 3: Deferred Update Batching
**Context:** Monthly tick fires 20+ ownership change events
**Options Considered:**
1. Immediate update per event (20 rebuilds = 20ms+ lag)
2. Deferred update in LateUpdate (1 rebuild = smooth)
3. Frame-skip throttling (complex, inconsistent)

**Decision:** Deferred batching in LateUpdate
**Rationale:** Simple, effective, processes all events in single frame
**Trade-offs:** 1 frame latency (imperceptible)

---

## What Worked ✅

1. **GPU Compute Shader for Pixel Processing**
   - What: Use existing GradientMapMode.compute instead of CPU iteration
   - Why it worked: GPU processes 11.5M pixels in parallel
   - Reusable pattern: Yes - all gradient-based map modes use this

2. **Graphics.CopyTexture for GPU-to-GPU Copy**
   - What: Copy RenderTexture to Texture2DArray slice without CPU
   - Impact: Eliminated 170ms CPU stall, now <1ms total

3. **Deferred Update Batching**
   - What: Collect dirty flags, single update in LateUpdate
   - Impact: Monthly tick with 20+ events stays smooth

---

## What Didn't Work ❌

1. **CPU Pixel Iteration**
   - What we tried: Iterate all provinces, fill pixel buffers on CPU
   - Why it failed: 11.5M pixels = 125-170ms per update
   - Lesson learned: Never iterate millions of pixels on CPU
   - Don't try this again because: GPU exists for exactly this purpose

2. **ReadPixels for GPU→Texture Array Copy**
   - What we tried: ReadPixels → GetPixels32 → SetPixels32 → Apply
   - Why it failed: Forces GPU→CPU sync, 170ms stall
   - Lesson learned: ReadPixels is a synchronization point
   - Don't try this again because: Graphics.CopyTexture is the correct approach

---

## Problems Encountered & Solutions

### Problem 1: Map Mode Not Displaying (Only Political Showed)
**Symptom:** Switching to Farm mode showed political colors
**Root Cause:** Shaders had hardcoded map mode keywords (ENGINE/GAME violation)
**Solution:** Implemented texture array system with integer-based mode switching

### Problem 2: 125-170ms Update Lag on Monthly Tick
**Symptom:** Noticeable frame drop every monthly tick
**Root Cause:** CPU pixel iteration + ReadPixels GPU sync
**Solution:**
1. GPU compute shader for colorization
2. Graphics.CopyTexture for GPU-to-GPU copy
3. Deferred batching for multiple events
**Pattern for Future:** Always use GPU for pixel-level operations

### Problem 3: Map Displayed Flipped/Misaligned
**Symptom:** Colors didn't align with province borders, appeared flipped
**Root Cause:** Coordinate system mismatch between compute shader and fragment shader
**Solution:** Y-flip in fragment shader when sampling texture array
```hlsl
float2 flippedUV = float2(uv.x, 1.0 - uv.y);
```
**Why This Works:** Per unity-compute-shader-coordination.md - Y-flip ONLY in fragment shaders

---

## Architecture Impact

### New Patterns Discovered
**Pattern:** GPU-Accelerated Map Mode Updates
- When to use: Any map mode that needs to colorize provinces
- Benefits: 100x+ faster than CPU, no frame drops
- Implementation:
  1. CPU calculates per-province values (fast, ~3000 items)
  2. GPU compute shader colorizes pixels (parallel, ~1ms)
  3. Graphics.CopyTexture to texture array (GPU-to-GPU, <1ms)

**Pattern:** Deferred Update Batching
- When to use: Event-driven updates that may fire multiple times per frame
- Benefits: Prevents redundant work, smooth frame times
- Implementation: Set dirty flag, process in LateUpdate

---

## Code Quality Notes

### Performance
- **Before:** 125-170ms per map mode update
- **After:** ~2-3ms per update (compute + copy)
- **Target:** <16ms (60 FPS budget)
- **Status:** ✅ Meets target

### Technical Debt
- **Paid Down:** Removed hardcoded shader keywords, proper ENGINE/GAME separation

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- Key implementation: `GradientMapMode.cs:237-277` (UpdateTextures method)
- Critical decision: GPU compute + Graphics.CopyTexture for performance
- Active pattern: Texture array for instant mode switching

**Gotchas for Next Session:**
- Y-flip ONLY in fragment shader, never in compute shader
- Graphics.CopyTexture requires matching texture formats
- MarkDirty() only triggers update if map mode is active

---

## Links & References

### Related Documentation
- [unity-compute-shader-coordination.md](../../learnings/unity-compute-shader-coordination.md) - GPU coordination patterns
- [unity-gpu-debugging-guide.md](../../learnings/unity-gpu-debugging-guide.md) - GPU debugging

### Code References
- GradientMapMode: `Assets/Archon-Engine/Scripts/Map/MapModes/GradientMapMode.cs`
- Compute shader: `Assets/Archon-Engine/Shaders/GradientMapMode.compute`
- Fragment shader: `Assets/Archon-Engine/Shaders/Includes/DefaultMapModes.hlsl:25-44`
- MapModeManager: `Assets/Archon-Engine/Scripts/Map/MapModes/MapModeManager.cs:552-570`

---

*Template Version: 1.0 - Created 2025-09-30*
