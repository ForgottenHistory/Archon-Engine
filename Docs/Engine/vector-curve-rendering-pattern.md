# Vector Curve Rendering Pattern
**Category:** Graphics Architecture
**Status:** ✅ Production - Proven in border rendering system
**Created:** 2025-10-26

---

## Core Principle

**Problem:** Bitmap data (provinces, rivers, roads) creates jagged geometry at any zoom level. Rasterization cannot create detail that doesn't exist in source pixels.

**Solution:** Extract geometry from bitmap → Fit parametric curves → Store control points → Evaluate curves in fragment shader at screen resolution.

**Key Insight:** Separate geometry (static, vectorizable) from appearance (dynamic, runtime). Curves are mathematical functions, not pixels.

---

## When to Use

**Perfect for:**
- Features from bitmap sources (province borders, rivers, roads, coastlines)
- Need smooth curves at any zoom/resolution
- Static or rarely-changing geometry
- Large feature counts (1000s+) requiring efficient storage

**Don't use for:**
- Text rendering (use TextMeshPro SDF instead)
- UI elements (use Unity UI)
- Frequently-changing geometry (runtime curve fitting is expensive)
- Features without clear boundaries to extract

---

## Architecture Components

### 1. Curve Fitting (CPU, Initialization)
**Purpose:** Convert pixel chains into parametric curves

**Requirements:**
- Least-squares or similar fitting algorithm
- Handle short chains (3-10 pixels) without degeneracy
- Segment long chains into manageable curve counts

**Output:** Control points (e.g., cubic Bézier: P0, P1, P2, P3)

**Cost:** One-time at map load, O(pixels) per feature

### 2. Spatial Acceleration (CPU/GPU, Initialization)
**Purpose:** Prevent O(all curves) tests per fragment

**Required:** Hash grid, quadtree, or BVH mapping screen regions to curve subsets

**Critical:** Without spatial acceleration, fragment shader tests all curves → GPU timeout at scale

**Optimization:** Aim for ~100-500 curves per grid cell maximum

### 3. Fragment Shader Evaluation (GPU, Per-Frame)
**Purpose:** Calculate distance from fragment to nearest curve

**Pattern:**
- Early-out using sparse mask texture (~90% of pixels skip)
- Query spatial grid for fragment position
- Test only nearby curves (not all curves)
- Distance threshold determines if fragment is "on curve"

**Performance:** Distance calculation is expensive (~10-20 iterations for closest point), minimize tests via spatial acceleration

### 4. Distance-to-Curve Algorithm
**Core operation:** Given fragment position and curve, find minimum distance

**Common approaches:**
- Bézier curves: Newton-Raphson iteration to find closest t parameter
- Line segments: Analytical solution (faster but less smooth)
- Polynomial root finding: Exact but complex

**Trade-off:** Accuracy vs performance - balance iteration count with quality needs

---

## Memory Characteristics

**Comparison (10K features, 2 curves per feature avg):**
- Rasterized texture (5632×2048): ~40 MB
- Vector curves (20K segments × 36 bytes): ~720 KB
- **Savings: 55x compression**

**Scalability:** Curve memory grows with feature count, not map resolution

---

## Performance Characteristics

**Bottlenecks:**
1. Fragment shader curve tests (mitigate: spatial acceleration + early-out)
2. Distance calculation iterations (mitigate: limit iteration count, use approximations)
3. Spatial grid lookup (mitigate: uniform grid over complex structures)

**Target:** <0.5ms per frame for border rendering at 1080p

**Achieved:** 0.4-0.8ms per frame (200+ FPS at 5x speed, 100-120 FPS at 50x debug speed)

---

## Constraints & Limitations

**GPU requirements:**
- StructuredBuffer support (DX11+)
- Sufficient shader instruction budget for distance calculations
- UAV texture formats (R8G8B8A8_UNorm universal, avoid R16G16_UNorm platform issues)

**Limitations:**
- Static geometry only (dynamic curve fitting too expensive for per-frame)
- Spatial grid must be rebuilt if curves change
- Distance calculations have iteration limits (perfect accuracy not guaranteed)
- Memory cost grows linearly with feature count

---

## Critical Success Factors

**Must have:**
- Spatial acceleration (GPU timeout without it)
- Early-out optimization (sparse mask, border mask, etc.)
- Explicit struct layout ([StructLayout(Sequential)]) for CPU-GPU data transfer
- 4-byte aligned types (uint not ushort) for GPU buffer compatibility

**Avoid:**
- Testing all curves per fragment (O(curves) → GPU timeout)
- Runtime curve refitting (too expensive, pre-compute at init)
- Rasterizing curves back to texture (defeats resolution independence)
- Float operations for curve control points (use consistent precision)

---

## Known Applications

**Implemented:**
- Province/country borders (583K Bézier segments, 100+ FPS)

**Transferable to:**
- Rivers (extract from bitmap, fit curves, render with width/flow)
- Roads/trade routes (smooth paths with dynamic width)
- Terrain biome boundaries (curves as blend regions between textures)
- Coastlines (land/sea boundaries with wave effects)

**Not applicable:**
- Text rendering (use TextMeshPro SDF)
- Dynamic paths (too expensive to refit curves every frame)
- UI elements (different rendering paradigm)

---

## Comparison with Alternatives

**vs Rasterized Textures:**
- ✅ Resolution independent
- ✅ 50x memory savings
- ❌ More complex implementation
- ❌ GPU instruction cost per fragment

**vs Signed Distance Fields:**
- ✅ True vector curves (not rasterized at any resolution)
- ✅ Better for thin features (1-2px lines)
- ❌ More expensive distance calculations
- ❌ Requires spatial acceleration

**vs Geometry Instancing:**
- ✅ No vertex/triangle overhead
- ✅ Single draw call for all features
- ❌ Fragment shader bound (not vertex bound)
- ❌ Limited to features expressible as curves

---

## Integration Pattern

**Phase 1 (Initialization):**
1. Extract feature pixels from bitmap
2. Chain pixels into ordered paths
3. Fit parametric curves to paths
4. Build spatial acceleration structure
5. Upload curve data + spatial grid to GPU

**Phase 2 (Runtime):**
1. Fragment shader checks sparse mask (early-out)
2. Query spatial grid for fragment position
3. Test distance to nearby curves only
4. Apply appearance based on distance/type

**Phase 3 (Dynamic Updates - Optional):**
1. Update appearance flags (colors, thickness, visibility)
2. Do NOT refit curves (geometry is static)

---

## Anti-Patterns

**Don't:**
- Refit curves at runtime (pre-compute at initialization)
- Test all curves per fragment (use spatial acceleration)
- Use ushort for GPU data (requires uint for 4-byte alignment)
- Assume C# and HLSL struct layouts match (use explicit [StructLayout(Sequential)])
- Rasterize smooth curves back to texture resolution (defeats purpose)
- Skip early-out optimization (testing curves is expensive)

**Do:**
- Document byte offsets for CPU-GPU structs
- Use progressive color debugging for GPU issues
- Profile at target scale from day one
- Limit distance calculation iterations (quality vs performance)

---

## Future Optimizations

**If performance becomes an issue:**
1. Early termination in distance loop (stop when exact hit found)
2. Bounding box culling (AABB test before expensive curve distance)
3. LOD system (simplified curves at far zoom)
4. Hierarchical spatial grid (coarse→fine culling)
5. Compute shader preprocessing (render to texture once per frame)

**Current performance (100-120 FPS at 50x debug speed) is sufficient. Optimize only if needed.**

---

## Lessons Learned

**Critical insights from implementation:**
1. Rasterized smooth curves cannot exceed source bitmap resolution
2. GPU struct layout debugging requires progressive color visualization
3. Spatial acceleration is mandatory, not optional
4. uint (4-byte) alignment is required for GPU buffers
5. TYPELESS texture formats break UAV writes (use explicit formats)

**"The Sunday Was Worth It":**
- Solving graphics problems requires invention, not just implementation
- No reference material exists (Paradox doesn't document this)
- Dead ends are part of the process (Chaikin smoothing, high-res rasterization)
- True resolution independence requires mathematical curves, not more pixels

---

## References

**Related Docs:**
- Session logs: `Docs/Log/2025-10/26/1-7` (full implementation journey)
- Struct layout: `decisions/explicit-graphics-format.md`
- GPU patterns: `dual-layer-architecture.md`

**External Concepts:**
- Bézier curve mathematics
- Signed distance field rendering (fonts)
- Spatial hashing / acceleration structures
- Newton-Raphson root finding

---

*Pattern documented 2025-10-26 after successful border rendering implementation*
*Memory: 720KB curves vs 40MB rasterized (55x savings)*
*Performance: 100-120 FPS with 583K curve segments*
