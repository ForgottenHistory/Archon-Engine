# Loading Performance Optimization — Session 2 (Raw Pixel Cache + Single-Pass Texture Population)
**Date**: 2026-02-02
**Session**: 2
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Eliminate PNG decompression bottleneck (~7.8s) via raw pixel cache
- Optimize CPU pixel loops in MapTexturePopulator (~5.2s)

**Success Criteria:**
- Province data loading < 1s on cache hit
- Texture population < 1s

---

## Context & Background

**Previous Work:**
- See: [1-loading-performance-optimization.md](1-loading-performance-optimization.md)

**Current State (start of session):**
- Baseline ~33s reduced to ~21s after session 1 optimizations
- Two remaining bottlenecks: PNG decompress (~7.8s) and CPU pixel loops (~5.2s)
- GPU compute shader for province ID texture verified working

---

## What We Did

### 1. Raw Pixel Cache for Province Map Loading
**Files Changed:**
- `Scripts/Map/Loading/ProvinceMapProcessor.cs` — added `TryLoadPixelCache()`, `SavePixelCache()`, `BuildResultFromPixelData()`
- `Scripts/Map/Loading/Images/ProvinceMapParser.cs` — extracted `ParseProvinceMapWithPixelData()` from `ParseProvinceMapUnified()`

**Architecture:**
- Cache file: `{image_path}.pixels` (e.g., `provinces.png.pixels`)
- Binary format: 16-byte header (magic "RPXL", width, height, bpp, colorType, bitDepth) + raw decoded pixel bytes
- First run: PNG decompress as normal, then save cache (~292MB for 15000x6500 RGB)
- Subsequent runs: `File.ReadAllBytes` + single `UnsafeUtility.MemCpy` into `NativeArray<byte>`
- Cache invalidation: `File.GetLastWriteTimeUtc` comparison — cache stale if source PNG is newer
- CSV parsing still runs every load (fast, ~87ms) — only image decompression is cached
- `ParseProvinceMapWithPixelData()` extracted to avoid duplicating CSV logic between cache-hit and cache-miss paths

**Measured Impact:** PNG load **~7.8s → 197ms** (119ms cache read + 78ms CSV)

### 2. Single-Pass Texture Population
**Files Changed:** `Scripts/Map/Rendering/MapTexturePopulator.cs`

**Before:** Two separate 97.5M pixel CPU loops:
1. `PackRGBPixels()` — raw bytes → `uint[]` for GPU compute shader
2. `PopulateColorTextureFromRawBytes()` — raw bytes → `Color32[]` → `SetPixels32` → `Apply`

**After:** Single loop that simultaneously:
1. Packs `uint[]` for GPU upload
2. Writes RGBA32 directly into texture buffer via `GetRawTextureData<byte>()` — zero managed allocation for color texture

**Measured Impact:** Texture population **~5.2s → 644ms** (pack+color: 188ms, hash: 7ms, GPU dispatch+sync: 449ms)

### 3. Timing Instrumentation
**Files Changed:**
- `Scripts/Map/Loading/ProvinceMapProcessor.cs` — added cache hit/miss timing logs
- `Scripts/Map/Rendering/MapTexturePopulator.cs` — unconditional timing log (not gated by `logProgress`)

---

## Decisions Made

### Decision 1: File.ReadAllBytes + Single MemCpy vs Streamed Read
**Context:** Initial implementation used FileStream with 1MB chunked reads to avoid 292MB managed allocation
**Result:** Chunked reads were slower due to 292 `fixed`+`MemCpy` calls. `File.ReadAllBytes` + single `MemCpy` with `NativeArrayOptions.UninitializedMemory` was significantly faster.
**Lesson:** For sequential reads, .NET's internal buffering in `File.ReadAllBytes` outperforms manual chunking.

### Decision 2: GetRawTextureData vs SetPixels32
**Context:** `SetPixels32` requires allocating a `Color32[]` managed array (390MB for RGBA32 at 97.5M pixels)
**Decision:** Use `GetRawTextureData<byte>()` to get a NativeArray view of the texture's internal buffer, write RGBA bytes directly via unsafe pointer.
**Benefit:** Eliminates 390MB managed allocation, halves memory pressure, single pass over source data.

---

## Problems Encountered & Solutions

### Problem 1: Cache Read Slower Than Expected (~5.3s)
**Symptom:** First cache implementation saved ~2.4s instead of expected ~7s
**Root Cause:** FileStream with 1MB chunked reads + 292 `fixed`/`MemCpy` calls per chunk was slow for 292MB
**Solution:** Replaced with `File.ReadAllBytes` (one sequential read) + single `UnsafeUtility.MemCpy` + `NativeArrayOptions.UninitializedMemory`
**Result:** Cache read dropped to 119ms

### Problem 2: Missing MapTexturePopulator Logs
**Symptom:** No MapTexturePopulator timing logs in any log file
**Root Cause:** `logProgress` parameter was `false` because `gameSettings.ShouldLog(LogLevel.Info)` returns false when log level is Warnings
**Solution:** Made GPU path timing log unconditional (not gated by `logProgress`)

---

## Performance Results

### Final Measured Breakdown (cached run, 97.5M pixels, 50k provinces)

| Phase | Before (baseline) | After | Measured |
|-------|-------------------|-------|----------|
| Province registration | 2.7s | 9ms | Session 1 |
| Normal map gen | 0.9s | 0s | Session 1 |
| Province data loading | 7.8s | 197ms | Cache: 119ms, CSV: 78ms |
| Texture population | ~9.4s | 644ms | Pack+color: 188ms, hash: 7ms, GPU: 449ms |
| Pre-sized collections | — | — | ~0.1s |

### Remaining Time (not optimized this session)
- Texture creation/allocation: ~3s (VRAM allocation for 15000x6500 textures)
- Terrain texture generation: ~0.7s (compute shader, already fast)
- Heightmap loading: ~0.4s
- Texture binding: ~0.8s per rebind cycle
- MapMode/border init: ~5.7s

---

## Quick Reference for Future Claude

**Key implementation:**
- Pixel cache: `Scripts/Map/Loading/ProvinceMapProcessor.cs` — `TryLoadPixelCache()`, `SavePixelCache()`
- Cache format: 16-byte header ("RPXL" + dims + bpp) + raw pixel bytes
- Separated parser: `Scripts/Map/Loading/Images/ProvinceMapParser.cs:ParseProvinceMapWithPixelData()`
- Single-pass populator: `Scripts/Map/Rendering/MapTexturePopulator.cs:TryPopulateGPU()`
- Direct texture write: `GetRawTextureData<byte>()` for zero-alloc RGBA32 population

**Gotchas:**
- `File.ReadAllBytes` + single `MemCpy` beats streamed chunked reads for large sequential files
- `NativeArrayOptions.UninitializedMemory` skips zeroing — critical for 292MB allocations
- `GetRawTextureData<byte>()` returns RGBA32 layout (R,G,B,A per pixel, 4 bytes) for RGBA32 textures
- Cache invalidation uses file timestamps — modifying the PNG auto-invalidates
- GPU dispatch+sync takes ~449ms — this is `ComputeBuffer.SetData` (390MB upload) + dispatch + `AsyncGPUReadback.WaitForCompletion`

**Files changed this session:**
- `Scripts/Map/Loading/ProvinceMapProcessor.cs` — cache read/write, timing logs
- `Scripts/Map/Loading/Images/ProvinceMapParser.cs` — extracted `ParseProvinceMapWithPixelData`
- `Scripts/Map/Rendering/MapTexturePopulator.cs` — single-pass pack+color, unconditional timing log

---

## Related Sessions
- [Session 1](1-loading-performance-optimization.md) — GPU compute shader, logging removal, pre-sized collections
