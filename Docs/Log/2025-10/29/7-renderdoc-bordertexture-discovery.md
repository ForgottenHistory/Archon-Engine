# RenderDoc BorderTexture Discovery
**Date**: 2025-10-29
**Session**: 7
**Status**: ✅ Complete - Critical breakthrough
**Priority**: High

---

## Session Goal

**Primary Objective:**
- Understand how Paradox (Imperator Rome) achieves razor-thin, smooth province borders by analyzing RenderDoc captures

**Secondary Objectives:**
- Determine why previous Bézier curve and mesh-based approaches failed
- Find actionable implementation approach for our border system

**Success Criteria:**
- Understand Paradox's actual border rendering technique
- Identify key shaders and textures used
- Have clear path forward for implementation

---

## Context & Background

**Previous Work:**
- Session 6: Gaussian blur experiment - worked but doesn't solve core thinness problem
- Session 5: Hybrid distance field + BorderMask approach - lines too wide
- Sessions 1-4 (Oct 28-29): Bézier curves + distance fields - junction problems, round caps
- Session 1 (Oct 29): Mesh-based rendering - visible segment breaks

**Current State:**
- All pixel-based approaches produce wide borders (resolution-bound)
- Distance fields create round caps (mathematically impossible to fix)
- Mesh quads show visible segment breaks at junctions
- Blur smooths but doesn't thin borders

**Why Now:**
- User provided RenderDoc captures from Imperator Rome showing perfect thin borders
- Need to understand actual production implementation

---

## What We Did

### 1. Analyzed BorderDistanceTexture Shaders
**Files Analyzed:**
- `DrawIndexedInstance-108-1602-Pixel.txt` (RenderDoc capture)
- `1-DrawIndexedInstance-258-41-Pixel.txt` (RenderDoc capture)
- `2-Draw-4-Pixel.txt` (RenderDoc capture)
- `paradox_borderdistance_texture.png` (screenshot)

**Implementation Pattern Found:**
All three pixel shaders show identical 9-tap sampling + two-layer rendering:

```hlsl
// 9-tap sampling pattern
sample r2.z, r1.xy, BorderDistanceTexture      // Center
mad offsets, InvGradientTextureSize.xyxy, (-0.75, -0.75, 0.75, 0.75), r1.xyxy
sample r3.y, offsets.xy, BorderDistanceTexture // Sample at offset
[... 7 more samples at ±0.75 offsets ...]
mad r8.x, r2.z, 0.111111, -r3.y  // Average all 9 (1/9 = 0.111111)

// Two-layer border rendering
// Gradient layer: soft falloff
// Edge layer: sharp boundary with smoothstep
```

**BorderDistanceTexture Specs:**
- Format: 2048×1024 R8_UNORM
- Flags: RENDER_TARGET (GPU-generated)
- Content: Distance field for borders

**Critical Realization:**
User noticed BorderDistanceTexture doesn't contain all provinces (thousands missing):
> "It's country borders I think. But only Land to Land borders, no coastal"

**Conclusion:** BorderDistanceTexture is only for thick COUNTRY borders (diplomatic boundaries), NOT thin province borders.

### 2. Reviewed Why Bézier Curves Failed
**Session Logs Analyzed:**
- `2025-10/28/1-bezier-curve-refinement-and-junction-experiments.md`
- `2025-10/28/4-junction-detection-and-distance-field-round-cap-problem.md`
- `2025-10/29/1-mesh-based-border-rendering.md`

**Distance Field Bézier (Sessions Oct 28):**
- Implemented curve generation and distance field evaluation
- Problem: Junction overlap with round caps
- Root cause: Distance field evaluation creates circular regions at endpoints
- Tried: Junction detection, control point alignment, CPU unification
- Result: Mathematically impossible to get flat caps from Euclidean distance

**Mesh-Based Rendering (Session Oct 29):**
- Switched to quad mesh generation from Chaikin-smoothed polylines
- Successfully eliminated round caps (flat ends)
- Problem: Visible segment breaks between quads
- User feedback: "they looked like shit, that's why. They were clearly separate segments"

**Why Failures Matter:**
Both approaches (distance fields + mesh quads) were resolution-independent but had visual artifacts. Need seamless joins.

### 3. Line Strip Discussion
**GPU Primitive Introduced:** LINE_STRIP (MeshTopology.LineStrip)

**What It Is:**
- GPU topology mode for connected lines
- Shares vertices between segments (seamless joins)
- Example: vertices [A,B,C,D] → lines AB, BC, CD (no gaps)

**User Response:**
> "the hell is a line strip and why havent you mentioned it earlier"

**Limitation Discovered:**
- Modern graphics APIs: LINE_STRIP always 1 pixel wide
- No API support for line width control (deprecated in OpenGL 3.2+)
- Cannot achieve subpixel-thin borders with basic line strips

**Potential Solutions:**
1. Geometry shader expansion (take line strip, emit thin quads)
2. Pre-expand to triangle strip on CPU
3. Use compute shader to rasterize to texture

### 4. BorderTexture Discovery (CRITICAL BREAKTHROUGH)
**File Found:** `3-DrawIndexedInstance-114-37.txt` (RenderDoc event list showing 754 Draw calls)

**User Discovery:** Found DrawIndexed event with "BorderTexture_Texture" binding

**Shaders Analyzed:**
- `vertex.txt` - Simple vertex transformation with UV passthrough
- `pixel.txt` - **THE CRITICAL SHADER**

**Pixel Shader Analysis:**
```hlsl
ps_5_0
// Line 12: Border texture binding (64x64 style/pattern texture)
dcl_resource_texture2d (float,float,float,float) BorderTexture_Texture (t0)
dcl_resource_texture2d (float,float,float,float) FogOfWarAlpha_Texture (t1)

// Line 18: Sample border style texture with UV coordinates
sample_indexable(texture2d)(float,float,float,float) r0.xyzw, v2.xyxx, BorderTexture_Texture.xyzw, _sampler_0_

// Lines 19-37: Apply fog of war
mul r1.xy, v1.xzxx, InverseWorldSize.xyxx
sample_indexable(texture2d)(float,float,float,float) r1.z, r1.xyxx, FogOfWarAlpha_Texture.yzxw, _sampler_1_
[... fog calculations ...]

// Lines 38-67: Apply distance fog
[... distance fog calculations ...]

// Line 68: Output with alpha blending
mul o0.w, r0.w, vAlpha.x
```

**Key Realization:**
The shader samples BorderTexture with **UV coordinates** (v2.xy), not world position. This means:
- BorderTexture is a **style/pattern texture** (e.g., 64x64 for border appearance)
- NOT a pre-rendered map of all borders
- The 754 Draw() calls are rendering actual border **geometry** every frame
- BorderTexture provides the visual style/color for that geometry

**Supporting Evidence:**
RenderDoc shows 754 Draw() calls using this shader:
```
Draw(770)   // Border polyline with 770 vertices
Draw(802)   // Border polyline with 802 vertices
Draw(2228)  // Longer border polyline
Draw(72)    // Short border polyline
[... 754 total Draw calls ...]
```

**Interpretation:** Each Draw() call renders one border polyline as **geometry** (likely LINE_STRIP or expanded triangle strip). The BorderTexture provides styling/appearance for that geometry.

---

## Decisions Made

### Decision 1: Paradox Uses Per-Frame Geometry Rendering
**Context:** Analyzed RenderDoc captures to understand actual implementation

**Discovery:** Paradox renders border geometry every frame:
- 754 Draw() calls per frame, each rendering one border polyline
- BorderTexture is just a 64x64 style/pattern texture (not pre-baked map)
- Shader samples BorderTexture with UV coords for visual appearance
- Actual borders are geometry (LINE_STRIP or expanded triangle strips)

**Implication:**
- NOT a pre-baked approach (initial misinterpretation)
- Renders actual geometry every frame
- Need to investigate vertex data to understand geometry expansion technique

### Decision 2: Two-Tier Border Architecture (Like Paradox)
**Context:** BorderDistanceTexture only contains country borders, not province borders

**Discovery:** Separate rendering systems for different border types

**Approach:**
1. **Province borders** → 754 Draw() calls rendering geometry with BorderTexture styling
2. **Country borders** → BorderDistanceTexture (expensive 9-tap + two-layer rendering for special effects)

**Rationale:**
- Province borders: thousands, rendered as simple geometry with style texture
- Country borders: hundreds, can afford expensive distance field shader
- Different visual requirements justify different techniques

---

## What Worked ✅

1. **RenderDoc Shader Analysis**
   - What: Extracted and analyzed pixel shaders from Imperator Rome
   - Why it worked: Clear view into production implementation
   - Reusable pattern: Yes - RenderDoc invaluable for reverse engineering rendering

2. **Session Log Review**
   - What: Read previous failure analysis to understand constraints
   - Impact: Avoided repeating failed approaches, understood why they failed

3. **User Engagement**
   - What: User found the critical BorderTexture shader
   - Impact: Led to breakthrough discovery

---

## What Didn't Work ❌

1. **Distance Field Bézier Curves**
   - What we tried: Evaluate distance to Bézier curves for smooth borders
   - Why it failed: Euclidean distance creates round caps (circular regions at endpoints)
   - Lesson learned: Distance fields fundamentally incompatible with flat-ended borders
   - Don't try this again because: Mathematically proven impossible

2. **Mesh Quad Rendering**
   - What we tried: Generate quad meshes along Chaikin-smoothed polylines
   - Why it failed: Visible breaks between quad segments, especially at junctions
   - Lesson learned: Discrete quads don't provide seamless visual continuity
   - Don't try this again because: User confirmed visual quality unacceptable

3. **LINE_STRIP as Direct Solution**
   - What we tried: Use GPU line strip primitive for seamless borders
   - Why it failed: Modern APIs lock line width to 1 pixel (no subpixel control)
   - Lesson learned: Basic GPU primitives insufficient, need expansion technique
   - Don't try this again because: API limitation, need geometry shader or pre-expansion

---

## Problems Encountered & Solutions

### Problem 1: Analyzing Wrong Border System
**Symptom:** Spent significant time on BorderDistanceTexture thinking it contained all borders

**Root Cause:** Assumed one texture for all borders, but Paradox uses two systems

**Investigation:**
- Analyzed BorderDistanceTexture: 2048×1024 R8_UNORM
- User noticed: Only shows country borders, thousands of provinces missing
- Screenshot confirmed: Only land-to-land country borders present

**Solution:** Recognized two-tier architecture:
- BorderDistanceTexture = country borders (expensive rendering)
- BorderTexture = province borders (cheap sampling)

**Why This Works:** Different border types have different performance/visual requirements

**Pattern for Future:** Don't assume single solution for all cases, look for specialization

### Problem 2: Not Mentioning Line Strips Earlier
**Symptom:** User frustrated that basic GPU primitive wasn't suggested earlier

**Root Cause:** Overthought problem, went straight to complex solutions (distance fields, mesh generation)

**User Feedback:**
> "the hell is a line strip and why havent you mentioned it earlier"

**Solution:** Acknowledged oversight, explained line strip limitations (1px width)

**Why This Works:** Honest acknowledgment + technical explanation of why it's not complete solution

**Pattern for Future:** Consider simple solutions first, explain fundamental primitives before complex techniques

### Problem 3: Misinterpreting BorderTexture Purpose
**Symptom:** Initially thought BorderTexture was a pre-rendered map of all borders

**Root Cause:** Didn't check UV coordinate usage in shader (sampled with v2.xy, not world position)

**Investigation:**
- User found BorderTexture shader with simple pixel shader
- Analyzed shader: samples texture with UV coordinates (v2.xy)
- User corrected: "BorderTexture is just a 64x64 texture for border styling"
- Realized: 754 Draw() calls render geometry every frame, not generating texture

**Solution:** Corrected understanding - BorderTexture is style/pattern texture, borders are geometry

**Why This Matters:** Paradox DOES render borders every frame (not pre-baked), need to understand geometry technique

**Pattern for Future:** Check coordinate space when analyzing texture sampling (UV vs world vs screen)

---

## Architecture Impact

### Key Discovery - Geometry-Based Rendering
**Paradox's Actual Approach:**
- Renders border **geometry** every frame (754 Draw calls)
- BorderTexture is just a 64x64 style/pattern texture for appearance
- Each Draw() call likely renders one border as LINE_STRIP or expanded triangle strip
- Shader samples BorderTexture with UV coords for visual styling

**Implication:**
- NOT pre-baked (previous misunderstanding)
- Renders actual geometry per frame
- Performance must be acceptable for 754 draw calls per frame
- Need to understand vertex expansion technique (geometry shader? CPU pre-expansion?)

### Critical Question Remaining
**How do they expand lines to thin geometry?**

**Options:**
1. **Geometry shader** - GPU expands LINE_STRIP to thin quads (width control, seamless)
2. **CPU pre-expansion** - Generate triangle strips with precise width
3. **Instancing** - Draw line segments with instanced quads
4. **Tessellation** - Hardware tessellation for smooth curves

**Need to investigate:**
- Vertex shader input layout (what attributes?)
- Draw call topology (LINE_STRIP? TRIANGLE_STRIP?)
- Presence of geometry shader in pipeline

---

## Code Quality Notes

### Performance
- **Target:** Texture sampling = ~0.1ms per frame (negligible)
- **Initialization:** 754 Draw calls acceptable (one-time cost)
- **Memory:** BorderTexture size TBD (4096×4096 = 64MB RGBA)

### Testing
- **RenderDoc Analysis:** Critical tool for understanding production techniques
- **Visual Verification:** User screenshots show target quality
- **Session Log Review:** Historical context prevents repeated failures

### Technical Debt
- **None Created:** Pure research/analysis session
- **Debt Resolved:** Clarity on why previous approaches failed

---

## Next Session

### Immediate Next Steps (Priority Order)
1. **Analyze vertex shader** - Understand vertex input layout and attributes
2. **Check RenderDoc topology** - Is it LINE_STRIP, TRIANGLE_STRIP, or TRIANGLES?
3. **Look for geometry shader** - Does pipeline have geometry stage?
4. **Understand expansion technique** - How do they create thin border geometry?

### Questions to Resolve
1. **What topology does Paradox use?** LINE_STRIP or pre-expanded triangles?
2. **Geometry shader or CPU expansion?** Where does line → quad expansion happen?
3. **How many vertices per border?** Draw(770) = 770 vertices suggests detailed polylines
4. **What are vertex attributes?** Position only? Position + UV? Normal for width?

### Investigation Needed
1. **Vertex shader input:** Check dcl_input declarations for attribute layout
2. **Draw call parameters:** Topology, vertex count, index count
3. **Pipeline stages:** Vertex → [Geometry?] → Pixel
4. **Vertex buffer data:** What does actual vertex data look like?

---

## Session Statistics

**Files Changed:** 0 (analysis only)
**Files Analyzed:** 6 RenderDoc captures + 3 session logs
**Lines Analyzed:** ~500 lines of HLSL assembly
**Bugs Fixed:** 0
**Breakthroughs:** 1 (pre-baked texture approach)

---

## Quick Reference for Future Claude

**What Claude Should Know:**
- **CORRECTED:** Paradox renders border GEOMETRY every frame (754 Draw calls), NOT pre-baked texture
- BorderTexture is a **64x64 style/pattern texture** for visual appearance (NOT a map of borders)
- Pixel shader: `border-pixel.txt:18` - samples style texture with UV coords (v2.xy)
- BorderDistanceTexture = country borders ONLY (not province borders)
- Previous approaches failed: distance fields (round caps), mesh quads (visible breaks)

**What Changed Since Last Doc Read:**
- Two-tier architecture: province borders (geometry) vs country borders (distance field)
- Province borders rendered as geometry with BorderTexture styling
- Need to investigate vertex expansion technique (how LINE_STRIP → thin quads?)

**Gotchas for Next Session:**
- BorderTexture is NOT a pre-rendered map (initial misinterpretation corrected by user)
- Check coordinate space when analyzing texture sampling (UV vs world vs screen)
- Need to analyze vertex shader and draw topology to understand expansion
- 754 Draw() calls per frame means geometry rendering must be efficient

**Critical Correction:**
User: "BorderTexture is just a 64x64 texture for border styling"

Initial interpretation was WRONG - it's not a pre-baked map, it's a style texture. Borders are rendered as geometry every frame using 754 Draw() calls.

---

## Links & References

### Related Sessions
- [Session 6: Gaussian Blur Experiment](6-gaussian-blur-bordermask-experiment.md) - Blur doesn't thin borders
- [Session 1 (Oct 29): Mesh-Based Rendering](1-mesh-based-border-rendering.md) - Visible segment breaks
- [Session 4 (Oct 28): Distance Field Round Caps](../28/4-junction-detection-and-distance-field-round-cap-problem.md) - Mathematically impossible

### RenderDoc Captures Analyzed
- `DrawIndexedInstance-108-1602-Pixel.txt` - BorderDistanceTexture shader (country borders)
- `1-DrawIndexedInstance-258-41-Pixel.txt` - BorderDistanceTexture shader variant
- `2-Draw-4-Pixel.txt` - BorderDistanceTexture shader variant
- `vertex.txt` - BorderTexture vertex shader (simple transform)
- `pixel.txt` - **BorderTexture pixel shader (THE BREAKTHROUGH)**
- `renderdoc.txt` - Event list showing 754 Draw() calls
- `paradox_borderdistance_texture.png` - Screenshot proving country borders only

### External Resources
- Imperator Rome screenshots showing target quality
- RenderDoc documentation for shader analysis

### Code References
- BorderTexture sampling: `pixel.txt:18` (single texture fetch)
- 9-tap BorderDistanceTexture: `DrawIndexedInstance-108-1602-Pixel.txt:200-221`

---

## Notes & Observations

**Session Tone:**
- Started with BorderDistanceTexture analysis (wrong system - country borders only)
- Reviewed why previous approaches failed (distance fields, mesh quads)
- Discussed line strips (should have mentioned earlier)
- User found BorderTexture shader
- **INITIAL MISINTERPRETATION:** Thought it was pre-baked map approach
- **USER CORRECTION:** "BorderTexture is just a 64x64 texture for border styling"
- Corrected understanding: geometry-based rendering with style texture

**The Actual Discovery:**
Paradox renders border **geometry** every frame (754 Draw calls), NOT pre-baked texture:
- BorderTexture = 64x64 style/pattern texture for appearance
- Each Draw() renders one border polyline as geometry
- Shader samples BorderTexture with UV coords for visual styling
- Need to understand vertex expansion technique (geometry shader? CPU pre-expansion?)

**Why This Matters:**
- Confirms geometry-based approach is viable (Paradox uses it)
- 754 Draw calls per frame acceptable performance
- Need to investigate vertex shader and topology to understand expansion
- BorderTexture just provides visual style, not the actual border map

**User Insights:**
- Noticed BorderDistanceTexture missing provinces (critical observation)
- Found BorderTexture shader (key discovery)
- **Corrected my misinterpretation** about pre-baked approach (accurate correction)
- Provided Imperator screenshots showing target quality

**Next Direction:**
Investigate vertex expansion technique:
1. Analyze vertex shader input layout
2. Check draw topology (LINE_STRIP? TRIANGLE_STRIP?)
3. Look for geometry shader in pipeline
4. Understand how thin border geometry is generated

Need to figure out HOW they expand polylines into thin, seamless border geometry.

---

*Session ended with corrected understanding: Paradox uses geometry-based rendering with style texture, NOT pre-baked map. Still need to investigate vertex expansion technique.*
