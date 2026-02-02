# Loading Performance Optimization (97.5M pixel stress test)
**Date**: 2026-02-02
**Session**: 1
**Status**: ✅ Complete
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Optimize map loading time for 15000x6500 (97.5M pixel) stress test map with 50k provinces

**Success Criteria:**
- Reduce total load time from ~33s baseline

---

## Context & Background

**Current State:**
- Stress test map: 15000x6500 pixels, 50,000 provinces, ~97.5M pixels
- Baseline total load: ~33 seconds
- Three bottlenecks identified: province registration logging (~2.7s), PNG load+parse (~8s), CPU pixel loop (~10s)

---

## What We Did

### 1. Disabled Normal Map Generation
**Files Changed:** `Scripts/Map/Loading/MapDataLoader.cs:122,181`

Commented out two `GenerateNormalMapFromHeightmap()` calls — feature was broken anyway, saves ~0.9s.

### 2. Removed Per-Province Logging in Registries
**Files Changed:**
- `Scripts/Core/Registries/ProvinceRegistry.cs:52` — removed per-registration log (50,000 calls)
- `Scripts/Core/Registries/CountryRegistry.cs:60` — removed per-registration log
- `Scripts/Core/Registries/Registry.cs:53` — removed per-registration log (generic registry)

**Impact:** Province registration: **2.7s → 9ms**. Game init total: **2.91s → 0.21s**.

### 3. Pre-sized Collections in ProvinceRegistry and DefinitionLoader
**Files Changed:**
- `Scripts/Core/Registries/ProvinceRegistry.cs` — constructor now takes capacity, pre-sizes Dictionary and List (default 65536)
- `Scripts/Core/Loaders/DefinitionLoader.cs` — pre-sizes entries List with `lines.Length` capacity

### 4. GPU Compute Shader for Province Texture Population (VERIFIED)
**Files Changed:**
- `Resources/Shaders/PopulateProvinceTextures.compute` — **NEW** compute shader
- `Scripts/Map/Rendering/MapTexturePopulator.cs` — rewritten with GPU path + CPU fallback
- `Scripts/Map/Loading/ProvinceMapProcessor.cs` — added `BMPData.TryGetRawPixelBytes()` accessor

**Architecture:**
- Open-addressing hash table built on CPU from `NativeHashMap<int,int>` color→provinceID mapping
- Uploaded to GPU as `StructuredBuffer<uint2>` (~128k slots, ~1MB)
- Raw PNG pixel bytes packed into `uint[]` on CPU, uploaded as `StructuredBuffer<uint>`
- Compute shader (8x8 threads): each pixel reads RGB, hashes to find province ID, writes packed ID to ProvinceIDTexture
- ProvinceColorTexture populated separately via CPU (it's a Texture2D, not RenderTexture)
- CPU fallback path preserved for BMP format (not currently used)
- Hash function: `key ^= key >> 16; key *= 0x45d9f3b; key ^= key >> 16;` — identical in C# and HLSL

**Measured Impact:** CPU pixel loop **~9.4s → ~1.3s** (pack + hash build + GPU dispatch + color texture copy)
Note: ~1.3s includes CPU-side pixel packing of 97.5M pixels into uint[] + Color32[] arrays. Detailed breakdown unavailable because log level was set to Warnings (Info logs suppressed).

---

## Decisions Made

### Decision 1: GPU Hash Table vs 3D Lookup Texture
**Options:**
1. Open-addressing hash table in ComputeBuffer — ~1MB, works for any province count
2. 3D lookup texture (256^3) — 64MB VRAM, fast but wasteful

**Decision:** Hash table. 50% load factor, linear probing, max 64 probes.
**Rationale:** 1MB vs 64MB, scales to 65k provinces trivially.

### Decision 2: Keep 65k Province Limit (ushort)
**Context:** Considered 80k to match EU5
**Decision:** Stay at 65k. EU4 ~5k, Vic3 ~15k, HOI4 ~13k, CK3 ~10k. 65k exceeds all shipped Paradox titles except EU5. Changing ushort encoding would be invasive across entire texture pipeline.

### Decision 3: ProvinceColorTexture Separate from GPU Path
**Context:** ProvinceColorTexture is a Texture2D (not RenderTexture), can't be written by compute shader
**Decision:** GPU shader only writes ProvinceIDTexture. Color texture populated via direct CPU byte copy from raw PNG data (no hash lookups needed — just memcpy-equivalent).

### Decision 4: BMP Support Deferred
**Context:** BMP has BGR order, row padding, bottom-up flip
**Decision:** GPU path only supports PNG. BMP falls back to CPU. Comment left for future if needed.

---

## Performance Results (All Optimizations)

| Metric | Before | After | Saved |
|--------|--------|-------|-------|
| Province registration | 2.7s | 9ms | ~2.7s |
| Normal map gen | 0.9s | 0s (disabled) | ~0.9s |
| CPU pixel loop → GPU | ~9.4s | ~1.3s | ~8.1s |
| Pre-sized collections | — | — | ~0.1s |
| **Total saved** | — | — | **~11.8s** |

Remaining bottleneck:
- PNG load + decompress: ~7.8s (unchanged) — raw pixel cache (Task #4) would address this

---

## Resolved Questions
1. **Hash function GPU correctness:** ✅ Confirmed — map loads correctly, province selection and ownership work
2. **AsyncGPUReadback.WaitForCompletion sync:** ✅ Confirmed — OwnerTextureDispatcher reads ProvinceIDTexture correctly after GPU populate

## Next Session

### Immediate Next Steps
1. Raw pixel cache for PNG decompression (Task #4) — cache decoded pixels as .raw binary, skip PNG decompress on reload (~7.8s → <1s)
2. Consider further optimizing the ~1.3s GPU path — most time is CPU-side pixel packing (97.5M pixels × 2 passes: uint[] for GPU + Color32[] for color texture)

---

## Quick Reference for Future Claude

**Key implementation:**
- Compute shader: `Resources/Shaders/PopulateProvinceTextures.compute`
- C# dispatcher: `Scripts/Map/Rendering/MapTexturePopulator.cs:106-198` (TryPopulateGPU)
- Raw byte accessor: `Scripts/Map/Loading/ProvinceMapProcessor.cs:84-97` (TryGetRawPixelBytes)
- Hash function must match EXACTLY between C# (HashRGB) and HLSL (HashRGB)

**Gotchas:**
- `Core.Registries` resolves to `Map.Core.Registries` in Map namespace — use `global::Core.Registries`
- `ComputeShader.SetInt` takes `int` not `uint` — no cast needed since table sizes are int
- ProvinceColorTexture is Texture2D, NOT RenderTexture — can't use RWTexture2D in compute shader
