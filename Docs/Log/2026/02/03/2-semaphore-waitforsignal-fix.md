# Semaphore.WaitForSignal GPU Sync Fix
**Date**: 2026-02-03
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Eliminate 150ms+ `Semaphore.WaitForSignal` spikes occurring on monthly tick and mid-month during gameplay

**Success Criteria:**
- No frame hitches when AI colonizes provinces
- Smooth gameplay at 100x speed with all AI active

---

## Context & Background

**Previous Work:**
- See: [Session 1 — Runtime Performance](1-runtime-performance-optimization.md) — reverse index, modifier fix, GPU readback removal
- Session 1 resolved 400ms+ monthly tick spikes but left a 150ms `Semaphore.WaitForSignal` stall

**Current State:**
- ~50k provinces, ~10 AI countries colonizing on monthly tick
- `Semaphore.WaitForSignal` under `PlayerLoop > UpdateScene` — not expandable in profiler
- Spikes happen on monthly tick + intermittently mid-month (0.1s batch delay)

---

## What We Did

### 1. CPU-Side Province ID Lookup (ReadPixels Elimination)
**Files Changed:** `Scripts/Map/MapTextureManager.cs`, `Scripts/Map/Rendering/MapTexturePopulator.cs`

Replaced `Texture2D.ReadPixels()` GPU readback in `GetProvinceID()` with a CPU-side `ushort[]` lookup array built at load time from the province map image.

- `MapTexturePopulator.BuildProvinceIDLookup()` builds the array during loading
- `MapTextureManager.SetProvinceIDLookup()` / `GetProvinceID()` uses direct array access
- Y-axis flip required: callers pass UV-space y (0=bottom), array is image-space (0=top)

### 2. Per-Province Pixel Index for Targeted GPU Dispatch
**Files Changed:** `Scripts/Map/Rendering/MapTexturePopulator.cs`, `Scripts/Map/Rendering/OwnerTextureDispatcher.cs`

Built a load-time data structure mapping province ID → list of pixel coordinates. Enables compute shader dispatch over only affected pixels instead of full 97.5M pixel map.

- Two-pass build in `BuildProvinceIDLookup()`: count pixels per province, prefix-sum offsets, fill coordinates
- Pixel coordinates packed as `x | (y << 16)` in `uint[]`
- Uploaded to persistent GPU buffers once at load time

### 3. Indexed Compute Shaders
**Files Created:**
- `Resources/Shaders/UpdateOwnerByIndex.compute` — 1D dispatch over affected pixels only, binary search for province entry
- `Resources/Shaders/UpdateBorderByIndex.compute` — same pattern for border recalculation + 4 cardinal neighbors

**Files Changed:** `Scripts/Map/Rendering/OwnerTextureDispatcher.cs`, `Scripts/Map/Rendering/Border/BorderComputeDispatcher.cs`

Both dispatchers rewritten to use indexed compute shaders at runtime, falling back to full-map dispatch at load time.

### 4. CommandBuffer Async Dispatch (Eliminated Direct Dispatch Blocking)
**Files Changed:** `Scripts/Map/Rendering/TextureUpdateBridge.cs`, `Scripts/Map/Rendering/OwnerTextureDispatcher.cs`, `Scripts/Map/Rendering/Border/BorderComputeDispatcher.cs`

Changed from direct `ComputeShader.Dispatch()` to `CommandBuffer` with `Graphics.ExecuteCommandBufferAsync()`:
- `UpdateOwnerTexture()` and `DetectBordersIndexed()` now accept a `CommandBuffer` parameter
- `TextureUpdateBridge.ProcessPendingUpdates()` creates a single `CommandBuffer`, passes to both dispatchers, executes async
- Requires `cmd.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute)` before `ExecuteCommandBufferAsync`

### 5. Removed Unnecessary Texture2D.Apply() — THE ROOT CAUSE FIX
**Files Changed:** `Scripts/Map/Rendering/TextureUpdateBridge.cs:114-118`

**This was the actual fix.** `ProcessPendingUpdates()` called `textureManager.ApplyTextureChanges()` which called `Texture2D.Apply(false)` on three unchanged textures (`provinceColorTexture`, `provinceColorPalette`, `provinceTerrainTexture`). `Apply()` uploads the full texture to GPU and forces a pipeline sync — even when nothing changed.

The owner texture is a `RenderTexture` updated by compute shader — no `Apply()` needed. No CPU-side textures change during runtime province updates. Removed the call entirely.

---

## What Didn't Work

### 1. Double-Buffered ComputeBuffers
- **What we tried:** Alternating between two sets of per-dispatch ComputeBuffers to avoid `SetData` writing to buffers the GPU is still reading
- **Why it failed:** The stall wasn't from `SetData`/`Dispatch` — it was from `Texture2D.Apply()` on unchanged textures
- **Lesson:** Profile the actual blocking call, don't assume

### 2. Deferred Border Update to Next Frame
- **What we tried:** Moving `DetectBorders()` to a `LateUpdate` pending flag
- **Why it failed:** Same root cause — the Semaphore wasn't from the compute dispatch itself

### 3. CommandBuffer Alone (Without Removing Apply)
- **What we tried:** Switching all compute dispatches to `CommandBuffer` + `ExecuteCommandBufferAsync`
- **Result:** Still 128ms — the `Texture2D.Apply()` calls happened before the CommandBuffer work
- **Lesson:** CommandBuffer only helps with dispatch submission, not with other blocking GPU operations

---

## Problems Encountered & Solutions

### Problem 1: Province Selection Broken After CPU Lookup
**Symptom:** Clicking provinces returned wrong/no province
**Root Cause:** Missing Y-axis flip. Callers pass UV-space y (0=bottom), lookup is image-space (0=top).
**Solution:** `int flippedY = mapHeight - 1 - y` in `GetProvinceID()`

### Problem 2: CS1503 — SetInt Takes int Not uint
**Symptom:** Build error on `SetInt("NumChangedProvinces", (uint)numChanged)`
**Solution:** Remove `(uint)` cast — Unity's `ComputeShader.SetInt` takes `(string, int)`

### Problem 3: CommandBuffer Missing AsyncCompute Flag
**Symptom:** Unity error "CommandBuffer is being executed on async compute queue but does not have AsyncCompute flag"
**Solution:** `cmd.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute)` before `ExecuteCommandBufferAsync`

### Problem 4: AI Constructor Silently Failing
**Symptom:** Logs show "Creating AI system..." but never "AISystem: Initialized". No colonization.
**Root Cause:** Unity coroutines swallow exceptions silently. Constructor threw but coroutine continued.
**Resolution:** Turned out to be Unity not recompiling properly — fresh restart fixed it.

### Problem 5: The Actual Root Cause — Texture2D.Apply() on Unchanged Textures
**Symptom:** 128-153ms `Semaphore.WaitForSignal` persisted through all compute shader optimizations
**Root Cause:** `TextureUpdateBridge.ProcessPendingUpdates()` called `textureManager.ApplyTextureChanges()` which uploaded 3 unchanged CPU textures to GPU every time a province changed ownership. `Texture2D.Apply()` forces full GPU pipeline sync.
**Solution:** Removed the `ApplyTextureChanges()` call from the runtime update path. Owner texture is a RenderTexture updated by compute shader — no CPU texture upload needed.

---

## Architecture Impact

### New Anti-Pattern: Texture2D.Apply() on Unchanged Textures
- **What not to do:** Call `Texture2D.Apply()` on textures that haven't been modified on CPU
- **Why it's bad:** Forces full texture upload + GPU pipeline sync even for no-op
- **Rule:** Only call `Apply()` when CPU-side pixel data has actually changed. RenderTextures updated by compute shaders never need `Apply()`.

### New Pattern: CommandBuffer for Runtime Compute Dispatches
- **When to use:** Any compute shader dispatch triggered by gameplay events (not loading)
- **Benefits:** Non-blocking GPU work submission, batching multiple dispatches
- **Key:** Set `CommandBufferExecutionFlags.AsyncCompute` + use `Graphics.ExecuteCommandBufferAsync()`

### New Pattern: Per-Province Pixel Index
- **What:** Load-time mapping of province ID → pixel coordinate list
- **When to use:** Targeted GPU updates for province-specific visual changes
- **Benefits:** O(affected pixels) instead of O(all map pixels) for owner/border updates

---

## Quick Reference for Future Claude

**Key implementations:**
- CPU province lookup: `Scripts/Map/MapTextureManager.cs` — `provinceIDLookup`, `GetProvinceID()`
- Pixel index build: `Scripts/Map/Rendering/MapTexturePopulator.cs` — `BuildProvinceIDLookup()`
- Indexed owner update: `Scripts/Map/Rendering/OwnerTextureDispatcher.cs` — `UpdateOwnerTexture(queries, changed, cmd)`
- Indexed border update: `Scripts/Map/Rendering/Border/BorderComputeDispatcher.cs` — `DetectBordersIndexed(changed, cmd)`
- CommandBuffer orchestration: `Scripts/Map/Rendering/TextureUpdateBridge.cs` — `ProcessPendingUpdates()`
- Compute shaders: `Resources/Shaders/UpdateOwnerByIndex.compute`, `Resources/Shaders/UpdateBorderByIndex.compute`

**Gotchas:**
- `Semaphore.WaitForSignal` is generic — means "main thread waiting". Look at parent profiler marker for context.
- `Texture2D.Apply()` is a hidden GPU sync point — never call on unchanged textures
- `ComputeBuffer.SetData()` can also sync if GPU is reading the buffer — use double-buffering
- CommandBuffer needs `AsyncCompute` flag for `ExecuteCommandBufferAsync`
- Y-flip needed for CPU province lookup (UV-space vs image-space)

**Files changed this session:**
- `Scripts/Map/MapTextureManager.cs` — CPU province ID lookup
- `Scripts/Map/Rendering/MapTexturePopulator.cs` — pixel index build
- `Scripts/Map/Rendering/OwnerTextureDispatcher.cs` — indexed dispatch + CommandBuffer
- `Scripts/Map/Rendering/Border/BorderComputeDispatcher.cs` — indexed dispatch + CommandBuffer
- `Scripts/Map/Rendering/TextureUpdateBridge.cs` — CommandBuffer orchestration, removed Apply()
- `Resources/Shaders/UpdateOwnerByIndex.compute` — new compute shader
- `Resources/Shaders/UpdateBorderByIndex.compute` — new compute shader
- `Scripts/StarterKit/Initializer.cs` — temp disable/re-enable farm map mode events

---

## Related Sessions
- [Session 1 — Runtime Performance](1-runtime-performance-optimization.md) — preceding optimization work
