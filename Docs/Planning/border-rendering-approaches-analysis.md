# Border Rendering Approaches - Architectural Analysis
**Status**: Knowledge Base
**Purpose**: Timeless comparison of border rendering techniques for grand strategy games
**Updated**: 2025-10-29 (after AAA title RenderDoc investigation)

---

## Requirements for Grand Strategy Map Borders

### Visual Quality
- Smooth anti-aliased edges (no jaggies)
- Consistent visual width at all zoom levels
- Flat caps at endpoints (not round blobs)
- Handles complex geometry (coastlines, islands, diagonal borders)
- Professional appearance (painted/artistic look)

### Technical Constraints
- Large maps (5000x2000+ pixels, thousands of provinces)
- 150,000+ border pixels
- Real-time performance (<2ms per frame)
- Works with bitmap province IDs (not vector source data)
- Must support 3D terrain tessellation

### Target Benchmark (AAA Grand Strategy)
- 8192x4096 province map (large scale)
- Smooth, thin borders with flat caps
- Two-layer rendering (sharp edge + soft gradient)
- Dynamic province coloring

---

## Approach Comparison Matrix

| Approach | Memory | GPU Cost | Quality | Scalability | Flat Caps | AAA Standard? |
|----------|--------|----------|---------|-------------|-----------|---------------|
| **Distance Field Texture + Multi-Tap** | 2-5MB | 0.5ms | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ✅ | **YES** |
| Mesh-Based Quad Rendering | 10-50MB | 1-2ms | ⭐⭐⭐⭐ | ⭐⭐ | ✅ | ❌ |
| Point-Based SDF (Runtime) | 0MB | 2-5ms | ⭐⭐⭐⭐ | ⭐⭐⭐ | ❌ | ❌ |
| BorderMask + Bilinear | 2-10MB | 0.3ms | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⚠️ | ❌ |
| Chaikin + Rasterization | 10-40MB | 0.5ms | ⭐⭐ | ⭐⭐ | ⚠️ | ❌ |
| Geometry Shader Generation | 1-5MB | 1-3ms | ⭐⭐⭐⭐ | ⭐⭐⭐ | ✅ | ❓ |

---

## Approach 1: Distance Field Texture + Multi-Tap Filtering ⭐ WINNER

**Industry Standard:** Used by AAA grand strategy titles

### Architecture

**Generation (CPU/GPU, once per border change):**
1. Detect border pixels from province map
2. Generate distance field texture (Jump Flood Algorithm or similar)
3. Store in R8_UNORM texture at **1/4 resolution** (e.g., 2048x1024 for 8192x4096 map)
4. Distance value = 0.0 at border, 1.0 far from border

**Rendering (GPU, every frame):**
1. Fragment shader samples distance texture **9 times** (3x3 pattern, ±0.75 offset)
2. Average samples: `dist *= 1/9` (box blur for extra smoothing)
3. **Two-layer rendering:**
   - **Edge layer:** Sharp border line (`smoothstep(EdgeWidth + EdgeSmoothness, EdgeWidth, dist)`)
   - **Gradient layer:** Soft outer glow (`smoothstep(EdgeWidth + GradientWidth, EdgeWidth, dist)`)
4. Blend layers with province-based coloring

### Implementation Details

**Texture:**
- Format: R8_UNORM (1 byte per pixel, 0.0-1.0 range)
- Resolution: 1/4 of province map (e.g., 1408x512 for 5632x2048)
- Memory: ~2MB (vs 33.5MB at full resolution = 94% savings)
- Flags: `RENDER_TARGET | SHADER_RESOURCE` (written once, sampled many times)

**Fragment Shader (simplified):**
```hlsl
// 9-tap sampling
float dist = 0;
for (int y = -1; y <= 1; y++) {
    for (int x = -1; x <= 1; x++) {
        dist += tex2D(BorderDistanceTex, uv + float2(x, y) * 0.75 * InvSize);
    }
}
dist *= 0.111111; // 1/9

// Two-layer rendering
float edgeAlpha = smoothstep(EdgeWidth + EdgeSmoothness, EdgeWidth, dist);
float gradAlpha = smoothstep(EdgeWidth + GradientWidth, EdgeWidth, dist);

float3 edgeColor = provinceColor * EdgeColorMul;
float3 gradColor = provinceColor * GradientColorMul;

float3 finalColor = lerp(gradColor, edgeColor, edgeAlpha);
float finalAlpha = max(edgeAlpha * EdgeAlpha, gradAlpha * GradientAlpha);
```

**Tunable Constants:**
- `EdgeWidth` - Sharp border thickness (e.g., 0.5 pixels)
- `GradientWidth` - Soft gradient falloff (e.g., 2.0 pixels)
- `EdgeSmoothness` - Anti-aliasing factor (e.g., 0.2 pixels)
- `EdgeColorMul` - Edge darkening (e.g., 0.8)
- `GradientColorMul` - Gradient darkening (e.g., 0.5)
- `EdgeAlpha`, `GradientAlphaInside`, `GradientAlphaOutside` - Opacity controls

### Why It Works

**1/4 Resolution + Multi-Tap = High Quality**
- Low resolution (2MB) + 9-tap sampling + bilinear filtering = smooth as butter
- Compensates for reduced resolution with multiple samples
- Fragment shader runs at screen resolution → resolution independent

**Two-Layer Rendering = Professional Look**
- Sharp edge + soft gradient = visual depth
- Not a flat line (amateur) but painted appearance (professional)
- Artists can tune layers independently

**Province-Based Coloring = Cohesive Aesthetic**
- Borders inherit and darken province colors
- Dynamic (changes when ownership changes)
- Maintains visual consistency

### Performance

- **Memory:** 2MB texture (1408x512 × 1 byte)
- **GPU Cost:** ~0.5ms per frame
  - 9 texture samples per pixel (texture cache efficient)
  - Simple arithmetic (smoothstep, lerp)
  - Scales with viewport, not map complexity
- **Scalability:** O(screen_pixels) - independent of border complexity

### Pros & Cons

**✅ Pros:**
- AAA-quality results (proven technique)
- Memory efficient (2MB vs 50MB+ for alternatives)
- Resolution independent (smooth at any zoom)
- Works with 3D terrain tessellation (fragment shader samples world space)
- Flat caps achievable (distance to line segments, not points)
- Handles complex coastlines without vertex explosion

**❌ Cons:**
- Requires distance field generation (JFA or similar algorithm)
- 9 texture samples per pixel (but textures are fast)
- Round caps if using point-based distance (must use line segment distance)
- Must regenerate texture when borders change (~5ms CPU/GPU cost)

### When to Use

**Use when:**
- Building grand strategy game with Paradox-quality borders
- Map has complex geometry (coastlines, islands)
- Need consistent performance across zoom levels
- Have 3D terrain requiring fragment shader rendering

**Don't use when:**
- Borders are extremely thick (>10 pixels) - mesh might be simpler
- Borders change every frame (regeneration cost)
- Very simple geometry (few straight borders) - mesh is fine

---

## Approach 2: Mesh-Based Quad Rendering

### Architecture

**Generation (CPU, once per border change):**
1. Extract border polylines (Chaikin smoothed or raw)
2. For each line segment:
   - Calculate perpendicular direction
   - Generate 4 vertices forming thin quad
   - Add to vertex buffer
3. Split into multiple meshes if >65k vertices (Unity limit)

**Rendering (GPU, every frame):**
1. `Graphics.DrawMesh` or standard mesh rendering
2. Vertex shader transforms positions, reads heightmap for 3D terrain
3. Fragment shader applies vertex colors or textures

### Implementation Details

**Vertex Generation:**
```csharp
Vector2 dir = (p1 - p0).normalized;
Vector2 perp = new Vector2(-dir.y, dir.x) * borderWidth * 0.5f;

// 4 vertices per segment
vertices.Add(new Vector3(p0.x - perp.x, height, p0.y - perp.y));
vertices.Add(new Vector3(p0.x + perp.x, height, p0.y + perp.y));
vertices.Add(new Vector3(p1.x + perp.x, height, p1.y + perp.y));
vertices.Add(new Vector3(p1.x - perp.x, height, p1.y - perp.y));

// 2 triangles per segment
triangles.Add(baseIndex + 0, 1, 2);
triangles.Add(baseIndex + 0, 2, 3);
```

**Vertex Data:**
- Position (Vector3, 12 bytes)
- Color (Color32, 4 bytes)
- Total: 16 bytes per vertex

### Performance

- **Memory:** Variable (4 verts per segment)
  - Simple map: 200k segments = 800k verts = 12.8MB
  - Complex map (Imperator quality): 1M+ verts = 16MB+
- **GPU Cost:** 0.5-2ms per frame
  - Vertex transform (cheap)
  - Rasterization (GPU's bread and butter)
  - Multiple draw calls if split meshes
- **Scalability:** O(border_segments × subdivision_level)

### Pros & Cons

**✅ Pros:**
- **Flat caps by design** (quads end at exact endpoints)
- Simple implementation (standard mesh rendering)
- No texture generation needed
- Direct geometry control (miter joins, caps, etc.)
- Familiar technique (every engine supports meshes)

**❌ Cons:**
- **Vertex count explosion** for complex coastlines (need dense sampling for smooth curves)
- Unity 65k vertex limit (requires splitting into multiple meshes)
- **Doesn't scale** to Imperator-level complexity (1M+ vertices needed)
- LOD system required for performance (adds complexity)
- Miter joins needed at corners (extra geometry)
- Hard triangle edges (no built-in anti-aliasing, need MSAA)
- Memory grows with border complexity

### When to Use

**Use when:**
- Simple maps with few borders
- Borders rarely change (static geometry)
- Need explicit geometry control
- Rendering pipeline already mesh-focused

**Don't use when:**
- Complex coastlines, islands (vertex explosion)
- Large-scale grand strategy maps
- Dynamic borders that change frequently
- Need Imperator-quality smoothness without millions of vertices

---

## Approach 3: Point-Based SDF (Runtime Evaluation)

### Architecture

**Setup (CPU, once per border change):**
1. Extract border polylines
2. Fit curves (Bézier, Catmull-Rom, etc.)
3. Upload curve control points to GPU buffer
4. Build spatial grid for acceleration

**Rendering (GPU, every frame):**
1. Fragment shader for each pixel:
   - Query spatial grid for nearby curves
   - Calculate distance to each curve's sample points
   - Take minimum distance
   - Render if distance < threshold

### Implementation Details

**Distance Calculation:**
```hlsl
float minDist = 999999;
for each nearby curve:
    for each sample point on curve:
        float dist = distance(pixelPos, samplePoint);
        minDist = min(minDist, dist);

if (minDist < borderWidth) {
    output = borderColor;
}
```

### Performance

- **Memory:** Minimal (curve control points + spatial grid = <1MB)
- **GPU Cost:** 1-5ms per frame (depends on curve density)
  - Per-pixel distance calculations
  - Spatial grid reduces checks to ~5-20 curves per pixel
- **Scalability:** O(screen_pixels × curves_per_grid_cell)

### Pros & Cons

**✅ Pros:**
- Minimal memory footprint
- Resolution independent (true vector rendering)
- Smooth curves (Bézier or similar)
- No texture generation
- Easy to update (just modify curve points)

**❌ Cons:**
- **Round caps inevitable** (point-based distance = circular falloff)
- **Mathematically unfixable** (tried 4 sessions, all attempts failed)
- Higher GPU cost than texture sampling
- Complexity in curve fitting and spatial acceleration
- Not used by AAA titles (confirmed via RenderDoc investigation)

### Why Round Caps Are Unfixable

**Mathematical Reality:**
```
Distance to curve points:
- At curve middle: Multiple points nearby → minimum distance = perpendicular distance to curve
- At curve endpoint: Only ONE point nearby → distance = Euclidean distance to that point
- Euclidean distance to a point = circular gradient = ROUND CAP
```

**All attempted fixes failed:**
- Endpoint distance penalty → varying thickness
- Directional culling → gaps and inverse caps
- Connectivity flags → doesn't change distance geometry
- Junction detection → doesn't affect endpoints

**Conclusion:** Point-based SDF and flat caps are incompatible.

### When to Use

**Use when:**
- Round caps are acceptable (decorative elements, UI)
- Need minimal memory (mobile, web)
- Borders change frequently (no texture regeneration)
- Simple aesthetic (not aiming for Paradox quality)

**Don't use when:**
- Flat caps required (grand strategy genre standard)
- Trying to match AAA grand strategy appearance
- Performance budget is tight (texture sampling faster)

---

## Approach 4: BorderMask + Bilinear Filtering

### Architecture

**Generation (CPU/GPU, once per border change):**
1. Rasterize borders to texture
2. Binary mask: 0.0 = no border, 1.0 = border, 0.5 = junctions
3. Store in R8_UNORM texture

**Rendering (GPU, every frame):**
1. Fragment shader samples BorderMask with **bilinear filtering**
2. Threshold check: `if (mask > 0.4 && mask < 0.6) render border`
3. Bilinear filtering creates smooth gradient at texture edges
4. Solid color rendering (no alpha blending)

### Performance

- **Memory:** 2-10MB (depends on mask resolution)
- **GPU Cost:** 0.3ms per frame (single texture sample)
- **Scalability:** O(screen_pixels) - excellent

### Pros & Cons

**✅ Pros:**
- Very fast (single texture sample)
- Simple implementation
- Works reasonably well for pixel-accurate borders
- Bilinear filtering provides sub-pixel smoothing

**❌ Cons:**
- **Fundamental trade-off:** Can't achieve smooth + accurate + complete simultaneously
  - Wider threshold → smooth but offset from true border
  - Narrow threshold → accurate but incomplete coverage (gaps)
- Binary mask loses distance information
- Not as smooth as continuous distance field
- Flat caps depend on rasterization (not guaranteed)

### Why We Tried This

**Session 5 (Oct 28) Discovery:**
- Accidentally discovered bilinear filtering on mask creates smooth appearance
- Mask texture (rasterized) + bilinear filtering (GPU) + solid rendering = crisp output
- "Bilinear filtering on MASK, not RENDERED BORDER"

**Why It's Not The AAA Approach:**
- AAA titles use **distance values** (0.0-1.0 continuous), not binary mask
- AAA approach uses **9-tap sampling**, not single sample
- AAA approach uses **two-layer rendering**, not single threshold

**This was 75% of the way there** - just needed distance values + multi-tap + two-layer!

### When to Use

**Use when:**
- Prototyping (quick implementation)
- Pixel-accurate borders acceptable
- Simple visual style
- Not aiming for Paradox quality

**Don't use when:**
- Need smooth geometric curves
- Targeting AAA-level polish
- Complex coastlines must look good

---

## Approach 5: Chaikin Smoothing + Rasterization

### Architecture

1. Extract pixel chains from province boundaries
2. Apply Chaikin corner-cutting algorithm (iterative smoothing)
3. Rasterize smoothed polylines back to texture (same resolution as province map)
4. Fragment shader samples rasterized texture

### Performance

- **Memory:** 10-40MB (full-resolution border texture)
- **GPU Cost:** 0.5ms (texture sampling)
- **Scalability:** Limited by source resolution

### Pros & Cons

**✅ Pros:**
- Chaikin smoothing creates genuinely smooth curves
- Algorithm is simple and fast
- Works on jagged pixel data

**❌ Cons:**
- **Resolution-bound:** Smoothing happens, but rasterization quantizes back to original pixel grid
- Small features (3-10 pixels) have no detail to work with
- Can't create detail that doesn't exist in source
- Memory cost of full-resolution texture
- Not resolution independent

### Why It Failed

**The Problem:**
```
Input: Jagged pixel border (100 pixels)
Chaikin: Smooth curve (100 points, sub-pixel precision)
Rasterize: Back to 100 pixels (loses sub-pixel detail!)
Result: Still looks jagged because final output is same resolution as input
```

**Lesson:** Pre-processing + rasterization can't exceed source resolution. Need runtime evaluation (distance field, mesh, etc.).

### When to Use

**Don't use** - this approach has no advantages over distance field texture + multi-tap.

---

## Approach 6: Geometry Shader Generation

### Architecture (Theoretical)

1. Upload border polylines as line strip
2. Vertex shader positions endpoints, samples heightmap
3. **Geometry shader** generates quads from line segments
4. Fragment shader renders with textures/colors

### Performance (Estimated)

- **Memory:** 1-5MB (just line data)
- **GPU Cost:** 1-3ms (geometry shader execution)
- **Scalability:** Good (GPU generates geometry)

### Pros & Cons

**✅ Pros:**
- Flat caps (quad generation controls endpoints)
- Geometry generated on GPU (no CPU mesh building)
- Can add miter joins in geometry shader
- Memory efficient (store line data, generate quads on GPU)

**❌ Cons:**
- Geometry shaders not universally supported (mobile, web)
- More complex to implement
- Debugging is harder (geometry shader)
- Not confirmed to be Imperator's approach

### Speculation

**Could AAA Titles Use This?**
- Unlikely - RenderDoc investigation showed `DrawIndexedInstanced(108, 1602)` which suggests pre-built geometry or distance texture
- Distance texture approach simpler and proven via shader code extraction
- Geometry shaders have support issues on some platforms

---

## Fundamental Principles

### Principle 1: Distance Field Type Matters
- **Point-based distance** = Round caps (Euclidean distance to discrete points)
- **Line segment distance** = Flat caps possible (distance to continuous line)
- Can't fix point-based with penalties/culling/flags

### Principle 2: Resolution Independence Requires Runtime Evaluation
- Pre-rasterization hits source resolution ceiling
- Must evaluate at fragment shader time or generate geometry
- Bilinear filtering helps but doesn't add geometric detail

### Principle 3: Multi-Tap Filtering Compensates for Low Resolution
- Single sample at low res = jagged
- 9 samples + bilinear at low res = smooth as butter
- Industry secret: 1/4 resolution + multi-tap = nearly full resolution quality

### Principle 4: Two-Layer Rendering = Professional Polish
- Single threshold = flat line (amateur)
- Edge + gradient = painted look (professional)
- Photoshop's "stroke + outer glow" in shader form

### Principle 5: Memory vs Quality Trade-offs
- Full resolution texture: High quality, high memory
- 1/4 resolution + multi-tap: High quality, low memory **← AAA industry standard**
- Mesh geometry: Variable quality, variable memory (depends on subdivision)

### Principle 6: Scalability Determines Long-Term Viability
- O(screen_pixels) = scales with viewport **← Best**
- O(border_segments) = scales with map complexity (can explode)
- O(map_resolution) = fixed cost but can be large

---

## Recommended Approach

**For Grand Strategy Games:**
→ **Distance Field Texture + Multi-Tap Filtering + Two-Layer Rendering**

**Why:**
- Proven by AAA grand strategy titles (RenderDoc confirmed)
- Best quality-to-memory ratio (2MB for high quality)
- Best performance scaling (O(screen_pixels))
- Handles complex geometry without vertex explosion
- Works with 3D terrain tessellation
- Achieves flat caps (with line segment distance)
- Professional two-layer appearance

**Implementation Time:** 7-8 hours (one evening)
**Memory Cost:** 2-5MB
**Performance Cost:** 0.5ms per frame
**Quality:** ⭐⭐⭐⭐⭐ (AAA-level)

---

## Implementation Roadmap

### Phase 1: Distance Field Generation (2-3 hours)
1. Generate distance field from province boundaries (JFA or similar)
2. Store in R8_UNORM texture at 1/4 resolution
3. Create as render target + shader resource
4. Regenerate when borders change

### Phase 2: Fragment Shader - Multi-Tap Sampling (1 hour)
1. Sample distance texture 9 times (3x3 pattern, ±0.75 offset)
2. Average samples: `dist *= 1/9`
3. Basic rendering: `if (dist < threshold) output = color`

### Phase 3: Two-Layer Edge + Gradient (2 hours)
1. Add EdgeWidth and GradientWidth constants
2. Implement edge layer (sharp line)
3. Implement gradient layer (soft glow)
4. Blend with smoothstep

### Phase 4: Polish (2 hours)
1. Add all tunable constants (EdgeSmoothness, color multipliers, alphas)
2. Integrate province color sampling
3. Performance optimization
4. Visual comparison to reference AAA titles

**Total:** 7-8 hours from zero to AAA-quality borders

---

## Lessons from 5 Days of Experimentation

### What Worked
1. **RenderDoc investigation** - 3 hours revealed complete solution (saved weeks)
2. **Systematic exploration** - Tried 6 approaches, learned from each failure
3. **Chaikin smoothing** - Correct shape, just wrong rendering method
4. **Distance field concept** - Right idea, wrong execution (point-based vs line segment)

### What Didn't Work
1. **Guessing from screenshots** - Wasted 5 days vs 3 hours with RenderDoc
2. **Point-based SDF** - 4 sessions trying to fix unfixable round caps
3. **Mesh approach** - Works but doesn't scale to target quality
4. **Trying to fix symptoms** - Should have questioned fundamental approach sooner

### Key Insights
1. **Round caps are a deal-breaker** in grand strategy genre
2. **Multi-tap filtering is the secret sauce** for low-res textures
3. **Two-layer rendering separates amateur from professional**
4. **1/4 resolution is the sweet spot** for memory vs quality
5. **Always reverse engineer the leaders** before reinventing the wheel

---

*Last Updated: 2025-10-29*
*AAA approach confirmed via RenderDoc investigation of grand strategy titles*
*Ready for implementation in Archon engine*
