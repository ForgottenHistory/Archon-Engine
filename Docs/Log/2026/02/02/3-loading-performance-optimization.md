# Loading Performance Optimization — Session 3 (Terrain/Heightmap Cache + Texture Init)
**Date**: 2026-02-02
**Session**: 3
**Status**: 🔄 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Eliminate remaining PNG decompression bottlenecks for terrain.png (~5.3s) and heightmap.png (~6.0s)
- Fix wasteful texture initialization in VisualTextureSet (1.56GB + 390MB managed allocations)

**Success Criteria:**
- Terrain + heightmap loading < 500ms on cache hit
- Total map load time under 10s

---

## Context & Background

**Previous Work:**
- See: [Session 2](2-loading-performance-optimization.md) — Province pixel cache, single-pass texture population
- See: [Session 1](1-loading-performance-optimization.md) — GPU compute shader, logging removal, pre-sized collections

**Current State (start of session):**
- Baseline ~33s reduced to ~18.3s after sessions 1-2
- Two unaccounted gaps identified via timeline analysis:
  - Gap 1 (41.4→46.7 = 5.3s): TerrainImageLoader — PNG decompress + CPU pixel loop + SetPixels32 + GL.Flush
  - Gap 2 (49.0→55.0 = 6.0s): HeightmapImageLoader — PNG decompress + CPU pixel loop + SetPixels (Color[], 16 bytes/pixel!) + GL.Flush
- VisualTextureSet.CreateHeightmapTexture allocated `Color[97.5M]` = 1.56GB just to initialize with mid-height
- VisualTextureSet.CreateProvinceTerrainTexture allocated `Color32[97.5M]` = 390MB to initialize with default color

---

## What We Did

### 1. HeightmapImageLoader — Complete Rewrite
**Files Changed:** `Scripts/Map/Loading/Images/HeightmapBitmapLoader.cs`

**Before:**
- PNG decompression every load (~3s DEFLATE + ~2s unfiltering)
- CPU loop creating `new Color[97.5M]` = **1.56GB managed allocation** (16 bytes/pixel for R8 texture!)
- `SetPixels()` + `Apply()` + `GL.Flush()` — synchronous GPU upload with forced sync
- `RebindTextures()` with `FindFirstObjectByType` called redundantly

**After:**
- Raw pixel cache (`.pixels` file) — same RPXL format as ProvinceMapProcessor
- `GetRawTextureData<byte>()` — writes 1 byte/pixel directly into R8 texture buffer
- Zero managed allocation for texture population
- Removed `GL.Flush()` and `RebindTextures()`

**Measured Impact:** **6.0s → 41ms** (cache read: 27ms, populate: 14ms)

### 2. TerrainImageLoader — Complete Rewrite
**Files Changed:** `Scripts/Map/Loading/Images/TerrainBitmapLoader.cs`

**Before:**
- PNG decompression every load
- CPU loop with `TryGetPixelRGB()` + `HashSet<int>.Add()` per pixel + `new Color32[97.5M]` = 390MB
- `SetPixels32()` + `Apply()` + `GL.Flush()`
- `RebindTextures()` with `FindFirstObjectByType`

**After:**
- Raw pixel cache (`.pixels` file) — same RPXL format
- `GetRawTextureData<byte>()` — writes RGBA32 directly via unsafe pointer
- Zero managed allocation, no HashSet tracking
- Removed `GL.Flush()` and `RebindTextures()`
- Added timing breakdown (populate vs terrain type generation)

**Measured Impact:** **5.3s → 985ms** (cache read: 101ms, populate: 259ms, terrainTypeGen: 726ms)

### 3. VisualTextureSet — Texture Initialization Fix
**Files Changed:** `Scripts/Map/Rendering/VisualTextureSet.cs`

**Heightmap init (R8):**
- Before: `new Color[97.5M]` (1.56GB!) + loop + `SetPixels` + `Apply`
- After: `GetRawTextureData<byte>()` + `UnsafeUtility.MemSet(128)` + `Apply`

**Terrain init (RGBA32):**
- Before: `new Color32[97.5M]` (390MB) + loop + `SetPixels32` + `Apply`
- After: `GetRawTextureData<byte>()` + unsafe pointer loop + `Apply`

**Measured Impact:** Texture allocation phase 1.9s → 1.5s (0.4s saved from eliminated GC pressure)

### 4. Timeline Analysis & Bottleneck Investigation
Identified remaining 5.9s bottleneck is **GPU province adjacency scanning** in `ArchonEngine.ScanProvinceAdjacencies()` → `GPUProvinceNeighborDetector.DetectNeighborsGPU()`:
- Synchronous `GetData()` GPU→CPU readback of ~400k neighbor pairs
- CPU `Dictionary<int, HashSet<int>>` building with no pre-sizing
- Not a map loading issue — it's adjacency detection

---

## Decisions Made

### Decision 1: GetRawTextureData<byte>() for R8 Heightmap
**Context:** HeightmapTexture is R8 format (1 byte/pixel). Old code used `Color[]` (16 bytes/pixel).
**Decision:** Write single bytes directly into R8 buffer via `GetRawTextureData<byte>()`
**Benefit:** Eliminates 1.56GB managed allocation, 16x less memory, zero GC pressure

### Decision 2: Remove GL.Flush() from Bitmap Loaders
**Context:** Both terrain and heightmap loaders called `GL.Flush()` after `Apply()`, forcing CPU to wait for GPU upload completion.
**Decision:** Removed — unnecessary between sequential CPU operations. GPU will process uploads when needed.
**Benefit:** Removes synchronous GPU stall between sequential loads.

### Decision 3: Remove RebindTextures() from Loaders
**Context:** Both loaders called `RebindTextures()` which did `FindFirstObjectByType` twice. MapSystemCoordinator rebinds textures after all loading completes.
**Decision:** Removed from loaders — coordinator handles final rebinding.

---

## Problems Encountered & Solutions

### Problem 1: Identifying the Two Hidden Gaps
**Symptom:** 11.3s unaccounted time in map_initialization.log between known operations
**Root Cause:** TerrainImageLoader and HeightmapImageLoader had no timing logs (gated by `logProgress` which was false)
**Solution:** Timeline correlation across all log files (map_initialization, map_rendering, core_simulation) to identify what code ran during each gap

### Problem 2: Heightmap Uses Color[] Instead of Color32[]
**Symptom:** HeightmapImageLoader gap was ~6s despite heightmap being R8 (should be much smaller than RGBA32 terrain)
**Root Cause:** Used `Color[]` (16 bytes/pixel = 1.56GB) instead of `Color32[]` (4 bytes) or direct byte writes (1 byte). `SetPixels()` takes `Color[]` while `SetPixels32()` takes `Color32[]` — the R8 texture needs neither.
**Solution:** `GetRawTextureData<byte>()` writes exactly 1 byte per pixel = 95MB vs 1.56GB

---

## Performance Results

### Final Measured Breakdown (cached run, 97.5M pixels, 50k provinces)

| Phase | Session 2 | Session 3 | Measured |
|-------|-----------|-----------|----------|
| Shader init | 1.2s | 1.1s | 37.594→38.737 |
| Texture allocation | 1.9s | 1.5s | 38.741→40.260 |
| Province data loading | 197ms | 196ms | Cache: 108ms, CSV: 88ms |
| **Terrain loading** | **5.3s** | **985ms** | Cache: 101ms, populate: 259ms, terrainTypeGen: 726ms |
| **Heightmap loading** | **6.0s** | **41ms** | Cache: 27ms, populate: 14ms |
| Texture population (GPU) | 644ms | 554ms | Pack+color: 179ms, hash: 5ms, GPU: 370ms |
| Owner texture | 8ms | 8ms | GPU compute |
| **GPU adjacency scan** | **~5.9s** | **~5.9s** | **Not optimized this session** |
| Interaction + borders + GAME | 0.2s | 0.2s | Fast |

### Total: ~18.3s → ~10.8s (7.5s saved this session)

### Cumulative: ~33s → ~10.8s (67% reduction across 3 sessions)

---

## Quick Reference for Future Claude

**Key implementations:**
- Heightmap cache + direct R8 write: `Scripts/Map/Loading/Images/HeightmapBitmapLoader.cs`
- Terrain cache + direct RGBA32 write: `Scripts/Map/Loading/Images/TerrainBitmapLoader.cs`
- Texture init fix: `Scripts/Map/Rendering/VisualTextureSet.cs` — `CreateHeightmapTexture()`, `CreateProvinceTerrainTexture()`
- Cache format: identical RPXL format as ProvinceMapProcessor (16-byte header + raw pixels)

**Gotchas:**
- R8 texture = 1 byte/pixel. Never use `Color[]` (16 bytes) or `Color32[]` (4 bytes) for R8 — use `GetRawTextureData<byte>()`
- `GL.Flush()` is almost never needed — it forces synchronous GPU completion and blocks CPU
- `FindFirstObjectByType` in hot paths is expensive — avoid in loaders called during init
- Heightmap.png is only 5632x2048 (much smaller than 15000x6500 provinces/terrain) — cache is only ~33MB vs ~292MB

**Remaining bottleneck:**
- **GPU adjacency scan: 5.9s** — `GPUProvinceNeighborDetector.DetectNeighborsGPU()` in `ArchonEngine.ScanProvinceAdjacencies()`
- Bottleneck is synchronous `GetData()` GPU→CPU readback + CPU Dictionary/HashSet building
- Best approach: cache adjacency results to disk (same invalidation pattern as pixel cache)
- File: `Scripts/Map/Province/GPUProvinceNeighborDetector.cs`
- Called from: `Scripts/Engine/ArchonEngine.cs:593`

**Files changed this session:**
- `Scripts/Map/Loading/Images/HeightmapBitmapLoader.cs` — complete rewrite with cache + GetRawTextureData
- `Scripts/Map/Loading/Images/TerrainBitmapLoader.cs` — complete rewrite with cache + GetRawTextureData
- `Scripts/Map/Rendering/VisualTextureSet.cs` — R8 MemSet init, RGBA32 unsafe pointer init

---

## Related Sessions
- [Session 1](1-loading-performance-optimization.md) — GPU compute shader, logging removal, pre-sized collections
- [Session 2](2-loading-performance-optimization.md) — Province pixel cache, single-pass texture population
