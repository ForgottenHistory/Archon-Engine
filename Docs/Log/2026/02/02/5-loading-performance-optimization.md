# Loading Performance Optimization — Session 5 (TerrainTypeGenerator GPU Compute Shader)
**Date**: 2026-02-02
**Session**: 5
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Replace the last CPU pixel loop (TerrainTypeTextureGenerator) with a GPU compute shader

**Success Criteria:**
- TerrainTypeTextureGenerator uses GPU compute instead of CPU GetPixels32 + dictionary lookup
- No regressions — consumers (TreeInstanceGenerator, ProvinceTerrainAnalyzer, shaders) work unchanged
- Measurable speedup from ~860ms baseline

**Result:** CPU pixel loop eliminated. **860ms → 114ms** (87% reduction). Total load ~4.2s.

---

## Context & Background

**Previous Work:**
- See: [Session 4](4-loading-performance-optimization.md) — Adjacency cache, terrain analyzer GPU-direct
- Session 4 identified TerrainTypeTextureGenerator (~860ms) as the next optimization target

**Current State (start of session):**
- Total load ~5.0s after session 4
- TerrainTypeTextureGenerator was the last remaining CPU pixel loop:
  - `GetPixels32()` on 97.5M pixel RGBA32 texture (390MB `Color32[]` allocation)
  - Per-pixel dictionary lookup mapping RGB → terrain type index
  - `SetPixelData()` to upload byte array to R8 texture
- All other pixel operations already on GPU

**Why Now:**
- Identified as remaining CPU pixel processing anti-pattern (violates architecture rule: never process millions of pixels on CPU)
- Session 4 explicitly flagged this as next target

---

## What We Did

### 1. Created TerrainTypeGenerator Compute Shader
**Files Created:** `Resources/Shaders/TerrainTypeGenerator.compute`

**Architecture:** Input RGBA32 texture + color lookup buffers → Output R8_UNorm texture

Compute shader approach:
- `Texture2D<float4> TerrainColorTexture` — reads RGBA32 terrain bitmap (already on GPU)
- `StructuredBuffer<uint> ColorKeyBuffer` — packed RGB keys (r<<16|g<<8|b) for each terrain type
- `StructuredBuffer<uint> TerrainIndexBuffer` — corresponding terrain type index for each key
- `RWTexture2D<float> OutputTexture` — writes R8_UNorm terrain indices
- Linear search through ~15 entries per pixel (trivial on GPU with parallel threads)
- Standard `[numthreads(8, 8, 1)]` matching all other project compute shaders

### 2. Rewrote TerrainTypeTextureGenerator C# Dispatcher
**Files Changed:** `Scripts/Map/Loading/TerrainTypeTextureGenerator.cs`

**Changes:**
- Loads shader via `ModLoader.LoadAssetWithFallback` (standard pattern)
- Builds parallel lookup arrays from `TerrainColorMapper.GetRegisteredIndices()`
- Creates temporary `RenderTexture` (R8_UNorm, enableRandomWrite) for compute output
- Dispatches compute shader with standard thread group calculation
- Copies result to `Texture2D` via `ReadPixels` (consumers expect `Texture2D`)
- Releases all GPU resources in `finally` block
- CPU fallback retained if compute shader unavailable

**Eliminated:**
- 390MB `Color32[]` allocation from `GetPixels32()`
- CPU loop over 97.5M pixels with per-pixel dictionary lookup
- 97.5MB `byte[]` allocation for output

**Kept unchanged:**
- Return type remains `Texture2D` — all consumers (TreeInstanceGenerator, ProvinceTerrainAnalyzer, DefaultCommon.hlsl shader) bind it as `Texture2D`
- Public API signature identical: `GenerateTerrainTypeTexture(Texture2D, bool)`

---

## Decisions Made

### Decision 1: Keep Texture2D Return Type (RenderTexture → ReadPixels → Texture2D)
**Context:** Compute shaders write to `RWTexture2D` (requires RenderTexture), but all consumers expect `Texture2D`.
**Options:**
1. **ReadPixels copy** — dispatch to temp RenderTexture, copy to Texture2D, release RT
2. ~~Change all consumers to RenderTexture~~ — touches TreeInstanceGenerator, ProvinceTerrainAnalyzer, shader bindings
3. ~~Graphics.CopyTexture~~ — format compatibility caveats with R8_UNorm between RT and Texture2D

**Decision:** Option 1 — ReadPixels copy
**Rationale:** This runs once at load time. The 114ms total (including readback) is acceptable. Changing all consumers would be a large refactor for marginal gain on a one-time operation.
**Trade-off:** ~100ms of the 114ms is the synchronous ReadPixels readback. Could be eliminated by changing consumers to accept RenderTexture, but not worth the churn.

### Decision 2: Linear Search in Shader (vs Lookup Texture)
**Context:** Need to map ~15 RGB colors to terrain indices on GPU
**Options:**
1. **Linear search through StructuredBuffer** — simple, ~15 iterations per pixel
2. ~~3D lookup texture (256x256x256)~~ — overkill for 15 entries, 16MB texture
3. ~~Hash map in shader~~ — complex, unnecessary for small N

**Decision:** Option 1 — linear search
**Rationale:** 15 iterations is trivial per GPU thread. Thousands of threads run in parallel. Simpler to implement and debug.

---

## Performance Results

### Measured (97.5M pixels, 15000x6500, 15 terrain types)

| Metric | Before (CPU) | After (GPU) | Delta |
|--------|-------------|-------------|-------|
| TerrainTypeGenerator | ~860ms | 114ms | **-87%** |
| Memory allocation | ~490MB (Color32[] + byte[]) | ~0 (GPU buffers only) | **-99%** |

### Updated Load Time Budget (~4.2s total)

| Phase | Session 4 | Session 5 | Delta |
|-------|-----------|-----------|-------|
| Engine start → textures | 1.5s | 1.1s | ±variance |
| Texture creation | 1.5s | 1.5s | — |
| Province data (cache) | 178ms | ~180ms | — |
| **Terrain load + TerrainTypeGen** | **1.1s** | **~240ms** | **-78%** |
| Heightmap load (cache) | 26ms | ~26ms | — |
| Texture populate (GPU) | 534ms | ~540ms | — |
| Terrain analysis | 35ms | ~35ms | — |
| Blend maps + borders + rest | ~500ms | ~500ms | — |
| StarterKit game layer | ~108ms | ~108ms | — |
| **Total** | **~5.0s** | **~4.2s** | **-16%** |

### Cumulative: ~33s → ~4.2s (87% reduction across 5 sessions)

---

## Architecture Impact

**Anti-pattern eliminated:**
- Last CPU pixel processing loop removed from map loading pipeline
- All pixel-level operations now run on GPU compute shaders

**Zero remaining CPU pixel loops in map loading.**

---

## Quick Reference for Future Claude

**Key implementations:**
- Compute shader: `Resources/Shaders/TerrainTypeGenerator.compute` — `GenerateTerrainTypes` kernel
- C# dispatcher: `Scripts/Map/Loading/TerrainTypeTextureGenerator.cs` — GPU path with CPU fallback
- Lookup data source: `Scripts/Map/Loading/Data/TerrainColorMapper.cs` — `GetRegisteredIndices()`, `GetTerrainColor()`

**Remaining load time budget (~4.2s):**
- Texture creation/allocation: ~1.5s (GPU memory allocation for 15000x6500 textures)
- Engine startup: ~1.1s
- GPU texture population: ~0.5s (province ID pack + owner dispatch)
- Terrain image load + TerrainTypeGen: ~240ms
- Everything else: ~0.9s (blend maps, borders, interaction, game layer)
- Further optimization would target texture allocation (~1.5s) or engine startup (~1.1s)

**ReadPixels bottleneck:**
- 114ms total, ~100ms is synchronous ReadPixels readback from RenderTexture → Texture2D
- Could eliminate by changing consumers (TreeInstanceGenerator, ProvinceTerrainAnalyzer, shader bindings) to accept RenderTexture
- Not worth the refactor for a one-time load operation

**Files changed this session:**
- `Resources/Shaders/TerrainTypeGenerator.compute` — new compute shader
- `Scripts/Map/Loading/TerrainTypeTextureGenerator.cs` — rewritten to use GPU compute

---

## Related Sessions
- [Session 4](4-loading-performance-optimization.md) — Adjacency cache, terrain analyzer GPU-direct
- [Session 3](3-loading-performance-optimization.md) — Terrain/heightmap pixel cache, texture init fix
- [Session 2](2-loading-performance-optimization.md) — Province pixel cache, single-pass texture population
- [Session 1](1-loading-performance-optimization.md) — GPU compute shader, logging removal, pre-sized collections
