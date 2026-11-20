# Paradox-Style Water, Smooth Coasts, Smooth Country Colors
**Date**: 2026-11-20
**Session**: 2
**Status**: ✅ Complete
**Priority**: Medium

---

## Session Goal

**Primary Objective:**
- Bring a game's map look closer to HOI4: water surface, coastlines, country color edges

**Success Criteria:**
- Water reads like HOI4's (waves, sky reflection)
- No pixel steps on coasts or between country colors

All shader work lived in the GAME layer (map mode / terrain HLSL plus the data converter). No
engine changes; this log records the techniques for reuse.

---

## What We Did

### 1. Water: port of HOI4 `pdxwater.shader`
HOI4's water (gfx/FX/pdxwater.shader + standardfuncsgfx.fxh) is:
- **LEAN-mapped waves**: `lean1/lean2` textures, 4 layers at scales 1000/700/534/300 per map,
  scrolling in different directions, blended with `BlendLEAN` (t = 0.5). B = mean slope,
  M = second moments; normal = normalize(B.x, 1, B.y).
- **Wave mask** (`fow_rgb_waterspec_a` alpha): flattens normals and B/M near coasts
  (`n.y += 1 - mask; n.xz *= mask; B *= mask; M *= mask²`).
- **Refraction** of the underwater pass (sea floor + depth tint), offset by the wave normal.
- **Sky cubemap reflection** (`reflection.dds`) with fresnel `bias + (1-bias) * pow(1 - 0.5*N·V, 10)`,
  bias 0.5, so the sea is about half sky even looking straight down. This gives HOI4's grey-blue sea.
- 20% darkening, lit with the wave normal, plus a LEAN sun glint (Gaussian slope distribution).
- Ice from seasonal snow (not ported: no seasons yet).

Port notes:
- The visual style loads only 2D textures, so the cubemap was baked to a lat-long image in the
  converter (D3D face convention, uncompressed 32-bit DDS walked by hand; Pillow reads only face 0).
- `MAP_POW2_X/Y` (wave UV scale) is set by the Clausewitz engine at runtime, not in the files;
  wave UVs were made square in map pixels with a tunable scale.
- The LEAN glint peaks in the hundreds to thousands near the mirror direction (HOI4 tames it
  inside its lighting model). Mapped through `1 - exp(-0.02 * spec)`.

### 2. Coast: heightmap isoline, not province pixels
HOI4 has no coastline geometry. The water plane's alpha is
`1 - saturate((h / 9.57 - 0.954) * 25)` on a 9-tap blurred heightmap, so the coast is a smooth
height contour (fully water below ~9.13, land at ~9.51; sea level 9.5).

Port: the converter bakes a coast height field (blurred heightmap, 8.5-10.5 range) into a spare
channel of the terrain index, forced to full land/water more than 2 px from the province coast
(inland depressions below sea level stay land), plus a "nearest land tile" channel so the smooth
coast has terrain to draw where it crosses into sea pixels. Shader land coverage = HOI4 shore
alpha of the bilinear field. Measured agreement: the heightmap coast is within ~1 px of the
province coast for 99%+ of pixels.

### 3. Country colors: smooth edges and gradients
HOI4 "gradient borders": a CPU-built texture with country color + distance-to-border alpha,
9-tap blurred; color stops at a cutoff isoline, leaving a thin colorless sliver hidden under the
border lines.

Prototyped three options (A: bilinear owner colors, B: HOI4 distance fade, C: blurred owner
coverage). B left a visible halo; C chosen:
- Per fragment, collect up to 4 distinct owners in a 4x4 pixel window with tent weights
  (~1.9 px radius); each owner's summed weight is its blend share.
- Color = share-weighted blend (premultiplied; unowned = alpha 0, so colors fade at coasts).
- Softness parameter: shares raised to a power (1..8) before normalizing; 1 = gradient between
  countries over ~3-4 map px, higher = towards a sharp line.

---

## What Didn't Work ❌

1. **Two-owner blend (nearest owner vs "first other owner")**
   - Looked fine on 2-country borders, clashed at 3+ country junctions: the third owner was
     ignored and the "other" owner flipped per pixel.
   - Fix: track all owners in the window (up to 4) and blend by share.

2. **Anti-aliased cut at coverage 0.5**
   - Smooth, but "too solid": instant switch from color A to B. Users expect the HOI4-like
     gradient between neighbouring country colors.

---

## Quick Reference for Future Claude

**Gotchas:**
- Water is evaluated for every map pixel (derivative-based samples can't be branched), about
  12 texture reads; coast and country edges add 16 owner loads in political mode.
- The gradient between countries is limited to ~4 map px by the 4x4 window; wider needs the
  engine border distance field (32 px) instead.
- Province highlight fill is still pixel based (the mesh outline is the default).
