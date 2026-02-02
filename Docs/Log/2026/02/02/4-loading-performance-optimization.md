# Loading Performance Optimization — Session 4 (Adjacency Cache + Terrain Analyzer GPU-Direct)
**Date**: 2026-02-02
**Session**: 4
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Eliminate GPU adjacency scan bottleneck (5.9s)
- Eliminate ProvinceTerrainAnalyzer CPU conversion bottleneck (5.8s)
- Push total map load time below 10s

**Success Criteria:**
- Adjacency loading < 50ms on cache hit
- Terrain analysis < 200ms (GPU-direct, no CPU conversion)
- Total load time under 10s

**Result:** All criteria met. Total: **~33s → 5.0s** (85% reduction across 4 sessions)

---

## Context & Background

**Previous Work:**
- See: [Session 3](3-loading-performance-optimization.md) — Terrain/heightmap pixel cache, texture init fix
- See: [Session 2](2-loading-performance-optimization.md) — Province pixel cache, single-pass texture population
- See: [Session 1](1-loading-performance-optimization.md) — GPU compute shader, logging removal, pre-sized collections

**Current State (start of session):**
- Total load ~10.8s after session 3
- Two remaining bottlenecks:
  - GPU adjacency scan: 5.9s — synchronous `GetData()` readback + CPU Dictionary/HashSet building
  - Hidden 5.8s gap: `ConvertRGBToTerrainTypes` in ProvinceTerrainAnalyzer — `GetPixels()` on 97.5M RGBA32 texture (1.56GB `Color[]`) + CPU per-pixel dictionary lookup, entirely redundant since TerrainTypeTexture (R8) already exists

---

## What We Did

### 1. Adjacency Disk Cache
**Files Changed:** `Scripts/Map/Province/GPUProvinceNeighborDetector.cs`, `Scripts/Engine/ArchonEngine.cs`

**Problem:** `DetectNeighborsGPU()` took 5.9s every load — GPU dispatch + synchronous `GetData()` readback + CPU Dictionary/HashSet building from ~77k pairs.

**Solution:** Binary disk cache alongside `provinces.png`:
- Cache file: `provinces.png.adjacency`
- Format: `[ADJC magic 4B][version 4B][provinceCount 4B][pairCount 4B][id1:ushort, id2:ushort]...`
- Each edge stored once (lower ID first), reader rebuilds bidirectional adjacency
- Invalidation: `File.GetLastWriteTimeUtc(provinces.png) > cache timestamp`
- File size: ~308KB for 77k pairs

**ArchonEngine.ScanProvinceAdjacencies** now:
1. Try `TryLoadAdjacencyCache(provincesImagePath)` → returns `Dictionary<int, HashSet<int>>` or null
2. On miss: run GPU detection, then `SaveAdjacencyCache()` for next load
3. On hit: skip GPU entirely

**Measured Impact:** **5.9s → 18ms** (cache read + Dictionary building)

### 2. ProvinceTerrainAnalyzer — GPU-Direct Texture Sampling
**Files Changed:** `Scripts/Map/Rendering/ProvinceTerrainAnalyzer.cs`, `Scripts/Map/Loading/MapDataLoader.cs`, `Resources/Shaders/ProvinceTerrainAnalyzer.compute`

**Problem:** 5.8s gap between heightmap completion and terrain analysis start. Root cause: `ConvertRGBToTerrainTypes` in `TerrainBitmapReader`:
- `terrainTexture.GetPixels()` — allocates `Color[97.5M]` = **1.56GB**
- CPU loop over 97.5M pixels: float→byte conversion + dictionary lookup per pixel
- Creates `uint[97.5M]` = 390MB for terrain type indices
- Uploads 390MB to GPU via `ComputeBuffer.SetData()`
- **All redundant** — the R8 `TerrainTypeTexture` (1 byte/pixel terrain indices) was already generated and on the GPU

**Solution:** Shader samples the R8 texture directly, zero CPU involvement:

Compute shader change:
```hlsl
// Before: StructuredBuffer<uint> TerrainDataBuffer (390MB upload from CPU)
// After:  Texture2D<float> TerrainTypeTexture (already on GPU)

uint terrainType = (uint)(TerrainTypeTexture[id.xy] * 255.0 + 0.5);
```

C# changes:
- `AnalyzeAndGetTerrainTypes` now accepts `Texture2D terrainTypeTexture` (R8) instead of `Texture2D terrainTexture` (RGBA32)
- Removed `AnalyzeProvinceTerrain` intermediate method
- Removed `TerrainBitmapReader` dependency from ProvinceTerrainAnalyzer
- `AnalyzeProvinceTerrainGPU` binds texture via `SetTexture()` instead of creating/uploading `ComputeBuffer`
- `MapDataLoader` passes `textureManager.TerrainTypeTexture` instead of `textureManager.ProvinceTerrainTexture`

**Eliminated:**
- 1.56GB `Color[]` allocation (`GetPixels()` on RGBA32)
- 390MB `uint[]` allocation for terrain indices
- 390MB `ComputeBuffer` GPU upload
- CPU loop over 97.5M pixels with per-pixel dictionary lookup

**Measured Impact:** **5.8s → 35ms** (GPU dispatch + readback only)

---

## Decisions Made

### Decision 1: Adjacency Cache Format
**Context:** Need compact, fast-to-read binary format for ~77k adjacency pairs
**Decision:** Custom binary with 16-byte header + 4 bytes per unique pair (ushort + ushort)
**Rationale:** ~308KB file, single `File.ReadAllBytes` + linear scan. Same cache invalidation pattern as pixel caches (source file timestamp comparison).

### Decision 2: GPU-Direct Texture Sampling vs Buffer Upload
**Context:** ProvinceTerrainAnalyzer needed terrain type data. Old approach: CPU-convert RGB→indices, upload as ComputeBuffer.
**Options:**
1. ~~CPU byte→uint expansion from R8 texture~~ — still allocates 390MB uint[]
2. **Shader samples R8 texture directly** — zero CPU work, texture already on GPU
3. ~~ByteAddressBuffer~~ — requires shader change anyway, no benefit over texture

**Decision:** Option 2 — `Texture2D<float>` in shader, `SetTexture()` in C#
**Rationale:** The R8 texture is already on the GPU from TerrainTypeTextureGenerator. Sampling it directly eliminates all CPU conversion and buffer allocation. Proper solution, not a workaround.

---

## Performance Results

### Final Measured Breakdown (cached run, 97.5M pixels, 50k provinces)

| Phase | Session 3 | Session 4 | Delta |
|-------|-----------|-----------|-------|
| Engine start → textures | 1.5s | 1.5s | — |
| Texture creation | 1.5s | 1.5s | — |
| Province data (cache) | 196ms | 178ms | — |
| Terrain load (cache) | 985ms | 1.1s | ±variance |
| Heightmap load (cache) | 41ms | 26ms | — |
| Texture populate (GPU) | 554ms | 534ms | — |
| **Terrain analysis** | **5.8s** | **35ms** | **-99.4%** |
| Blend map generation | 272ms | 193ms | — |
| **Adjacency scan** | **5.9s** | **18ms** | **-99.7%** |
| Interaction + borders | ~200ms | ~200ms | — |
| StarterKit game layer | ~140ms | ~108ms | — |

### Total: ~10.8s → ~5.0s (5.8s saved this session)

### Cumulative: ~33s → ~5.0s (85% reduction across 4 sessions)

---

## Quick Reference for Future Claude

**Key implementations:**
- Adjacency cache: `Scripts/Map/Province/GPUProvinceNeighborDetector.cs` — `TryLoadAdjacencyCache()`, `SaveAdjacencyCache()`
- Adjacency cache consumer: `Scripts/Engine/ArchonEngine.cs` — `ScanProvinceAdjacencies()`
- Terrain GPU-direct: `Scripts/Map/Rendering/ProvinceTerrainAnalyzer.cs` — `AnalyzeProvinceTerrainGPU()`
- Terrain shader: `Resources/Shaders/ProvinceTerrainAnalyzer.compute` — `Texture2D<float> TerrainTypeTexture`
- Texture source change: `Scripts/Map/Loading/MapDataLoader.cs` — passes `TerrainTypeTexture` instead of `ProvinceTerrainTexture`

**Anti-patterns eliminated:**
- Never re-derive data on CPU that's already on GPU as a texture — bind the texture directly in compute shaders
- Never use `GetPixels()`/`SetPixels()` on 97.5M+ pixel textures — allocates GB of managed memory
- Never run GPU→CPU readback every load for data that rarely changes — cache to disk

**Remaining load time budget (~5.0s):**
- Texture creation/allocation: ~2.5s (GPU memory allocation for 15000x6500 textures)
- Terrain image loading + TerrainTypeGen: ~1.1s (cache hit + R8 texture generation from RGB)
- GPU texture population: ~0.5s (province ID pack + owner dispatch)
- Everything else: ~0.9s (blend maps, borders, interaction, StarterKit)
- Further optimization would target TerrainTypeTextureGenerator (~860ms) or texture allocation

**Files changed this session:**
- `Scripts/Map/Province/GPUProvinceNeighborDetector.cs` — added `TryLoadAdjacencyCache()`, `SaveAdjacencyCache()`
- `Scripts/Engine/ArchonEngine.cs` — `ScanProvinceAdjacencies()` rewritten with cache-first pattern
- `Scripts/Map/Rendering/ProvinceTerrainAnalyzer.cs` — removed CPU conversion, GPU-direct texture sampling
- `Scripts/Map/Loading/MapDataLoader.cs` — passes TerrainTypeTexture to analyzer
- `Resources/Shaders/ProvinceTerrainAnalyzer.compute` — `Texture2D<float>` replaces `StructuredBuffer<uint>`

---

## Related Sessions
- [Session 3](3-loading-performance-optimization.md) — Terrain/heightmap pixel cache, texture init fix
- [Session 2](2-loading-performance-optimization.md) — Province pixel cache, single-pass texture population
- [Session 1](1-loading-performance-optimization.md) — GPU compute shader, logging removal, pre-sized collections
