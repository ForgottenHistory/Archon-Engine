# Smooth Borders - Debugging Session
**Date**: 2025-10-26
**Session**: 3
**Status**: 🔴 Blocked - Compute shader UAV writes not working
**Priority**: Critical

---

## Session Goal

**Primary Objective:**
- Debug why smooth border curves are not appearing in Border Debug map mode
- Fix compute shader rasterization to actually write to BorderTexture

**Context from Previous Sessions:**
- Session 1: Designed smooth border solution (investigation)
- Session 2: Implemented border extraction, Chaikin smoothing, GPU upload
- Session 3 (this): Debugging why nothing renders

**Success Criteria:**
- Border Debug mode shows rasterized curves (not pure white)
- Can verify UAV writes are working from compute shader dispatch

---

## Problems Encountered & Solutions

### Problem 1: Border Debug Map Mode Shows Pure White
**Symptom:** Border Debug mode (`MapMode = 100`) shows entirely white screen, should show BorderTexture contents

**Investigation:**
1. Checked shader binding - `_BorderTexture` declared and bound correctly
2. Checked texture format - `R16G16_UNorm` with `enableRandomWrite = true` ✓
3. Verified curves extracted - 11,432 borders with 2.3M points uploaded ✓
4. Verified dispatch - 178 thread groups for 11,381 segments ✓
5. Verified clear - `GL.Clear(true, true, Color.white)` sets texture to (1,1,1,1) ✓

**Root Cause:** Compute shader UAV writes not making it to BorderTexture

**Evidence:**
- Manual fill test (thread 0 fills entire texture) → Works, screen turns black
- Grid pattern test (every thread writes one pixel) → Fails, screen stays white
- Conclusion: UAV writes work from thread 0, but NOT from actual dispatched threads

**Current Status:** BLOCKED - Cannot get compute shader to write from dispatched threads

### Problem 2: Province Selector Y-Flip
**Symptom:** Mouse clicks select wrong province (flipped in Y)

**Root Cause:** Removed Y-flip from `GetProvinceID()` but UVs from `hit.textureCoord` are OpenGL convention

**Solution:** Restored Y-flip in MapTextureManager.cs:84
```csharp
int flippedY = mapHeight - 1 - y;
temp.ReadPixels(new Rect(x, flippedY, 1, 1), 0, 0);
```

**Status:** ✅ FIXED

### Problem 3: Chaikin Smoothing Creates Degenerate Curves
**Symptom:** Short borders (14 pixels) smoothed to 112 points all at same location `(3064, 328)`

**Root Cause:**
- Raw pixels: `(3064,328) (3065,329) (3063,330)...` ✓
- Chained: Still diverse ✓
- Smoothed: `(3064.00, 328.00) (3064.02, 328.02) (3064.05, 328.05)...`
- After rounding to int: ALL become `(3064, 328)`
- Chaikin on short borders creates massive point count with sub-pixel spacing
- DrawLine skips all segments because `length < 0.01`

**Solution:** Skip smoothing for short borders (< 20 pixels)
```csharp
if (orderedPath.Count >= 20)
{
    smoothedCurve = SmoothCurve(orderedPath, smoothingIterations);
}
else
{
    smoothedCurve = orderedPath; // Use raw pixels
}
```

**Status:** ✅ FIXED (but doesn't matter since UAV writes broken)

---

## What Worked ✅

1. **Border curve extraction** - 11,432 borders extracted in 3.3s (0.29ms per border)
2. **GPU buffer upload** - 2.3M points, 11,381 segments uploaded successfully
3. **Compute shader dispatch** - 178 thread groups dispatched correctly
4. **Thread 0 UAV writes** - Filling entire texture from thread 0 works perfectly
5. **Province selector Y-flip fix** - Mouse clicks now select correct provinces

---

## What Didn't Work ❌

1. **Dispatched thread UAV writes** - Cannot write to BorderTexture from `id.x > 0` threads
2. **Border Debug visualization** - Shows pure white instead of texture contents
3. **Smooth curve rendering** - Curves extracted but never visible

---

## Key Technical Details

### Compute Shader Configuration
```hlsl
RWTexture2D<float2> BorderTexture;  // R16G16_UNorm format
[numthreads(64, 1, 1)]
void RasterizeCurves(uint3 id : SV_DispatchThreadID)
```

### Texture Configuration
```csharp
// DynamicTextureSet.cs:55-57
var descriptor = new RenderTextureDescriptor(mapWidth, mapHeight,
    UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16_UNorm, 0);
descriptor.enableRandomWrite = true;  // UAV support
```

### Dispatch Configuration
```csharp
// 11,381 segments / 64 threads per group = 178 thread groups
rasterizerShader.Dispatch(rasterizeKernel, threadGroups, 1, 1);
```

### Test Results
| Test | Expected | Actual | Status |
|------|----------|--------|--------|
| Thread 0 fills texture | Black screen | Black screen | ✅ PASS |
| Thread 0 draws cross | Red cross | Red cross | ✅ PASS |
| All threads write grid | Grid pattern | Pure white | ❌ FAIL |
| Curve rasterization | Border lines | Pure white | ❌ FAIL |

---

## Architecture Impact

**Current Architecture:**
1. BorderCurveExtractor extracts smooth curves (CPU)
2. BorderCurveRenderer uploads to GPU buffers
3. BorderCurveRasterizer.compute writes to BorderTexture
4. MapModeCommon.hlsl samples BorderTexture for rendering

**Broken Step:** Step 3 - compute shader dispatch not writing

---

## Code Changes This Session

### Files Modified
1. `MapTextureManager.cs` - Restored Y-flip for province selector
2. `BorderCurveExtractor.cs` - Skip smoothing for short borders
3. `BorderCurveRenderer.cs` - Added extensive debug logging
4. `BorderCurveRasterizer.compute` - Multiple test patterns to isolate issue
5. `MapCore.shader` - Updated Border Debug visualization

### Files Read/Analyzed
- `BorderComputeDispatcher.cs`
- `BorderCurveCache.cs`
- `DynamicTextureSet.cs`
- `MapModeCommon.hlsl`
- `ProvinceSelector.cs`

---

## Debug Logging Added

```csharp
// BorderCurveExtractor.cs - Log curve processing
"Raw sample: (3064,328) (3065,329) (3063,330)..."
"Chained sample: (3064,328) (3065,329) (3064,330)..."
"Smoothed sample: (3064.00,328.00) (3064.02,328.02)..."

// BorderCurveRenderer.cs - Log buffer upload
"Uploaded buffers - Points: 2307118, Segments: 11381"
"First segment: start=0, count=14, type=1"
"Dispatching 178 thread groups for 11381 segments"

// BorderCurveRenderer.cs - Log curve types
"Curve types - Hidden: 0, Province: 11381, Country: 0"
```

---

## Next Session

### Immediate Next Steps
1. **Investigate compute shader thread execution**
   - Why do writes from thread 0 work but not from dispatched threads?
   - Check if threads > 0 are even executing
   - Verify `id.x` values are correct

2. **Alternative approaches if UAV writes broken**
   - Use Graphics.Blit with custom shader instead of compute shader?
   - Rasterize on CPU and upload as Texture2D?
   - Use line rendering (LineRenderer/GL.LINES) instead of texture?

3. **Check Unity/GPU compatibility**
   - Test on different graphics API (DX11 vs DX12)?
   - Check Unity console for hidden shader compilation errors?
   - Verify graphics card supports UAV writes from non-thread-0?

### Questions to Resolve
1. Why do UAV writes work from thread 0 but not from id.x > 0?
2. Is there a Unity bug with RWTexture2D writes from compute shaders?
3. Should we abandon compute shader approach and use CPU rasterization?
4. Is the RenderTexture format actually writable from compute shaders?

---

## Confusion This Session

**Major Confusion:**
- I kept thinking the jagged borders in Political mode were the problem
- User repeatedly clarified: "We're fixing Border Debug mode, not Political mode"
- Political mode works fine with the old system
- The NEW smooth curve system is what's broken (can't see it in Border Debug)

**Lesson:** Read the user's actual request, don't assume context!

---

## Current Status

**Working:**
- ✅ Border curve extraction (11,432 curves)
- ✅ Chaikin smoothing (with short-border fix)
- ✅ GPU buffer upload (2.3M points)
- ✅ Compute shader dispatch (178 thread groups)
- ✅ Thread 0 UAV writes
- ✅ Province selector

**Broken:**
- ❌ Dispatched thread UAV writes (critical blocker)
- ❌ Border Debug visualization
- ❌ Smooth curve rendering

**Blocker:** Cannot proceed with smooth borders until compute shader UAV writes work from all threads, not just thread 0.

---

## Performance Notes

- Border extraction: 3.3 seconds (0.29ms per border)
- Buffer upload: < 50ms
- Curve smoothing: Included in extraction time
- GPU dispatch: < 1ms (but doesn't work)

---

*Session Log - Created 2025-10-26*
*BLOCKED: UAV writes from compute shader threads failing*
