# Scalability Fixes & GPU Adjacency Detection
**Date**: 2026-01-28
**Session**: 1
**Status**: 🔄 In Progress
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Fix scalability issues for large maps (50k+ provinces, 97.5M pixels)

**Secondary Objectives:**
- Make map texture dimensions dynamic (not hardcoded)
- Move adjacency scanning from CPU to GPU

**Success Criteria:**
- Map loads without capacity errors
- Texture sizes match actual map image
- Adjacency detection works at scale

---

## Context & Background

**Current State:**
- User testing with 50,000 province map (97.5M pixels)
- Multiple hardcoded limits blocking large maps

**Why Now:**
- Open source project needs to support extreme cases
- Stress testing for scalability

---

## What We Did

### 1. Save System Quick Fixes
**Files Changed:**
- `Scripts/Core/SaveLoad/SaveManager.cs:343`
- `Scripts/Core/SaveLoad/SaveGameData.cs:53`
- `Scripts/Core/SaveLoad/SaveFileSerializer.cs`

**Implementation:**
- Fixed `currentTick` - was hardcoded to 0, now reads from `gameState.Time.CurrentTick`
- Changed `currentTick` type from `int` to `ulong` to match `TimeManager.CurrentTick`
- Added CRC32 checksum validation on save/load (using `System.IO.Hashing.Crc32`)

**Updated doc:** `Docs/Planning/save-system-improvements.md` - marked Phase 1 complete

### 2. Province Capacity Increase
**Files Changed:**
- `Scripts/Core/Systems/ProvinceSystem.cs:55` - changed `initialCapacity` from 10,000 to 100,000
- `Scripts/Core/Systems/Province/ProvinceDataManager.cs:28-33,53-67`

**Implementation:**
- Default capacity now 100,000 (was 10,000)
- Added `SUPPORTED_PROVINCE_LIMIT = 100000` constant
- Changed hard error to warning for >100k provinces
- Warning only shows once per session

### 3. Dynamic Map Texture Dimensions
**Files Changed:**
- `Scripts/Map/MapTextureManager.cs:14-17,64-85,239-247`
- `Scripts/Map/Loading/Images/ImageParser.cs:131-184`
- `Scripts/Engine/ArchonEngine.cs:456-475`

**Implementation:**
- Removed `[SerializeField]` from map dimensions - now purely dynamic
- Added `ImageParser.TryGetDimensions(filePath, out width, out height)` - reads only header bytes
- `MapTextureManager.Initialize(int width, int height)` now requires dimensions
- `ArchonEngine.InitializeMap()` reads provinces.png header before creating textures
- Normal map defaults to half resolution of main map

### 4. CPU Adjacency Scanner Capacity Fixes (FAILED)
**Files Changed:**
- `Scripts/Map/FastAdjacencyScanner.cs:26-96`

**Attempted:**
- Made capacity dynamic based on `knownProvinceCount`
- Increased multiplier from 50 to 100 per province
- Added minimum 1M capacity

**Result:** Still overflowed `NativeParallelHashSet` at scale. Parallel writes to fixed-capacity hash set fundamentally doesn't scale.

### 5. GPU Adjacency Detection (IN PROGRESS)
**Files Changed:**
- `Resources/Shaders/ProvinceNeighborDetection.compute` (NEW)
- `Scripts/Map/Province/GPUProvinceNeighborDetector.cs`
- `Scripts/Engine/ArchonEngine.cs:569-642`

**Implementation:**
Created new compute shader with two kernels:
```hlsl
#pragma kernel DetectNeighbors    // Outputs neighbor pairs to AppendStructuredBuffer
#pragma kernel CalculateBounds    // Calculates province bounding boxes
```

Key design:
- Uses `AppendStructuredBuffer<uint2>` for dynamic pair output (no fixed capacity)
- Only checks right+bottom neighbors (avoids duplicate detection)
- Creates canonical pairs (smaller ID first)
- Detects coastal provinces (touching ocean ID 0)

**Updated GPUProvinceNeighborDetector:**
- Changed to use `ComputeBufferType.Append`
- Uses `ComputeBuffer.CopyCount()` to read actual append count
- Changed input from `Texture2D` to `Texture` to accept `RenderTexture`
- Added `AdjacencyDictionary` field to result for direct use by `AdjacencySystem`

---

## What Didn't Work ❌

### 1. CPU Parallel Hash Set for Large Maps
- **What we tried:** `NativeParallelHashSet<ulong>` with various capacity estimates
- **Why it failed:** Fixed capacity + parallel atomic writes = overflow at scale
- **Lesson:** Parallel writes to fixed containers don't scale for unknown output sizes
- **Don't try again:** Any fixed-capacity parallel container for variable output

---

## Problems Encountered & Solutions

### Problem 1: Texture Size Mismatch
**Symptom:** `MapTexturePopulator: Size mismatch - Array: 97500000, Texture: 11534336`
**Root Cause:** Hardcoded texture dimensions didn't match loaded map image
**Solution:** Read dimensions from image header before creating textures

### Problem 2: HashMap is Full (Burst Job)
**Symptom:** Multiple `System.InvalidOperationException: HashMap is full` from `AdjacencyScanJob`
**Root Cause:** 50k provinces × complex borders = millions of border pixel discoveries, fixed hash set too small
**Solution:** Move to GPU with `AppendStructuredBuffer` (no capacity limit)

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Fix `ProvinceNeighborDetector.NeighborResult`** - Add `AdjacencyDictionary` field to struct
2. **Test GPU adjacency detection** - Verify compute shader compiles and runs
3. **Wire up to AdjacencySystem** - `GameState.Adjacencies.SetAdjacencies(gpuResult.AdjacencyDictionary)`
4. **Test full map load** - 50k province stress test

### Code State - Where We Left Off

**Compute Shader:** `Resources/Shaders/ProvinceNeighborDetection.compute` - Created, needs testing

**GPUProvinceNeighborDetector.cs:**
- `Initialize()` - loads shader from `Resources/Shaders/ProvinceNeighborDetection`
- `DetectNeighborsGPU(Texture, int)` - updated to use AppendStructuredBuffer
- `ConvertToResult()` - builds `Dictionary<int, HashSet<int>>` but needs `AdjacencyDictionary` field added to result struct

**ArchonEngine.cs:569-642:**
- `ScanProvinceAdjacencies()` - updated to call GPU detector
- Currently tries to access `gpuResult.AdjacencyDictionary` which doesn't exist yet

### Files That Need Changes

1. **`Scripts/Map/Province/ProvinceNeighborDetector.cs`** - Find `NeighborResult` struct, add:
   ```csharp
   public Dictionary<int, HashSet<int>> AdjacencyDictionary;
   ```

2. **`Scripts/Engine/ArchonEngine.cs:607-614`** - Update to use `AdjacencyDictionary`:
   ```csharp
   // Populate GameState.Adjacencies directly from GPU result
   GameState.Adjacencies.SetAdjacencies(gpuResult.AdjacencyDictionary);
   ```

---

## Quick Reference for Future Claude

**Key Files:**
- GPU Shader: `Resources/Shaders/ProvinceNeighborDetection.compute`
- GPU Detector: `Scripts/Map/Province/GPUProvinceNeighborDetector.cs`
- Caller: `Scripts/Engine/ArchonEngine.cs:ScanProvinceAdjacencies()`
- Old CPU Scanner: `Scripts/Map/FastAdjacencyScanner.cs` (deprecated for large maps)

**Critical Pattern:**
- `AppendStructuredBuffer` + `ComputeBuffer.CopyCount()` for variable GPU output
- Canonical pairs: `a < b ? (a,b) : (b,a)` to avoid duplicates

**Gotchas:**
- `ProvinceIDTexture` is a `RenderTexture`, not `Texture2D`
- Province ID encoding: `(g << 8) | r` from RG channels
- Ocean = province ID 0

---

## Links & References

### Related Documentation
- [save-system-improvements.md](../../Planning/save-system-improvements.md) - Save system roadmap
- [BorderDetection.compute](../../../../Resources/Shaders/BorderDetection.compute) - Similar pattern for border detection

### Code References
- Province ID decoding: `BorderDetection.compute:28-36`
- Existing GPU pattern: `BorderComputeDispatcher.cs`
- AdjacencySystem: `Scripts/Core/Systems/AdjacencySystem.cs`

---

*Session ended mid-implementation of GPU adjacency detection*
