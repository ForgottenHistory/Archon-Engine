# Terrain Detail Mapping Architecture

**Status:** 🚧 Planning
**Date:** 2025-11-02
**Goal:** Scale-independent terrain visuals via macro/micro texture blending

---

## Principle: Dual-Layer Terrain Rendering

**Problem:** Single terrain texture stretched across map becomes pixelated when zoomed in (fixed pixel budget).

**Solution:** Separate color (macro) from detail (micro) using dual-layer architecture:
- **Macro Texture:** Low-res unique colors per region (what terrain type)
- **Detail Texture:** High-res tiled grain (sharp at any zoom level)

**Key Insight:** World-space UVs make detail texture scale-independent. Camera zooms in → samples detail texture more densely → stays sharp.

---

## Architecture: Three-Texture System

### 1. Macro Texture (Existing: `_ProvinceTerrainTexture`)
- **Source:** `terrain.bmp` (8-bit indexed) converted via `TerrainColorMapper`
- **Size:** 5632x2048 RGBA32
- **UVs:** Map coordinates (0-1 across world)
- **Purpose:** Defines terrain COLOR per region (grasslands=green, desert=tan, etc.)

### 2. Terrain Type Texture (NEW: `_TerrainTypeTexture`)
- **Format:** R8 (1 byte per pixel, 0-255 terrain type IDs)
- **Size:** Same as macro texture (5632x2048)
- **Generation:** Reverse lookup from `terrain.bmp` using `TerrainColorMapper`
- **Purpose:** Tells shader WHICH detail texture to use per pixel
- **Moddable:** Auto-generated from `terrain.bmp`, no manual authoring

### 3. Detail Texture Array (NEW: `_TerrainDetailArray`)
- **Type:** `Texture2DArray` (all detail textures in one shader property)
- **Format:** RGBA32_sRGB (BC7 compressed for performance)
- **Size per layer:** 512x512 or 1024x1024 (tiled)
- **Indices:** Match `TerrainColorMapper` (0=grasslands, 3=desert, 6=mountain, etc.)
- **UVs:** World-space (`positionWS.xz * _DetailTiling`)
- **Moddable:** Load from `Assets/Data/textures/terrain_detail/{index}_{name}.png`

---

## Shader Algorithm

```
1. Sample terrain type ID from _TerrainTypeTexture (R8, 0-255)
2. Sample macro color from _ProvinceTerrainTexture (RGBA32, map UVs)
3. Calculate world-space UVs: worldUV = positionWS.xz * _DetailTiling
4. Sample detail from array: _TerrainDetailArray[terrainType] using worldUV
5. Blend: finalColor = macroColor * detail
```

**Why This Works:**
- Macro gives unique color per region (not tiled)
- Detail gives sharp grain (tiled via world-space UVs)
- Terrain type selects correct detail texture per pixel
- Supports 256 distinct terrain types (future-proof)

---

## Texture Format Requirements

**Critical:** Follow explicit `GraphicsFormat` pattern (see `decisions/explicit-graphics-format.md`)

### Terrain Type Texture
```csharp
var descriptor = new RenderTextureDescriptor(
    width, height,
    GraphicsFormat.R8_UNorm,  // 1 byte, [0,1] normalized
    0
);
descriptor.enableRandomWrite = false;  // Read-only
```
**Why R8_UNorm:**
- 1 byte = 256 terrain types (0-255)
- No UAV needed (static after generation)
- Minimal memory (5632x2048x1 = 11.5 MB uncompressed)

### Detail Texture Array
```csharp
var textureArray = new Texture2DArray(
    512, 512,          // Per-texture resolution
    layerCount,        // Number of terrain types
    GraphicsFormat.R8G8B8A8_SRGB,  // sRGB for proper color
    TextureCreationFlags.MipChain  // Mipmaps for performance
);
```
**Why Texture2DArray:**
- Single shader property (no array limit on texture slots)
- GPU-efficient indexing (no branching)
- Mipmaps per layer (automatic LOD)
- BC7 compression supported (4:1 ratio)

---

## Moddability Design

### File Structure
```
Assets/Data/textures/terrain_detail/
├── 0_grasslands.png
├── 3_desert.png
├── 6_mountain.png
├── 9_marsh.png
├── 12_forest.png
├── 15_ocean.png
├── 16_snow.png
└── ... (modders add more)
```

### Loader Behavior
1. **Scan folder** for `{index}_{name}.png` pattern
2. **Parse index** from filename (0-255)
3. **Build Texture2DArray** with layers matching indices
4. **Missing textures:** Use default neutral gray detail (50% gray = no effect on multiply blend)
5. **Runtime replacement:** Hot-reload supported (rebuild array on file change)

### Modder Workflow
**Add new terrain type:**
1. Add color to `TerrainColorMapper.cs` (e.g., `[25] = new Color32(...)`)
2. Create `25_volcanic.png` detail texture
3. Edit `terrain.bmp` to use palette index 25
4. Regenerate terrain type texture (automatic on load)

**Replace existing detail:**
1. Drop new `3_desert.png` in folder
2. Overrides default desert detail

---

## Performance Characteristics

### Memory Cost
- **Terrain Type Texture:** 11.5 MB uncompressed (5632x2048x1)
- **Detail Array (uncompressed):** `512x512x4 bytes x N layers`
  - 8 layers = 8 MB
  - 16 layers = 16 MB
- **Detail Array (BC7 compressed):** ~25% of uncompressed
  - 8 layers = 2 MB
  - 16 layers = 4 MB

### Runtime Cost
- **Additional texture samples:** +2 per pixel (terrain type, detail array)
- **Frame time impact:** <5% (detail sampling is cheap)
- **Mipmaps:** Automatic LOD reduces bandwidth at distance

### Scalability
- **Supports 256 terrain types** (1 byte index)
- **No shader recompilation** when adding terrain types
- **No texture slot limits** (single Texture2DArray property)

---

## Implementation Phases

### Phase 1: Terrain Type Texture Generation
- Utility to convert `terrain.bmp` → terrain type texture (R8)
- Reverse lookup using `TerrainColorMapper`
- Save as `terrain_types.asset` (RenderTexture)

### Phase 2: Detail Texture Array Loader
- Scan `Assets/Data/textures/terrain_detail/`
- Build `Texture2DArray` from `{index}_{name}.png` files
- Apply BC7 compression
- Generate mipmaps

### Phase 3: Shader Integration
- Add `_TerrainTypeTexture` (Texture2D R8) property
- Add `_TerrainDetailArray` (Texture2DArray) property
- Add `_DetailTiling` (float) property
- Modify `RenderTerrain()` in `MapModeTerrain.hlsl`

### Phase 4: Default Detail Textures
- Create initial set (grasslands, desert, mountain, forest, etc.)
- Procedurally generated or sourced from CC0 libraries

---

## Anti-Tiling Enhancement (Future)

**Optional:** Add Inigo Quilez anti-tiling to break up repetition patterns.

**Cost:** +1 texture sample, +1 noise texture slot, +50% shader complexity

**Benefit:** Eliminates visible tiling patterns at extreme zoom

**Decision:** Implement basic detail mapping first, evaluate if tiling is noticeable before adding anti-tiling.

---

## Trade-offs

### What We Gain
- ✅ Scale-independent terrain (infinite zoom sharpness)
- ✅ Supports 256 distinct terrain types
- ✅ Fully moddable (drop-in PNG files)
- ✅ Modern AAA visuals (Victoria 3/EU5 quality)
- ✅ Minimal runtime cost (<5% frame time)

### What We Give Up
- ❌ Additional texture memory (~2-4 MB compressed)
- ❌ Slightly more complex shader (+2 texture samples)
- ❌ Requires detail texture authoring (but can start with procedural)

**Verdict:** Trade-off heavily favors implementation. Memory cost is trivial on modern GPUs, visual improvement is massive.

---

## Related Patterns

- **Pattern 1 (Engine-Game Separation):** Terrain type texture = ENGINE mechanism, detail textures = GAME policy
- **Explicit GraphicsFormat:** Always use `GraphicsFormat.R8_UNorm` for terrain type texture
- **GPU-First Architecture:** Detail selection happens entirely on GPU (zero CPU cost)

---

## Success Metrics

**Visual Quality:**
- Terrain stays sharp at all zoom levels (no pixelation)
- Distinct detail per terrain type (grass ≠ desert ≠ mountain)

**Performance:**
- <5% frame time impact
- No stuttering when zooming

**Moddability:**
- Modders can add new terrain types without code changes
- Hot-reload of detail textures works

---

*Decision to implement: 2025-11-02*
*Based on: Paradox modern graphics architecture (Victoria 3/EU5)*
